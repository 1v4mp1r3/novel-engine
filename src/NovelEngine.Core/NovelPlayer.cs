using System.Text.Json;

namespace NovelEngine.Core;

public sealed class RuntimeSaveState
{
    public required string NodeId { get; init; }
    public Dictionary<string, JsonElement> Variables { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public string CurrentBackground { get; set; } = string.Empty;
    public string CurrentMusic { get; set; } = string.Empty;
    public List<CharacterPlacement> CurrentCharacters { get; init; } = [];
}

public sealed class NovelPlayer
{
    private readonly NovelProject _project;

    public NovelPlayer(NovelProject project)
    {
        project.Validate();
        _project = project;
        State = new ScriptState();
    }

    public ScriptState State { get; }
    public NovelNode? CurrentNode { get; private set; }

    public NovelNode Start()
    {
        State.Reset();
        var start = _project.Nodes.Single(node => node.Kind == NodeKind.Start);
        return Enter(start);
    }

    public NovelNode StartAt(string nodeId)
    {
        State.Reset();
        var target = _project.FindNode(nodeId)
            ?? throw new InvalidOperationException("Нода для предпросмотра не найдена.");
        var start = _project.Nodes.Single(node => node.Kind == NodeKind.Start);
        if (target.Id == start.Id)
        {
            return Enter(start);
        }

        var path = FindPath(start.Id, target.Id);
        if (path is null)
        {
            return Enter(target);
        }

        Enter(start);
        foreach (var step in path)
        {
            VisualScriptCompiler.Execute(
                step.Output.Script,
                step.Output.ScriptBlocks,
                State);
            Enter(step.Target);
        }

        return CurrentNode!;
    }

    public IReadOnlyList<NodeOutput> GetAvailableOutputs() =>
        CurrentNode?.Outputs
            .Where(output => VisualConditionCompiler.Evaluate(output.Condition, State))
            .ToList()
        ?? [];

    public RuntimeSaveState CreateSaveState()
    {
        var node = CurrentNode
            ?? throw new InvalidOperationException("Проигрывание ещё не начато.");
        return new RuntimeSaveState
        {
            NodeId = node.Id,
            CurrentBackground = State.CurrentBackground,
            CurrentMusic = State.CurrentMusic,
            CurrentCharacters =
            [
                .. State.CurrentCharacters.Select(character => character.Clone()),
            ],
            Variables = State.Variables.ToDictionary(
                pair => pair.Key,
                pair => JsonSerializer.SerializeToElement(pair.Value),
                StringComparer.OrdinalIgnoreCase),
        };
    }

    public NovelNode Restore(RuntimeSaveState saveState)
    {
        var node = _project.FindNode(saveState.NodeId)
            ?? throw new InvalidOperationException("Сохранённая нода не найдена.");
        State.Reset();
        State.CurrentBackground = saveState.CurrentBackground;
        State.CurrentMusic = saveState.CurrentMusic;
        State.CurrentCharacters.AddRange(
            saveState.CurrentCharacters.Select(character => character.Clone()));
        foreach (var variable in saveState.Variables)
        {
            State.Variables[variable.Key] = RestoreVariable(variable.Value);
        }
        CurrentNode = node;
        return node;
    }

    public NovelNode Choose(string outputId)
    {
        var node = CurrentNode
            ?? throw new InvalidOperationException("Проигрывание ещё не начато.");
        var output = node.Outputs.FirstOrDefault(candidate => candidate.Id == outputId)
            ?? throw new InvalidOperationException("Выход ноды не найден.");
        if (!VisualConditionCompiler.Evaluate(output.Condition, State))
        {
            throw new InvalidOperationException("Условие выбранного выхода не выполнено.");
        }

        var target = _project.FindNode(output.TargetNodeId)
            ?? throw new InvalidOperationException("Выход не подключён к ноде.");
        VisualScriptCompiler.Execute(output.Script, output.ScriptBlocks, State);
        return Enter(target);
    }

    private NovelNode Enter(NovelNode node)
    {
        if (!node.InheritBackground)
        {
            State.CurrentBackground = node.Background;
        }

        if (!node.InheritMusic)
        {
            State.CurrentMusic = node.Music;
        }

        if (!node.InheritCharacters)
        {
            State.CurrentCharacters.Clear();
            State.CurrentCharacters.AddRange(node.Characters.Select(character => character.Clone()));
        }

        VisualScriptCompiler.Execute(node.Script, node.ScriptBlocks, State);
        CurrentNode = node;
        return node;
    }

    private static object? RestoreVariable(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var integer)
                ? integer
                : element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            _ => element.GetRawText(),
        };

    private List<PathStep>? FindPath(string startNodeId, string targetNodeId)
    {
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { startNodeId };
        var previous = new Dictionary<string, PathStep>(StringComparer.Ordinal);
        queue.Enqueue(startNodeId);

        while (queue.Count > 0)
        {
            var node = _project.FindNode(queue.Dequeue());
            if (node is null)
            {
                continue;
            }

            foreach (var output in node.Outputs)
            {
                var target = _project.FindNode(output.TargetNodeId);
                if (target is null || !visited.Add(target.Id))
                {
                    continue;
                }

                previous[target.Id] = new PathStep(node.Id, output, target);
                if (target.Id == targetNodeId)
                {
                    return BuildPath(previous, startNodeId, targetNodeId);
                }

                queue.Enqueue(target.Id);
            }
        }

        return null;
    }

    private static List<PathStep> BuildPath(
        Dictionary<string, PathStep> previous,
        string startNodeId,
        string targetNodeId)
    {
        var path = new List<PathStep>();
        var currentId = targetNodeId;
        while (currentId != startNodeId)
        {
            var step = previous[currentId];
            path.Add(step);
            currentId = step.SourceNodeId;
        }

        path.Reverse();
        return path;
    }

    private sealed record PathStep(
        string SourceNodeId,
        NodeOutput Output,
        NovelNode Target);
}
