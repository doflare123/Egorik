using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Platform;

public sealed class DesktopInstalledApplicationLocator : IInstalledApplicationLocator
{
    public string? FindExecutable(string executableName)
    {
        if (!OperatingSystem.IsWindows() ||
            string.IsNullOrWhiteSpace(executableName) ||
            !string.Equals(Path.GetFileName(executableName), executableName, StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(executableName), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return FindRunningExecutable(executableName) ??
               FindAppPath(executableName) ??
               FindInPerUserPrograms(executableName);
    }

    [SupportedOSPlatform("windows")]
    private static string? FindRunningExecutable(string executableName)
    {
        var processName = Path.GetFileNameWithoutExtension(executableName);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is not null &&
                        string.Equals(Path.GetFileName(path), executableName, StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                        System.ComponentModel.Win32Exception or
                        NotSupportedException)
                {
                    // Проверяем остальные процессы и установочные каталоги.
                }
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? FindAppPath(string executableName)
    {
        string[] roots =
        [
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\App Paths\",
            @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\App Paths\"
        ];

        foreach (var root in roots)
        {
            try
            {
                var value = Registry.GetValue(root + executableName, string.Empty, null) as string;
                if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
                {
                    return value;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    System.Security.SecurityException)
            {
                // Реестр необязателен: остаётся пользовательский каталог Programs.
            }
        }

        return null;
    }

    private static string? FindInPerUserPrograms(string executableName)
    {
        var programs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");
        try
        {
            if (!Directory.Exists(programs))
            {
                return null;
            }

            var direct = Path.Combine(programs, executableName);
            if (File.Exists(direct))
            {
                return direct;
            }

            foreach (var applicationDirectory in Directory.EnumerateDirectories(programs))
            {
                var candidate = Path.Combine(applicationDirectory, executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }
}
