namespace Egorik.Core.Platform;

public sealed record LaunchResult(bool IsSuccess, string Message);

public interface IApplicationLauncher
{
    ValueTask<LaunchResult> LaunchAsync(
        string target,
        CancellationToken cancellationToken = default);
}

public interface IWebBrowserService
{
    ValueTask<LaunchResult> OpenUrlAsync(
        Uri uri,
        CancellationToken cancellationToken = default);

    ValueTask<LaunchResult> SearchAsync(
        string query,
        string provider,
        CancellationToken cancellationToken = default);
}

public enum MediaAction
{
    PlayPause,
    Next,
    Previous,
    Stop,
    VolumeUp,
    VolumeDown,
    Mute
}

public interface IMediaControlService
{
    ValueTask<LaunchResult> ExecuteAsync(
        MediaAction action,
        CancellationToken cancellationToken = default);
}

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
