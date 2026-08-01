using Egorik.Core.Commands;

namespace Egorik.Core.Applications;

public sealed class ApplicationActionRegistry : IApplicationActionRegistry
{
    private readonly IReadOnlyDictionary<string, RegisteredApplication> _applications;

    public ApplicationActionRegistry(IEnumerable<IApplicationIntegration> integrations)
    {
        _applications = BuildApplicationMap(integrations);
        AvailableApplications = _applications.Values
            .Select(application => application.Integration.Descriptor)
            .OrderBy(application => application.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ApplicationIntegrationDescriptor> AvailableApplications { get; }

    public ApplicationActionValidationResult Validate(
        ApplicationActionRequest request,
        CommandRisk maximumAllowedRisk)
    {
        if (!_applications.TryGetValue(request.ApplicationId, out var application))
        {
            return ApplicationActionValidationResult.Failure(
                $"Неизвестное приложение «{request.ApplicationId}».");
        }

        if (!application.Actions.TryGetValue(request.ActionId, out var action))
        {
            return ApplicationActionValidationResult.Failure(
                $"Приложение «{application.Integration.Descriptor.DisplayName}» " +
                $"не поддерживает действие «{request.ActionId}».");
        }

        if (action.Risk > maximumAllowedRisk)
        {
            return ApplicationActionValidationResult.Failure(
                $"Действие «{action.Title}» требует отдельного подтверждения пользователя.");
        }

        var parameters = action.Parameters.ToDictionary(
            parameter => parameter.Name,
            StringComparer.Ordinal);

        foreach (var argument in request.Arguments)
        {
            if (!parameters.ContainsKey(argument.Key))
            {
                return ApplicationActionValidationResult.Failure(
                    $"Действие «{action.Title}» не принимает аргумент «{argument.Key}».");
            }
        }

        foreach (var parameter in action.Parameters)
        {
            if (!request.Arguments.TryGetValue(parameter.Name, out var value))
            {
                if (parameter.IsRequired)
                {
                    return ApplicationActionValidationResult.Failure(
                        $"Для действия «{action.Title}» отсутствует аргумент «{parameter.Name}».");
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return ApplicationActionValidationResult.Failure(
                    $"Аргумент «{parameter.Name}» действия «{action.Title}» пуст.");
            }

            if (value.Length > parameter.MaxLength)
            {
                return ApplicationActionValidationResult.Failure(
                    $"Аргумент «{parameter.Name}» действия «{action.Title}» слишком длинный.");
            }

            if (value.Any(char.IsControl))
            {
                return ApplicationActionValidationResult.Failure(
                    $"Аргумент «{parameter.Name}» действия «{action.Title}» " +
                    "содержит управляющие символы.");
            }

            if (parameter.AllowedValues is { Count: > 0 } allowedValues &&
                !allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                return ApplicationActionValidationResult.Failure(
                    $"Значение «{value}» не разрешено для аргумента «{parameter.Name}».");
            }
        }

        return ApplicationActionValidationResult.Success();
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        ApplicationActionRequest request,
        CommandRisk maximumAllowedRisk,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request, maximumAllowedRisk);
        if (!validation.IsValid)
        {
            return CommandResult.Failure(
                $"Действие приложения отклонено: {validation.Error}",
                "application.action");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await _applications[request.ApplicationId]
            .Integration
            .ExecuteAsync(request, cancellationToken);
    }

    private static IReadOnlyDictionary<string, RegisteredApplication> BuildApplicationMap(
        IEnumerable<IApplicationIntegration> integrations)
    {
        var applications = new Dictionary<string, RegisteredApplication>(StringComparer.Ordinal);

        foreach (var integration in integrations)
        {
            var descriptor = integration.Descriptor;
            if (!IsSafeId(descriptor.Id))
            {
                throw new InvalidOperationException(
                    "ID интеграции приложения должен быть в формате snake_case.");
            }

            var actions = new Dictionary<string, ApplicationActionDescriptor>(StringComparer.Ordinal);
            foreach (var action in descriptor.Actions)
            {
                if (!IsSafeId(action.Id))
                {
                    throw new InvalidOperationException(
                        $"ID действия приложения «{descriptor.Id}» должен быть в формате snake_case.");
                }

                if (!actions.TryAdd(action.Id, action))
                {
                    throw new InvalidOperationException(
                        $"Действие «{descriptor.Id}.{action.Id}» зарегистрировано дважды.");
                }

                var parameterNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var parameter in action.Parameters)
                {
                    if (!IsSafeId(parameter.Name) ||
                        parameter.MaxLength <= 0 ||
                        !parameterNames.Add(parameter.Name))
                    {
                        throw new InvalidOperationException(
                            $"Некорректный параметр действия «{descriptor.Id}.{action.Id}».");
                    }
                }
            }

            if (!applications.TryAdd(
                    descriptor.Id,
                    new RegisteredApplication(integration, actions)))
            {
                throw new InvalidOperationException(
                    $"Интеграция приложения «{descriptor.Id}» зарегистрирована дважды.");
            }
        }

        return applications;
    }

    private static bool IsSafeId(string value) =>
        value.Length is >= 1 and <= 64 &&
        value[0] is >= 'a' and <= 'z' &&
        value.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private sealed record RegisteredApplication(
        IApplicationIntegration Integration,
        IReadOnlyDictionary<string, ApplicationActionDescriptor> Actions);
}
