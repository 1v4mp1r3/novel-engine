namespace NovelEngine.Core;

public enum ProjectDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ProjectDiagnostic(
    ProjectDiagnosticSeverity Severity,
    string Location,
    string Message);

public sealed class ProjectDiagnosticReport
{
    public ProjectDiagnosticReport(IReadOnlyList<ProjectDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<ProjectDiagnostic> Diagnostics { get; }

    public int ErrorCount =>
        Diagnostics.Count(diagnostic => diagnostic.Severity == ProjectDiagnosticSeverity.Error);

    public int WarningCount =>
        Diagnostics.Count(diagnostic => diagnostic.Severity == ProjectDiagnosticSeverity.Warning);

    public int InfoCount =>
        Diagnostics.Count(diagnostic => diagnostic.Severity == ProjectDiagnosticSeverity.Info);

    public bool HasErrors => ErrorCount > 0;
}

public static class ProjectDiagnostics
{
    public static ProjectDiagnosticReport Analyze(
        NovelProject project,
        string? projectPath = null)
    {
        var diagnostics = new List<ProjectDiagnostic>();
        AddStructuralValidation(project, diagnostics);
        AddScriptDiagnostics(project, diagnostics);
        AddGraphDiagnostics(project, diagnostics);
        AddAssetDiagnostics(project, diagnostics, projectPath);
        return new ProjectDiagnosticReport(diagnostics);
    }

    private static void AddStructuralValidation(
        NovelProject project,
        List<ProjectDiagnostic> diagnostics)
    {
        try
        {
            project.Validate();
        }
        catch (Exception error) when (
            error is InvalidDataException
            or InvalidOperationException)
        {
            diagnostics.Add(
                new ProjectDiagnostic(
                    ProjectDiagnosticSeverity.Error,
                    "Проект",
                    error.Message));
        }
    }

    private static void AddScriptDiagnostics(
        NovelProject project,
        List<ProjectDiagnostic> diagnostics)
    {
        foreach (var node in project.Nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Script))
            {
                TryAddScriptError(
                    diagnostics,
                    $"Нода «{DisplayNode(node)}», скрипт входа",
                    () => NovelScript.Execute(node.Script, new ScriptState()));
            }

            foreach (var output in node.Outputs)
            {
                if (!string.IsNullOrWhiteSpace(output.Condition))
                {
                    TryAddScriptError(
                        diagnostics,
                        $"Нода «{DisplayNode(node)}», условие «{output.Label}»",
                        () => NovelScript.Evaluate(output.Condition, new ScriptState()));
                }
                if (!string.IsNullOrWhiteSpace(output.Script))
                {
                    TryAddScriptError(
                        diagnostics,
                        $"Нода «{DisplayNode(node)}», скрипт «{output.Label}»",
                        () => NovelScript.Execute(output.Script, new ScriptState()));
                }
            }
        }
    }

    private static void TryAddScriptError(
        List<ProjectDiagnostic> diagnostics,
        string location,
        Action validate)
    {
        try
        {
            validate();
        }
        catch (Exception error) when (
            error is InvalidDataException
            or InvalidOperationException)
        {
            diagnostics.Add(
                new ProjectDiagnostic(
                    ProjectDiagnosticSeverity.Error,
                    location,
                    error.Message));
        }
    }

    private static void AddGraphDiagnostics(
        NovelProject project,
        List<ProjectDiagnostic> diagnostics)
    {
        var uniqueNodesById = project.Nodes
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

        foreach (var node in project.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Title))
            {
                diagnostics.Add(
                    Warning($"Нода {node.Id}", "У ноды не указано название."));
            }
            if (node.Kind is NodeKind.Scene or NodeKind.Dialogue
                && string.IsNullOrWhiteSpace(node.Text))
            {
                diagnostics.Add(
                    Warning(
                        $"Нода «{DisplayNode(node)}»",
                        "Текст сцены или реплики пустой."));
            }
            if (node.Kind == NodeKind.Dialogue
                && string.IsNullOrWhiteSpace(node.Speaker))
            {
                diagnostics.Add(
                    Warning(
                        $"Нода «{DisplayNode(node)}»",
                        "У диалога не указан говорящий."));
            }

            foreach (var character in node.Characters)
            {
                if (string.IsNullOrWhiteSpace(character.Name))
                {
                    diagnostics.Add(
                        Warning(
                            $"Нода «{DisplayNode(node)}», персонаж {character.Id}",
                            "У персонажа не указано имя."));
                }
                if (string.IsNullOrWhiteSpace(character.Sprite))
                {
                    diagnostics.Add(
                        Warning(
                            $"Нода «{DisplayNode(node)}», персонаж «{DisplayCharacter(character)}»",
                            "У персонажа не выбран спрайт."));
                }
            }

            foreach (var output in node.Outputs)
            {
                if (output.TargetNodeId is null)
                {
                    diagnostics.Add(
                        Warning(
                            $"Нода «{DisplayNode(node)}», выход «{output.Label}»",
                            "Выход не подключён к целевой ноде."));
                }
                else if (!uniqueNodesById.ContainsKey(output.TargetNodeId))
                {
                    diagnostics.Add(
                        new ProjectDiagnostic(
                            ProjectDiagnosticSeverity.Error,
                            $"Нода «{DisplayNode(node)}», выход «{output.Label}»",
                            $"Целевая нода «{output.TargetNodeId}» не найдена."));
                }
            }
        }

        AddUnreachableNodeDiagnostics(project, diagnostics, uniqueNodesById);
    }

    private static void AddUnreachableNodeDiagnostics(
        NovelProject project,
        List<ProjectDiagnostic> diagnostics,
        IReadOnlyDictionary<string, NovelNode> uniqueNodesById)
    {
        var start = project.Nodes.SingleOrDefault(node => node.Kind == NodeKind.Start);
        if (start is null || !uniqueNodesById.ContainsKey(start.Id))
        {
            return;
        }

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<NovelNode>();
        queue.Enqueue(start);
        reachable.Add(start.Id);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var targetId in node.Outputs.Select(output => output.TargetNodeId))
            {
                if (targetId is null
                    || !uniqueNodesById.TryGetValue(targetId, out var target)
                    || !reachable.Add(target.Id))
                {
                    continue;
                }
                queue.Enqueue(target);
            }
        }

        foreach (var node in project.Nodes.Where(node => !reachable.Contains(node.Id)))
        {
            diagnostics.Add(
                Warning(
                    $"Нода «{DisplayNode(node)}»",
                    "До этой ноды нет пути от стартовой ноды."));
        }
    }

    private static void AddAssetDiagnostics(
        NovelProject project,
        List<ProjectDiagnostic> diagnostics,
        string? projectPath)
    {
        foreach (var asset in project.Assets)
        {
            if (project.CountAssetReferences(asset.Id) == 0)
            {
                diagnostics.Add(
                    new ProjectDiagnostic(
                        ProjectDiagnosticSeverity.Info,
                        $"Ассет @{asset.Id}",
                        "Ассет пока нигде не используется."));
            }
            if (projectPath is null)
            {
                continue;
            }

            try
            {
                var path = ProjectAssets.ResolvePath(projectPath, asset);
                if (!File.Exists(path))
                {
                    diagnostics.Add(
                        new ProjectDiagnostic(
                            ProjectDiagnosticSeverity.Error,
                            $"Ассет @{asset.Id}",
                            $"Файл не найден: {path}"));
                }
            }
            catch (Exception error) when (
                error is IOException
                or ArgumentException
                or NotSupportedException
                or UnauthorizedAccessException)
            {
                diagnostics.Add(
                    new ProjectDiagnostic(
                        ProjectDiagnosticSeverity.Error,
                        $"Ассет @{asset.Id}",
                        error.Message));
            }
        }
    }

    private static ProjectDiagnostic Warning(string location, string message) =>
        new(ProjectDiagnosticSeverity.Warning, location, message);

    private static string DisplayNode(NovelNode node) =>
        string.IsNullOrWhiteSpace(node.Title) ? node.Id : node.Title;

    private static string DisplayCharacter(CharacterPlacement character) =>
        string.IsNullOrWhiteSpace(character.Name) ? character.Id : character.Name;
}
