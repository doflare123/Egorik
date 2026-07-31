using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Egorik.App.ViewModels;
using Egorik.Core.Platform;

namespace Egorik.App.Views;

public sealed partial class ScreenWakeOverlayWindow : Window, IDisposable
{
    private IDesktopOverlayWindowService? _platformService;
    private MainViewModel? _viewModel;
    private bool _hasOpened;
    private bool _disposed;

    public ScreenWakeOverlayWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += OnOpened;
        Closed += OnClosed;
    }

    public ScreenWakeOverlayWindow(IDesktopOverlayWindowService platformService)
        : this() =>
        _platformService = platformService;

    public void Connect(MainViewModel viewModel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateVisibility();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }

        Opened -= OnOpened;
        Closed -= OnClosed;

        if (_hasOpened)
        {
            Screens.Changed -= OnScreensChanged;
            Close();
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(MainViewModel.IsWakeAnimationActive))
        {
            UpdateVisibility();
        }
    }

    private void UpdateVisibility()
    {
        if (_disposed)
        {
            return;
        }

        if (_viewModel?.IsWakeAnimationActive == true)
        {
            FitPrimaryScreen();
            if (!IsVisible)
            {
                Show();
            }

            return;
        }

        if (IsVisible)
        {
            Hide();
        }
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        _hasOpened = true;
        FitPrimaryScreen();
        Screens.Changed += OnScreensChanged;

        var handle = TryGetPlatformHandle();
        if (handle is not null && _platformService is not null)
        {
            _platformService.ConfigureInputTransparent(handle.Handle);
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        if (_hasOpened)
        {
            Screens.Changed -= OnScreensChanged;
            _hasOpened = false;
        }
    }

    private void OnScreensChanged(object? sender, EventArgs eventArgs) =>
        FitPrimaryScreen();

    private void FitPrimaryScreen()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null)
        {
            return;
        }

        var bounds = screen.Bounds;
        Position = new PixelPoint(bounds.X, bounds.Y);
        Width = bounds.Width / screen.Scaling;
        Height = bounds.Height / screen.Scaling;
    }
}
