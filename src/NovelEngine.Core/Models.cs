namespace NovelEngine.Core;

public enum NodeKind
{
    Start,
    Scene,
    Dialogue,
}

public enum CharacterPosition
{
    Left,
    Center,
    Right,
}

public sealed class CharacterPlacement
{
    public required string Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Sprite { get; set; } = string.Empty;
    public CharacterPosition Position { get; set; } = CharacterPosition.Center;

    public CharacterPlacement Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Sprite = Sprite,
            Position = Position,
        };
}

public sealed class NodeOutput
{
    public required string Id { get; init; }
    public string Label { get; set; } = "Дальше";
    public string? TargetNodeId { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
    public string TransitionSound { get; set; } = string.Empty;
    public int FadeDurationMs { get; set; } = 350;
}

public sealed class NovelNode
{
    public required string Id { get; init; }
    public NodeKind Kind { get; init; }
    public string Title { get; set; } = string.Empty;
    public string Speaker { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public bool InheritBackground { get; set; } = true;
    public string Music { get; set; } = string.Empty;
    public bool InheritMusic { get; set; } = true;
    public bool InheritCharacters { get; set; } = true;
    public List<CharacterPlacement> Characters { get; init; } = [];
    public string Script { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public List<NodeOutput> Outputs { get; init; } = [];
}

public sealed class NovelProject
{
    public int FormatVersion { get; init; } = 2;
    public string Title { get; set; } = "Новая новелла";
    public List<NovelNode> Nodes { get; init; } = [];

    public NovelNode? FindNode(string? nodeId) =>
        nodeId is null
            ? null
            : Nodes.FirstOrDefault(node => node.Id == nodeId);

    public NodeOutput? FindOutput(string nodeId, string outputId) =>
        FindNode(nodeId)?.Outputs.FirstOrDefault(output => output.Id == outputId);

    public NovelNode AddNode(NodeKind kind, float x, float y)
    {
        if (kind == NodeKind.Start)
        {
            throw new InvalidOperationException("В проекте может быть только одна стартовая нода.");
        }

        var index = Nodes.Count(node => node.Kind == kind) + 1;
        var node = new NovelNode
        {
            Id = CreateId("node"),
            Kind = kind,
            Title = kind == NodeKind.Scene ? $"Сцена {index}" : $"Диалог {index}",
            X = x,
            Y = y,
            InheritBackground = kind == NodeKind.Dialogue,
            InheritMusic = true,
            InheritCharacters = true,
        };

        if (kind == NodeKind.Scene)
        {
            node.Outputs.Add(CreateOutput("Дальше"));
        }
        else
        {
            node.Outputs.Add(CreateOutput("Вариант 1"));
            node.Outputs.Add(CreateOutput("Вариант 2"));
        }

        Nodes.Add(node);
        return node;
    }

    public NodeOutput AddChoice(string nodeId, string label = "Новый вариант")
    {
        var node = FindNode(nodeId)
            ?? throw new InvalidOperationException("Нода не найдена.");
        if (node.Kind != NodeKind.Dialogue)
        {
            throw new InvalidOperationException("Варианты ответа доступны только диалоговой ноде.");
        }

        var output = CreateOutput(label);
        node.Outputs.Add(output);
        return output;
    }

    public void Connect(string sourceNodeId, string outputId, string targetNodeId)
    {
        var source = FindNode(sourceNodeId)
            ?? throw new InvalidOperationException("Исходная нода не найдена.");
        var target = FindNode(targetNodeId)
            ?? throw new InvalidOperationException("Целевая нода не найдена.");
        var output = source.Outputs.FirstOrDefault(candidate => candidate.Id == outputId)
            ?? throw new InvalidOperationException("Выход ноды не найден.");

        if (source.Id == target.Id)
        {
            throw new InvalidOperationException("Ноду нельзя соединить саму с собой.");
        }

        output.TargetNodeId = target.Id;
    }

    public bool RemoveOutput(string nodeId, string outputId)
    {
        var node = FindNode(nodeId);
        if (node is null || node.Kind != NodeKind.Dialogue)
        {
            return false;
        }

        var output = node.Outputs.FirstOrDefault(candidate => candidate.Id == outputId);
        return output is not null && node.Outputs.Remove(output);
    }

    public bool RemoveNode(string nodeId)
    {
        var node = FindNode(nodeId);
        if (node is null || node.Kind == NodeKind.Start)
        {
            return false;
        }

        Nodes.Remove(node);
        foreach (var output in Nodes.SelectMany(candidate => candidate.Outputs))
        {
            if (output.TargetNodeId == nodeId)
            {
                output.TargetNodeId = null;
            }
        }

        return true;
    }

    public void Validate()
    {
        if (FormatVersion != 2)
        {
            throw new InvalidDataException($"Версия формата {FormatVersion} не поддерживается.");
        }

        if (Nodes.Count(node => node.Kind == NodeKind.Start) != 1)
        {
            throw new InvalidDataException("Проект должен содержать ровно одну стартовую ноду.");
        }

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var outputIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            if (!nodeIds.Add(node.Id))
            {
                throw new InvalidDataException($"Повторяющийся id ноды: {node.Id}");
            }

            if (node.Kind != NodeKind.Dialogue && node.Outputs.Count != 1)
            {
                throw new InvalidDataException(
                    $"Нода «{node.Title}» должна иметь ровно один выход.");
            }

            foreach (var output in node.Outputs)
            {
                if (!outputIds.Add(output.Id))
                {
                    throw new InvalidDataException($"Повторяющийся id выхода: {output.Id}");
                }
                if (output.FadeDurationMs is < 0 or > 10_000)
                {
                    throw new InvalidDataException(
                        $"Длительность перехода «{output.Label}» должна быть от 0 до 10000 мс.");
                }
            }

            var characterIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var character in node.Characters)
            {
                if (!characterIds.Add(character.Id))
                {
                    throw new InvalidDataException(
                        $"Повторяющийся id персонажа: {character.Id}");
                }
            }
        }

        foreach (var output in Nodes.SelectMany(node => node.Outputs))
        {
            if (output.TargetNodeId is not null && !nodeIds.Contains(output.TargetNodeId))
            {
                throw new InvalidDataException(
                    $"Выход «{output.Label}» ссылается на отсутствующую ноду.");
            }
        }
    }

    public static NovelProject CreateDefault()
    {
        var start = new NovelNode
        {
            Id = "start",
            Kind = NodeKind.Start,
            Title = "Начало",
            X = 20,
            Y = 180,
            InheritBackground = false,
            InheritMusic = false,
            InheritCharacters = false,
            Outputs = [CreateOutput("Дальше")],
        };
        var scene = new NovelNode
        {
            Id = "scene-1",
            Kind = NodeKind.Scene,
            Title = "Первая сцена",
            Text = "Здесь начинается история.",
            X = 255,
            Y = 120,
            InheritBackground = false,
            InheritMusic = false,
            InheritCharacters = false,
            Outputs = [CreateOutput("Дальше")],
        };
        var dialogue = new NovelNode
        {
            Id = "dialogue-1",
            Kind = NodeKind.Dialogue,
            Title = "Первый выбор",
            Speaker = "Герой",
            Text = "Что сделать дальше?",
            X = 490,
            Y = 120,
            Outputs =
            [
                CreateOutput("Продолжить"),
                CreateOutput("Уйти"),
            ],
        };

        start.Outputs[0].TargetNodeId = scene.Id;
        scene.Outputs[0].TargetNodeId = dialogue.Id;

        return new NovelProject
        {
            Nodes = [start, scene, dialogue],
        };
    }

    private static NodeOutput CreateOutput(string label) =>
        new()
        {
            Id = CreateId("out"),
            Label = label,
        };

    private static string CreateId(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}";
}
