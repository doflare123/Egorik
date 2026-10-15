using System.Text;
using Egorik.Core.Commands;

namespace Egorik.Core.Speech;

public enum WakePhraseKind
{
    NotAddressed,
    WakeOnly,
    Command
}

public sealed record WakePhraseMatch(
    WakePhraseKind Kind,
    string RecognizedText,
    string Command,
    string MatchedWakeText,
    bool IsApproximate);

public static class WakePhraseParser
{
    public static WakePhraseMatch Parse(string text, string assistantName)
    {
        var normalized = TextNormalizer.Normalize(text);
        var wakeName = TextNormalizer.Normalize(assistantName);

        if (normalized.Length == 0 || wakeName.Length == 0)
        {
            return NotAddressed(normalized);
        }

        // An explicit address can interrupt background speech. A bare name inside
        // a sentence is still treated as a mention, never as an approximate wake.
        var explicitAddress = "слушай " + wakeName;
        var addressIndex = normalized.IndexOf(explicitAddress, StringComparison.Ordinal);
        if (addressIndex >= 0 && (addressIndex == 0 || normalized[addressIndex - 1] == ' '))
        {
            var end = addressIndex + explicitAddress.Length;
            if (end == normalized.Length)
            {
                return WakeOnly(normalized, explicitAddress, false);
            }

            if (normalized[end] == ' ')
            {
                return Command(normalized, normalized[(end + 1)..], explicitAddress, false);
            }
        }

        if (string.Equals(normalized, wakeName, StringComparison.Ordinal))
        {
            return WakeOnly(normalized, wakeName, false);
        }

        if (normalized.StartsWith(wakeName + " ", StringComparison.Ordinal))
        {
            return Command(
                normalized,
                normalized[(wakeName.Length + 1)..].Trim(),
                wakeName,
                false);
        }

        return TryMatchApproximatePrefix(normalized, wakeName) ??
               NotAddressed(normalized);
    }

    private static WakePhraseMatch? TryMatchApproximatePrefix(
        string normalized,
        string wakeName)
    {
        var compactWakeName = RemoveSpaces(wakeName);
        if (compactWakeName.Length < 4)
        {
            return null;
        }

        var speechTokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wakeTokenCount = wakeName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var minimumTokenCount = Math.Max(1, wakeTokenCount - 1);
        var maximumTokenCount = Math.Min(speechTokens.Length, wakeTokenCount + 1);
        var phoneticWakeName = CreateRussianPhoneticKey(compactWakeName);
        var maximumLengthDifference = Math.Max(2, compactWakeName.Length / 4);
        var maximumPhoneticDistance = phoneticWakeName.Length >= 9 ? 2 : 1;

        ApproximateCandidate? bestCandidate = null;
        for (var tokenCount = minimumTokenCount;
             tokenCount <= maximumTokenCount;
             tokenCount++)
        {
            var matchedText = string.Join(' ', speechTokens.Take(tokenCount));
            var compactCandidate = RemoveSpaces(matchedText);
            if (Math.Abs(compactCandidate.Length - compactWakeName.Length) >
                maximumLengthDifference)
            {
                continue;
            }

            var phoneticCandidate = CreateRussianPhoneticKey(compactCandidate);
            var distance = CalculateEditDistance(phoneticWakeName, phoneticCandidate);
            if (distance > maximumPhoneticDistance)
            {
                continue;
            }

            var candidate = new ApproximateCandidate(tokenCount, matchedText, distance);
            if (bestCandidate is null ||
                candidate.Distance < bestCandidate.Distance ||
                candidate.Distance == bestCandidate.Distance &&
                candidate.TokenCount < bestCandidate.TokenCount)
            {
                bestCandidate = candidate;
            }
        }

        if (bestCandidate is null)
        {
            return null;
        }

        var command = string.Join(' ', speechTokens.Skip(bestCandidate.TokenCount));
        return command.Length == 0
            ? WakeOnly(normalized, bestCandidate.MatchedText, true)
            : Command(normalized, command, bestCandidate.MatchedText, true);
    }

    private static string CreateRussianPhoneticKey(string text)
    {
        var key = new StringBuilder(text.Length);
        char? previous = null;

        foreach (var character in text)
        {
            var mapped = character switch
            {
                'а' or 'е' or 'ё' or 'и' or 'о' or 'у' or 'ы' or 'э' or 'ю' or 'я' => 'а',
                'б' or 'п' => 'п',
                'в' or 'ф' => 'ф',
                'г' or 'к' or 'х' => 'к',
                'д' or 'т' => 'т',
                'ж' or 'ш' or 'щ' or 'ч' => 'ш',
                'з' or 'с' or 'ц' => 'с',
                'й' or 'ь' or 'ъ' => (char?)null,
                _ => character
            };

            if (mapped is null || mapped == previous)
            {
                continue;
            }

            key.Append(mapped.Value);
            previous = mapped;
        }

        return key.ToString();
    }

    private static int CalculateEditDistance(string left, string right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var index = 0; index <= right.Length; index++)
        {
            previous[index] = index;
        }

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost =
                    left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(
                        current[rightIndex - 1] + 1,
                        previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static string RemoveSpaces(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal);

    private static WakePhraseMatch NotAddressed(string normalized) =>
        new(
            WakePhraseKind.NotAddressed,
            normalized,
            string.Empty,
            string.Empty,
            false);

    private static WakePhraseMatch WakeOnly(
        string normalized,
        string matchedWakeText,
        bool isApproximate) =>
        new(
            WakePhraseKind.WakeOnly,
            normalized,
            string.Empty,
            matchedWakeText,
            isApproximate);

    private static WakePhraseMatch Command(
        string normalized,
        string command,
        string matchedWakeText,
        bool isApproximate) =>
        new(
            WakePhraseKind.Command,
            normalized,
            command,
            matchedWakeText,
            isApproximate);

    private sealed record ApproximateCandidate(
        int TokenCount,
        string MatchedText,
        int Distance);
}
