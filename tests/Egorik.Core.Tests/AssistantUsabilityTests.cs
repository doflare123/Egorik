using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Plugins;
using Egorik.Core.Speech;
using Egorik.Infrastructure.Commands;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class AssistantUsabilityTests
{
    [TestMethod]
    [DataRow(true, false, "application.action", "Задача выполнена.")]
    [DataRow(true, true, "scenario.execute", "Задача выполнена.")]
    [DataRow(false, false, "application.action", "Задача не выполнена.")]
    [DataRow(false, true, "ai.plan", "Задача не выполнена.")]
    [DataRow(false, true, "scenario.execute", "Задача не выполнена.")]
    public void ShortResponsesUseOnlyOutcomeAndPreserveDetails(
        bool success, bool detailedFailures, string commandId, string expected)
    {
        var result = success
            ? CommandResult.Success("Подробности результата", commandId)
            : CommandResult.Failure("Подробности результата", commandId);

        Assert.AreEqual(expected, SpokenResponse.For(result, detailedFailures, shortResponses: true));
        Assert.AreEqual("Подробности результата", result.Message);
        Assert.AreEqual(success, result.IsSuccess);
    }

    [TestMethod]
    public void BriefFailuresKeepDetailsInResultAndSuccessSpeechIntact()
    {
        var failure = CommandResult.Failure("HTTP 503: подробности", "ai.plan");
        Assert.AreEqual("API ИИ недоступен.", SpokenResponse.For(failure, false));
        Assert.AreEqual(failure.Message, SpokenResponse.For(failure, true));
        Assert.AreEqual("HTTP 503: подробности", failure.Message);
        Assert.AreEqual("Не удалось выполнить команду.",
            SpokenResponse.For(CommandResult.Failure("ошибка пути", "application.open"), false));
        Assert.AreEqual("Готово", SpokenResponse.For(CommandResult.Success("Готово"), false));
    }

    [TestMethod]
    [DataRow("отбой")]
    [DataRow("Егорик, отбой!")]
    [DataRow("открой браузер отбой")]
    public void StopSuppressesImmediatelyForTenSecondsWithoutExtendingOnBackgroundSpeech(string phrase)
    {
        var suppression = new VoiceSuppression();
        Assert.IsTrue(suppression.Observe(phrase, TimeSpan.FromSeconds(1)));
        Assert.IsTrue(suppression.Observe("Егорик открой браузер", TimeSpan.FromSeconds(5)));
        Assert.IsTrue(suppression.IsSuppressed(TimeSpan.FromSeconds(10.99)));
        Assert.IsFalse(suppression.IsSuppressed(TimeSpan.FromSeconds(11)));
        Assert.IsFalse(suppression.Observe("Егорик открой браузер", TimeSpan.FromSeconds(11)));
    }

    [TestMethod]
    [DataRow("отбойник")]
    [DataRow("бой")]
    [DataRow("отбои")]
    public void StopDoesNotUseFuzzyMatching(string text)
    {
        Assert.IsFalse(new VoiceSuppression().Observe(text, TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void ExplicitAddressInterruptsConversationButMentionDoesNot()
    {
        var match = WakePhraseParser.Parse("мы обсуждали планы слушай Егорик открой браузер", "Егорик");
        Assert.AreEqual(WakePhraseKind.Command, match.Kind);
        Assert.AreEqual("открой браузер", match.Command);
        Assert.IsFalse(match.IsApproximate);
        Assert.AreEqual(WakePhraseKind.WakeOnly,
            WakePhraseParser.Parse("мы говорили слушай Егорик", "Егорик").Kind);
        Assert.AreEqual(WakePhraseKind.NotAddressed,
            WakePhraseParser.Parse("я рассказывал про Егорика", "Егорик").Kind);
        Assert.AreEqual(WakePhraseKind.NotAddressed,
            WakePhraseParser.Parse("мы говорили Егорик открой браузер", "Егорик").Kind);
        Assert.AreEqual(WakePhraseKind.NotAddressed,
            WakePhraseParser.Parse("я рассказывал слушай Егориков", "Егорик").Kind);
    }

    [TestMethod]
    public async Task HelpOpensCatalogInsteadOfRecitingCommands()
    {
        var command = new HelpCommand();
        var match = await command.MatchAsync("список команд");
        Assert.IsNotNull(match);
        var result = await command.ExecuteAsync(new CommandContext("список команд", "список команд"), match);
        Assert.AreEqual(ClientAction.OpenCommandCatalog, result.ClientAction);
        Assert.AreEqual("Открываю справочник команд.", result.Message);
        Assert.IsNull(await command.MatchAsync("он рассказывал про список команд"));
    }

    [TestMethod]
    public void CatalogUsesPluginMetadataAndSavedTrustWithoutExecutingScripts()
    {
        var script = new LoadedAssistantPlugin(
            new PluginManifest("script", "Скрипт", "1", "1", [], Kind: "javascript",
                Description: "Описание", Examples: ["пример"]), "", [], "fingerprint");
        var manifest = new PluginCommandManifest("pick", "Выбор", "Выбирает героя", "pick", ["выбери"], "hero", "heroes.json");
        var declarative = new LoadedAssistantPlugin(
            new PluginManifest("declarative", "Декларативный", "1", "1", []), "",
            [new LoadedPluginCommand(manifest, [new PluginEntity("hero", "Герой", "hero", ["героя"])])], "");
        var settings = new AssistantSettings();
        settings.EnabledPlugins.Add("script");
        var catalog = CommandHelpCatalog.Build([new HelpCommand().Descriptor], [script, declarative], settings);
        Assert.AreEqual(3, catalog.Count);
        Assert.AreEqual("пример", catalog[1].Examples);
        StringAssert.Contains(catalog[1].Source, "выключен");
        Assert.AreEqual("выбери героя", catalog[2].Examples);
        settings.TrustedPluginFingerprints["script"] = "fingerprint";
        StringAssert.Contains(CommandHelpCatalog.Build([], [script], settings)[0].Source, "включён");
    }
}
