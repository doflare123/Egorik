using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Egorik.App.ViewModels;

namespace Egorik.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty ||
            change.Property == WindowStateProperty ||
            change.Property == DataContextProperty)
        {
            if (change.Property == DataContextProperty && change.OldValue is MainViewModel previous)
            {
                previous.SetVoiceActivityVisible(false);
            }

            if (DataContext is MainViewModel viewModel)
            {
                viewModel.SetVoiceActivityVisible(IsVisible && WindowState != WindowState.Minimized);
            }
        }
    }
}
