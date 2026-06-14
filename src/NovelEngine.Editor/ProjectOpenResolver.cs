using System.IO;

namespace NovelEngine.Editor;

internal static class ProjectOpenResolver
{
    private const string ProjectPattern = "*.novel.json";

    public static string ResolveProjectPath(string inputPath)
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
            return fullPath;
        }
        if (Directory.Exists(fullPath))
        {
            return ResolveProjectFromDirectory(fullPath);
        }

        throw new FileNotFoundException(
            $"Путь проекта не найден: {fullPath}",
            fullPath);
    }

    private static string ResolveProjectFromDirectory(string directory)
    {
        var projectFiles = Directory
            .EnumerateFiles(directory, ProjectPattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (projectFiles.Count == 0)
        {
            throw new InvalidDataException(
                $"В папке '{directory}' не найден файл проекта {ProjectPattern}.");
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
            return preferredProject;
        }

        if (projectFiles.Count == 1)
        {
            return projectFiles[0];
        }

        throw new InvalidDataException(
            "В папке найдено несколько файлов проекта. "
            + $"Ожидался один {ProjectPattern} или файл '{preferredName}'.");
    }
}
