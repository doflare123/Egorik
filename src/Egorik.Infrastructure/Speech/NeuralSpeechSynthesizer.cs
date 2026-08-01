using Egorik.Core.Speech;
using NAudio.Wave;
using SherpaOnnx;

namespace Egorik.Infrastructure.Speech;

public sealed class NeuralSpeechSynthesizer(
    INeuralVoiceModelService modelService,
    WindowsSpeechSynthesizer fallback) : ISpeechSynthesizer, IDisposable
{
    private readonly SemaphoreSlim _speechLock = new(1, 1);
    private OfflineTts? _tts;
    private bool _disposed;

    public bool IsAvailable => modelService.IsInstalled || fallback.IsAvailable;

    public string VoiceName =>
        modelService.IsInstalled
            ? "Локальный нейроголос Denis"
            : fallback.VoiceName;

    public async ValueTask SpeakAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await _speechLock.WaitAsync(cancellationToken);
        try
        {
            if (modelService.IsInstalled &&
                await TrySpeakNeuralAsync(text, cancellationToken))
            {
                return;
            }

            await fallback.SpeakAsync(text, cancellationToken);
        }
        finally
        {
            ReleaseNeuralResources();
            _speechLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _speechLock.Wait();
        try
        {
            ReleaseNeuralResources();
            _disposed = true;
        }
        finally
        {
            _speechLock.Release();
            _speechLock.Dispose();
        }
    }

    private async Task<bool> TrySpeakNeuralAsync(
        string text,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"egorik-tts-{Guid.NewGuid():N}.wav");

        try
        {
            var generated = await Task.Run(
                () =>
                {
                    var tts = GetOrCreateTts();
                    var generation = new OfflineTtsGenerationConfig
                    {
                        Sid = 0,
                        Speed = 1.04f,
                        SilenceScale = 0.18f
                    };

                    var audio = tts.GenerateWithConfig(text, generation, null);
                    return audio.SaveToWaveFile(outputPath);
                },
                cancellationToken);

            if (!generated)
            {
                return false;
            }

            await PlayWaveFileAsync(outputPath, cancellationToken);
            return true;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private OfflineTts GetOrCreateTts()
    {
        if (_tts is not null)
        {
            return _tts;
        }

        var config = new OfflineTtsConfig();
        config.Model.Vits.Model = Path.Combine(
            modelService.ModelDirectory,
            "ru_RU-denis-medium.onnx");
        config.Model.Vits.Tokens = Path.Combine(
            modelService.ModelDirectory,
            "tokens.txt");
        config.Model.Vits.DataDir = Path.Combine(
            modelService.ModelDirectory,
            "espeak-ng-data");
        config.Model.Vits.NoiseScale = 0.62f;
        config.Model.Vits.NoiseScaleW = 0.78f;
        config.Model.Vits.LengthScale = 0.96f;
        config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        config.Model.Provider = "cpu";
        config.MaxNumSentences = 1;

        _tts = new OfflineTts(config);
        return _tts;
    }

    private void ReleaseNeuralResources()
    {
        _tts?.Dispose();
        _tts = null;
    }

    private static async Task PlayWaveFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using var reader = new WaveFileReader(path);
        using var output = new WaveOutEvent();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        output.PlaybackStopped += OnPlaybackStopped;
        using var cancellationRegistration = cancellationToken.Register(output.Stop);

        try
        {
            output.Init(reader);
            output.Play();
            await completion.Task;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            output.PlaybackStopped -= OnPlaybackStopped;
        }

        void OnPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
        {
            if (eventArgs.Exception is not null)
            {
                completion.TrySetException(eventArgs.Exception);
            }
            else
            {
                completion.TrySetResult();
            }
        }
    }
}
