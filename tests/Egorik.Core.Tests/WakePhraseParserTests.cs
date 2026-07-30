using Egorik.Core.Speech;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class WakePhraseParserTests
{
    [TestMethod]
    public void ParseRecognizesNameAndCommandInOnePhrase()
    {
        var match = WakePhraseParser.Parse(
            "Егорик, открой калькулятор",
            "Егорик");

        Assert.AreEqual(WakePhraseKind.Command, match.Kind);
        Assert.AreEqual("открой калькулятор", match.Command);
        Assert.IsFalse(match.IsApproximate);
    }

    [TestMethod]
    public void ParseRecognizesWakeNameWithoutCommand()
    {
        var match = WakePhraseParser.Parse("ЕГОРИК!", "Егорик");

        Assert.AreEqual(WakePhraseKind.WakeOnly, match.Kind);
        Assert.AreEqual(string.Empty, match.Command);
        Assert.IsFalse(match.IsApproximate);
    }

    [TestMethod]
    public void ParseDoesNotRunSpeechWithoutWakeName()
    {
        var match = WakePhraseParser.Parse(
            "открой калькулятор",
            "Егорик");

        Assert.AreEqual(WakePhraseKind.NotAddressed, match.Kind);
    }

    [TestMethod]
    [DataRow("игорек")]
    [DataRow("игор рек")]
    [DataRow("егор рек")]
    [DataRow("егор рик")]
    [DataRow("и горек")]
    public void ParseRecognizesCommonSpeechToTextWakeNameArtifacts(string phrase)
    {
        var match = WakePhraseParser.Parse(phrase, "Егорик");

        Assert.AreEqual(WakePhraseKind.WakeOnly, match.Kind);
        Assert.IsTrue(match.IsApproximate);
        Assert.AreEqual(phrase, match.MatchedWakeText);
    }

    [TestMethod]
    [DataRow("игорек открой калькулятор")]
    [DataRow("игор рек открой калькулятор")]
    [DataRow("егор рек открой калькулятор")]
    [DataRow("и горек открой калькулятор")]
    public void ParseExtractsCommandAfterApproximateWakeName(string phrase)
    {
        var match = WakePhraseParser.Parse(phrase, "Егорик");

        Assert.AreEqual(WakePhraseKind.Command, match.Kind);
        Assert.AreEqual("открой калькулятор", match.Command);
        Assert.IsTrue(match.IsApproximate);
    }

    [TestMethod]
    [DataRow("Игорь расскажи анекдот")]
    [DataRow("дворик открой калькулятор")]
    [DataRow("открой калькулятор")]
    public void ParseRejectsUnrelatedSimilarWords(string phrase)
    {
        var match = WakePhraseParser.Parse(phrase, "Егорик");

        Assert.AreEqual(WakePhraseKind.NotAddressed, match.Kind);
    }
}
