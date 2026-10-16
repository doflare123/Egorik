namespace Egorik.Core.Scenarios;

public sealed record ScenarioStackBlock(ScenarioNode Node,
    IReadOnlyList<ScenarioStackBlock> Then, IReadOnlyList<ScenarioStackBlock> Else);

public sealed record ScenarioProgram(ScenarioNode Start, ScenarioNode End, IReadOnlyList<ScenarioStackBlock> Body);

// Structured editing changes links atomically. The persisted/executable format
// remains the validated graph; existing actions, arguments and IDs are preserved.
public static class ScenarioStructure
{
    public static bool TryRead(ScenarioFlow flow, out ScenarioProgram? program, out string? error)
    {
        program = null;
        error = null;
        try
        {
            if (flow.Version != 1 || flow.Nodes is null || flow.Nodes.Count is < 2 or > ScenarioFlowExecutor.MaximumNodes ||
                flow.Nodes.Any(node => node is null) || flow.Nodes.Select(node => node.Id).Distinct().Count() != flow.Nodes.Count)
            {
                throw new InvalidOperationException("Эта версия или структура сценария не поддерживается блочным редактором.");
            }

            var map = flow.Nodes.ToDictionary(node => node.Id);
            var ends = flow.Nodes.Where(node => node.Kind == ScenarioNodeKind.End).ToArray();
            if (!map.TryGetValue(flow.EntryNodeId, out var start) || start.Kind != ScenarioNodeKind.Start ||
                start.Next is null || ends.Length != 1)
            {
                throw new InvalidOperationException("Для конструктора нужны один старт и одно завершение.");
            }

            var end = ends[0];
            if (end.Next is not null || end.Otherwise is not null || start.Otherwise is not null)
            {
                throw new InvalidOperationException("Некорректные переходы старта или завершения.");
            }

            var visited = new HashSet<Guid> { start.Id, end.Id };
            Dictionary<Guid, int> Reachable(Guid from)
            {
                var distances = new Dictionary<Guid, int> { [from] = 0 };
                var queue = new Queue<Guid>();
                queue.Enqueue(from);
                while (queue.TryDequeue(out var id))
                {
                    if (!map.TryGetValue(id, out var node)) { throw new InvalidOperationException("Переход ведёт к удалённому блоку."); }
                    foreach (var next in new[] { node.Next, node.Otherwise })
                    {
                        if (next is not null && distances.TryAdd(next.Value, distances[id] + 1)) { queue.Enqueue(next.Value); }
                    }
                }

                return distances;
            }

            IReadOnlyList<ScenarioStackBlock> ReadStack(Guid current, Guid stop)
            {
                var blocks = new List<ScenarioStackBlock>();
                while (current != stop)
                {
                    if (!visited.Add(current) || !map.TryGetValue(current, out var node) || node.Next is null ||
                        !Enum.IsDefined(node.Kind) || node.Kind is ScenarioNodeKind.Start or ScenarioNodeKind.End)
                    {
                        throw new InvalidOperationException("Схема содержит цикл или пересекающиеся ветки. Исходный сценарий сохранён без изменений.");
                    }

                    if (node.Kind == ScenarioNodeKind.Condition)
                    {
                        if (node.Otherwise is null) { throw new InvalidOperationException("У условия отсутствует ветка «иначе»."); }
                        var yes = Reachable(node.Next.Value);
                        var no = Reachable(node.Otherwise.Value);
                        var joins = yes.Keys.Where(no.ContainsKey).OrderBy(id => Math.Max(yes[id], no[id])).ThenBy(id => yes[id] + no[id]).ToArray();
                        if (joins.Length == 0) { throw new InvalidOperationException("Ветки условия должны возвращаться в общий стек."); }
                        var join = joins[0];
                        blocks.Add(new(node, ReadStack(node.Next.Value, join), ReadStack(node.Otherwise.Value, join)));
                        current = join;
                    }
                    else
                    {
                        if (node.Otherwise is not null) { throw new InvalidOperationException("Лишняя ветка у обычного блока."); }
                        blocks.Add(new(node, [], []));
                        current = node.Next.Value;
                    }
                }

                return blocks;
            }

            var body = ReadStack(start.Next.Value, end.Id);
            if (visited.Count != map.Count) { throw new InvalidOperationException("В сценарии есть неподключённые блоки."); }
            program = new(start, end, body);
            return true;
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public static ScenarioFlow Compile(ScenarioProgram program)
    {
        var nodes = new List<ScenarioNode>();
        Guid Write(IReadOnlyList<ScenarioStackBlock> stack, Guid next)
        {
            for (var index = stack.Count - 1; index >= 0; index--)
            {
                var block = stack[index];
                var node = block.Node with
                {
                    Next = block.Node.Kind == ScenarioNodeKind.Condition ? Write(block.Then, next) : next,
                    Otherwise = block.Node.Kind == ScenarioNodeKind.Condition ? Write(block.Else, next) : null
                };
                nodes.Add(node);
                next = node.Id;
            }

            return next;
        }

        var first = Write(program.Body, program.End.Id);
        nodes.Add(program.Start with { Next = first, Otherwise = null });
        nodes.Add(program.End with { Next = null, Otherwise = null });
        // Stable execution order is useful for list-based consumers of the JSON.
        var map = nodes.ToDictionary(node => node.Id);
        var ordered = new List<ScenarioNode>();
        var seen = new HashSet<Guid>();
        void Visit(Guid id)
        {
            if (id == program.End.Id || !seen.Add(id)) { return; }
            var node = map[id];
            ordered.Add(node);
            if (node.Next is not null) { Visit(node.Next.Value); }
            if (node.Otherwise is not null) { Visit(node.Otherwise.Value); }
        }

        Visit(program.Start.Id);
        ordered.Add(map[program.End.Id]);
        return new(1, program.Start.Id, ordered);
    }

    public static bool TryEdit(ScenarioFlow flow, Guid? movingId, ScenarioNode? newNode,
        Guid? ownerId, bool otherwise, int index, bool remove,
        out ScenarioFlow edited, out string? error)
    {
        edited = flow;
        if (!TryRead(flow, out var program, out error)) { return false; }
        var found = movingId is null ? null : Flatten(program!.Body).FirstOrDefault(block => block.Node.Id == movingId);
        if (movingId is not null && found is null) { error = "Старт и завершение нельзя перемещать или удалять."; return false; }
        if (ownerId is not null && Flatten(program!.Body).FirstOrDefault(block => block.Node.Id == ownerId)?.Node.Kind != ScenarioNodeKind.Condition)
        { error = "Вложить блок можно только в условие."; return false; }
        if (found is not null && ownerId is not null && Flatten([found]).Any(block => block.Node.Id == ownerId))
        { error = "Нельзя вложить блок в самого себя."; return false; }

        var body = program!.Body;
        if (found is not null)
        {
            var source = Stack(body, ownerId, otherwise);
            var oldIndex = source.ToList().FindIndex(block => block.Node.Id == movingId);
            if (oldIndex >= 0 && oldIndex < index) { index--; }
            body = Remove(body, found.Node.Id);
        }

        if (!remove)
        {
            var block = found ?? (newNode is null ? null : new ScenarioStackBlock(newNode, [], []));
            if (block is null || block.Node.Kind is ScenarioNodeKind.Start or ScenarioNodeKind.End ||
                (found is null && (flow.Nodes.Count >= ScenarioFlowExecutor.MaximumNodes || flow.Nodes.Any(node => node.Id == block.Node.Id))))
            { error = "Не удалось добавить блок: недопустимый тип, ID или превышен лимит."; return false; }
            body = Insert(body, ownerId, otherwise, Math.Clamp(index, 0, Stack(body, ownerId, otherwise).Count), block);
        }

        edited = Compile(program with { Body = body });
        error = null;
        return true;
    }

    public static IEnumerable<ScenarioStackBlock> Flatten(IReadOnlyList<ScenarioStackBlock> blocks) =>
        blocks.SelectMany(block => new[] { block }.Concat(Flatten(block.Then)).Concat(Flatten(block.Else)));

    private static IReadOnlyList<ScenarioStackBlock> Stack(IReadOnlyList<ScenarioStackBlock> body, Guid? owner, bool otherwise)
    {
        if (owner is null) { return body; }
        var block = Flatten(body).First(block => block.Node.Id == owner);
        return otherwise ? block.Else : block.Then;
    }

    private static IReadOnlyList<ScenarioStackBlock> Remove(IReadOnlyList<ScenarioStackBlock> body, Guid id) => body
        .Where(block => block.Node.Id != id).Select(block => block with { Then = Remove(block.Then, id), Else = Remove(block.Else, id) }).ToArray();

    private static IReadOnlyList<ScenarioStackBlock> Insert(IReadOnlyList<ScenarioStackBlock> body, Guid? owner, bool otherwise,
        int index, ScenarioStackBlock added)
    {
        if (owner is null)
        {
            var result = body.ToList();
            result.Insert(index, added);
            return result;
        }

        return body.Select(block => block.Node.Id == owner
            ? otherwise ? block with { Else = Insert(block.Else, null, false, index, added) } : block with { Then = Insert(block.Then, null, false, index, added) }
            : block with { Then = Insert(block.Then, owner, otherwise, index, added), Else = Insert(block.Else, owner, otherwise, index, added) }).ToArray();
    }
}
