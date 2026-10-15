using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Egorik.App.ViewModels;
using Egorik.App.Views;
using Egorik.Core.Configuration;
using Egorik.Core.Platform;
using Egorik.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Egorik.App;

public sealed partial class App : Application
{
    private ServiceProvider? _services;
    private ScreenWakeOverlayWindow? _wakeOverlayWindow;
    private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;
    private bool _isExitRequested;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        _trayIcon = TrayIcon.GetIcons(this)?.FirstOrDefault();
        if (_trayIcon is not null)
        {
            _trayIcon.Icon = TrayIconFactory.Create();
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _services = Bootstrapper.BuildServices();
        _desktopLifetime = desktop;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.Exit += (_, _) =>
        {
            _wakeOverlayWindow?.Dispose();
            _services.Dispose();
        };

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
        var services = _services!;
        var viewModel = services.GetRequiredService<MainViewModel>();
        var overlayWindow = new ScreenWakeOverlayWindow(
            services.GetRequiredService<IDesktopOverlayWindowService>());
        overlayWindow.Connect(viewModel);

        try
        {
            await viewModel.InitializeAsync();
        }
        catch
        {
            overlayWindow.Dispose();
            throw;
        }

        var mainWindow = new MainWindow
        {
            DataContext = viewModel
        };
        mainWindow.Closing += OnMainWindowClosing;
        viewModel.WindowRequested += (_, _) => ShowMainWindow();

        _wakeOverlayWindow?.Dispose();
        _wakeOverlayWindow = overlayWindow;
        _mainWindow = mainWindow;
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = true;
        }

        desktop.MainWindow = mainWindow;
        var startInTray = desktop.Args?.Contains("--tray", StringComparer.Ordinal) == true;
        if (startInTray && _trayIcon is not null)
        {
            mainWindow.ShowInTaskbar = false;
        }
        else
        {
            mainWindow.Show();
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
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

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_isExitRequested)
        {
            return;
        }

        eventArgs.Cancel = true;
        HideMainWindow();
    }

    private void OnTrayIconClicked(object? sender, EventArgs eventArgs) =>
        ShowMainWindow();

    private void OnShowMainWindowClicked(object? sender, EventArgs eventArgs) =>
        ShowMainWindow();

    private void OnExitClicked(object? sender, EventArgs eventArgs)
    {
        _isExitRequested = true;
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
        }

        _mainWindow?.Close();
        _desktopLifetime?.Shutdown();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = true;
        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void HideMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowInTaskbar = false;
        _mainWindow.Hide();
    }
}
