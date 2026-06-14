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

public enum AssetKind
{
    Image,
    Audio,
    Other,
}

public sealed class NovelAsset
{
    public required string Id { get; set; }
    public AssetKind Kind { get; set; }
    public required string Path { get; set; }
    public string Folder { get; set; } = string.Empty;
}

public sealed class CharacterPlacement
{
    public required string Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Sprite { get; set; } = string.Empty;
    public CharacterPosition Position { get; set; } = CharacterPosition.Center;
    public bool HasCustomTransform { get; set; }
    public double X { get; set; } = CharacterLayout.StageWidth / 2;
    public double Y { get; set; } = CharacterLayout.DefaultCenterY;
    public double Scale { get; set; } = 1;
    public double Rotation { get; set; }

    public CharacterPlacement Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Sprite = Sprite,
            Position = Position,
            HasCustomTransform = HasCustomTransform,
            X = X,
            Y = Y,
            Scale = Scale,
            Rotation = Rotation,
        };
}

public static class CharacterLayout
{
    public const double StageWidth = 1920;
    public const double StageHeight = 1080;
    public const double BaseWidth = 420;
    public const double BaseHeight = 820;
    public const double DefaultCenterY = 500;

    public static double DefaultCenterX(CharacterPosition position) =>
        position switch
        {
            CharacterPosition.Left => 360,
            CharacterPosition.Right => 1560,
            _ => StageWidth / 2,
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

public sealed class NodeTypeDefaults
{
    public string? Title { get; set; }
    public string? Speaker { get; set; }
    public string? Text { get; set; }
    public string? Background { get; set; }
    public bool? InheritBackground { get; set; }
    public string? Music { get; set; }
    public bool? InheritMusic { get; set; }
    public bool? InheritCharacters { get; set; }
    public List<CharacterPlacement> Characters { get; init; } = [];
    public string? Script { get; set; }
}

public sealed class NodeTypeDefinition
{
    public required string Name { get; init; }
    public required string BaseType { get; set; }
    public NodeTypeDefaults Defaults { get; init; } = new();
}

public sealed class NovelNode
{
    public required string Id { get; init; }
    public NodeKind Kind { get; init; }
    public string TypeName { get; set; } = string.Empty;
    public bool UsesTypeDefaults { get; set; }
    public HashSet<string> PropertyOverrides { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
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
    public int FormatVersion { get; set; } = 4;
    public string Title { get; set; } = "Новая новелла";
    public string SourceCode { get; set; } = string.Empty;
    public List<string> AssetFolders { get; init; } = [];
    public List<NovelAsset> Assets { get; init; } = [];
    public List<NodeTypeDefinition> NodeTypes { get; init; } = [];
    public List<NovelNode> Nodes { get; init; } = [];

    public NovelAsset? FindAsset(string? id) =>
        id is null
            ? null
            : Assets.FirstOrDefault(
                asset => asset.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public string ResolveAssetReference(string reference)
    {
        if (!AssetReference.TryGetId(reference, out var id))
        {
            return reference;
        }
        return FindAsset(id)?.Path ?? string.Empty;
    }

    public int CountAssetReferences(string assetId)
    {
        var reference = AssetReference.Create(assetId);
        return EnumerateAssetValues().Count(
            value => value.Equals(reference, StringComparison.OrdinalIgnoreCase));
    }

    public void ReplaceAssetReference(string assetId, string replacement)
    {
        var reference = AssetReference.Create(assetId);
        foreach (var type in NodeTypes)
        {
            ReplaceAssetValues(type.Defaults, reference, replacement);
        }
        foreach (var node in Nodes)
        {
            node.Background = Replace(node.Background, reference, replacement);
            node.Music = Replace(node.Music, reference, replacement);
            foreach (var character in node.Characters)
            {
                character.Sprite = Replace(character.Sprite, reference, replacement);
            }
            foreach (var output in node.Outputs)
            {
                output.TransitionSound = Replace(
                    output.TransitionSound,
                    reference,
                    replacement);
            }
        }
    }

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
            TypeName = kind == NodeKind.Scene ? "scene" : "dialogue",
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
        if (FormatVersion is < 2 or > 4)
        {
            throw new InvalidDataException($"Версия формата {FormatVersion} не поддерживается.");
        }

        if (Nodes.Count(node => node.Kind == NodeKind.Start) != 1)
        {
            throw new InvalidDataException("Проект должен содержать ровно одну стартовую ноду.");
        }

        var assetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assetFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in AssetFolders)
        {
            var normalized = ProjectAssets.NormalizeFolder(folder);
            if (normalized.Length == 0 || !assetFolders.Add(normalized))
            {
                throw new InvalidDataException(
                    $"Повторяющаяся или некорректная папка ассетов: {folder}");
            }
        }
        foreach (var asset in Assets)
        {
            if (!AssetReference.IsValidId(asset.Id) || !assetIds.Add(asset.Id))
            {
                throw new InvalidDataException(
                    $"Повторяющийся или некорректный id ассета: {asset.Id}");
            }
            if (string.IsNullOrWhiteSpace(asset.Path))
            {
                throw new InvalidDataException(
                    $"У ассета «{asset.Id}» не указан путь.");
            }
            var folder = ProjectAssets.NormalizeFolder(asset.Folder);
            if (folder.Length > 0 && !assetFolders.Contains(folder))
            {
                throw new InvalidDataException(
                    $"Ассет «{asset.Id}» находится в неизвестной папке «{folder}».");
            }
        }

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var outputIds = new HashSet<string>(StringComparer.Ordinal);
        var typeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "start",
            "scene",
            "dialogue",
        };
        var typeDefinitions = new Dictionary<string, NodeTypeDefinition>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var type in NodeTypes)
        {
            if (string.IsNullOrWhiteSpace(type.Name)
                || !typeNames.Add(type.Name))
            {
                throw new InvalidDataException(
                    $"Повторяющийся или пустой тип ноды: {type.Name}");
            }
            typeDefinitions[type.Name] = type;
        }
        foreach (var type in NodeTypes)
        {
            if (!typeNames.Contains(type.BaseType))
            {
                throw new InvalidDataException(
                    $"Тип «{type.Name}» наследуется от неизвестного типа «{type.BaseType}».");
            }
        }

        NodeKind ResolveTypeKind(string typeName, HashSet<string>? chain = null)
        {
            if (typeName.Equals("start", StringComparison.OrdinalIgnoreCase))
            {
                return NodeKind.Start;
            }
            if (typeName.Equals("scene", StringComparison.OrdinalIgnoreCase))
            {
                return NodeKind.Scene;
            }
            if (typeName.Equals("dialogue", StringComparison.OrdinalIgnoreCase))
            {
                return NodeKind.Dialogue;
            }
            if (!typeDefinitions.TryGetValue(typeName, out var definition))
            {
                throw new InvalidDataException($"Неизвестный тип ноды: {typeName}");
            }

            chain ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!chain.Add(typeName))
            {
                throw new InvalidDataException(
                    $"Циклическое наследование типа «{typeName}».");
            }
            var kind = ResolveTypeKind(definition.BaseType, chain);
            chain.Remove(typeName);
            if (kind == NodeKind.Start)
            {
                throw new InvalidDataException(
                    $"Тип «{typeName}» не может наследоваться от start.");
            }
            return kind;
        }

        foreach (var type in NodeTypes)
        {
            _ = ResolveTypeKind(type.Name);
            var characterIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var character in type.Defaults.Characters)
            {
                if (!characterIds.Add(character.Id))
                {
                    throw new InvalidDataException(
                        $"Повторяющийся id персонажа: {character.Id}");
                }
                ValidateCharacterTransform(character);
            }
        }

        foreach (var node in Nodes)
        {
            if (!nodeIds.Add(node.Id))
            {
                throw new InvalidDataException($"Повторяющийся id ноды: {node.Id}");
            }
            if (!string.IsNullOrWhiteSpace(node.TypeName)
                && ResolveTypeKind(node.TypeName) != node.Kind)
            {
                throw new InvalidDataException(
                    $"Тип «{node.TypeName}» несовместим с нодой «{node.Title}».");
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
                ValidateCharacterTransform(character);
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

        foreach (var value in EnumerateTypedAssetValues())
        {
            ValidateAssetReference(value.Value, value.ExpectedKind, value.Owner);
        }
    }

    private static void ValidateCharacterTransform(CharacterPlacement character)
    {
        if (!double.IsFinite(character.X)
            || !double.IsFinite(character.Y)
            || !double.IsFinite(character.Scale)
            || !double.IsFinite(character.Rotation))
        {
            throw new InvalidDataException(
                $"У персонажа «{character.Name}» некорректная трансформация.");
        }
        if (character.Scale is < 0.05 or > 8)
        {
            throw new InvalidDataException(
                $"Масштаб персонажа «{character.Name}» должен быть от 0.05 до 8.");
        }
    }

    public static NovelProject CreateDefault()
    {
        var start = new NovelNode
        {
            Id = "start",
            Kind = NodeKind.Start,
            TypeName = "start",
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
            TypeName = "scene",
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
            TypeName = "dialogue",
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

    private IEnumerable<string> EnumerateAssetValues() =>
        EnumerateTypedAssetValues().Select(value => value.Value);

    private IEnumerable<AssetValue> EnumerateTypedAssetValues()
    {
        foreach (var type in NodeTypes)
        {
            if (type.Defaults.Background is not null)
            {
                yield return new AssetValue(
                    type.Defaults.Background,
                    AssetKind.Image,
                    $"тип {type.Name}, фон");
            }
            if (type.Defaults.Music is not null)
            {
                yield return new AssetValue(
                    type.Defaults.Music,
                    AssetKind.Audio,
                    $"тип {type.Name}, музыка");
            }
            foreach (var character in type.Defaults.Characters)
            {
                yield return new AssetValue(
                    character.Sprite,
                    AssetKind.Image,
                    $"тип {type.Name}, персонаж {character.Name}");
            }
        }

        foreach (var node in Nodes)
        {
            yield return new AssetValue(
                node.Background,
                AssetKind.Image,
                $"нода {node.Title}, фон");
            yield return new AssetValue(
                node.Music,
                AssetKind.Audio,
                $"нода {node.Title}, музыка");
            foreach (var character in node.Characters)
            {
                yield return new AssetValue(
                    character.Sprite,
                    AssetKind.Image,
                    $"нода {node.Title}, персонаж {character.Name}");
            }
            foreach (var output in node.Outputs)
            {
                yield return new AssetValue(
                    output.TransitionSound,
                    AssetKind.Audio,
                    $"переход {output.Label}");
            }
        }
    }

    private void ValidateAssetReference(
        string value,
        AssetKind expectedKind,
        string owner)
    {
        if (!AssetReference.TryGetId(value, out var id))
        {
            return;
        }
        var asset = FindAsset(id)
            ?? throw new InvalidDataException(
                $"Неизвестный ассет «@{id}» используется: {owner}.");
        if (asset.Kind != expectedKind)
        {
            throw new InvalidDataException(
                $"Ассет «@{id}» имеет тип {asset.Kind.ToString().ToLowerInvariant()}, "
                + $"но для «{owner}» требуется {expectedKind.ToString().ToLowerInvariant()}.");
        }
    }

    private static void ReplaceAssetValues(
        NodeTypeDefaults defaults,
        string reference,
        string replacement)
    {
        defaults.Background = ReplaceNullable(
            defaults.Background,
            reference,
            replacement);
        defaults.Music = ReplaceNullable(defaults.Music, reference, replacement);
        foreach (var character in defaults.Characters)
        {
            character.Sprite = Replace(character.Sprite, reference, replacement);
        }
    }

    private static string? ReplaceNullable(
        string? value,
        string reference,
        string replacement) =>
        value?.Equals(reference, StringComparison.OrdinalIgnoreCase) == true
            ? replacement
            : value;

    private static string Replace(
        string? value,
        string reference,
        string replacement) =>
        value?.Equals(reference, StringComparison.OrdinalIgnoreCase) == true
            ? replacement
            : value ?? string.Empty;

    private sealed record AssetValue(
        string Value,
        AssetKind ExpectedKind,
        string Owner);
}

public static class AssetReference
{
    public static string Create(string assetId) => $"@{assetId}";

    public static bool TryGetId(string value, out string id)
    {
        if (value.StartsWith('@')
            && IsValidId(value[1..]))
        {
            id = value[1..];
            return true;
        }
        id = string.Empty;
        return false;
    }

    public static bool IsValidId(string value)
    {
        if (value.Length == 0 || !(value[0] == '_' || char.IsLetter(value[0])))
        {
            return false;
        }
        return value.Skip(1).All(
            character => character is '_' or '-' || char.IsLetterOrDigit(character));
    }

    public static AssetKind GuessKind(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" =>
                AssetKind.Image,
            ".mp3" or ".wav" or ".wma" or ".aac" or ".m4a" or ".ogg" or ".flac" =>
                AssetKind.Audio,
            _ => AssetKind.Other,
        };
    }
}
