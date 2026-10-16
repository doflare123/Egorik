using Egorik.Core.Actions;
using Egorik.Core.Ai;
using Egorik.Core.Commands;

namespace Egorik.Infrastructure.Actions;

// Reuse the locally checked implementations; no planner or network SDK is invoked
// by this adapter. Registration here does not expose new actions to the AI.
public sealed class BuiltInActionProvider(IEnumerable<IAiTool> tools) : IAssistantActionProvider
{
    private readonly IReadOnlyDictionary<string, IAiTool> _tools = tools
        .Where(tool => tool.Descriptor.Id != "application_action" && tool.Descriptor.Risk == CommandRisk.Low)
        .ToDictionary(tool => "builtin." + tool.Descriptor.Id, StringComparer.Ordinal);

    public ActionProviderManifest Manifest { get; } = new("builtin", "Встроенные действия");

    public IReadOnlyList<AssistantActionDescriptor> Actions => _tools.Select(pair => new AssistantActionDescriptor(
        pair.Key, Title(pair.Value.Descriptor.Id), Description(pair.Value.Descriptor.Id, pair.Value.Descriptor.Description),
        pair.Value.Descriptor.Parameters.Select(parameter => new ActionParameterDescriptor(
            parameter.Name, ParameterTitle(parameter.Name, parameter.Description),
            parameter.AllowedValues is null ? ActionParameterKind.Text : ActionParameterKind.Choice,
            parameter.IsRequired, parameter.MaxLength, parameter.AllowedValues,
            ValueTitles: parameter.AllowedValues?.ToDictionary(value => value, ChoiceTitle, StringComparer.Ordinal))).ToArray())).ToArray();

    public ValueTask<CommandResult> ExecuteAsync(ActionInvocation invocation, CancellationToken cancellationToken = default) =>
        _tools.TryGetValue(invocation.ActionId, out var tool)
            ? tool.ExecuteAsync(invocation.Arguments, cancellationToken)
            : ValueTask.FromResult(CommandResult.Failure("Встроенное действие не найдено."));

    private static string Title(string id) => id switch
    {
        "application_open" => "Открыть приложение",
        "website_open" => "Открыть сайт",
        "web_search" => "Поиск в интернете",
        "media_control" => "Управление медиа",
        "calculator" => "Вычислить выражение",
        "date_time" => "Дата и время",
        "client_navigate" => "Открыть раздел Egorik",
        "assistant_reply" => "Ответ ассистента",
        "get_weather" => "Узнать погоду",
        "spreadsheet_search" => "Поиск в разрешённой книге Excel",
        _ => id
    };

    private static string ParameterTitle(string name, string fallback) => name switch
    {
        "application" => "Приложение",
        "site" => "Сайт",
        "query" => "Поисковый запрос",
        "action" => "Действие",
        "expression" => "Выражение",
        "kind" => "Что сообщить",
        "section" => "Раздел",
        "message" => "Текст ответа",
        "city" => "Город",
        "document" => "Книга Excel",
        _ => fallback
    };

    private static string Description(string id, string fallback) => id switch
    {
        "application_open" => "Открывает приложение из списка в настройках.",
        "website_open" => "Открывает сайт из списка в настройках.",
        "web_search" => "Открывает результаты поиска в браузере.",
        "media_control" => "Управляет треками и общей громкостью колонок.",
        "calculator" => "Вычисляет арифметическое выражение.",
        "date_time" => "Добавляет текущую дату или время к ответу.",
        "client_navigate" => "Открывает выбранный раздел Egorik.",
        "assistant_reply" => "Добавляет текст к ответу по завершении сценария.",
        "get_weather" => "Получает текущую погоду через интернет. Пустой город берётся из настроек.",
        "spreadsheet_search" => "Ищет текст в выбранной разрешённой книге Excel.",
        _ => fallback
    };

    private static string ChoiceTitle(string value) => value switch
    {
        "PlayPause" => "Воспроизведение / пауза",
        "Next" => "Следующий трек",
        "Previous" => "Предыдущий трек",
        "Stop" => "Остановить",
        "VolumeUp" => "Увеличить громкость колонок",
        "VolumeDown" => "Уменьшить громкость колонок",
        "Mute" => "Выключить / включить звук",
        "time" => "Время",
        "date" => "Дата",
        "settings" => "Настройки",
        "scenarios" => "Сценарии",
        _ => value
    };
}
