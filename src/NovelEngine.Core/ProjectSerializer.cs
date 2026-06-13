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
        return project;
    }

    public static void Save(NovelProject project, string path)
    {
        project.Validate();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";

        using (var stream = File.Create(temporaryPath))
        {
            JsonSerializer.Serialize(stream, project, Options);
        }

        File.Move(temporaryPath, fullPath, true);
    }

    public static string ToJson(NovelProject project)
    {
        project.Validate();
        return JsonSerializer.Serialize(project, Options);
    }

    public static NovelProject FromJson(string json)
    {
        var project = JsonSerializer.Deserialize<NovelProject>(json, Options)
            ?? throw new InvalidDataException("JSON проекта пуст.");
        project.Validate();
        return project;
    }
}
