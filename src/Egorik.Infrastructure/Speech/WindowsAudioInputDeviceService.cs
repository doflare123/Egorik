using System.Runtime.InteropServices;
using System.Text;
using Egorik.Core.Speech;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Egorik.Infrastructure.Speech;

public sealed record WaveInputDevice(int DeviceNumber, string Id, string Name);

public interface IWaveInputDeviceCatalog
{
    IReadOnlyList<WaveInputDevice> GetWaveDevices(CancellationToken cancellationToken = default);
}

public static class WaveInputDeviceSelector
{
    public static WaveInputDevice Select(string? deviceId, IReadOnlyList<WaveInputDevice> devices)
    {
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("Windows не обнаружила ни одного устройства записи.");
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return new(-1, string.Empty, AudioInputDevice.SystemDefault.Name);
        }

        var matching = devices.Where(device => string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matching.Length == 1
            ? matching[0]
            : throw new InvalidOperationException("Выбранный микрофон недоступен. Подключи его или выбери другой в настройках.");
    }
}

public sealed class WindowsAudioInputDeviceService : IAudioInputDeviceService, IWaveInputDeviceCatalog
{
    // DRV_RESERVED + 17 / 18 from the Windows SDK mmddk.h.
    private const uint QueryFunctionInstanceId = 0x0811;
    private const uint QueryFunctionInstanceIdSize = 0x0812;

    public async ValueTask<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        await Task.Run(() => GetWaveDevices(cancellationToken)
            .Select(device => new AudioInputDevice(device.Id, device.Name)).ToArray(), cancellationToken);

    public IReadOnlyList<WaveInputDevice> GetWaveDevices(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var devices = new List<WaveInputDevice>();
        using var enumerator = new MMDeviceEnumerator();
        for (var index = 0; index < WaveInEvent.DeviceCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deviceId = ReadEndpointId(index);
            using var endpoint = enumerator.GetDevice(deviceId);
            devices.Add(new(index, deviceId, endpoint.FriendlyName));
        }

        return devices;
    }

    private static string ReadEndpointId(int deviceNumber)
    {
        var device = new IntPtr(deviceNumber);
        if (QuerySize(device, QueryFunctionInstanceIdSize, out var bytes, UIntPtr.Zero) != 0 ||
            bytes is < 2 or > 8192 || bytes % 2 != 0)
        {
            throw new InvalidOperationException("Не удалось получить идентификатор микрофона из Windows.");
        }

        var identity = new StringBuilder((int)bytes / 2);
        if (QueryId(device, QueryFunctionInstanceId, identity, new UIntPtr(bytes)) != 0 || identity.Length == 0)
        {
            throw new InvalidOperationException("Не удалось определить микрофон. Обнови список устройств.");
        }

        return identity.ToString();
    }

    [DllImport("winmm.dll", EntryPoint = "waveInMessage")]
    private static extern uint QuerySize(IntPtr device, uint message, out uint bytes, UIntPtr unused);

    [DllImport("winmm.dll", EntryPoint = "waveInMessage", CharSet = CharSet.Unicode)]
    private static extern uint QueryId(IntPtr device, uint message, StringBuilder identity, UIntPtr bytes);
}
