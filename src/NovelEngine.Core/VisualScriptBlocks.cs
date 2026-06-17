using System.Globalization;
using System.Text;

namespace NovelEngine.Core;

public enum VisualScriptBlockKind
{
    SetVariable,
    AddVariable,
    SubtractVariable,
    MultiplyVariable,
    DivideVariable,
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

public static class VisualScriptBlockOperations
{
    public static VisualScriptBlock CloneWithNewId(VisualScriptBlock block)
    {
        var clone = block.Clone();
        clone.Id = $"block-{Guid.NewGuid():N}";
        return clone;
    }

    public static IReadOnlyList<VisualScriptBlock> CloneForPaste(
        IEnumerable<VisualScriptBlock> blocks) =>
        blocks.Select(CloneWithNewId).ToList();
}

public static class VisualScriptCompiler
{
    public static IReadOnlyList<VisualScriptBlock> ParseScript(string script)
    {
        var blocks = new List<VisualScriptBlock>();
        var index = 1;
        foreach (var sourceLine in script.Replace("\r", string.Empty).Split('\n'))
        {
            if (!NovelScriptCommands.TryParseLine(sourceLine, out var command))
            {
                if (command.Source.Length > 0)
                {
                    throw new InvalidDataException(
                        $"Команда не поддерживается visual blocks: {command.Source}");
                }
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Comment)
            {
                blocks.Add(
                    new VisualScriptBlock
                    {
                        Id = $"imported-{index++}",
                        Kind = VisualScriptBlockKind.Comment,
                        Text = command.Comment,
                    });
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Set)
            {
                blocks.Add(
                    new VisualScriptBlock
                    {
                        Id = $"imported-{index++}",
                        Kind = command.Value.Equals("true", StringComparison.OrdinalIgnoreCase)
                            ? VisualScriptBlockKind.SetFlagTrue
                            : command.Value.Equals("false", StringComparison.OrdinalIgnoreCase)
                                ? VisualScriptBlockKind.SetFlagFalse
                                : VisualScriptBlockKind.SetVariable,
                        VariableName = command.Name,
                        Value = command.Value,
                    });
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Add)
            {
                var value = command.Value;
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
                        VariableName = command.Name,
                        Value = value,
                    });
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Multiply)
            {
                blocks.Add(
                    new VisualScriptBlock
                    {
                        Id = $"imported-{index++}",
                        Kind = VisualScriptBlockKind.MultiplyVariable,
                        VariableName = command.Name,
                        Value = command.Value,
                    });
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Divide)
            {
                blocks.Add(
                    new VisualScriptBlock
                    {
                        Id = $"imported-{index++}",
                        Kind = VisualScriptBlockKind.DivideVariable,
                        VariableName = command.Name,
                        Value = command.Value,
                    });
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Unset)
            {
                blocks.Add(
                    new VisualScriptBlock
                    {
                        Id = $"imported-{index++}",
                        Kind = VisualScriptBlockKind.UnsetVariable,
                        VariableName = command.Name,
                    });
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Toggle)
            {
                blocks.Add(
                    new VisualScriptBlock
                    {
                        Id = $"imported-{index++}",
                        Kind = VisualScriptBlockKind.ToggleVariable,
                        VariableName = command.Name,
                    });
                continue;
            }

            throw new InvalidDataException(
                $"Команда не поддерживается visual blocks: {command.Source}");
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

    public static string Describe(VisualScriptBlock block) =>
        block.Kind switch
        {
            VisualScriptBlockKind.SetVariable =>
                $"{RequiredVariableName(block)} = {RequiredValue(block)}",
            VisualScriptBlockKind.SetFlagTrue =>
                $"Флаг {RequiredVariableName(block)}: включить",
            VisualScriptBlockKind.SetFlagFalse =>
                $"Флаг {RequiredVariableName(block)}: выключить",
            VisualScriptBlockKind.AddVariable =>
                $"{RequiredVariableName(block)} += {RequiredValue(block)}",
            VisualScriptBlockKind.SubtractVariable =>
                $"{RequiredVariableName(block)} -= {RequiredPositiveNumber(block)}",
            VisualScriptBlockKind.MultiplyVariable =>
                $"{RequiredVariableName(block)} *= {RequiredValue(block)}",
            VisualScriptBlockKind.DivideVariable =>
                $"{RequiredVariableName(block)} /= {RequiredNonZeroNumber(block)}",
            VisualScriptBlockKind.UnsetVariable =>
                $"Удалить переменную {RequiredVariableName(block)}",
            VisualScriptBlockKind.ToggleVariable =>
                $"Переключить флаг {RequiredVariableName(block)}",
            VisualScriptBlockKind.Comment =>
                block.Text.Trim().Length == 0
                    ? "Пустой комментарий"
                    : $"Комментарий: {block.Text.Trim()}",
            _ => throw new InvalidDataException(
                $"Неизвестный тип visual script block: {block.Kind}"),
        };

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
            VisualScriptBlockKind.MultiplyVariable =>
                $"multiply {RequiredVariableName(block)} {RequiredValue(block)}",
            VisualScriptBlockKind.DivideVariable =>
                $"divide {RequiredVariableName(block)} {RequiredNonZeroNumber(block)}",
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

    private static string RequiredNonZeroNumber(VisualScriptBlock block)
    {
        var value = RequiredValue(block);
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number)
            || number == 0)
        {
            throw new InvalidDataException(
                $"В блоке {DisplayBlockId(block)} нужно указать число не равное 0.");
        }

        return number.ToString("G", CultureInfo.InvariantCulture);
    }

    private static string DisplayBlockId(VisualScriptBlock block) =>
        string.IsNullOrWhiteSpace(block.Id)
            ? block.Kind.ToString()
            : $"«{block.Id}»";

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
