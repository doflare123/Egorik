using System.Net;
using System.Text;
using Egorik.Core.Ai;
using Egorik.Core.Configuration;
using Egorik.Core.Platform;
using Egorik.Core.Weather;
using Egorik.Infrastructure.Ai;
using Egorik.Infrastructure.Weather;

namespace Egorik.Core.Tests;

[TestClass]
public sealed class WeatherTests
{
    private const string Geocoding = """
        {"results":[{"name":"Калининград","country":"Россия","latitude":54.71,"longitude":20.51}]}
        """;
    private const string Current = """
        {"timezone":"Europe/Kaliningrad","utc_offset_seconds":7200,
         "current_units":{"temperature_2m":"°C","apparent_temperature":"°C","wind_speed_10m":"m/s","precipitation":"mm","relative_humidity_2m":"%"},
         "current":{"time":"2026-10-05T12:00","temperature_2m":12.5,"apparent_temperature":10.3,
           "wind_speed_10m":3.2,"precipitation":0.1,"relative_humidity_2m":80,"weather_code":3}}
        """;

    [TestMethod]
    public async Task ValidatedWeatherPlanUsesDefaultCityAndReportsSourcedValues()
    {
        var handler = new WeatherHandler(Geocoding, Current);
        using var http = new HttpClient(handler);
        var service = new OpenMeteoWeatherService(http, new FixedClock());
        var executor = new AiToolExecutor([new WeatherAiTool(service, new Settings())]);

        var result = await executor.ExecuteAsync(Plan(new Dictionary<string, string>()));

        Assert.IsTrue(result.IsSuccess);
        StringAssert.Contains(result.Message, "Калининград");
        StringAssert.Contains(result.Message, "12,5 °C");
        StringAssert.Contains(result.Message, "3,2 м/с");
        StringAssert.Contains(result.Message, "Open-Meteo");
        StringAssert.Contains(result.Message, "05.10.2026 12:00");
        Assert.AreEqual("geocoding-api.open-meteo.com", handler.Requests[0].Host);
        StringAssert.Contains(Uri.UnescapeDataString(handler.Requests[0].Query), "name=Калининград");
        Assert.AreEqual("api.open-meteo.com", handler.Requests[1].Host);
        StringAssert.Contains(handler.Requests[1].Query, "latitude=54.71");
    }

    [TestMethod]
    [DataRow("https://evil.test")]
    [DataRow("../settings.json")]
    [DataRow("Москва\nhttp://evil.test")]
    [DataRow("")]
    public async Task WeatherToolAndServiceRejectInvalidLocationsBeforeNetwork(string city)
    {
        var handler = new WeatherHandler(Geocoding, Current);
        using var http = new HttpClient(handler);
        var service = new OpenMeteoWeatherService(http, new FixedClock());
        var tool = new WeatherAiTool(service, new Settings());

        Assert.IsFalse((await tool.ExecuteAsync(new Dictionary<string, string> { ["city"] = city })).IsSuccess);
        Assert.IsNull((await service.GetCurrentAsync(city)).Report);
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task WeatherRejectsExtraArgumentsAndUnknownToolsBeforeAnyRequest()
    {
        var handler = new WeatherHandler(Geocoding, Current);
        using var http = new HttpClient(handler);
        var tool = new WeatherAiTool(new OpenMeteoWeatherService(http, new FixedClock()), new Settings());
        var executor = new AiToolExecutor([tool]);
        var arguments = new Dictionary<string, string> { ["city"] = "Калининград", ["url"] = "https://evil.test" };

        Assert.IsFalse((await executor.ExecuteAsync(Plan(arguments))).IsSuccess);
        Assert.IsFalse((await tool.ExecuteAsync(arguments)).IsSuccess);
        Assert.IsFalse((await executor.ExecuteAsync(new AiPlan([new PlannedToolCall("unknown_weather", arguments)]))).IsSuccess);
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task WeatherAsksForCityWhenDefaultIsEmptyAndUsesExplicitCityWhenProvided()
    {
        var weather = new RecordingWeather();
        var settings = new Settings();
        settings.Current.DefaultWeatherCity = "";
        var tool = new WeatherAiTool(weather, settings);

        Assert.IsFalse((await tool.ExecuteAsync(new Dictionary<string, string>())).IsSuccess);
        Assert.IsNull(weather.City);
        Assert.IsFalse((await tool.ExecuteAsync(new Dictionary<string, string> { ["city"] = "Москва" })).IsSuccess);
        Assert.AreEqual("Москва", weather.City);
    }

    [TestMethod]
    public async Task WeatherRejectsAmbiguousCityWithoutChoosingFirstResult()
    {
        const string ambiguous = """
            {"results":[{"name":"Москва","latitude":55,"longitude":37},{"name":"Москва","latitude":51,"longitude":31}]}
            """;
        var handler = new WeatherHandler(ambiguous, Current);
        using var http = new HttpClient(handler);

        var result = await new OpenMeteoWeatherService(http, new FixedClock()).GetCurrentAsync("Москва");

        Assert.IsNull(result.Report);
        StringAssert.Contains(result.Error!, "несколько");
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow("2026-10-05T12:00", "km/h", 12.5)]
    [DataRow("2026-10-04T12:00", "m/s", 12.5)]
    [DataRow("2026-10-05T12:00", "m/s", 200.0)]
    public async Task WeatherRejectsUnexpectedUnitsStaleDataAndImpossibleValues(string time, string unit, double temperature)
    {
        var current = Current.Replace("2026-10-05T12:00", time, StringComparison.Ordinal)
            .Replace("m/s", unit, StringComparison.Ordinal)
            .Replace("12.5", temperature.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var handler = new WeatherHandler(Geocoding, current);
        using var http = new HttpClient(handler);

        Assert.IsNull((await new OpenMeteoWeatherService(http, new FixedClock()).GetCurrentAsync("Калининград")).Report);
    }

    [TestMethod]
    public async Task WeatherPropagatesCallerCancellationWithoutSendingRequest()
    {
        var handler = new WeatherHandler(Geocoding, Current);
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await new OpenMeteoWeatherService(http, new FixedClock()).GetCurrentAsync("Калининград", cancellation.Token));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task WeatherRejectsMissingNumbersMalformedJsonAndOversizedResponses()
    {
        foreach (var body in new[] { "{", "{}", Current.Replace("\"temperature_2m\":12.5,", "", StringComparison.Ordinal), new string(' ', 70000) })
        {
            var handler = new WeatherHandler(Geocoding, body);
            using var http = new HttpClient(handler);
            Assert.IsNull((await new OpenMeteoWeatherService(http, new FixedClock()).GetCurrentAsync("Калининград")).Report);
        }
    }

    private static AiPlan Plan(IReadOnlyDictionary<string, string> arguments) => new([new PlannedToolCall("get_weather", arguments)]);

    private sealed class WeatherHandler(string geocoding, string current) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.IsNull(request.Headers.Authorization);
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Requests.Count == 1 ? geocoding : current, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset Now => new(2026, 10, 5, 12, 10, 0, TimeSpan.FromHours(2));
    }

    private sealed class Settings : IAssistantSettingsService
    {
        public AssistantSettings Current { get; } = new();
        public ValueTask LoadAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingWeather : IWeatherService
    {
        public string? City { get; private set; }
        public ValueTask<WeatherLookupResult> GetCurrentAsync(string location, CancellationToken cancellationToken = default)
        {
            City = location;
            return ValueTask.FromResult(new WeatherLookupResult(null, "Нет данных"));
        }
    }
}
