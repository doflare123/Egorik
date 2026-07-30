using Egorik.Core.Commands;

namespace Egorik.Core.Ai;

public sealed record AiToolParameterDescriptor(
    string Name,
    string Description,
    bool IsRequired = true,
    int MaxLength = 300,
    IReadOnlyList<string>? AllowedValues = null);

public sealed record AiToolDescriptor(
    string Id,
    string Description,
    IReadOnlyList<AiToolParameterDescriptor> Parameters,
    CommandRisk Risk = CommandRisk.Low);

public sealed record PlannedToolCall(
    string ToolId,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record AiPlan(IReadOnlyList<PlannedToolCall> Steps);

public sealed record AiPlanValidationResult(bool IsValid, string? Error)
{
    public static AiPlanValidationResult Success() => new(true, null);

    public static AiPlanValidationResult Failure(string error) => new(false, error);
}

public interface IAiTool
{
    AiToolDescriptor Descriptor { get; }

    ValueTask<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default);
}

public interface IAiToolExecutor
{
    IReadOnlyList<AiToolDescriptor> AvailableTools { get; }

    AiPlanValidationResult Validate(AiPlan plan);

    ValueTask<CommandResult> ExecuteAsync(
        AiPlan plan,
        CancellationToken cancellationToken = default);
}

public interface IAiPlanner
{
    bool IsConfigured { get; }

    ValueTask<AiPlan?> CreatePlanAsync(
        string userRequest,
        IReadOnlyList<AiToolDescriptor> availableTools,
        CancellationToken cancellationToken = default);
}

public interface IAiApiKeyStore
{
    bool HasApiKey { get; }

    string? GetApiKey();

    void SaveApiKey(string apiKey);

    void DeleteApiKey();
}

public sealed class AiPlannerException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class DisabledAiPlanner : IAiPlanner
{
    public bool IsConfigured => false;

    public ValueTask<AiPlan?> CreatePlanAsync(
        string userRequest,
        IReadOnlyList<AiToolDescriptor> availableTools,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AiPlan?>(null);
}
