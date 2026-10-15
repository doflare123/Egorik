using Egorik.Core.Applications;
using Egorik.Core.Commands;
using Egorik.Core.Platform;

namespace Egorik.Infrastructure.Applications;

public sealed class YandexMusicApplicationIntegration(
    IYandexMusicController controller) : IApplicationIntegration
{
    private static readonly IReadOnlyDictionary<string, YandexMusicAction> MediaActions =
        new Dictionary<string, YandexMusicAction>(StringComparer.Ordinal)
        {
            ["play_pause"] = YandexMusicAction.PlayPause,
            ["next"] = YandexMusicAction.Next,
            ["previous"] = YandexMusicAction.Previous,
            ["stop"] = YandexMusicAction.Stop,
            ["volume_up"] = YandexMusicAction.VolumeUp,
            ["volume_down"] = YandexMusicAction.VolumeDown,
            ["mute"] = YandexMusicAction.Mute,
            ["unmute"] = YandexMusicAction.Unmute
        };

    private static readonly ApplicationActionDescriptor OpenAction = new(
        "open",
        "Открыть Яндекс Музыку",
        "Открывает установленное desktop-приложение Яндекс Музыка.",
        [],
        IsAvailableToAi: true);

    private static readonly ApplicationActionDescriptor SearchAction = new(
        "search",
        "Найти в Яндекс Музыке",
        "Открывает поиск исполнителя, альбома или трека в desktop-приложении Яндекс Музыка.",
        [
            new ApplicationActionParameterDescriptor(
                "query",
                "Название исполнителя, альбома или трека.",
                MaxLength: 200)
        ],
        IsAvailableToAi: true);

    private static readonly ApplicationActionDescriptor StartMyWaveAction = new(
        "start_my_wave",
        "Включить Мою волну",
        "Запускает Мою волну через проверенный deeplink desktop-приложения Яндекс Музыка.",
        [],
        IsAvailableToAi: true);

    public ApplicationIntegrationDescriptor Descriptor { get; } = new(
        "yandex_music",
        "Яндекс Музыка",
        ["яндекс музыка", "yandex music"],
        [
            OpenAction,
            SearchAction,
            StartMyWaveAction,
            new ApplicationActionDescriptor(
                "play_pause",
                "Пауза или продолжение",
                "Переключает паузу медиасессии приложения Яндекс Музыка.",
                [],
                IsAvailableToAi: true),
            new ApplicationActionDescriptor(
                "next",
                "Следующий трек",
                "Переключает медиасессию Яндекс Музыки на следующий трек.",
                [],
                IsAvailableToAi: true),
            new ApplicationActionDescriptor(
                "previous",
                "Предыдущий трек",
                "Переключает медиасессию Яндекс Музыки на предыдущий трек.",
                [],
                IsAvailableToAi: true),
            new ApplicationActionDescriptor(
                "stop",
                "Остановить воспроизведение",
                "Останавливает медиасессию приложения Яндекс Музыка.",
                [],
                IsAvailableToAi: true),
            new ApplicationActionDescriptor("volume_up", "Музыку громче",
                "Повышает только громкость аудиосессий Яндекс Музыки на 10 процентных пунктов.", [], IsAvailableToAi: true),
            new ApplicationActionDescriptor("volume_down", "Музыку тише",
                "Понижает только громкость аудиосессий Яндекс Музыки на 10 процентных пунктов.", [], IsAvailableToAi: true),
            new ApplicationActionDescriptor("mute", "Отключить звук музыки",
                "Отключает звук только аудиосессий Яндекс Музыки.", [], IsAvailableToAi: true),
            new ApplicationActionDescriptor("unmute", "Включить звук музыки",
                "Включает звук только аудиосессий Яндекс Музыки.", [], IsAvailableToAi: true),
            new ApplicationActionDescriptor("set_volume", "Громкость музыки в процентах",
                "Устанавливает громкость только аудиосессий Яндекс Музыки, не колонок и других приложений.",
                [new ApplicationActionParameterDescriptor("percent", "Целое число процентов от 0 до 100.",
                    MaxLength: 3, AllowedValues: Enumerable.Range(0, 101).Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray())],
                IsAvailableToAi: true)
        ]);

    public async ValueTask<CommandResult> ExecuteAsync(
        ApplicationActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ApplicationId != Descriptor.Id)
        {
            return CommandResult.Failure("Некорректный ID Яндекс Музыки.", "application.action");
        }

        LaunchResult result;
        if (request.ActionId == OpenAction.Id)
        {
            result = await controller.OpenAsync(cancellationToken);
        }
        else if (request.ActionId == SearchAction.Id &&
                 request.Arguments.TryGetValue("query", out var query) &&
                 !string.IsNullOrWhiteSpace(query))
        {
            result = await controller.SearchAsync(query.Trim(), cancellationToken);
        }
        else if (request.ActionId == StartMyWaveAction.Id)
        {
            result = await controller.StartMyWaveAsync(cancellationToken);
        }
        else if (request.ActionId == "set_volume" && request.Arguments.Count == 1 &&
                 request.Arguments.TryGetValue("percent", out var percentText) &&
                 int.TryParse(percentText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var percent) &&
                 percent is >= 0 and <= 100)
        {
            result = await controller.SetVolumeAsync(percent, cancellationToken);
        }
        else if (MediaActions.TryGetValue(request.ActionId, out var mediaAction) && request.Arguments.Count == 0)
        {
            result = await controller.ExecuteAsync(mediaAction, cancellationToken);
        }
        else
        {
            return CommandResult.Failure(
                "Неизвестное действие Яндекс Музыки.",
                "application.action");
        }

        return result.IsSuccess
            ? CommandResult.Success(result.Message, "application.action")
            : CommandResult.Failure(result.Message, "application.action");
    }
}
