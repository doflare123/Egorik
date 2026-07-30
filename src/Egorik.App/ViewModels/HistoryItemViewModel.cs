namespace Egorik.App.ViewModels;

public sealed class HistoryItemViewModel(
    string request,
    string response,
    bool isSuccess,
    DateTimeOffset timestamp)
{
    public string Request { get; } = request;

    public string Response { get; } = response;

    public bool IsSuccess { get; } = isSuccess;

    public string Timestamp { get; } = timestamp.ToString("HH:mm");

    public string StatusGlyph => IsSuccess ? "✓" : "!";
}
