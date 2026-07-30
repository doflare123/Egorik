using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Egorik.App.ViewModels;

namespace Egorik.App.Views;

public sealed partial class OnboardingWindow : Window
{
    public OnboardingWindow() => AvaloniaXamlLoader.Load(this);

    public event EventHandler? Completed;

    private async void ContinueButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OnboardingViewModel viewModel &&
            await viewModel.CompleteAsync())
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }
}
