using Egorik.App.ViewModels;
using Egorik.Core;
using Egorik.Core.Ai;
using Egorik.Core.Applications;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Platform;
using Egorik.Core.Scenarios;
using Egorik.Core.Speech;
using Egorik.Infrastructure.Ai;
using Egorik.Infrastructure.Applications;
using Egorik.Infrastructure.Commands;
using Egorik.Infrastructure.Persistence;
using Egorik.Infrastructure.Platform;
using Egorik.Infrastructure.Speech;
using Microsoft.Extensions.DependencyInjection;

namespace Egorik.App;

internal static class Bootstrapper
{
    public static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ApplicationDataPaths>();
        services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        });
        services.AddSingleton<IAssistantSettingsService, JsonAssistantSettingsService>();
        services.AddSingleton<IScenarioRepository, JsonScenarioRepository>();

        services.AddSingleton<IInstalledApplicationLocator, DesktopInstalledApplicationLocator>();
        services.AddSingleton<IApplicationLauncher, DesktopApplicationLauncher>();
        services.AddSingleton<IWebBrowserService, DesktopWebBrowserService>();
        services.AddSingleton<IMediaControlService, DesktopMediaControlService>();
        services.AddSingleton<IYandexMusicController, DesktopYandexMusicController>();
        services.AddSingleton<IOfficeDocumentService, OpenXmlOfficeDocumentService>();
        services.AddSingleton<ISteamGameCatalog, SteamGameCatalog>();
        services.AddSingleton<IApplicationIntegration, DefaultBrowserApplicationIntegration>();
        services.AddSingleton<IApplicationIntegration, SteamApplicationIntegration>();
        services.AddSingleton<IApplicationIntegration, YandexMusicApplicationIntegration>();
        services.AddSingleton<IApplicationIntegration, MicrosoftWordApplicationIntegration>();
        services.AddSingleton<IApplicationIntegration, MicrosoftExcelApplicationIntegration>();
        services.AddSingleton<IApplicationActionRegistry, ApplicationActionRegistry>();
        services.AddSingleton<IDesktopOverlayWindowService, DesktopOverlayWindowService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IAiApiKeyStore, DpapiAiApiKeyStore>();
        services.AddSingleton<IAiPlanner, OpenAiCompatiblePlanner>();
        services.AddSingleton<IAiTool, ApplicationOpenAiTool>();
        services.AddSingleton<IAiTool, WebsiteOpenAiTool>();
        services.AddSingleton<IAiTool, WebSearchAiTool>();
        services.AddSingleton<IAiTool, MediaControlAiTool>();
        services.AddSingleton<IAiTool, ApplicationActionAiTool>();
        services.AddSingleton<IAiTool, SpreadsheetSearchAiTool>();
        services.AddSingleton<IAiTool, CalculatorAiTool>();
        services.AddSingleton<IAiTool, DateTimeAiTool>();
        services.AddSingleton<IAiTool, ClientNavigateAiTool>();
        services.AddSingleton<IAiTool, AssistantReplyAiTool>();
        services.AddSingleton<IAiToolExecutor, AiToolExecutor>();
        services.AddSingleton<IVoiceModelService, VoskVoiceModelService>();
        services.AddSingleton<INeuralVoiceModelService, SherpaNeuralVoiceModelService>();
        services.AddSingleton<IVoiceCommandSource, VoskVoiceCommandSource>();
        services.AddSingleton<WindowsSpeechSynthesizer>();
        services.AddSingleton<ISpeechSynthesizer, NeuralSpeechSynthesizer>();

        services.AddSingleton<IAssistantCommand, HelpCommand>();
        services.AddSingleton<IAssistantCommand, NavigationCommand>();
        services.AddSingleton<IAssistantCommand, OpenSiteCommand>();
        services.AddSingleton<IAssistantCommand, OpenApplicationCommand>();
        services.AddSingleton<IAssistantCommand, ApplicationActionCommand>();
        services.AddSingleton<IAssistantCommand, WebSearchCommand>();
        services.AddSingleton<IAssistantCommand, CalculatorCommand>();
        services.AddSingleton<IAssistantCommand, DateTimeCommand>();
        services.AddSingleton<IAssistantCommand, MediaCommand>();

        services.AddSingleton<ICommandRouter, CommandRouter>();
        services.AddSingleton<AssistantEngine>();

        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
