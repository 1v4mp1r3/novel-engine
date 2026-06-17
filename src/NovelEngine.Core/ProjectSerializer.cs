using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovelEngine.Core;

public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static NovelProject Load(string path)
    {
        using var stream = File.OpenRead(path);
        var project = JsonSerializer.Deserialize<NovelProject>(stream, Options)
            ?? throw new InvalidDataException("Файл проекта пуст.");
        project.Validate();
        project.FormatVersion = 5;
        return project;
    }

    public static void Save(NovelProject project, string path)
    {
        project.FormatVersion = 5;
        project.Validate();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";

        var completed = false;
        try
        {
            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, project, Options);
            }

            File.Move(temporaryPath, fullPath, true);
            completed = true;
        }
        finally
        {
            if (!completed && File.Exists(temporaryPath))
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    public static string ToJson(NovelProject project)
    {
        project.FormatVersion = 5;
        project.Validate();
        return JsonSerializer.Serialize(project, Options);
    }

    public static NovelProject FromJson(string json)
    {
        var project = JsonSerializer.Deserialize<NovelProject>(json, Options)
            ?? throw new InvalidDataException("JSON проекта пуст.");
        project.Validate();
        project.FormatVersion = 5;
        return project;
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (
            error is IOException
            or UnauthorizedAccessException)
        {
            // Best-effort cleanup after a failed save.
        }
    }
}
