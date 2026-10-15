using System.Collections.ObjectModel;
using Egorik.Core.Speech;

namespace Egorik.App.ViewModels;

public sealed class AudioInputSettingsViewModel : ViewModelBase
{
    private readonly IAudioInputDeviceService _devices;
    private AudioInputDevice? _selectedDevice;
    private string _selectedDeviceId;
    private string _statusText = "Загружаю список микрофонов…";
    private bool _isRefreshing;

    public AudioInputSettingsViewModel(IAudioInputDeviceService devices, string selectedDeviceId)
    {
        _devices = devices;
        _selectedDeviceId = selectedDeviceId;
        Devices.Add(AudioInputDevice.SystemDefault);
        SelectSavedDevice();
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(), () => !IsRefreshing);
    }

    public ObservableCollection<AudioInputDevice> Devices { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }

    public string SelectedDeviceId => _selectedDeviceId;

    public AudioInputDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value) && value is not null)
            {
                _selectedDeviceId = value.Id;
                RaisePropertyChanged(nameof(SelectedDeviceId));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            SetProperty(ref _isRefreshing, value);
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        StatusText = "Обновляю список микрофонов…";
        try
        {
            var available = await _devices.GetDevicesAsync(cancellationToken);
            // Clearing ComboBox selection must not erase the saved device identity.
            Devices.Clear();
            Devices.Add(AudioInputDevice.SystemDefault);
            foreach (var device in available)
            {
                Devices.Add(device);
            }

            var missing = !Devices.Any(device => string.Equals(device.Id, _selectedDeviceId, StringComparison.OrdinalIgnoreCase));
            SelectSavedDevice();
            StatusText = missing
                ? "Выбранный микрофон недоступен. Подключи его или выбери другой."
                : available.Count == 0
                    ? "Устройства записи не найдены. Подключи микрофон и обнови список."
                    : "Выбери микрофон и сохрани настройки. Индикатор ниже показывает входящий звук.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusText = $"Не удалось обновить микрофоны: {exception.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void SelectSavedDevice()
    {
        var selected = Devices.FirstOrDefault(device => string.Equals(device.Id, _selectedDeviceId, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            selected = new AudioInputDevice(_selectedDeviceId, "Выбранный микрофон недоступен");
            Devices.Add(selected);
        }

        SelectedDevice = selected;
    }
}
