using System.Text.RegularExpressions;

namespace NovelEngine.Core;

public enum VisualConditionKind
{
    Always,
    VariableTrue,
    VariableFalse,
    Comparison,
    All,
    Any,
}

public sealed class VisualConditionExpression
{
    public VisualConditionKind Kind { get; set; }
    public string VariableName { get; set; } = string.Empty;
    public string Operator { get; set; } = "==";
    public string Value { get; set; } = string.Empty;
    public List<VisualConditionExpression> Children { get; init; } = [];

    public VisualConditionExpression Clone() =>
        new()
        {
            Kind = Kind,
            VariableName = VariableName,
            Operator = Operator,
            Value = Value,
            Children = Children.Select(child => child.Clone()).ToList(),
        };
}

public static partial class VisualConditionCompiler
{
    public static readonly IReadOnlyList<string> ComparisonOperators =
        ["==", "!=", ">=", "<=", ">", "<"];

    public static VisualConditionExpression Parse(string condition)
    {
        condition = condition.Trim();
        if (condition.Length == 0)
        {
            return new VisualConditionExpression();
        }

        var anyParts = SplitLogical(condition, "||");
        if (anyParts.Count > 1)
        {
            return CreateGroup(VisualConditionKind.Any, anyParts);
        }

        var allParts = SplitLogical(condition, "&&");
        if (allParts.Count > 1)
        {
            return CreateGroup(VisualConditionKind.All, allParts);
        }

        if (Identifier().IsMatch(condition))
        {
            return new VisualConditionExpression
            {
                Kind = VisualConditionKind.VariableTrue,
                VariableName = condition,
            };
        }

        if (condition.StartsWith('!')
            && Identifier().IsMatch(condition[1..]))
        {
            return new VisualConditionExpression
            {
                Kind = VisualConditionKind.VariableFalse,
                VariableName = condition[1..],
            };
        }

        var match = Comparison().Match(condition);
        if (match.Success)
        {
            return new VisualConditionExpression
            {
                Kind = VisualConditionKind.Comparison,
                VariableName = match.Groups["name"].Value,
                Operator = match.Groups["operator"].Value,
                Value = match.Groups["value"].Value.Trim(),
            };
        }

        throw new InvalidDataException($"Некорректное условие: {condition}");
    }

    public static string Compile(VisualConditionExpression expression) =>
        expression.Kind switch
        {
            VisualConditionKind.Always => string.Empty,
            VisualConditionKind.VariableTrue =>
                RequiredVariableName(expression),
            VisualConditionKind.VariableFalse =>
                $"!{RequiredVariableName(expression)}",
            VisualConditionKind.Comparison =>
                $"{RequiredVariableName(expression)} {RequiredOperator(expression)} {RequiredValue(expression)}",
            VisualConditionKind.All =>
                string.Join(
                    " && ",
                    RequiredChildren(expression).Select(child =>
                        CompileChild(expression.Kind, child))),
            VisualConditionKind.Any =>
                string.Join(
                    " || ",
                    RequiredChildren(expression).Select(child =>
                        CompileChild(expression.Kind, child))),
            _ => throw new InvalidDataException(
                $"Неизвестный тип visual condition: {expression.Kind}"),
        };

    public static void Validate(VisualConditionExpression expression)
    {
        var condition = Compile(expression);
        _ = NovelScript.Evaluate(condition, new ScriptState());
    }

    public static bool Evaluate(string condition, ScriptState state) =>
        Evaluate(Parse(condition), state);

    public static bool Evaluate(NodeOutput output, ScriptState state) =>
        output.ConditionExpression is null
            ? Evaluate(output.Condition, state)
            : Evaluate(output.ConditionExpression, state);

    public static bool Evaluate(VisualConditionExpression expression, ScriptState state) =>
        expression.Kind switch
        {
            VisualConditionKind.All =>
                RequiredChildren(expression).All(child => Evaluate(child, state)),
            VisualConditionKind.Any =>
                RequiredChildren(expression).Any(child => Evaluate(child, state)),
            _ => NovelScript.Evaluate(Compile(expression), state),
        };

    public static string Compile(NodeOutput output) =>
        output.ConditionExpression is null
            ? output.Condition.Trim()
            : Compile(output.ConditionExpression);

    public static void SyncTextFromExpression(NodeOutput output)
    {
        if (output.ConditionExpression is not null)
        {
            output.Condition = Compile(output.ConditionExpression);
        }
    }

    private static string RequiredVariableName(VisualConditionExpression expression)
    {
        var name = expression.VariableName.Trim();
        if (!Identifier().IsMatch(name))
        {
            throw new InvalidDataException(
                "Имя переменной должно начинаться с латинской буквы или _ и содержать только латиницу, цифры и _.");
        }
        return name;
    }

    private static string RequiredOperator(VisualConditionExpression expression)
    {
        var operation = expression.Operator.Trim();
        if (!ComparisonOperators.Contains(operation, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"Некорректный оператор условия: {operation}");
        }
        return operation;
    }

    private static string RequiredValue(VisualConditionExpression expression)
    {
        var value = expression.Value.Trim();
        if (value.Length == 0)
        {
            throw new InvalidDataException("Укажите значение для сравнения.");
        }
        return value;
    }

    private static IReadOnlyList<VisualConditionExpression> RequiredChildren(
        VisualConditionExpression expression)
    {
        if (expression.Children.Count == 0)
        {
            throw new InvalidDataException("Группа условий должна содержать хотя бы одно условие.");
        }
        return expression.Children;
    }

    private static string CompileChild(
        VisualConditionKind parentKind,
        VisualConditionExpression child)
    {
        if (parentKind is VisualConditionKind.All
            && child.Kind is VisualConditionKind.Any)
        {
            throw new InvalidDataException(
                "OR-группа внутри AND пока не поддерживается без скобок.");
        }
        return Compile(child);
    }

    private static VisualConditionExpression CreateGroup(
        VisualConditionKind kind,
        IReadOnlyList<string> parts)
    {
        var expression = new VisualConditionExpression { Kind = kind };
        foreach (var part in parts)
        {
            expression.Children.Add(Parse(part));
        }
        return expression;
    }

    private static List<string> SplitLogical(string condition, string operation)
    {
        var parts = new List<string>();
        var start = 0;
        var inString = false;
        var escaped = false;
        for (var index = 0; index <= condition.Length - operation.Length; index++)
        {
            var current = condition[index];
            if (inString && current == '\\' && !escaped)
            {
                escaped = true;
                continue;
            }
            if (current == '"' && !escaped)
            {
                inString = !inString;
            }
            escaped = false;

            if (inString
                || !condition.AsSpan(index, operation.Length)
                    .SequenceEqual(operation))
            {
                continue;
            }

            parts.Add(condition[start..index].Trim());
            index += operation.Length - 1;
            start = index + 1;
        }

        if (parts.Count == 0)
        {
            return [];
        }
        parts.Add(condition[start..].Trim());
        if (parts.Any(part => part.Length == 0))
        {
            throw new InvalidDataException($"Некорректное условие: {condition}");
        }
        return parts;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(
        "^(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*(?<operator>==|!=|>=|<=|>|<)\\s*(?<value>.+)$")]
    private static partial Regex Comparison();
}
