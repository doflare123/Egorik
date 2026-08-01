using System.Diagnostics;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Platform;

public sealed class DesktopApplicationLauncher(IInstalledApplicationLocator applicationLocator)
    : IApplicationLauncher
{
    public ValueTask<LaunchResult> LaunchAsync(
        string target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var resolvedTarget = applicationLocator.FindExecutable(target) ?? target;
            Process.Start(new ProcessStartInfo
            {
                FileName = resolvedTarget,
                UseShellExecute = true
            });

            return ValueTask.FromResult(new LaunchResult(true, "Приложение запущено."));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ValueTask.FromResult(new LaunchResult(
                false,
                $"Не удалось запустить приложение: {exception.Message}"));
        }
    }
}
