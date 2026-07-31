using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Platform;

public sealed partial class DesktopOverlayWindowService : IDesktopOverlayWindowService
{
    public void ConfigureInputTransparent(nint windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == 0)
        {
            return;
        }

        ConfigureWindowsOverlay(windowHandle);
    }

    [SupportedOSPlatform("windows")]
    private static void ConfigureWindowsOverlay(nint windowHandle)
    {
        const int extendedWindowStyleIndex = -20;
        const int transparentStyle = 0x00000020;
        const int toolWindowStyle = 0x00000080;
        const int layeredStyle = 0x00080000;
        const int noActivateStyle = 0x08000000;

        var currentStyle = NativeMethods.GetWindowLong(
            windowHandle,
            extendedWindowStyleIndex);
        var overlayStyle = currentStyle |
                           transparentStyle |
                           toolWindowStyle |
                           layeredStyle |
                           noActivateStyle;

        _ = NativeMethods.SetWindowLong(
            windowHandle,
            extendedWindowStyleIndex,
            overlayStyle);
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        internal static partial int GetWindowLong(nint windowHandle, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
        internal static partial int SetWindowLong(
            nint windowHandle,
            int index,
            int newStyle);
    }
}
