using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using Egorik.Core;
using Egorik.Core.Actions;
using Egorik.Core.Commands;
using Egorik.Core.Scenarios;

namespace Egorik.App.ViewModels;

public sealed class ScenarioEditorViewModel : ViewModelBase, IDisposable
{
    private readonly IScenarioRepository _repository;
    private readonly IAssistantActionRegistry _actions;
    private readonly IScenarioFlowExecutor _executor;
    private readonly AssistantEngine _engine;
    private ScenarioItemViewModel? _selectedScenario;
    private ScenarioBlockViewModel? _selectedBlock;
    private Guid _draftId;
    private int _flowVersion = 1;
    private List<string> _originalCommands = [];
    private string _name = string.Empty;
    private string _trigger = string.Empty;
    private string _status = "Перетащите блок из палитры к стыку в сценарии.";
    private bool _enabled = true;
    private bool _isRunning;
    private CancellationTokenSource? _runCancellation;
    private readonly Stack<ScenarioFlow> _undo = new();
    private readonly Stack<ScenarioFlow> _redo = new();
    private bool _isBlockEditorOpen;
    private ScenarioFlow? _beforeEditing;

    public ScenarioEditorViewModel(IScenarioRepository repository, IAssistantActionRegistry actions,
        IScenarioFlowExecutor executor, AssistantEngine engine)
    {
        _repository = repository;
        _actions = actions;
        _executor = executor;
        _engine = engine;
        NewCommand = new RelayCommand(NewDraft);
        AddActionCommand = new RelayCommand(() => AddBlock(ScenarioNodeKind.Action));
        AddDelayCommand = new RelayCommand(() => AddBlock(ScenarioNodeKind.Delay));
        AddConditionCommand = new RelayCommand(() => AddBlock(ScenarioNodeKind.Condition));
        AddTextCommand = new RelayCommand(() => AddBlock(ScenarioNodeKind.LegacyCommand));
        RemoveBlockCommand = new RelayCommand(RemoveBlock, () => SelectedBlock?.CanRemove == true);
        ValidateCommand = new RelayCommand(() => ValidateDraft());
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => SelectedScenario is not null);
        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(() => _runCancellation?.Cancel(), () => IsRunning);
        UndoCommand = new RelayCommand(() => RestoreHistory(_undo, _redo), () => !IsRunning && _undo.Count > 0);
        RedoCommand = new RelayCommand(() => RestoreHistory(_redo, _undo), () => !IsRunning && _redo.Count > 0);
        EditBlockCommand = new RelayCommand(OpenBlockEditor);
        CloseBlockEditorCommand = new RelayCommand(CloseBlockEditor);
        NewDraft();
    }

    public ObservableCollection<ScenarioItemViewModel> Scenarios { get; } = [];
    public event EventHandler<ClientAction>? NavigationRequested;
    public ObservableCollection<ScenarioBlockViewModel> Blocks { get; } = [];
    public RelayCommand NewCommand { get; }
    public RelayCommand AddActionCommand { get; }
    public RelayCommand AddDelayCommand { get; }
    public RelayCommand AddConditionCommand { get; }
    public RelayCommand AddTextCommand { get; }
    public RelayCommand RemoveBlockCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand EditBlockCommand { get; }
    public RelayCommand CloseBlockEditorCommand { get; }
    public bool IsBlockEditorOpen { get => _isBlockEditorOpen; private set => SetProperty(ref _isBlockEditorOpen, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Trigger { get => _trigger; set => SetProperty(ref _trigger, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            SetProperty(ref _isRunning, value);
            RunCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            UpdateHistoryCommands();
        }
    }

    public ScenarioItemViewModel? SelectedScenario
    {
        get => _selectedScenario;
        set
        {
            if (IsRunning || !SetProperty(ref _selectedScenario, value)) { return; }
            DeleteCommand.RaiseCanExecuteChanged();
            if (value is not null)
            {
                var differentScenario = value.Model.Id != _draftId;
                CloseBlockEditor();
                LoadDraft(value.Model);
                if (differentScenario) { _undo.Clear(); _redo.Clear(); }
                UpdateHistoryCommands();
            }
        }
    }

    public ScenarioBlockViewModel? SelectedBlock
    {
        get => _selectedBlock;
        set { SetProperty(ref _selectedBlock, value); RemoveBlockCommand.RaiseCanExecuteChanged(); }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var scenarios = await _repository.GetAllAsync(cancellationToken);
        Scenarios.Clear();
        foreach (var scenario in scenarios) { Scenarios.Add(new(scenario)); }
    }

    public void RefreshActions()
    {
        if (IsRunning) { return; }
        var selectedId = SelectedBlock?.Id;
        LoadDraft(new AssistantScenario
        {
            Id = _draftId,
            Name = Name,
            Trigger = Trigger,
            Enabled = Enabled,
            Commands = [.. _originalCommands],
            Flow = BuildFlow()
        });
        SelectedBlock = Blocks.FirstOrDefault(block => block.Id == selectedId) ?? Blocks.FirstOrDefault();
    }

    public ScenarioFlow BuildFlow() => new(_flowVersion,
        Blocks.FirstOrDefault(block => block.Kind == ScenarioNodeKind.Start)?.Id ?? Guid.Empty,
        Blocks.Select(block => block.ToNode()).ToArray());

    private void NewDraft()
    {
        if (IsRunning) { return; }
        CloseBlockEditor();
        _undo.Clear(); _redo.Clear(); UpdateHistoryCommands();
        SelectedScenario = null;
        var end = new ScenarioNode(Guid.NewGuid(), ScenarioNodeKind.End, "Завершение");
        var start = new ScenarioNode(Guid.NewGuid(), ScenarioNodeKind.Start, "Старт", Next: end.Id);
        LoadDraft(new AssistantScenario { Flow = new(1, start.Id, [start, end]) });
        Status = "Новый сценарий. Добавьте действие между стартом и завершением.";
    }

    private void LoadDraft(AssistantScenario scenario)
    {
        _draftId = scenario.Id;
        _flowVersion = scenario.Flow?.Version ?? 1;
        Name = scenario.Name;
        Trigger = scenario.Trigger;
        Enabled = scenario.Enabled;
        _originalCommands = [.. scenario.Commands];
        foreach (var block in Blocks) { block.PropertyChanged -= OnBlockChanged; }
        Blocks.Clear();
        var flow = scenario.Flow;
        if (flow is null)
        {
            var end = new ScenarioNode(Guid.NewGuid(), ScenarioNodeKind.End, "Завершение");
            var nodes = new List<ScenarioNode> { end };
            foreach (var command in scenario.Commands.AsEnumerable().Reverse())
            {
                nodes.Insert(0, new(Guid.NewGuid(), ScenarioNodeKind.LegacyCommand, "Текстовая команда", Next: nodes[0].Id, Value: command));
            }

            var start = new ScenarioNode(Guid.NewGuid(), ScenarioNodeKind.Start, "Старт", Next: nodes[0].Id);
            nodes.Insert(0, start);
            flow = new(1, start.Id, nodes);
            Status = "Старый сценарий показан текстовыми блоками. Исходные команды сохранятся. Заменяйте их на действия постепенно.";
        }
        else
        {
            Status = "Перетаскивайте блоки за заголовок. Двойной щелчок — параметры.";
        }

        foreach (var node in flow.Nodes)
        {
            var block = new ScenarioBlockViewModel(node, _actions.AvailableActions);
            block.PropertyChanged += OnBlockChanged;
            Blocks.Add(block);
        }

        RefreshTargets();
        SelectedBlock = Blocks.FirstOrDefault();
    }

    private void AddBlock(ScenarioNodeKind kind)
    {
        if (IsRunning) { return; }
        if (!ScenarioStructure.TryRead(BuildFlow(), out var program, out var error))
        { Status = error ?? "Не удалось открыть сценарий."; return; }
        Guid? owner = null;
        var otherwise = false;
        var index = program!.Body.Count;
        bool Find(IReadOnlyList<ScenarioStackBlock> stack, Guid? parent, bool branch)
        {
            for (var position = 0; position < stack.Count; position++)
            {
                var block = stack[position];
                if (block.Node.Id == SelectedBlock?.Id) { owner = parent; otherwise = branch; index = position + 1; return true; }
                if (Find(block.Then, block.Node.Id, false) || Find(block.Else, block.Node.Id, true)) { return true; }
            }
            return false;
        }
        if (SelectedBlock?.Kind == ScenarioNodeKind.Start) { index = 0; }
        else { Find(program.Body, null, false); }
        DropBlock(null, kind, owner, otherwise, index);
    }

    public void DropBlock(Guid? movingId, ScenarioNodeKind? kind, Guid? owner, bool otherwise, int index)
    {
        if (IsRunning) { return; }
        var before = BuildFlow();
        var title = kind switch { ScenarioNodeKind.Delay => "Подождать", ScenarioNodeKind.Condition => "Если", ScenarioNodeKind.LegacyCommand => "Команда", _ => "Действие" };
        var added = kind is null ? null : new ScenarioNode(Guid.NewGuid(), kind.Value, title, DelaySeconds: kind == ScenarioNodeKind.Delay ? 1 : 0);
        if (!ScenarioStructure.TryEdit(before, movingId, added, owner, otherwise, index, false, out var edited, out var error))
        { Status = error ?? "Не удалось переместить блок."; return; }
        _undo.Push(before); _redo.Clear();
        ApplyFlow(edited, movingId ?? added?.Id, true);
        Status = movingId is null ? "Блок добавлен. Двойной щелчок открывает параметры." : "Блок перемещён. Связи обновлены автоматически.";
    }

    private void RemoveBlock()
    {
        if (IsRunning || SelectedBlock?.CanRemove != true) { return; }
        var before = BuildFlow();
        if (!ScenarioStructure.TryEdit(before, SelectedBlock.Id, null, null, false, 0, true, out var edited, out var error))
        { Status = error ?? "Не удалось удалить блок."; return; }
        _undo.Push(before); _redo.Clear();
        IsBlockEditorOpen = false;
        ApplyFlow(edited, null, true);
        Status = "Блок удалён вместе с содержимым. Действие можно отменить.";
    }

    private void ApplyFlow(ScenarioFlow flow, Guid? selectedId, bool preserveIdentity = false)
    {
        if (preserveIdentity)
        {
            var previous = Blocks.ToDictionary(block => block.Id);
            foreach (var block in Blocks) { block.PropertyChanged -= OnBlockChanged; }
            Blocks.Clear();
            foreach (var node in flow.Nodes)
            {
                var block = previous.GetValueOrDefault(node.Id) ?? new ScenarioBlockViewModel(node, _actions.AvailableActions);
                block.Next = node.Next is null ? null : new(node.Next.Value, "");
                block.Otherwise = node.Otherwise is null ? null : new(node.Otherwise.Value, "");
                block.PropertyChanged += OnBlockChanged;
                Blocks.Add(block);
            }
            RefreshTargets();
        }
        else
        {
            LoadDraft(new AssistantScenario
            {
                Id = _draftId,
                Name = Name,
                Trigger = Trigger,
                Enabled = Enabled,
                Commands = [.. _originalCommands],
                Flow = flow
            });
        }
        SelectedBlock = Blocks.FirstOrDefault(block => block.Id == selectedId) ?? Blocks.FirstOrDefault();
        UpdateHistoryCommands();
    }

    private void RestoreHistory(Stack<ScenarioFlow> source, Stack<ScenarioFlow> destination)
    {
        if (IsRunning || source.Count == 0) { return; }
        IsBlockEditorOpen = false;
        _beforeEditing = null;
        destination.Push(BuildFlow());
        ApplyFlow(source.Pop(), SelectedBlock?.Id);
        Status = "Изменение восстановлено.";
    }

    private void UpdateHistoryCommands()
    {
        UndoCommand?.RaiseCanExecuteChanged();
        RedoCommand?.RaiseCanExecuteChanged();
    }

    public void OpenBlockEditor()
    {
        if (IsRunning || SelectedBlock is null) { return; }
        _beforeEditing = BuildFlow();
        IsBlockEditorOpen = true;
    }

    private void CloseBlockEditor()
    {
        if (IsBlockEditorOpen && _beforeEditing is not null &&
            JsonSerializer.Serialize(_beforeEditing) != JsonSerializer.Serialize(BuildFlow()))
        {
            // A settings session is one undoable edit, including all its fields.
            _undo.Push(_beforeEditing); _redo.Clear(); UpdateHistoryCommands();
        }
        IsBlockEditorOpen = false;
        _beforeEditing = null;
    }

    private void OnBlockChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ScenarioBlockViewModel.Title)) { RefreshTargets(); }
    }

    private void RefreshTargets()
    {
        for (var index = 0; index < Blocks.Count; index++) { Blocks[index].Number = index + 1; }
        foreach (var block in Blocks) { block.RefreshTargets(Blocks.ToArray()); }
    }

    private bool ValidateDraft()
    {
        var validation = _executor.Validate(BuildFlow());
        Status = validation.IsValid ? "Схема проверена: можно сохранить или запустить." : $"Ошибка схемы: {validation.Error}";
        if (validation.NodeId is not null) { SelectedBlock = Blocks.FirstOrDefault(block => block.Id == validation.NodeId); }
        return validation.IsValid;
    }

    private async Task SaveAsync()
    {
        if (IsRunning || !ValidateDraft()) { return; }
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 120 ||
            string.IsNullOrWhiteSpace(Trigger) || Trigger.Trim().Length > 300 ||
            TextNormalizer.Normalize(Trigger).Length == 0 || Name.Any(char.IsControl) || Trigger.Any(char.IsControl))
        {
            Status = "Введите название (до 120 символов) и фразу запуска (до 300 символов).";
            return;
        }

        try
        {
            var all = (await _repository.GetAllAsync()).ToList();
            if (all.Any(scenario => scenario.Id != _draftId && TextNormalizer.Normalize(scenario.Trigger) == TextNormalizer.Normalize(Trigger)))
            {
                Status = "Такая фраза запуска уже используется.";
                return;
            }

            var scenario = new AssistantScenario
            {
                Id = _draftId,
                Name = Name.Trim(),
                Trigger = Trigger.Trim(),
                Enabled = Enabled,
                Commands = [.. _originalCommands],
                Flow = BuildFlow()
            };
            var index = all.FindIndex(item => item.Id == _draftId);
            if (index >= 0) { all[index] = scenario; } else { all.Add(scenario); }
            await _repository.SaveAsync(all);
            await InitializeAsync();
            SelectedScenario = Scenarios.First(item => item.Model.Id == scenario.Id);
            Status = $"Сценарий «{scenario.Name}» сохранён.";
        }
        catch (Exception exception) { Status = $"Не удалось сохранить сценарий: {exception.Message}"; }
    }

    private async Task DeleteAsync()
    {
        if (IsRunning || SelectedScenario is null) { return; }
        try
        {
            var id = SelectedScenario.Model.Id;
            var all = await _repository.GetAllAsync();
            await _repository.SaveAsync(all.Where(scenario => scenario.Id != id).ToArray());
            await InitializeAsync();
            NewDraft();
            Status = "Сценарий удалён.";
        }
        catch (Exception exception) { Status = $"Не удалось удалить сценарий: {exception.Message}"; }
    }

    private async Task RunAsync()
    {
        if (IsRunning || !ValidateDraft()) { return; }
        using var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        IsRunning = true;
        try
        {
            var progress = new InlineProgress(update =>
            {
                foreach (var block in Blocks) { block.IsExecuting = !update.IsFinished && block.Id == update.NodeId; }
                Status = update.Message;
            });
            var result = await _executor.ExecuteAsync(BuildFlow(),
                (text, token) => _engine.ExecuteAsync(text, token), progress, cancellation.Token);
            Status = result.Message;
            if (result.IsSuccess && result.ClientAction != ClientAction.None)
            {
                NavigationRequested?.Invoke(this, result.ClientAction);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Status = "Сценарий отменён."; }
        catch (Exception exception) { Status = $"Ошибка выполнения: {exception.Message}"; }
        finally
        {
            foreach (var block in Blocks) { block.IsExecuting = false; }
            _runCancellation = null;
            IsRunning = false;
        }
    }

    public void Dispose()
    {
        _runCancellation?.Cancel();
        foreach (var block in Blocks) { block.PropertyChanged -= OnBlockChanged; }
    }

    private sealed class InlineProgress(Action<ScenarioExecutionProgress> report) : IProgress<ScenarioExecutionProgress>
    {
        public void Report(ScenarioExecutionProgress value) => report(value);
    }
}
