using Egorik.Core.Scenarios;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class ScenarioStructureTests
{
    [TestMethod]
    public void MoveBetweenBranchesRebuildsAllLinksAndKeepsArgumentsAndIds()
    {
        var start = Node(ScenarioNodeKind.Start);
        var end = Node(ScenarioNodeKind.End);
        var action = Node(ScenarioNodeKind.Action) with { ActionId = "test.action", Arguments = new Dictionary<string, string> { ["value"] = "сохранить" } };
        var condition = new ScenarioStackBlock(Node(ScenarioNodeKind.Condition) with { Value = "браузер" }, [new(action, [], [])], []);
        var original = ScenarioStructure.Compile(new(start, end, [condition]));
        Assert.IsTrue(ScenarioStructure.TryEdit(original, action.Id, null, condition.Node.Id, true, 0, false, out var moved, out var error), error);
        var branch = moved.Nodes.Single(node => node.Id == condition.Node.Id);
        Assert.AreEqual(end.Id, branch.Next);
        Assert.AreEqual(action.Id, branch.Otherwise);
        Assert.AreEqual(end.Id, moved.Nodes.Single(node => node.Id == action.Id).Next);
        Assert.AreEqual("сохранить", moved.Nodes.Single(node => node.Id == action.Id).Arguments!["value"]);
        Assert.AreEqual(original.Nodes.Count, moved.Nodes.Count);
        Assert.AreEqual(action.Id, original.Nodes.Single(node => node.Id == condition.Node.Id).Next);
    }

    [TestMethod]
    public void MovingConditionMovesItsWholeContentsAndPreventsSelfNesting()
    {
        var start = Node(ScenarioNodeKind.Start);
        var end = Node(ScenarioNodeKind.End);
        var child = new ScenarioStackBlock(Node(ScenarioNodeKind.Delay) with { DelaySeconds = 2 }, [], []);
        var inner = new ScenarioStackBlock(Node(ScenarioNodeKind.Condition) with { Value = "сайт" }, [child], []);
        var outer = new ScenarioStackBlock(Node(ScenarioNodeKind.Condition) with { Value = "браузер" }, [inner], []);
        var flow = ScenarioStructure.Compile(new(start, end, [outer]));
        Assert.IsFalse(ScenarioStructure.TryEdit(flow, outer.Node.Id, null, inner.Node.Id, false, 0, false, out var unchanged, out _));
        Assert.AreSame(flow, unchanged);
        Assert.IsTrue(ScenarioStructure.TryEdit(flow, inner.Node.Id, null, null, false, 1, false, out var moved, out _));
        Assert.IsTrue(ScenarioStructure.TryRead(moved, out var program, out _));
        Assert.AreEqual(2, program!.Body.Count);
        Assert.AreEqual(0, program.Body[0].Then.Count);
        Assert.AreEqual(child.Node.Id, program.Body[1].Then.Single().Node.Id);
    }

    [TestMethod]
    public void ReorderWithinOneStackAccountsForRemovedPositionAndEmptyBranchesAcceptNewBlocks()
    {
        var first = new ScenarioStackBlock(Node(ScenarioNodeKind.Action), [], []);
        var second = new ScenarioStackBlock(Node(ScenarioNodeKind.Delay), [], []);
        var condition = new ScenarioStackBlock(Node(ScenarioNodeKind.Condition), [], []);
        var flow = ScenarioStructure.Compile(new(Node(ScenarioNodeKind.Start), Node(ScenarioNodeKind.End), [first, second, condition]));
        Assert.IsTrue(ScenarioStructure.TryEdit(flow, first.Node.Id, null, null, false, 2, false, out var moved, out _));
        Assert.IsTrue(ScenarioStructure.TryRead(moved, out var program, out _));
        Assert.AreEqual(second.Node.Id, program!.Body[0].Node.Id);
        Assert.AreEqual(first.Node.Id, program.Body[1].Node.Id);
        var added = Node(ScenarioNodeKind.LegacyCommand) with { Value = "который час" };
        Assert.IsTrue(ScenarioStructure.TryEdit(moved, null, added, condition.Node.Id, true, 0, false, out var inserted, out _));
        Assert.AreEqual(added.Id, inserted.Nodes.Single(node => node.Id == condition.Node.Id).Otherwise);
        Assert.IsTrue(ScenarioStructure.TryEdit(inserted, condition.Node.Id, null, null, false, 0, true, out var removed, out _));
        Assert.IsFalse(removed.Nodes.Any(node => node.Id == added.Id));
    }

    [TestMethod]
    public void CyclesDanglingEdgesAndFutureVersionsAreRejectedWithoutChangingOriginal()
    {
        var end = Node(ScenarioNodeKind.End);
        var action = Node(ScenarioNodeKind.Action);
        var start = Node(ScenarioNodeKind.Start) with { Next = action.Id };
        var flow = new ScenarioFlow(1, start.Id, [start, action with { Next = action.Id }, end]);
        Assert.IsFalse(ScenarioStructure.TryRead(flow, out _, out _));
        Assert.IsFalse(ScenarioStructure.TryRead(flow with { Nodes = [start, action with { Next = Guid.NewGuid() }, end] }, out _, out _));
        Assert.IsFalse(ScenarioStructure.TryRead(flow with { Version = 99 }, out _, out _));
        Assert.IsFalse(ScenarioStructure.TryEdit(flow, action.Id, null, null, false, 0, true, out var unchanged, out _));
        Assert.AreSame(flow, unchanged);
    }

    private static ScenarioNode Node(ScenarioNodeKind kind) => new(Guid.NewGuid(), kind, kind.ToString());
}
