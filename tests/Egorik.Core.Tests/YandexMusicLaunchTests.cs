using Egorik.Core.Platform;
using Egorik.Infrastructure.Platform;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class YandexMusicLaunchTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OpenRunningApplicationNeverStartsAnotherProcess(bool windowAvailable)
    {
        var launcher = new RecordingLauncher();
        var window = new WindowService { Result = new LaunchResult(windowAvailable, "Окно уже запущенного приложения") };
        var controller = CreateController(launcher, window);

        var result = await controller.OpenAsync();

        Assert.AreEqual(windowAvailable, result.IsSuccess);
        Assert.AreEqual(window.Result, result);
        Assert.AreEqual(1, window.Calls);
        Assert.IsEmpty(launcher.Targets);
    }

    [TestMethod]
    public async Task OpenStoppedApplicationLaunchesOnlyLocatedExecutable()
    {
        var launcher = new RecordingLauncher();
        var controller = CreateController(launcher, new WindowService());

        var result = await controller.OpenAsync();

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] { Locator.Executable }, launcher.Targets);
    }

    [TestMethod]
    public async Task SearchAndWaveUseProtocolInsteadOfExecutableArguments()
    {
        var launcher = new RecordingLauncher();
        var window = new WindowService();
        var controller = CreateController(launcher, window);
        using var cancellation = new CancellationTokenSource();
        const string query = "  Трек &text=другой \" --flag  ";

        await controller.SearchAsync(query, cancellation.Token);
        await controller.StartMyWaveAsync(cancellation.Token);

        CollectionAssert.AreEqual(new[]
        {
            "yandexmusic://search?text=" + Uri.EscapeDataString(query.Trim()),
            "yandexmusic://play-vibe/"
        }, launcher.Targets);
        Assert.AreEqual(cancellation.Token, launcher.LastToken);
        Assert.AreEqual(0, window.Calls);
    }

    [TestMethod]
    public async Task MissingInstallationAndInvalidSearchDoNotLaunchAnything()
    {
        var launcher = new RecordingLauncher();
        var controller = new DesktopYandexMusicController(
            new Locator { IsInstalled = false }, new UnusedVolumeService(), new WindowService(), launcher);

        Assert.IsFalse((await controller.OpenAsync()).IsSuccess);
        Assert.IsFalse((await controller.StartMyWaveAsync()).IsSuccess);
        Assert.IsFalse((await controller.SearchAsync("трек")).IsSuccess);
        Assert.IsFalse((await controller.SearchAsync(" ")).IsSuccess);
        Assert.IsFalse((await controller.SearchAsync(new string('а', 201))).IsSuccess);
        Assert.IsEmpty(launcher.Targets);
    }

    [TestMethod]
    public async Task CancellationPreventsWindowActivationAndLaunch()
    {
        var launcher = new RecordingLauncher();
        var window = new WindowService();
        var controller = CreateController(launcher, window);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await controller.OpenAsync(cancellation.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await controller.SearchAsync("трек", cancellation.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async () => await controller.StartMyWaveAsync(cancellation.Token));

        Assert.AreEqual(0, window.Calls);
        Assert.IsEmpty(launcher.Targets);
    }

    private static DesktopYandexMusicController CreateController(RecordingLauncher launcher, WindowService window) =>
        new(new Locator(), new UnusedVolumeService(), window, launcher);

    private sealed class Locator : IInstalledApplicationLocator
    {
        public const string Executable = @"C:\Programs\YandexMusic\Яндекс Музыка.exe";

        public bool IsInstalled { get; init; } = true;

        public string? FindExecutable(string executableName)
        {
            Assert.AreEqual(DesktopYandexMusicController.ExecutableName, executableName);
            return IsInstalled ? Executable : null;
        }
    }

    private sealed class WindowService : IYandexMusicWindowService
    {
        public LaunchResult? Result { get; init; }

        public int Calls { get; private set; }

        public ValueTask<LaunchResult?> TryShowAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class RecordingLauncher : IApplicationLauncher
    {
        public List<string> Targets { get; } = [];

        public CancellationToken LastToken { get; private set; }

        public ValueTask<LaunchResult> LaunchAsync(string target, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Targets.Add(target);
            LastToken = cancellationToken;
            return ValueTask.FromResult(new LaunchResult(true, "Запущено"));
        }
    }

    private sealed class UnusedVolumeService : IYandexMusicVolumeService
    {
        public ValueTask<LaunchResult> AdjustAsync(YandexMusicAction action, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Открытие не должно менять громкость.");

        public ValueTask<LaunchResult> SetAsync(int percent, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Открытие не должно менять громкость.");
    }
}
