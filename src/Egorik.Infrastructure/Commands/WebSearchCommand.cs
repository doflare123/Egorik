using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Commands;

public sealed class WebSearchCommand(
    IAssistantSettingsService settings,
    IWebBrowserService browser) : IAssistantCommand
{
    public CommandDescriptor Descriptor { get; } = new(
        "web.search",
        "Поиск в интернете",
        "Открывает результаты интернет-поиска.");

    public ValueTask<CommandMatch?> MatchAsync(
        string normalizedText,
        CancellationToken cancellationToken = default)
    {
        var query = CommandText.AfterPrefix(
            normalizedText,
            "найди в интернете",
            "поищи в интернете",
            "найди",
            "поищи",
            "поиск");

        return ValueTask.FromResult(query is { Length: > 0 }
            ? CommandMatch.Create(0.9, ("query", query))
            : null);
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CommandMatch match,
        CancellationToken cancellationToken = default)
    {
        var query = match.Argument("query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return CommandResult.Failure("Что нужно найти?", Descriptor.Id);
        }

        var result = await browser.SearchAsync(
            query,
            settings.Current.SearchProvider,
            cancellationToken);

        return result.IsSuccess
            ? CommandResult.Success($"Ищу: {query}.", Descriptor.Id)
            : CommandResult.Failure(result.Message, Descriptor.Id);
    }
}
