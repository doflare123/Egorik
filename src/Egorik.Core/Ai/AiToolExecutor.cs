using Egorik.Core.Commands;

namespace Egorik.Core.Ai;

public sealed class AiToolExecutor(IEnumerable<IAiTool> tools) : IAiToolExecutor
{
    public const int MaximumPlanSteps = 8;

    private readonly IReadOnlyDictionary<string, IAiTool> _tools = BuildToolMap(tools);

    public IReadOnlyList<AiToolDescriptor> AvailableTools =>
        _tools.Values
            .Select(tool => tool.Descriptor)
            .OrderBy(tool => tool.Id, StringComparer.Ordinal)
            .ToArray();

    public AiPlanValidationResult Validate(AiPlan plan)
    {
        if (plan.Steps.Count == 0)
        {
            return AiPlanValidationResult.Failure("ИИ вернул пустой план.");
        }

        if (plan.Steps.Count > MaximumPlanSteps)
        {
            return AiPlanValidationResult.Failure(
                $"ИИ-план содержит больше {MaximumPlanSteps} шагов.");
        }

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            var validation = ValidateStep(plan.Steps[index], index);
            if (!validation.IsValid)
            {
                return validation;
            }
        }

        return AiPlanValidationResult.Success();
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        AiPlan plan,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(plan);
        if (!validation.IsValid)
        {
            return CommandResult.Failure(
                $"Небезопасный ИИ-план отклонён: {validation.Error}",
                "ai.plan");
        }

        var messages = new List<string>(plan.Steps.Count);
        var clientAction = ClientAction.None;

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = plan.Steps[index];
            var tool = _tools[step.ToolId];
            var result = await tool.ExecuteAsync(step.Arguments, cancellationToken);

            if (!result.IsSuccess)
            {
                return CommandResult.Failure(
                    $"ИИ-план остановлен на шаге {index + 1}: {result.Message}",
                    "ai.plan");
            }

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                messages.Add(result.Message);
            }

            if (result.ClientAction != ClientAction.None)
            {
                clientAction = result.ClientAction;
            }
        }

        return CommandResult.Success(
            messages.Count == 0 ? "Готово." : string.Join(" ", messages),
            "ai.plan",
            clientAction);
    }

    private AiPlanValidationResult ValidateStep(PlannedToolCall step, int index)
    {
        if (!_tools.TryGetValue(step.ToolId, out var tool))
        {
            return AiPlanValidationResult.Failure(
                $"шаг {index + 1} использует неизвестный инструмент «{step.ToolId}».");
        }

        var descriptor = tool.Descriptor;
        if (descriptor.Risk != CommandRisk.Low)
        {
            return AiPlanValidationResult.Failure(
                $"инструмент «{descriptor.Id}» не разрешён для автоматического выполнения.");
        }

        var parameters = descriptor.Parameters.ToDictionary(
            parameter => parameter.Name,
            StringComparer.Ordinal);

        foreach (var argument in step.Arguments)
        {
            if (!parameters.ContainsKey(argument.Key))
            {
                return AiPlanValidationResult.Failure(
                    $"инструмент «{descriptor.Id}» не принимает аргумент «{argument.Key}».");
            }
        }

        foreach (var parameter in descriptor.Parameters)
        {
            if (!step.Arguments.TryGetValue(parameter.Name, out var value))
            {
                if (parameter.IsRequired)
                {
                    return AiPlanValidationResult.Failure(
                        $"для инструмента «{descriptor.Id}» отсутствует аргумент «{parameter.Name}».");
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return AiPlanValidationResult.Failure(
                    $"аргумент «{parameter.Name}» инструмента «{descriptor.Id}» пуст.");
            }

            if (value.Length > parameter.MaxLength)
            {
                return AiPlanValidationResult.Failure(
                    $"аргумент «{parameter.Name}» инструмента «{descriptor.Id}» слишком длинный.");
            }

            if (value.Any(char.IsControl))
            {
                return AiPlanValidationResult.Failure(
                    $"аргумент «{parameter.Name}» инструмента «{descriptor.Id}» содержит управляющие символы.");
            }

            if (parameter.AllowedValues is { Count: > 0 } allowedValues &&
                !allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                return AiPlanValidationResult.Failure(
                    $"значение «{value}» не разрешено для аргумента «{parameter.Name}».");
            }
        }

        return AiPlanValidationResult.Success();
    }

    private static IReadOnlyDictionary<string, IAiTool> BuildToolMap(
        IEnumerable<IAiTool> tools)
    {
        var result = new Dictionary<string, IAiTool>(StringComparer.Ordinal);

        foreach (var tool in tools)
        {
            if (!result.TryAdd(tool.Descriptor.Id, tool))
            {
                throw new InvalidOperationException(
                    $"ИИ-инструмент «{tool.Descriptor.Id}» зарегистрирован дважды.");
            }
        }

        return result;
    }
}
