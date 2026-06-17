using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovelEngine.Core;

public sealed class NovelBuildManifest
{
    public int BuildFormatVersion { get; set; } = 1;
    public string Title { get; set; } = string.Empty;
    public string RuntimeProjectFile { get; set; } = "game.novel.json";
    public string BuildId { get; set; } = string.Empty;
    public DateTimeOffset CompiledAtUtc { get; set; }
    public bool DebugSymbols { get; set; }
    public int NodeCount { get; set; }
    public int AssetCount { get; set; }
}

public sealed record NovelBuildResult(
    string OutputDirectory,
    string ManifestPath,
    string RuntimeProjectPath,
    NovelBuildManifest Manifest);

public static class NovelBuildCompiler
{
    public const string ManifestFileName = "manifest.json";
    public const string RuntimeProjectFileName = "game.novel.json";

    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static NovelBuildResult Compile(
        NovelProject project,
        string projectPath,
        string outputDirectory,
        bool debugSymbols)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullProjectPath))
        {
            throw new FileNotFoundException(
                "Сначала сохраните проект перед компиляцией.",
                fullProjectPath);
        }

        var source = string.IsNullOrWhiteSpace(project.SourceCode)
            ? ProjectLanguage.Format(project)
            : project.SourceCode;
        var runtimeProject = ProjectLanguage.Parse(source);
        CopyMainMenu(project.MainMenu, runtimeProject.MainMenu);
        runtimeProject.SourceCode = debugSymbols ? source : string.Empty;
        runtimeProject.Validate();

        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        ValidateOutputDirectory(fullOutputDirectory, fullProjectPath);
        var parentDirectory = Path.GetDirectoryName(fullOutputDirectory)
            ?? throw new InvalidDataException("Не удалось определить каталог сборки.");
        Directory.CreateDirectory(parentDirectory);
        var temporaryDirectory = Path.Combine(
            parentDirectory,
            $".{Path.GetFileName(fullOutputDirectory)}.tmp-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            var assetCount = CompileAssets(
                runtimeProject,
                Path.GetDirectoryName(fullProjectPath)!,
                temporaryDirectory);
            var runtimeProjectPath = Path.Combine(
                temporaryDirectory,
                RuntimeProjectFileName);
            ProjectSerializer.Save(runtimeProject, runtimeProjectPath);

            var runtimeJson = File.ReadAllText(runtimeProjectPath);
            var manifest = new NovelBuildManifest
            {
                Title = runtimeProject.Title,
                RuntimeProjectFile = RuntimeProjectFileName,
                BuildId = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(runtimeJson)))[..16],
                CompiledAtUtc = DateTimeOffset.UtcNow,
                DebugSymbols = debugSymbols,
                NodeCount = runtimeProject.Nodes.Count,
                AssetCount = assetCount,
            };
            var manifestPath = Path.Combine(
                temporaryDirectory,
                ManifestFileName);
            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, ManifestOptions));

            ReplaceExistingBuild(temporaryDirectory, fullOutputDirectory);

            return new NovelBuildResult(
                fullOutputDirectory,
                Path.Combine(fullOutputDirectory, ManifestFileName),
                Path.Combine(fullOutputDirectory, RuntimeProjectFileName),
                manifest);
        }
        catch
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
            throw;
        }
    }

    private static void CopyMainMenu(
        MainMenuDesign source,
        MainMenuDesign destination)
    {
        destination.Background = source.Background;
        destination.Elements.Clear();
        destination.Elements.AddRange(
            source.Elements.Select(element => element.Clone()));
    }

    public static NovelBuildManifest LoadManifest(string manifestPath)
    {
        using var stream = File.OpenRead(manifestPath);
        var manifest = JsonSerializer.Deserialize<NovelBuildManifest>(
                stream,
                ManifestOptions)
            ?? throw new InvalidDataException("Манифест сборки пуст.");
        if (manifest.BuildFormatVersion != 1
            || string.IsNullOrWhiteSpace(manifest.RuntimeProjectFile))
        {
            throw new InvalidDataException(
                "Версия или содержимое игрового build-пакета не поддерживается.");
        }
        return manifest;
    }

    public static (NovelBuildManifest Manifest, NovelProject Project)
        LoadBuild(string manifestPath)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var manifest = LoadManifest(fullManifestPath);
        var buildDirectory = Path.GetDirectoryName(fullManifestPath)!;
        var runtimeProjectPath = Path.GetFullPath(
            Path.Combine(buildDirectory, manifest.RuntimeProjectFile));
        if (!IsInside(runtimeProjectPath, buildDirectory))
        {
            throw new InvalidDataException(
                "Манифест ссылается на файл за пределами build-пакета.");
        }
        return (manifest, ProjectSerializer.Load(runtimeProjectPath));
    }

    private static int CompileAssets(
        NovelProject project,
        string projectDirectory,
        string outputDirectory)
    {
        var copiedPaths = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in project.Assets)
        {
            var sourcePath = ResolveSourcePath(projectDirectory, asset.Path);
            var folder = asset.Folder.Length > 0
                ? asset.Folder
                : asset.Kind.ToString().ToLowerInvariant();
            asset.Path = CopyAsset(
                sourcePath,
                folder,
                outputDirectory,
                copiedPaths,
                usedTargets);
        }

        CompileMainMenuAssets(
            project.MainMenu,
            projectDirectory,
            outputDirectory,
            copiedPaths,
            usedTargets);

        foreach (var character in project.Characters)
        {
            CompileCharacterAssets(
                character,
                projectDirectory,
                outputDirectory,
                copiedPaths,
                usedTargets);
        }

        foreach (var type in project.NodeTypes)
        {
            type.Defaults.Background = CompileDirectValue(
                type.Defaults.Background,
                projectDirectory,
                outputDirectory,
                copiedPaths,
                usedTargets);
            type.Defaults.Music = CompileDirectValue(
                type.Defaults.Music,
                projectDirectory,
                outputDirectory,
                copiedPaths,
                usedTargets);
            foreach (var character in type.Defaults.Characters)
            {
                CompileCharacterAssets(
                    character,
                    projectDirectory,
                    outputDirectory,
                    copiedPaths,
                    usedTargets);
            }
        }

        foreach (var node in project.Nodes)
        {
            node.Background = CompileDirectValue(
                node.Background,
                projectDirectory,
                outputDirectory,
                copiedPaths,
                usedTargets) ?? string.Empty;
            node.Music = CompileDirectValue(
                node.Music,
                projectDirectory,
                outputDirectory,
                copiedPaths,
                usedTargets) ?? string.Empty;
            foreach (var character in node.Characters)
            {
                CompileCharacterAssets(
                    character,
                    projectDirectory,
                    outputDirectory,
                    copiedPaths,
                    usedTargets);
            }
            foreach (var output in node.Outputs)
            {
                output.TransitionSound = CompileDirectValue(
                    output.TransitionSound,
                    projectDirectory,
                    outputDirectory,
                    copiedPaths,
                    usedTargets) ?? string.Empty;
            }
        }
        return copiedPaths.Count;
    }

    private static void CompileMainMenuAssets(
        MainMenuDesign mainMenu,
        string projectDirectory,
        string outputDirectory,
        IDictionary<string, string> copiedPaths,
        ISet<string> usedTargets)
    {
        mainMenu.Background = CompileDirectValue(
            mainMenu.Background,
            projectDirectory,
            outputDirectory,
            copiedPaths,
            usedTargets) ?? string.Empty;
        foreach (var element in mainMenu.Elements)
        {
            element.Image = CompileDirectValue(
                element.Image,
                projectDirectory,
                outputDirectory,
                copiedPaths,
                usedTargets) ?? string.Empty;
        }
    }

    private static void CompileCharacterAssets(
        CharacterPlacement character,
        string projectDirectory,
        string outputDirectory,
        IDictionary<string, string> copiedPaths,
        ISet<string> usedTargets)
    {
        character.Sprite = CompileDirectValue(
            character.Sprite,
            projectDirectory,
            outputDirectory,
            copiedPaths,
            usedTargets) ?? string.Empty;
        character.VoiceSound = CompileDirectValue(
            character.VoiceSound,
            projectDirectory,
            outputDirectory,
            copiedPaths,
            usedTargets) ?? string.Empty;
        CompileDirectValues(
            character.VoiceSounds,
            projectDirectory,
            outputDirectory,
            copiedPaths,
            usedTargets);
    }

    private static void CompileDirectValues(
        List<string> values,
        string projectDirectory,
        string outputDirectory,
        IDictionary<string, string> copiedPaths,
        ISet<string> usedTargets)
    {
        for (var index = 0; index < values.Count; index++)
        {
            values[index] = CompileDirectValue(
                values[index],
                projectDirectory,
                outputDirectory,
                copiedPaths,
                usedTargets) ?? string.Empty;
        }
        values.RemoveAll(string.IsNullOrWhiteSpace);
    }

    private static string? CompileDirectValue(
        string? value,
        string projectDirectory,
        string outputDirectory,
        IDictionary<string, string> copiedPaths,
        ISet<string> usedTargets)
    {
        if (string.IsNullOrWhiteSpace(value)
            || AssetReference.TryGetId(value, out _))
        {
            return value;
        }
        return CopyAsset(
            ResolveSourcePath(projectDirectory, value),
            "external",
            outputDirectory,
            copiedPaths,
            usedTargets);
    }

    private static string CopyAsset(
        string sourcePath,
        string folder,
        string outputDirectory,
        IDictionary<string, string> copiedPaths,
        ISet<string> usedTargets)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException(
                $"Ассет для сборки не найден: {sourcePath}",
                fullSourcePath);
        }
        if (copiedPaths.TryGetValue(fullSourcePath, out var existing))
        {
            return existing;
        }

        var safeFolder = ProjectAssets.NormalizeFolder(folder);
        var fileName = Path.GetFileName(fullSourcePath);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var index = 1;
        var relativePath = BuildAssetPath(safeFolder, fileName);
        while (!usedTargets.Add(relativePath))
        {
            relativePath = BuildAssetPath(
                safeFolder,
                $"{stem}-{index++}{extension}");
        }

        var targetPath = Path.Combine(
            outputDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(fullSourcePath, targetPath, overwrite: true);
        copiedPaths[fullSourcePath] = relativePath;
        return relativePath;
    }

    private static string BuildAssetPath(string folder, string fileName) =>
        folder.Length == 0
            ? $"assets/{fileName}"
            : $"assets/{folder}/{fileName}";

    private static string ResolveSourcePath(
        string projectDirectory,
        string path) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(projectDirectory, path));

    private static void ValidateOutputDirectory(
        string outputDirectory,
        string projectPath)
    {
        var root = Path.GetPathRoot(outputDirectory);
        if (string.Equals(
            outputDirectory.TrimEnd(Path.DirectorySeparatorChar),
            root?.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase)
            || IsInside(projectPath, outputDirectory))
        {
            throw new InvalidDataException(
                "Каталог сборки не может быть корнем диска или содержать исходный проект.");
        }
        if (Directory.Exists(outputDirectory))
        {
            var manifestPath = Path.Combine(outputDirectory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException(
                    "Существующий каталог не является build-пакетом Novel Engine и не будет перезаписан.");
            }
            _ = LoadManifest(manifestPath);
        }
    }

    private static void ReplaceExistingBuild(
        string temporaryDirectory,
        string outputDirectory)
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.Move(temporaryDirectory, outputDirectory);
            return;
        }

        var backupDirectory =
            $"{outputDirectory}.old-{Guid.NewGuid():N}";
        Directory.Move(outputDirectory, backupDirectory);
        try
        {
            Directory.Move(temporaryDirectory, outputDirectory);
            Directory.Delete(backupDirectory, recursive: true);
        }
        catch
        {
            if (!Directory.Exists(outputDirectory)
                && Directory.Exists(backupDirectory))
            {
                Directory.Move(backupDirectory, outputDirectory);
            }
            throw;
        }
    }

    private static bool IsInside(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative != ".."
            && !relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }
}
