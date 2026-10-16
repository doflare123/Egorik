using System.Globalization;
using Egorik.Core.Commands;

namespace Egorik.Core.Actions;

public sealed class AssistantActionRegistry : IAssistantActionRegistry
{
    public const int ApiVersion = 1;
    private readonly IReadOnlyList<IAssistantActionProvider> _providers;

    public AssistantActionRegistry(IEnumerable<IAssistantActionProvider> providers)
    {
        _providers = providers.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in _providers)
        {
            if (provider.Manifest.ApiVersion != ApiVersion || !IsSafeId(provider.Manifest.Id) ||
                !ids.Add(provider.Manifest.Id))
            {
                throw new InvalidOperationException("Несовместимая версия API или повторный ID поставщика действий.");
            }
        }

        _ = BuildMap();
    }

    public IReadOnlyList<AssistantActionDescriptor> AvailableActions => BuildMap().Values
        .Select(entry => entry.Descriptor).Where(action => action.Risk == CommandRisk.Low)
        .OrderBy(action => action.Title, StringComparer.CurrentCulture).ToArray();

    public ActionValidationResult Validate(ActionInvocation invocation) => Validate(invocation, BuildMap());

    public async ValueTask<CommandResult> ExecuteAsync(ActionInvocation invocation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (invocation.Arguments is null)
        {
            return CommandResult.Failure("Действие отклонено: не задан набор аргументов.", "action.execute");
        }

        var snapshot = invocation with { Arguments = new Dictionary<string, string>(invocation.Arguments, StringComparer.Ordinal) };
        var map = BuildMap();
        var validation = Validate(snapshot, map);
        if (!validation.IsValid)
        {
            return CommandResult.Failure($"Действие отклонено: {validation.Error}", "action.execute");
        }

        try
        {
            return await map[snapshot.ActionId].Provider.ExecuteAsync(snapshot, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return CommandResult.Failure($"Ошибка действия «{snapshot.ActionId}»: {exception.Message}", "action.execute");
        }
    }

    private static ActionValidationResult Validate(ActionInvocation invocation, IReadOnlyDictionary<string, RegisteredAction> map)
    {
        if (string.IsNullOrEmpty(invocation.ActionId) || !map.TryGetValue(invocation.ActionId, out var entry))
        {
            return new(false, "Действие отсутствует в локальном реестре или плагин выключен.");
        }

        if (entry.Descriptor.Risk != CommandRisk.Low)
        {
            return new(false, "Действие требует отдельного механизма подтверждения.");
        }

        if (invocation.Arguments is null)
        {
            return new(false, "Не задан набор аргументов.");
        }

        var parameters = entry.Descriptor.Parameters;
        if (invocation.Arguments.Keys.Any(key => !parameters.Any(parameter => parameter.Name == key)))
        {
            return new(false, "Передан неизвестный аргумент.");
        }

        foreach (var parameter in parameters)
        {
            if (!invocation.Arguments.TryGetValue(parameter.Name, out var value))
            {
                if (parameter.IsRequired)
                {
                    return new(false, $"Заполните поле «{parameter.Title}».");
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(value) || value.Length > parameter.MaxLength || value.Any(char.IsControl))
            {
                return new(false, $"Некорректное значение поля «{parameter.Title}».");
            }

            var valid = parameter.Kind switch
            {
                ActionParameterKind.Text => true,
                ActionParameterKind.Choice => parameter.AllowedValues?.Contains(value, StringComparer.OrdinalIgnoreCase) == true,
                ActionParameterKind.Integer => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                                               number >= parameter.Minimum && number <= parameter.Maximum,
                ActionParameterKind.Boolean => value is "true" or "false",
                _ => false
            };
            if (!valid || parameter.AllowedValues is not null && !parameter.AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                return new(false, $"Значение поля «{parameter.Title}» не входит в допустимый диапазон.");
            }
        }

        return new(true);
    }

    private Dictionary<string, RegisteredAction> BuildMap()
    {
        var map = new Dictionary<string, RegisteredAction>(StringComparer.Ordinal);
        foreach (var provider in _providers)
        {
            foreach (var action in provider.Actions)
            {
                if (!IsSafeId(action.Id) || !action.Id.StartsWith(provider.Manifest.Id + ".", StringComparison.Ordinal) ||
                    action.Parameters.Any(parameter => !IsSafeId(parameter.Name) || parameter.MaxLength is < 1 or > 8192 ||
                                                       !Enum.IsDefined(parameter.Kind) || parameter.Minimum > parameter.Maximum) ||
                    action.Parameters.Select(parameter => parameter.Name).Distinct(StringComparer.Ordinal).Count() != action.Parameters.Count ||
                    !map.TryAdd(action.Id, new(provider, action)))
                {
                    throw new InvalidOperationException($"Некорректный или повторный контракт действия «{action.Id}».");
                }
            }
        }

        return map;
    }

    private static bool IsSafeId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 160 &&
        id.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '-');

    private sealed record RegisteredAction(IAssistantActionProvider Provider, AssistantActionDescriptor Descriptor);
}
