using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using Egorik.Core;
using Egorik.Core.Ai;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Scenarios;
using Egorik.Core.Speech;
using Egorik.Core.Plugins;

namespace Egorik.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly AssistantEngine _engine;
    private readonly IAssistantSettingsService _settings;
    private readonly IScenarioRepository _scenarioRepository;
    private readonly IVoiceCommandSource _voiceCommandSource;
    private readonly IVoiceModelService _voiceModelService;
    private readonly INeuralVoiceModelService _neuralVoiceModelService;
    private readonly ISpeechSynthesizer _speechSynthesizer;
    private readonly IAiPlanner _aiPlanner;
    private readonly IAiToolExecutor _aiToolExecutor;
    private readonly IAiApiKeyStore _aiApiKeyStore;
    private readonly IPluginCatalog _pluginCatalog;
    private readonly IReadOnlyDictionary<string, IPluginActivationService> _pluginActivations;
    private readonly VoiceWakeIndicator _voiceWakeIndicator = new();
    private string _commandText = string.Empty;
    private string _statusText = "Готов к команде";
    private string _assistantName;
    private string _searchProvider;
    private string _applicationAliasesText;
    private string _siteAliasesText;
    private string _documentAliasesText;
    private bool _speakResponses;
    private bool _voiceActivationEnabled;
    private int _selectedTabIndex;
    private bool _isBusy;
    private string _scenarioName = string.Empty;
    private string _scenarioTrigger = string.Empty;
    private string _scenarioCommandsText = string.Empty;
    private ScenarioItemViewModel? _selectedScenario;
    private string _voiceStatusText;
    private double _voiceDownloadProgress;
    private bool _isVoiceModelInstalled;
    private string _voiceActivityText = "Ожидаю звук с микрофона.";
    private double _voiceInputLevel;
    private string _neuralVoiceStatusText;
    private double _neuralVoiceDownloadProgress;
    private bool _isNeuralVoiceInstalled;
    private bool _aiEnabled;
    private string _aiEndpoint;
    private string _aiModel;
    private string _aiApiKey = string.Empty;
    private string _aiStatusText;
    private bool _isWakeAnimationActive;
    private string _pluginStatusText;

    public MainViewModel(
        AssistantEngine engine,
        IAssistantSettingsService settings,
        IScenarioRepository scenarioRepository,
        IVoiceCommandSource voiceCommandSource,
        IVoiceModelService voiceModelService,
        INeuralVoiceModelService neuralVoiceModelService,
        ISpeechSynthesizer speechSynthesizer,
        IAiPlanner aiPlanner,
        IAiToolExecutor aiToolExecutor,
        IAiApiKeyStore aiApiKeyStore,
        IPluginCatalog pluginCatalog,
        IEnumerable<IPluginActivationService> pluginActivations)
    {
        _engine = engine;
        _settings = settings;
        _scenarioRepository = scenarioRepository;
        _voiceCommandSource = voiceCommandSource;
        _voiceModelService = voiceModelService;
        _neuralVoiceModelService = neuralVoiceModelService;
        _speechSynthesizer = speechSynthesizer;
        _aiPlanner = aiPlanner;
        _aiToolExecutor = aiToolExecutor;
        _aiApiKeyStore = aiApiKeyStore;
        _pluginCatalog = pluginCatalog;
        _pluginActivations = pluginActivations.ToDictionary(
            activation => activation.PluginId,
            StringComparer.Ordinal);

        _assistantName = settings.Current.AssistantName;
        _searchProvider = settings.Current.SearchProvider;
        _speakResponses = settings.Current.SpeakResponses;
        _voiceActivationEnabled = settings.Current.VoiceActivationEnabled;
        _aiEnabled = settings.Current.AiEnabled;
        _aiEndpoint = settings.Current.AiEndpoint;
        _aiModel = settings.Current.AiModel;
        _applicationAliasesText = FormatAliases(settings.Current.Applications);
        _siteAliasesText = FormatAliases(settings.Current.Sites);
        _documentAliasesText = FormatAliases(settings.Current.Documents);
        foreach (var plugin in pluginCatalog.Plugins)
        {
            var trusted = !plugin.IsScriptPlugin ||
                          settings.Current.TrustedPluginFingerprints.TryGetValue(
                              plugin.Manifest.Id,
                              out var fingerprint) &&
                          string.Equals(fingerprint, plugin.Fingerprint, StringComparison.Ordinal);
            Plugins.Add(new PluginItemViewModel(
                plugin,
                settings.Current.EnabledPlugins.Contains(plugin.Manifest.Id) && trusted,
                trusted));
        }

        _pluginStatusText = BuildPluginStatusText();
        _isVoiceModelInstalled = voiceModelService.IsInstalled;
        _voiceStatusText = voiceModelService.IsInstalled
            ? "Русская модель установлена. Подключаю микрофон…"
            : "Русская модель Vosk не загружена.";
        _isNeuralVoiceInstalled = neuralVoiceModelService.IsInstalled;
        _neuralVoiceStatusText = neuralVoiceModelService.IsInstalled
            ? $"Активен: {speechSynthesizer.VoiceName}."
            : "Сейчас используется системный голос Windows.";
        _aiStatusText = BuildAiStatusText();

        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => !string.IsNullOrWhiteSpace(CommandText));
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        AddScenarioCommand = new AsyncRelayCommand(AddScenarioAsync);
        DeleteScenarioCommand = new AsyncRelayCommand(DeleteSelectedScenarioAsync, () => SelectedScenario is not null);
        DownloadVoiceModelCommand = new AsyncRelayCommand(
            DownloadVoiceModelAsync,
            () => !IsVoiceModelInstalled);
        DownloadNeuralVoiceCommand = new AsyncRelayCommand(
            DownloadNeuralVoiceAsync,
            () => !IsNeuralVoiceInstalled);
        TestVoiceCommand = new AsyncRelayCommand(TestVoiceAsync);
        TestAiCommand = new AsyncRelayCommand(TestAiAsync);
        DeleteAiApiKeyCommand = new RelayCommand(DeleteAiApiKey);
        UseExampleCommand = new RelayCommand(() => CommandText = "что ты умеешь");

        _voiceCommandSource.StateChanged += OnVoiceStateChanged;
        _voiceCommandSource.CommandRecognized += OnVoiceCommandRecognized;
        _voiceCommandSource.ActivityChanged += OnVoiceActivityChanged;
    }

    public string Greeting => $"{AssistantName} на связи";

    public ObservableCollection<HistoryItemViewModel> History { get; } = [];

    public ObservableCollection<ScenarioItemViewModel> Scenarios { get; } = [];

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = [];

    public IReadOnlyList<string> SearchProviders { get; } = ["Yandex", "Google", "DuckDuckGo"];

    public AsyncRelayCommand ExecuteCommand { get; }

    public AsyncRelayCommand SaveSettingsCommand { get; }

    public AsyncRelayCommand AddScenarioCommand { get; }

    public AsyncRelayCommand DeleteScenarioCommand { get; }

    public AsyncRelayCommand DownloadVoiceModelCommand { get; }

    public AsyncRelayCommand DownloadNeuralVoiceCommand { get; }

    public AsyncRelayCommand TestVoiceCommand { get; }

    public AsyncRelayCommand TestAiCommand { get; }

    public RelayCommand DeleteAiApiKeyCommand { get; }

    public RelayCommand UseExampleCommand { get; }

    public string PluginStatusText
    {
        get => _pluginStatusText;
        private set => SetProperty(ref _pluginStatusText, value);
    }

    public string PluginsDirectory => _pluginCatalog.UserPluginsDirectory;

    public string VoiceModelButtonText =>
        IsVoiceModelInstalled ? "Модель установлена" : "Загрузить модель · 45 МБ";

    public string NeuralVoiceButtonText =>
        IsNeuralVoiceInstalled ? "Нейроголос установлен" : "Загрузить нейроголос · 81 МБ";

    public string VoiceStatusText
    {
        get => _voiceStatusText;
        private set => SetProperty(ref _voiceStatusText, value);
    }

    public double VoiceDownloadProgress
    {
        get => _voiceDownloadProgress;
        private set => SetProperty(ref _voiceDownloadProgress, value);
    }

    public bool IsVoiceModelInstalled
    {
        get => _isVoiceModelInstalled;
        private set
        {
            if (SetProperty(ref _isVoiceModelInstalled, value))
            {
                RaisePropertyChanged(nameof(VoiceModelButtonText));
                DownloadVoiceModelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string VoiceActivityText
    {
        get => _voiceActivityText;
        private set => SetProperty(ref _voiceActivityText, value);
    }

    public double VoiceInputLevel
    {
        get => _voiceInputLevel;
        private set => SetProperty(ref _voiceInputLevel, value);
    }

    public bool IsWakeAnimationActive
    {
        get => _isWakeAnimationActive;
        private set => SetProperty(ref _isWakeAnimationActive, value);
    }

    public string NeuralVoiceStatusText
    {
        get => _neuralVoiceStatusText;
        private set => SetProperty(ref _neuralVoiceStatusText, value);
    }

    public double NeuralVoiceDownloadProgress
    {
        get => _neuralVoiceDownloadProgress;
        private set => SetProperty(ref _neuralVoiceDownloadProgress, value);
    }

    public bool IsNeuralVoiceInstalled
    {
        get => _isNeuralVoiceInstalled;
        private set
        {
            if (SetProperty(ref _isNeuralVoiceInstalled, value))
            {
                RaisePropertyChanged(nameof(NeuralVoiceButtonText));
                DownloadNeuralVoiceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CommandText
    {
        get => _commandText;
        set
        {
            if (SetProperty(ref _commandText, value))
            {
                ExecuteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string AssistantName
    {
        get => _assistantName;
        set
        {
            if (SetProperty(ref _assistantName, value))
            {
                RaisePropertyChanged(nameof(Greeting));
            }
        }
    }

    public string SearchProvider
    {
        get => _searchProvider;
        set => SetProperty(ref _searchProvider, value);
    }

    public bool SpeakResponses
    {
        get => _speakResponses;
        set => SetProperty(ref _speakResponses, value);
    }

    public bool VoiceActivationEnabled
    {
        get => _voiceActivationEnabled;
        set => SetProperty(ref _voiceActivationEnabled, value);
    }

    public bool AiEnabled
    {
        get => _aiEnabled;
        set
        {
            if (SetProperty(ref _aiEnabled, value))
            {
                AiStatusText = BuildAiStatusText();
            }
        }
    }

    public string AiEndpoint
    {
        get => _aiEndpoint;
        set => SetProperty(ref _aiEndpoint, value);
    }

    public string AiModel
    {
        get => _aiModel;
        set => SetProperty(ref _aiModel, value);
    }

    public string AiApiKey
    {
        get => _aiApiKey;
        set => SetProperty(ref _aiApiKey, value);
    }

    public string AiApiKeyWatermark =>
        _aiApiKeyStore.HasApiKey
            ? "Ключ уже сохранён; оставьте поле пустым"
            : "API-ключ (для локального API можно не указывать)";

    public string AiStatusText
    {
        get => _aiStatusText;
        private set => SetProperty(ref _aiStatusText, value);
    }

    public string ApplicationAliasesText
    {
        get => _applicationAliasesText;
        set => SetProperty(ref _applicationAliasesText, value);
    }

    public string SiteAliasesText
    {
        get => _siteAliasesText;
        set => SetProperty(ref _siteAliasesText, value);
    }

    public string DocumentAliasesText
    {
        get => _documentAliasesText;
        set => SetProperty(ref _documentAliasesText, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public string ScenarioName
    {
        get => _scenarioName;
        set => SetProperty(ref _scenarioName, value);
    }

    public string ScenarioTrigger
    {
        get => _scenarioTrigger;
        set => SetProperty(ref _scenarioTrigger, value);
    }

    public string ScenarioCommandsText
    {
        get => _scenarioCommandsText;
        set => SetProperty(ref _scenarioCommandsText, value);
    }

    public ScenarioItemViewModel? SelectedScenario
    {
        get => _selectedScenario;
        set
        {
            if (SetProperty(ref _selectedScenario, value))
            {
                DeleteScenarioCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task InitializeAsync()
    {
        var scenarios = await _scenarioRepository.GetAllAsync();
        Scenarios.Clear();
        foreach (var scenario in scenarios)
        {
            Scenarios.Add(new ScenarioItemViewModel(scenario));
        }

        await ApplyPluginActivationsAsync();

        if (VoiceActivationEnabled && IsVoiceModelInstalled)
        {
            await _voiceCommandSource.StartAsync();
        }
    }

    private async Task ExecuteAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        var request = CommandText.Trim();
        StatusText = "Обрабатываю команду…";

        try
        {
            var result = await _engine.ExecuteAsync(request);
            History.Insert(0, new HistoryItemViewModel(
                request,
                result.Message,
                result.IsSuccess,
                DateTimeOffset.Now));

            while (History.Count > 30)
            {
                History.RemoveAt(History.Count - 1);
            }

            StatusText = result.Message;
            CommandText = string.Empty;

            SelectedTabIndex = result.ClientAction switch
            {
                ClientAction.OpenSettings => 2,
                ClientAction.OpenScenarios => 1,
                _ => SelectedTabIndex
            };

            if (SpeakResponses && _speechSynthesizer.IsAvailable)
            {
                var restartVoice = VoiceActivationEnabled && IsVoiceModelInstalled;
                try
                {
                    if (restartVoice)
                    {
                        await _voiceCommandSource.PauseAsync();
                    }

                    await _speechSynthesizer.SpeakAsync(result.Message);
                }
                finally
                {
                    if (restartVoice)
                    {
                        await _voiceCommandSource.ResumeAsync();
                    }
                }
            }
        }
        catch (Exception exception)
        {
            StatusText = $"Ошибка: {exception.Message}";
            History.Insert(0, new HistoryItemViewModel(
                request,
                StatusText,
                false,
                DateTimeOffset.Now));
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task SaveSettingsAsync()
    {
        var name = AssistantName.Trim();
        if (name.Length is < 2 or > 24)
        {
            StatusText = "Имя должно содержать от 2 до 24 символов.";
            return;
        }

        if (!TryParseAliases(ApplicationAliasesText, out var applications) ||
            !TryParseAliases(SiteAliasesText, out var sites) ||
            !TryParseAliases(DocumentAliasesText, out var documents))
        {
            StatusText = "Проверь списки: каждая строка должна иметь формат имя=значение.";
            return;
        }

        if (!TryApplyAiDraft(out var aiError))
        {
            StatusText = aiError;
            return;
        }

        _settings.Current.AssistantName = name;
        _settings.Current.SearchProvider = SearchProvider;
        _settings.Current.SpeakResponses = SpeakResponses;
        _settings.Current.VoiceActivationEnabled = VoiceActivationEnabled;
        _settings.Current.AiEnabled = AiEnabled;
        _settings.Current.AiEndpoint = AiEndpoint.Trim();
        _settings.Current.AiModel = AiModel.Trim();
        _settings.Current.Applications = applications;
        _settings.Current.Sites = sites;
        _settings.Current.Documents = documents;
        foreach (var plugin in Plugins)
        {
            if (plugin.IsEnabled)
            {
                _settings.Current.EnabledPlugins.Add(plugin.Id);
                if (plugin.Plugin.IsScriptPlugin)
                {
                    _settings.Current.TrustedPluginFingerprints[plugin.Id] =
                        plugin.Plugin.Fingerprint;
                    plugin.MarkTrusted();
                }
            }
            else
            {
                _settings.Current.EnabledPlugins.Remove(plugin.Id);
                _settings.Current.TrustedPluginFingerprints.Remove(plugin.Id);
            }
        }
        await _settings.SaveAsync();

        var pluginResult = await ApplyPluginActivationsAsync();

        if (VoiceActivationEnabled)
        {
            await _voiceCommandSource.StartAsync();
        }
        else
        {
            await _voiceCommandSource.StopAsync();
        }

        StatusText = pluginResult is null || pluginResult.IsSuccess
            ? "Настройки сохранены."
            : pluginResult.Message;
        AiStatusText = BuildAiStatusText();
        RaisePropertyChanged(nameof(Greeting));
    }

    private async Task<CommandResult?> ApplyPluginActivationsAsync()
    {
        CommandResult? firstFailure = null;
        var messages = new List<string>();
        foreach (var activation in _pluginActivations.Values)
        {
            var available = _pluginCatalog.Plugins.Any(plugin =>
                string.Equals(plugin.Manifest.Id, activation.PluginId, StringComparison.Ordinal));
            var package = _pluginCatalog.Plugins.FirstOrDefault(plugin =>
                string.Equals(plugin.Manifest.Id, activation.PluginId, StringComparison.Ordinal));
            var trusted = package is not { IsScriptPlugin: true } ||
                          _settings.Current.TrustedPluginFingerprints.TryGetValue(
                              activation.PluginId,
                              out var fingerprint) &&
                          string.Equals(fingerprint, package.Fingerprint, StringComparison.Ordinal);
            var enabled = available && trusted &&
                          _settings.Current.EnabledPlugins.Contains(activation.PluginId);
            var result = await activation.ApplyAsync(enabled);
            messages.Add(result.Message);
            if (!result.IsSuccess)
            {
                firstFailure ??= result;
            }
        }

        PluginStatusText = BuildPluginStatusText(messages);
        return firstFailure;
    }

    private string BuildPluginStatusText(IReadOnlyList<string>? activationMessages = null)
    {
        var loaded = _pluginCatalog.Plugins.Count;
        var summary = loaded == 0
            ? "Плагины не найдены."
            : $"Загружено плагинов: {loaded}.";
        if (_pluginCatalog.Issues.Count > 0)
        {
            summary += $" Отклонено: {_pluginCatalog.Issues.Count}.";
        }

        if (activationMessages is { Count: > 0 })
        {
            summary += " " + string.Join(" ", activationMessages);
        }

        return summary;
    }

    private async Task DownloadVoiceModelAsync()
    {
        VoiceStatusText = "Загружаю русскую модель…";
        VoiceDownloadProgress = 0;
        var progress = new Progress<double>(value =>
        {
            VoiceDownloadProgress = value * 100;
            VoiceStatusText = $"Загрузка модели: {VoiceDownloadProgress:0}%";
        });

        try
        {
            await _voiceModelService.DownloadAsync(progress);
            IsVoiceModelInstalled = _voiceModelService.IsInstalled;
            VoiceDownloadProgress = 100;
            VoiceStatusText = IsVoiceModelInstalled
                ? "Русская модель установлена. Подключаю микрофон…"
                : "Модель загружена, но не прошла проверку.";

            if (IsVoiceModelInstalled)
            {
                VoiceActivationEnabled = true;
                _settings.Current.VoiceActivationEnabled = true;
                await _settings.SaveAsync();
                await _voiceCommandSource.StartAsync();
            }
        }
        catch (Exception exception)
        {
            VoiceStatusText = $"Не удалось загрузить модель: {exception.Message}";
        }
    }

    private async Task DownloadNeuralVoiceAsync()
    {
        NeuralVoiceStatusText = "Загружаю локальный нейроголос Denis…";
        NeuralVoiceDownloadProgress = 0;
        var progress = new Progress<double>(value =>
        {
            NeuralVoiceDownloadProgress = value * 100;
            NeuralVoiceStatusText =
                $"Загрузка нейроголоса: {NeuralVoiceDownloadProgress:0}%";
        });

        try
        {
            await _neuralVoiceModelService.DownloadAsync(progress);
            IsNeuralVoiceInstalled = _neuralVoiceModelService.IsInstalled;
            NeuralVoiceDownloadProgress = IsNeuralVoiceInstalled ? 100 : 0;
            NeuralVoiceStatusText = IsNeuralVoiceInstalled
                ? $"Активен: {_speechSynthesizer.VoiceName}."
                : "Модель загружена, но не прошла проверку.";

            if (IsNeuralVoiceInstalled)
            {
                await TestVoiceAsync();
            }
        }
        catch (Exception exception)
        {
            NeuralVoiceStatusText =
                $"Не удалось загрузить нейроголос: {exception.Message}";
        }
    }

    private async Task TestVoiceAsync()
    {
        var restartVoice = VoiceActivationEnabled && IsVoiceModelInstalled;
        try
        {
            NeuralVoiceStatusText = $"Проверяю: {_speechSynthesizer.VoiceName}…";
            if (restartVoice)
            {
                await _voiceCommandSource.PauseAsync();
            }

            await _speechSynthesizer.SpeakAsync(
                $"Привет! Я {AssistantName}. Голосовой ответ работает.");
            NeuralVoiceStatusText = $"Активен: {_speechSynthesizer.VoiceName}.";
        }
        catch (Exception exception)
        {
            NeuralVoiceStatusText = $"Ошибка озвучивания: {exception.Message}";
        }
        finally
        {
            if (restartVoice)
            {
                await _voiceCommandSource.ResumeAsync();
            }
        }
    }

    private async Task TestAiAsync()
    {
        if (!TryApplyAiDraft(out var error))
        {
            AiStatusText = error;
            StatusText = error;
            return;
        }

        if (!AiEnabled)
        {
            AiStatusText = "Сначала включите ИИ-планировщик.";
            return;
        }

        AiStatusText = "Проверяю подключение и строгий формат плана…";

        try
        {
            var plan = await _aiPlanner.CreatePlanAsync(
                "Проверка подключения. Верни один шаг assistant_reply с сообщением «Подключение работает».",
                _aiToolExecutor.AvailableTools);

            if (plan is null)
            {
                AiStatusText = "API не вернул план.";
                return;
            }

            var validation = _aiToolExecutor.Validate(plan);
            AiStatusText = validation.IsValid
                ? $"Подключение работает. Безопасный план: {plan.Steps.Count} шаг(а)."
                : $"API доступен, но план отклонён: {validation.Error}";
        }
        catch (AiPlannerException exception)
        {
            AiStatusText = $"Ошибка API: {exception.Message}";
        }
        catch (Exception exception)
        {
            AiStatusText = $"Не удалось проверить API: {exception.Message}";
        }

        StatusText = AiStatusText;
    }

    private bool TryApplyAiDraft(out string error)
    {
        error = string.Empty;
        var endpoint = AiEndpoint.Trim();
        var model = AiModel.Trim();

        if (AiEnabled)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
                (endpointUri.Scheme != Uri.UriSchemeHttps &&
                 !(endpointUri.Scheme == Uri.UriSchemeHttp && endpointUri.IsLoopback)))
            {
                error = "Для ИИ разрешён HTTPS endpoint или HTTP на localhost.";
                return false;
            }

            if (model.Length is < 2 or > 120)
            {
                error = "Укажите корректное имя модели ИИ.";
                return false;
            }
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(AiApiKey))
            {
                _aiApiKeyStore.SaveApiKey(AiApiKey);
                AiApiKey = string.Empty;
                RaisePropertyChanged(nameof(AiApiKeyWatermark));
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or PlatformNotSupportedException or AiPlannerException)
        {
            error = $"Не удалось сохранить API-ключ: {exception.Message}";
            return false;
        }

        _settings.Current.AiEnabled = AiEnabled;
        _settings.Current.AiEndpoint = endpoint;
        _settings.Current.AiModel = model;
        return true;
    }

    private void DeleteAiApiKey()
    {
        try
        {
            _aiApiKeyStore.DeleteApiKey();
            AiApiKey = string.Empty;
            AiStatusText = "Сохранённый API-ключ удалён.";
            RaisePropertyChanged(nameof(AiApiKeyWatermark));
        }
        catch (Exception exception)
        {
            AiStatusText = $"Не удалось удалить API-ключ: {exception.Message}";
        }

        StatusText = AiStatusText;
    }

    private string BuildAiStatusText()
    {
        if (!AiEnabled)
        {
            return "ИИ-планировщик выключен. Базовые команды работают локально.";
        }

        var keyStatus = _aiApiKeyStore.HasApiKey
            ? "API-ключ защищён Windows DPAPI."
            : "API-ключ не задан; это допустимо для локального сервера.";
        return $"Модель: {AiModel}. {keyStatus}";
    }

    private async Task AddScenarioAsync()
    {
        var name = ScenarioName.Trim();
        var trigger = ScenarioTrigger.Trim();
        var commands = ScenarioCommandsText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(command => command.Length > 0)
            .ToList();

        if (name.Length == 0 || trigger.Length == 0 || commands.Count == 0)
        {
            StatusText = "Для сценария нужны название, фраза и хотя бы одно действие.";
            return;
        }

        var all = (await _scenarioRepository.GetAllAsync()).ToList();
        if (all.Any(item =>
                TextNormalizer.Normalize(item.Trigger) == TextNormalizer.Normalize(trigger)))
        {
            StatusText = "Сценарий с такой фразой уже существует.";
            return;
        }

        var scenario = new AssistantScenario
        {
            Name = name,
            Trigger = trigger,
            Commands = commands
        };

        all.Add(scenario);
        await _scenarioRepository.SaveAsync(all);
        Scenarios.Add(new ScenarioItemViewModel(scenario));

        ScenarioName = string.Empty;
        ScenarioTrigger = string.Empty;
        ScenarioCommandsText = string.Empty;
        StatusText = $"Сценарий «{name}» сохранён.";
    }

    private async Task DeleteSelectedScenarioAsync()
    {
        if (SelectedScenario is null)
        {
            return;
        }

        var selected = SelectedScenario;
        var all = (await _scenarioRepository.GetAllAsync())
            .Where(item => item.Id != selected.Model.Id)
            .ToList();

        await _scenarioRepository.SaveAsync(all);
        Scenarios.Remove(selected);
        SelectedScenario = null;
        StatusText = $"Сценарий «{selected.Name}» удалён.";
    }

    private static string FormatAliases(IReadOnlyDictionary<string, string> values)
    {
        var builder = new StringBuilder();
        foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(pair.Key).Append('=').AppendLine(pair.Value);
        }

        return builder.ToString().TrimEnd();
    }

    private static bool TryParseAliases(
        string text,
        out Dictionary<string, string> result)
    {
        result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                return false;
            }

            var alias = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (alias.Length == 0 || value.Length == 0)
            {
                return false;
            }

            result[alias] = value;
        }

        return true;
    }

    private void OnVoiceStateChanged(
        object? sender,
        VoiceInputStateChangedEventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() =>
        {
            IsWakeAnimationActive = _voiceWakeIndicator.ObserveState(eventArgs.State);
            VoiceStatusText = eventArgs.Message;
            StatusText = eventArgs.Message;
        });

    private void OnVoiceCommandRecognized(
        object? sender,
        VoiceCommandRecognizedEventArgs eventArgs) =>
        Dispatcher.UIThread.Post(async () =>
        {
            IsWakeAnimationActive = _voiceWakeIndicator.AcceptCommand();
            CommandText = eventArgs.Text;
            await ExecuteAsync();
        });

    private void OnVoiceActivityChanged(
        object? sender,
        VoiceInputActivityEventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() =>
        {
            IsWakeAnimationActive = _voiceWakeIndicator.ObserveActivity(
                eventArgs.PartialText,
                AssistantName);
            VoiceInputLevel = eventArgs.Level * 100;
            if (!string.IsNullOrWhiteSpace(eventArgs.PartialText))
            {
                VoiceActivityText = $"Слышу: «{eventArgs.PartialText}»";
            }
            else if (eventArgs.Level >= 0.08)
            {
                VoiceActivityText = "Микрофон слышит звук…";
            }
        });
}
