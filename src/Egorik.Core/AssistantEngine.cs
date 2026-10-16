using Egorik.Core.Ai;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;
using Egorik.Core.Scenarios;

namespace Egorik.Core;

public sealed class AssistantEngine(
    ICommandRouter commandRouter,
    IScenarioRepository scenarioRepository,
    IAssistantSettingsService settingsService,
    IAiPlanner aiPlanner,
    IAiToolExecutor aiToolExecutor,
    IScenarioFlowExecutor? scenarioFlowExecutor = null)
{
    private const int MaximumScenarioDepth = 3;
    private const int MaximumScenarioCommands = 20;

    public IReadOnlyList<CommandDescriptor> AvailableCommands => commandRouter.AvailableCommands;

    public async ValueTask<CommandResult> ExecuteAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return CommandResult.Failure("Скажи или напиши команду.");
        }

        return await ExecuteInternalAsync(text, 0, cancellationToken);
    }

    private async ValueTask<CommandResult> ExecuteInternalAsync(
        string text,
        int depth,
        CancellationToken cancellationToken)
    {
        var normalized = TextNormalizer.Normalize(text);
        normalized = TextNormalizer.RemoveWakeName(
            normalized,
            settingsService.Current.AssistantName);

        if (normalized.Length == 0)
        {
            return CommandResult.Success("Слушаю.");
        }

        if (depth < MaximumScenarioDepth)
        {
            var scenarioResult = await TryExecuteScenarioAsync(normalized, depth, cancellationToken);
            if (scenarioResult is not null)
            {
                return scenarioResult;
            }
        }

        var context = new CommandContext(text, normalized, depth);
        var routed = await commandRouter.RouteAsync(context, cancellationToken);
        if (routed is not null)
        {
            return routed;
        }

        if (aiPlanner.IsConfigured)
        {
            try
            {
                var plan = await aiPlanner.CreatePlanAsync(
                    text,
                    aiToolExecutor.AvailableTools,
                    cancellationToken);

                if (plan is { Steps.Count: > 0 })
                {
                    return await aiToolExecutor.ExecuteAsync(plan, cancellationToken);
                }

                return CommandResult.Failure(
                    "ИИ не смог составить план из разрешённых инструментов.",
                    "ai.plan");
            }
            catch (AiPlannerException exception)
            {
                return CommandResult.Failure(
                    $"ИИ-планировщик недоступен: {exception.Message}",
                    "ai.plan");
            }
        }

        return CommandResult.Failure(
            "Я пока не знаю такую команду. Напиши «что ты умеешь», чтобы увидеть доступные возможности.");
    }

    private async ValueTask<CommandResult?> TryExecuteScenarioAsync(
        string normalizedText,
        int depth,
        CancellationToken cancellationToken)
    {
        var scenarios = await scenarioRepository.GetAllAsync(cancellationToken);
        var scenario = scenarios.FirstOrDefault(candidate =>
            candidate.Enabled &&
            TextNormalizer.Normalize(candidate.Trigger) == normalizedText);

        if (scenario is null)
        {
            return null;
        }

        if (scenario.Flow is not null)
        {
            if (scenarioFlowExecutor is null)
            {
                return CommandResult.Failure("Исполнитель блок-схем не подключён.", "scenario.execute");
            }

            return await scenarioFlowExecutor.ExecuteAsync(
                scenario.Flow,
                (command, token) => ExecuteInternalAsync(command, depth + 1, token),
                cancellationToken: cancellationToken);
        }

        if (scenario.Commands.Count == 0)
        {
            return CommandResult.Failure($"Сценарий «{scenario.Name}» не содержит команд.");
        }

        var messages = new List<string>();
        foreach (var command in scenario.Commands.Take(MaximumScenarioCommands))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExecuteInternalAsync(command, depth + 1, cancellationToken);
            messages.Add(result.Message);

            if (!result.IsSuccess)
            {
                return CommandResult.Failure(
                    $"Сценарий «{scenario.Name}» остановлен: {result.Message}",
                    "scenario.execute");
            }
        }

        return CommandResult.Success(
            $"Сценарий «{scenario.Name}» выполнен. {string.Join(" ", messages)}",
            "scenario.execute");
    }
}
