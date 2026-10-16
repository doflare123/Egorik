using Egorik.App.ViewModels;
using Egorik.Core;
using Egorik.Core.Actions;
using Egorik.Core.Ai;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Scenarios;

namespace Egorik.App.Tests;

[TestClass]
public sealed class ScenarioEditorTests
{
    [TestMethod]
    public async Task CreateSaveAndReopenTypedScenarioKeepsActionArgumentsAndEdges()
    {
        var repository = new Repository();
        using var editor = CreateEditor(repository);
        editor.Name = "Рабочее утро";
        editor.Trigger = "начинаем работать";
        editor.AddActionCommand.Execute(null);
        var block = editor.SelectedBlock!;
        block.SelectedAction = block.AvailableActions.Single();
        block.Arguments.Single().Value = "Привет";
        await editor.SaveCommand.ExecuteAsync();
        Assert.AreEqual(1, repository.Scenarios.Count);
        Assert.AreEqual(3, repository.Scenarios[0].Flow!.Nodes.Count);
        Assert.AreEqual("Рабочее утро", editor.SelectedScenario?.Name);
        editor.NewCommand.Execute(null);
        editor.SelectedScenario = editor.Scenarios.Single();
        var reopened = editor.Blocks.Single(item => item.IsAction);
        Assert.AreEqual("Привет", reopened.Arguments.Single().Value);
        Assert.AreEqual("test.reply", reopened.SelectedAction?.Id);
        Assert.AreEqual(reopened.Id, editor.Blocks[0].Next?.Id);
        Assert.AreEqual(editor.Blocks[^1].Id, reopened.Next?.Id);
    }

    [TestMethod]
    public async Task OldScenarioOpensAsBlocksAndSavingKeepsOriginalCommandsAndEnabledFlag()
    {
        var old = new AssistantScenario { Name = "Старый", Trigger = "поехали", Commands = ["который час", "открой настройки"], Enabled = false };
        var repository = new Repository { Scenarios = [old] };
        using var editor = CreateEditor(repository);
        await editor.InitializeAsync();
        editor.SelectedScenario = editor.Scenarios.Single();
        Assert.AreEqual(2, editor.Blocks.Count(block => block.IsLegacy));
        await editor.SaveCommand.ExecuteAsync();
        CollectionAssert.AreEqual(old.Commands, repository.Scenarios[0].Commands);
        Assert.AreEqual(old.Id, repository.Scenarios[0].Id);
        Assert.IsFalse(repository.Scenarios[0].Enabled);
        Assert.AreEqual("который час", repository.Scenarios[0].Flow!.Nodes[1].Value);
    }

    [TestMethod]
    public async Task MissingPluginKeepsUnknownActionAndArgumentsAndBlocksSaving()
    {
        var end = new ScenarioNode(Guid.NewGuid(), ScenarioNodeKind.End);
        var missing = new ScenarioNode(Guid.NewGuid(), ScenarioNodeKind.Action, ActionId: "missing.action", Next: end.Id,
            Arguments: new Dictionary<string, string> { ["saved"] = "не терять" });
        var start = new ScenarioNode(Guid.NewGuid(), ScenarioNodeKind.Start, Next: missing.Id);
        var old = new AssistantScenario { Name = "Плагин", Trigger = "пуск", Flow = new(1, start.Id, [start, missing, end]) };
        var repository = new Repository { Scenarios = [old] };
        using var editor = CreateEditor(repository);
        await editor.InitializeAsync();
        editor.SelectedScenario = editor.Scenarios.Single();
        var draft = editor.BuildFlow().Nodes.Single(node => node.Kind == ScenarioNodeKind.Action);
        Assert.AreEqual("missing.action", draft.ActionId);
        Assert.AreEqual("не терять", draft.Arguments!["saved"]);
        await editor.SaveCommand.ExecuteAsync();
        Assert.AreSame(old, repository.Scenarios.Single());
        StringAssert.Contains(editor.Status, "Ошибка схемы");
    }

    [TestMethod]
    public void AddAndRemoveReconnectsConditionBranchesWithoutDanglingReferences()
    {
        using var editor = CreateEditor(new());
        editor.AddConditionCommand.Execute(null);
        var condition = editor.SelectedBlock!;
        editor.AddDelayCommand.Execute(null);
        var delay = editor.SelectedBlock!;
        condition.Otherwise = condition.Targets.Single(target => target.Id == delay.Id);
        Assert.AreEqual(delay.Id, condition.Next?.Id);
        editor.RemoveBlockCommand.Execute(null);
        Assert.AreEqual(editor.Blocks[^1].Id, condition.Next?.Id);
        Assert.AreEqual(editor.Blocks[^1].Id, condition.Otherwise?.Id);
        Assert.IsFalse(editor.BuildFlow().Nodes.Any(node => node.Id == delay.Id));
    }

    [TestMethod]
    public async Task CancelButtonInterruptsRunningPauseAndUnlocksEditor()
    {
        using var editor = CreateEditor(new());
        editor.AddDelayCommand.Execute(null);
        editor.SelectedBlock!.DelaySeconds = 30;
        var run = editor.RunCommand.ExecuteAsync();
        Assert.IsTrue(editor.IsRunning);
        Assert.IsTrue(editor.CancelCommand.CanExecute(null));
        editor.CancelCommand.Execute(null);
        await run;
        Assert.IsFalse(editor.IsRunning);
        Assert.IsFalse(editor.Blocks.Any(block => block.IsExecuting));
        Assert.AreEqual("Сценарий отменён.", editor.Status);
    }

    [TestMethod]
    public async Task DuplicateWakePhraseAndFutureFlowVersionCannotOverwriteSavedScenarios()
    {
        var repository = new Repository { Scenarios = [new() { Name = "Уже есть", Trigger = "старт" }] };
        using var editor = CreateEditor(repository);
        editor.Name = "Повтор";
        editor.Trigger = "Старт!";
        await editor.SaveCommand.ExecuteAsync();
        Assert.AreEqual(1, repository.Scenarios.Count);
        StringAssert.Contains(editor.Status, "уже используется");
        var flow = editor.BuildFlow() with { Version = 99 };
        repository.Scenarios = [new() { Name = "Будущий", Trigger = "будущее", Flow = flow }];
        await editor.InitializeAsync();
        editor.SelectedScenario = editor.Scenarios.Single();
        Assert.AreEqual(99, editor.BuildFlow().Version);
        await editor.SaveCommand.ExecuteAsync();
        Assert.AreEqual(99, repository.Scenarios[0].Flow!.Version);
    }

    [TestMethod]
    public async Task RunningClientActionRequestsNavigationToHost()
    {
        using var editor = CreateEditor(new());
        editor.AddActionCommand.Execute(null);
        editor.SelectedBlock!.SelectedAction = editor.SelectedBlock.AvailableActions.Single();
        editor.SelectedBlock.Arguments.Single().Value = "Ответ";
        ClientAction? requested = null;
        editor.NavigationRequested += (_, action) => requested = action;
        await editor.RunCommand.ExecuteAsync();
        Assert.AreEqual(ClientAction.OpenSettings, requested);
        Assert.AreEqual("Ответ", editor.Status);
    }

    [TestMethod]
    public void DroppingBlocksInBranchesCanBeUndoneAndRedoneWithoutLosingFields()
    {
        using var editor = CreateEditor(new());
        editor.AddConditionCommand.Execute(null);
        var condition = editor.SelectedBlock!.Id;
        editor.DropBlock(null, ScenarioNodeKind.Action, condition, false, 0);
        var action = editor.SelectedBlock!;
        action.SelectedAction = action.AvailableActions.Single();
        action.Arguments.Single().Value = "Сохранить";
        editor.DropBlock(action.Id, null, condition, true, 0);
        Assert.IsTrue(ScenarioStructure.TryRead(editor.BuildFlow(), out var moved, out _));
        Assert.AreEqual(0, moved!.Body[0].Then.Count);
        Assert.AreEqual(action.Id, moved.Body[0].Else.Single().Node.Id);
        editor.UndoCommand.Execute(null);
        Assert.IsTrue(ScenarioStructure.TryRead(editor.BuildFlow(), out var undone, out _));
        Assert.AreEqual("Сохранить", undone!.Body[0].Then.Single().Node.Arguments!["text"]);
        editor.RedoCommand.Execute(null);
        Assert.IsTrue(ScenarioStructure.TryRead(editor.BuildFlow(), out var redone, out _));
        Assert.AreEqual(action.Id, redone!.Body[0].Else.Single().Node.Id);
        editor.DropBlock(condition, null, condition, false, 0);
        StringAssert.Contains(editor.Status, "самого себя");
    }

    [TestMethod]
    public async Task SettingsSessionIsUndoableEvenAfterSaving()
    {
        using var editor = CreateEditor(new());
        editor.Name = "Тест"; editor.Trigger = "тест";
        editor.AddActionCommand.Execute(null);
        editor.SelectedBlock!.SelectedAction = editor.SelectedBlock.AvailableActions.Single();
        editor.SelectedBlock.Arguments.Single().Value = "До";
        editor.OpenBlockEditor();
        editor.SelectedBlock.Arguments.Single().Value = "После";
        editor.CloseBlockEditorCommand.Execute(null);
        await editor.SaveCommand.ExecuteAsync();
        editor.UndoCommand.Execute(null);
        Assert.AreEqual("До", editor.Blocks.Single(block => block.IsAction).Arguments.Single().Value);
        editor.RedoCommand.Execute(null);
        Assert.AreEqual("После", editor.Blocks.Single(block => block.IsAction).Arguments.Single().Value);
    }

    private static ScenarioEditorViewModel CreateEditor(Repository repository)
    {
        var settings = new Settings();
        var actions = new AssistantActionRegistry([new Provider()]);
        var executor = new ScenarioFlowExecutor(actions, settings);
        var engine = new AssistantEngine(new CommandRouter([]), repository, settings, new DisabledAiPlanner(), new AiToolExecutor([]), executor);
        return new(repository, actions, executor, engine);
    }

    private sealed class Repository : IScenarioRepository
    {
        public List<AssistantScenario> Scenarios { get; set; } = [];
        public ValueTask<IReadOnlyList<AssistantScenario>> GetAllAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<AssistantScenario>>(Scenarios);
        public ValueTask SaveAsync(IReadOnlyCollection<AssistantScenario> scenarios, CancellationToken cancellationToken = default)
        { Scenarios = scenarios.ToList(); return ValueTask.CompletedTask; }
    }

    private sealed class Settings : IAssistantSettingsService
    {
        public AssistantSettings Current { get; } = new();
        public ValueTask LoadAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class Provider : IAssistantActionProvider
    {
        public ActionProviderManifest Manifest { get; } = new("test", "Тест");
        public IReadOnlyList<AssistantActionDescriptor> Actions { get; } = [new("test.reply", "Ответ", "", [new("text", "Текст")])];
        public ValueTask<CommandResult> ExecuteAsync(ActionInvocation invocation, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CommandResult.Success(invocation.Arguments["text"], clientAction: ClientAction.OpenSettings));
    }
}
