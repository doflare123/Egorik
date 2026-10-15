using System.Globalization;
using Egorik.Core.Ai;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Platform;
using Egorik.Infrastructure.Commands;

namespace Egorik.Infrastructure.Ai;

public sealed class ApplicationOpenAiTool(
    IAssistantSettingsService settings,
    IApplicationLauncher launcher) : IAiTool
{
    public AiToolDescriptor Descriptor => new(
        "application_open",
        "Открывает только приложение из пользовательского списка разрешённых приложений.",
        [
            new AiToolParameterDescriptor(
                "application",
                "Точное имя приложения из разрешённого списка.",
                AllowedValues: settings.Current.Applications.Keys
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray())
        ]);

    public async ValueTask<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("application", out var alias) ||
            !settings.Current.Applications.TryGetValue(alias, out var target))
        {
            return CommandResult.Failure(
                "Приложение отсутствует в разрешённом списке.",
                Descriptor.Id);
        }

        var result = await launcher.LaunchAsync(target, cancellationToken);
        return result.IsSuccess
            ? CommandResult.Success($"Открываю {alias}.", Descriptor.Id)
            : CommandResult.Failure(result.Message, Descriptor.Id);
    }
}

public sealed class WebsiteOpenAiTool(
    IAssistantSettingsService settings,
    IWebBrowserService browser) : IAiTool
{
    public AiToolDescriptor Descriptor => new(
        "website_open",
        "Открывает только сайт из пользовательского списка разрешённых сайтов.",
        [
            new AiToolParameterDescriptor(
                "site",
                "Точное имя сайта из разрешённого списка.",
                AllowedValues: settings.Current.Sites.Keys
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray())
        ]);

    public async ValueTask<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("site", out var alias) ||
            !settings.Current.Sites.TryGetValue(alias, out var value))
        {
            return CommandResult.Failure(
                "Сайт отсутствует в разрешённом списке.",
                Descriptor.Id);
        }

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return CommandResult.Failure(
                "В настройках сайта указан некорректный HTTP/HTTPS-адрес.",
                Descriptor.Id);
        }

        var result = await browser.OpenUrlAsync(uri, cancellationToken);
        return result.IsSuccess
            ? CommandResult.Success($"Открываю {alias}.", Descriptor.Id)
            : CommandResult.Failure(result.Message, Descriptor.Id);
    }
}

public sealed class WebSearchAiTool(
    IAssistantSettingsService settings,
    IWebBrowserService browser) : IAiTool
{
    public AiToolDescriptor Descriptor { get; } = new(
        "web_search",
        "Открывает в браузере результаты поиска по безопасно закодированному текстовому запросу.",
        [
            new AiToolParameterDescriptor(
                "query",
                "Текст поискового запроса без URL и без инструкций для системы.",
                MaxLength: 300)
        ]);

    public async ValueTask<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("query", out var query))
        {
            return CommandResult.Failure("Поисковый запрос не указан.", Descriptor.Id);
        }

        query = query.Trim();
        var result = await browser.SearchAsync(
            query,
            settings.Current.SearchProvider,
            cancellationToken);

        return result.IsSuccess
            ? CommandResult.Success($"Ищу: {query}.", Descriptor.Id)
            : CommandResult.Failure(result.Message, Descriptor.Id);
    }
}

public sealed class MediaControlAiTool(IMediaControlService mediaControl) : IAiTool
{
    private static readonly string[] AllowedActions = Enum.GetNames<MediaAction>();

    public AiToolDescriptor Descriptor { get; } = new(
        "media_control",
        "Управляет текущим медиаплеером и общей громкостью колонок. " +
        "Для громкости музыки или Яндекс Музыки используй application_action с yandex_music.volume_up, volume_down или set_volume; этот инструмент меняет все колонки.",
        [
            new AiToolParameterDescriptor(
                "action",
                "Одно из разрешённых действий с медиа.",
                AllowedValues: AllowedActions)
        ]);

    public async ValueTask<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("action", out var value) ||
            !Enum.TryParse<MediaAction>(value, true, out var action))
        {
            return CommandResult.Failure("Неизвестное действие с медиа.", Descriptor.Id);
        }

        var result = await mediaControl.ExecuteAsync(action, cancellationToken);
        return result.IsSuccess
            ? CommandResult.Success(result.Message, Descriptor.Id)
            : CommandResult.Failure(result.Message, Descriptor.Id);
    }
}

public sealed class CalculatorAiTool : IAiTool
{
    public AiToolDescriptor Descriptor { get; } = new(
        "calculator",
        "Вычисляет локально только арифметическое выражение с числами, скобками и операторами +, -, *, /.",
        [
            new AiToolParameterDescriptor(
                "expression",
                "Арифметическое выражение, например: (42 + 8) / 5.",
                MaxLength: 160)
        ]);

    public ValueTask<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!arguments.TryGetValue("expression", out var expression) ||
            !ExpressionParser.TryEvaluate(expression.Replace(',', '.'), out var value))
        {
            return ValueTask.FromResult(
                CommandResult.Failure(
                    "Не получилось вычислить выражение.",
                    Descriptor.Id));
        }

        return ValueTask.FromResult(CommandResult.Success(
            $"Получается {value.ToString("0.##########", CultureInfo.GetCultureInfo("ru-RU"))}.",
            Descriptor.Id));
    }
}

public sealed class DateTimeAiTool(IClock clock) : IAiTool
{
    private static readonly string[] AllowedKinds = ["time", "date"];

    public AiToolDescriptor Descriptor { get; } = new(
        "date_time",
        "Возвращает локальное время или дату компьютера.",
        [
            new AiToolParameterDescriptor(
                "kind",
                "Что вернуть: time — время, date — дату.",
                AllowedValues: AllowedKinds)
        ]);

    public ValueTask<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        var now = clock.Now;
        var message = arguments.TryGetValue("kind", out var kind) &&
                      string.Equals(kind, "time", StringComparison.OrdinalIgnoreCase)
            ? $"Сейчас {now.ToString("HH:mm", culture)}."
            : $"Сегодня {now.ToString("d MMMM yyyy", culture)} года.";

        return ValueTask.FromResult(CommandResult.Success(message, Descriptor.Id));
    }
}

public sealed class ClientNavigateAiTool : IAiTool
{
    private static readonly string[] AllowedSections = ["settings", "scenarios"];

    public AiToolDescriptor Descriptor { get; } = new(
        "client_navigate",
        "Открывает безопасный раздел интерфейса самого ассистента.",
        [
            new AiToolParameterDescriptor(
                "section",
                "Раздел: settings — настройки, scenarios — сценарии.",
                AllowedValues: AllowedSections)
        ]);

    public ValueTask<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!arguments.TryGetValue("section", out var section))
        {
            return ValueTask.FromResult(
                CommandResult.Failure("Раздел не указан.", Descriptor.Id));
        }

        var isSettings = string.Equals(
            section,
            "settings",
            StringComparison.OrdinalIgnoreCase);
        var action = isSettings
            ? ClientAction.OpenSettings
            : ClientAction.OpenScenarios;
        var message = isSettings
            ? "Открываю настройки."
            : "Открываю сценарии.";

        return ValueTask.FromResult(
            CommandResult.Success(message, Descriptor.Id, action));
    }
}

public sealed class AssistantReplyAiTool : IAiTool
{
    public AiToolDescriptor Descriptor { get; } = new(
        "assistant_reply",
        "Возвращает пользователю короткий текст без выполнения действий в системе. Используется, если действие невозможно или нужен обычный ответ.",
        [
            new AiToolParameterDescriptor(
                "message",
                "Короткий ответ пользователю на русском языке.",
                MaxLength: 500)
        ]);

    public ValueTask<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            arguments.TryGetValue("message", out var message)
                ? CommandResult.Success(message.Trim(), Descriptor.Id)
                : CommandResult.Failure("ИИ не передал текст ответа.", Descriptor.Id));
    }
}
