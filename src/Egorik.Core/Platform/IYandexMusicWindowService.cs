namespace Egorik.Core.Platform;

public interface IYandexMusicWindowService
{
    // null means the application is not running and may be launched.
    ValueTask<LaunchResult?> TryShowAsync(CancellationToken cancellationToken = default);
}
