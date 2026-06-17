using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovelEngine.Core;

public sealed class ScriptState
{
    public Dictionary<string, object?> Variables { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string CurrentBackground { get; set; } = string.Empty;
    public string CurrentMusic { get; set; } = string.Empty;
    public List<CharacterPlacement> CurrentCharacters { get; } = [];

    public void Reset()
    {
        Variables.Clear();
        CurrentBackground = string.Empty;
        CurrentMusic = string.Empty;
        CurrentCharacters.Clear();
    }
}

public static partial class NovelScript
{
    public static void Execute(string script, ScriptState state)
    {
        foreach (var sourceLine in script.Replace("\r", string.Empty).Split('\n'))
        {
            if (!NovelScriptCommands.TryParseLine(sourceLine, out var command))
            {
                if (command.Source.Length > 0)
                {
                    throw new InvalidDataException(
                        $"Неизвестная команда языка: {command.Source}");
                }
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Comment)
            {
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Set)
            {
                state.Variables[command.Name] = ParseLiteral(command.Value);
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Add)
            {
                ApplyNumericCommand(command, state, (current, operand) => current + operand);
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Multiply)
            {
                ApplyNumericCommand(command, state, (current, operand) => current * operand);
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Divide)
            {
                ApplyNumericCommand(
                    command,
                    state,
                    (current, operand) =>
                    {
                        if (operand == 0)
                        {
                            throw new InvalidDataException(
                                $"Команда divide не может делить на 0: {command.Source}");
                        }
                        return current / operand;
                    });
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Unset)
            {
                state.Variables.Remove(command.Name);
                continue;
            }

            if (command.Kind == NovelScriptCommandKind.Toggle)
            {
                state.Variables[command.Name] =
                    !IsTruthy(GetVariable(command.Name, state));
                continue;
            }

            throw new InvalidDataException(
                $"Неизвестная команда языка: {command.Source}");
        }
    }

    public static bool Evaluate(string condition, ScriptState state)
    {
        condition = condition.Trim();
        if (condition.Length == 0)
        {
            return true;
        }

        if (condition.StartsWith('!') && Identifier().IsMatch(condition[1..]))
        {
            return !IsTruthy(GetVariable(condition[1..], state));
        }

        if (Identifier().IsMatch(condition))
        {
            return IsTruthy(GetVariable(condition, state));
        }

        var match = Comparison().Match(condition);
        if (!match.Success)
        {
            throw new InvalidDataException($"Некорректное условие: {condition}");
        }

        var left = GetVariable(match.Groups["name"].Value, state);
        var right = ParseLiteral(match.Groups["value"].Value);
        var operation = match.Groups["operator"].Value;

        if (TryNumber(left, out var leftNumber) && TryNumber(right, out var rightNumber))
        {
            return operation switch
            {
                "==" => leftNumber == rightNumber,
                "!=" => leftNumber != rightNumber,
                ">" => leftNumber > rightNumber,
                ">=" => leftNumber >= rightNumber,
                "<" => leftNumber < rightNumber,
                "<=" => leftNumber <= rightNumber,
                _ => false,
            };
        }

        var comparison = string.Compare(
            Convert.ToString(left, CultureInfo.InvariantCulture),
            Convert.ToString(right, CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
        return operation switch
        {
            "==" => comparison == 0,
            "!=" => comparison != 0,
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            _ => false,
        };
    }

    private static object? GetVariable(string name, ScriptState state) =>
        state.Variables.TryGetValue(name, out var value) ? value : null;

    private static void ApplyNumericCommand(
        NovelScriptCommand command,
        ScriptState state,
        Func<double, double, double> operation)
    {
        var operandValue = ParseLiteral(command.Value);
        if (!TryNumber(operandValue, out var operand))
        {
            throw new InvalidDataException(
                $"Команда {command.Kind.ToString().ToLowerInvariant()} ожидает число: {command.Source}");
        }

        var current = 0d;
        if (state.Variables.TryGetValue(command.Name, out var existing)
            && !TryNumber(existing, out current))
        {
            throw new InvalidDataException(
                $"Переменная «{command.Name}» не является числом для {command.Kind.ToString().ToLowerInvariant()}.");
        }

        state.Variables[command.Name] = operation(current, operand);
    }

    private static object? ParseLiteral(string source)
    {
        var value = source.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return JsonSerializer.Deserialize<string>(value);
        }

        if (bool.TryParse(value, out var boolean))
        {
            return boolean;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }

    private static bool TryNumber(object? value, out double number)
    {
        try
        {
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return value is not null && value is not bool;
        }
        catch (Exception) when (value is null or string)
        {
            number = 0;
            return false;
        }
    }

    private static bool IsTruthy(object? value) =>
        value switch
        {
            null => false,
            bool boolean => boolean,
            string text => text.Length > 0,
            _ when TryNumber(value, out var number) => number != 0,
            _ => true,
        };

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(
        "^(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*(?<operator>==|!=|>=|<=|>|<)\\s*(?<value>.+)$")]
    private static partial Regex Comparison();
}
