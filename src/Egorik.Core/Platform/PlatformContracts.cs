namespace Egorik.Core.Platform;

public sealed record LaunchResult(bool IsSuccess, string Message);

public interface IApplicationLauncher
{
    ValueTask<LaunchResult> LaunchAsync(
        string target,
        CancellationToken cancellationToken = default);
}

public interface IInstalledApplicationLocator
{
    string? FindExecutable(string executableName);
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

public enum YandexMusicAction
{
    PlayPause,
    Next,
    Previous,
    Stop
}

public interface IYandexMusicController
{
    ValueTask<LaunchResult> OpenAsync(
        CancellationToken cancellationToken = default);

    ValueTask<LaunchResult> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);

    ValueTask<LaunchResult> StartMyWaveAsync(
        CancellationToken cancellationToken = default);

    ValueTask<LaunchResult> ExecuteAsync(
        YandexMusicAction action,
        CancellationToken cancellationToken = default);
}

public sealed record SpreadsheetCellMatch(
    string Sheet,
    string Cell,
    string Value);

public sealed record SpreadsheetSearchResult(
    bool IsSuccess,
    string Message,
    IReadOnlyList<SpreadsheetCellMatch> Matches);

public interface IOfficeDocumentService
{
    ValueTask<LaunchResult> CreateWordNoteAsync(
        string text,
        CancellationToken cancellationToken = default);

    ValueTask<SpreadsheetSearchResult> SearchWorkbookAsync(
        string path,
        string query,
        CancellationToken cancellationToken = default);
}

public interface IClock
{
    DateTimeOffset Now { get; }
}

public interface IDesktopOverlayWindowService
{
    void ConfigureInputTransparent(nint windowHandle);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
