using Egorik.Core.Commands;

namespace Egorik.Core.Speech;

public sealed class VoiceSuppression
{
    private TimeSpan _suppressedUntil;

    public bool IsSuppressed(TimeSpan now) => now < _suppressedUntil;

    public bool Observe(string text, TimeSpan now)
    {
        if (IsSuppressed(now))
        {
            return true;
        }

        if (!TextNormalizer.Normalize(text).Split(' ').Contains("отбой", StringComparer.Ordinal))
        {
            return false;
        }

        _suppressedUntil = now + TimeSpan.FromSeconds(10);
        return true;
    }
}
