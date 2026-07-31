using Egorik.App.ViewModels;
using Egorik.Core.Speech;

namespace Egorik.App.Tests;

[TestClass]
public sealed class VoiceWakeIndicatorTests
{
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
