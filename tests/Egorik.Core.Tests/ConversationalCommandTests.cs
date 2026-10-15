using Egorik.Core;
using Egorik.Core.Ai;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Platform;
using Egorik.Core.Scenarios;
using Egorik.Infrastructure.Commands;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class ConversationalCommandTests
{
    [TestMethod]
    [DataRow("Егорик, можешь открыть мне браузер, пожалуйста?")]
    [DataRow("Будь добр, запусти пожалуйста брау зер")]
    [DataRow("Ты можешь запустить яндексмузыку?")]
    [DataRow("Егорик, от крой брау зер")]
    [DataRow("откройбраузер")]
    public async Task EngineExecutesConversationalOpeningLocally(string phrase)
    {
        var platform = new RecordingPlatform();
        var settings = new MemorySettings();
        var planner = new RecordingPlanner();
        var engine = new AssistantEngine(
            new CommandRouter([new OpenApplicationCommand(settings, platform)]),
            new MemoryScenarios(), settings, planner, new AiToolExecutor([]));

        var result = await engine.ExecuteAsync(phrase);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(phrase.Contains("яндекс", StringComparison.Ordinal) ? "music.exe" : "browser.exe", platform.Target);
        Assert.AreEqual(0, planner.Calls);
    }

    [TestMethod]
    [DataRow("не открой браузер")]
    [DataRow("можешь не открывать браузер")]
    [DataRow("я сказал открой браузер")]
    [DataRow("открои браузер")]
    [DataRow("открой браузер и выключи звук")]
    [DataRow("открой браузер не надо")]
    public async Task RouterRejectsNegationsQuotedCommandsAndUnlistedVerbTypos(string phrase)
    {
        var platform = new RecordingPlatform();
        var router = new CommandRouter([new OpenApplicationCommand(new MemorySettings(), platform)]);

        Assert.IsNull(await router.RouteAsync(new CommandContext(phrase, phrase)));
        Assert.IsNull(platform.Target);
    }

    [TestMethod]
    public async Task OriginalQueryKeepsPolitenessWordsAndBoundaries()
    {
        var platform = new RecordingPlatform();
        var router = new CommandRouter([new WebSearchCommand(new MemorySettings(), platform)]);
        const string text = "найди слово пожалуйста брау зер";

        var result = await router.RouteAsync(new CommandContext(text, text));

        Assert.IsTrue(result!.IsSuccess);
        Assert.AreEqual("слово пожалуйста брау зер", platform.Query);
    }

    [TestMethod]
    public async Task ConversationalSearchKeepsLiteralQuery()
    {
        var platform = new RecordingPlatform();
        var router = new CommandRouter([new WebSearchCommand(new MemorySettings(), platform)]);
        const string text = "можешь найти слово пожалуйста";

        Assert.IsTrue((await router.RouteAsync(new CommandContext(text, text)))!.IsSuccess);
        Assert.AreEqual("слово пожалуйста", platform.Query);
    }

    [TestMethod]
    public async Task ConversationalRequestSupportsMediaAndNavigation()
    {
        var platform = new RecordingPlatform();
        var router = new CommandRouter([new MediaCommand(platform), new NavigationCommand()]);
        const string media = "можешь поставить на паузу пожалуйста";
        const string navigation = "можешь показать мне настройки";

        Assert.IsTrue((await router.RouteAsync(new CommandContext(media, media)))!.IsSuccess);
        Assert.AreEqual(MediaAction.PlayPause, platform.Action);
        Assert.AreEqual(ClientAction.OpenSettings,
            (await router.RouteAsync(new CommandContext(navigation, navigation)))!.ClientAction);
    }

    [TestMethod]
    public async Task AmbiguousApplicationAliasIsNotLaunched()
    {
        var settings = new MemorySettings();
        settings.Current.Applications["яндексмузыка"] = "other.exe";
        var platform = new RecordingPlatform();
        var router = new CommandRouter([new OpenApplicationCommand(settings, platform)]);
        const string text = "можешь открыть ян дексмузыка";

        Assert.IsNull(await router.RouteAsync(new CommandContext(text, text)));
        Assert.IsNull(platform.Target);
    }

    [TestMethod]
    public async Task ConversationalParsingDoesNotApplyToHigherRiskCommands()
    {
        var command = new HigherRiskCommand();
        var router = new CommandRouter([command]);
        const string text = "пожалуйста открой настройки";

        Assert.IsNull(await router.RouteAsync(new CommandContext(text, text)));
        Assert.IsFalse(command.Executed);
    }

    [TestMethod]
    public async Task ScenarioRetainsPriorityOverConversationalCommand()
    {
        var platform = new RecordingPlatform();
        var settings = new MemorySettings();
        var planner = new RecordingPlanner();
        var engine = new AssistantEngine(
            new CommandRouter([new OpenApplicationCommand(settings, platform), new NavigationCommand()]),
            new MemoryScenarios([new AssistantScenario
            {
                Name = "Тест", Trigger = "можешь открыть браузер", Commands = ["открой настройки"]
            }]), settings, planner, new AiToolExecutor([]));

        Assert.IsTrue((await engine.ExecuteAsync("можешь открыть браузер")).IsSuccess);
        Assert.IsNull(platform.Target);
        Assert.AreEqual(0, planner.Calls);
    }

    private sealed class HigherRiskCommand : IAssistantCommand
    {
        public CommandDescriptor Descriptor { get; } = new("test.high", "Тест", "Тест", CommandRisk.High);
        public bool Executed { get; private set; }

        public ValueTask<CommandMatch?> MatchAsync(string normalizedText, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(normalizedText == "открой настройки" ? CommandMatch.Create(1) : null);

        public ValueTask<CommandResult> ExecuteAsync(CommandContext context, CommandMatch match, CancellationToken cancellationToken = default)
        {
            Executed = true;
            return ValueTask.FromResult(CommandResult.Success("Готово"));
        }
    }

    private sealed class MemorySettings : IAssistantSettingsService
    {
        public AssistantSettings Current { get; } = new()
        {
            Applications = new(StringComparer.OrdinalIgnoreCase)
            {
                ["браузер"] = "browser.exe",
                ["яндекс музыка"] = "music.exe"
            }
        };
        public ValueTask LoadAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class MemoryScenarios(IReadOnlyList<AssistantScenario>? scenarios = null) : IScenarioRepository
    {
        public ValueTask<IReadOnlyList<AssistantScenario>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<AssistantScenario>>(scenarios ?? []);
        public ValueTask SaveAsync(IReadOnlyCollection<AssistantScenario> updated, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingPlanner : IAiPlanner
    {
        public bool IsConfigured => true;
        public int Calls { get; private set; }
        public ValueTask<AiPlan?> CreatePlanAsync(string userRequest, IReadOnlyList<AiToolDescriptor> availableTools, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult<AiPlan?>(null);
        }
    }

    private sealed class RecordingPlatform : IApplicationLauncher, IWebBrowserService, IMediaControlService
    {
        public string? Target { get; private set; }
        public string? Query { get; private set; }
        public MediaAction? Action { get; private set; }

        public ValueTask<LaunchResult> LaunchAsync(string target, CancellationToken cancellationToken = default)
        {
            Target = target;
            return ValueTask.FromResult(new LaunchResult(true, "Готово"));
        }

        public ValueTask<LaunchResult> OpenUrlAsync(Uri uri, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new LaunchResult(true, "Готово"));

        public ValueTask<LaunchResult> SearchAsync(string query, string provider, CancellationToken cancellationToken = default)
        {
            Query = query;
            return ValueTask.FromResult(new LaunchResult(true, "Готово"));
        }

        public ValueTask<LaunchResult> ExecuteAsync(MediaAction action, CancellationToken cancellationToken = default)
        {
            Action = action;
            return ValueTask.FromResult(new LaunchResult(true, "Готово"));
        }
    }
}
