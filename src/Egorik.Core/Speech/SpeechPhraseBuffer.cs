using System.Text;

namespace Egorik.Core.Speech;

public sealed class SpeechPhraseBuffer
{
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(30);
    private const int MaximumLength = 1024;
    private readonly StringBuilder _finalText = new();
    private string _partialText = string.Empty;
    private TimeSpan? _startedAt;
    private TimeSpan _lastChangedAt;
    private bool _rejected;

    public bool HasFinalizedText => _finalText.Length > 0;

    public string PreviewText => _rejected
        ? string.Empty
        : string.Join(' ', new[] { _finalText.ToString(), _partialText }.Where(text => text.Length > 0));

    public void AddFinal(string text, TimeSpan now)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 && _partialText.Length == 0)
        {
            return;
        }

        _startedAt ??= now;
        _lastChangedAt = now;
        _partialText = string.Empty;
        if (_finalText.Length + trimmed.Length + 1 > MaximumLength)
        {
            _rejected = true;
        }

        if (!_rejected && trimmed.Length > 0)
        {
            if (_finalText.Length > 0)
            {
                _finalText.Append(' ');
            }

            _finalText.Append(trimmed);
        }
    }

    public void UpdatePartial(string text, TimeSpan now)
    {
        if (text == _partialText)
        {
            return;
        }

        _startedAt ??= now;
        _lastChangedAt = now;
        _partialText = text;
        if (_finalText.Length + text.Length > MaximumLength)
        {
            _rejected = true;
        }
    }

    public string? TakeReadyPhrase(TimeSpan now)
    {
        if (_startedAt is null)
        {
            return null;
        }

        if (now - _startedAt.Value >= MaximumDuration)
        {
            _rejected = true;
        }

        // Partial hypotheses can still change: never execute a finalized prefix of them.
        if (_partialText.Length > 0 || now - _lastChangedAt < SilenceWindow)
        {
            return null;
        }

        var text = !_rejected && _finalText.Length > 0 ? _finalText.ToString() : null;
        Reset();
        return text;
    }

    public void Reset()
    {
        _finalText.Clear();
        _partialText = string.Empty;
        _startedAt = null;
        _lastChangedAt = default;
        _rejected = false;
    }
}
