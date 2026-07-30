using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Platform;

public sealed partial class DesktopMediaControlService : IMediaControlService
{
    public ValueTask<LaunchResult> ExecuteAsync(
        MediaAction action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(new LaunchResult(
                false,
                "Управление медиа для этой операционной системы ещё не подключено."));
        }

        SendWindowsMediaKey(action);
        return ValueTask.FromResult(new LaunchResult(true, "Медиа-команда выполнена."));
    }

    [SupportedOSPlatform("windows")]
    private static void SendWindowsMediaKey(MediaAction action)
    {
        const uint keyUp = 0x0002;
        var virtualKey = action switch
        {
            MediaAction.PlayPause => 0xB3,
            MediaAction.Next => 0xB0,
            MediaAction.Previous => 0xB1,
            MediaAction.Stop => 0xB2,
            MediaAction.VolumeUp => 0xAF,
            MediaAction.VolumeDown => 0xAE,
            MediaAction.Mute => 0xAD,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

        NativeMethods.KeybdEvent((byte)virtualKey, 0, 0, UIntPtr.Zero);
        NativeMethods.KeybdEvent((byte)virtualKey, 0, keyUp, UIntPtr.Zero);
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "keybd_event")]
        internal static partial void KeybdEvent(
            byte virtualKey,
            byte scanCode,
            uint flags,
            UIntPtr extraInfo);
    }
}
