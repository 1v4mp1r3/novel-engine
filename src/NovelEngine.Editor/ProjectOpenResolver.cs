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
        var directoryName = Path.GetFileName(directory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var preferredProject = AddJsonProjectFiles(
            directory,
            directoryName,
            projectFiles,
            novelProjectFiles);
        if (projectFiles.Count == 0)
        {
            return new ProjectOpenResult(
                null,
                directory,
                CreatedEmptyWorkspace: true);
        }

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

        throw new InvalidDataException(
            $"В папке найдено несколько JSON-проектов: {directory}. "
            + "Откройте конкретный .novel.json файл.");
    }

    private static string? AddJsonProjectFiles(
        string directory,
        string directoryName,
        List<string> projectFiles,
        List<string> novelProjectFiles)
    {
        var preferredNovelName = $"{directoryName}.novel.json";
        var preferredJsonName = $"{directoryName}.json";
        string? preferredNovelProject = null;
        string? preferredJsonProject = null;
        foreach (var path in Directory.EnumerateFiles(
            directory,
            "*.json",
            SearchOption.TopDirectoryOnly))
        {
            projectFiles.Add(path);
            var fileName = Path.GetFileName(path);
            if (fileName.Equals(
                    preferredNovelName,
                    StringComparison.OrdinalIgnoreCase))
            {
                preferredNovelProject = path;
            }
            else if (fileName.Equals(
                         preferredJsonName,
                         StringComparison.OrdinalIgnoreCase))
            {
                preferredJsonProject = path;
            }

            if (IsNovelProjectFile(path))
            {
                novelProjectFiles.Add(path);
            }
        }

        return preferredNovelProject ?? preferredJsonProject;
    }

    private static bool IsNovelProjectFile(string path) =>
        Path.GetFileName(path).EndsWith(
            ".novel.json",
            StringComparison.OrdinalIgnoreCase);
}
