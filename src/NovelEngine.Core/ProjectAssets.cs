using System.Text;

namespace NovelEngine.Core;

public static class ProjectAssets
{
    public const string ManagedFilesDirectoryName = "files";
    public const string LegacyAssetsDirectoryName = "assets";

    public static NovelAsset Import(
        NovelProject project,
        string projectPath,
        string sourcePath,
        string? folder = null)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Файл ассета не найден.", source);
        }

        var existing = project.Assets.FirstOrDefault(
            asset => string.Equals(
                ResolvePath(projectPath, asset),
                source,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var projectDirectory = GetProjectDirectory(projectPath);
        var kind = AssetReference.GuessKind(source);
        folder = NormalizeFolder(
            folder ?? kind switch
            {
                AssetKind.Image => "images",
                AssetKind.Audio => "audio",
                _ => "other",
            });
        EnsureFolder(project, folder);
        var targetDirectory = Path.Combine(
            projectDirectory,
            ManagedFilesDirectoryName,
            folder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(targetDirectory);

        var fileName = Path.GetFileName(source);
        var target = Path.Combine(targetDirectory, fileName);
        var suffix = 2;
        while (File.Exists(target)
            && !string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            target = Path.Combine(
                targetDirectory,
                $"{Path.GetFileNameWithoutExtension(fileName)}-{suffix++}"
                + Path.GetExtension(fileName));
        }
        if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, target, overwrite: false);
        }

        var baseId = MakeId(Path.GetFileNameWithoutExtension(target));
        var id = baseId;
        suffix = 2;
        while (project.FindAsset(id) is not null)
        {
            id = $"{baseId}_{suffix++}";
        }

        var asset = new NovelAsset
        {
            Id = id,
            Kind = kind,
            Folder = folder,
            Path = Path.GetRelativePath(projectDirectory, target)
                .Replace('\\', '/'),
        };
        project.Assets.Add(asset);
        return asset;
    }

    public static string ResolvePath(string projectPath, NovelAsset asset)
    {
        if (Path.IsPathRooted(asset.Path))
        {
            return Path.GetFullPath(asset.Path);
        }
        return Path.GetFullPath(
            Path.Combine(GetProjectDirectory(projectPath), asset.Path));
    }

    public static string GetAssetsDirectory(string projectPath) =>
        Path.Combine(GetProjectDirectory(projectPath), ManagedFilesDirectoryName);

    public static int SyncFromDisk(NovelProject project, string projectPath)
    {
        var root = GetAssetsDirectory(projectPath);
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var changes = 0;
        foreach (var directory in Directory.EnumerateDirectories(
            root,
            "*",
            SearchOption.AllDirectories))
        {
            var folder = NormalizeFolder(
                Path.GetRelativePath(root, directory).Replace('\\', '/'));
            var before = project.AssetFolders.Count;
            EnsureFolder(project, folder);
            changes += project.AssetFolders.Count - before;
        }

        var projectDirectory = GetProjectDirectory(projectPath);
        foreach (var file in Directory.EnumerateFiles(
            root,
            "*",
            SearchOption.AllDirectories))
        {
            var relativeProjectPath = Path.GetRelativePath(projectDirectory, file)
                .Replace('\\', '/');
            var existing = project.Assets.FirstOrDefault(asset =>
                asset.Path.Equals(relativeProjectPath, StringComparison.OrdinalIgnoreCase)
                || ResolvePath(projectPath, asset).Equals(
                    Path.GetFullPath(file),
                    StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                continue;
            }

            var relativeFolder = Path.GetDirectoryName(
                    Path.GetRelativePath(root, file))
                ?.Replace('\\', '/')
                ?? string.Empty;
            var before = project.Assets.Count;
            _ = Import(project, projectPath, file, relativeFolder);
            changes += project.Assets.Count - before;
        }

        return changes;
    }

    public static void CreateFolder(NovelProject project, string folder)
    {
        folder = NormalizeFolder(folder);
        if (folder.Length == 0)
        {
            throw new InvalidDataException("Имя папки не может быть пустым.");
        }
        EnsureFolder(project, folder);
    }

    public static void RenameFolder(
        NovelProject project,
        string projectPath,
        string folder,
        string newName)
    {
        folder = NormalizeFolder(folder);
        newName = NormalizeFolder(newName);
        if (folder.Length == 0 || newName.Contains('/'))
        {
            throw new InvalidDataException("Укажите одно корректное имя папки.");
        }
        var parent = ParentFolder(folder);
        var target = parent.Length == 0 ? newName : $"{parent}/{newName}";
        if (project.AssetFolders.Any(
            candidate => candidate.Equals(target, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Папка с таким именем уже существует.");
        }

        var sourceDirectory = ExistingManagedFolderDirectory(projectPath, folder);
        var targetDirectory = Path.Combine(
            GetAssetsDirectory(projectPath),
            target.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(sourceDirectory))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory)!);
            Directory.Move(sourceDirectory, targetDirectory);
        }

        for (var index = 0; index < project.AssetFolders.Count; index++)
        {
            var candidate = NormalizeFolder(project.AssetFolders[index]);
            if (IsInFolder(candidate, folder))
            {
                project.AssetFolders[index] = target + candidate[folder.Length..];
            }
        }
        foreach (var asset in project.Assets)
        {
            var candidate = NormalizeFolder(asset.Folder);
            if (!IsInFolder(candidate, folder))
            {
                continue;
            }
            asset.Folder = target + candidate[folder.Length..];
            var relativeFile = Path.GetFileName(asset.Path);
            asset.Path = BuildManagedAssetPath(asset.Folder, relativeFile);
        }
    }

    public static void DeleteFolder(NovelProject project, string projectPath, string folder)
    {
        folder = NormalizeFolder(folder);
        if (project.Assets.Any(asset => IsInFolder(asset.Folder, folder)))
        {
            throw new InvalidOperationException(
                "Сначала переместите или удалите ассеты из этой папки.");
        }
        if (project.AssetFolders.Any(
            candidate => !candidate.Equals(folder, StringComparison.OrdinalIgnoreCase)
                && IsInFolder(candidate, folder)))
        {
            throw new InvalidOperationException(
                "Сначала удалите вложенные папки.");
        }
        project.AssetFolders.RemoveAll(
            candidate => candidate.Equals(folder, StringComparison.OrdinalIgnoreCase));
        foreach (var directory in ManagedFolderDirectories(projectPath, folder))
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: false);
            }
        }
    }

    public static void MoveAsset(
        NovelProject project,
        string projectPath,
        NovelAsset asset,
        string folder)
    {
        folder = NormalizeFolder(folder);
        EnsureFolder(project, folder);
        if (asset.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var source = ResolvePath(projectPath, asset);
        var targetDirectory = Path.Combine(
            GetAssetsDirectory(projectPath),
            folder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(targetDirectory);
        var target = UniquePath(targetDirectory, Path.GetFileName(source));
        if (File.Exists(source))
        {
            File.Move(source, target);
        }
        asset.Folder = folder;
        asset.Path = Path.GetRelativePath(GetProjectDirectory(projectPath), target)
            .Replace('\\', '/');
    }

    public static string NormalizeFolder(string? folder)
    {
        var normalized = (folder ?? string.Empty)
            .Replace('\\', '/')
            .Trim('/');
        if (normalized.Length == 0)
        {
            return string.Empty;
        }
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0
                || segment is "." or ".."
                || segment.IndexOfAny(invalid) >= 0)
            {
                throw new InvalidDataException($"Некорректная папка ассетов: {folder}");
            }
        }
        return normalized;
    }

    public static string MakeId(string fileName)
    {
        var builder = new StringBuilder();
        foreach (var character in fileName.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '-')
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }
        var result = builder.ToString().Trim('_', '-');
        if (result.Length == 0)
        {
            return "asset";
        }
        return char.IsDigit(result[0]) ? $"asset_{result}" : result;
    }

    public static string BuildManagedAssetPath(string folder, string fileName)
    {
        folder = NormalizeFolder(folder);
        return folder.Length == 0
            ? $"{ManagedFilesDirectoryName}/{fileName}"
            : $"{ManagedFilesDirectoryName}/{folder}/{fileName}";
    }

    private static string GetProjectDirectory(string projectPath) =>
        Path.GetDirectoryName(Path.GetFullPath(projectPath))
        ?? throw new InvalidOperationException("Не удалось определить папку проекта.");

    private static string ExistingManagedFolderDirectory(string projectPath, string folder)
    {
        var preferred = Path.Combine(
            GetAssetsDirectory(projectPath),
            folder.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(preferred))
        {
            return preferred;
        }
        var legacy = Path.Combine(
            GetProjectDirectory(projectPath),
            LegacyAssetsDirectoryName,
            folder.Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(legacy) ? legacy : preferred;
    }

    private static IEnumerable<string> ManagedFolderDirectories(
        string projectPath,
        string folder)
    {
        var relativeFolder = folder.Replace('/', Path.DirectorySeparatorChar);
        yield return Path.Combine(GetAssetsDirectory(projectPath), relativeFolder);
        yield return Path.Combine(
            GetProjectDirectory(projectPath),
            LegacyAssetsDirectoryName,
            relativeFolder);
    }

    private static void EnsureFolder(NovelProject project, string folder)
    {
        folder = NormalizeFolder(folder);
        if (folder.Length == 0)
        {
            return;
        }
        var current = string.Empty;
        foreach (var segment in folder.Split('/'))
        {
            current = current.Length == 0 ? segment : $"{current}/{segment}";
            if (!project.AssetFolders.Contains(
                current,
                StringComparer.OrdinalIgnoreCase))
            {
                project.AssetFolders.Add(current);
            }
        }
    }

    private static string ParentFolder(string folder)
    {
        var separator = folder.LastIndexOf('/');
        return separator < 0 ? string.Empty : folder[..separator];
    }

    private static bool IsInFolder(string candidate, string folder)
    {
        candidate = NormalizeFolder(candidate);
        folder = NormalizeFolder(folder);
        return candidate.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(
                folder + "/",
                StringComparison.OrdinalIgnoreCase);
    }

    private static string UniquePath(string directory, string fileName)
    {
        var target = Path.Combine(directory, fileName);
        var suffix = 2;
        while (File.Exists(target))
        {
            target = Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(fileName)}-{suffix++}"
                + Path.GetExtension(fileName));
        }
        return target;
    }
}
