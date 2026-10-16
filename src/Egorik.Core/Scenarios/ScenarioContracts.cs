namespace Egorik.Core.Scenarios;

public sealed class AssistantScenario
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Trigger { get; set; } = string.Empty;

    public List<string> Commands { get; set; } = [];

    public ScenarioFlow? Flow { get; set; }

    public bool Enabled { get; set; } = true;
}

public interface IScenarioRepository
{
    ValueTask<IReadOnlyList<AssistantScenario>> GetAllAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        IReadOnlyCollection<AssistantScenario> scenarios,
        CancellationToken cancellationToken = default);
}
