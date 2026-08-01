using System.Text.Json;
using Egorik.Core.Configuration;
using Egorik.Core.Speech;
using Egorik.Infrastructure.Persistence;
using NAudio.Wave;
using Vosk;

namespace Egorik.Infrastructure.Speech;

public sealed class VoskVoiceCommandSource(
    IVoiceModelService modelService,
    IAssistantSettingsService settings,
    ApplicationDataPaths paths) : IVoiceCommandSource, IDisposable
{
    private static readonly TimeSpan CommandWindow = TimeSpan.FromSeconds(8);
    private readonly Lock _syncRoot = new();
    private WaveInEvent? _waveInput;
    private Model? _model;
    private VoskRecognizer? _recognizer;
    private DateTimeOffset _armedUntil;
    private DateTimeOffset _lastActivityPublishedAt;
    private string _lastPartialText = string.Empty;
    private int _startVersion;
    private int _armVersion;
    private bool _hasReceivedAudio;
    private bool _disposed;

    public bool IsAvailable => OperatingSystem.IsWindows() && modelService.IsInstalled;

    public VoiceInputState State { get; private set; } =
        modelService.IsInstalled
            ? VoiceInputState.Disabled
            : VoiceInputState.ModelMissing;

    public event EventHandler<VoiceInputStateChangedEventArgs>? StateChanged;

    public event EventHandler<VoiceCommandRecognizedEventArgs>? CommandRecognized;

    public event EventHandler<VoiceInputActivityEventArgs>? ActivityChanged;

    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            SetState(VoiceInputState.Error, "Голосовой ввод пока реализован только для Windows.");
            return ValueTask.CompletedTask;
        }

        if (!modelService.IsInstalled)
        {
            SetState(VoiceInputState.ModelMissing, "Сначала загрузи русскую голосовую модель.");
            return ValueTask.CompletedTask;
        }

        lock (_syncRoot)
        {
            if (_waveInput is not null)
            {
                return ValueTask.CompletedTask;
            }

            SetState(VoiceInputState.Starting, "Подключаю микрофон и проверяю аудиопоток…");
            try
            {
                if (WaveInEvent.DeviceCount == 0)
                {
                    throw new InvalidOperationException(
                        "Windows не обнаружила ни одного устройства записи.");
                }

                Vosk.Vosk.SetLogLevel(-1);
                if (_model is null)
                {
                    _model = new Model(modelService.ModelDirectory);
                }

                if (_recognizer is null)
                {
                    _recognizer = new VoskRecognizer(_model, 16000);
                    _recognizer.SetWords(false);
                }
                else
                {
                    _recognizer.Reset();
                }

                _hasReceivedAudio = false;
                _lastPartialText = string.Empty;
                _lastActivityPublishedAt = DateTimeOffset.MinValue;
                var startVersion = ++_startVersion;

                _waveInput = new WaveInEvent
                {
                    DeviceNumber = 0,
                    WaveFormat = new WaveFormat(16000, 16, 1),
                    BufferMilliseconds = 80,
                    NumberOfBuffers = 4
                };
                _waveInput.DataAvailable += OnDataAvailable;
                _waveInput.RecordingStopped += OnRecordingStopped;
                _waveInput.StartRecording();

                var deviceName = WaveInEvent.GetCapabilities(0).ProductName;
                WriteDiagnostic($"Микрофон открыт: {deviceName}.");
                _ = WatchAudioInputAsync(startVersion);
            }
            catch (Exception exception)
            {
                DisposeRecognitionObjects();
                SetState(VoiceInputState.Error, $"Не удалось открыть микрофон: {exception.Message}");
                WriteDiagnostic($"Ошибка запуска микрофона: {exception}");
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            ++_startVersion;
            ++_armVersion;
            DisposeWaveInput();
        }

        ActivityChanged?.Invoke(this, new VoiceInputActivityEventArgs(0, string.Empty));
        SetState(
            modelService.IsInstalled
                ? VoiceInputState.Disabled
                : VoiceInputState.ModelMissing,
            "Голосовой ввод временно приостановлен.");
        return ValueTask.CompletedTask;
    }

    public ValueTask ResumeAsync(CancellationToken cancellationToken = default) =>
        StartAsync(cancellationToken);

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            ++_startVersion;
            ++_armVersion;

            DisposeRecognitionObjects();
        }

        ActivityChanged?.Invoke(this, new VoiceInputActivityEventArgs(0, string.Empty));
        SetState(
            modelService.IsInstalled
                ? VoiceInputState.Disabled
                : VoiceInputState.ModelMissing,
            "Голосовая активация выключена.");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync();
        _disposed = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAsync().AsTask().GetAwaiter().GetResult();
        _disposed = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        string? finalText = null;
        string partialText = string.Empty;
        var becameReady = false;
        var level = CalculateLevel(eventArgs.Buffer, eventArgs.BytesRecorded);

        lock (_syncRoot)
        {
            if (_recognizer is null)
            {
                return;
            }

            try
            {
                if (!_hasReceivedAudio)
                {
                    _hasReceivedAudio = true;
                    becameReady = true;
                    WriteDiagnostic("Получен первый аудиобуфер; захват микрофона работает.");
                }

                if (_recognizer.AcceptWaveform(eventArgs.Buffer, eventArgs.BytesRecorded))
                {
                    finalText = ReadText(_recognizer.Result(), "text");
                    _lastPartialText = string.Empty;
                }
                else
                {
                    partialText = ReadText(_recognizer.PartialResult(), "partial") ?? string.Empty;
                }
            }
            catch (Exception exception)
            {
                SetState(VoiceInputState.Error, $"Ошибка распознавания: {exception.Message}");
                WriteDiagnostic($"Ошибка распознавания: {exception}");
            }
        }

        if (becameReady)
        {
            SetWaitingState();
        }

        PublishActivity(level, partialText);

        if (!string.IsNullOrWhiteSpace(finalText))
        {
            HandleRecognizedText(finalText);
        }
    }

    private void HandleRecognizedText(string text)
    {
        var match = WakePhraseParser.Parse(text, settings.Current.AssistantName);
        var normalized = match.RecognizedText;
        if (normalized.Length == 0)
        {
            return;
        }

        ActivityChanged?.Invoke(this, new VoiceInputActivityEventArgs(0, normalized));

        var now = DateTimeOffset.Now;

        if (match.Kind == WakePhraseKind.WakeOnly)
        {
            ArmForCommand(
                match.IsApproximate
                    ? $"Расслышал обращение как «{match.MatchedWakeText}». "
                    : string.Empty);
            return;
        }

        if (match.Kind == WakePhraseKind.Command)
        {
            var command = match.Command;
            if (command.Length > 0)
            {
                Disarm();
                CommandRecognized?.Invoke(this, new VoiceCommandRecognizedEventArgs(command));
                var wakeDetails = match.IsApproximate
                    ? $"Имя расслышано как «{match.MatchedWakeText}». "
                    : string.Empty;
                SetWaitingState(
                    $"{wakeDetails}Команда распознана: «{command}».");
            }

            return;
        }

        if (State == VoiceInputState.ListeningForCommand)
        {
            if (now <= _armedUntil)
            {
                Disarm();
                CommandRecognized?.Invoke(this, new VoiceCommandRecognizedEventArgs(normalized));
                SetWaitingState($"Команда распознана: «{normalized}».");
                return;
            }

            Disarm();
        }

        SetWaitingState(
            $"Слышал: «{normalized}». Для команды сначала скажи «{settings.Current.AssistantName}».");
    }

    private void ArmForCommand(string prefix)
    {
        _armedUntil = DateTimeOffset.Now.Add(CommandWindow);
        var armVersion = ++_armVersion;
        SetState(
            VoiceInputState.ListeningForCommand,
            $"{prefix}Слушаю команду…");
        _ = WatchCommandWindowAsync(armVersion);
    }

    private void Disarm()
    {
        _armedUntil = DateTimeOffset.MinValue;
        ++_armVersion;
    }

    private async Task WatchAudioInputAsync(int startVersion)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));

        lock (_syncRoot)
        {
            if (startVersion != _startVersion || _waveInput is null || _hasReceivedAudio)
            {
                return;
            }
        }

        SetState(
            VoiceInputState.Error,
            "Микрофон открылся, но звук от него не поступает. Проверь доступ к микрофону в Windows.");
        WriteDiagnostic("За 3 секунды не получено ни одного аудиобуфера.");
    }

    private async Task WatchCommandWindowAsync(int armVersion)
    {
        await Task.Delay(CommandWindow);
        if (armVersion != _armVersion || DateTimeOffset.Now < _armedUntil)
        {
            return;
        }

        Disarm();
        SetWaitingState("Не расслышал команду. Позови меня ещё раз.");
    }

    private void PublishActivity(double level, string partialText)
    {
        var now = DateTimeOffset.Now;
        var partialChanged = !string.Equals(
            partialText,
            _lastPartialText,
            StringComparison.Ordinal);

        if (!partialChanged && now - _lastActivityPublishedAt < TimeSpan.FromMilliseconds(100))
        {
            return;
        }

        _lastActivityPublishedAt = now;
        _lastPartialText = partialText;
        ActivityChanged?.Invoke(
            this,
            new VoiceInputActivityEventArgs(level, partialText));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is not null)
        {
            SetState(VoiceInputState.Error, $"Микрофон остановлен: {eventArgs.Exception.Message}");
            WriteDiagnostic($"Микрофон остановлен с ошибкой: {eventArgs.Exception}");
        }
    }

    private void SetWaitingState(string? prefix = null)
    {
        var waiting = $"Жду обращения «{settings.Current.AssistantName}».";
        SetState(
            VoiceInputState.WaitingForWakeWord,
            string.IsNullOrWhiteSpace(prefix) ? waiting : $"{prefix} {waiting}");
    }

    private static string? ReadText(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var text)
            ? text.GetString()
            : null;
    }

    private static double CalculateLevel(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded < 2)
        {
            return 0;
        }

        double sum = 0;
        var sampleCount = bytesRecorded / 2;
        for (var index = 0; index < bytesRecorded - 1; index += 2)
        {
            var sample = (short)(buffer[index] | (buffer[index + 1] << 8));
            var normalized = sample / 32768d;
            sum += normalized * normalized;
        }

        var rms = Math.Sqrt(sum / sampleCount);
        return Math.Clamp(rms * 7, 0, 1);
    }

    private void SetState(VoiceInputState state, string message)
    {
        State = state;
        StateChanged?.Invoke(this, new VoiceInputStateChangedEventArgs(state, message));
    }

    private void WriteDiagnostic(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.VoiceLogFile)!);
            File.AppendAllText(
                paths.VoiceLogFile,
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Диагностика не должна останавливать голосовой контур.
        }
    }

    private void DisposeRecognitionObjects()
    {
        try
        {
            DisposeWaveInput();
        }
        finally
        {
            try
            {
                _recognizer?.Dispose();
            }
            finally
            {
                _model?.Dispose();
                _recognizer = null;
                _model = null;
                _hasReceivedAudio = false;
            }
        }
    }

    private void DisposeWaveInput()
    {
        if (_waveInput is null)
        {
            return;
        }

        var waveInput = _waveInput;
        _waveInput = null;
        waveInput.DataAvailable -= OnDataAvailable;
        waveInput.RecordingStopped -= OnRecordingStopped;
        try
        {
            waveInput.StopRecording();
        }
        finally
        {
            waveInput.Dispose();
            _hasReceivedAudio = false;
        }
    }
}
