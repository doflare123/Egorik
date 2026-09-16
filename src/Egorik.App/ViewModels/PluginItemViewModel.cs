using Egorik.Core.Plugins;

namespace Egorik.App.ViewModels;

public sealed class PluginItemViewModel : ViewModelBase
{
    private bool _isEnabled;
    private bool _isTrusted;

    public PluginItemViewModel(
        LoadedAssistantPlugin plugin,
        bool isEnabled,
        bool isTrusted)
    {
        Plugin = plugin;
        _isEnabled = isEnabled;
        _isTrusted = isTrusted;
    }

    public LoadedAssistantPlugin Plugin { get; }

    public string Id => Plugin.Manifest.Id;

    public string Name => Plugin.Manifest.Name;

    public string Version => Plugin.Manifest.Version;

    public string KindText => Plugin.IsScriptPlugin ? "JavaScript" : "Декларативный";

    public string PermissionsText => Plugin.Manifest.Permissions.Count == 0
        ? "Системные разрешения не запрошены"
        : "Разрешения: " + string.Join(", ", Plugin.Manifest.Permissions);

    public string TrustText => Plugin.IsScriptPlugin
        ? _isTrusted
            ? "Код подтверждён. При изменении файлов потребуется повторное включение."
            : "Включение подтвердит запуск этого кода в изолированной песочнице."
        : "Исполняемого кода нет.";

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public void MarkTrusted()
    {
        if (_isTrusted)
        {
            return;
        }

        _isTrusted = true;
        RaisePropertyChanged(nameof(TrustText));
    }
}
