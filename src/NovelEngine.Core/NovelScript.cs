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
            var line = sourceLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var setMatch = SetCommand().Match(line);
            if (setMatch.Success)
            {
                state.Variables[setMatch.Groups["name"].Value] =
                    ParseLiteral(setMatch.Groups["value"].Value);
                continue;
            }

            var addMatch = AddCommand().Match(line);
            if (addMatch.Success)
            {
                var name = addMatch.Groups["name"].Value;
                var amount = Convert.ToDouble(
                    ParseLiteral(addMatch.Groups["value"].Value),
                    CultureInfo.InvariantCulture);
                var current = state.Variables.TryGetValue(name, out var existing)
                    ? Convert.ToDouble(existing, CultureInfo.InvariantCulture)
                    : 0d;
                state.Variables[name] = current + amount;
                continue;
            }

            var unsetMatch = UnsetCommand().Match(line);
            if (unsetMatch.Success)
            {
                state.Variables.Remove(unsetMatch.Groups["name"].Value);
                continue;
            }

            throw new InvalidDataException($"Неизвестная команда языка: {line}");
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

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(
        "^(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*(?<operator>==|!=|>=|<=|>|<)\\s*(?<value>.+)$")]
    private static partial Regex Comparison();
}
