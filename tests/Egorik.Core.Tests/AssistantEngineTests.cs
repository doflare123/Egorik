using Egorik.Core.Ai;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Scenarios;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class AssistantEngineTests
{
    [TestMethod]
    public async Task ExecuteAsync_RemovesAssistantNameBeforeRouting()
    {
        var command = new ExactCommand("пинг", "понг");
        var engine = CreateEngine([command], []);

        var result = await engine.ExecuteAsync("Егорик, пинг");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("понг", result.Message);
    }

    [TestMethod]
    public async Task ExecuteAsync_RunsScenarioCommandsInOrder()
    {
        var calls = new List<string>();
        var commands = new IAssistantCommand[]
        {
            new ExactCommand("первый", "один", calls),
            new ExactCommand("второй", "два", calls)
        };
        var scenarios = new[]
        {
            new AssistantScenario
            {
                Name = "Тест",
                Trigger = "запусти тест",
                Commands = ["первый", "второй"]
            }
        };
        var engine = CreateEngine(commands, scenarios);

        var result = await engine.ExecuteAsync("запусти тест");

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] { "первый", "второй" }, calls);
    }

    [TestMethod]
    public async Task ExecuteAsync_UsesValidatedAiPlanAsFallback()
    {
        var calls = new List<string>();
        var tool = new RecordingAiTool(calls);
        var executor = new AiToolExecutor([tool]);
        var planner = new FixedAiPlanner(new AiPlan(
            [
                new PlannedToolCall(
                    tool.Descriptor.Id,
                    new Dictionary<string, string>
                    {
                        ["value"] = "готово"
                    })
            ]));
        var engine = new AssistantEngine(
            new CommandRouter([]),
            new MemoryScenarioRepository([]),
            new MemorySettingsService(),
            planner,
            executor);

        var result = await engine.ExecuteAsync("сделай неизвестное действие");

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] { "готово" }, calls);
    }

    private static AssistantEngine CreateEngine(
        IEnumerable<IAssistantCommand> commands,
        IReadOnlyList<AssistantScenario> scenarios)
    {
        var router = new CommandRouter(commands);
        return new AssistantEngine(
            router,
            new MemoryScenarioRepository(scenarios),
            new MemorySettingsService(),
            new DisabledAiPlanner(),
            new AiToolExecutor([]));
    }

    private sealed class ExactCommand(
        string phrase,
        string response,
        List<string>? calls = null) : IAssistantCommand
    {
        public CommandDescriptor Descriptor { get; } =
            new($"test.{phrase}", phrase, phrase);

        public ValueTask<CommandMatch?> MatchAsync(
            string normalizedText,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                normalizedText == phrase ? CommandMatch.Create(1) : null);

        public ValueTask<CommandResult> ExecuteAsync(
            CommandContext context,
            CommandMatch match,
            CancellationToken cancellationToken = default)
        {
            calls?.Add(phrase);
            return ValueTask.FromResult(CommandResult.Success(response, Descriptor.Id));
        }
    }

    private sealed class FixedAiPlanner(AiPlan plan) : IAiPlanner
    {
        public bool IsConfigured => true;

        public ValueTask<AiPlan?> CreatePlanAsync(
            string userRequest,
            IReadOnlyList<AiToolDescriptor> availableTools,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AiPlan?>(plan);
    }

    private sealed class RecordingAiTool(List<string> calls) : IAiTool
    {
        public AiToolDescriptor Descriptor { get; } = new(
            "test_tool",
            "Тест",
            [new AiToolParameterDescriptor("value", "Значение")]);

        public ValueTask<CommandResult> ExecuteAsync(
            IReadOnlyDictionary<string, string> arguments,
            CancellationToken cancellationToken = default)
        {
            calls.Add(arguments["value"]);
            return ValueTask.FromResult(
                CommandResult.Success(arguments["value"], Descriptor.Id));
        }
    }

    private sealed class MemoryScenarioRepository(IReadOnlyList<AssistantScenario> scenarios)
        : IScenarioRepository
    {
        public ValueTask<IReadOnlyList<AssistantScenario>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(scenarios);

        public ValueTask SaveAsync(
            IReadOnlyCollection<AssistantScenario> updated,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class MemorySettingsService : IAssistantSettingsService
    {
        public AssistantSettings Current { get; } = new()
        {
            AssistantName = "Егорик",
            OnboardingCompleted = true
        };

        public ValueTask LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SaveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
