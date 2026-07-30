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
}
