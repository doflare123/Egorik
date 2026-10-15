using Egorik.Core.Speech;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class SpeechPhraseBufferTests
{
    [TestMethod]
    public void JoinsFinalSegmentsInsteadOfExecutingFirstFragment()
    {
        var buffer = new SpeechPhraseBuffer();
        buffer.AddFinal("егорик открой", At(0));
        Assert.IsNull(buffer.TakeReadyPhrase(At(0.8)));
        buffer.UpdatePartial("бра", At(1));
        Assert.IsNull(buffer.TakeReadyPhrase(At(2)));
        buffer.AddFinal("браузер", At(2.1));

        Assert.AreEqual("егорик открой браузер", buffer.TakeReadyPhrase(At(3.4)));
        Assert.IsNull(buffer.TakeReadyPhrase(At(4)));
    }

    [TestMethod]
    public void RevisedPartialHypothesesAreNeverAppendedOrExecuted()
    {
        var buffer = new SpeechPhraseBuffer();
        buffer.UpdatePartial("егорик отключи", At(0));
        buffer.UpdatePartial("егорик открой", At(0.5));
        Assert.IsNull(buffer.TakeReadyPhrase(At(2)));
        buffer.AddFinal("егорик открой браузер", At(2.1));

        Assert.AreEqual("егорик открой браузер", buffer.TakeReadyPhrase(At(3.4)));
    }

    [TestMethod]
    public void UnfinishedContinuationDoesNotExecuteFinalizedPrefix()
    {
        var buffer = new SpeechPhraseBuffer();
        buffer.AddFinal("егорик открой браузер", At(0));
        buffer.UpdatePartial("не надо", At(0.8));

        Assert.IsNull(buffer.TakeReadyPhrase(At(2)));
        Assert.IsNull(buffer.TakeReadyPhrase(At(31)));
        buffer.AddFinal("не надо", At(31.1));
        Assert.IsNull(buffer.TakeReadyPhrase(At(32.4)));
    }

    [TestMethod]
    public void LongerContinuousUtteranceCanFinishBeforeDurationLimit()
    {
        var buffer = new SpeechPhraseBuffer();
        buffer.UpdatePartial("егорик найди", At(0));
        buffer.AddFinal("егорик найди расписание поездов на завтра", At(10));

        Assert.AreEqual("егорик найди расписание поездов на завтра", buffer.TakeReadyPhrase(At(11.3)));
    }

    [TestMethod]
    public void ResetDiscardsSpeechFromPreviousListeningSession()
    {
        var buffer = new SpeechPhraseBuffer();
        buffer.AddFinal("егорик открой", At(0));
        buffer.Reset();
        buffer.AddFinal("пауза", At(1));

        Assert.AreEqual("пауза", buffer.TakeReadyPhrase(At(2.3)));
    }

    [TestMethod]
    public void OverlongPhraseIsDiscardedWithoutExecutingTruncatedPrefix()
    {
        var buffer = new SpeechPhraseBuffer();
        buffer.AddFinal("егорик открой браузер", At(0));
        buffer.AddFinal(new string('а', 1024), At(0.5));
        Assert.IsNull(buffer.TakeReadyPhrase(At(2)));
        buffer.AddFinal("егорик пауза", At(3));

        Assert.AreEqual("егорик пауза", buffer.TakeReadyPhrase(At(4.3)));
    }

    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);
}
