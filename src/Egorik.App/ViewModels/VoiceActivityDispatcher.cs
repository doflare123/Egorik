using Egorik.Core.Speech;

namespace Egorik.App.ViewModels;

public sealed class VoiceActivityDispatcher(
    Action<Action> post,
    Func<string, bool> isWakePhrase,
    Action<string> observeWake,
    Action<VoiceInputActivityEventArgs> display) : IDisposable
{
    private readonly Lock _sync = new();
    private VoiceInputActivityEventArgs? _latest;
    private string? _pendingWake;
    private bool _scheduled;
    private bool _visible = true;
    private bool _disposed;

    public void Publish(VoiceInputActivityEventArgs activity)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _latest = activity;
            if (activity.PartialText.Length > 0 && isWakePhrase(activity.PartialText))
            {
                _pendingWake = activity.PartialText;
            }

            if (_scheduled)
            {
                return;
            }

            _scheduled = true;
        }

        post(Flush);
    }

    public void ClearPendingWake()
    {
        lock (_sync)
        {
            _pendingWake = null;
        }
    }

    // Visibility and presentation callbacks belong to the UI thread.
    public void SetVisible(bool visible)
    {
        VoiceInputActivityEventArgs? latest;
        lock (_sync)
        {
            if (_disposed || _visible == visible)
            {
                return;
            }

            _visible = visible;
            latest = _latest;
        }

        if (visible && latest is not null)
        {
            display(latest);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _latest = null;
            _pendingWake = null;
        }
    }

    private void Flush()
    {
        VoiceInputActivityEventArgs? latest;
        string? wake;
        bool visible;
        lock (_sync)
        {
            _scheduled = false;
            if (_disposed)
            {
                return;
            }

            latest = _latest;
            wake = _pendingWake;
            _pendingWake = null;
            visible = _visible;
        }

        if (wake is not null)
        {
            observeWake(wake);
        }

        if (visible && latest is not null)
        {
            display(latest);
        }
    }
}
