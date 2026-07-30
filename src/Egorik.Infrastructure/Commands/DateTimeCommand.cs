using System.Globalization;
using Egorik.Core.Commands;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Commands;

public sealed class DateTimeCommand(IClock clock) : IAssistantCommand
{
    private static readonly string[] TimePhrases = ["который час", "сколько времени", "текущее время"];
    private static readonly string[] DatePhrases = ["какое сегодня число", "какая сегодня дата", "сегодняшняя дата"];

    public CommandDescriptor Descriptor { get; } = new(
        "system.datetime",
        "Дата и время",
        "Сообщает локальную дату или время.");

    public ValueTask<CommandMatch?> MatchAsync(
        string normalizedText,
        CancellationToken cancellationToken = default)
    {
        if (TimePhrases.Contains(normalizedText, StringComparer.Ordinal))
        {
            return ValueTask.FromResult<CommandMatch?>(
                CommandMatch.Create(0.98, ("kind", "time")));
        }

        return ValueTask.FromResult(DatePhrases.Contains(normalizedText, StringComparer.Ordinal)
            ? CommandMatch.Create(0.98, ("kind", "date"))
            : null);
    }

    public ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CommandMatch match,
        CancellationToken cancellationToken = default)
    {
        var now = clock.Now;
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        var message = match.Argument("kind") == "time"
            ? $"Сейчас {now.ToString("HH:mm", culture)}."
            : $"Сегодня {now.ToString("d MMMM yyyy", culture)} года.";

        return ValueTask.FromResult(CommandResult.Success(message, Descriptor.Id));
    }
}
