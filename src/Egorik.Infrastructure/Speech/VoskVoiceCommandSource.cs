using System.Diagnostics;
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
    IWaveInputDeviceCatalog inputDevices,
    ApplicationDataPaths paths) : IVoiceCommandSource, IDisposable
{
    private static readonly TimeSpan CommandWindow = TimeSpan.FromSeconds(8);
    private readonly Lock _syncRoot = new();
    private readonly SpeechPhraseBuffer _phraseBuffer = new();
    private readonly VoiceSuppression _suppression = new();
    private readonly RecognitionStreamBudget _streamBudget = new();
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
    private string _activeDeviceId = string.Empty;
    private string _activeDeviceName = string.Empty;

    public bool IsAvailable => OperatingSystem.IsWindows() && modelService.IsInstalled;

    public VoiceInputState State { get; private set; } =
        modelService.IsInstalled
            ? VoiceInputState.Disabled
            : VoiceInputState.ModelMissing;

    public event EventHandler<VoiceInputStateChangedEventArgs>? StateChanged;

    public event EventHandler<VoiceCommandRecognizedEventArgs>? CommandRecognized;

    public event EventHandler<VoiceInputActivityEventArgs>? ActivityChanged;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default) =>
        await Task.Run(() => StartCore(cancellationToken), cancellationToken);

    private void StartCore(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            SetState(VoiceInputState.Error, "Голосовой ввод пока реализован только для Windows.");
            return;
        }

        if (!modelService.IsInstalled)
        {
            SetState(VoiceInputState.ModelMissing, "Сначала загрузи русскую голосовую модель.");
            return;
        }

        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var requestedDeviceId = settings.Current.AudioInputDeviceId;
            if (_waveInput is not null && State != VoiceInputState.Error &&
                string.Equals(_activeDeviceId, requestedDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ++_startVersion;
            Disarm();
            DisposeWaveInput();

            SetState(VoiceInputState.Starting, "Подключаю микрофон и проверяю аудиопоток…");
            try
            {
                var selectedDevice = WaveInputDeviceSelector.Select(requestedDeviceId, inputDevices.GetWaveDevices(cancellationToken));

                Vosk.Vosk.SetLogLevel(-1);
                if (_model is null)
                {
                    _model = new Model(modelService.ModelDirectory);
                }

                if (_recognizer is null)
                {
                    _recognizer = new VoskRecognizer(_model, 16000);
                    _recognizer.SetWords(false);
                    _streamBudget.Reset();
                }
                else
                {
                    _recognizer.Reset();
                }

                _hasReceivedAudio = false;
                _phraseBuffer.Reset();
                _lastPartialText = string.Empty;
                _lastActivityPublishedAt = DateTimeOffset.MinValue;
                var startVersion = ++_startVersion;
                cancellationToken.ThrowIfCancellationRequested();
                _activeDeviceId = selectedDevice.Id;
                _activeDeviceName = selectedDevice.Name;

                _waveInput = new WaveInEvent
                {
                    DeviceNumber = selectedDevice.DeviceNumber,
                    WaveFormat = new WaveFormat(16000, 16, 1),
                    BufferMilliseconds = 80,
                    NumberOfBuffers = 4
                };
                _waveInput.DataAvailable += OnDataAvailable;
                _waveInput.RecordingStopped += OnRecordingStopped;
                _waveInput.StartRecording();

                WriteDiagnostic($"Микрофон открыт: {_activeDeviceName}.");
                _ = WatchAudioInputAsync(startVersion);
            }
            catch (OperationCanceledException)
            {
                DisposeRecognitionObjects();
                SetState(VoiceInputState.Disabled, "Подключение микрофона отменено.");
                throw;
            }
            catch (Exception exception)
            {
                DisposeRecognitionObjects();
                SetState(VoiceInputState.Error, $"Не удалось открыть микрофон: {exception.Message}");
                WriteDiagnostic($"Ошибка запуска микрофона: {exception}");
            }
        }
    }

    public ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            ++_startVersion;
            ++_armVersion;
            _phraseBuffer.Reset();
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
            _phraseBuffer.Reset();
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
        string? phrase = null;
        string partialText = string.Empty;
        var becameReady = false;
        var abandonedPhrase = false;
        var level = CalculateLevel(eventArgs.Buffer, eventArgs.BytesRecorded);

        lock (_syncRoot)
        {
            if (_recognizer is null || !ReferenceEquals(sender, _waveInput))
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

                if (_suppression.IsSuppressed(Stopwatch.GetElapsedTime(0)))
                {
                    if (State != VoiceInputState.Suppressed)
                    {
                        SetState(VoiceInputState.Suppressed, "Отбой. Ожидаю окончания паузы.");
                    }

                    return;
                }

                if (State == VoiceInputState.Suppressed)
                {
                    SetWaitingState();
                }

                var phraseFinalized = _recognizer.AcceptWaveform(eventArgs.Buffer, eventArgs.BytesRecorded);
                var recognizedText = phraseFinalized
                    ? ReadText(_recognizer.Result(), "text") ?? string.Empty
                    : ReadText(_recognizer.PartialResult(), "partial") ?? string.Empty;
                if (_suppression.Observe(recognizedText, Stopwatch.GetElapsedTime(0)))
                {
                    Disarm();
                    _phraseBuffer.Reset();
                    _recognizer.Reset();
                    SetState(VoiceInputState.Suppressed, "Отбой. Не реагирую на обращения 10 секунд.");
                    ActivityChanged?.Invoke(this, new VoiceInputActivityEventArgs(0, string.Empty));
                    return;
                }

                if (phraseFinalized)
                {
                    var finalText = recognizedText;
                    if (_phraseBuffer.HasFinalizedText &&
                        WakePhraseParser.Parse(finalText, settings.Current.AssistantName).Kind != WakePhraseKind.NotAddressed)
                    {
                        // A fresh address starts a phrase instead of attaching it to background speech.
                        _phraseBuffer.Reset();
                    }

                    _phraseBuffer.AddFinal(
                        finalText,
                        Stopwatch.GetElapsedTime(0));
                    _lastPartialText = string.Empty;
                }
                else
                {
                    partialText = recognizedText;
                    _phraseBuffer.UpdatePartial(partialText, Stopwatch.GetElapsedTime(0));
                }

                partialText = _phraseBuffer.PreviewText;
                phrase = _phraseBuffer.TakeReadyPhrase(Stopwatch.GetElapsedTime(0));
                var reset = _streamBudget.Advance(eventArgs.BytesRecorded, phraseFinalized);
                if (reset != RecognitionStreamReset.None)
                {
                    // Reuse the loaded model, but release all native decoder/frontend history.
                    // Reset() alone keeps that history in Vosk.
                    _recognizer.Dispose();
                    _recognizer = null;
                    _recognizer = new VoskRecognizer(_model!, 16000);
                    _recognizer.SetWords(false);
                    if (reset == RecognitionStreamReset.AbandonOverlongPhrase)
                    {
                        abandonedPhrase = partialText.Length > 0 || State == VoiceInputState.ListeningForCommand;
                        _phraseBuffer.Reset();
                        Disarm();
                        partialText = string.Empty;
                        phrase = null;
                    }
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

        if (abandonedPhrase)
        {
            SetWaitingState("Слишком длинный непрерывный звук. Позови меня ещё раз.");
        }

        PublishActivity(level, partialText);

        if (!string.IsNullOrWhiteSpace(phrase))
        {
            HandleRecognizedText(phrase);
        }
    }

    private void HandleRecognizedText(string text)
    {
        if (_suppression.IsSuppressed(Stopwatch.GetElapsedTime(0)))
        {
            return;
        }

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
        if (!ReferenceEquals(sender, _waveInput))
        {
            return;
        }

        if (eventArgs.Exception is not null)
        {
            SetState(VoiceInputState.Error, $"Микрофон остановлен: {eventArgs.Exception.Message}");
            WriteDiagnostic($"Микрофон остановлен с ошибкой: {eventArgs.Exception}");
        }
    }

    private void SetWaitingState(string? prefix = null)
    {
        var waiting = $"Микрофон: {_activeDeviceName}. Жду обращения «{settings.Current.AssistantName}».";
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
                _phraseBuffer.Reset();
                _streamBudget.Reset();
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
