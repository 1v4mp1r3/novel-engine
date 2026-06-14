using System.IO;
using NovelEngine.Core;

namespace NovelEngine.Editor;

internal static class ProjectWorkspace
{
    public static string CreateProjectInDirectory(string directory)
    {
        var workspaceDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(workspaceDirectory);

        var project = NovelProject.CreateDefault();
        var title = Path.GetFileName(
            workspaceDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(title))
        {
            project.Title = title;
        }

        var projectPath = GetAvailableProjectPath(workspaceDirectory);
        ProjectAssets.EnsureDefaultStructure(project, projectPath);
        Directory.CreateDirectory(Path.Combine(workspaceDirectory, "autosaves"));
        ProjectSerializer.Save(project, projectPath);
        return projectPath;
    }

    private static string GetAvailableProjectPath(string workspaceDirectory)
    {
        var baseName = MakeSafeFileStem(Path.GetFileName(
            workspaceDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)));
        if (baseName.Length == 0)
        {
            baseName = "NovelProject";
        }

        var preferred = Path.Combine(workspaceDirectory, $"{baseName}.novel.json");
        if (!File.Exists(preferred))
        {
            return preferred;
        }

        var suffix = 2;
        while (true)
        {
            var candidate = Path.Combine(
                workspaceDirectory,
                $"{baseName}-{suffix++}.novel.json");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string MakeSafeFileStem(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(
            source.Select(character =>
                invalid.Contains(character) ? '_' : character)).Trim();
    }
}
