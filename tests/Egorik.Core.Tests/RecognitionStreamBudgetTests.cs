using Egorik.Infrastructure.Speech;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class RecognitionStreamBudgetTests
{
    private const int BytesPerSecond = 16000 * 2;

    [TestMethod]
    public void ShortFinalizedPhrasesDoNotResetDecoder()
    {
        var budget = new RecognitionStreamBudget();
        for (var second = 0; second < 59; second++)
        {
            Assert.AreEqual(RecognitionStreamReset.None, budget.Advance(BytesPerSecond, true));
        }
    }

    [TestMethod]
    public void FirstBoundaryAfterMinuteReleasesHistoryAndStartsNewBudget()
    {
        var budget = new RecognitionStreamBudget();
        Assert.AreEqual(RecognitionStreamReset.None, budget.Advance(60 * BytesPerSecond, false));
        Assert.AreEqual(RecognitionStreamReset.AtPhraseBoundary, budget.Advance(BytesPerSecond, true));
        Assert.AreEqual(RecognitionStreamReset.None, budget.Advance(59 * BytesPerSecond, true));
        Assert.AreEqual(RecognitionStreamReset.AtPhraseBoundary, budget.Advance(BytesPerSecond, true));
    }

    [TestMethod]
    public void EndlessNoiseCannotRetainUnlimitedDecoderHistory()
    {
        var budget = new RecognitionStreamBudget();
        for (var round = 0; round < 100; round++)
        {
            Assert.AreEqual(RecognitionStreamReset.None, budget.Advance(89 * BytesPerSecond, false));
            Assert.AreEqual(RecognitionStreamReset.AbandonOverlongPhrase, budget.Advance(BytesPerSecond, false));
        }
    }

    [TestMethod]
    public void BoundaryAtHardLimitKeepsCompletedPhrase()
    {
        var budget = new RecognitionStreamBudget();
        Assert.AreEqual(RecognitionStreamReset.AtPhraseBoundary, budget.Advance(90 * BytesPerSecond, true));
    }

    [TestMethod]
    public void StopOrNewDecoderClearsPreviouslyConsumedAudio()
    {
        var budget = new RecognitionStreamBudget();
        budget.Advance(89 * BytesPerSecond, false);
        budget.Reset();
        Assert.AreEqual(RecognitionStreamReset.None, budget.Advance(BytesPerSecond, false));
    }

    [TestMethod]
    public void EmptyBuffersDoNotSpendBudgetAndNegativeSizeIsRejected()
    {
        var budget = new RecognitionStreamBudget();
        Assert.AreEqual(RecognitionStreamReset.None, budget.Advance(0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.Advance(-1, false));
    }
}
