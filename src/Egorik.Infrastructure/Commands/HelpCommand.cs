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
            "Я умею открывать приложения и сайты, искать в интернете, считать, " +
            "сообщать дату и время, управлять медиа, открывать браузер по умолчанию, " +
            "работать со Steam и Яндекс Музыкой через безопасные действия и " +
            "создавать новые заметки Word, искать в разрешённых книгах Excel и " +
            "выполнять пользовательские сценарии. Декларативный плагин Dota 2 " +
            "после включения понимает команды вроде «пикни Пуджа».",
            Descriptor.Id));
}
