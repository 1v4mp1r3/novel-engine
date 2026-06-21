using System.IO;

namespace NovelEngine.Editor;

internal sealed record ProjectOpenResult(
    string? ProjectPath,
    string WorkspaceDirectory,
    bool CreatedEmptyWorkspace);

internal static class ProjectOpenResolver
{
    public static ProjectOpenResult Resolve(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new InvalidDataException("Путь к проекту пуст.");
        }

        var expandedPath = Environment.ExpandEnvironmentVariables(
            inputPath.Trim().Trim('"'));
        var fullPath = Path.GetFullPath(expandedPath);
        if (File.Exists(fullPath))
        {
            return new ProjectOpenResult(
                fullPath,
                Path.GetDirectoryName(fullPath)!,
                CreatedEmptyWorkspace: false);
        }
        if (Directory.Exists(fullPath))
        {
            return ResolveProjectFromDirectory(fullPath);
        }

        throw new FileNotFoundException(
            $"Путь проекта не найден: {fullPath}",
            fullPath);
    }

    private static ProjectOpenResult ResolveProjectFromDirectory(string directory)
    {
        directory = Path.GetFullPath(directory);
        var projectFiles = new List<string>();
        var novelProjectFiles = new List<string>();
        AddJsonProjectFiles(directory, projectFiles, novelProjectFiles);
        if (projectFiles.Count == 0)
        {
            return new ProjectOpenResult(
                null,
                directory,
                CreatedEmptyWorkspace: true);
        }

        var directoryName = Path.GetFileName(directory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var preferredProject = PreferredProject(projectFiles, directoryName);
        if (preferredProject is not null)
        {
            return new ProjectOpenResult(
                preferredProject,
                directory,
                CreatedEmptyWorkspace: false);
        }

        if (novelProjectFiles.Count == 1)
        {
            return new ProjectOpenResult(
                novelProjectFiles[0],
                directory,
                CreatedEmptyWorkspace: false);
        }

        if (projectFiles.Count == 1)
        {
            return new ProjectOpenResult(
                projectFiles[0],
                directory,
                CreatedEmptyWorkspace: false);
        }

        return new ProjectOpenResult(
            null,
            directory,
            CreatedEmptyWorkspace: true);
    }

    private static void AddJsonProjectFiles(
        string directory,
        List<string> projectFiles,
        List<string> novelProjectFiles)
    {
        foreach (var path in Directory.EnumerateFiles(
            directory,
            "*.json",
            SearchOption.TopDirectoryOnly))
        {
            projectFiles.Add(path);
            if (IsNovelProjectFile(path))
            {
                novelProjectFiles.Add(path);
            }
        }

        projectFiles.Sort(StringComparer.OrdinalIgnoreCase);
        novelProjectFiles.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private static string? PreferredProject(
        IReadOnlyList<string> projectFiles,
        string directoryName)
    {
        var preferredNovelName = $"{directoryName}.novel.json";
        foreach (var path in projectFiles)
        {
            if (Path.GetFileName(path).Equals(
                preferredNovelName,
                StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        var preferredJsonName = $"{directoryName}.json";
        foreach (var path in projectFiles)
        {
            if (Path.GetFileName(path).Equals(
                preferredJsonName,
                StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return null;
    }

    private static bool IsNovelProjectFile(string path) =>
        Path.GetFileName(path).EndsWith(
            ".novel.json",
            StringComparison.OrdinalIgnoreCase);
}
