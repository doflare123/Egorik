using Egorik.Core.Ai;
using Egorik.Core.Applications;
using Egorik.Core.Commands;

namespace Egorik.Infrastructure.Ai;

public sealed class ApplicationActionAiTool : IAiTool
{
    private readonly IApplicationActionRegistry _registry;
    private readonly IReadOnlyDictionary<string, AutomaticAction> _actions;

    public ApplicationActionAiTool(IApplicationActionRegistry registry)
    {
        _registry = registry;
        _actions = registry.AvailableApplications
            .SelectMany(application => application.Actions
                .Where(action =>
                    action.Risk == CommandRisk.Low &&
                    action.IsAvailableToAi &&
                    action.Parameters.Count <= 1)
                .Select(action => new AutomaticAction(application, action)))
            .ToDictionary(action => action.Operation, StringComparer.Ordinal);

        var catalog = string.Join(
            " ",
            _actions.Values
                .OrderBy(action => action.Operation, StringComparer.Ordinal)
                .Select(action =>
                {
                    var input = action.Action.Parameters.Count == 1
                        ? $" Аргумент input: {action.Action.Parameters[0].Description}"
                        : " Аргумент input не передавать.";
                    return $"{action.Operation} — {action.Action.Description}{input}";
                }));
        var maximumInputLength = _actions.Values
            .SelectMany(action => action.Action.Parameters)
            .Select(parameter => parameter.MaxLength)
            .DefaultIfEmpty(1)
            .Max();

        Descriptor = new AiToolDescriptor(
            "application_action",
            "Выполняет только зарегистрированное низкорисковое действие приложения. " + catalog,
            [
                new AiToolParameterDescriptor(
                    "operation",
                    "Точная пара приложение.действие из локального каталога.",
                    AllowedValues: _actions.Keys.Order(StringComparer.Ordinal).ToArray()),
                new AiToolParameterDescriptor(
                    "input",
                    "Единственный текстовый аргумент действия; не передавать для действий без аргумента.",
                    IsRequired: false,
                    MaxLength: maximumInputLength)
            ]);
    }

    public AiToolDescriptor Descriptor { get; }

    public async ValueTask<CommandResult> ExecuteAsync(
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("operation", out var operation) ||
            !_actions.TryGetValue(operation, out var selected))
        {
            return CommandResult.Failure(
                "Действие приложения отсутствует в разрешённом каталоге.",
                Descriptor.Id);
        }

        var actionArguments = new Dictionary<string, string>(StringComparer.Ordinal);
        if (selected.Action.Parameters.Count == 1)
        {
            if (arguments.TryGetValue("input", out var input))
            {
                actionArguments[selected.Action.Parameters[0].Name] = input;
            }
        }
        else if (arguments.ContainsKey("input"))
        {
            return CommandResult.Failure(
                $"Действие «{operation}» не принимает текстовый аргумент.",
                Descriptor.Id);
        }

        return await _registry.ExecuteAsync(
            new ApplicationActionRequest(
                selected.Application.Id,
                selected.Action.Id,
                actionArguments),
            CommandRisk.Low,
            cancellationToken);
    }

    private sealed record AutomaticAction(
        ApplicationIntegrationDescriptor Application,
        ApplicationActionDescriptor Action)
    {
        public string Operation => $"{Application.Id}.{Action.Id}";
    }
}
