namespace Egorik.Infrastructure;

public static class DefaultCatalog
{
    public static IReadOnlyDictionary<string, string> Applications()
    {
        if (OperatingSystem.IsWindows())
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["блокнот"] = "notepad.exe",
                ["калькулятор"] = "calc.exe",
                ["проводник"] = "explorer.exe",
                ["терминал"] = "cmd.exe",
                ["настройки windows"] = "ms-settings:"
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["калькулятор"] = "/System/Applications/Calculator.app",
                ["проводник"] = "/System/Library/CoreServices/Finder.app",
                ["терминал"] = "/System/Applications/Utilities/Terminal.app"
            };
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["проводник"] = "xdg-open",
            ["терминал"] = "x-terminal-emulator"
        };
    }

    public static IReadOnlyDictionary<string, string> Sites() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["яндекс"] = "https://ya.ru",
            ["ютуб"] = "https://youtube.com",
            ["youtube"] = "https://youtube.com",
            ["гитхаб"] = "https://github.com",
            ["github"] = "https://github.com",
            ["почта"] = "https://mail.yandex.ru",
            ["яндекс музыка"] = "https://music.yandex.ru"
        };
}
