using System.Text.Json;
using Egorik.Core.Scenarios;

namespace Egorik.Infrastructure.Persistence;

public sealed class JsonScenarioRepository(ApplicationDataPaths paths) : IScenarioRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async ValueTask<IReadOnlyList<AssistantScenario>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.ScenariosFile))
        {
            return [];
        }

        await using var stream = File.OpenRead(paths.ScenariosFile);
        return await JsonSerializer.DeserializeAsync<List<AssistantScenario>>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? [];
    }

    public async ValueTask SaveAsync(
        IReadOnlyCollection<AssistantScenario> scenarios,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.RootDirectory);
        var temporaryFile = paths.ScenariosFile + ".tmp";

        await using (var stream = File.Create(temporaryFile))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                scenarios,
                JsonOptions,
                cancellationToken);
        }

        File.Move(temporaryFile, paths.ScenariosFile, true);
    }
}
