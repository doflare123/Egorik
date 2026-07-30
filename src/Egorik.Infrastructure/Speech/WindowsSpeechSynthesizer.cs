using System.Globalization;
using System.Runtime.Versioning;
using System.Speech.Synthesis;
using Egorik.Core.Speech;

namespace Egorik.Infrastructure.Speech;

public sealed class WindowsSpeechSynthesizer : ISpeechSynthesizer
{
    private readonly SemaphoreSlim _speechLock = new(1, 1);

    public bool IsAvailable => OperatingSystem.IsWindows();

    public string VoiceName => "Системный голос Windows";

    public async ValueTask SpeakAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await _speechLock.WaitAsync(cancellationToken);
        try
        {
            await SpeakWindowsAsync(text, cancellationToken);
        }
        catch (PlatformNotSupportedException)
        {
            // Системный TTS является необязательным fallback.
        }
        finally
        {
            _speechLock.Release();
        }
    }

    [SupportedOSPlatform("windows")]
    private static Task SpeakWindowsAsync(string text, CancellationToken cancellationToken) =>
        Task.Run(() => SpeakWindows(text), cancellationToken);

    [SupportedOSPlatform("windows")]
    private static void SpeakWindows(string text)
    {
        using var synthesizer = new SpeechSynthesizer();
        var russianVoice = synthesizer
            .GetInstalledVoices(CultureInfo.GetCultureInfo("ru-RU"))
            .FirstOrDefault(voice => voice.Enabled);

        if (russianVoice is not null)
        {
            synthesizer.SelectVoice(russianVoice.VoiceInfo.Name);
        }

        synthesizer.SetOutputToDefaultAudioDevice();
        synthesizer.Speak(text);
    }
}
