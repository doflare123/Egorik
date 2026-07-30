using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Egorik.App.Views;

public sealed partial class StartupErrorWindow : Window
{
    public StartupErrorWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public StartupErrorWindow(string message, string logPath) : this()
    {
        DataContext = new StartupErrorViewModel(message, logPath);
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close();

    private sealed record StartupErrorViewModel(string Message, string LogPath);
}
