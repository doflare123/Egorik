namespace Egorik.Core.Weather;

public sealed record WeatherReport(
    string Location,
    DateTimeOffset ValidAt,
    string TimeZone,
    double Temperature,
    double FeelsLike,
    double WindSpeed,
    double Precipitation,
    int Humidity,
    int WeatherCode);

public sealed record WeatherLookupResult(WeatherReport? Report, string? Error = null);

public interface IWeatherService
{
    ValueTask<WeatherLookupResult> GetCurrentAsync(string location, CancellationToken cancellationToken = default);
}

public static class WeatherLocation
{
    public static bool IsValid(string? location) =>
        location is { Length: >= 2 and <= 100 } && location.Count(char.IsLetter) >= 2 &&
        location.All(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or ',' or '.' or '\'');
}
