using Egorik.Core.Actions;
using Egorik.Core.Commands;
using Egorik.Core.Configuration;

namespace Egorik.Core.Scenarios;

public sealed class ScenarioFlowExecutor(
    IAssistantActionRegistry actions,
    IAssistantSettingsService settings,
    TimeProvider? timeProvider = null) : IScenarioFlowExecutor
{
    public const int MaximumNodes = 32;
    public const int MaximumTotalDelaySeconds = 120;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public ScenarioValidationResult Validate(ScenarioFlow flow)
    {
        if (flow.Version != 1 || flow.Nodes is null || flow.Nodes.Count is < 2 or > MaximumNodes)
        {
            return new(false, $"Нужна схема версии 1, содержащая от 2 до {MaximumNodes} блоков.");
        }

        var nodes = new Dictionary<Guid, ScenarioNode>();
        foreach (var node in flow.Nodes)
        {
            if (node is null || node.Id == Guid.Empty || !nodes.TryAdd(node.Id, node) || !Enum.IsDefined(node.Kind))
            {
                return new(false, "Неизвестный тип или повторный ID блока.");
            }
        }

        if (!nodes.TryGetValue(flow.EntryNodeId, out var start) || start.Kind != ScenarioNodeKind.Start ||
            nodes.Values.Count(node => node.Kind == ScenarioNodeKind.Start) != 1 ||
            !nodes.Values.Any(node => node.Kind == ScenarioNodeKind.End))
        {
            return new(false, "Схеме нужны один старт и хотя бы одно завершение.");
        }

        long totalDelay = 0;
        foreach (var node in nodes.Values)
        {
            ScenarioValidationResult Fail(string message) => new(false, message, node.Id);
            if (node.Title is null || node.Title.Length > 120 || node.Value is null || node.Value.Length > 300 ||
                node.Value.Any(char.IsControl) || !Enum.IsDefined(node.Condition))
            {
                return Fail("Некорректное название или значение блока.");
            }

            var edges = new[] { node.Next, node.Otherwise }.Where(edge => edge.HasValue).Select(edge => edge!.Value);
            if (edges.Any(edge => !nodes.ContainsKey(edge) || edge == flow.EntryNodeId))
            {
                return Fail("Переход ссылается на удалённый блок или на старт.");
            }

            if (node.Kind == ScenarioNodeKind.End ? node.Next is not null : node.Next is null)
            {
                return Fail("Укажите следующий блок; у завершения переходов быть не должно.");
            }

            if (node.Kind == ScenarioNodeKind.Condition ? node.Otherwise is null : node.Otherwise is not null)
            {
                return Fail("У условия должны быть переходы «Да» и «Нет».");
            }

            if (node.Kind == ScenarioNodeKind.Action)
            {
                var validation = actions.Validate(new ActionInvocation(node.ActionId ?? "", node.Arguments ?? new Dictionary<string, string>()));
                if (!validation.IsValid)
                {
                    return Fail(validation.Error!);
                }
            }
            else if (node.ActionId is not null || node.Arguments is { Count: > 0 })
            {
                return Fail("Аргументы действия допустимы только в блоке действия.");
            }

            if (node.Kind == ScenarioNodeKind.Delay)
            {
                if (node.DelaySeconds is < 1 or > 30)
                {
                    return Fail("Пауза должна длиться от 1 до 30 секунд.");
                }

                totalDelay += node.DelaySeconds;
            }
            else if (node.DelaySeconds != 0)
            {
                return Fail("Длительность допустима только в блоке паузы.");
            }

            if (node.Kind == ScenarioNodeKind.Condition && node.Condition != ScenarioConditionKind.AiEnabled &&
                string.IsNullOrWhiteSpace(node.Value) || node.Kind == ScenarioNodeKind.LegacyCommand && string.IsNullOrWhiteSpace(node.Value))
            {
                return Fail("Заполните значение блока.");
            }
        }

        if (totalDelay > MaximumTotalDelaySeconds)
        {
            return new(false, $"Сумма пауз не должна превышать {MaximumTotalDelaySeconds} секунд.");
        }

        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        bool Visit(Guid id)
        {
            if (visiting.Contains(id)) { return false; }
            if (visited.Contains(id)) { return true; }
            visiting.Add(id);
            var node = nodes[id];
            foreach (var next in new[] { node.Next, node.Otherwise })
            {
                if (next is not null && !Visit(next.Value)) { return false; }
            }

            visiting.Remove(id);
            visited.Add(id);
            return true;
        }

        if (!Visit(flow.EntryNodeId))
        {
            return new(false, "Циклы в этой версии редактора запрещены.");
        }

        return visited.Count == nodes.Count ? new(true) : new(false, "Есть блоки без пути от старта.");
    }

    public async ValueTask<CommandResult> ExecuteAsync(
        ScenarioFlow flow,
        Func<string, CancellationToken, ValueTask<CommandResult>>? legacyCommand = null,
        IProgress<ScenarioExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Execute the same immutable snapshot that passed whole-graph validation.
        if (flow.Nodes is not null)
        {
            flow = flow with
            {
                Nodes = flow.Nodes.Select(node => node is null ? null! : node with
                {
                    Arguments = node.Arguments is null ? null : new Dictionary<string, string>(node.Arguments, StringComparer.Ordinal)
                }).ToArray()
            };
        }

        var validation = Validate(flow);
        if (!validation.IsValid)
        {
            return CommandResult.Failure($"Схема отклонена: {validation.Error}", "scenario.execute");
        }

        if (legacyCommand is null && flow.Nodes!.Any(node => node.Kind == ScenarioNodeKind.LegacyCommand))
        {
            return CommandResult.Failure("Текстовые блоки не подключены к локальному маршрутизатору.", "scenario.execute");
        }

        var nodes = flow.Nodes!.ToDictionary(node => node.Id);
        var current = flow.EntryNodeId;
        var messages = new List<string>();
        var clientAction = ClientAction.None;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = nodes[current];
            progress?.Report(new(node.Id, $"Выполняется: {node.Title}"));
            if (node.Kind == ScenarioNodeKind.End)
            {
                progress?.Report(new(node.Id, "Сценарий завершён.", true));
                return CommandResult.Success(messages.Count == 0 ? "Сценарий выполнен." : string.Join(" ", messages), "scenario.execute", clientAction);
            }

            CommandResult? result = null;
            if (node.Kind == ScenarioNodeKind.Action)
            {
                result = await actions.ExecuteAsync(new(node.ActionId!, node.Arguments ?? new Dictionary<string, string>()), cancellationToken);
            }
            else if (node.Kind == ScenarioNodeKind.Delay)
            {
                await Task.Delay(TimeSpan.FromSeconds(node.DelaySeconds), _time, cancellationToken);
            }
            else if (node.Kind == ScenarioNodeKind.LegacyCommand)
            {
                result = legacyCommand is null
                    ? CommandResult.Failure("Текстовый блок не подключён к локальному маршрутизатору.")
                    : await legacyCommand(node.Value, cancellationToken);
            }

            if (result is not null)
            {
                if (!result.IsSuccess)
                {
                    progress?.Report(new(node.Id, result.Message, true));
                    return CommandResult.Failure($"Сценарий остановлен в блоке «{node.Title}»: {result.Message}", "scenario.execute");
                }

                if (!string.IsNullOrWhiteSpace(result.Message)) { messages.Add(result.Message); }
                if (result.ClientAction != ClientAction.None) { clientAction = result.ClientAction; }
            }

            current = node.Kind == ScenarioNodeKind.Condition && !EvaluateCondition(node) ? node.Otherwise!.Value : node.Next!.Value;
        }
    }

    private bool EvaluateCondition(ScenarioNode node) => node.Condition switch
    {
        ScenarioConditionKind.ApplicationConfigured => settings.Current.Applications.ContainsKey(node.Value),
        ScenarioConditionKind.SiteConfigured => settings.Current.Sites.ContainsKey(node.Value),
        ScenarioConditionKind.AiEnabled => settings.Current.AiEnabled,
        _ => false
    };
}
