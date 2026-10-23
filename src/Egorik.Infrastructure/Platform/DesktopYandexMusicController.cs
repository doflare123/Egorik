using System.Runtime.Versioning;
using Egorik.Core.Platform;
using Windows.Media.Control;

namespace Egorik.Infrastructure.Platform;

public sealed class DesktopYandexMusicController(
    IInstalledApplicationLocator applicationLocator,
    IYandexMusicVolumeService volumeService,
    IYandexMusicWindowService windowService,
    IApplicationLauncher applicationLauncher) : IYandexMusicController
{
    public const string ExecutableName = "Яндекс Музыка.exe";

    public ValueTask<LaunchResult> SetVolumeAsync(int percent, CancellationToken cancellationToken = default) =>
        volumeService.SetAsync(percent, cancellationToken);

    public ValueTask<LaunchResult> OpenAsync(
        CancellationToken cancellationToken = default) =>
        LaunchAsync(null, cancellationToken);

    public ValueTask<LaunchResult> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200)
        {
            return ValueTask.FromResult(new LaunchResult(
                false,
                "Поисковый запрос Яндекс Музыки пуст или слишком длинный."));
        }

        return LaunchAsync(
            $"yandexmusic://search?text={Uri.EscapeDataString(query.Trim())}",
            cancellationToken);
    }

    public ValueTask<LaunchResult> StartMyWaveAsync(
        CancellationToken cancellationToken = default) =>
        LaunchAsync("yandexmusic://play-vibe/", cancellationToken);

    public async ValueTask<LaunchResult> ExecuteAsync(
        YandexMusicAction action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (action is YandexMusicAction.VolumeUp or YandexMusicAction.VolumeDown or
            YandexMusicAction.Mute or YandexMusicAction.Unmute)
        {
            return await volumeService.AdjustAsync(action, cancellationToken);
        }

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return new LaunchResult(
                false,
                "Адресное управление Яндекс Музыкой поддерживается в Windows 10 версии 1809 и новее.");
        }

        try
        {
            var session = await FindSessionAsync(cancellationToken);
            if (session is null)
            {
                var launch = await OpenAsync(cancellationToken);
                if (!launch.IsSuccess)
                {
                    return launch;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                session = await FindSessionAsync(cancellationToken);
            }

            if (session is null)
            {
                return new LaunchResult(
                    false,
                    "Яндекс Музыка запущена, но ещё не зарегистрировала медиасессию. Включи любой трек и повтори команду.");
            }

            var executed = await ExecuteSessionActionAsync(session, action);
            cancellationToken.ThrowIfCancellationRequested();
            return executed
                ? new LaunchResult(true, "Команда выполнена именно в приложении Яндекс Музыка.")
                : new LaunchResult(false, "Яндекс Музыка отклонила медиакоманду.");
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or InvalidOperationException or
                System.Runtime.InteropServices.COMException)
        {
            return new LaunchResult(
                false,
                $"Не удалось управлять медиасессией Яндекс Музыки: {exception.Message}");
        }
    }

    private async ValueTask<LaunchResult> LaunchAsync(
        string? deepLink,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return new LaunchResult(
                false,
                "Приложение Яндекс Музыка пока поддерживается только в Windows.");
        }

        if (deepLink is null && await windowService.TryShowAsync(cancellationToken) is { } shown)
        {
            return shown;
        }

        var executable = applicationLocator.FindExecutable(ExecutableName);
        if (executable is null)
        {
            return new LaunchResult(
                false,
                $"Не найден файл «{ExecutableName}». Ожидаемый каталог: " +
                @"%LocalAppData%\Programs\YandexMusic.");
        }

        // Let Windows dispatch deeplinks through the registered protocol handler.
        // Do not start another executable with a deeplink as a raw argument.
        return await applicationLauncher.LaunchAsync(deepLink ?? executable, cancellationToken);
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private static async Task<GlobalSystemMediaTransportControlsSession?> FindSessionAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return manager.GetSessions().FirstOrDefault(session =>
            IsYandexMusicSession(session.SourceAppUserModelId));
    }

    private static bool IsYandexMusicSession(string sourceAppUserModelId) =>
        sourceAppUserModelId.Contains("YandexMusic", StringComparison.OrdinalIgnoreCase) ||
        sourceAppUserModelId.Contains("Яндекс Музыка", StringComparison.OrdinalIgnoreCase);

    [SupportedOSPlatform("windows10.0.17763")]
    private static async Task<bool> ExecuteSessionActionAsync(
        GlobalSystemMediaTransportControlsSession session,
        YandexMusicAction action) =>
        action switch
        {
            YandexMusicAction.PlayPause => await session.TryTogglePlayPauseAsync(),
            YandexMusicAction.Next => await session.TrySkipNextAsync(),
            YandexMusicAction.Previous => await session.TrySkipPreviousAsync(),
            YandexMusicAction.Stop => await session.TryStopAsync(),
            _ => false
        };
}
