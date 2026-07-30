using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Egorik.App.ViewModels;
using Egorik.App.Views;
using Egorik.Core.Configuration;
using Egorik.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Egorik.App;

public sealed partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _services = Bootstrapper.BuildServices();
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.Exit += (_, _) => _services.Dispose();

        Dispatcher.UIThread.Post(() => InitializeDesktopAsync(desktop));
        base.OnFrameworkInitializationCompleted();
    }

    private async void InitializeDesktopAsync(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var settings = _services!.GetRequiredService<IAssistantSettingsService>();
            await settings.LoadAsync();

            if (settings.Current.OnboardingCompleted)
            {
                await ShowMainWindowAsync(desktop);
            }
            else
            {
                ShowOnboardingWindow(desktop);
            }
        }
        catch (Exception exception)
        {
            await ShowStartupErrorAsync(desktop, exception);
        }
    }

    private void ShowOnboardingWindow(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var onboarding = new OnboardingWindow
        {
            DataContext = _services!.GetRequiredService<OnboardingViewModel>()
        };

        onboarding.Completed += async (_, _) =>
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                await ShowMainWindowAsync(desktop);
                onboarding.Close();
            }
            catch (Exception exception)
            {
                onboarding.Close();
                await ShowStartupErrorAsync(desktop, exception);
            }
        };

        desktop.MainWindow = onboarding;
        onboarding.Show();
        desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    private async Task ShowMainWindowAsync(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var viewModel = _services!.GetRequiredService<MainViewModel>();
        await viewModel.InitializeAsync();

        var mainWindow = new MainWindow
        {
            DataContext = viewModel
        };

        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
    }

    private async Task ShowStartupErrorAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        Exception exception)
    {
        var paths = _services!.GetRequiredService<ApplicationDataPaths>();
        Directory.CreateDirectory(Path.GetDirectoryName(paths.StartupLogFile)!);

        var logEntry = $"""
                        [{DateTimeOffset.Now:O}]
                        {exception}

                        """;
        await File.AppendAllTextAsync(paths.StartupLogFile, logEntry);

        var errorWindow = new StartupErrorWindow(
            exception.Message,
            paths.StartupLogFile);
        desktop.MainWindow = errorWindow;
        errorWindow.Show();
        desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
    }
}
