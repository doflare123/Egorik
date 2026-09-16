using System.Text.Json;
using Egorik.Core.Configuration;

namespace Egorik.Infrastructure.Persistence;

public sealed class JsonAssistantSettingsService(ApplicationDataPaths paths)
    : IAssistantSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AssistantSettings Current { get; private set; } = CreateDefaults();

    public async ValueTask LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.SettingsFile))
        {
            Current = CreateDefaults();
            return;
        }

        await using var stream = File.OpenRead(paths.SettingsFile);
        Current = await JsonSerializer.DeserializeAsync<AssistantSettings>(
                      stream,
                      JsonOptions,
                      cancellationToken)
                  ?? CreateDefaults();

        ApplyMissingDefaults(Current);
    }

    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.RootDirectory);
        var temporaryFile = paths.SettingsFile + ".tmp";

        await using (var stream = File.Create(temporaryFile))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                Current,
                JsonOptions,
                cancellationToken);
        }

        File.Move(temporaryFile, paths.SettingsFile, true);
    }

    private static AssistantSettings CreateDefaults()
    {
        var settings = new AssistantSettings();
        ApplyMissingDefaults(settings);
        return settings;
    }

    private static void ApplyMissingDefaults(AssistantSettings settings)
    {
        settings.AssistantName = string.IsNullOrWhiteSpace(settings.AssistantName)
            ? "Егорик"
            : settings.AssistantName.Trim();

        settings.AiEndpoint = string.IsNullOrWhiteSpace(settings.AiEndpoint)
            ? "https://api.openai.com/v1/chat/completions"
            : settings.AiEndpoint.Trim();
        settings.AiModel = string.IsNullOrWhiteSpace(settings.AiModel)
            ? "gpt-4o-mini"
            : settings.AiModel.Trim();

        settings.Applications = new Dictionary<string, string>(
            settings.Applications ?? [],
            StringComparer.OrdinalIgnoreCase);
        settings.Sites = new Dictionary<string, string>(
            settings.Sites ?? [],
            StringComparer.OrdinalIgnoreCase);
        settings.Documents = new Dictionary<string, string>(
            settings.Documents ?? [],
            StringComparer.OrdinalIgnoreCase);
        settings.EnabledPlugins = new HashSet<string>(
            settings.EnabledPlugins ?? [],
            StringComparer.Ordinal);
        settings.TrustedPluginFingerprints = new Dictionary<string, string>(
            settings.TrustedPluginFingerprints ?? [],
            StringComparer.Ordinal);

        foreach (var pair in DefaultCatalog.Applications())
        {
            settings.Applications.TryAdd(pair.Key, pair.Value);
        }

        foreach (var pair in DefaultCatalog.Sites())
        {
            settings.Sites.TryAdd(pair.Key, pair.Value);
        }
    }
}
