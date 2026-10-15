using Egorik.Core.Platform;
using Microsoft.Win32;

namespace Egorik.Infrastructure.Platform;

public sealed class DesktopSystemStartupService : ISystemStartupService
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public ValueTask SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            if (enabled)
            {
                throw new PlatformNotSupportedException("Автозапуск пока доступен только в Windows.");
            }

            return ValueTask.CompletedTask;
        }

        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (enabled)
        {
            var executable = Environment.ProcessPath ??
                throw new InvalidOperationException("Не найден исполняемый файл Egorik.");
            if (!string.Equals(Path.GetFileNameWithoutExtension(executable), "Egorik", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Включите автозапуск из собранного приложения Egorik.exe.");
            }

            key.SetValue("Egorik", $"\"{executable}\" --tray");
        }
        else
        {
            key.DeleteValue("Egorik", throwOnMissingValue: false);
        }

        return ValueTask.CompletedTask;
    }
}
