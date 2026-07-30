using Egorik.Core.Commands;

namespace Egorik.Infrastructure.Commands;

public sealed class NavigationCommand : IAssistantCommand
{
    private static readonly IReadOnlyDictionary<string, ClientAction> Destinations =
        new Dictionary<string, ClientAction>(StringComparer.Ordinal)
        {
            ["открой настройки"] = ClientAction.OpenSettings,
            ["покажи настройки"] = ClientAction.OpenSettings,
            ["открой сценарии"] = ClientAction.OpenScenarios,
            ["покажи сценарии"] = ClientAction.OpenScenarios
        };

    public CommandDescriptor Descriptor { get; } = new(
        "client.navigate",
        "Открыть раздел",
        "Открывает настройки или редактор сценариев.");

    public ValueTask<CommandMatch?> MatchAsync(
        string normalizedText,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Destinations.TryGetValue(normalizedText, out var action)
            ? CommandMatch.Create(0.99, ("destination", action.ToString()))
            : null);
    }

    public ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CommandMatch match,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<ClientAction>(match.Argument("destination"), out var action))
        {
            return ValueTask.FromResult(
                CommandResult.Failure("Раздел не найден.", Descriptor.Id));
        }

        var message = action == ClientAction.OpenSettings
            ? "Открываю настройки."
            : "Открываю сценарии.";

        return ValueTask.FromResult(
            CommandResult.Success(message, Descriptor.Id, action));
    }
}
