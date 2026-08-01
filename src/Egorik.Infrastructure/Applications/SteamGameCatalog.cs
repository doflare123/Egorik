using Egorik.Core.Commands;
using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Text;

namespace Egorik.Infrastructure.Applications;

public sealed class SteamGameCatalog : ISteamGameCatalog
{
    public ValueTask<IReadOnlyList<InstalledSteamGame>> FindAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(query))
        {
            return ValueTask.FromResult<IReadOnlyList<InstalledSteamGame>>([]);
        }

        if (TextNormalizer.Normalize(query).Length == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<InstalledSteamGame>>([]);
        }

        var games = ReadInstalledGames(cancellationToken);
        var scored = games
            .Select(game => new
            {
                Game = game,
                Score = SteamGameNameMatcher.MatchScore(query, game.Name)
            })
            .Where(candidate => candidate.Score > 0)
            .ToArray();
        if (scored.Length == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<InstalledSteamGame>>([]);
        }

        var bestScore = scored.Max(candidate => candidate.Score);
        var matches = scored
            .Where(candidate => candidate.Score == bestScore)
            .Select(candidate => candidate.Game)
            .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<InstalledSteamGame>>(matches);
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<InstalledSteamGame> ReadInstalledGames(
        CancellationToken cancellationToken)
    {
        var games = new Dictionary<string, InstalledSteamGame>(StringComparer.Ordinal);
        foreach (var library in FindLibraries())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var steamApps = Path.Combine(library, "steamapps");
            try
            {
                if (!Directory.Exists(steamApps))
                {
                    continue;
                }

                foreach (var manifest in Directory.EnumerateFiles(
                             steamApps,
                             "appmanifest_*.acf",
                             SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var game = TryReadManifest(manifest);
                    if (game is not null)
                    {
                        games.TryAdd(game.AppId, game);
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Недоступная библиотека не должна мешать поиску в остальных.
            }
        }

        return games.Values.ToArray();
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> FindLibraries()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            libraries.Add(Path.Combine(programFilesX86, "Steam"));
        }

        try
        {
            var configuredPath = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Valve\Steam",
                "SteamPath",
                null) as string;
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                libraries.Add(configuredPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Остаются стандартные каталоги установки.
        }

        foreach (var root in libraries.ToArray())
        {
            var file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                foreach (var line in File.ReadLines(file))
                {
                    if (!line.Contains("\"path\"", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var values = ReadQuotedValues(line);
                    if (values.Count >= 2 &&
                        string.Equals(values[0], "path", StringComparison.OrdinalIgnoreCase))
                    {
                        libraries.Add(values[1].Replace("\\\\", "\\", StringComparison.Ordinal));
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Основная библиотека всё равно остаётся доступной для проверки.
            }
        }

        return libraries.ToArray();
    }

    private static InstalledSteamGame? TryReadManifest(string path)
    {
        try
        {
            string? appId = null;
            string? name = null;
            foreach (var line in File.ReadLines(path))
            {
                var values = ReadQuotedValues(line);
                if (values.Count < 2)
                {
                    continue;
                }

                if (string.Equals(values[0], "appid", StringComparison.OrdinalIgnoreCase))
                {
                    appId = values[1];
                }
                else if (string.Equals(values[0], "name", StringComparison.OrdinalIgnoreCase))
                {
                    name = values[1];
                }
            }

            return appId is not null &&
                   appId.All(char.IsAsciiDigit) &&
                   !string.IsNullOrWhiteSpace(name)
                ? new InstalledSteamGame(appId, name.Trim())
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadQuotedValues(string line)
    {
        var values = new List<string>();
        var start = -1;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != '"')
            {
                continue;
            }

            if (start < 0)
            {
                start = index + 1;
            }
            else
            {
                values.Add(line[start..index]);
                start = -1;
            }
        }

        return values;
    }
}

public static class SteamGameNameMatcher
{
    private static readonly IReadOnlyDictionary<string, string> LatinSounds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["shch"] = "щ",
            ["yo"] = "е",
            ["zh"] = "ж",
            ["kh"] = "х",
            ["ts"] = "ц",
            ["ch"] = "ч",
            ["sh"] = "ш",
            ["yu"] = "ю",
            ["ya"] = "я",
            ["a"] = "а",
            ["b"] = "б",
            ["c"] = "к",
            ["d"] = "д",
            ["e"] = "е",
            ["f"] = "ф",
            ["g"] = "г",
            ["h"] = "х",
            ["i"] = "и",
            ["j"] = "дж",
            ["k"] = "к",
            ["l"] = "л",
            ["m"] = "м",
            ["n"] = "н",
            ["o"] = "о",
            ["p"] = "п",
            ["q"] = "к",
            ["r"] = "р",
            ["s"] = "с",
            ["t"] = "т",
            ["u"] = "у",
            ["v"] = "в",
            ["w"] = "в",
            ["x"] = "кс",
            ["y"] = "й",
            ["z"] = "з"
        };

    public static int MatchScore(string query, string installedName)
    {
        var normalizedQuery = TextNormalizer.Normalize(query);
        var normalizedName = TextNormalizer.Normalize(installedName);
        if (normalizedQuery.Length == 0 || normalizedName.Length == 0)
        {
            return 0;
        }

        if (normalizedQuery == normalizedName)
        {
            return 100;
        }

        var spokenName = TransliterateLatin(normalizedName);
        if (normalizedQuery == spokenName)
        {
            return 100;
        }

        if (KnownEntityMatcher.IsMatch(normalizedName, normalizedQuery) ||
            KnownEntityMatcher.IsMatch(spokenName, normalizedQuery))
        {
            return 95;
        }

        var nameWithoutNumber = RemoveTrailingNumbers(normalizedName);
        var spokenNameWithoutNumber = RemoveTrailingNumbers(spokenName);
        if (KnownEntityMatcher.IsMatch(nameWithoutNumber, normalizedQuery) ||
            KnownEntityMatcher.IsMatch(spokenNameWithoutNumber, normalizedQuery))
        {
            return 90;
        }

        if (normalizedQuery.Length >= 3 &&
            (normalizedName.Contains(normalizedQuery, StringComparison.Ordinal) ||
             spokenName.Contains(normalizedQuery, StringComparison.Ordinal)))
        {
            return 70;
        }

        return 0;
    }

    private static string TransliterateLatin(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length;)
        {
            var matched = false;
            foreach (var pair in LatinSounds)
            {
                if (value.AsSpan(index).StartsWith(pair.Key, StringComparison.Ordinal))
                {
                    result.Append(pair.Value);
                    index += pair.Key.Length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                result.Append(value[index]);
                index++;
            }
        }

        return result.ToString();
    }

    private static string RemoveTrailingNumbers(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && words[^1].All(char.IsDigit))
        {
            words.RemoveAt(words.Count - 1);
        }

        return string.Join(' ', words);
    }
}
