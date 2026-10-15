using Egorik.Infrastructure.Speech;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class AudioInputDeviceTests
{
    [TestMethod]
    public void SavedIdentityFindsCorrectMicrophoneAfterDeviceNumbersChange()
    {
        WaveInputDevice[] devices = [new(0, "usb-second", "Второй микрофон"), new(1, "usb-first", "Первый микрофон")];

        var selected = WaveInputDeviceSelector.Select("USB-FIRST", devices);

        Assert.AreEqual(1, selected.DeviceNumber);
        Assert.AreEqual("Первый микрофон", selected.Name);
    }

    [TestMethod]
    public void SameNamesDoNotConfuseDifferentMicrophones()
    {
        WaveInputDevice[] devices = [new(0, "usb-one", "USB Microphone"), new(1, "usb-two", "USB Microphone")];

        Assert.AreEqual(1, WaveInputDeviceSelector.Select("usb-two", devices).DeviceNumber);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(null)]
    public void DefaultUsesWindowsMapperInsteadOfFirstDevice(string? identity)
    {
        var selected = WaveInputDeviceSelector.Select(identity, [new(0, "usb-one", "USB Microphone")]);

        Assert.AreEqual(-1, selected.DeviceNumber);
        Assert.AreEqual(string.Empty, selected.Id);
    }

    [TestMethod]
    public void DisconnectedSelectedMicrophoneNeverFallsBackToAnotherDevice()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            WaveInputDeviceSelector.Select("disconnected", [new(0, "other-device", "Другой микрофон")]));
    }

    [TestMethod]
    public void NoDevicesAndAmbiguousIdentityAreRejected()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => WaveInputDeviceSelector.Select("", []));
        Assert.ThrowsExactly<InvalidOperationException>(() => WaveInputDeviceSelector.Select("duplicate",
            [new(0, "duplicate", "Один"), new(1, "duplicate", "Два")]));
    }

    [TestMethod]
    public async Task DeviceEnumerationHonorsCancellationBeforeUsingWindows()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new WindowsAudioInputDeviceService();

        Assert.ThrowsExactly<OperationCanceledException>(() => service.GetWaveDevices(cancellation.Token));
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await service.GetDevicesAsync(cancellation.Token));
    }
}
