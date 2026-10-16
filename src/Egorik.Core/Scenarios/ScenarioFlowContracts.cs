using Egorik.Core.Commands;

namespace Egorik.Core.Scenarios;

public enum ScenarioNodeKind { Start, Action, Delay, Condition, End, LegacyCommand }

public enum ScenarioConditionKind { ApplicationConfigured, SiteConfigured, AiEnabled }

public sealed record ScenarioNode(
    Guid Id,
    ScenarioNodeKind Kind,
    string Title = "",
    Guid? Next = null,
    Guid? Otherwise = null,
    string? ActionId = null,
    IReadOnlyDictionary<string, string>? Arguments = null,
    int DelaySeconds = 0,
    ScenarioConditionKind Condition = ScenarioConditionKind.ApplicationConfigured,
    string Value = "");

public sealed record ScenarioFlow(int Version, Guid EntryNodeId, IReadOnlyList<ScenarioNode> Nodes);

public sealed record ScenarioValidationResult(bool IsValid, string? Error = null, Guid? NodeId = null);

public sealed record ScenarioExecutionProgress(Guid NodeId, string Message, bool IsFinished = false);

public interface IScenarioFlowExecutor
{
    ScenarioValidationResult Validate(ScenarioFlow flow);

    ValueTask<CommandResult> ExecuteAsync(
        ScenarioFlow flow,
        Func<string, CancellationToken, ValueTask<CommandResult>>? legacyCommand = null,
        IProgress<ScenarioExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
