using System.Diagnostics;
using System.Runtime.InteropServices;
using Egorik.Core.Platform;
using NAudio.CoreAudioApi;

namespace Egorik.Infrastructure.Platform;

public sealed class DesktopYandexMusicVolumeService(IInstalledApplicationLocator applicationLocator)
    : IYandexMusicVolumeService
{
    public ValueTask<LaunchResult> AdjustAsync(YandexMusicAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return action is YandexMusicAction.VolumeUp or YandexMusicAction.VolumeDown or
            YandexMusicAction.Mute or YandexMusicAction.Unmute
            ? RunAsync(action, null, cancellationToken)
            : ValueTask.FromResult(new LaunchResult(false, "Неизвестная команда громкости Яндекс Музыки."));
    }

    public ValueTask<LaunchResult> SetAsync(int percent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return percent is >= 0 and <= 100
            ? RunAsync(null, percent, cancellationToken)
            : ValueTask.FromResult(new LaunchResult(false, "Громкость должна быть от 0 до 100 процентов."));
    }

    private async ValueTask<LaunchResult> RunAsync(YandexMusicAction? action, int? percent, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new LaunchResult(false, "Громкость приложения пока поддерживается только в Windows.");
        }

        return await Task.Run(() => ChangeVolume(action, percent, cancellationToken), cancellationToken);
    }

    private LaunchResult ChangeVolume(YandexMusicAction? action, int? percent, CancellationToken cancellationToken)
    {
        try
        {
            var executable = applicationLocator.FindExecutable(DesktopYandexMusicController.ExecutableName);
            if (executable is null)
            {
                return new LaunchResult(false, "Не найдена установленная Яндекс Музыка.");
            }

            using var enumerator = new MMDeviceEnumerator();
            var changed = 0;
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (var index = 0; index < sessions.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var session = sessions[index];
                        if (!BelongsToYandexMusic(session.GetProcessID, executable))
                        {
                            continue;
                        }

                        using var volume = session.SimpleAudioVolume;
                        if (percent is { } value)
                        {
                            volume.Volume = value / 100f;
                        }
                        else if (action is YandexMusicAction.VolumeUp or YandexMusicAction.VolumeDown)
                        {
                            volume.Volume = Math.Clamp(volume.Volume + (action == YandexMusicAction.VolumeUp ? 0.1f : -0.1f), 0, 1);
                        }

                        if (percent > 0 || action is YandexMusicAction.VolumeUp or YandexMusicAction.Unmute)
                        {
                            volume.Mute = false;
                        }
                        else if (action == YandexMusicAction.Mute)
                        {
                            volume.Mute = true;
                        }

                        changed++;
                    }
                }
            }

            return changed > 0
                ? new LaunchResult(true, percent is { } level
                    ? $"Громкость Яндекс Музыки — {level} процентов."
                    : "Громкость изменена только у Яндекс Музыки.")
                : new LaunchResult(false, "Нет аудиосессии Яндекс Музыки. Включи трек и повтори команду.");
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or
            UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new LaunchResult(false, $"Не удалось изменить громкость Яндекс Музыки: {exception.Message}");
        }
    }

    private static bool BelongsToYandexMusic(uint processId, string executable)
    {
        if (processId == 0 || processId > int.MaxValue)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            // A process may exit between session enumeration and inspecting its executable.
            return false;
        }
    }
}
