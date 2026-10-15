using System.Text.RegularExpressions;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Commands;

public sealed partial class OpenSiteCommand(
    IAssistantSettingsService settings,
    IWebBrowserService browser) : IAssistantCommand
{
    public CommandDescriptor Descriptor { get; } = new(
        "website.open",
        "Открыть сайт",
        "Открывает сайт по сохранённому имени или адресу.");

    public ValueTask<CommandMatch?> MatchAsync(
        string normalizedText,
        CancellationToken cancellationToken = default)
    {
        var explicitTarget = CommandText.AfterPrefix(
            normalizedText,
            "открой сайт",
            "перейди на сайт",
            "открой");

        if (explicitTarget is null || explicitTarget.Length == 0)
        {
            return ValueTask.FromResult<CommandMatch?>(null);
        }

        var site = KnownEntityMatcher.FindUniqueMatch(settings.Current.Sites.Keys, explicitTarget);

        if (site is not null)
        {
            return ValueTask.FromResult<CommandMatch?>(
                CommandMatch.Create(0.93, ("site", site)));
        }

        return ValueTask.FromResult(LooksLikeDomain().IsMatch(explicitTarget)
            ? CommandMatch.Create(0.91, ("url", explicitTarget))
            : null);
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CommandMatch match,
        CancellationToken cancellationToken = default)
    {
        string? value = null;
        var site = match.Argument("site");

        if (site is not null)
        {
            settings.Current.Sites.TryGetValue(site, out value);
        }

        value ??= match.Argument("url");
        if (string.IsNullOrWhiteSpace(value))
        {
            return CommandResult.Failure("Адрес сайта не найден.", Descriptor.Id);
        }

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return CommandResult.Failure("Некорректный адрес сайта.", Descriptor.Id);
        }

        var result = await browser.OpenUrlAsync(uri, cancellationToken);
        return result.IsSuccess
            ? CommandResult.Success($"Открываю {site ?? uri.Host}.", Descriptor.Id)
            : CommandResult.Failure(result.Message, Descriptor.Id);
    }

    [GeneratedRegex(
        @"^(?:https?://)?(?:[a-z0-9-]+\.)+[a-z]{2,}(?:/.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LooksLikeDomain();
}
