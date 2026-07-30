using Egorik.Core.Ai;
using Egorik.Core.Commands;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class AiToolExecutorTests
{
    [TestMethod]
    public void Validate_RejectsUnknownTool()
    {
        var executor = new AiToolExecutor([new RecordingTool("allowed")]);
        var plan = Plan(("not_allowed", Arguments()));

        var result = executor.Validate(plan);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.Error, "неизвестный инструмент");
    }

    [TestMethod]
    public void Validate_RejectsUnexpectedArgument()
    {
        var executor = new AiToolExecutor([new RecordingTool("allowed")]);
        var plan = Plan(("allowed", Arguments(("value", "ok"), ("extra", "bad"))));

        var result = executor.Validate(plan);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.Error, "не принимает аргумент");
    }

    [TestMethod]
    public void Validate_RejectsDisallowedValue()
    {
        var executor = new AiToolExecutor(
            [new RecordingTool("allowed", ["first", "second"])]);
        var plan = Plan(("allowed", Arguments(("value", "third"))));

        var result = executor.Validate(plan);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.Error, "не разрешено");
    }

    [TestMethod]
    public void Validate_RejectsHighRiskTool()
    {
        var executor = new AiToolExecutor(
            [new RecordingTool("dangerous", risk: CommandRisk.High)]);
        var plan = Plan(("dangerous", Arguments(("value", "ok"))));

        var result = executor.Validate(plan);

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.Error, "не разрешён");
    }

    [TestMethod]
    public async Task ExecuteAsync_RunsValidatedStepsInOrder()
    {
        var calls = new List<string>();
        var executor = new AiToolExecutor(
            [
                new RecordingTool("first", calls: calls),
                new RecordingTool("second", calls: calls)
            ]);
        var plan = Plan(
            ("first", Arguments(("value", "one"))),
            ("second", Arguments(("value", "two"))));

        var result = await executor.ExecuteAsync(plan);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { "first:one", "second:two" },
            calls);
    }

    [TestMethod]
    public void Validate_RejectsMoreThanMaximumSteps()
    {
        var executor = new AiToolExecutor([new RecordingTool("allowed")]);
        var steps = Enumerable.Range(0, AiToolExecutor.MaximumPlanSteps + 1)
            .Select(_ => new PlannedToolCall(
                "allowed",
                Arguments(("value", "ok"))))
            .ToArray();

        var result = executor.Validate(new AiPlan(steps));

        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(result.Error, "больше");
    }

    private static AiPlan Plan(
        params (string Tool, IReadOnlyDictionary<string, string> Arguments)[] steps) =>
        new(steps
            .Select(step => new PlannedToolCall(step.Tool, step.Arguments))
            .ToArray());

    private static IReadOnlyDictionary<string, string> Arguments(
        params (string Name, string Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);

    private sealed class RecordingTool(
        string id,
        IReadOnlyList<string>? allowedValues = null,
        CommandRisk risk = CommandRisk.Low,
        List<string>? calls = null) : IAiTool
    {
        public AiToolDescriptor Descriptor { get; } = new(
            id,
            id,
            [
                new AiToolParameterDescriptor(
                    "value",
                    "value",
                    AllowedValues: allowedValues)
            ],
            risk);

        public ValueTask<CommandResult> ExecuteAsync(
            IReadOnlyDictionary<string, string> arguments,
            CancellationToken cancellationToken = default)
        {
            calls?.Add($"{id}:{arguments["value"]}");
            return ValueTask.FromResult(CommandResult.Success(id, id));
        }
    }
}
