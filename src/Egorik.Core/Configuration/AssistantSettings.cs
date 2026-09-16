namespace Egorik.Core.Configuration;

public sealed class AssistantSettings
{
    public string AssistantName { get; set; } = "Егорик";

    public bool OnboardingCompleted { get; set; }

    public bool SpeakResponses { get; set; } = true;

    public bool VoiceActivationEnabled { get; set; }

    public string SearchProvider { get; set; } = "Yandex";

    public bool AiEnabled { get; set; }

    public string AiEndpoint { get; set; } =
        "https://api.openai.com/v1/chat/completions";

    public string AiModel { get; set; } = "gpt-4o-mini";

    public Dictionary<string, string> Applications { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Sites { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Documents { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> EnabledPlugins { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, string> TrustedPluginFingerprints { get; set; } =
        new(StringComparer.Ordinal);
}

public interface IAssistantSettingsService
{
    AssistantSettings Current { get; }

    ValueTask LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(CancellationToken cancellationToken = default);
}
