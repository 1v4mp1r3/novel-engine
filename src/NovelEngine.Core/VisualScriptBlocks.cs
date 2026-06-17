using System.Text;

namespace NovelEngine.Core;

public enum VisualScriptBlockKind
{
    SetVariable,
    AddVariable,
    UnsetVariable,
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

public static class VisualScriptCompiler
{
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
            VisualScriptBlockKind.AddVariable =>
                $"add {RequiredVariableName(block)} {RequiredValue(block)}",
            VisualScriptBlockKind.UnsetVariable =>
                $"unset {RequiredVariableName(block)}",
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

    private static string DisplayBlockId(VisualScriptBlock block) =>
        string.IsNullOrWhiteSpace(block.Id)
            ? block.Kind.ToString()
            : $"«{block.Id}»";
}
