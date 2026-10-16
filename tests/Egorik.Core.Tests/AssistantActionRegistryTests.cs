using Egorik.Core.Actions;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Plugins;
using Egorik.Infrastructure.Actions;
using Egorik.Infrastructure.Ai;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class AssistantActionRegistryTests
{
    [TestMethod]
    [DataRow("unknown", "value")]
    [DataRow("choice", "forbidden")]
    [DataRow("count", "101")]
    [DataRow("count", "-1")]
    [DataRow("count", "1.5")]
    [DataRow("flag", "yes")]
    [DataRow("text", "too long")]
    [DataRow("text", "a\nb")]
    [DataRow("text", "")]
    public async Task InvalidTypedOrExtraArgumentsNeverReachProvider(string name, string value)
    {
        var provider = new Provider();
        var registry = new AssistantActionRegistry([provider]);
        var arguments = ValidArguments();
        arguments[name] = value;
        var result = await registry.ExecuteAsync(new("test.action", arguments));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, provider.Calls);
    }

    [TestMethod]
    public async Task ValidTypedArgumentsExecuteAndRequiredMissingArgumentsFail()
    {
        var provider = new Provider();
        var registry = new AssistantActionRegistry([provider]);
        Assert.IsTrue((await registry.ExecuteAsync(new("test.action", ValidArguments()))).IsSuccess);
        Assert.AreEqual(1, provider.Calls);
        var arguments = ValidArguments();
        arguments.Remove("choice");
        Assert.IsFalse(registry.Validate(new("test.action", arguments)).IsValid);
        Assert.IsFalse(registry.Validate(new("test.missing", arguments)).IsValid);
        Assert.IsFalse(registry.Validate(new("test.action", null!)).IsValid);
        arguments["choice"] = null!;
        Assert.IsFalse(registry.Validate(new("test.action", arguments)).IsValid);
    }

    [TestMethod]
    public async Task EmptyAllowlistAndHighRiskAreDeniedAndCancellationHasNoEffects()
    {
        var provider = new Provider();
        provider.Definitions = [provider.Definitions[0] with { Parameters = [new("choice", "Выбор", ActionParameterKind.Choice, AllowedValues: [])] }];
        var registry = new AssistantActionRegistry([provider]);
        Assert.IsFalse(registry.Validate(new("test.action", new Dictionary<string, string> { ["choice"] = "anything" })).IsValid);
        provider.Definitions = [provider.Definitions[0] with { Risk = CommandRisk.High }];
        Assert.AreEqual(0, registry.AvailableActions.Count);
        Assert.IsFalse((await registry.ExecuteAsync(new("test.action", new Dictionary<string, string>()))).IsSuccess);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await registry.ExecuteAsync(new("test.action", ValidArguments()), cancellation.Token));
        Assert.AreEqual(0, provider.Calls);
    }

    [TestMethod]
    public void IncompatibleApiDuplicateProviderAndForgedNamespaceAreRejected()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new AssistantActionRegistry([new Provider { Version = 2 }]));
        Assert.ThrowsExactly<InvalidOperationException>(() => new AssistantActionRegistry([new Provider(), new Provider()]));
        var forged = new Provider();
        forged.Definitions = [forged.Definitions[0] with { Id = "other.action" }];
        Assert.ThrowsExactly<InvalidOperationException>(() => new AssistantActionRegistry([forged]));
        var duplicate = new Provider();
        duplicate.Definitions = [duplicate.Definitions[0], duplicate.Definitions[0]];
        Assert.ThrowsExactly<InvalidOperationException>(() => new AssistantActionRegistry([duplicate]));
    }

    [TestMethod]
    public async Task RegistryRechecksAvailabilityImmediatelyBeforeExecution()
    {
        var provider = new Provider();
        var registry = new AssistantActionRegistry([provider]);
        var invocation = new ActionInvocation("test.action", ValidArguments());
        Assert.IsTrue(registry.Validate(invocation).IsValid);
        provider.Definitions = [];
        Assert.IsFalse((await registry.ExecuteAsync(invocation)).IsSuccess);
        Assert.AreEqual(0, provider.Calls);
    }

    [TestMethod]
    public async Task BuiltInAdapterExecutesLocalImplementationWithoutAiPlanner()
    {
        var provider = new BuiltInActionProvider([new AssistantReplyAiTool()]);
        var registry = new AssistantActionRegistry([provider]);
        var result = await registry.ExecuteAsync(new("builtin.assistant_reply", new Dictionary<string, string> { ["message"] = "Привет" }));
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Привет", result.Message);
    }

    [TestMethod]
    public async Task PluginAdapterRequiresEnabledPluginDeclaredCapabilityAndLowRiskHostHandler()
    {
        var settings = new MemorySettings();
        var handler = new Handler();
        var command = new LoadedPluginCommand(new("pick", "Выбрать", "", "test.pick", ["выбери"], "entity", "entities.json"),
            [new("hero", "Герой", "safe-value", [])]);
        var plugin = new LoadedAssistantPlugin(new("example", "Пример", "1", "1", ["test.pick"]), "", [command], "");
        var catalog = new Catalog([plugin]);
        var registry = new AssistantActionRegistry([new DeclarativePluginActionProvider(catalog, settings, [handler])]);
        var invocation = new ActionInvocation("plugins.example.pick", new Dictionary<string, string> { ["entity"] = "hero" });
        Assert.IsFalse(registry.Validate(invocation).IsValid);
        settings.Current.EnabledPlugins.Add("example");
        Assert.IsTrue((await registry.ExecuteAsync(invocation)).IsSuccess);
        Assert.AreEqual("safe-value", handler.LastValue);
        settings.Current.EnabledPlugins.Clear();
        Assert.IsFalse((await registry.ExecuteAsync(invocation)).IsSuccess);
        Assert.AreEqual(1, handler.Calls);
        settings.Current.EnabledPlugins.Add("example");
        var high = new AssistantActionRegistry([new DeclarativePluginActionProvider(catalog, settings, [new Handler { Risk = CommandRisk.High }])]);
        Assert.AreEqual(0, high.AvailableActions.Count);
        var noGrant = new Catalog([plugin with { Manifest = plugin.Manifest with { Permissions = [] } }]);
        Assert.AreEqual(0, new DeclarativePluginActionProvider(noGrant, settings, [handler]).Actions.Count);
    }

    private static Dictionary<string, string> ValidArguments() => new() { ["choice"] = "allowed", ["count"] = "42", ["flag"] = "true", ["text"] = "ok" };

    private sealed class Provider : IAssistantActionProvider
    {
        public int Version { get; init; } = 1;
        public int Calls { get; private set; }
        public ActionProviderManifest Manifest => new("test", "Тест", Version);
        public IReadOnlyList<AssistantActionDescriptor> Definitions { get; set; } =
        [new("test.action", "Тест", "", [new("choice", "Выбор", ActionParameterKind.Choice, AllowedValues: ["allowed"]),
            new("count", "Число", ActionParameterKind.Integer), new("flag", "Флаг", ActionParameterKind.Boolean), new("text", "Текст", MaxLength: 4)])];
        public IReadOnlyList<AssistantActionDescriptor> Actions => Definitions;
        public ValueTask<CommandResult> ExecuteAsync(ActionInvocation invocation, CancellationToken cancellationToken = default)
        { Calls++; return ValueTask.FromResult(CommandResult.Success("Готово")); }
    }

    private sealed class MemorySettings : IAssistantSettingsService
    {
        public AssistantSettings Current { get; } = new();
        public ValueTask LoadAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class Catalog(IReadOnlyList<LoadedAssistantPlugin> plugins) : IPluginCatalog
    {
        public IReadOnlyList<LoadedAssistantPlugin> Plugins => plugins;
        public IReadOnlyList<PluginLoadIssue> Issues => [];
        public string UserPluginsDirectory => "";
    }

    private sealed class Handler : IPluginCapabilityHandler
    {
        public string Capability => "test.pick";
        public CommandRisk Risk { get; init; } = CommandRisk.Low;
        public int Calls { get; private set; }
        public string? LastValue { get; private set; }
        public ValueTask<CommandResult> ExecuteAsync(PluginCapabilityRequest request, CancellationToken cancellationToken = default)
        { Calls++; LastValue = request.Value; return ValueTask.FromResult(CommandResult.Success("Готово")); }
    }
}
