using Egorik.Core;
using Egorik.Core.Actions;
using Egorik.Core.Ai;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Scenarios;
using Egorik.Infrastructure.Persistence;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class ScenarioFlowTests
{
    [TestMethod]
    public async Task EntireGraphIsValidatedBeforeFirstSideEffectIncludingUnusedBranch()
    {
        var provider = new TestProvider();
        var executor = CreateExecutor(provider);
        var end = Node(ScenarioNodeKind.End);
        var bad = Node(ScenarioNodeKind.Action) with { ActionId = "test.unknown", Next = end.Id };
        var branch = Node(ScenarioNodeKind.Condition) with { Condition = ScenarioConditionKind.AiEnabled, Next = end.Id, Otherwise = bad.Id };
        var action = ActionNode(branch.Id);
        var start = Node(ScenarioNodeKind.Start) with { Next = action.Id };
        var result = await executor.ExecuteAsync(new(1, start.Id, [start, action, branch, bad, end]));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, provider.Calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConditionExecutesExactlyOneBranch(bool configured)
    {
        var provider = new TestProvider();
        var settings = new MemorySettings();
        if (configured) { settings.Current.Applications["браузер"] = "browser.exe"; }
        var executor = new ScenarioFlowExecutor(new AssistantActionRegistry([provider]), settings);
        var end = Node(ScenarioNodeKind.End);
        var yes = ActionNode(end.Id, "да");
        var no = ActionNode(end.Id, "нет");
        var branch = Node(ScenarioNodeKind.Condition) with { Value = "браузер", Next = yes.Id, Otherwise = no.Id };
        var start = Node(ScenarioNodeKind.Start) with { Next = branch.Id };
        var progress = new RecordingProgress();
        var result = await executor.ExecuteAsync(new(1, start.Id, [start, branch, yes, no, end]), progress: progress);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, provider.Calls);
        Assert.AreEqual(configured ? "да" : "нет", provider.LastValue);
        Assert.AreEqual(end.Id, progress.Updates[^1].NodeId);
        Assert.IsTrue(progress.Updates[^1].IsFinished);
    }

    [TestMethod]
    public async Task ActionFailureStopsRemainingNodesAndClientActionIsPreservedOnSuccess()
    {
        var provider = new TestProvider { Fail = true };
        var executor = CreateExecutor(provider);
        var end = Node(ScenarioNodeKind.End);
        var second = ActionNode(end.Id);
        var first = ActionNode(second.Id);
        var start = Node(ScenarioNodeKind.Start) with { Next = first.Id };
        var flow = new ScenarioFlow(1, start.Id, [start, first, second, end]);
        Assert.IsFalse((await executor.ExecuteAsync(flow)).IsSuccess);
        Assert.AreEqual(1, provider.Calls);
        provider.Fail = false;
        var result = await executor.ExecuteAsync(flow);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(ClientAction.OpenSettings, result.ClientAction);
        Assert.AreEqual(3, provider.Calls);
    }

    [TestMethod]
    public void RejectsCyclesMissingEdgesDuplicateIdsOrphansAndUnsupportedVersions()
    {
        var executor = CreateExecutor(new());
        var end = Node(ScenarioNodeKind.End);
        var action = ActionNode(end.Id);
        var start = Node(ScenarioNodeKind.Start) with { Next = action.Id };
        var valid = new ScenarioFlow(1, start.Id, [start, action, end]);
        Assert.IsTrue(executor.Validate(valid).IsValid);
        Assert.IsFalse(executor.Validate(valid with { Version = 2 }).IsValid);
        Assert.IsFalse(executor.Validate(valid with { Nodes = [start, action with { Next = action.Id }, end] }).IsValid);
        Assert.IsFalse(executor.Validate(valid with { Nodes = [start, action with { Next = Guid.NewGuid() }, end] }).IsValid);
        Assert.IsFalse(executor.Validate(valid with { Nodes = [start, action, action, end] }).IsValid);
        Assert.IsFalse(executor.Validate(valid with { Nodes = [start, action, end, Node(ScenarioNodeKind.End)] }).IsValid);
        Assert.IsFalse(executor.Validate(valid with { Nodes = [start, action, end with { Next = action.Id }] }).IsValid);
        Assert.IsFalse(executor.Validate(valid with { Nodes = null! }).IsValid);
        Assert.IsFalse(executor.Validate(valid with { Nodes = [start, null!, end] }).IsValid);
    }

    [TestMethod]
    public async Task CancelDuringDelayPreventsFollowingAction()
    {
        var provider = new TestProvider();
        var executor = CreateExecutor(provider);
        var end = Node(ScenarioNodeKind.End);
        var action = ActionNode(end.Id);
        var delay = Node(ScenarioNodeKind.Delay) with { Next = action.Id, DelaySeconds = 30 };
        var start = Node(ScenarioNodeKind.Start) with { Next = delay.Id };
        using var cancellation = new CancellationTokenSource();
        var run = executor.ExecuteAsync(new(1, start.Id, [start, delay, action, end]), cancellationToken: cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await run);
        Assert.AreEqual(0, provider.Calls);
    }

    [TestMethod]
    public void RejectsDelayBudgetAndNodeCountOverflow()
    {
        var executor = CreateExecutor(new());
        var end = Node(ScenarioNodeKind.End);
        var nodes = new List<ScenarioNode> { end };
        for (var index = 0; index < 5; index++) { nodes.Insert(0, Node(ScenarioNodeKind.Delay) with { DelaySeconds = 30, Next = nodes[0].Id }); }
        var start = Node(ScenarioNodeKind.Start) with { Next = nodes[0].Id };
        nodes.Insert(0, start);
        Assert.IsFalse(executor.Validate(new(1, start.Id, nodes)).IsValid);
        Assert.IsFalse(executor.Validate(new(1, start.Id, Enumerable.Repeat(end, 33).ToArray())).IsValid);
    }

    [TestMethod]
    public async Task TypedScenarioTakesPriorityWithoutCallingAiOrTextRouter()
    {
        var provider = new TestProvider();
        var settings = new MemorySettings();
        var planner = new CountingPlanner();
        var end = Node(ScenarioNodeKind.End);
        var action = ActionNode(end.Id);
        var start = Node(ScenarioNodeKind.Start) with { Next = action.Id };
        var repository = new MemoryScenarios([new() { Name = "Работа", Trigger = "начать", Flow = new(1, start.Id, [start, action, end]) }]);
        var engine = new AssistantEngine(new CommandRouter([]), repository, settings, planner,
            new AiToolExecutor([]), new ScenarioFlowExecutor(new AssistantActionRegistry([provider]), settings));
        var result = await engine.ExecuteAsync("Егорик начать");
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, provider.Calls);
        Assert.AreEqual(0, planner.Calls);
    }

    [TestMethod]
    public async Task OldScenariosRoundTripWithoutLosingCommandsAndNewGraphsRoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "EgorikTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new ApplicationDataPaths(root);
            await File.WriteAllTextAsync(paths.ScenariosFile, """[{"name":"Старый","trigger":"старт","commands":["который час"],"enabled":false}]""");
            var repository = new JsonScenarioRepository(paths);
            var old = (await repository.GetAllAsync()).Single();
            Assert.IsNull(old.Flow);
            Assert.IsFalse(old.Enabled);
            var end = Node(ScenarioNodeKind.End);
            var start = Node(ScenarioNodeKind.Start) with { Next = end.Id };
            old.Flow = new(1, start.Id, [start, end]);
            await repository.SaveAsync([old]);
            var loaded = (await repository.GetAllAsync()).Single();
            CollectionAssert.AreEqual(old.Commands, loaded.Commands);
            Assert.AreEqual(old.Id, loaded.Id);
            Assert.IsFalse(loaded.Enabled);
            Assert.IsTrue(CreateExecutor(new()).Validate(loaded.Flow!).IsValid);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task LegacyBlockCallsExplicitAdapterWithOriginalTextAndCancellation()
    {
        var executor = CreateExecutor(new());
        var end = Node(ScenarioNodeKind.End);
        var legacy = Node(ScenarioNodeKind.LegacyCommand) with { Value = "который час", Next = end.Id };
        var start = Node(ScenarioNodeKind.Start) with { Next = legacy.Id };
        var flow = new ScenarioFlow(1, start.Id, [start, legacy, end]);
        Assert.IsFalse((await executor.ExecuteAsync(flow)).IsSuccess);
        using var cancellation = new CancellationTokenSource();
        var result = await executor.ExecuteAsync(flow, (text, token) =>
        {
            Assert.AreEqual("который час", text);
            Assert.AreEqual(cancellation.Token, token);
            return ValueTask.FromResult(CommandResult.Success("Сейчас полдень."));
        }, cancellationToken: cancellation.Token);
        Assert.IsTrue(result.IsSuccess);
    }

    private static ScenarioNode Node(ScenarioNodeKind kind) => new(Guid.NewGuid(), kind, kind.ToString());
    private static ScenarioNode ActionNode(Guid next, string value = "тест") => Node(ScenarioNodeKind.Action) with
    { Next = next, ActionId = "test.reply", Arguments = new Dictionary<string, string> { ["text"] = value } };
    private static ScenarioFlowExecutor CreateExecutor(TestProvider provider) => new(new AssistantActionRegistry([provider]), new MemorySettings());

    private sealed class RecordingProgress : IProgress<ScenarioExecutionProgress>
    {
        public List<ScenarioExecutionProgress> Updates { get; } = [];
        public void Report(ScenarioExecutionProgress value) => Updates.Add(value);
    }

    private sealed class TestProvider : IAssistantActionProvider
    {
        public int Calls { get; private set; }
        public string? LastValue { get; private set; }
        public bool Fail { get; set; }
        public ActionProviderManifest Manifest { get; } = new("test", "Тест");
        public IReadOnlyList<AssistantActionDescriptor> Actions { get; } = [new("test.reply", "Ответ", "", [new("text", "Текст")])];
        public ValueTask<CommandResult> ExecuteAsync(ActionInvocation invocation, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastValue = invocation.Arguments["text"];
            return ValueTask.FromResult(Fail ? CommandResult.Failure("Сбой") : CommandResult.Success("Готово", clientAction: ClientAction.OpenSettings));
        }
    }

    private sealed class MemorySettings : IAssistantSettingsService
    {
        public AssistantSettings Current { get; } = new();
        public ValueTask LoadAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class MemoryScenarios(IReadOnlyList<AssistantScenario> scenarios) : IScenarioRepository
    {
        public ValueTask<IReadOnlyList<AssistantScenario>> GetAllAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(scenarios);
        public ValueTask SaveAsync(IReadOnlyCollection<AssistantScenario> items, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class CountingPlanner : IAiPlanner
    {
        public int Calls { get; private set; }
        public bool IsConfigured => true;
        public ValueTask<AiPlan?> CreatePlanAsync(string userRequest, IReadOnlyList<AiToolDescriptor> availableTools, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult<AiPlan?>(null); }
    }
}
