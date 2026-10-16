using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;

namespace Egorik.App.Views;

public sealed partial class ScenarioEditorView : UserControl
{
    public ScenarioEditorView() => AvaloniaXamlLoader.Load(this);

    private ScenarioGraphControl WorkspaceControl => this.FindControl<ScenarioGraphControl>("Workspace")!;
    private void OnZoomIn(object? sender, RoutedEventArgs eventArgs) => WorkspaceControl.ZoomBy(1.15);
    private void OnZoomOut(object? sender, RoutedEventArgs eventArgs) => WorkspaceControl.ZoomBy(1 / 1.15);
    private void OnFit(object? sender, RoutedEventArgs eventArgs) => WorkspaceControl.Fit();
    private void OnExpand(object? sender, RoutedEventArgs eventArgs)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }
}
