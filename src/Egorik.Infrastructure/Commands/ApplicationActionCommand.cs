using Egorik.Core.Applications;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Infrastructure.Platform;

namespace Egorik.Infrastructure.Commands;

public sealed class ApplicationActionCommand(
    IApplicationActionRegistry registry,
    IAssistantSettingsService settings)
    : IAssistantCommand
{
    private static readonly IReadOnlyDictionary<string, string> YandexMusicActions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["пауза"] = "play_pause",
            ["поставь на паузу"] = "play_pause",
            ["продолжи"] = "play_pause",
            ["следующий трек"] = "next",
            ["переключи трек"] = "next",
            ["переключи на следующий трек"] = "next",
            ["включи следующий трек"] = "next",
            ["следующая песня"] = "next",
            ["предыдущий трек"] = "previous",
            ["переключи на предыдущий трек"] = "previous",
            ["включи предыдущий трек"] = "previous",
            ["предыдущая песня"] = "previous"
        };

    private static readonly string[] StartMyWavePhrases =
    [
        "включи мою волну",
        "запусти мою волну",
        "включи волну",
        "запусти волну"
    ];

    public CommandDescriptor Descriptor { get; } = new(
        "application.action",
        "Действие в приложении",
        "Выполняет зарегистрированное безопасное действие через адаптер приложения.");

    public ValueTask<CommandMatch?> MatchAsync(
        string normalizedText,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryMatchYandexMusicAction(normalizedText, out var mediaAction))
        {
            return Match("yandex_music", mediaAction, 0.99);
        }

        if (TryMatchMyWaveAction(normalizedText))
        {
            return Match("yandex_music", "start_my_wave", 0.99);
        }

        var libraryTarget = CommandText.AfterPrefix(
            normalizedText,
            "открой библиотеку в",
            "открой библиотеку");
        if (!string.IsNullOrWhiteSpace(libraryTarget) &&
            MatchesApplicationAlias("steam", libraryTarget))
        {
            return Match("steam", "open_library", 0.99);
        }

        var note = CommandText.AfterPrefix(
            normalizedText,
            "запиши в word",
            "запиши в ворд",
            "создай заметку в word",
            "создай заметку в ворд");
        if (!string.IsNullOrWhiteSpace(note))
        {
            return Match("microsoft_word", "create_note", 0.99, ("text", note));
        }

        var workbookSearch = MatchWorkbookSearch(normalizedText);
        if (workbookSearch is not null)
        {
            return workbookSearch.Value;
        }

        var steamGame = ExtractArgumentBeforeApplication(
            normalizedText,
            ["запусти игру ", "запусти "],
            "steam");
        if (steamGame is not null)
        {
            return Match("steam", "launch_game", 0.99, ("game", steamGame));
        }

        steamGame = ExtractArgumentBeforeApplication(
            normalizedText,
            ["найди игру ", "найди "],
            "steam");
        if (steamGame is not null)
        {
            return Match("steam", "search_game", 0.99, ("game", steamGame));
        }

        steamGame = CommandText.AfterPrefix(
            normalizedText,
            "найди игру в steam",
            "найди игру в стиме");
        if (!string.IsNullOrWhiteSpace(steamGame))
        {
            return Match("steam", "search_game", 0.99, ("game", steamGame));
        }

        var musicQuery = ExtractArgumentBeforeApplication(
            normalizedText,
            ["найди трек ", "найди песню ", "найди "],
            "yandex_music");
        if (musicQuery is not null)
        {
            return Match("yandex_music", "search", 0.99, ("query", musicQuery));
        }

        var target = CommandText.AfterPrefix(normalizedText, "открой", "запусти");
        if (!string.IsNullOrWhiteSpace(target))
        {
            if (YandexMusicAliases().Any(alias => KnownEntityMatcher.IsMatch(alias, target)))
            {
                return Match("yandex_music", "open", 0.98);
            }

            var application = registry.AvailableApplications.FirstOrDefault(candidate =>
                candidate.Actions.Any(action =>
                    action.Id == "open" && action.Risk == CommandRisk.Low) &&
                candidate.Aliases.Any(alias =>
                    KnownEntityMatcher.IsMatch(alias, target)));

            if (application is not null)
            {
                return Match(application.Id, "open", 0.96);
            }
        }

        return ValueTask.FromResult<CommandMatch?>(null);
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CommandMatch match,
        CancellationToken cancellationToken = default)
    {
        var applicationId = match.Argument("application");
        var actionId = match.Argument("action");
        if (applicationId is null || actionId is null)
        {
            return CommandResult.Failure(
                "Не удалось определить действие приложения.",
                Descriptor.Id);
        }

        var arguments = match.Arguments
            .Where(argument =>
                !string.Equals(argument.Key, "application", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(argument.Key, "action", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(argument => argument.Key, argument => argument.Value, StringComparer.Ordinal);

        if (applicationId == "microsoft_word" &&
            actionId == "create_note" &&
            arguments.TryGetValue("text", out var normalizedNote))
        {
            arguments["text"] = ExtractOriginalNote(context.OriginalText) ?? normalizedNote;
        }

        return await registry.ExecuteAsync(
            new ApplicationActionRequest(applicationId, actionId, arguments),
            CommandRisk.Low,
            cancellationToken);
    }

    private static ValueTask<CommandMatch?> Match(
        string application,
        string action,
        double confidence,
        params (string Name, string Value)[] arguments)
    {
        var values = new List<(string Name, string Value)>
        {
            ("application", application),
            ("action", action)
        };
        values.AddRange(arguments);
        return ValueTask.FromResult<CommandMatch?>(CommandMatch.Create(confidence, values.ToArray()));
    }

    private string? ExtractArgumentBeforeApplication(
        string text,
        IReadOnlyList<string> prefixes,
        string applicationId)
    {
        var marker = text.LastIndexOf(" в ", StringComparison.Ordinal);
        if (marker < 0)
        {
            return null;
        }

        var application = text[(marker + 3)..].Trim();
        if (!MatchesApplicationAlias(applicationId, application))
        {
            return null;
        }

        var command = text[..marker].TrimEnd();
        foreach (var prefix in prefixes.OrderByDescending(value => value.Length))
        {
            if (!command.StartsWith(prefix, StringComparison.Ordinal) ||
                command.Length <= prefix.Length)
            {
                continue;
            }

            var value = command[prefix.Length..].Trim();
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    private bool TryMatchYandexMusicAction(string text, out string action)
    {
        var marker = text.LastIndexOf(" в ", StringComparison.Ordinal);
        if (marker >= 0)
        {
            var command = text[..marker];
            var application = text[(marker + 3)..];
            if (YandexMusicActions.TryGetValue(command, out action!) &&
                YandexMusicAliases().Any(alias =>
                    KnownEntityMatcher.IsMatch(alias, application)))
            {
                return true;
            }
        }

        var stopTarget = CommandText.AfterPrefix(text, "останови");
        if (!string.IsNullOrWhiteSpace(stopTarget) &&
            YandexMusicAliases().Any(alias => KnownEntityMatcher.IsMatch(alias, stopTarget)))
        {
            action = "stop";
            return true;
        }

        action = string.Empty;
        return false;
    }

    private bool TryMatchMyWaveAction(string text)
    {
        if (StartMyWavePhrases.Contains(text, StringComparer.Ordinal))
        {
            return true;
        }

        var marker = text.LastIndexOf(" в ", StringComparison.Ordinal);
        return marker >= 0 &&
               StartMyWavePhrases.Contains(text[..marker], StringComparer.Ordinal) &&
               YandexMusicAliases().Any(alias =>
                   KnownEntityMatcher.IsMatch(alias, text[(marker + 3)..]));
    }

    private bool MatchesApplicationAlias(string applicationId, string spokenValue) =>
        ApplicationAliases(applicationId).Any(alias =>
            KnownEntityMatcher.IsMatch(alias, spokenValue));

    private IEnumerable<string> ApplicationAliases(string applicationId)
    {
        var aliases = registry.AvailableApplications
            .FirstOrDefault(application => application.Id == applicationId)
            ?.Aliases ?? [];
        return applicationId == "yandex_music"
            ? aliases.Concat(YandexMusicConfiguredAliases())
            : aliases;
    }

    private IEnumerable<string> YandexMusicAliases() =>
        ApplicationAliases("yandex_music");

    private IEnumerable<string> YandexMusicConfiguredAliases() =>
        settings.Current.Applications
            .Where(application =>
                string.Equals(
                    Path.GetFileName(application.Value),
                    DesktopYandexMusicController.ExecutableName,
                    StringComparison.OrdinalIgnoreCase))
            .Select(application => application.Key);

    private ValueTask<CommandMatch?>? MatchWorkbookSearch(string text)
    {
        const string leadingPrefix = "найди в таблице ";
        if (text.StartsWith(leadingPrefix, StringComparison.Ordinal))
        {
            var tail = text[leadingPrefix.Length..];
            foreach (var document in OrderedDocuments())
            {
                var alias = TextNormalizer.Normalize(document);
                if (tail.StartsWith(alias + " ", StringComparison.Ordinal))
                {
                    var query = tail[(alias.Length + 1)..].Trim();
                    if (query.Length > 0)
                    {
                        return Match(
                            "microsoft_excel",
                            "search_workbook",
                            0.99,
                            ("document", document),
                            ("query", query));
                    }
                }
            }
        }

        const string searchPrefix = "найди ";
        if (!text.StartsWith(searchPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var document in OrderedDocuments())
        {
            var suffix = " в таблице " + TextNormalizer.Normalize(document);
            if (!text.EndsWith(suffix, StringComparison.Ordinal) ||
                text.Length <= searchPrefix.Length + suffix.Length)
            {
                continue;
            }

            var query = text[searchPrefix.Length..^suffix.Length].Trim();
            if (query.Length > 0)
            {
                return Match(
                    "microsoft_excel",
                    "search_workbook",
                    0.99,
                    ("document", document),
                    ("query", query));
            }
        }

        return null;
    }

    private IEnumerable<string> OrderedDocuments() =>
        settings.Current.Documents.Keys
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .OrderByDescending(alias => TextNormalizer.Normalize(alias).Length);

    private static string? ExtractOriginalNote(string originalText)
    {
        string[] prefixes =
        [
            "создай заметку в word",
            "создай заметку в ворд",
            "запиши в word",
            "запиши в ворд"
        ];

        foreach (var prefix in prefixes.OrderByDescending(value => value.Length))
        {
            var index = originalText.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var value = originalText[(index + prefix.Length)..]
                .Trim(' ', '\t', ',', ':', '-', '—');
            return value.Length == 0 ? null : value;
        }

        return null;
    }
}
