using Egorik.Core.Scenarios;

namespace Egorik.App.ViewModels;

public sealed class ScenarioItemViewModel(AssistantScenario scenario)
{
    public AssistantScenario Model { get; } = scenario;

    public string Name => Model.Name;

    public string Trigger => $"«{Model.Trigger}»";

    public string Summary => Model.Flow is null ? $"{Model.Commands.Count} команд" : $"{Model.Flow.Nodes.Count} блоков";
}
