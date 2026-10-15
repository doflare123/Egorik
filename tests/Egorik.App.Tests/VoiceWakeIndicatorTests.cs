using Egorik.App.ViewModels;
using Egorik.Core.Speech;

namespace Egorik.App.Tests;

[TestClass]
public sealed class VoiceWakeIndicatorTests
{
    [TestMethod]
    public void FinalizedWakePhraseKeepsIndicatorVisibleWhileWaitingForContinuation()
    {
        var indicator = new VoiceWakeIndicator();
        var buffer = new SpeechPhraseBuffer();
        buffer.AddFinal("егорик открой", TimeSpan.Zero);

        Assert.IsTrue(indicator.ObserveActivity(buffer.PreviewText, "Егорик"));
        Assert.IsNull(buffer.TakeReadyPhrase(TimeSpan.FromMilliseconds(500)));
        buffer.UpdatePartial("браузер", TimeSpan.FromMilliseconds(600));
        Assert.AreEqual("егорик открой браузер", buffer.PreviewText);
        Assert.IsTrue(indicator.ObserveActivity(buffer.PreviewText, "Егорик"));
        buffer.AddFinal("браузер", TimeSpan.FromMilliseconds(700));
        Assert.IsNotNull(buffer.TakeReadyPhrase(TimeSpan.FromSeconds(2)));
        Assert.IsFalse(indicator.AcceptCommand());
    }

    [TestMethod]
    public void WakeNameInPartialSpeechActivatesUntilCommandIsAccepted()
    {
        var indicator = new VoiceWakeIndicator();

        var activated = indicator.ObserveActivity(
            "Егорик открой калькулятор",
            "Егорик");
        var deactivated = indicator.AcceptCommand();

        Assert.IsTrue(activated);
        Assert.IsFalse(deactivated);
        Assert.IsFalse(indicator.IsActive);
    }

    [TestMethod]
    public void UnrelatedPartialSpeechDoesNotActivateIndicator()
    {
        var indicator = new VoiceWakeIndicator();

        var active = indicator.ObserveActivity(
            "открой калькулятор",
            "Егорик");

        Assert.IsFalse(active);
    }

    [TestMethod]
    public void ListeningStateActivatesAndWaitingStateDeactivatesIndicator()
    {
        var indicator = new VoiceWakeIndicator();

        var listening = indicator.ObserveState(VoiceInputState.ListeningForCommand);
        var waiting = indicator.ObserveState(VoiceInputState.WaitingForWakeWord);

        Assert.IsTrue(listening);
        Assert.IsFalse(waiting);
        Assert.IsFalse(indicator.IsActive);
    }
}
