using Egorik.Core.Ai;
using Egorik.Core.Applications;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Platform;
using Egorik.Infrastructure.Ai;
using Egorik.Infrastructure.Applications;
using Egorik.Infrastructure.Commands;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class ApplicationAutomationTests
{
    [TestMethod]
    [DataRow("сделай музыку тише", "volume_down", YandexMusicAction.VolumeDown)]
    [DataRow("сделай яндекс музыку громче", "volume_up", YandexMusicAction.VolumeUp)]
    [DataRow("убавь громкость яндекс музыки", "volume_down", YandexMusicAction.VolumeDown)]
    [DataRow("сделай тише в яндекс музыке", "volume_down", YandexMusicAction.VolumeDown)]
    [DataRow("выключи звук в яндекс музыке", "mute", YandexMusicAction.Mute)]
    public async Task MusicVolumeUsesAddressedControllerInsteadOfGlobalMedia(string phrase, string action, YandexMusicAction expected)
    {
        var controller = new RecordingYandexMusicController();
        var registry = new ApplicationActionRegistry([new YandexMusicApplicationIntegration(controller)]);
        var global = new RecordingMediaControl();
        var router = new CommandRouter([new ApplicationActionCommand(registry, new FakeSettingsService()), new MediaCommand(global)]);

        var result = await router.RouteAsync(new CommandContext(phrase, phrase));

        Assert.IsTrue(result!.IsSuccess);
        CollectionAssert.AreEqual(new[] { expected }, controller.Actions.ToArray());
        Assert.AreEqual(0, global.Calls);
        Assert.IsTrue(registry.AvailableApplications[0].Actions.Any(candidate => candidate.Id == action));
    }

    [TestMethod]
    public async Task MusicVolumeSupportsPercentagesFromLocalCommandAndValidatedAiPlan()
    {
        var controller = new RecordingYandexMusicController();
        var registry = new ApplicationActionRegistry([new YandexMusicApplicationIntegration(controller)]);
        var command = new ApplicationActionCommand(registry, new FakeSettingsService());
        var text = TextNormalizer.Normalize("громкость яндекс музыки на 50%");
        var match = await command.MatchAsync(text);
        Assert.IsNotNull(match);
        Assert.IsTrue((await command.ExecuteAsync(new CommandContext(text, text), match)).IsSuccess);
        Assert.AreEqual(50, controller.VolumePercent);

        var executor = new AiToolExecutor([new ApplicationActionAiTool(registry)]);
        var result = await executor.ExecuteAsync(new AiPlan([new PlannedToolCall("application_action",
            new Dictionary<string, string> { ["operation"] = "yandex_music.set_volume", ["input"] = "25" })]));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(25, controller.VolumePercent);
        Assert.AreEqual(0, controller.Actions.Count);
    }

    [TestMethod]
    [DataRow("101")]
    [DataRow("-1")]
    [DataRow("50.5")]
    [DataRow("shell")]
    public async Task MusicVolumeRejectsDisallowedAiValuesBeforeController(string value)
    {
        var controller = new RecordingYandexMusicController();
        var registry = new ApplicationActionRegistry([new YandexMusicApplicationIntegration(controller)]);
        var executor = new AiToolExecutor([new ApplicationActionAiTool(registry)]);

        var result = await executor.ExecuteAsync(new AiPlan([new PlannedToolCall("application_action",
            new Dictionary<string, string> { ["operation"] = "yandex_music.set_volume", ["input"] = value })]));

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(controller.VolumePercent);
    }

    [TestMethod]
    public async Task MusicVolumeIntegrationRejectsExtraArgumentsWhenCalledDirectly()
    {
        var controller = new RecordingYandexMusicController();
        var integration = new YandexMusicApplicationIntegration(controller);

        Assert.IsFalse((await integration.ExecuteAsync(new ApplicationActionRequest("yandex_music", "set_volume",
            new Dictionary<string, string> { ["percent"] = "50", ["extra"] = "bad" }))).IsSuccess);
        Assert.IsFalse((await integration.ExecuteAsync(new ApplicationActionRequest("yandex_music", "volume_up",
            new Dictionary<string, string> { ["percent"] = "50" }))).IsSuccess);
        Assert.IsNull(controller.VolumePercent);
        Assert.IsEmpty(controller.Actions);
    }

    [TestMethod]
    [DataRow("сделай плеер тише")]
    [DataRow("не сделай музыку громче")]
    [DataRow("сделай музыку громче и выключи компьютер")]
    [DataRow("громкость яндекс музыки на 101 процентов")]
    public async Task MusicVolumeRejectsOtherTargetsNegationsAndCompoundRequests(string phrase)
    {
        var controller = new RecordingYandexMusicController();
        var command = new ApplicationActionCommand(new ApplicationActionRegistry([new YandexMusicApplicationIntegration(controller)]), new FakeSettingsService());

        Assert.IsNull(await command.MatchAsync(phrase));
        Assert.IsEmpty(controller.Actions);
    }

    private sealed class RecordingMediaControl : IMediaControlService
    {
        public int Calls { get; private set; }
        public ValueTask<LaunchResult> ExecuteAsync(MediaAction action, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(new LaunchResult(true, "Готово"));
        }
    }
    [TestMethod]
    public void Registry_RejectsActionAboveAllowedRisk()
    {
        var integration = new RecordingIntegration(
            "editor",
            new ApplicationActionDescriptor(
                "overwrite",
                "Перезаписать",
                "Опасное действие.",
                [],
                CommandRisk.Medium));
        var registry = new ApplicationActionRegistry([integration]);

        var validation = registry.Validate(
            Request("editor", "overwrite"),
            CommandRisk.Low);

        Assert.IsFalse(validation.IsValid);
        StringAssert.Contains(validation.Error, "подтверждения");
        Assert.AreEqual(0, integration.ExecutionCount);
    }

    [TestMethod]
    public void Registry_RejectsUnexpectedArgumentBeforeExecution()
    {
        var integration = new RecordingIntegration(
            "player",
            new ApplicationActionDescriptor(
                "next",
                "Следующий трек",
                "Переключает трек.",
                []));
        var registry = new ApplicationActionRegistry([integration]);

        var validation = registry.Validate(
            Request("player", "next", ("path", "C:\\unsafe.exe")),
            CommandRisk.Low);

        Assert.IsFalse(validation.IsValid);
        StringAssert.Contains(validation.Error, "не принимает аргумент");
        Assert.AreEqual(0, integration.ExecutionCount);
    }

    [TestMethod]
    public async Task Command_LaunchesOnlyExactSteamGamePattern()
    {
        var integration = new RecordingIntegration(
            "steam",
            new ApplicationActionDescriptor(
                "launch_game",
                "Запустить игру",
                "Запускает установленную игру.",
                [new ApplicationActionParameterDescriptor("game", "Название игры.")]),
            aliases: ["steam", "стим"]);
        var registry = new ApplicationActionRegistry([integration]);
        var command = new ApplicationActionCommand(registry, new FakeSettingsService());

        var unrelated = await command.MatchAsync("найди работу разработчика");
        var match = await command.MatchAsync("запусти dota 2 в стиме");
        var result = await command.ExecuteAsync(
            new CommandContext("запусти Dota 2 в Стиме", "запусти dota 2 в стиме"),
            match!);

        Assert.IsNull(unrelated);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("steam", integration.LastRequest?.ApplicationId);
        Assert.AreEqual("launch_game", integration.LastRequest?.ActionId);
        Assert.AreEqual("dota 2", integration.LastRequest?.Arguments["game"]);
    }

    [TestMethod]
    public async Task Command_MatchesKnownSteamAliasWithOneRecognitionError()
    {
        var integration = new RecordingIntegration(
            "steam",
            new ApplicationActionDescriptor(
                "launch_game",
                "Запустить игру",
                "Запускает установленную игру.",
                [new ApplicationActionParameterDescriptor("game", "Название игры.")]),
            aliases: ["steam", "стим"]);
        var command = new ApplicationActionCommand(
            new ApplicationActionRegistry([integration]),
            new FakeSettingsService());

        var match = await command.MatchAsync("запусти доту в стиле");

        Assert.IsNotNull(match);
        Assert.AreEqual("steam", match.Argument("application"));
        Assert.AreEqual("launch_game", match.Argument("action"));
        Assert.AreEqual("доту", match.Argument("game"));
    }

    [TestMethod]
    public async Task Command_UsesConfiguredInflectedYandexMusicAlias()
    {
        var settings = new FakeSettingsService();
        settings.Current.Applications["музыка"] = "Яндекс Музыка.exe";
        var controller = new RecordingYandexMusicController();
        var command = new ApplicationActionCommand(
            new ApplicationActionRegistry([
                new YandexMusicApplicationIntegration(controller)
            ]),
            settings);

        var openMatch = await command.MatchAsync("открой музыку");
        var waveMatch = await command.MatchAsync("включи волну в музыке");
        var nextMatch = await command.MatchAsync("переключи трек в музыке");

        Assert.IsNotNull(openMatch);
        Assert.IsNotNull(waveMatch);
        Assert.IsNotNull(nextMatch);
        await command.ExecuteAsync(
            new CommandContext("открой музыку", "открой музыку"),
            openMatch);
        await command.ExecuteAsync(
            new CommandContext("включи волну в музыке", "включи волну в музыке"),
            waveMatch);
        await command.ExecuteAsync(
            new CommandContext("переключи трек в музыке", "переключи трек в музыке"),
            nextMatch);

        Assert.AreEqual(1, controller.OpenCount);
        Assert.AreEqual(1, controller.MyWaveCount);
        CollectionAssert.AreEqual(
            new[] { YandexMusicAction.Next },
            controller.Actions.ToArray());
    }

    [TestMethod]
    public async Task OpenApplicationCommand_MatchesConfiguredAliasInflection()
    {
        var settings = new FakeSettingsService();
        settings.Current.Applications["музыка"] = "music.exe";
        var launcher = new RecordingLauncher();
        var command = new OpenApplicationCommand(settings, launcher);

        var match = await command.MatchAsync("открой музыку");
        Assert.IsNotNull(match);
        var result = await command.ExecuteAsync(
            new CommandContext("открой музыку", "открой музыку"),
            match);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("music.exe", launcher.LastTarget);
    }

    [TestMethod]
    public async Task Command_UsesOnlyConfiguredWorkbookAlias()
    {
        var settings = new FakeSettingsService();
        settings.Current.Documents["бюджет"] = "D:\\Documents\\budget.xlsx";
        var integration = new RecordingIntegration(
            "microsoft_excel",
            new ApplicationActionDescriptor(
                "search_workbook",
                "Поиск",
                "Ищет в книге.",
                [
                    new ApplicationActionParameterDescriptor("document", "Псевдоним."),
                    new ApplicationActionParameterDescriptor("query", "Запрос.")
                ]));
        var command = new ApplicationActionCommand(
            new ApplicationActionRegistry([integration]),
            settings);

        var unknown = await command.MatchAsync("найди расходы в таблице секреты");
        var match = await command.MatchAsync("найди расходы в таблице бюджет");

        Assert.IsNull(unknown);
        Assert.IsNotNull(match);
        Assert.AreEqual("бюджет", match.Argument("document"));
        Assert.AreEqual("расходы", match.Argument("query"));
    }

    [TestMethod]
    public async Task Command_PreservesOriginalWordNoteTextDuringExecution()
    {
        var integration = new RecordingIntegration(
            "microsoft_word",
            new ApplicationActionDescriptor(
                "create_note",
                "Создать заметку",
                "Создаёт новую заметку.",
                [new ApplicationActionParameterDescriptor("text", "Текст.", MaxLength: 4_000)]));
        var command = new ApplicationActionCommand(
            new ApplicationActionRegistry([integration]),
            new FakeSettingsService());
        var match = await command.MatchAsync("запиши в ворд купить молоко хлеб");

        var result = await command.ExecuteAsync(
            new CommandContext(
                "Егорик, запиши в ворд Купить молоко, хлеб и чай.",
                "запиши в ворд купить молоко хлеб"),
            match!);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            "Купить молоко, хлеб и чай.",
            integration.LastRequest?.Arguments["text"]);
    }

    [TestMethod]
    public async Task AiTool_ExposesOnlyExplicitlyAllowedLowRiskActions()
    {
        var integration = new RecordingIntegration(
            "notes",
            [
                new ApplicationActionDescriptor(
                    "open",
                    "Открыть",
                    "Открывает приложение.",
                    [],
                    IsAvailableToAi: true),
                new ApplicationActionDescriptor(
                    "create",
                    "Создать",
                    "Создаёт файл.",
                    [new ApplicationActionParameterDescriptor("text", "Текст.")])
            ]);
        var tool = new ApplicationActionAiTool(
            new ApplicationActionRegistry([integration]));
        var allowed = tool.Descriptor.Parameters
            .Single(parameter => parameter.Name == "operation")
            .AllowedValues;

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["operation"] = "notes.open"
        });

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] { "notes.open" }, allowed?.ToArray());
        Assert.AreEqual("open", integration.LastRequest?.ActionId);
    }

    [TestMethod]
    public async Task SteamIntegration_LaunchesOnlyNumericInstalledAppId()
    {
        var launcher = new RecordingLauncher();
        var integration = new SteamApplicationIntegration(
            new FakeSteamGameCatalog([new InstalledSteamGame("570", "Dota 2")]),
            launcher,
            new RecordingBrowser());

        var result = await integration.ExecuteAsync(Request(
            "steam",
            "launch_game",
            ("game", "dota 2")));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("steam://rungameid/570", launcher.LastTarget);
    }

    [TestMethod]
    [DataRow("Dota 2", 100)]
    [DataRow("дота 2", 100)]
    [DataRow("доту", 90)]
    public void SteamGameNameMatcher_MatchesSpokenRussianGameName(
        string query,
        int expectedScore)
    {
        Assert.AreEqual(expectedScore, SteamGameNameMatcher.MatchScore(query, "Dota 2"));
    }

    [TestMethod]
    public void SteamGameNameMatcher_RejectsUnrelatedGame()
    {
        Assert.AreEqual(0, SteamGameNameMatcher.MatchScore("доту", "Stardew Valley"));
    }

    private static ApplicationActionRequest Request(
        string application,
        string action,
        params (string Name, string Value)[] arguments) =>
        new(
            application,
            action,
            arguments.ToDictionary(argument => argument.Name, argument => argument.Value));

    private sealed class RecordingIntegration : IApplicationIntegration
    {
        public RecordingIntegration(
            string id,
            ApplicationActionDescriptor action,
            IReadOnlyList<string>? aliases = null)
            : this(id, [action], aliases)
        {
        }

        public RecordingIntegration(
            string id,
            IReadOnlyList<ApplicationActionDescriptor> actions,
            IReadOnlyList<string>? aliases = null)
        {
            Descriptor = new ApplicationIntegrationDescriptor(
                id,
                id,
                aliases ?? [id],
                actions);
        }

        public ApplicationIntegrationDescriptor Descriptor { get; }

        public int ExecutionCount { get; private set; }

        public ApplicationActionRequest? LastRequest { get; private set; }

        public ValueTask<CommandResult> ExecuteAsync(
            ApplicationActionRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            LastRequest = request;
            return ValueTask.FromResult(CommandResult.Success("Готово."));
        }
    }

    private sealed class FakeSettingsService : IAssistantSettingsService
    {
        public AssistantSettings Current { get; } = new();

        public ValueTask LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SaveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeSteamGameCatalog(IReadOnlyList<InstalledSteamGame> games)
        : ISteamGameCatalog
    {
        public ValueTask<IReadOnlyList<InstalledSteamGame>> FindAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(games);
    }

    private sealed class RecordingLauncher : Egorik.Core.Platform.IApplicationLauncher
    {
        public string? LastTarget { get; private set; }

        public ValueTask<Egorik.Core.Platform.LaunchResult> LaunchAsync(
            string target,
            CancellationToken cancellationToken = default)
        {
            LastTarget = target;
            return ValueTask.FromResult(new Egorik.Core.Platform.LaunchResult(true, "Готово."));
        }
    }

    private sealed class RecordingBrowser : Egorik.Core.Platform.IWebBrowserService
    {
        public ValueTask<Egorik.Core.Platform.LaunchResult> OpenUrlAsync(
            Uri uri,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new Egorik.Core.Platform.LaunchResult(true, "Готово."));

        public ValueTask<Egorik.Core.Platform.LaunchResult> SearchAsync(
            string query,
            string provider,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new Egorik.Core.Platform.LaunchResult(true, "Готово."));
    }

    private sealed class RecordingYandexMusicController : IYandexMusicController
    {
        public int? VolumePercent { get; private set; }

        public ValueTask<LaunchResult> SetVolumeAsync(int percent, CancellationToken cancellationToken = default)
        {
            VolumePercent = percent;
            return Success();
        }

        public int OpenCount { get; private set; }

        public int MyWaveCount { get; private set; }

        public List<YandexMusicAction> Actions { get; } = [];

        public ValueTask<LaunchResult> OpenAsync(
            CancellationToken cancellationToken = default)
        {
            OpenCount++;
            return Success();
        }

        public ValueTask<LaunchResult> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            Success();

        public ValueTask<LaunchResult> StartMyWaveAsync(
            CancellationToken cancellationToken = default)
        {
            MyWaveCount++;
            return Success();
        }

        public ValueTask<LaunchResult> ExecuteAsync(
            YandexMusicAction action,
            CancellationToken cancellationToken = default)
        {
            Actions.Add(action);
            return Success();
        }

        private static ValueTask<LaunchResult> Success() =>
            ValueTask.FromResult(new LaunchResult(true, "Готово."));
    }
}
