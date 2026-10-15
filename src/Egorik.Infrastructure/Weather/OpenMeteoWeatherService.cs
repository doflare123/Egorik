using System.Globalization;
using System.Text.Json;
using Egorik.Core.Commands;
using Egorik.Core.Platform;
using Egorik.Core.Weather;

namespace Egorik.Infrastructure.Weather;

public sealed class OpenMeteoWeatherService(HttpClient httpClient, IClock clock) : IWeatherService
{
    private const int MaximumResponseBytes = 64 * 1024;

    public async ValueTask<WeatherLookupResult> GetCurrentAsync(string location, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        location = location?.Trim() ?? "";
        if (!WeatherLocation.IsValid(location))
        {
            return new(null, "Укажи название города, при необходимости добавь страну или область через запятую.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var search = new Uri("https://geocoding-api.open-meteo.com/v1/search?name=" +
                Uri.EscapeDataString(location) + "&count=10&language=ru&format=json");
            using var geocoding = await GetJsonAsync(search, timeout.Token);
            if (!geocoding.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            {
                return new(null, $"Не нашёл город «{location}». Уточни название.");
            }

            var places = results.EnumerateArray().ToArray();
            var city = TextNormalizer.Normalize(location.Split(',')[0]);
            var exact = places.Where(place => TextNormalizer.Normalize(place.GetProperty("name").GetString() ?? "") == city).ToArray();
            var candidates = exact.Length > 0 ? exact : places;
            if (candidates.Length != 1)
            {
                return new(null, "Нашёл несколько населённых пунктов. Укажи город и страну или область через запятую.");
            }

            var selected = candidates[0];
            var latitude = selected.GetProperty("latitude").GetDouble();
            var longitude = selected.GetProperty("longitude").GetDouble();
            if (!double.IsFinite(latitude) || latitude is < -90 or > 90 ||
                !double.IsFinite(longitude) || longitude is < -180 or > 180)
            {
                return new(null, "Погодный сервис вернул некорректные координаты города.");
            }

            var url = new Uri("https://api.open-meteo.com/v1/forecast?latitude=" + latitude.ToString(CultureInfo.InvariantCulture) +
                "&longitude=" + longitude.ToString(CultureInfo.InvariantCulture) +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m" +
                "&temperature_unit=celsius&wind_speed_unit=ms&precipitation_unit=mm&timezone=auto");
            using var weather = await GetJsonAsync(url, timeout.Token);
            var root = weather.RootElement;
            var current = root.GetProperty("current");
            var units = root.GetProperty("current_units");
            if (units.GetProperty("temperature_2m").GetString() != "°C" ||
                units.GetProperty("apparent_temperature").GetString() != "°C" ||
                units.GetProperty("wind_speed_10m").GetString() != "m/s" ||
                units.GetProperty("precipitation").GetString() != "mm" ||
                units.GetProperty("relative_humidity_2m").GetString() != "%")
            {
                return new(null, "Погодный сервис вернул неожиданные единицы измерения.");
            }

            var localTime = DateTime.ParseExact(current.GetProperty("time").GetString()!,
                "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None);
            var validAt = new DateTimeOffset(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified),
                TimeSpan.FromSeconds(root.GetProperty("utc_offset_seconds").GetInt32()));
            if (clock.Now - validAt > TimeSpan.FromHours(2) || validAt - clock.Now > TimeSpan.FromMinutes(30))
            {
                return new(null, "Погодный сервис вернул устаревшие данные. Повтори запрос позже.");
            }

            var temperature = current.GetProperty("temperature_2m").GetDouble();
            var feelsLike = current.GetProperty("apparent_temperature").GetDouble();
            var wind = current.GetProperty("wind_speed_10m").GetDouble();
            var precipitation = current.GetProperty("precipitation").GetDouble();
            var humidity = current.GetProperty("relative_humidity_2m").GetInt32();
            var code = current.GetProperty("weather_code").GetInt32();
            if (!double.IsFinite(temperature) || temperature is < -100 or > 100 ||
                !double.IsFinite(feelsLike) || feelsLike is < -150 or > 150 ||
                !double.IsFinite(wind) || wind is < 0 or > 150 ||
                !double.IsFinite(precipitation) || precipitation is < 0 or > 1000 || humidity is < 0 or > 100)
            {
                return new(null, "Погодный сервис вернул некорректные значения.");
            }

            var name = selected.GetProperty("name").GetString()!;
            if (selected.TryGetProperty("country", out var country))
            {
                name += ", " + country.GetString();
            }

            return new(new WeatherReport(name, validAt, root.GetProperty("timezone").GetString()!,
                temperature, feelsLike, wind, precipitation, humidity, code));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(null, "Погодный сервис не ответил вовремя. Повтори запрос позже.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or
            KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            return new(null, "Не удалось получить актуальную погоду из Open-Meteo. Проверь интернет и повтори запрос.");
        }
    }

    private async Task<JsonDocument> GetJsonAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new JsonException("Weather response is too large.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaximumResponseBytes)
            {
                throw new JsonException("Weather response is too large.");
            }

            output.Write(buffer, 0, read);
        }

        return JsonDocument.Parse(output.ToArray());
    }
}
