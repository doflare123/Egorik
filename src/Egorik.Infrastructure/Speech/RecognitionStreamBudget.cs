namespace Egorik.Infrastructure.Speech;

public enum RecognitionStreamReset
{
    None,
    AtPhraseBoundary,
    AbandonOverlongPhrase
}

public sealed class RecognitionStreamBudget
{
    // Vosk retains its feature pipeline across ordinary Result()/Reset() calls.
    // Recycle the decoder at a phrase boundary; cap uninterrupted noise as well.
    private const long BoundaryBytes = 16000L * 2 * 60;
    private const long MaximumBytes = 16000L * 2 * 90;
    private long _bytes;

    public RecognitionStreamReset Advance(int bytesRecorded, bool phraseFinalized)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytesRecorded);
        _bytes += bytesRecorded;
        var reset = phraseFinalized && _bytes >= BoundaryBytes
            ? RecognitionStreamReset.AtPhraseBoundary
            : _bytes >= MaximumBytes
                ? RecognitionStreamReset.AbandonOverlongPhrase
                : RecognitionStreamReset.None;
        if (reset != RecognitionStreamReset.None)
        {
            Reset();
        }

        return reset;
    }

    public void Reset() => _bytes = 0;
}
