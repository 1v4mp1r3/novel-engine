namespace NovelEngine.Core;

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
            NovelScript.Execute(step.Output.Script, State);
            Enter(step.Target);
        }

        return CurrentNode!;
    }

    public IReadOnlyList<NodeOutput> GetAvailableOutputs() =>
        CurrentNode?.Outputs
            .Where(output => NovelScript.Evaluate(output.Condition, State))
            .ToList()
        ?? [];

    public NovelNode Choose(string outputId)
    {
        var node = CurrentNode
            ?? throw new InvalidOperationException("Проигрывание ещё не начато.");
        var output = node.Outputs.FirstOrDefault(candidate => candidate.Id == outputId)
            ?? throw new InvalidOperationException("Выход ноды не найден.");
        if (!NovelScript.Evaluate(output.Condition, State))
        {
            throw new InvalidOperationException("Условие выбранного выхода не выполнено.");
        }

        var target = _project.FindNode(output.TargetNodeId)
            ?? throw new InvalidOperationException("Выход не подключён к ноде.");
        NovelScript.Execute(output.Script, State);
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

        NovelScript.Execute(node.Script, State);
        CurrentNode = node;
        return node;
    }

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
