namespace Egorik.Core.Speech;

public sealed record AudioInputDevice(string Id, string Name)
{
    public static AudioInputDevice SystemDefault { get; } = new(string.Empty, "По умолчанию Windows");
}

public interface IAudioInputDeviceService
{
    ValueTask<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(CancellationToken cancellationToken = default);
}

public interface IWakeWordDetector : IAsyncDisposable
{
    bool IsAvailable { get; }

    event EventHandler? WakeWordDetected;

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public interface ISpeechRecognizer
{
    bool IsAvailable { get; }

    ValueTask<string?> RecognizeOnceAsync(CancellationToken cancellationToken = default);
}

public interface ISpeechSynthesizer
{
    bool IsAvailable { get; }

    string VoiceName { get; }

    ValueTask SpeakAsync(string text, CancellationToken cancellationToken = default);
}

public enum VoiceInputState
{
    Disabled,
    ModelMissing,
    Starting,
    WaitingForWakeWord,
    ListeningForCommand,
    Error,
    Suppressed
}

public sealed record VoiceInputStateChangedEventArgs(
    VoiceInputState State,
    string Message);

public sealed record VoiceCommandRecognizedEventArgs(string Text);

public sealed record VoiceInputActivityEventArgs(
    double Level,
    string PartialText);

public interface IVoiceCommandSource : IAsyncDisposable
{
    bool IsAvailable { get; }

    VoiceInputState State { get; }

    event EventHandler<VoiceInputStateChangedEventArgs>? StateChanged;

    event EventHandler<VoiceCommandRecognizedEventArgs>? CommandRecognized;

    event EventHandler<VoiceInputActivityEventArgs>? ActivityChanged;

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask PauseAsync(CancellationToken cancellationToken = default);

    ValueTask ResumeAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public interface IVoiceModelService
{
    bool IsInstalled { get; }

    string ModelDirectory { get; }

    ValueTask DownloadAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface INeuralVoiceModelService
{
    bool IsInstalled { get; }

    string ModelDirectory { get; }

    ValueTask DownloadAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
