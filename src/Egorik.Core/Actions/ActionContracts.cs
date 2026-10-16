using Egorik.Core.Commands;

namespace Egorik.Core.Actions;

public enum ActionParameterKind { Text, Choice, Integer, Boolean }

public sealed record ActionParameterDescriptor(
    string Name,
    string Title,
    ActionParameterKind Kind = ActionParameterKind.Text,
    bool IsRequired = true,
    int MaxLength = 300,
    IReadOnlyList<string>? AllowedValues = null,
    int Minimum = 0,
    int Maximum = 100,
    IReadOnlyDictionary<string, string>? ValueTitles = null);

public sealed record AssistantActionDescriptor(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<ActionParameterDescriptor> Parameters,
    CommandRisk Risk = CommandRisk.Low);

public sealed record ActionProviderManifest(string Id, string Name, int ApiVersion = 1);

public sealed record ActionInvocation(string ActionId, IReadOnlyDictionary<string, string> Arguments);

public sealed record ActionValidationResult(bool IsValid, string? Error = null);

// Providers are explicitly registered by the host. This contract grants no system
// access and does not replace sandboxing or permission approval for external code.
public interface IAssistantActionProvider
{
    ActionProviderManifest Manifest { get; }

    IReadOnlyList<AssistantActionDescriptor> Actions { get; }

    ValueTask<CommandResult> ExecuteAsync(ActionInvocation invocation, CancellationToken cancellationToken = default);
}

public interface IAssistantActionRegistry
{
    IReadOnlyList<AssistantActionDescriptor> AvailableActions { get; }

    ActionValidationResult Validate(ActionInvocation invocation);

    ValueTask<CommandResult> ExecuteAsync(ActionInvocation invocation, CancellationToken cancellationToken = default);
}
