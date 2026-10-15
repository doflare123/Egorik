using System.Globalization;
using Egorik.Core.Ai;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Weather;

namespace Egorik.Infrastructure.Ai;

public sealed class WeatherAiTool(IWeatherService weather, IAssistantSettingsService settings) : IAiTool
{
    public AiToolDescriptor Descriptor { get; } = new("get_weather",
        "Получает актуальный расчёт текущей погоды из Open-Meteo. При отсутствии city берёт город из настроек. " +
        "Не открывает браузер. Не используй для будущих дат: этот инструмент возвращает только текущие условия.",
        [new AiToolParameterDescriptor("city", "Город в именительном падеже; при неоднозначности добавь страну или область через запятую. " +
            "Передавай только если пользователь явно назвал город, иначе не передавай аргумент.", IsRequired: false, MaxLength: 100)]);

    public async ValueTask<CommandResult> ExecuteAsync(IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (arguments.Keys.Any(key => key != "city"))
        {
            return CommandResult.Failure("Погодный инструмент принимает только название города.", Descriptor.Id);
        }

        var city = arguments.TryGetValue("city", out var specified) ? specified?.Trim() ?? "" : settings.Current.DefaultWeatherCity?.Trim() ?? "";
        if (!WeatherLocation.IsValid(city))
        {
            return CommandResult.Failure("Укажи город для погоды в запросе или настройках.", Descriptor.Id);
        }

        var result = await weather.GetCurrentAsync(city, cancellationToken);
        if (result.Report is not { } report)
        {
            return CommandResult.Failure(result.Error ?? "Не удалось получить актуальную погоду.", Descriptor.Id);
        }

        var culture = CultureInfo.GetCultureInfo("ru-RU");
        return CommandResult.Success($"{report.Location}: {Describe(report.WeatherCode)}, " +
            $"{report.Temperature.ToString("0.#", culture)} °C, ощущается как {report.FeelsLike.ToString("0.#", culture)} °C. " +
            $"Ветер {report.WindSpeed.ToString("0.#", culture)} м/с, влажность {report.Humidity} %, " +
            $"осадки {report.Precipitation.ToString("0.#", culture)} мм за последний интервал расчёта. " +
            $"Источник: Open-Meteo, расчёт на {report.ValidAt.ToString("dd.MM.yyyy HH:mm", culture)} ({report.TimeZone}).", Descriptor.Id);
    }

    private static string Describe(int code) => code switch
    {
        0 => "ясно",
        1 => "преимущественно ясно",
        2 => "переменная облачность",
        3 => "пасмурно",
        45 or 48 => "туман",
        51 or 53 or 55 => "морось",
        56 or 57 => "переохлаждённая морось",
        61 or 63 or 65 => "дождь",
        66 or 67 => "ледяной дождь",
        71 or 73 or 75 or 77 => "снег",
        80 or 81 or 82 => "ливень",
        85 or 86 => "снегопад",
        95 or 96 or 99 => "гроза",
        _ => "условия по данным погодного сервиса"
    };
}
