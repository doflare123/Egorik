namespace Egorik.Core.Commands;

public sealed record CommandDescriptor(
    string Id,
    string Title,
    string Description,
    CommandRisk Risk = CommandRisk.Low);

public enum CommandRisk
{
    Low,
    Medium,
    High
}

public sealed record CommandMatch(
    double Confidence,
    IReadOnlyDictionary<string, string> Arguments)
{
    public string? Argument(string name) =>
        Arguments.TryGetValue(name, out var value) ? value : null;

    public static CommandMatch Create(double confidence, params (string Name, string Value)[] arguments) =>
        new(
            confidence,
            arguments.ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase));
}

public enum ClientAction
{
    None,
    OpenSettings,
    OpenScenarios
}

public sealed record CommandResult(
    bool IsSuccess,
    string Message,
    string? CommandId = null,
    ClientAction ClientAction = ClientAction.None)
{
    public static CommandResult Success(
        string message,
        string? commandId = null,
        ClientAction clientAction = ClientAction.None) =>
        new(true, message, commandId, clientAction);

    public static CommandResult Failure(string message, string? commandId = null) =>
        new(false, message, commandId);
}

public sealed record CommandContext(
    string OriginalText,
    string NormalizedText,
    int Depth = 0);

public interface IAssistantCommand
{
    CommandDescriptor Descriptor { get; }

    ValueTask<CommandMatch?> MatchAsync(
        string normalizedText,
        CancellationToken cancellationToken = default);

    ValueTask<CommandResult> ExecuteAsync(
        CommandContext context,
        CommandMatch match,
        CancellationToken cancellationToken = default);
}

public interface ICommandRouter
{
    IReadOnlyList<CommandDescriptor> AvailableCommands { get; }

    ValueTask<CommandResult?> RouteAsync(
        CommandContext context,
        CancellationToken cancellationToken = default);
}
