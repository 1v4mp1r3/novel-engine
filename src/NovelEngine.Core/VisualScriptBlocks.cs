using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NovelEngine.Core;

public enum VisualScriptBlockKind
{
    SetVariable,
    AddVariable,
    SubtractVariable,
    UnsetVariable,
    ToggleVariable,
    SetFlagTrue,
    SetFlagFalse,
    Comment,
}

public sealed class VisualScriptBlock
{
    public string Id { get; set; } = string.Empty;
    public VisualScriptBlockKind Kind { get; set; }
    public string VariableName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    public VisualScriptBlock Clone() =>
        new()
        {
            Id = Id,
            Kind = Kind,
            VariableName = VariableName,
            Value = Value,
            Text = Text,
        };
}

public static partial class VisualScriptCompiler
{
    public static IReadOnlyList<VisualScriptBlock> ParseScript(string script)
    {
        var blocks = new List<VisualScriptBlock>();
        var index = 1;
        foreach (var sourceLine in script.Replace("\r", string.Empty).Split('\n'))
        {
            var line = sourceLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryReadComment(line, out var comment))
            {
                blocks.Add(
                    new VisualScriptBlock
                    {
                        Id = $"imported-{index++}",
                        Kind = VisualScriptBlockKind.Comment,
                        Text = comment,
                    });
                continue;
            }

            var setMatch = SetCommand().Match(line);
            if (setMatch.Success)
            {
                var value = setMatch.Groups["value"].Value.Trim();
                blocks.Add(
                    new VisualScriptBlock
                    {
                        Id = $"imported-{index++}",
                        Kind = value.Equals("true", StringComparison.OrdinalIgnoreCase)
                            ? VisualScriptBlockKind.SetFlagTrue
                            : value.Equals("false", StringComparison.OrdinalIgnoreCase)
                                ? VisualScriptBlockKind.SetFlagFalse
                                : VisualScriptBlockKind.SetVariable,
                        VariableName = setMatch.Groups["name"].Value,
                        Value = value,
                    });
                continue;
            }

            var addMatch = AddCommand().Match(line);
            if (addMatch.Success)
            {
                var value = addMatch.Groups["value"].Value.Trim();
                var kind = VisualScriptBlockKind.AddVariable;
                if (double.TryParse(
                        value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var number)
                    && number < 0)
                {
                    kind = VisualScriptBlockKind.SubtractVariable;
                    value = (-number).ToString("G", CultureInfo.InvariantCulture);
                }

                blocks.Add(
                    new VisualScriptBlock
                    {
                        Id = $"imported-{index++}",
                        Kind = kind,
                        VariableName = addMatch.Groups["name"].Value,
                        Value = value,
                    });
                continue;
            }

            var unsetMatch = UnsetCommand().Match(line);
            if (unsetMatch.Success)
            {
                blocks.Add(
                    new VisualScriptBlock
                    {
                        Id = $"imported-{index++}",
                        Kind = VisualScriptBlockKind.UnsetVariable,
                        VariableName = unsetMatch.Groups["name"].Value,
                    });
                continue;
            }

            var toggleMatch = ToggleCommand().Match(line);
            if (toggleMatch.Success)
            {
                blocks.Add(
                    new VisualScriptBlock
                    {
                        Id = $"imported-{index++}",
                        Kind = VisualScriptBlockKind.ToggleVariable,
                        VariableName = toggleMatch.Groups["name"].Value,
                    });
                continue;
            }

            throw new InvalidDataException(
                $"Команда не поддерживается visual blocks: {line}");
        }

        Validate(blocks);
        return blocks;
    }

    public static string Compile(IEnumerable<VisualScriptBlock> blocks)
    {
        var builder = new StringBuilder();
        foreach (var block in blocks)
        {
            var line = CompileBlock(block);
            if (line.Length > 0)
            {
                builder.AppendLine(line);
            }
        }
        return builder.ToString().TrimEnd();
    }

    public static void Execute(
        string script,
        IEnumerable<VisualScriptBlock> blocks,
        ScriptState state)
    {
        NovelScript.Execute(script, state);
        var blockScript = Compile(blocks);
        if (blockScript.Length > 0)
        {
            NovelScript.Execute(blockScript, state);
        }
    }

    public static void Validate(IEnumerable<VisualScriptBlock> blocks)
    {
        var script = Compile(blocks);
        if (script.Length > 0)
        {
            NovelScript.Execute(script, new ScriptState());
        }
    }

    private static string CompileBlock(VisualScriptBlock block) =>
        block.Kind switch
        {
            VisualScriptBlockKind.SetVariable =>
                $"set {RequiredVariableName(block)} = {RequiredValue(block)}",
            VisualScriptBlockKind.SetFlagTrue =>
                $"set {RequiredVariableName(block)} = true",
            VisualScriptBlockKind.SetFlagFalse =>
                $"set {RequiredVariableName(block)} = false",
            VisualScriptBlockKind.AddVariable =>
                $"add {RequiredVariableName(block)} {RequiredValue(block)}",
            VisualScriptBlockKind.SubtractVariable =>
                $"add {RequiredVariableName(block)} -{RequiredPositiveNumber(block)}",
            VisualScriptBlockKind.UnsetVariable =>
                $"unset {RequiredVariableName(block)}",
            VisualScriptBlockKind.ToggleVariable =>
                $"toggle {RequiredVariableName(block)}",
            VisualScriptBlockKind.Comment =>
                block.Text.Trim().Length == 0
                    ? string.Empty
                    : $"# {block.Text.Trim()}",
            _ => throw new InvalidDataException(
                $"Неизвестный тип visual script block: {block.Kind}"),
        };

    private static string RequiredVariableName(VisualScriptBlock block)
    {
        var name = block.VariableName.Trim();
        if (name.Length == 0)
        {
            throw new InvalidDataException(
                $"В блоке {DisplayBlockId(block)} не указано имя переменной.");
        }
        return name;
    }

    private static string RequiredValue(VisualScriptBlock block)
    {
        var value = block.Value.Trim();
        if (value.Length == 0)
        {
            throw new InvalidDataException(
                $"В блоке {DisplayBlockId(block)} не указано значение.");
        }
        return value;
    }

    private static string RequiredPositiveNumber(VisualScriptBlock block)
    {
        var value = RequiredValue(block);
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number)
            || number < 0)
        {
            throw new InvalidDataException(
                $"В блоке {DisplayBlockId(block)} нужно указать положительное число.");
        }

        return number.ToString("G", CultureInfo.InvariantCulture);
    }

    private static string DisplayBlockId(VisualScriptBlock block) =>
        string.IsNullOrWhiteSpace(block.Id)
            ? block.Kind.ToString()
            : $"«{block.Id}»";

    private static bool TryReadComment(string line, out string comment)
    {
        if (line.StartsWith('#'))
        {
            comment = line[1..].Trim();
            return true;
        }
        if (line.StartsWith("//", StringComparison.Ordinal))
        {
            comment = line[2..].Trim();
            return true;
        }

        comment = string.Empty;
        return false;
    }

    [GeneratedRegex(
        "^set\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(?<value>.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SetCommand();

    [GeneratedRegex(
        "^add\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s+(?<value>.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex AddCommand();

    [GeneratedRegex(
        "^unset\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex UnsetCommand();

    [GeneratedRegex(
        "^toggle\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ToggleCommand();
}

public static class VisualScriptBlockPreserver
{
    public static void PreserveFrom(NovelProject source, NovelProject target)
    {
        var sourceNodes = source.Nodes.ToDictionary(
            node => node.Id,
            StringComparer.Ordinal);

        foreach (var targetNode in target.Nodes)
        {
            if (!sourceNodes.TryGetValue(targetNode.Id, out var sourceNode))
            {
                continue;
            }

            CopyBlocksIfEmpty(sourceNode.ScriptBlocks, targetNode.ScriptBlocks);
            PreserveOutputBlocks(sourceNode, targetNode);
        }
    }

    private static void PreserveOutputBlocks(
        NovelNode sourceNode,
        NovelNode targetNode)
    {
        var usedSourceOutputs = new HashSet<NodeOutput>();
        for (var index = 0; index < targetNode.Outputs.Count; index++)
        {
            var targetOutput = targetNode.Outputs[index];
            var sourceOutput = FindMatchingOutput(
                sourceNode,
                targetOutput,
                index,
                usedSourceOutputs);
            if (sourceOutput is null)
            {
                continue;
            }

            usedSourceOutputs.Add(sourceOutput);
            PreserveConditionExpression(sourceOutput, targetOutput);
            CopyBlocksIfEmpty(
                sourceOutput.ScriptBlocks,
                targetOutput.ScriptBlocks);
        }
    }

    private static void PreserveConditionExpression(
        NodeOutput sourceOutput,
        NodeOutput targetOutput)
    {
        if (sourceOutput.ConditionExpression is null
            || targetOutput.ConditionExpression is not null
            || !VisualConditionCompiler.Compile(sourceOutput).Equals(
                targetOutput.Condition,
                StringComparison.Ordinal))
        {
            return;
        }

        targetOutput.ConditionExpression = sourceOutput.ConditionExpression.Clone();
    }

    private static NodeOutput? FindMatchingOutput(
        NovelNode sourceNode,
        NodeOutput targetOutput,
        int targetIndex,
        ISet<NodeOutput> usedSourceOutputs)
    {
        var match = sourceNode.Outputs.FirstOrDefault(output =>
            !usedSourceOutputs.Contains(output)
            && output.Id.Equals(targetOutput.Id, StringComparison.Ordinal));
        if (match is not null)
        {
            return match;
        }

        match = sourceNode.Outputs.FirstOrDefault(output =>
            !usedSourceOutputs.Contains(output)
            && output.Label.Equals(
                targetOutput.Label,
                StringComparison.Ordinal)
            && string.Equals(
                output.TargetNodeId,
                targetOutput.TargetNodeId,
                StringComparison.Ordinal));
        if (match is not null)
        {
            return match;
        }

        match = sourceNode.Outputs.FirstOrDefault(output =>
            !usedSourceOutputs.Contains(output)
            && output.Label.Equals(
                targetOutput.Label,
                StringComparison.Ordinal));
        if (match is not null)
        {
            return match;
        }

        return targetIndex < sourceNode.Outputs.Count
            && !usedSourceOutputs.Contains(sourceNode.Outputs[targetIndex])
                ? sourceNode.Outputs[targetIndex]
                : null;
    }

    private static void CopyBlocksIfEmpty(
        IEnumerable<VisualScriptBlock> sourceBlocks,
        ICollection<VisualScriptBlock> targetBlocks)
    {
        if (targetBlocks.Count > 0)
        {
            return;
        }

        foreach (var block in sourceBlocks)
        {
            targetBlocks.Add(block.Clone());
        }
    }
}
