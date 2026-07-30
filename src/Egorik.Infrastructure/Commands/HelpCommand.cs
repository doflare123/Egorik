using Egorik.Core.Commands;

namespace Egorik.Infrastructure.Commands;

public sealed class HelpCommand : IAssistantCommand
{
    private static readonly string[] Phrases =
    [
        "что ты умеешь",
        "покажи команды",
        "список команд",
        "помощь"
    ];

    public CommandDescriptor Descriptor { get; } = new(
        "assistant.help",
        "Помощь",
        "Показывает примеры доступных команд.");

    public ValueTask<CommandMatch?> MatchAsync(
        string normalizedText,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Phrases.Contains(normalizedText, StringComparer.Ordinal)
            ? CommandMatch.Create(1)
            : null);

    public ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CommandMatch match,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(CommandResult.Success(
            "Я умею открывать приложения и сайты, искать в интернете, считать, сообщать дату и время, управлять медиа и выполнять пользовательские сценарии.",
            Descriptor.Id));
}
