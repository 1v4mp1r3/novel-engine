using System.IO;

namespace NovelEngine.Editor;

internal sealed record ProjectOpenResult(
    string? ProjectPath,
    string WorkspaceDirectory,
    bool CreatedEmptyWorkspace);

internal static class ProjectOpenResolver
{
    private const string ProjectPattern = "*.novel.json";

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
        var projectFiles = Directory
            .EnumerateFiles(directory, ProjectPattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        var preferredName = $"{directoryName}.novel.json";
        var preferredProject = projectFiles.FirstOrDefault(path =>
            Path.GetFileName(path).Equals(
                preferredName,
                StringComparison.OrdinalIgnoreCase));
        if (preferredProject is not null)
        {
            return new ProjectOpenResult(
                preferredProject,
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
}
