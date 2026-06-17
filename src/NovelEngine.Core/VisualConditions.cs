using System.Text.RegularExpressions;

namespace NovelEngine.Core;

public enum VisualConditionKind
{
    Always,
    VariableTrue,
    VariableFalse,
    Comparison,
}

public sealed class VisualConditionExpression
{
    public VisualConditionKind Kind { get; set; }
    public string VariableName { get; set; } = string.Empty;
    public string Operator { get; set; } = "==";
    public string Value { get; set; } = string.Empty;

    public VisualConditionExpression Clone() =>
        new()
        {
            Kind = Kind,
            VariableName = VariableName,
            Operator = Operator,
            Value = Value,
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
            _ => throw new InvalidDataException(
                $"Неизвестный тип visual condition: {expression.Kind}"),
        };

    public static void Validate(VisualConditionExpression expression)
    {
        var condition = Compile(expression);
        _ = NovelScript.Evaluate(condition, new ScriptState());
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

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(
        "^(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*(?<operator>==|!=|>=|<=|>|<)\\s*(?<value>.+)$")]
    private static partial Regex Comparison();
}
