namespace Egorik.Infrastructure.Persistence;

public sealed class ApplicationDataPaths
{
    public ApplicationDataPaths(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Egorik");
    }

    public string RootDirectory { get; }

    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public string ScenariosFile => Path.Combine(RootDirectory, "scenarios.json");

    public string StartupLogFile => Path.Combine(RootDirectory, "logs", "startup.log");

    public string VoiceLogFile => Path.Combine(RootDirectory, "logs", "voice.log");

    public string AiApiKeyFile => Path.Combine(RootDirectory, "ai-api-key.dat");

    public string GeneratedDocumentsDirectory => Path.Combine(RootDirectory, "documents");

    public string PluginsDirectory => Path.Combine(RootDirectory, "plugins");
}
