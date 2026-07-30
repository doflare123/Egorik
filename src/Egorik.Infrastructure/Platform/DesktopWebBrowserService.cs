using System.Diagnostics;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Platform;

public sealed class DesktopWebBrowserService : IWebBrowserService
{
    public ValueTask<LaunchResult> OpenUrlAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (uri.Scheme is not ("http" or "https"))
        {
            return ValueTask.FromResult(new LaunchResult(false, "Разрешены только HTTP и HTTPS адреса."));
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            return ValueTask.FromResult(new LaunchResult(true, "Страница открыта в браузере."));
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ValueTask.FromResult(new LaunchResult(
                false,
                $"Не удалось открыть браузер: {exception.Message}"));
        }
    }

    public ValueTask<LaunchResult> SearchAsync(
        string query,
        string provider,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ValueTask.FromResult(new LaunchResult(false, "Пустой поисковый запрос."));
        }

        var encodedQuery = Uri.EscapeDataString(query.Trim());
        var url = provider.ToLowerInvariant() switch
        {
            "google" => $"https://www.google.com/search?q={encodedQuery}",
            "duckduckgo" => $"https://duckduckgo.com/?q={encodedQuery}",
            _ => $"https://yandex.ru/search/?text={encodedQuery}"
        };

        return OpenUrlAsync(new Uri(url), cancellationToken);
    }
}
