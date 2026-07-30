using Egorik.Core.Commands;
using Egorik.Infrastructure.Commands;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class CalculatorCommandTests
{
    [TestMethod]
    public async Task ExecuteAsync_RespectsOperatorPrecedence()
    {
        var command = new CalculatorCommand();
        var match = await command.MatchAsync("сколько будет 2 + 3 * 4");

        Assert.IsNotNull(match);
        var result = await command.ExecuteAsync(
            new CommandContext(
                "сколько будет 2 + 3 * 4",
                "сколько будет 2 + 3 * 4"),
            match);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Получается 14.", result.Message);
    }

    [TestMethod]
    public async Task MatchAsync_RejectsInvalidExpression()
    {
        var command = new CalculatorCommand();

        var match = await command.MatchAsync("сколько будет 2 плюс кот");

        Assert.IsNull(match);
    }
}
