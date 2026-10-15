using Egorik.App.ViewModels;
using Egorik.Core.Speech;

namespace Egorik.App.Tests;

[TestClass]
public sealed class AudioInputSettingsTests
{
    [TestMethod]
    public async Task RefreshKeepsSelectedIdentityAfterReorderingAndRename()
    {
        var service = new DeviceService { Devices = [new("first", "Первый"), new("second", "Второй")] };
        var viewModel = new AudioInputSettingsViewModel(service, "second");
        await viewModel.RefreshAsync();
        Assert.AreEqual("Второй", viewModel.SelectedDevice?.Name);
        service.Devices = [new("second", "Новое имя"), new("first", "Первый")];
        await viewModel.RefreshAsync();

        Assert.AreEqual("second", viewModel.SelectedDeviceId);
        Assert.AreEqual("Новое имя", viewModel.SelectedDevice?.Name);
        Assert.AreEqual(3, viewModel.Devices.Count);
    }

    [TestMethod]
    public async Task DisconnectKeepsIdentityAndReconnectionRestoresDevice()
    {
        var service = new DeviceService { Devices = [new("other", "Другой")] };
        var viewModel = new AudioInputSettingsViewModel(service, "selected");

        await viewModel.RefreshAsync();
        StringAssert.Contains(viewModel.StatusText, "недоступен");
        Assert.AreEqual("selected", viewModel.SelectedDeviceId);
        service.Devices = [new("selected", "Вернувшийся микрофон")];
        await viewModel.RefreshAsync();

        Assert.AreEqual("Вернувшийся микрофон", viewModel.SelectedDevice?.Name);
        Assert.AreEqual("selected", viewModel.SelectedDeviceId);
    }

    [TestMethod]
    public async Task ExplicitSelectionAndSystemDefaultUpdateSavedDraft()
    {
        var service = new DeviceService { Devices = [new("one", "Один")] };
        var viewModel = new AudioInputSettingsViewModel(service, "");
        await viewModel.RefreshAsync();
        viewModel.SelectedDevice = viewModel.Devices[1];
        Assert.AreEqual("one", viewModel.SelectedDeviceId);
        // ComboBox emits null when its collection is refreshed.
        viewModel.SelectedDevice = null;
        Assert.AreEqual("one", viewModel.SelectedDeviceId);
        await viewModel.RefreshAsync();
        Assert.AreEqual("one", viewModel.SelectedDevice?.Id);
        viewModel.SelectedDevice = viewModel.Devices[0];

        Assert.AreEqual("", viewModel.SelectedDeviceId);
    }

    [TestMethod]
    public async Task RefreshFailurePreservesChoiceAndAllowsRetry()
    {
        var service = new DeviceService { Error = new InvalidOperationException("ошибка драйвера") };
        var viewModel = new AudioInputSettingsViewModel(service, "saved");
        await viewModel.RefreshAsync();

        StringAssert.Contains(viewModel.StatusText, "ошибка драйвера");
        Assert.AreEqual("saved", viewModel.SelectedDeviceId);
        Assert.IsFalse(viewModel.IsRefreshing);
        Assert.IsTrue(viewModel.RefreshCommand.CanExecute(null));
        service.Error = null;
        service.Devices = [new("saved", "Рабочий")];
        await viewModel.RefreshAsync();
        Assert.AreEqual("Рабочий", viewModel.SelectedDevice?.Name);
    }

    [TestMethod]
    public async Task EmptyDeviceListAndCancellationDoNotSelectRandomDevice()
    {
        var viewModel = new AudioInputSettingsViewModel(new DeviceService(), "");
        await viewModel.RefreshAsync();
        Assert.AreEqual(1, viewModel.Devices.Count);
        StringAssert.Contains(viewModel.StatusText, "не найдены");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => viewModel.RefreshAsync(cancellation.Token));
        Assert.IsFalse(viewModel.IsRefreshing);
        Assert.AreEqual("", viewModel.SelectedDeviceId);
    }

    private sealed class DeviceService : IAudioInputDeviceService
    {
        public IReadOnlyList<AudioInputDevice> Devices { get; set; } = [];
        public Exception? Error { get; set; }

        public ValueTask<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Error is not null)
            {
                throw Error;
            }

            return ValueTask.FromResult(Devices);
        }
    }
}
