using System.Collections.ObjectModel;
using Egorik.Core.Actions;
using Egorik.Core.Scenarios;

namespace Egorik.App.ViewModels;

public sealed record ScenarioNodeTarget(Guid Id, string Title);
public sealed record ScenarioConditionOption(ScenarioConditionKind Kind, string Title);
public sealed record ActionChoice(string Value, string Title);

public sealed class ActionArgumentViewModel(ActionParameterDescriptor descriptor, string value = "") : ViewModelBase
{
    private string _value = value;
    public ActionParameterDescriptor Descriptor { get; } = descriptor;
    public string Title => Descriptor.Title + (Descriptor.IsRequired ? " *" : " (необязательно)");
    public bool IsChoice => Descriptor.Kind is ActionParameterKind.Choice or ActionParameterKind.Boolean;
    public bool IsText => !IsChoice;
    public IReadOnlyList<ActionChoice> Choices
    {
        get
        {
            var choices = Descriptor.Kind == ActionParameterKind.Boolean
                ? new List<ActionChoice> { new("true", "Да"), new("false", "Нет") }
                : (Descriptor.AllowedValues ?? []).Select(value => new ActionChoice(value,
                    Descriptor.ValueTitles?.GetValueOrDefault(value) ?? value)).ToList();
            if (!Descriptor.IsRequired) { choices.Insert(0, new("", "Не задано")); }
            return choices;
        }
    }
    public ActionChoice? SelectedChoice
    {
        get => Choices.FirstOrDefault(choice => choice.Value == Value);
        set { if (value is not null) { Value = value.Value; } }
    }
    public int MaxLength => Descriptor.MaxLength;
    public string Hint => Descriptor.Kind == ActionParameterKind.Integer ? $"Целое число: {Descriptor.Minimum}–{Descriptor.Maximum}" : string.Empty;
    public string Value { get => _value; set { SetProperty(ref _value, value ?? string.Empty); RaisePropertyChanged(nameof(SelectedChoice)); } }
}

public sealed class ScenarioBlockViewModel : ViewModelBase
{
    private string _title;
    private AssistantActionDescriptor? _selectedAction;
    private ScenarioNodeTarget? _next;
    private ScenarioNodeTarget? _otherwise;
    private ScenarioConditionOption _selectedCondition;
    private string _value;
    private decimal _delaySeconds;
    private bool _isExecuting;
    private int _number;

    public ScenarioBlockViewModel(ScenarioNode node, IReadOnlyList<AssistantActionDescriptor> actions)
    {
        Id = node.Id;
        Kind = node.Kind;
        _title = node.Title;
        _value = node.Value ?? string.Empty;
        _delaySeconds = node.DelaySeconds == 0 ? 1 : node.DelaySeconds;
        _selectedCondition = Conditions.FirstOrDefault(option => option.Kind == node.Condition) ?? Conditions[0];
        AvailableActions = actions;
        if (node.ActionId is not null)
        {
            var action = actions.FirstOrDefault(action => action.Id == node.ActionId);
            if (action is null)
            {
                action = new(node.ActionId, "Недоступное действие", "Включите соответствующий плагин или выберите другое действие.", []);
                AvailableActions = [action, .. actions];
            }

            SelectedAction = action;
            foreach (var pair in node.Arguments ?? new Dictionary<string, string>())
            {
                var argument = Arguments.FirstOrDefault(argument => argument.Descriptor.Name == pair.Key);
                if (argument is null)
                {
                    argument = new(new(pair.Key, "Неизвестный аргумент: " + pair.Key), pair.Value);
                    Arguments.Add(argument);
                }
                else
                {
                    argument.Value = pair.Value;
                }
            }
        }

        if (node.Next is not null) { _next = new(node.Next.Value, "Удалённый блок"); }
        if (node.Otherwise is not null) { _otherwise = new(node.Otherwise.Value, "Удалённый блок"); }
    }

    public Guid Id { get; }
    public ScenarioNodeKind Kind { get; }
    public IReadOnlyList<AssistantActionDescriptor> AvailableActions { get; }
    public ObservableCollection<ActionArgumentViewModel> Arguments { get; } = [];
    public ObservableCollection<ScenarioNodeTarget> Targets { get; } = [];
    public IReadOnlyList<ScenarioConditionOption> Conditions { get; } =
    [
        new(ScenarioConditionKind.ApplicationConfigured, "Приложение есть в настройках"),
        new(ScenarioConditionKind.SiteConfigured, "Сайт есть в настройках"),
        new(ScenarioConditionKind.AiEnabled, "ИИ включён в настройках")
    ];
    public bool IsAction => Kind == ScenarioNodeKind.Action;
    public bool IsDelay => Kind == ScenarioNodeKind.Delay;
    public bool IsCondition => Kind == ScenarioNodeKind.Condition;
    public bool IsLegacy => Kind == ScenarioNodeKind.LegacyCommand;
    public bool HasNext => Kind != ScenarioNodeKind.End;
    public bool CanRemove => Kind is not (ScenarioNodeKind.Start or ScenarioNodeKind.End);
    public bool NeedsConditionValue => IsCondition && SelectedCondition.Kind != ScenarioConditionKind.AiEnabled;
    public IReadOnlyList<string> ConditionSuggestions => AvailableActions
        .FirstOrDefault(action => action.Id == (SelectedCondition.Kind == ScenarioConditionKind.ApplicationConfigured ? "builtin.application_open" : "builtin.website_open"))
        ?.Parameters.FirstOrDefault()?.AllowedValues ?? [];
    public string KindTitle => Kind switch
    {
        ScenarioNodeKind.Start => "● Старт",
        ScenarioNodeKind.Action => "▣ Действие",
        ScenarioNodeKind.Delay => "◷ Пауза",
        ScenarioNodeKind.Condition => "◇ Условие",
        ScenarioNodeKind.End => "■ Завершение",
        _ => "▤ Текстовая команда"
    };
    public string Color => IsCondition ? "#403052" : IsDelay ? "#253D45" : "#292444";
    public string Label => $"{_number}. {Title}";
    public string Connections => IsCondition ? $"Да → {Next?.Title ?? "?"}\nНет → {Otherwise?.Title ?? "?"}" : HasNext ? $"↓ {Next?.Title ?? "Выберите переход"}" : "Конец";
    public int Number { get => _number; set { SetProperty(ref _number, value); RaisePropertyChanged(nameof(Label)); } }
    public string Title { get => _title; set { SetProperty(ref _title, value); RaisePropertyChanged(nameof(Label)); } }
    public string Value { get => _value; set => SetProperty(ref _value, value); }
    public decimal DelaySeconds { get => _delaySeconds; set => SetProperty(ref _delaySeconds, value); }
    public bool IsExecuting { get => _isExecuting; set => SetProperty(ref _isExecuting, value); }
    public ScenarioNodeTarget? Next { get => _next; set { SetProperty(ref _next, value); RaisePropertyChanged(nameof(Connections)); } }
    public ScenarioNodeTarget? Otherwise { get => _otherwise; set { SetProperty(ref _otherwise, value); RaisePropertyChanged(nameof(Connections)); } }
    public ScenarioConditionOption SelectedCondition
    {
        get => _selectedCondition;
        set { if (value is not null) { SetProperty(ref _selectedCondition, value); RaisePropertyChanged(nameof(NeedsConditionValue)); RaisePropertyChanged(nameof(ConditionSuggestions)); } }
    }

    public AssistantActionDescriptor? SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (value is null || !SetProperty(ref _selectedAction, value)) { return; }
            Arguments.Clear();
            foreach (var parameter in value.Parameters) { Arguments.Add(new(parameter)); }
            if (string.IsNullOrWhiteSpace(Title) || Title == "Действие") { Title = value.Title; }
        }
    }

    public ScenarioNode ToNode() => new(Id, Kind, Title,
        HasNext ? Next?.Id : null, IsCondition ? Otherwise?.Id : null,
        IsAction ? SelectedAction?.Id : null,
        IsAction ? Arguments.Where(argument => argument.Value.Length > 0 || argument.Descriptor.IsRequired)
            .ToDictionary(argument => argument.Descriptor.Name, argument => argument.Value, StringComparer.Ordinal) : null,
        IsDelay && DelaySeconds == decimal.Truncate(DelaySeconds) && DelaySeconds is >= 1 and <= 30 ? decimal.ToInt32(DelaySeconds) : 0,
        SelectedCondition.Kind, IsCondition || IsLegacy ? Value : string.Empty);

    public void RefreshTargets(IReadOnlyList<ScenarioBlockViewModel> blocks)
    {
        var nextId = Next?.Id;
        var otherwiseId = Otherwise?.Id;
        Targets.Clear();
        foreach (var block in blocks.Where(block => block.Kind != ScenarioNodeKind.Start && block.Id != Id))
        {
            Targets.Add(new(block.Id, block.Label));
        }

        ScenarioNodeTarget? Resolve(Guid? id)
        {
            if (id is null) { return null; }
            var target = Targets.FirstOrDefault(target => target.Id == id);
            if (target is null) { target = new(id.Value, "Недоступный переход"); Targets.Add(target); }
            return target;
        }

        Next = Resolve(nextId);
        Otherwise = Resolve(otherwiseId);
    }
}
