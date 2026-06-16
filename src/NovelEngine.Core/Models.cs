namespace NovelEngine.Core;

public enum NodeKind
{
    Start,
    Scene,
    Dialogue,
}

public enum NodeTemplateKind
{
    EstablishingScene,
    CharacterLine,
    ChoiceBranch,
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

public enum MainMenuElementKind
{
    Label,
    Button,
    ImageLabel,
    ImageButton,
}

public enum MainMenuAction
{
    None,
    NewGame,
    Continue,
    LoadGame,
    Settings,
    Exit,
}

public sealed class MainMenuElement
{
    public required string Id { get; init; }
    public MainMenuElementKind Kind { get; set; }
    public MainMenuAction Action { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 58;
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 26;
    public string Foreground { get; set; } = "#F6F8FB";
    public string Background { get; set; } = "#D025354A";
    public string Border { get; set; } = "#50657F";
    public string CustomStyleCode { get; set; } = string.Empty;

    public MainMenuElement Clone() =>
        new()
        {
            Id = Id,
            Kind = Kind,
            Action = Action,
            Text = Text,
            Image = Image,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            FontFamily = FontFamily,
            FontSize = FontSize,
            Foreground = Foreground,
            Background = Background,
            Border = Border,
            CustomStyleCode = CustomStyleCode,
        };
}

public sealed class MainMenuDesign
{
    public string Background { get; set; } = string.Empty;
    public List<MainMenuElement> Elements { get; init; } =
    [
        new()
        {
            Id = "title",
            Kind = MainMenuElementKind.Label,
            Text = "Новая новелла",
            X = 120,
            Y = 100,
            Width = 760,
            Height = 72,
            FontSize = 44,
            Background = "Transparent",
        },
        new()
        {
            Id = "new-game",
            Kind = MainMenuElementKind.Button,
            Action = MainMenuAction.NewGame,
            Text = "Новая игра",
            X = 140,
            Y = 260,
        },
        new()
        {
            Id = "load-game",
            Kind = MainMenuElementKind.Button,
            Action = MainMenuAction.LoadGame,
            Text = "Загрузить",
            X = 140,
            Y = 335,
        },
        new()
        {
            Id = "exit",
            Kind = MainMenuElementKind.Button,
            Action = MainMenuAction.Exit,
            Text = "Выход",
            X = 140,
            Y = 410,
        },
    ];

    public MainMenuDesign Clone()
    {
        var clone = new MainMenuDesign
        {
            Background = Background,
        };
        clone.Elements.Clear();
        clone.Elements.AddRange(Elements.Select(element => element.Clone()));
        return clone;
    }
}

public sealed class NovelAsset
{
    public required string Id { get; set; }
    public AssetKind Kind { get; set; }
    public required string Path { get; set; }
    public string Folder { get; set; } = string.Empty;
}

public sealed record AssetUsage(
    string Location,
    AssetKind ExpectedKind,
    string Reference,
    string? NodeId = null);

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
    public string VoiceSound { get; set; } = string.Empty;
    public List<string> VoiceSounds { get; init; } = [];
    public double VoicePitch { get; set; } = 1;
    public int VoiceEveryNthCharacter { get; set; } = 1;

    public CharacterPlacement Clone() => CloneWithId(Id);

    public CharacterPlacement CloneWithId(string id) =>
        new()
        {
            Id = id,
            Name = Name,
            Sprite = Sprite,
            Position = Position,
            HasCustomTransform = HasCustomTransform,
            X = X,
            Y = Y,
            Scale = Scale,
            Rotation = Rotation,
            VoiceSound = VoiceSound,
            VoiceSounds = [.. VoiceSounds],
            VoicePitch = VoicePitch,
            VoiceEveryNthCharacter = VoiceEveryNthCharacter,
        };

    public List<string> GetVoiceSounds()
    {
        var values = VoiceSounds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(VoiceSound)
            && !values.Contains(VoiceSound, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(VoiceSound);
        }
        return values;
    }

    public void SetVoiceSounds(IEnumerable<string> sounds)
    {
        var values = sounds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        VoiceSounds.Clear();
        VoiceSounds.AddRange(values);
        VoiceSound = VoiceSounds.FirstOrDefault() ?? string.Empty;
    }
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
    public int FormatVersion { get; set; } = 5;
    public string Title { get; set; } = "Новая новелла";
    public string SourceCode { get; set; } = string.Empty;
    public MainMenuDesign MainMenu { get; init; } = new();
    public List<string> AssetFolders { get; init; } = [];
    public List<NovelAsset> Assets { get; init; } = [];
    public List<CharacterPlacement> Characters { get; init; } = [];
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

    public CharacterPlacement? FindCharacter(string? characterId) =>
        characterId is null
            ? null
            : Characters.FirstOrDefault(
                character => character.Id.Equals(
                    characterId,
                    StringComparison.Ordinal));

    public int CountAssetReferences(string assetId)
    {
        var reference = AssetReference.Create(assetId);
        return EnumerateAssetValues().Count(
            value => value.Equals(reference, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<AssetUsage> FindAssetUsages(string assetId)
    {
        var reference = AssetReference.Create(assetId);
        return EnumerateTypedAssetValues()
            .Where(value => value.Value.Equals(
                reference,
                StringComparison.OrdinalIgnoreCase))
            .Select(value => new AssetUsage(
                value.Owner,
                value.ExpectedKind,
                value.Value,
                value.NodeId))
            .ToList();
    }

    public void ReplaceAssetReference(string assetId, string replacement)
    {
        var reference = AssetReference.Create(assetId);
        foreach (var character in Characters)
        {
            ReplaceCharacterAssetValues(character, reference, replacement);
        }
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
                ReplaceCharacterAssetValues(character, reference, replacement);
            }
            foreach (var output in node.Outputs)
            {
                output.TransitionSound = Replace(
                    output.TransitionSound,
                    reference,
                    replacement);
            }
        }
        MainMenu.Background = Replace(MainMenu.Background, reference, replacement);
        foreach (var element in MainMenu.Elements)
        {
            element.Image = Replace(element.Image, reference, replacement);
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

    public NovelNode AddNodeTemplate(NodeTemplateKind template, float x, float y)
    {
        var node = AddNode(GetTemplateNodeKind(template), x, y);
        ApplyNodeTemplate(node, template);
        return node;
    }

    public NovelNode AddConnectedNode(
        string sourceNodeId,
        NodeKind kind,
        float x,
        float y)
    {
        if (kind == NodeKind.Start)
        {
            throw new InvalidOperationException("Связанная стартовая нода недоступна.");
        }

        var source = FindNode(sourceNodeId)
            ?? throw new InvalidOperationException("Исходная нода не найдена.");
        var output = source.Outputs.FirstOrDefault(output => output.TargetNodeId is null);
        if (output is null)
        {
            if (source.Kind != NodeKind.Dialogue)
            {
                throw new InvalidOperationException("У ноды нет свободного выхода.");
            }

            output = AddChoice(
                source.Id,
                kind == NodeKind.Dialogue ? "Новый диалог" : "Новая сцена");
        }

        var node = AddNode(kind, x, y);
        output.TargetNodeId = node.Id;
        return node;
    }

    public NovelNode AddConnectedNodeTemplate(
        string sourceNodeId,
        NodeTemplateKind template,
        float x,
        float y)
    {
        var node = AddConnectedNode(
            sourceNodeId,
            GetTemplateNodeKind(template),
            x,
            y);
        ApplyNodeTemplate(node, template);
        return node;
    }

    public NovelNode DuplicateNode(string nodeId, float offsetX = 48, float offsetY = 48)
    {
        var source = FindNode(nodeId)
            ?? throw new InvalidOperationException("Нода не найдена.");
        if (source.Kind == NodeKind.Start)
        {
            throw new InvalidOperationException("Стартовую ноду нельзя дублировать.");
        }

        var duplicate = new NovelNode
        {
            Id = CreateId("node"),
            Kind = source.Kind,
            TypeName = source.TypeName,
            UsesTypeDefaults = source.UsesTypeDefaults,
            Title = CreateDuplicateTitle(source.Title),
            Speaker = source.Speaker,
            Text = source.Text,
            Background = source.Background,
            InheritBackground = source.InheritBackground,
            Music = source.Music,
            InheritMusic = source.InheritMusic,
            InheritCharacters = source.InheritCharacters,
            Script = source.Script,
            X = source.X + offsetX,
            Y = source.Y + offsetY,
        };
        duplicate.PropertyOverrides.UnionWith(source.PropertyOverrides);
        duplicate.Characters.AddRange(
            source.Characters.Select(character => character.CloneWithId(CreateId("char"))));
        duplicate.Outputs.AddRange(
            source.Outputs.Select(output => new NodeOutput
            {
                Id = CreateId("out"),
                Label = output.Label,
                Condition = output.Condition,
                Script = output.Script,
                TransitionSound = output.TransitionSound,
                FadeDurationMs = output.FadeDurationMs,
            }));

        Nodes.Add(duplicate);
        return duplicate;
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

    public NodeOutput DuplicateOutput(string nodeId, string outputId)
    {
        var node = FindNode(nodeId)
            ?? throw new InvalidOperationException("Нода не найдена.");
        if (node.Kind != NodeKind.Dialogue)
        {
            throw new InvalidOperationException("Варианты ответа доступны только диалоговой ноде.");
        }

        var source = node.Outputs.FirstOrDefault(output => output.Id == outputId)
            ?? throw new InvalidOperationException("Вариант ответа не найден.");

        var duplicate = new NodeOutput
        {
            Id = CreateId("out"),
            Label = CreateDuplicateOutputLabel(node, source.Label),
            Condition = source.Condition,
            Script = source.Script,
            TransitionSound = source.TransitionSound,
            FadeDurationMs = source.FadeDurationMs,
        };

        var sourceIndex = node.Outputs.IndexOf(source);
        node.Outputs.Insert(sourceIndex + 1, duplicate);
        return duplicate;
    }

    public bool MoveOutput(string nodeId, string outputId, int direction)
    {
        if (direction == 0)
        {
            return false;
        }

        var node = FindNode(nodeId);
        if (node is null || node.Kind != NodeKind.Dialogue)
        {
            return false;
        }

        var currentIndex = node.Outputs.FindIndex(output => output.Id == outputId);
        if (currentIndex < 0)
        {
            return false;
        }

        var targetIndex = currentIndex + Math.Sign(direction);
        if (targetIndex < 0 || targetIndex >= node.Outputs.Count)
        {
            return false;
        }

        (node.Outputs[currentIndex], node.Outputs[targetIndex]) =
            (node.Outputs[targetIndex], node.Outputs[currentIndex]);
        return true;
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
        if (FormatVersion is < 2 or > 5)
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
                ValidateCharacterVoice(character);
            }
        }

        var libraryCharacterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var character in Characters)
        {
            if (string.IsNullOrWhiteSpace(character.Id)
                || !libraryCharacterIds.Add(character.Id))
            {
                throw new InvalidDataException(
                    $"Повторяющийся или пустой id персонажа библиотеки: {character.Id}");
            }
            ValidateCharacterTransform(character);
            ValidateCharacterVoice(character);
        }

        var mainMenuIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in MainMenu.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.Id)
                || !mainMenuIds.Add(element.Id))
            {
                throw new InvalidDataException(
                    $"Повторяющийся или пустой id элемента главного меню: {element.Id}");
            }
            ValidateMainMenuElement(element);
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
                ValidateCharacterVoice(character);
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

    private static void ValidateCharacterVoice(CharacterPlacement character)
    {
        if (!double.IsFinite(character.VoicePitch)
            || character.VoicePitch is < 0.25 or > 4)
        {
            throw new InvalidDataException(
                $"Высота голоса персонажа «{character.Name}» должна быть от 0.25 до 4.");
        }
        if (character.VoiceEveryNthCharacter is < 1 or > 12)
        {
            throw new InvalidDataException(
                $"Частота голоса персонажа «{character.Name}» должна быть от 1 до 12.");
        }
    }

    private static void ValidateMainMenuElement(MainMenuElement element)
    {
        if (!double.IsFinite(element.X)
            || !double.IsFinite(element.Y)
            || !double.IsFinite(element.Width)
            || !double.IsFinite(element.Height)
            || !double.IsFinite(element.FontSize))
        {
            throw new InvalidDataException(
                $"У элемента главного меню «{element.Id}» некорректная геометрия.");
        }
        if (element.Width is < 8 or > 4000 || element.Height is < 8 or > 4000)
        {
            throw new InvalidDataException(
                $"Размер элемента главного меню «{element.Id}» должен быть от 8 до 4000.");
        }
        if (element.FontSize is < 6 or > 180)
        {
            throw new InvalidDataException(
                $"Размер шрифта элемента главного меню «{element.Id}» должен быть от 6 до 180.");
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

    private static NodeKind GetTemplateNodeKind(NodeTemplateKind template) =>
        template == NodeTemplateKind.EstablishingScene
            ? NodeKind.Scene
            : NodeKind.Dialogue;

    private static void ApplyNodeTemplate(NovelNode node, NodeTemplateKind template)
    {
        node.Outputs.Clear();
        node.UsesTypeDefaults = false;
        node.PropertyOverrides.Clear();
        node.Characters.Clear();
        node.Background = string.Empty;
        node.Music = string.Empty;
        node.Script = string.Empty;

        switch (template)
        {
            case NodeTemplateKind.EstablishingScene:
                node.TypeName = "scene";
                node.Title = "Сцена с фоном";
                node.Speaker = string.Empty;
                node.Text = "Описание сцены.";
                node.InheritBackground = false;
                node.InheritMusic = true;
                node.InheritCharacters = true;
                node.Outputs.Add(CreateOutput("Дальше"));
                break;

            case NodeTemplateKind.CharacterLine:
                node.TypeName = "dialogue";
                node.Title = "Реплика персонажа";
                node.Speaker = "Герой";
                node.Text = "Текст реплики.";
                node.InheritBackground = true;
                node.InheritMusic = true;
                node.InheritCharacters = true;
                node.Outputs.Add(CreateOutput("Дальше"));
                break;

            case NodeTemplateKind.ChoiceBranch:
                node.TypeName = "dialogue";
                node.Title = "Выбор";
                node.Speaker = "Герой";
                node.Text = "Что сделать дальше?";
                node.InheritBackground = true;
                node.InheritMusic = true;
                node.InheritCharacters = true;
                node.Outputs.Add(CreateOutput("Вариант 1"));
                node.Outputs.Add(CreateOutput("Вариант 2"));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(template), template, null);
        }
    }

    private static string CreateId(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}";

    private string CreateDuplicateTitle(string title)
    {
        var baseTitle = string.IsNullOrWhiteSpace(title)
            ? "Копия ноды"
            : $"{title} копия";
        var existing = Nodes
            .Select(node => node.Title)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!existing.Contains(baseTitle))
        {
            return baseTitle;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{baseTitle} {index}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string CreateDuplicateOutputLabel(NovelNode node, string label)
    {
        var baseLabel = string.IsNullOrWhiteSpace(label)
            ? "Копия варианта"
            : $"{label} копия";
        var existing = node.Outputs
            .Select(output => output.Label)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!existing.Contains(baseLabel))
        {
            return baseLabel;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{baseLabel} {index}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private IEnumerable<string> EnumerateAssetValues() =>
        EnumerateTypedAssetValues().Select(value => value.Value);

    private IEnumerable<AssetValue> EnumerateTypedAssetValues()
    {
        foreach (var character in Characters)
        {
            yield return new AssetValue(
                character.Sprite,
                AssetKind.Image,
                $"библиотека персонажей, персонаж {character.Name}");
            foreach (var voice in CharacterVoiceValues(character))
            {
                yield return new AssetValue(
                    voice,
                    AssetKind.Audio,
                    $"библиотека персонажей, голос персонажа {character.Name}");
            }
        }

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
                foreach (var voice in CharacterVoiceValues(character))
                {
                    yield return new AssetValue(
                        voice,
                        AssetKind.Audio,
                        $"тип {type.Name}, голос персонажа {character.Name}");
                }
            }
        }

        yield return new AssetValue(
            MainMenu.Background,
            AssetKind.Image,
            "главное меню, фон");
        foreach (var element in MainMenu.Elements)
        {
            yield return new AssetValue(
                element.Image,
                AssetKind.Image,
                $"главное меню, элемент {element.Id}");
        }

        foreach (var node in Nodes)
        {
            yield return new AssetValue(
                node.Background,
                AssetKind.Image,
                $"нода {node.Title}, фон",
                node.Id);
            yield return new AssetValue(
                node.Music,
                AssetKind.Audio,
                $"нода {node.Title}, музыка",
                node.Id);
            foreach (var character in node.Characters)
            {
                yield return new AssetValue(
                    character.Sprite,
                    AssetKind.Image,
                    $"нода {node.Title}, персонаж {character.Name}",
                    node.Id);
                foreach (var voice in CharacterVoiceValues(character))
                {
                    yield return new AssetValue(
                        voice,
                        AssetKind.Audio,
                        $"нода {node.Title}, голос персонажа {character.Name}",
                        node.Id);
                }
            }
            foreach (var output in node.Outputs)
            {
                yield return new AssetValue(
                    output.TransitionSound,
                    AssetKind.Audio,
                    $"нода {node.Title}, переход {output.Label}",
                    node.Id);
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
            ReplaceCharacterAssetValues(character, reference, replacement);
        }
    }

    private static void ReplaceCharacterAssetValues(
        CharacterPlacement character,
        string reference,
        string replacement)
    {
        character.Sprite = Replace(character.Sprite, reference, replacement);
        character.VoiceSound = Replace(
            character.VoiceSound,
            reference,
            replacement);
        ReplaceAssetReferences(character.VoiceSounds, reference, replacement);
    }

    private static IReadOnlyList<string> CharacterVoiceValues(CharacterPlacement character)
        => character.GetVoiceSounds();

    private static void ReplaceAssetReferences(
        List<string> values,
        string reference,
        string replacement)
    {
        for (var index = values.Count - 1; index >= 0; index--)
        {
            if (!values[index].Equals(reference, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (replacement.Length == 0)
            {
                values.RemoveAt(index);
            }
            else
            {
                values[index] = replacement;
            }
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
        string Owner,
        string? NodeId = null);
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
