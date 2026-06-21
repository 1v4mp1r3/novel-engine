using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovelEngine.Core;

public sealed class ProjectLanguageException : Exception
{
    public ProjectLanguageException(string message, int line, int column)
        : base($"{message} (строка {line}, столбец {column})")
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }
    public int Column { get; }
}

public enum ProjectLanguageSyntaxKind
{
    Keyword,
    Declaration,
    String,
    Number,
    Comment,
    AssetReference,
    Punctuation,
}

public sealed record ProjectLanguageSyntaxSpan(
    int Start,
    int Length,
    ProjectLanguageSyntaxKind Kind);

public sealed record ProjectLanguageSourceLocation(
    int Start,
    int Length,
    int Line,
    int Column);

public enum ProjectLanguageCompletionKind
{
    Keyword,
    Snippet,
    Node,
    Type,
    Asset,
    Value,
}

public sealed record ProjectLanguageCompletion(
    string Label,
    string InsertText,
    string Description,
    ProjectLanguageCompletionKind Kind,
    int CaretOffset = -1);

public sealed record ProjectLanguageCompletionContext(
    int ReplacementStart,
    int ReplacementLength,
    IReadOnlyList<ProjectLanguageCompletion> Items);

public sealed record ProjectLanguageScopeSpan(
    int OpenBraceOffset,
    int CloseBraceOffset,
    int Depth);

public static class ProjectLanguage
{
    private static readonly JsonSerializerOptions StringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly HashSet<string> SyntaxKeywords = new(
        [
            "novel",
            "folder",
            "asset",
            "type",
            "extends",
            "node",
            "at",
            "in",
            "title",
            "speaker",
            "text",
            "background",
            "music",
            "inherit",
            "characters",
            "character",
            "name",
            "sprite",
            "voice",
            "voice-pitch",
            "voice-every",
            "position",
            "placement",
            "scale",
            "rotation",
            "script",
            "next",
            "choice",
            "when",
            "sound",
            "fade",
            "image",
            "audio",
            "other",
            "start",
            "scene",
            "dialogue",
            "left",
            "center",
            "right",
            "true",
            "false",
            "none",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static NovelProject Parse(string source)
    {
        var document = new Parser(source).Parse();
        var project = Compile(document);
        project.SourceCode = source;
        return project;
    }

    public static IReadOnlyList<ProjectLanguageSyntaxSpan> GetSyntaxSpans(
        string source) =>
        GetSyntaxSpansCore(source, null, out _);

    public static bool TryGetSyntaxSpans(
        string source,
        int maxSpans,
        out IReadOnlyList<ProjectLanguageSyntaxSpan> spans)
    {
        if (maxSpans < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSpans));
        }

        var result = GetSyntaxSpansCore(source, maxSpans, out var exceededLimit);
        spans = exceededLimit ? [] : result;
        return !exceededLimit;
    }

    private static IReadOnlyList<ProjectLanguageSyntaxSpan> GetSyntaxSpansCore(
        string source,
        int? maxSpans,
        out bool exceededLimit)
    {
        var spans = new List<ProjectLanguageSyntaxSpan>();
        exceededLimit = false;
        var index = 0;
        var declarationExpected = false;
        while (index < source.Length)
        {
            var current = source[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }
            if (current == '#'
                || current == '/' && index + 1 < source.Length
                    && source[index + 1] == '/')
            {
                var start = index;
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                }
                if (!AddSpan(
                        start,
                        index - start,
                        ProjectLanguageSyntaxKind.Comment))
                {
                    exceededLimit = true;
                    return spans;
                }
                continue;
            }
            if (current == '"')
            {
                var start = index;
                var triple = index + 2 < source.Length
                    && source[index + 1] == '"'
                    && source[index + 2] == '"';
                index += triple ? 3 : 1;
                var escaped = false;
                while (index < source.Length)
                {
                    if (triple
                        && index + 2 < source.Length
                        && source[index] == '"'
                        && source[index + 1] == '"'
                        && source[index + 2] == '"')
                    {
                        index += 3;
                        break;
                    }
                    if (!triple && !escaped && source[index] == '"')
                    {
                        index++;
                        break;
                    }
                    escaped = !triple && !escaped && source[index] == '\\';
                    if (source[index] != '\\')
                    {
                        escaped = false;
                    }
                    index++;
                }
                if (!AddSpan(
                        start,
                        index - start,
                        ProjectLanguageSyntaxKind.String))
                {
                    exceededLimit = true;
                    return spans;
                }
                declarationExpected = false;
                continue;
            }
            if (current == '@')
            {
                var start = index++;
                while (index < source.Length && IsSyntaxIdentifierPart(source[index]))
                {
                    index++;
                }
                if (!AddSpan(
                        start,
                        index - start,
                        ProjectLanguageSyntaxKind.AssetReference))
                {
                    exceededLimit = true;
                    return spans;
                }
                declarationExpected = false;
                continue;
            }
            if (char.IsDigit(current)
                || current == '-' && index + 1 < source.Length
                    && char.IsDigit(source[index + 1]))
            {
                var start = index++;
                while (index < source.Length
                    && (char.IsDigit(source[index]) || source[index] == '.'))
                {
                    index++;
                }
                if (!AddSpan(
                        start,
                        index - start,
                        ProjectLanguageSyntaxKind.Number))
                {
                    exceededLimit = true;
                    return spans;
                }
                declarationExpected = false;
                continue;
            }
            if (current == '_' || char.IsLetter(current))
            {
                var start = index++;
                while (index < source.Length && IsSyntaxIdentifierPart(source[index]))
                {
                    index++;
                }
                var value = source[start..index];
                var kind = declarationExpected
                    ? ProjectLanguageSyntaxKind.Declaration
                    : SyntaxKeywords.Contains(value)
                        ? ProjectLanguageSyntaxKind.Keyword
                        : ProjectLanguageSyntaxKind.Declaration;
                if (!AddSpan(start, index - start, kind))
                {
                    exceededLimit = true;
                    return spans;
                }
                declarationExpected = value.Equals(
                        "node",
                        StringComparison.OrdinalIgnoreCase)
                    || value.Equals("type", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("asset", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("character", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var punctuationLength = current == '-'
                && index + 1 < source.Length
                && source[index + 1] == '>'
                    ? 2
                    : 1;
            if (!AddSpan(
                    index,
                    punctuationLength,
                    ProjectLanguageSyntaxKind.Punctuation))
            {
                exceededLimit = true;
                return spans;
            }
            index += punctuationLength;
        }
        return spans;

        bool AddSpan(
            int start,
            int length,
            ProjectLanguageSyntaxKind kind)
        {
            spans.Add(new ProjectLanguageSyntaxSpan(start, length, kind));
            if (maxSpans.HasValue && spans.Count > maxSpans.Value)
            {
                return false;
            }
            return true;
        }
    }

    public static ProjectLanguageCompletionContext GetCompletions(
        string source,
        int caretOffset)
    {
        caretOffset = Math.Clamp(caretOffset, 0, source.Length);
        var ignoredSpans = GetIgnoredSyntaxSpans(source);
        if (IsInsideIgnoredSyntax(caretOffset, ignoredSpans))
        {
            return new ProjectLanguageCompletionContext(caretOffset, 0, []);
        }

        var replacementStart = caretOffset;
        while (replacementStart > 0
            && IsSyntaxIdentifierPart(source[replacementStart - 1]))
        {
            replacementStart--;
        }
        if (replacementStart > 0 && source[replacementStart - 1] == '@')
        {
            replacementStart--;
        }

        var prefix = source[replacementStart..caretOffset];
        var lineStart = source.LastIndexOf('\n', Math.Max(0, replacementStart - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var before = source[lineStart..replacementStart];
        var linePrefix = source[lineStart..caretOffset];
        var candidates = new List<ProjectLanguageCompletion>();

        if (prefix.StartsWith('@') || before.TrimEnd().EndsWith('@'))
        {
            AddDeclarationCompletions(
                candidates,
                source,
                @"(?m)^[ \t]*asset[ \t]+(?<id>[\p{L}_][\p{L}\p{N}_-]*)",
                "@",
                "Ассет проекта",
                ProjectLanguageCompletionKind.Asset);
        }
        else if (Regex.IsMatch(before, @"->[ \t]*$", RegexOptions.CultureInvariant))
        {
            AddDeclarationCompletions(
                candidates,
                source,
                @"(?m)^[ \t]*node[ \t]+(?<id>[\p{L}_][\p{L}\p{N}_-]*)",
                string.Empty,
                "Целевая нода",
                ProjectLanguageCompletionKind.Node);
            candidates.Add(
                new ProjectLanguageCompletion(
                    "none",
                    "none",
                    "Оставить переход неподключённым",
                    ProjectLanguageCompletionKind.Value));
        }
        else if (Regex.IsMatch(
            before,
            @"^[ \t]*node[ \t]+[\p{L}_][\p{L}\p{N}_-]*[ \t]*:[ \t]*$",
            RegexOptions.CultureInvariant))
        {
            AddTypeCompletions(candidates, source);
        }
        else if (Regex.IsMatch(
            before,
            @"^[ \t]*type[ \t]+[\p{L}_][\p{L}\p{N}_-]*[ \t]+extends[ \t]*$",
            RegexOptions.CultureInvariant))
        {
            AddTypeCompletions(candidates, source);
        }
        else if (Regex.IsMatch(
            linePrefix,
            @"^[ \t]*asset[ \t]+[\p{L}_][\p{L}\p{N}_-]*[ \t]*:[ \t]*[\p{L}_-]*$",
            RegexOptions.CultureInvariant))
        {
            AddValues(candidates, ["image", "audio", "other"], "Тип ассета");
        }
        else if (Regex.IsMatch(
            linePrefix,
            @"^[ \t]*position[ \t]+[\p{L}_-]*$",
            RegexOptions.CultureInvariant))
        {
            AddValues(candidates, ["left", "center", "right"], "Позиция персонажа");
        }
        else if (Regex.IsMatch(
            before,
            @"^[ \t]*inherit[ \t]+$",
            RegexOptions.CultureInvariant))
        {
            AddValues(
                candidates,
                ["background", "music", "characters"],
                "Наследуемое состояние");
        }
        else if (Regex.IsMatch(
            before,
            @"^[ \t]*inherit[ \t]+(?:background|music|characters)[ \t]+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            AddValues(candidates, ["true", "false"], "Логическое значение");
        }
        else if (GetBraceDepth(source, replacementStart, ignoredSpans) == 0)
        {
            candidates.AddRange(TopLevelCompletions);
        }
        else if (IsInsideCharacterScope(source, replacementStart, ignoredSpans))
        {
            candidates.AddRange(CharacterBodyCompletions);
        }
        else
        {
            candidates.AddRange(NodeBodyCompletions);
        }

        var normalizedPrefix = prefix.TrimStart('@');
        var items = BuildCompletionItems(candidates, normalizedPrefix);
        return new ProjectLanguageCompletionContext(
            replacementStart,
            caretOffset - replacementStart,
            items);
    }

    public static IReadOnlyList<ProjectLanguageScopeSpan> GetScopeSpans(
        string source)
    {
        var ignored = GetIgnoredSyntaxSpans(source);
        var scopes = new List<ProjectLanguageScopeSpan>();
        var stack = new Stack<(int Offset, int Depth)>();
        var ignoredIndex = 0;
        for (var index = 0; index < source.Length; index++)
        {
            while (ignoredIndex < ignored.Count
                && index >= ignored[ignoredIndex].Start
                    + ignored[ignoredIndex].Length)
            {
                ignoredIndex++;
            }
            if (ignoredIndex < ignored.Count
                && index >= ignored[ignoredIndex].Start
                && index < ignored[ignoredIndex].Start
                    + ignored[ignoredIndex].Length)
            {
                index = ignored[ignoredIndex].Start
                    + ignored[ignoredIndex].Length - 1;
                continue;
            }

            if (source[index] == '{')
            {
                stack.Push((index, stack.Count));
            }
            else if (source[index] == '}' && stack.TryPop(out var opening))
            {
                scopes.Add(
                    new ProjectLanguageScopeSpan(
                        opening.Offset,
                        index,
                        opening.Depth));
            }
        }
        while (stack.TryPop(out var opening))
        {
            scopes.Add(
                new ProjectLanguageScopeSpan(
                    opening.Offset,
                    source.Length,
                opening.Depth));
        }
        scopes.Sort(
            (left, right) =>
                left.OpenBraceOffset.CompareTo(right.OpenBraceOffset));
        return scopes;
    }

    public static ProjectLanguageSourceLocation? FindNodeDeclaration(
        string source,
        string nodeId)
    {
        var match = Regex.Match(
            source,
            $@"(?m)^[ \t]*node[ \t]+(?<id>{Regex.Escape(nodeId)})[ \t]*:",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }
        var group = match.Groups["id"];
        var (line, column) = GetLineColumn(source, group.Index);
        return new ProjectLanguageSourceLocation(
            group.Index,
            group.Length,
            line,
            column);
    }

    public static int GetOffset(string source, int line, int column)
    {
        line = Math.Max(1, line);
        column = Math.Max(1, column);
        var offset = 0;
        for (var currentLine = 1;
            currentLine < line && offset < source.Length;
            currentLine++)
        {
            var newline = source.IndexOf('\n', offset);
            offset = newline < 0 ? source.Length : newline + 1;
        }
        return Math.Clamp(offset + column - 1, 0, source.Length);
    }

    public static string Format(NovelProject project)
    {
        var builder = new StringBuilder();
        builder.Append("novel ").AppendLine(Quote(project.Title));

        foreach (var folder in project.AssetFolders)
        {
            builder.AppendLine();
            builder.Append("folder ").AppendLine(Quote(folder));
        }

        foreach (var asset in project.Assets)
        {
            builder.AppendLine();
            builder.Append("asset ")
                .Append(asset.Id)
                .Append(" : ")
                .Append(asset.Kind.ToString().ToLowerInvariant())
                .Append(' ')
                .Append(Quote(asset.Path));
            if (asset.Folder.Length > 0)
            {
                builder.Append(" in ").Append(Quote(asset.Folder));
            }
            builder.AppendLine();
        }

        foreach (var character in project.Characters)
        {
            builder.AppendLine();
            WriteCharacter(builder, character, string.Empty);
        }

        foreach (var type in project.NodeTypes)
        {
            builder.AppendLine();
            builder.Append("type ")
                .Append(type.Name)
                .Append(" extends ")
                .Append(type.BaseType)
                .AppendLine(" {");
            WriteDefaults(builder, type.Defaults, "    ");
            builder.AppendLine("}");
        }

        foreach (var node in project.Nodes)
        {
            builder.AppendLine();
            builder.Append("node ")
                .Append(node.Id)
                .Append(" : ")
                .Append(NodeTypeName(node))
                .Append(" at (")
                .Append(node.X.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(", ")
                .Append(node.Y.ToString("0.###", CultureInfo.InvariantCulture))
                .AppendLine(") {");
            if (ShouldWrite(node, "title", optional: false))
            {
                builder.Append("    title ").AppendLine(Quote(node.Title));
            }
            if (ShouldWrite(node, "speaker", optional: true, value: node.Speaker))
            {
                builder.Append("    speaker ").AppendLine(Quote(node.Speaker));
            }
            if (ShouldWrite(node, "text", optional: true, value: node.Text))
            {
                builder.Append("    text ").AppendLine(Quote(node.Text));
            }
            if (ShouldWrite(node, "inheritBackground", optional: false))
            {
                builder.Append("    inherit background ")
                    .AppendLine(Boolean(node.InheritBackground));
            }
            if (ShouldWrite(
                node,
                "background",
                optional: true,
                value: node.Background))
            {
                builder.Append("    background ")
                    .AppendLine(FormatAssetValue(node.Background));
            }
            if (ShouldWrite(node, "inheritMusic", optional: false))
            {
                builder.Append("    inherit music ")
                    .AppendLine(Boolean(node.InheritMusic));
            }
            if (ShouldWrite(node, "music", optional: true, value: node.Music))
            {
                builder.Append("    music ")
                    .AppendLine(FormatAssetValue(node.Music));
            }
            if (ShouldWrite(node, "inheritCharacters", optional: false))
            {
                builder.Append("    inherit characters ")
                    .AppendLine(Boolean(node.InheritCharacters));
            }
            if (ShouldWrite(
                node,
                "characters",
                optional: true,
                value: node.Characters.Count == 0 ? string.Empty : "characters"))
            {
                foreach (var character in node.Characters)
                {
                    WriteCharacter(builder, character, "    ");
                }
            }
            if (ShouldWrite(node, "script", optional: true, value: node.Script))
            {
                builder.Append("    script ").AppendLine(Quote(node.Script));
            }
            if (node.ScriptBlocks.Count > 0)
            {
                builder.Append("    # visual blocks: ")
                    .AppendLine(node.ScriptBlocks.Count.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var output in node.Outputs)
            {
                var keyword = node.Kind == NodeKind.Dialogue ? "choice" : "next";
                builder.Append("    ")
                    .Append(keyword)
                    .Append(' ')
                    .Append(Quote(output.Label))
                    .Append(" -> ")
                    .Append(output.TargetNodeId ?? "none");

                var condition = VisualConditionCompiler.Compile(output);
                var hasBody = condition.Length > 0
                    || output.Script.Length > 0
                    || output.TransitionSound.Length > 0
                    || output.FadeDurationMs != 350
                    || output.ScriptBlocks.Count > 0;
                if (!hasBody)
                {
                    builder.AppendLine();
                    continue;
                }

                builder.AppendLine(" {");
                if (condition.Length > 0)
                {
                    builder.Append("        when ").AppendLine(Quote(condition));
                }
                if (output.Script.Length > 0)
                {
                    builder.Append("        script ").AppendLine(Quote(output.Script));
                }
                if (output.ScriptBlocks.Count > 0)
                {
                    builder.Append("        # visual blocks: ")
                        .AppendLine(output.ScriptBlocks.Count.ToString(CultureInfo.InvariantCulture));
                }
                if (output.TransitionSound.Length > 0)
                {
                    builder.Append("        sound ")
                        .AppendLine(FormatAssetValue(output.TransitionSound));
                }
                if (output.FadeDurationMs != 350)
                {
                    builder.Append("        fade ")
                        .AppendLine(output.FadeDurationMs.ToString(CultureInfo.InvariantCulture));
                }
                builder.AppendLine("    }");
            }
            builder.AppendLine("}");
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static NovelProject Compile(LanguageDocument document)
    {
        var definitions = new Dictionary<string, TypeDeclaration>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in document.Types)
        {
            if (IsBuiltInType(declaration.Name)
                || !definitions.TryAdd(declaration.Name, declaration))
            {
                throw Error(
                    declaration.Token,
                    $"Тип «{declaration.Name}» уже объявлен.");
            }
        }

        var resolvedTypes = new Dictionary<string, ResolvedType>(
            StringComparer.OrdinalIgnoreCase);
        ResolvedType ResolveType(string name, Token token, HashSet<string>? stack = null)
        {
            if (TryBuiltInType(name, out var builtIn))
            {
                return builtIn;
            }
            if (resolvedTypes.TryGetValue(name, out var resolved))
            {
                return resolved;
            }
            if (!definitions.TryGetValue(name, out var declaration))
            {
                throw Error(token, $"Неизвестный тип ноды «{name}».");
            }

            stack ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!stack.Add(name))
            {
                throw Error(token, $"Циклическое наследование типа «{name}».");
            }
            var baseType = ResolveType(declaration.BaseType, declaration.Token, stack);
            if (baseType.Kind == NodeKind.Start)
            {
                throw Error(
                    declaration.Token,
                    "Пользовательский тип не может наследоваться от start.");
            }
            stack.Remove(name);

            resolved = new ResolvedType(
                baseType.Kind,
                PropertyBag.Merge(baseType.Defaults, declaration.Defaults));
            resolvedTypes[name] = resolved;
            return resolved;
        }

        foreach (var declaration in document.Types)
        {
            _ = ResolveType(declaration.Name, declaration.Token);
        }

        var project = new NovelProject
        {
            Title = document.Title,
        };
        foreach (var folder in document.Folders)
        {
            ProjectAssets.CreateFolder(project, folder.Path);
        }
        var assetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in document.Assets)
        {
            if (!assetIds.Add(declaration.Id))
            {
                throw Error(
                    declaration.Token,
                    $"Ассет «{declaration.Id}» уже объявлен.");
            }
            project.Assets.Add(
                new NovelAsset
                {
                    Id = declaration.Id,
                    Kind = declaration.Kind,
                    Path = declaration.Path,
                    Folder = declaration.Folder,
                });
            if (declaration.Folder.Length > 0)
            {
                ProjectAssets.CreateFolder(project, declaration.Folder);
            }
        }
        var characterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in document.Characters)
        {
            var character = declaration.Character;
            if (!characterIds.Add(character.Id))
            {
                throw Error(
                    declaration.Token,
                    $"Персонаж библиотеки «{character.Id}» уже объявлен.");
            }
            project.Characters.Add(character.Clone());
        }
        foreach (var declaration in document.Types)
        {
            project.NodeTypes.Add(
                new NodeTypeDefinition
                {
                    Name = declaration.Name,
                    BaseType = declaration.BaseType,
                    Defaults = declaration.Defaults.ToModel(),
                });
        }

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in document.Nodes)
        {
            if (!nodeIds.Add(declaration.Id))
            {
                throw Error(
                    declaration.Token,
                    $"Нода «{declaration.Id}» уже объявлена.");
            }

            var type = ResolveType(declaration.TypeName, declaration.Token);
            var values = PropertyBag.Merge(type.Defaults, declaration.Values);
            var node = new NovelNode
            {
                Id = declaration.Id,
                Kind = type.Kind,
                TypeName = declaration.TypeName,
                UsesTypeDefaults = true,
                Title = values.Title ?? declaration.Id,
                Speaker = values.Speaker ?? string.Empty,
                Text = values.Text ?? string.Empty,
                Background = values.Background ?? string.Empty,
                InheritBackground = values.InheritBackground
                    ?? type.Kind != NodeKind.Start,
                Music = values.Music ?? string.Empty,
                InheritMusic = values.InheritMusic
                    ?? type.Kind != NodeKind.Start,
                InheritCharacters = values.InheritCharacters
                    ?? type.Kind != NodeKind.Start,
                Script = values.Script ?? string.Empty,
                X = declaration.X,
                Y = declaration.Y,
            };
            node.PropertyOverrides.UnionWith(
                declaration.Values.ExplicitPropertyNames());
            foreach (var character in values.Characters)
            {
                node.Characters.Add(character.Clone());
            }
            for (var index = 0; index < declaration.Outputs.Count; index++)
            {
                var output = declaration.Outputs[index];
                node.Outputs.Add(
                    new NodeOutput
                    {
                        Id = $"out-{declaration.Id}-{index + 1}",
                        Label = output.Label,
                        TargetNodeId = output.TargetId.Equals(
                            "none",
                            StringComparison.OrdinalIgnoreCase)
                                ? null
                                : output.TargetId,
                        Condition = output.Condition,
                        Script = output.Script,
                        TransitionSound = output.Sound,
                        FadeDurationMs = output.FadeDurationMs,
                    });
            }

            if (node.Kind != NodeKind.Dialogue && node.Outputs.Count == 0)
            {
                node.Outputs.Add(
                    new NodeOutput
                    {
                        Id = $"out-{declaration.Id}-1",
                        Label = "Дальше",
                    });
            }
            project.Nodes.Add(node);
        }

        try
        {
            project.Validate();
            ValidateRuntimeScripts(project);
        }
        catch (InvalidDataException error)
        {
            throw new ProjectLanguageException(error.Message, 1, 1);
        }
        return project;
    }

    private static void ValidateRuntimeScripts(NovelProject project)
    {
        foreach (var node in project.Nodes)
        {
            VisualScriptCompiler.Execute(
                node.Script,
                node.ScriptBlocks,
                new ScriptState());
            foreach (var output in node.Outputs)
            {
                _ = VisualConditionCompiler.Evaluate(output, new ScriptState());
                VisualScriptCompiler.Execute(
                    output.Script,
                    output.ScriptBlocks,
                    new ScriptState());
            }
        }
    }

    private static void WriteDefaults(
        StringBuilder builder,
        NodeTypeDefaults defaults,
        string indent)
    {
        WriteOptional(builder, indent, "title", defaults.Title);
        WriteOptional(builder, indent, "speaker", defaults.Speaker);
        WriteOptional(builder, indent, "text", defaults.Text);
        WriteOptional(
            builder,
            indent,
            "inherit background",
            defaults.InheritBackground);
        WriteOptionalAsset(builder, indent, "background", defaults.Background);
        WriteOptional(builder, indent, "inherit music", defaults.InheritMusic);
        WriteOptionalAsset(builder, indent, "music", defaults.Music);
        WriteOptional(
            builder,
            indent,
            "inherit characters",
            defaults.InheritCharacters);
        foreach (var character in defaults.Characters)
        {
            WriteCharacter(builder, character, indent);
        }
        WriteOptional(builder, indent, "script", defaults.Script);
    }

    private static void WriteCharacter(
        StringBuilder builder,
        CharacterPlacement character,
        string indent)
    {
        builder.Append(indent).Append("character ").Append(character.Id).AppendLine(" {");
        builder.Append(indent).Append("    name ").AppendLine(Quote(character.Name));
        if (character.Sprite.Length > 0)
        {
            builder.Append(indent)
                .Append("    sprite ")
                .AppendLine(FormatAssetValue(character.Sprite));
        }
        foreach (var voice in CharacterVoiceSounds(character))
        {
            builder.Append(indent)
                .Append("    voice ")
                .AppendLine(FormatAssetValue(voice));
        }
        if (Math.Abs(character.VoicePitch - 1) > 0.001)
        {
            builder.Append(indent)
                .Append("    voice-pitch ")
                .AppendLine(character.VoicePitch.ToString("0.###", CultureInfo.InvariantCulture));
        }
        if (character.VoiceEveryNthCharacter != 1)
        {
            builder.Append(indent)
                .Append("    voice-every ")
                .AppendLine(character.VoiceEveryNthCharacter.ToString(CultureInfo.InvariantCulture));
        }
        builder.Append(indent)
            .Append("    position ")
            .AppendLine(character.Position.ToString().ToLowerInvariant());
        if (character.HasCustomTransform)
        {
            builder.Append(indent)
                .Append("    placement (")
                .Append(character.X.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(", ")
                .Append(character.Y.ToString("0.###", CultureInfo.InvariantCulture))
                .AppendLine(")");
            builder.Append(indent)
                .Append("    scale ")
                .AppendLine(character.Scale.ToString("0.###", CultureInfo.InvariantCulture));
            builder.Append(indent)
                .Append("    rotation ")
                .AppendLine(character.Rotation.ToString("0.###", CultureInfo.InvariantCulture));
        }
        builder.Append(indent).AppendLine("}");
    }

    private static IReadOnlyList<string> CharacterVoiceSounds(
        CharacterPlacement character) =>
        character.GetVoiceSounds();

    private static void WriteOptional(
        StringBuilder builder,
        string indent,
        string keyword,
        string? value)
    {
        if (value is not null)
        {
            builder.Append(indent).Append(keyword).Append(' ').AppendLine(Quote(value));
        }
    }

    private static void WriteOptionalAsset(
        StringBuilder builder,
        string indent,
        string keyword,
        string? value)
    {
        if (value is not null)
        {
            builder.Append(indent)
                .Append(keyword)
                .Append(' ')
                .AppendLine(FormatAssetValue(value));
        }
    }

    private static void WriteOptional(
        StringBuilder builder,
        string indent,
        string keyword,
        bool? value)
    {
        if (value.HasValue)
        {
            builder.Append(indent)
                .Append(keyword)
                .Append(' ')
                .AppendLine(Boolean(value.Value));
        }
    }

    private static string NodeTypeName(NovelNode node) =>
        string.IsNullOrWhiteSpace(node.TypeName)
            ? node.Kind.ToString().ToLowerInvariant()
            : node.TypeName;

    private static bool ShouldWrite(
        NovelNode node,
        string property,
        bool optional,
        string value = "") =>
        node.UsesTypeDefaults
            ? node.PropertyOverrides.Contains(property)
            : !optional || value.Length > 0;

    private static string Quote(string value) =>
        JsonSerializer.Serialize(value, StringOptions);

    private static string FormatAssetValue(string value) =>
        AssetReference.TryGetId(value, out var id)
            ? AssetReference.Create(id)
            : Quote(value);

    private static string Boolean(bool value) =>
        value ? "true" : "false";

    private static bool IsSyntaxIdentifierPart(char value) =>
        value is '_' or '-' || char.IsLetterOrDigit(value);

    private static IReadOnlyList<ProjectLanguageSyntaxSpan> GetIgnoredSyntaxSpans(
        string source)
    {
        var spans = new List<ProjectLanguageSyntaxSpan>();
        var index = 0;
        while (index < source.Length)
        {
            var current = source[index];
            if (current == '#'
                || current == '/' && index + 1 < source.Length
                    && source[index + 1] == '/')
            {
                var start = index;
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                }
                spans.Add(
                    new ProjectLanguageSyntaxSpan(
                        start,
                        index - start,
                        ProjectLanguageSyntaxKind.Comment));
                continue;
            }

            if (current == '"')
            {
                var start = index;
                var triple = index + 2 < source.Length
                    && source[index + 1] == '"'
                    && source[index + 2] == '"';
                index += triple ? 3 : 1;
                var escaped = false;
                while (index < source.Length)
                {
                    if (triple
                        && index + 2 < source.Length
                        && source[index] == '"'
                        && source[index + 1] == '"'
                        && source[index + 2] == '"')
                    {
                        index += 3;
                        break;
                    }

                    if (!triple && !escaped && source[index] == '"')
                    {
                        index++;
                        break;
                    }

                    escaped = !triple && !escaped && source[index] == '\\';
                    if (source[index] != '\\')
                    {
                        escaped = false;
                    }
                    index++;
                }
                spans.Add(
                    new ProjectLanguageSyntaxSpan(
                        start,
                        index - start,
                        ProjectLanguageSyntaxKind.String));
                continue;
            }

            index++;
        }

        return spans;
    }

    private static bool IsInsideIgnoredSyntax(
        int offset,
        IReadOnlyList<ProjectLanguageSyntaxSpan> ignoredSpans)
    {
        foreach (var span in ignoredSpans)
        {
            if (offset <= span.Start)
            {
                return false;
            }
            if (offset <= span.Start + span.Length)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsInsideCharacterScope(
        string source,
        int caretOffset,
        IReadOnlyList<ProjectLanguageSyntaxSpan> ignoredSpans)
    {
        var openBraceOffset = FindCurrentScopeOpening(
            source,
            caretOffset,
            ignoredSpans);
        if (!openBraceOffset.HasValue)
        {
            return false;
        }

        var lineStart = source.LastIndexOf(
            '\n',
            Math.Max(0, openBraceOffset.Value - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var header = source[lineStart..openBraceOffset.Value];
        return Regex.IsMatch(
            header,
            @"^[ \t]*character[ \t]+[\p{L}_][\p{L}\p{N}_-]*[ \t]*$",
            RegexOptions.CultureInvariant);
    }

    private static int? FindCurrentScopeOpening(
        string source,
        int end,
        IReadOnlyList<ProjectLanguageSyntaxSpan> ignoredSpans)
    {
        var stack = new Stack<int>();
        var ignoredIndex = 0;
        for (var index = 0; index < end; index++)
        {
            while (ignoredIndex < ignoredSpans.Count
                && index >= ignoredSpans[ignoredIndex].Start
                    + ignoredSpans[ignoredIndex].Length)
            {
                ignoredIndex++;
            }
            if (ignoredIndex < ignoredSpans.Count
                && index >= ignoredSpans[ignoredIndex].Start
                && index < ignoredSpans[ignoredIndex].Start
                    + ignoredSpans[ignoredIndex].Length)
            {
                index = Math.Min(
                    end,
                    ignoredSpans[ignoredIndex].Start
                    + ignoredSpans[ignoredIndex].Length)
                    - 1;
                continue;
            }

            if (source[index] == '{')
            {
                stack.Push(index);
            }
            else if (source[index] == '}')
            {
                _ = stack.TryPop(out _);
            }
        }
        return stack.TryPeek(out var opening) ? opening : null;
    }

    private static readonly IReadOnlyList<ProjectLanguageCompletion>
        TopLevelCompletions =
        [
            new(
                "asset",
                "asset id : image \"assets/path.png\"\n\n",
                "Объявить ассет проекта",
                ProjectLanguageCompletionKind.Snippet,
                6),
            new(
                "folder",
                "folder \"path\"\n\n",
                "Объявить папку ассетов",
                ProjectLanguageCompletionKind.Snippet,
                8),
            new(
                "node",
                "node id : scene at (0, 0) {\n    \n}\n\n",
                "Объявить ноду",
                ProjectLanguageCompletionKind.Snippet,
                5),
            new(
                "type",
                "type Name extends scene {\n    \n}\n\n",
                "Объявить наследуемый тип ноды",
                ProjectLanguageCompletionKind.Snippet,
                5),
        ];

    private static readonly IReadOnlyList<ProjectLanguageCompletion>
        NodeBodyCompletions =
        [
            new(
                "background",
                "background @",
                "Установить фон ноды",
                ProjectLanguageCompletionKind.Snippet),
            new(
                "character",
                "character id {\n    name \"\"\n    sprite @\n    position center\n}",
                "Добавить персонажа",
                ProjectLanguageCompletionKind.Snippet,
                10),
            new(
                "choice",
                "choice \"\" -> none",
                "Добавить вариант ответа",
                ProjectLanguageCompletionKind.Snippet,
                8),
            new(
                "inherit",
                "inherit background true",
                "Настроить наследование состояния",
                ProjectLanguageCompletionKind.Snippet,
                8),
            new(
                "music",
                "music @",
                "Установить музыку ноды",
                ProjectLanguageCompletionKind.Snippet),
            new(
                "next",
                "next \"\" -> none",
                "Добавить линейный переход",
                ProjectLanguageCompletionKind.Snippet,
                6),
            new(
                "script",
                "script \"\"",
                "Выполнить скрипт при входе",
                ProjectLanguageCompletionKind.Snippet,
                8),
            new(
                "speaker",
                "speaker \"\"",
                "Задать имя говорящего",
                ProjectLanguageCompletionKind.Snippet,
                9),
            new(
                "text",
                "text \"\"",
                "Задать текст сцены или реплики",
                ProjectLanguageCompletionKind.Snippet,
                6),
            new(
                "title",
                "title \"\"",
                "Задать название ноды",
                ProjectLanguageCompletionKind.Snippet,
                7),
        ];

    private static readonly IReadOnlyList<ProjectLanguageCompletion>
        CharacterBodyCompletions =
        [
            new(
                "name",
                "name \"\"",
                "Задать имя персонажа",
                ProjectLanguageCompletionKind.Snippet,
                6),
            new(
                "placement",
                "placement (960, 500)",
                "Задать свободную позицию персонажа на сцене",
                ProjectLanguageCompletionKind.Snippet),
            new(
                "position",
                "position center",
                "Задать базовую позицию персонажа",
                ProjectLanguageCompletionKind.Snippet),
            new(
                "rotation",
                "rotation 0",
                "Задать поворот персонажа в градусах",
                ProjectLanguageCompletionKind.Snippet),
            new(
                "scale",
                "scale 1",
                "Задать масштаб персонажа",
                ProjectLanguageCompletionKind.Snippet),
            new(
                "sprite",
                "sprite @",
                "Задать спрайт персонажа",
                ProjectLanguageCompletionKind.Snippet),
        ];

    private static void AddTypeCompletions(
        ICollection<ProjectLanguageCompletion> candidates,
        string source)
    {
        AddValues(candidates, ["start", "scene", "dialogue"], "Встроенный тип ноды");
        AddDeclarationCompletions(
            candidates,
            source,
            @"(?m)^[ \t]*type[ \t]+(?<id>[\p{L}_][\p{L}\p{N}_-]*)",
            string.Empty,
            "Пользовательский тип ноды",
            ProjectLanguageCompletionKind.Type);
    }

    private static void AddDeclarationCompletions(
        ICollection<ProjectLanguageCompletion> candidates,
        string source,
        string pattern,
        string insertPrefix,
        string description,
        ProjectLanguageCompletionKind kind)
    {
        foreach (Match match in Regex.Matches(
            source,
            pattern,
            RegexOptions.CultureInvariant))
        {
            var id = match.Groups["id"].Value;
            candidates.Add(
                new ProjectLanguageCompletion(
                    $"{insertPrefix}{id}",
                    $"{insertPrefix}{id}",
                    description,
                    kind));
        }
    }

    private static void AddValues(
        ICollection<ProjectLanguageCompletion> candidates,
        IEnumerable<string> values,
        string description)
    {
        foreach (var value in values)
        {
            candidates.Add(
                new ProjectLanguageCompletion(
                    value,
                    value,
                    description,
                    ProjectLanguageCompletionKind.Value));
        }
    }

    private static IReadOnlyList<ProjectLanguageCompletion> BuildCompletionItems(
        IReadOnlyList<ProjectLanguageCompletion> candidates,
        string normalizedPrefix)
    {
        const int limit = 40;
        var seenInsertText = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directMatches = new List<ProjectLanguageCompletion>();
        var normalizedMatches = new List<ProjectLanguageCompletion>();

        foreach (var item in candidates)
        {
            var normalizedLabel = item.Label.TrimStart('@');
            if (normalizedPrefix.Length > 0
                && !normalizedLabel.StartsWith(
                    normalizedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!seenInsertText.Add(item.InsertText))
            {
                continue;
            }

            if (item.Label.StartsWith(
                normalizedPrefix,
                StringComparison.OrdinalIgnoreCase))
            {
                directMatches.Add(item);
            }
            else
            {
                normalizedMatches.Add(item);
            }
        }

        directMatches.Sort(CompareCompletionLabels);
        normalizedMatches.Sort(CompareCompletionLabels);

        var items = new List<ProjectLanguageCompletion>(
            Math.Min(limit, directMatches.Count + normalizedMatches.Count));
        AddCompletionItems(items, directMatches, limit);
        AddCompletionItems(items, normalizedMatches, limit);
        return items;
    }

    private static int CompareCompletionLabels(
        ProjectLanguageCompletion left,
        ProjectLanguageCompletion right) =>
        StringComparer.OrdinalIgnoreCase.Compare(left.Label, right.Label);

    private static void AddCompletionItems(
        List<ProjectLanguageCompletion> target,
        IReadOnlyList<ProjectLanguageCompletion> source,
        int limit)
    {
        for (var index = 0; index < source.Count && target.Count < limit; index++)
        {
            target.Add(source[index]);
        }
    }

    private static int GetBraceDepth(
        string source,
        int end,
        IReadOnlyList<ProjectLanguageSyntaxSpan> ignoredSpans)
    {
        var depth = 0;
        var ignoredIndex = 0;
        for (var index = 0; index < end; index++)
        {
            while (ignoredIndex < ignoredSpans.Count
                && index >= ignoredSpans[ignoredIndex].Start
                    + ignoredSpans[ignoredIndex].Length)
            {
                ignoredIndex++;
            }
            if (ignoredIndex < ignoredSpans.Count
                && index >= ignoredSpans[ignoredIndex].Start
                && index < ignoredSpans[ignoredIndex].Start
                    + ignoredSpans[ignoredIndex].Length)
            {
                index = Math.Min(
                    end,
                    ignoredSpans[ignoredIndex].Start
                    + ignoredSpans[ignoredIndex].Length)
                    - 1;
                continue;
            }
            depth += source[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };
        }
        return Math.Max(0, depth);
    }

    private static (int Line, int Column) GetLineColumn(string source, int offset)
    {
        var line = 1;
        var lineStart = 0;
        for (var index = 0; index < offset && index < source.Length; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }
        return (line, offset - lineStart + 1);
    }

    private static bool IsBuiltInType(string name) =>
        name.Equals("start", StringComparison.OrdinalIgnoreCase)
        || name.Equals("scene", StringComparison.OrdinalIgnoreCase)
        || name.Equals("dialogue", StringComparison.OrdinalIgnoreCase);

    private static bool TryBuiltInType(string name, out ResolvedType type)
    {
        var defaults = new PropertyBag();
        if (name.Equals("start", StringComparison.OrdinalIgnoreCase))
        {
            defaults.InheritBackground = false;
            defaults.InheritMusic = false;
            defaults.InheritCharacters = false;
            type = new ResolvedType(NodeKind.Start, defaults);
            return true;
        }
        if (name.Equals("scene", StringComparison.OrdinalIgnoreCase))
        {
            defaults.InheritBackground = false;
            defaults.InheritMusic = true;
            defaults.InheritCharacters = true;
            type = new ResolvedType(NodeKind.Scene, defaults);
            return true;
        }
        if (name.Equals("dialogue", StringComparison.OrdinalIgnoreCase))
        {
            defaults.InheritBackground = true;
            defaults.InheritMusic = true;
            defaults.InheritCharacters = true;
            type = new ResolvedType(NodeKind.Dialogue, defaults);
            return true;
        }

        type = null!;
        return false;
    }

    private static ProjectLanguageException Error(Token token, string message) =>
        new(message, token.Line, token.Column);

    private sealed record ResolvedType(NodeKind Kind, PropertyBag Defaults);

    private sealed class LanguageDocument
    {
        public string Title { get; set; } = "Новая новелла";
        public List<FolderDeclaration> Folders { get; } = [];
        public List<AssetDeclaration> Assets { get; } = [];
        public List<CharacterDeclaration> Characters { get; } = [];
        public List<TypeDeclaration> Types { get; } = [];
        public List<NodeDeclaration> Nodes { get; } = [];
    }

    private sealed record AssetDeclaration(
        string Id,
        AssetKind Kind,
        string Path,
        string Folder,
        Token Token);

    private sealed record FolderDeclaration(string Path, Token Token);

    private sealed record CharacterDeclaration(CharacterPlacement Character, Token Token);

    private sealed record TypeDeclaration(
        string Name,
        string BaseType,
        PropertyBag Defaults,
        Token Token);

    private sealed class NodeDeclaration
    {
        public required string Id { get; init; }
        public required string TypeName { get; init; }
        public required Token Token { get; init; }
        public float X { get; set; }
        public float Y { get; set; }
        public PropertyBag Values { get; } = new();
        public List<OutputDeclaration> Outputs { get; } = [];
    }

    private sealed class OutputDeclaration
    {
        public required string Label { get; init; }
        public required string TargetId { get; set; }
        public string Condition { get; set; } = string.Empty;
        public string Script { get; set; } = string.Empty;
        public string Sound { get; set; } = string.Empty;
        public int FadeDurationMs { get; set; } = 350;
    }

    private sealed class PropertyBag
    {
        public string? Title { get; set; }
        public string? Speaker { get; set; }
        public string? Text { get; set; }
        public string? Background { get; set; }
        public bool? InheritBackground { get; set; }
        public string? Music { get; set; }
        public bool? InheritMusic { get; set; }
        public bool? InheritCharacters { get; set; }
        public List<CharacterPlacement> Characters { get; } = [];
        public string? Script { get; set; }

        public static PropertyBag Merge(PropertyBag inherited, PropertyBag overrides)
        {
            var result = new PropertyBag
            {
                Title = overrides.Title ?? inherited.Title,
                Speaker = overrides.Speaker ?? inherited.Speaker,
                Text = overrides.Text ?? inherited.Text,
                Background = overrides.Background ?? inherited.Background,
                InheritBackground =
                    overrides.InheritBackground ?? inherited.InheritBackground,
                Music = overrides.Music ?? inherited.Music,
                InheritMusic = overrides.InheritMusic ?? inherited.InheritMusic,
                InheritCharacters =
                    overrides.InheritCharacters ?? inherited.InheritCharacters,
                Script = overrides.Script ?? inherited.Script,
            };
            var characters = overrides.Characters.Count > 0
                ? overrides.Characters
                : inherited.Characters;
            result.Characters.AddRange(characters.Select(character => character.Clone()));
            return result;
        }

        public NodeTypeDefaults ToModel()
        {
            var defaults = new NodeTypeDefaults
            {
                Title = Title,
                Speaker = Speaker,
                Text = Text,
                Background = Background,
                InheritBackground = InheritBackground,
                Music = Music,
                InheritMusic = InheritMusic,
                InheritCharacters = InheritCharacters,
                Script = Script,
            };
            defaults.Characters.AddRange(
                Characters.Select(character => character.Clone()));
            return defaults;
        }

        public IEnumerable<string> ExplicitPropertyNames()
        {
            if (Title is not null)
            {
                yield return "title";
            }
            if (Speaker is not null)
            {
                yield return "speaker";
            }
            if (Text is not null)
            {
                yield return "text";
            }
            if (Background is not null)
            {
                yield return "background";
            }
            if (InheritBackground.HasValue)
            {
                yield return "inheritBackground";
            }
            if (Music is not null)
            {
                yield return "music";
            }
            if (InheritMusic.HasValue)
            {
                yield return "inheritMusic";
            }
            if (InheritCharacters.HasValue)
            {
                yield return "inheritCharacters";
            }
            if (Characters.Count > 0)
            {
                yield return "characters";
            }
            if (Script is not null)
            {
                yield return "script";
            }
        }
    }

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private int _position;

        public Parser(string source)
        {
            _tokens = new Lexer(source).Tokenize();
        }

        public LanguageDocument Parse()
        {
            var document = new LanguageDocument();
            ExpectKeyword("novel");
            document.Title = Expect(TokenKind.String, "Ожидалось название новеллы.").Text;

            while (!Check(TokenKind.End))
            {
                if (MatchKeyword("folder"))
                {
                    document.Folders.Add(ParseFolder());
                }
                else if (MatchKeyword("asset"))
                {
                    document.Assets.Add(ParseAsset());
                }
                else if (MatchKeyword("character"))
                {
                    document.Characters.Add(ParseCharacterDeclaration());
                }
                else if (MatchKeyword("type"))
                {
                    document.Types.Add(ParseType());
                }
                else if (MatchKeyword("node"))
                {
                    document.Nodes.Add(ParseNode());
                }
                else
                {
                    throw Error(
                        Current,
                        "Ожидалось объявление folder, asset, character, type или node.");
                }
            }
            return document;
        }

        private FolderDeclaration ParseFolder()
        {
            var token = Expect(TokenKind.String, "Ожидался путь папки.");
            try
            {
                var folder = ProjectAssets.NormalizeFolder(token.Text);
                if (folder.Length == 0)
                {
                    throw Error(token, "Папка не может быть пустой.");
                }
                return new FolderDeclaration(folder, token);
            }
            catch (InvalidDataException error)
            {
                throw Error(token, error.Message);
            }
        }

        private AssetDeclaration ParseAsset()
        {
            var id = ExpectIdentifier("Ожидался id ассета.");
            if (!AssetReference.IsValidId(id.Text))
            {
                throw Error(id, $"Некорректный id ассета «{id.Text}».");
            }
            Expect(TokenKind.Colon, "Ожидалась «:» после id ассета.");
            var kindToken = ExpectIdentifier(
                "Ожидался тип ассета image, audio или other.");
            var kind = kindToken.Text.ToLowerInvariant() switch
            {
                "image" => AssetKind.Image,
                "audio" => AssetKind.Audio,
                "other" => AssetKind.Other,
                _ => throw Error(
                    kindToken,
                    "Тип ассета должен быть image, audio или other."),
            };
            var path = ExpectString("Ожидался путь к файлу ассета.");
            var folder = string.Empty;
            if (MatchKeyword("in"))
            {
                var folderToken = Expect(
                    TokenKind.String,
                    "Ожидался путь папки после in.");
                try
                {
                    folder = ProjectAssets.NormalizeFolder(folderToken.Text);
                }
                catch (InvalidDataException error)
                {
                    throw Error(folderToken, error.Message);
                }
            }
            return new AssetDeclaration(id.Text, kind, path, folder, id);
        }

        private TypeDeclaration ParseType()
        {
            var name = ExpectIdentifier("Ожидалось имя типа.");
            ExpectKeyword("extends");
            var baseType = ExpectIdentifier("Ожидалось имя базового типа.");
            Expect(TokenKind.LeftBrace, "Ожидалась «{» после типа.");
            var defaults = new PropertyBag();
            while (!Match(TokenKind.RightBrace))
            {
                var keyword = ExpectIdentifier("Ожидалось свойство типа.");
                if (!ParseCommonProperty(keyword, defaults))
                {
                    throw Error(keyword, $"Неизвестное свойство типа «{keyword.Text}».");
                }
            }
            return new TypeDeclaration(name.Text, baseType.Text, defaults, name);
        }

        private NodeDeclaration ParseNode()
        {
            var id = ExpectIdentifier("Ожидался id ноды.");
            Expect(TokenKind.Colon, "Ожидалась «:» после id ноды.");
            var type = ExpectIdentifier("Ожидался тип ноды.");
            var node = new NodeDeclaration
            {
                Id = id.Text,
                TypeName = type.Text,
                Token = id,
            };

            if (MatchKeyword("at"))
            {
                Expect(TokenKind.LeftParenthesis, "Ожидалась «(» после at.");
                node.X = ExpectFloat("Ожидалась координата X.");
                Expect(TokenKind.Comma, "Ожидалась запятая между координатами.");
                node.Y = ExpectFloat("Ожидалась координата Y.");
                Expect(TokenKind.RightParenthesis, "Ожидалась «)» после координат.");
            }

            Expect(TokenKind.LeftBrace, "Ожидалась «{» после ноды.");
            while (!Match(TokenKind.RightBrace))
            {
                var keyword = ExpectIdentifier("Ожидалось свойство или переход ноды.");
                if (ParseCommonProperty(keyword, node.Values))
                {
                    continue;
                }
                if (keyword.Text.Equals("next", StringComparison.OrdinalIgnoreCase)
                    || keyword.Text.Equals(
                        "choice",
                        StringComparison.OrdinalIgnoreCase))
                {
                    node.Outputs.Add(ParseOutput());
                    continue;
                }
                throw Error(keyword, $"Неизвестное свойство ноды «{keyword.Text}».");
            }
            return node;
        }

        private bool ParseCommonProperty(Token keyword, PropertyBag bag)
        {
            if (Keyword(keyword, "title"))
            {
                bag.Title = ExpectString("Ожидался текст title.");
                return true;
            }
            if (Keyword(keyword, "speaker"))
            {
                bag.Speaker = ExpectString("Ожидался текст speaker.");
                return true;
            }
            if (Keyword(keyword, "text"))
            {
                bag.Text = ExpectString("Ожидался текст ноды.");
                return true;
            }
            if (Keyword(keyword, "background"))
            {
                bag.Background = ExpectAssetValue("Ожидался путь или @ссылка на фон.");
                return true;
            }
            if (Keyword(keyword, "music"))
            {
                bag.Music = ExpectAssetValue(
                    "Ожидался путь или @ссылка на музыку.");
                return true;
            }
            if (Keyword(keyword, "script"))
            {
                bag.Script = ExpectString("Ожидался скрипт.");
                return true;
            }
            if (Keyword(keyword, "inherit"))
            {
                var resource = ExpectIdentifier(
                    "Ожидалось background, music или characters.");
                var value = ExpectBoolean();
                if (Keyword(resource, "background"))
                {
                    bag.InheritBackground = value;
                }
                else if (Keyword(resource, "music"))
                {
                    bag.InheritMusic = value;
                }
                else if (Keyword(resource, "characters"))
                {
                    bag.InheritCharacters = value;
                }
                else
                {
                    throw Error(
                        resource,
                        $"Неизвестный наследуемый ресурс «{resource.Text}».");
                }
                return true;
            }
            if (Keyword(keyword, "character"))
            {
                bag.Characters.Add(ParseCharacter());
                return true;
            }
            return false;
        }

        private CharacterPlacement ParseCharacter()
        {
            var id = ExpectIdentifier("Ожидался id персонажа.");
            Expect(TokenKind.LeftBrace, "Ожидалась «{» после персонажа.");
            var name = id.Text;
            var sprite = string.Empty;
            var position = CharacterPosition.Center;
            var hasCustomTransform = false;
            var x = CharacterLayout.StageWidth / 2;
            var y = CharacterLayout.DefaultCenterY;
            var scale = 1d;
            var rotation = 0d;
            var voiceSounds = new List<string>();
            var voicePitch = 1d;
            var voiceEveryNthCharacter = 1;
            while (!Match(TokenKind.RightBrace))
            {
                var property = ExpectIdentifier("Ожидалось свойство персонажа.");
                if (Keyword(property, "name"))
                {
                    name = ExpectString("Ожидалось имя персонажа.");
                }
                else if (Keyword(property, "sprite"))
                {
                    sprite = ExpectAssetValue(
                        "Ожидался путь или @ссылка на спрайт.");
                }
                else if (Keyword(property, "voice"))
                {
                    voiceSounds.Add(ExpectAssetValue(
                        "Ожидался путь или @ссылка на voice-блип."));
                }
                else if (Keyword(property, "voice-pitch"))
                {
                    voicePitch = ExpectFloat("Ожидалась высота voice-блипа.");
                }
                else if (Keyword(property, "voice-every"))
                {
                    voiceEveryNthCharacter = ExpectInteger(
                        "Ожидалась частота voice-блипа.");
                }
                else if (Keyword(property, "position"))
                {
                    var value = ExpectIdentifier("Ожидалась позиция персонажа.");
                    position = value.Text.ToLowerInvariant() switch
                    {
                        "left" => CharacterPosition.Left,
                        "center" => CharacterPosition.Center,
                        "right" => CharacterPosition.Right,
                        _ => throw Error(
                            value,
                            "Позиция должна быть left, center или right."),
                    };
                }
                else if (Keyword(property, "placement"))
                {
                    Expect(TokenKind.LeftParenthesis, "Ожидалась «(» после placement.");
                    x = ExpectFloat("Ожидалась координата X.");
                    Expect(TokenKind.Comma, "Ожидалась запятая между координатами.");
                    y = ExpectFloat("Ожидалась координата Y.");
                    Expect(TokenKind.RightParenthesis, "Ожидалась «)» после координат.");
                    hasCustomTransform = true;
                }
                else if (Keyword(property, "scale"))
                {
                    scale = ExpectFloat("Ожидался масштаб персонажа.");
                    hasCustomTransform = true;
                }
                else if (Keyword(property, "rotation"))
                {
                    rotation = ExpectFloat("Ожидался угол поворота персонажа.");
                    hasCustomTransform = true;
                }
                else
                {
                    throw Error(
                        property,
                        $"Неизвестное свойство персонажа «{property.Text}».");
                }
            }
            return new CharacterPlacement
            {
                Id = id.Text,
                Name = name,
                Sprite = sprite,
                Position = position,
                HasCustomTransform = hasCustomTransform,
                X = x,
                Y = y,
                Scale = scale,
                Rotation = rotation,
                VoiceSound = voiceSounds.FirstOrDefault() ?? string.Empty,
                VoiceSounds = voiceSounds,
                VoicePitch = voicePitch,
                VoiceEveryNthCharacter = voiceEveryNthCharacter,
            };
        }

        private CharacterDeclaration ParseCharacterDeclaration()
        {
            var token = Current;
            return new CharacterDeclaration(ParseCharacter(), token);
        }

        private OutputDeclaration ParseOutput()
        {
            var output = new OutputDeclaration
            {
                Label = ExpectString("Ожидался текст перехода."),
                TargetId = string.Empty,
            };
            Expect(TokenKind.Arrow, "Ожидалась стрелка «->».");
            output.TargetId = ExpectIdentifier("Ожидался id целевой ноды или none.").Text;
            if (!Match(TokenKind.LeftBrace))
            {
                return output;
            }

            while (!Match(TokenKind.RightBrace))
            {
                var property = ExpectIdentifier("Ожидалось свойство перехода.");
                if (Keyword(property, "when"))
                {
                    output.Condition = ExpectString("Ожидалось условие перехода.");
                }
                else if (Keyword(property, "script"))
                {
                    output.Script = ExpectString("Ожидался скрипт перехода.");
                }
                else if (Keyword(property, "sound"))
                {
                    output.Sound = ExpectAssetValue(
                        "Ожидался путь или @ссылка на звук.");
                }
                else if (Keyword(property, "fade"))
                {
                    output.FadeDurationMs = ExpectInteger(
                        "Ожидалась длительность затухания в миллисекундах.");
                }
                else
                {
                    throw Error(
                        property,
                        $"Неизвестное свойство перехода «{property.Text}».");
                }
            }
            return output;
        }

        private string ExpectString(string message) =>
            Expect(TokenKind.String, message).Text;

        private string ExpectAssetValue(string message)
        {
            if (Check(TokenKind.String))
            {
                return Expect(TokenKind.String, message).Text;
            }
            if (Match(TokenKind.At))
            {
                var id = ExpectIdentifier("Ожидался id ассета после @.");
                return AssetReference.Create(id.Text);
            }
            throw Error(Current, message);
        }

        private bool ExpectBoolean()
        {
            var token = ExpectIdentifier("Ожидалось true или false.");
            if (bool.TryParse(token.Text, out var value))
            {
                return value;
            }
            throw Error(token, "Ожидалось true или false.");
        }

        private int ExpectInteger(string message)
        {
            var token = Expect(TokenKind.Number, message);
            if (int.TryParse(
                token.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value))
            {
                return value;
            }
            throw Error(token, message);
        }

        private float ExpectFloat(string message)
        {
            var token = Expect(TokenKind.Number, message);
            if (float.TryParse(
                token.Text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
            {
                return value;
            }
            throw Error(token, message);
        }

        private void ExpectKeyword(string keyword)
        {
            var token = ExpectIdentifier($"Ожидалось ключевое слово {keyword}.");
            if (!Keyword(token, keyword))
            {
                throw Error(token, $"Ожидалось ключевое слово {keyword}.");
            }
        }

        private bool MatchKeyword(string keyword)
        {
            if (Current.Kind == TokenKind.Identifier && Keyword(Current, keyword))
            {
                _position++;
                return true;
            }
            return false;
        }

        private Token ExpectIdentifier(string message) =>
            Expect(TokenKind.Identifier, message);

        private Token Expect(TokenKind kind, string message)
        {
            if (!Check(kind))
            {
                throw Error(Current, message);
            }
            return _tokens[_position++];
        }

        private bool Match(TokenKind kind)
        {
            if (!Check(kind))
            {
                return false;
            }
            _position++;
            return true;
        }

        private bool Check(TokenKind kind) =>
            Current.Kind == kind;

        private Token Current => _tokens[_position];

        private static bool Keyword(Token token, string value) =>
            token.Text.Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Lexer
    {
        private readonly string _source;
        private readonly List<Token> _tokens = [];
        private int _index;
        private int _line = 1;
        private int _column = 1;

        public Lexer(string source)
        {
            _source = source;
        }

        public List<Token> Tokenize()
        {
            while (!AtEnd)
            {
                SkipTrivia();
                if (AtEnd)
                {
                    break;
                }

                var line = _line;
                var column = _column;
                var current = Peek();
                if (IsIdentifierStart(current))
                {
                    ReadIdentifier(line, column);
                }
                else if (char.IsDigit(current)
                    || current == '-' && char.IsDigit(Peek(1)))
                {
                    ReadNumber(line, column);
                }
                else if (current == '"')
                {
                    ReadString(line, column);
                }
                else
                {
                    ReadSymbol(line, column);
                }
            }
            _tokens.Add(new Token(TokenKind.End, string.Empty, _line, _column));
            return _tokens;
        }

        private void SkipTrivia()
        {
            while (!AtEnd)
            {
                if (char.IsWhiteSpace(Peek()))
                {
                    Advance();
                    continue;
                }
                if (Peek() == '#')
                {
                    SkipLine();
                    continue;
                }
                if (Peek() == '/' && Peek(1) == '/')
                {
                    Advance();
                    Advance();
                    SkipLine();
                    continue;
                }
                break;
            }
        }

        private void SkipLine()
        {
            while (!AtEnd && Peek() != '\n')
            {
                Advance();
            }
        }

        private void ReadIdentifier(int line, int column)
        {
            var start = _index;
            Advance();
            while (!AtEnd && IsIdentifierPart(Peek()))
            {
                Advance();
            }
            Add(
                TokenKind.Identifier,
                _source[start.._index],
                line,
                column);
        }

        private void ReadNumber(int line, int column)
        {
            var start = _index;
            if (Peek() == '-')
            {
                Advance();
            }
            while (!AtEnd && char.IsDigit(Peek()))
            {
                Advance();
            }
            if (!AtEnd && Peek() == '.')
            {
                Advance();
                while (!AtEnd && char.IsDigit(Peek()))
                {
                    Advance();
                }
            }
            Add(TokenKind.Number, _source[start.._index], line, column);
        }

        private void ReadString(int line, int column)
        {
            if (Peek(1) == '"' && Peek(2) == '"')
            {
                Advance();
                Advance();
                Advance();
                var contentStart = _index;
                while (!AtEnd
                    && !(Peek() == '"' && Peek(1) == '"' && Peek(2) == '"'))
                {
                    Advance();
                }
                if (AtEnd)
                {
                    throw new ProjectLanguageException(
                        "Незакрытая многострочная строка.",
                        line,
                        column);
                }
                var value = _source[contentStart.._index].Replace("\r\n", "\n");
                Advance();
                Advance();
                Advance();
                Add(TokenKind.String, value, line, column);
                return;
            }

            var start = _index;
            Advance();
            var escaped = false;
            while (!AtEnd)
            {
                var current = Peek();
                if (!escaped && current == '"')
                {
                    Advance();
                    var source = _source[start.._index];
                    try
                    {
                        Add(
                            TokenKind.String,
                            JsonSerializer.Deserialize<string>(source) ?? string.Empty,
                            line,
                            column);
                        return;
                    }
                    catch (JsonException)
                    {
                        throw new ProjectLanguageException(
                            "Некорректная строка.",
                            line,
                            column);
                    }
                }
                escaped = !escaped && current == '\\';
                if (current != '\\')
                {
                    escaped = false;
                }
                Advance();
            }
            throw new ProjectLanguageException(
                "Незакрытая строка.",
                line,
                column);
        }

        private void ReadSymbol(int line, int column)
        {
            var current = Advance();
            switch (current)
            {
                case '{':
                    Add(TokenKind.LeftBrace, "{", line, column);
                    break;
                case '}':
                    Add(TokenKind.RightBrace, "}", line, column);
                    break;
                case '(':
                    Add(TokenKind.LeftParenthesis, "(", line, column);
                    break;
                case ')':
                    Add(TokenKind.RightParenthesis, ")", line, column);
                    break;
                case ':':
                    Add(TokenKind.Colon, ":", line, column);
                    break;
                case ',':
                    Add(TokenKind.Comma, ",", line, column);
                    break;
                case '@':
                    Add(TokenKind.At, "@", line, column);
                    break;
                case '-' when Peek() == '>':
                    Advance();
                    Add(TokenKind.Arrow, "->", line, column);
                    break;
                default:
                    throw new ProjectLanguageException(
                        $"Неожиданный символ «{current}».",
                        line,
                        column);
            }
        }

        private void Add(TokenKind kind, string text, int line, int column) =>
            _tokens.Add(new Token(kind, text, line, column));

        private char Advance()
        {
            var current = _source[_index++];
            if (current == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
            return current;
        }

        private char Peek(int offset = 0)
        {
            var position = _index + offset;
            return position >= _source.Length ? '\0' : _source[position];
        }

        private bool AtEnd => _index >= _source.Length;

        private static bool IsIdentifierStart(char value) =>
            value == '_' || char.IsLetter(value);

        private static bool IsIdentifierPart(char value) =>
            value is '_' or '-' || char.IsLetterOrDigit(value);
    }

    private sealed record Token(TokenKind Kind, string Text, int Line, int Column);

    private enum TokenKind
    {
        Identifier,
        String,
        Number,
        LeftBrace,
        RightBrace,
        LeftParenthesis,
        RightParenthesis,
        Colon,
        Comma,
        At,
        Arrow,
        End,
    }
}
