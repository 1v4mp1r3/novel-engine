using System.Text.RegularExpressions;

namespace NovelEngine.Core;

public static partial class ProjectScriptVariables
{
    public static IReadOnlyList<string> Collect(NovelProject project)
    {
        var variables = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in project.NodeTypes)
        {
            AddScript(type.Defaults.Script, variables);
        }
        foreach (var node in project.Nodes)
        {
            AddScript(node.Script, variables);
            AddBlocks(node.ScriptBlocks, variables);
            foreach (var output in node.Outputs)
            {
                AddCondition(output, variables);
                AddScript(output.Script, variables);
                AddBlocks(output.ScriptBlocks, variables);
            }
        }

        return variables.ToList();
    }

    private static void AddScript(string? script, ISet<string> variables)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return;
        }

        foreach (var sourceLine in script.Replace("\r", string.Empty).Split('\n'))
        {
            var line = sourceLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var match = ScriptCommandVariable().Match(line);
            if (match.Success)
            {
                variables.Add(match.Groups["name"].Value);
            }
        }
    }

    private static void AddCondition(NodeOutput output, ISet<string> variables)
    {
        if (output.ConditionExpression is not null)
        {
            AddCondition(output.ConditionExpression, variables);
        }
        AddCondition(output.Condition, variables);
    }

    private static void AddCondition(
        VisualConditionExpression expression,
        ISet<string> variables)
    {
        var variable = expression.VariableName.Trim();
        if ((expression.Kind is VisualConditionKind.VariableTrue
                or VisualConditionKind.VariableFalse
                or VisualConditionKind.Comparison)
            && Identifier().IsMatch(variable))
        {
            variables.Add(variable);
        }
    }

    private static void AddCondition(string condition, ISet<string> variables)
    {
        condition = condition.Trim();
        if (condition.Length == 0)
        {
            return;
        }

        if (condition.StartsWith('!')
            && Identifier().IsMatch(condition[1..]))
        {
            variables.Add(condition[1..]);
            return;
        }
        if (Identifier().IsMatch(condition))
        {
            variables.Add(condition);
            return;
        }

        var match = ComparisonVariable().Match(condition);
        if (match.Success)
        {
            variables.Add(match.Groups["name"].Value);
        }
    }

    private static void AddBlocks(
        IEnumerable<VisualScriptBlock> blocks,
        ISet<string> variables)
    {
        foreach (var block in blocks)
        {
            var variable = block.VariableName.Trim();
            if (variable.Length > 0)
            {
                variables.Add(variable);
            }
        }
    }

    [GeneratedRegex(
        "^(?:set|add|unset)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ScriptCommandVariable();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(
        "^(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*(?:==|!=|>=|<=|>|<)\\s*.+$")]
    private static partial Regex ComparisonVariable();
}
