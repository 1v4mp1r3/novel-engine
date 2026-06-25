using System.IO;
using NovelEngine.Core;

namespace NovelEngine.Editor;

internal static class ProjectWorkspace
{
    private static readonly HashSet<char> InvalidFileNameChars = new(
        Path.GetInvalidFileNameChars());

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
        var autoSaveDirectory = Path.Combine(workspaceDirectory, "autosaves");
        Directory.CreateDirectory(autoSaveDirectory);
        ProjectSerializer.Save(project, projectPath);
        WriteInitialAutoSaveSnapshot(project, projectPath, autoSaveDirectory);
        return projectPath;
    }

    public static string GetAvailableProjectPath(string workspaceDirectory)
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

        var buffer = new char[source.Length];
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            buffer[index] = InvalidFileNameChars.Contains(character)
                ? '_'
                : character;
        }

        return new string(buffer).Trim();
    }

    private static void WriteInitialAutoSaveSnapshot(
        NovelProject project,
        string projectPath,
        string autoSaveDirectory)
    {
        var stem = Path.GetFileNameWithoutExtension(projectPath);
        var snapshotPath = AutoSaveStore.CreateSnapshotPath(
            autoSaveDirectory,
            stem,
            DateTime.UtcNow);
        ProjectSerializer.Save(project, snapshotPath);
        AutoSaveStore.Prune(autoSaveDirectory, stem, keepCount: 24);
    }
}
