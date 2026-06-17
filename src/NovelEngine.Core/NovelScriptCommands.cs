using System.Text.RegularExpressions;

namespace NovelEngine.Core;

internal enum NovelScriptCommandKind
{
    Comment,
    Set,
    Add,
    Unset,
    Toggle,
}

internal sealed record NovelScriptCommand(
    NovelScriptCommandKind Kind,
    string Source,
    string Name = "",
    string Value = "",
    string Comment = "");

internal static partial class NovelScriptCommands
{
    public static bool TryParseLine(string sourceLine, out NovelScriptCommand command)
    {
        var line = sourceLine.Trim();
        if (line.Length == 0)
        {
            command = new NovelScriptCommand(NovelScriptCommandKind.Comment, string.Empty);
            return false;
        }

        if (TryReadComment(line, out var comment))
        {
            command = new NovelScriptCommand(
                NovelScriptCommandKind.Comment,
                line,
                Comment: comment);
            return true;
        }

        var setMatch = SetCommand().Match(line);
        if (setMatch.Success)
        {
            command = new NovelScriptCommand(
                NovelScriptCommandKind.Set,
                line,
                setMatch.Groups["name"].Value,
                setMatch.Groups["value"].Value.Trim());
            return true;
        }

        var addMatch = AddCommand().Match(line);
        if (addMatch.Success)
        {
            command = new NovelScriptCommand(
                NovelScriptCommandKind.Add,
                line,
                addMatch.Groups["name"].Value,
                addMatch.Groups["value"].Value.Trim());
            return true;
        }

        var unsetMatch = UnsetCommand().Match(line);
        if (unsetMatch.Success)
        {
            command = new NovelScriptCommand(
                NovelScriptCommandKind.Unset,
                line,
                unsetMatch.Groups["name"].Value);
            return true;
        }

        var toggleMatch = ToggleCommand().Match(line);
        if (toggleMatch.Success)
        {
            command = new NovelScriptCommand(
                NovelScriptCommandKind.Toggle,
                line,
                toggleMatch.Groups["name"].Value);
            return true;
        }

        command = new NovelScriptCommand(NovelScriptCommandKind.Comment, line);
        return false;
    }

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
