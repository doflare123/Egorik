namespace Egorik.Core.Commands;

public static class KnownEntityMatcher
{
    public static string? FindUniqueMatch(IEnumerable<string> aliases, string spokenValue)
    {
        var candidates = aliases.ToArray();
        var normalized = TextNormalizer.Normalize(spokenValue);
        var exact = candidates.Where(alias => TextNormalizer.Normalize(alias) == normalized).Take(2).ToArray();
        if (exact.Length > 0)
        {
            return exact.Length == 1 ? exact[0] : null;
        }

        var matches = candidates.Where(alias => IsMatch(alias, spokenValue)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static readonly string[] RussianEndings =
    [
        "иями", "ями", "ами", "ого", "ему", "ыми", "ими",
        "ая", "яя", "ую", "юю", "ой", "ей", "ом", "ем",
        "ам", "ям", "ах", "ях", "ов", "ев",
        "ы", "и", "а", "я", "у", "ю", "е", "о"
    ];

    public static bool IsMatch(string knownAlias, string spokenValue)
    {
        var known = TextNormalizer.Normalize(knownAlias);
        var spoken = TextNormalizer.Normalize(spokenValue);
        if (known.Length == 0 || spoken.Length == 0)
        {
            return false;
        }

        if (string.Equals(known, spoken, StringComparison.Ordinal))
        {
            return true;
        }

        if (known.Replace(" ", string.Empty, StringComparison.Ordinal) ==
            spoken.Replace(" ", string.Empty, StringComparison.Ordinal))
        {
            return true;
        }

        var knownWords = known.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var spokenWords = spoken.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (knownWords.Length != spokenWords.Length)
        {
            // Repair word boundaries only when letters (or known inflections) agree.
            var knownCompact = RemoveRussianEnding(known.Replace(" ", string.Empty, StringComparison.Ordinal));
            var spokenCompact = RemoveRussianEnding(spoken.Replace(" ", string.Empty, StringComparison.Ordinal));
            return knownCompact.Length >= 4 && knownCompact == spokenCompact;
        }

        var approximateWords = 0;
        for (var index = 0; index < knownWords.Length; index++)
        {
            if (string.Equals(knownWords[index], spokenWords[index], StringComparison.Ordinal))
            {
                continue;
            }

            var knownStem = RemoveRussianEnding(knownWords[index]);
            var spokenStem = RemoveRussianEnding(spokenWords[index]);
            if (knownStem.Length < 4 || spokenStem.Length < 4)
            {
                return false;
            }

            if (string.Equals(knownStem, spokenStem, StringComparison.Ordinal))
            {
                continue;
            }

            approximateWords++;
            if (approximateWords > 1 || EditDistanceExceedsOne(knownStem, spokenStem))
            {
                return false;
            }
        }

        return true;
    }

    private static string RemoveRussianEnding(string value)
    {
        foreach (var ending in RussianEndings)
        {
            if (value.Length - ending.Length >= 4 &&
                value.EndsWith(ending, StringComparison.Ordinal))
            {
                return value[..^ending.Length];
            }
        }

        return value;
    }

    private static bool EditDistanceExceedsOne(string first, string second)
    {
        if (Math.Abs(first.Length - second.Length) > 1)
        {
            return true;
        }

        if (first.Length == second.Length)
        {
            var differences = 0;
            for (var index = 0; index < first.Length; index++)
            {
                if (first[index] != second[index] && ++differences > 1)
                {
                    return true;
                }
            }

            return false;
        }

        var shorter = first.Length < second.Length ? first : second;
        var longer = first.Length < second.Length ? second : first;
        var shortIndex = 0;
        var longIndex = 0;
        var skipped = false;
        while (shortIndex < shorter.Length && longIndex < longer.Length)
        {
            if (shorter[shortIndex] == longer[longIndex])
            {
                shortIndex++;
                longIndex++;
                continue;
            }

            if (skipped)
            {
                return true;
            }

            skipped = true;
            longIndex++;
        }

        return false;
    }
}
