using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Plugins;
using Egorik.Infrastructure.Plugins;
using Egorik.Infrastructure.Platform;
using System.Diagnostics;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class PluginTests
{
    [TestMethod]
    public void DotaPicker_UsesNativeWindowsInputStructureSize()
    {
        Assert.AreEqual(IntPtr.Size == 8 ? 40 : 28, DesktopDotaHeroPicker.WindowsInputStructureSize);
    }

    [TestMethod]
    public void DotaPicker_TargetsCenterOfConfirmButtonAtScreenshotResolution()
    {
        var point = DesktopDotaHeroPicker.HeroConfirmPointForClient(1868, 1079);

        Assert.IsInRange(1425, 1430, point.X);
        Assert.IsInRange(825, 830, point.Y);
    }

    [TestMethod]
    public void Catalog_LoadsBundledDotaJavaScriptPlugin()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "plugins");
        var catalog = new JsonPluginCatalog([root], root);

        Assert.AreEqual(0, catalog.Issues.Count, DescribeIssues(catalog));
        var plugin = catalog.Plugins.Single(candidate =>
            candidate.Manifest.Id == "egorik.dota2.hero-picker");

        Assert.IsTrue(plugin.IsScriptPlugin);
        Assert.IsTrue(File.Exists(plugin.EntryPointPath));
        Assert.IsTrue(plugin.DataFilePaths?.ContainsKey("heroes.json"));
        Assert.AreEqual(64, plugin.Fingerprint.Length);
    }

    [TestMethod]
    public async Task JavaScriptCommand_RequiresTrustAndMatchesExplicitSlangCommand()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "plugins");
        var catalog = new JsonPluginCatalog([root], root);
        var plugin = catalog.Plugins.Single();
        var settings = new MemorySettingsService();
        var handler = new RecordingCapabilityHandler();
        var command = new JavaScriptPluginCommand(catalog, settings, [handler]);

        Assert.IsNull(await command.MatchAsync("пикни пуджа"));

        settings.Current.EnabledPlugins.Add(plugin.Manifest.Id);
        settings.Current.TrustedPluginFingerprints[plugin.Manifest.Id] = plugin.Fingerprint;
        Assert.IsNull(await command.MatchAsync("пудж"));
        Assert.IsNull(await command.MatchAsync("пикни пужда"));

        var pudge = await command.MatchAsync("пикни пуджа");
        var spiritBreaker = await command.MatchAsync("возьми баратрума");
        var screenshotCommand = await command.MatchAsync("выбери героя бару");

        Assert.IsNotNull(pudge);
        Assert.IsNotNull(spiritBreaker);
        Assert.IsNotNull(screenshotCommand);

        var result = await command.ExecuteAsync(
            new CommandContext("пикни пуджа", "пикни пуджа"),
            pudge);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Pudge", handler.LastRequest?.Value);
        Assert.AreEqual("javascript", handler.LastRequest?.CommandId);
    }

    [TestMethod]
    public async Task JavaScriptCommand_RejectsPackageChangedAfterTrust()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "plugins", "egorik.dota2.hero-picker");
        var root = Path.Combine(Path.GetTempPath(), $"egorik-plugin-test-{Guid.NewGuid():N}");
        var copy = Path.Combine(root, "egorik.dota2.hero-picker");
        Directory.CreateDirectory(copy);
        try
        {
            foreach (var file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
            }

            var catalog = new JsonPluginCatalog([root], root);
            var plugin = catalog.Plugins.Single();
            var settings = new MemorySettingsService();
            settings.Current.EnabledPlugins.Add(plugin.Manifest.Id);
            settings.Current.TrustedPluginFingerprints[plugin.Manifest.Id] = plugin.Fingerprint;
            var command = new JavaScriptPluginCommand(
                catalog,
                settings,
                [new RecordingCapabilityHandler()]);

            File.AppendAllText(plugin.EntryPointPath!, Environment.NewLine + "// changed");

            Assert.IsNull(await command.MatchAsync("пикни пуджа"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void Catalog_RejectsDirectoryContainingExecutableDll()
    {
        var root = Path.Combine(Path.GetTempPath(), $"egorik-plugin-test-{Guid.NewGuid():N}");
        var pluginDirectory = Path.Combine(root, "unsafe");
        Directory.CreateDirectory(pluginDirectory);
        try
        {
            File.WriteAllText(Path.Combine(pluginDirectory, "plugin.json"), "{}");
            File.WriteAllBytes(Path.Combine(pluginDirectory, "unsafe.dll"), [0]);

            var catalog = new JsonPluginCatalog([root], root);

            Assert.AreEqual(0, catalog.Plugins.Count);
            Assert.AreEqual(1, catalog.Issues.Count);
            StringAssert.Contains(catalog.Issues[0].Message, "DLL-плагины запрещены");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task JavaScriptPlugin_HasNoClrOrProcessAccessAndRunawayCodeIsBounded()
    {
        var root = Path.Combine(Path.GetTempPath(), $"egorik-plugin-test-{Guid.NewGuid():N}");
        var pluginDirectory = Path.Combine(root, "example.sandbox");
        Directory.CreateDirectory(pluginDirectory);
        try
        {
            File.WriteAllText(
                Path.Combine(pluginDirectory, "plugin.json"),
                """
                {
                  "id": "example.sandbox",
                  "name": "Sandbox test",
                  "version": "1.0.0",
                  "apiVersion": "1",
                  "kind": "javascript",
                  "entryPoint": "plugin.js",
                  "permissions": []
                }
                """);
            File.WriteAllText(
                Path.Combine(pluginDirectory, "plugin.js"),
                """
                function match() {
                  if (typeof System !== "undefined" || typeof require !== "undefined" ||
                      typeof process !== "undefined") {
                    return { matched: true };
                  }
                  while (true) {}
                }
                function execute() { return null; }
                """);

            var catalog = new JsonPluginCatalog([root], root);
            var plugin = catalog.Plugins.Single();
            var settings = new MemorySettingsService();
            settings.Current.EnabledPlugins.Add(plugin.Manifest.Id);
            settings.Current.TrustedPluginFingerprints[plugin.Manifest.Id] = plugin.Fingerprint;
            var command = new JavaScriptPluginCommand(catalog, settings, []);
            var stopwatch = Stopwatch.StartNew();

            Assert.IsNull(await command.MatchAsync("test"));
            Assert.IsLessThan(TimeSpan.FromSeconds(2), stopwatch.Elapsed);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string DescribeIssues(IPluginCatalog catalog) =>
        string.Join(Environment.NewLine, catalog.Issues.Select(issue => issue.Message));

    private sealed class MemorySettingsService : IAssistantSettingsService
    {
        public AssistantSettings Current { get; } = new();

        public ValueTask LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask SaveAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingCapabilityHandler : IPluginCapabilityHandler
    {
        public string Capability => "dota2.hero_pick";

        public CommandRisk Risk => CommandRisk.Medium;

        public PluginCapabilityRequest? LastRequest { get; private set; }

        public ValueTask<CommandResult> ExecuteAsync(
            PluginCapabilityRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return ValueTask.FromResult(CommandResult.Success("Готово."));
        }
    }
}
