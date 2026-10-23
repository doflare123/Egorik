using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Platform;

public sealed partial class DesktopYandexMusicWindowService(IInstalledApplicationLocator applicationLocator)
    : IYandexMusicWindowService
{
    public async ValueTask<LaunchResult?> TryShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return await Task.Run(() => TryShowWindow(cancellationToken), cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private LaunchResult? TryShowWindow(CancellationToken cancellationToken)
    {
        var executable = applicationLocator.FindExecutable(DesktopYandexMusicController.ExecutableName);
        var processIds = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName(
                     Path.GetFileNameWithoutExtension(DesktopYandexMusicController.ExecutableName)))
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (executable is not null && string.Equals(
                            process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    {
                        processIds.Add((uint)process.Id);
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or
                    System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // A process may exit while its executable is being inspected.
                }
            }
        }

        if (processIds.Count == 0)
        {
            return null;
        }

        nint window = 0;
        // MainWindowHandle is zero for a window hidden in the tray. Enumerate hidden
        // top-level windows too, but exclude Chromium helper and owned windows.
        _ = EnumWindows((handle, parameter) =>
        {
            _ = GetWindowThreadProcessId(handle, out var processId);
            if (processIds.Contains(processId) && GetWindow(handle, 4) == 0 &&
                GetWindowTextLength(handle) > 0 && (GetWindowLong(handle, -20) & 0x80) == 0)
            {
                window = handle;
                return false;
            }

            return true;
        }, 0);

        cancellationToken.ThrowIfCancellationRequested();
        if (window == 0)
        {
            return new LaunchResult(false,
                "Яндекс Музыка уже запущена, но её окно пока недоступно. Повтори команду после загрузки приложения.");
        }

        if (!ShowWindowAsync(window, IsIconic(window) ? 9 : 5))
        {
            return new LaunchResult(false, "Не удалось показать окно уже запущенной Яндекс Музыки.");
        }

        _ = SetForegroundWindow(window);
        return new LaunchResult(true, "Окно Яндекс Музыки показано.");
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(nint window, int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowAsync(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);
}
