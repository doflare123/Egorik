using Egorik.App.ViewModels;
using Egorik.Core.Speech;

namespace Egorik.App.Tests;

[TestClass]
public sealed class VoiceActivityDispatcherTests
{
    [TestMethod]
    public void BusyUiKeepsOneCallbackAndDisplaysOnlyLatestActivity()
    {
        var callbacks = new Queue<Action>();
        var displayed = new List<VoiceInputActivityEventArgs>();
        using var dispatcher = Create(callbacks, displayed);

        for (var index = 0; index < 100_000; index++)
        {
            dispatcher.Publish(new VoiceInputActivityEventArgs(index / 100_000d, index.ToString()));
        }

        Assert.AreEqual(1, callbacks.Count);
        callbacks.Dequeue()();
        Assert.HasCount(1, displayed);
        Assert.AreEqual("99999", displayed[0].PartialText);
        Assert.AreEqual(0, callbacks.Count);
    }

    [TestMethod]
    public void HiddenWindowDoesNotReceiveMeterUpdatesAndRestoresLatestOnShow()
    {
        var callbacks = new Queue<Action>();
        var displayed = new List<VoiceInputActivityEventArgs>();
        using var dispatcher = Create(callbacks, displayed);
        dispatcher.SetVisible(false);

        for (var index = 0; index < 1000; index++)
        {
            dispatcher.Publish(new VoiceInputActivityEventArgs(0.5, index.ToString()));
            callbacks.Dequeue()();
        }

        Assert.HasCount(0, displayed);
        dispatcher.SetVisible(true);
        Assert.HasCount(1, displayed);
        Assert.AreEqual("999", displayed[0].PartialText);
        dispatcher.SetVisible(true);
        Assert.HasCount(1, displayed);
    }

    [TestMethod]
    public void CoalescingKeepsWakeIndicatorWorkingWhileHidden()
    {
        var callbacks = new Queue<Action>();
        var displayed = new List<VoiceInputActivityEventArgs>();
        var wakes = new List<string>();
        using var dispatcher = Create(callbacks, displayed, wakes);
        dispatcher.SetVisible(false);
        dispatcher.Publish(new VoiceInputActivityEventArgs(0.8, "егорик"));
        dispatcher.Publish(new VoiceInputActivityEventArgs(0, string.Empty));

        callbacks.Dequeue()();

        CollectionAssert.AreEqual(new[] { "егорик" }, wakes);
        Assert.HasCount(0, displayed);
    }

    [TestMethod]
    public void CommandOrStateBoundaryClearsQueuedWake()
    {
        var callbacks = new Queue<Action>();
        var displayed = new List<VoiceInputActivityEventArgs>();
        var wakes = new List<string>();
        using var dispatcher = Create(callbacks, displayed, wakes);
        dispatcher.Publish(new VoiceInputActivityEventArgs(0.8, "егорик"));
        dispatcher.ClearPendingWake();

        callbacks.Dequeue()();

        Assert.HasCount(0, wakes);
        Assert.HasCount(1, displayed);
    }

    [TestMethod]
    public void DisposingDropsQueuedActivityAndRejectsNewEvents()
    {
        var callbacks = new Queue<Action>();
        var displayed = new List<VoiceInputActivityEventArgs>();
        var wakes = new List<string>();
        var dispatcher = Create(callbacks, displayed, wakes);
        dispatcher.Publish(new VoiceInputActivityEventArgs(0.8, "егорик"));
        dispatcher.Dispose();
        callbacks.Dequeue()();
        dispatcher.Publish(new VoiceInputActivityEventArgs(0.8, "егорик"));
        dispatcher.SetVisible(false);
        dispatcher.SetVisible(true);

        Assert.AreEqual(0, callbacks.Count);
        Assert.HasCount(0, displayed);
        Assert.HasCount(0, wakes);
    }

    [TestMethod]
    public void ActivityArrivingDuringPresentationGetsOneFollowingCallback()
    {
        var callbacks = new Queue<Action>();
        var displayed = new List<string>();
        VoiceActivityDispatcher? dispatcher = null;
        dispatcher = new VoiceActivityDispatcher(callbacks.Enqueue, _ => false, _ => { }, activity =>
        {
            displayed.Add(activity.PartialText);
            if (activity.PartialText == "first")
            {
                dispatcher!.Publish(new VoiceInputActivityEventArgs(0.2, "second"));
                dispatcher.Publish(new VoiceInputActivityEventArgs(0.3, "third"));
            }
        });
        using (dispatcher)
        {
            dispatcher.Publish(new VoiceInputActivityEventArgs(0.1, "first"));
            callbacks.Dequeue()();
            Assert.AreEqual(1, callbacks.Count);
            callbacks.Dequeue()();
        }

        CollectionAssert.AreEqual(new[] { "first", "third" }, displayed);
    }

    [TestMethod]
    public void ConcurrentProducersShareOnePendingCallback()
    {
        var callbacks = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var displayed = new List<VoiceInputActivityEventArgs>();
        using var dispatcher = new VoiceActivityDispatcher(callbacks.Enqueue, _ => false, _ => { }, displayed.Add);
        Parallel.For(0, 10_000, index => dispatcher.Publish(new VoiceInputActivityEventArgs(0.5, index.ToString())));

        Assert.AreEqual(1, callbacks.Count);
        Assert.IsTrue(callbacks.TryDequeue(out var callback));
        callback();
        Assert.HasCount(1, displayed);
    }

    private static VoiceActivityDispatcher Create(
        Queue<Action> callbacks,
        List<VoiceInputActivityEventArgs> displayed,
        List<string>? wakes = null) =>
        new(callbacks.Enqueue, text => text == "егорик", text => wakes?.Add(text), displayed.Add);
}
