using Egorik.Core.Commands;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class TextNormalizerTests
{
    [TestMethod]
    public void Normalize_CleansPunctuationAndYo()
    {
        var result = TextNormalizer.Normalize("  Ёгорик,   ОТКРОЙ калькулятор! ");

        Assert.AreEqual("егорик открой калькулятор", result);
    }

    [TestMethod]
    public void RemoveWakeName_RemovesOnlyLeadingName()
    {
        var result = TextNormalizer.RemoveWakeName(
            "егорик найди егорик",
            "Егорик");

        Assert.AreEqual("найди егорик", result);
    }

    [TestMethod]
    [DataRow("музыка", "музыку")]
    [DataRow("яндекс музыка", "яндекс музыке")]
    [DataRow("стим", "стиле")]
    public void KnownEntityMatcher_AcceptsInflectionAndOneRecognitionError(
        string knownAlias,
        string spokenValue)
    {
        Assert.IsTrue(KnownEntityMatcher.IsMatch(knownAlias, spokenValue));
    }

    [TestMethod]
    [DataRow("стим", "стол")]
    [DataRow("музыка", "музей")]
    [DataRow("мой браузер", "мой блокнот")]
    public void KnownEntityMatcher_RejectsUnrelatedValues(
        string knownAlias,
        string spokenValue)
    {
        Assert.IsFalse(KnownEntityMatcher.IsMatch(knownAlias, spokenValue));
    }
}
