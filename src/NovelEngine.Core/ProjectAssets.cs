using System.Text;

namespace NovelEngine.Core;

public static class ProjectAssets
{
    public const string ManagedFilesDirectoryName = "files";
    public const string LegacyAssetsDirectoryName = "assets";
    public static readonly IReadOnlyList<string> DefaultProjectFolders =
    [
        "characters",
        "voices",
        "audio_fx",
        "audio",
        "backgrounds",
    ];

    public static int EnsureDefaultFolders(NovelProject project)
    {
        var changes = 0;
        var knownFolders = BuildAssetFolderSet(project);
        foreach (var folder in DefaultProjectFolders)
        {
            changes += EnsureFolder(project, folder, knownFolders);
        }
        return changes;
    }

    public static int EnsureDefaultStructure(NovelProject project, string projectPath)
    {
        var changes = EnsureDefaultFolders(project);
        var root = GetAssetsDirectory(projectPath);
        Directory.CreateDirectory(root);
        foreach (var folder in DefaultProjectFolders)
        {
            Directory.CreateDirectory(
                Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar)));
        }
        return changes;
    }

    public static NovelAsset Import(
        NovelProject project,
        string projectPath,
        string sourcePath,
        string? folder = null)
    {
        var knownAssetIds = BuildAssetIdSet(project);
        var knownAssetFolders = BuildAssetFolderSet(project);
        var knownAssetsByFullPath = BuildAssetFullPathMap(project, projectPath);
        return ImportCore(
            project,
            projectPath,
            sourcePath,
            folder,
            knownAssetIds,
            knownAssetFolders,
            knownAssetsByFullPath);
    }

    public static IReadOnlyList<NovelAsset> ImportMany(
        NovelProject project,
        string projectPath,
        IEnumerable<string> sourcePaths,
        string? folder = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        var knownAssetIds = BuildAssetIdSet(project);
        var knownAssetFolders = BuildAssetFolderSet(project);
        var knownAssetsByFullPath = BuildAssetFullPathMap(project, projectPath);
        var imported = new List<NovelAsset>();
        foreach (var sourcePath in sourcePaths)
        {
            imported.Add(ImportCore(
                project,
                projectPath,
                sourcePath,
                folder,
                knownAssetIds,
                knownAssetFolders,
                knownAssetsByFullPath));
        }

        return imported;
    }

    private static NovelAsset ImportCore(
        NovelProject project,
        string projectPath,
        string sourcePath,
        string? folder,
        HashSet<string> knownAssetIds,
        HashSet<string> knownAssetFolders,
        Dictionary<string, NovelAsset> knownAssetsByFullPath)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Файл ассета не найден.", source);
        }

        if (knownAssetsByFullPath.TryGetValue(source, out var existing))
        {
            return existing;
        }

        var projectDirectory = GetProjectDirectory(projectPath);
        var kind = AssetReference.GuessKind(source);
        var managedFolder = folder is null
            ? TryGetManagedFolder(projectPath, source)
            : null;
        folder = NormalizeFolder(
            folder ?? managedFolder ?? kind switch
            {
                AssetKind.Image => "backgrounds",
                AssetKind.Audio => "audio",
                _ => "other",
            });
        EnsureFolder(project, folder, knownAssetFolders);
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

        var id = CreateUniqueAssetId(
            MakeId(Path.GetFileNameWithoutExtension(target)),
            knownAssetIds);

        var asset = new NovelAsset
        {
            Id = id,
            Kind = kind,
            Folder = folder,
            Path = Path.GetRelativePath(projectDirectory, target)
                .Replace('\\', '/'),
        };
        project.Assets.Add(asset);
        knownAssetsByFullPath[source] = asset;
        knownAssetsByFullPath[Path.GetFullPath(target)] = asset;
        return asset;
    }

    private static string? TryGetManagedFolder(string projectPath, string sourcePath)
    {
        var root = GetAssetsDirectory(projectPath);
        var relative = Path.GetRelativePath(root, sourcePath);
        if (relative == "."
            || relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
            || relative.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            return null;
        }

        return NormalizeFolder(
            Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty);
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
        var changes = RemoveMissingManagedAssets(project, projectPath);
        changes += RemoveMissingManagedFolders(project, root);
        if (!Directory.Exists(root))
        {
            return changes;
        }

        var knownAssetFolders = BuildAssetFolderSet(project);
        foreach (var directory in Directory.EnumerateDirectories(
            root,
            "*",
            SearchOption.AllDirectories))
        {
            var folder = NormalizeFolder(
                Path.GetRelativePath(root, directory).Replace('\\', '/'));
            changes += EnsureFolder(project, folder, knownAssetFolders);
        }

        var projectDirectory = GetProjectDirectory(projectPath);
        var knownRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in project.Assets)
        {
            knownRelativePaths.Add(NormalizeRelativeAssetPath(asset.Path));
        }
        var knownAssetsByFullPath = BuildAssetFullPathMap(project, projectPath);
        var knownAssetIds = BuildAssetIdSet(project);
        foreach (var file in Directory.EnumerateFiles(
            root,
            "*",
            SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(file);
            var relativeProjectPath = Path.GetRelativePath(projectDirectory, file)
                .Replace('\\', '/');
            if (knownRelativePaths.Contains(relativeProjectPath)
                || knownAssetsByFullPath.ContainsKey(fullPath))
            {
                continue;
            }

            var relativeFolder = Path.GetDirectoryName(
                    Path.GetRelativePath(root, file))
                ?.Replace('\\', '/')
                ?? string.Empty;
            var before = project.Assets.Count;
            var imported = RegisterManagedFile(
                project,
                fullPath,
                relativeProjectPath,
                relativeFolder,
                knownAssetIds,
                knownAssetFolders);
            knownRelativePaths.Add(NormalizeRelativeAssetPath(imported.Path));
            knownAssetsByFullPath[ResolvePath(projectPath, imported)] = imported;
            changes += project.Assets.Count - before;
        }

        return changes;
    }

    private static int RemoveMissingManagedFolders(
        NovelProject project,
        string root)
    {
        var changes = 0;
        for (var index = project.AssetFolders.Count - 1; index >= 0; index--)
        {
            var folder = NormalizeFolder(project.AssetFolders[index]);
            if (IsDefaultProjectFolder(folder)
                || HasAssetInFolder(project, folder)
                || Directory.Exists(GetManagedFolderDirectory(root, folder)))
            {
                continue;
            }

            project.AssetFolders.RemoveAt(index);
            changes++;
        }

        return changes;
    }

    private static int RemoveMissingManagedAssets(
        NovelProject project,
        string projectPath)
    {
        var changes = 0;
        for (var index = project.Assets.Count - 1; index >= 0; index--)
        {
            var asset = project.Assets[index];
            if (!IsManagedAssetPath(asset.Path)
                || File.Exists(ResolvePath(projectPath, asset)))
            {
                continue;
            }

            project.ReplaceAssetReference(asset.Id, string.Empty);
            project.Assets.RemoveAt(index);
            changes++;
        }

        return changes;
    }

    private static NovelAsset RegisterManagedFile(
        NovelProject project,
        string fullPath,
        string relativeProjectPath,
        string relativeFolder,
        HashSet<string> knownAssetIds,
        HashSet<string> knownAssetFolders)
    {
        var folder = NormalizeFolder(relativeFolder);
        EnsureFolder(project, folder, knownAssetFolders);

        var id = CreateUniqueAssetId(
            MakeId(Path.GetFileNameWithoutExtension(fullPath)),
            knownAssetIds);

        var asset = new NovelAsset
        {
            Id = id,
            Kind = AssetReference.GuessKind(fullPath),
            Folder = folder,
            Path = relativeProjectPath,
        };
        project.Assets.Add(asset);
        return asset;
    }

    private static HashSet<string> BuildAssetIdSet(NovelProject project)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in project.Assets)
        {
            ids.Add(asset.Id);
        }

        return ids;
    }

    private static HashSet<string> BuildAssetFolderSet(NovelProject project)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in project.AssetFolders)
        {
            folders.Add(folder);
        }

        return folders;
    }

    private static Dictionary<string, NovelAsset> BuildAssetFullPathMap(
        NovelProject project,
        string projectPath)
    {
        var assetsByFullPath = new Dictionary<string, NovelAsset>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var asset in project.Assets)
        {
            assetsByFullPath.TryAdd(ResolvePath(projectPath, asset), asset);
        }

        return assetsByFullPath;
    }

    private static string CreateUniqueAssetId(
        string baseId,
        HashSet<string> knownAssetIds)
    {
        var id = baseId;
        var suffix = 2;
        while (!knownAssetIds.Add(id))
        {
            id = $"{baseId}_{suffix++}";
        }
        return id;
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
        if (FolderExists(project, target))
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
        if (folder.Length == 0)
        {
            throw new InvalidDataException("Имя папки не может быть пустым.");
        }
        if (HasAssetInFolder(project, folder))
        {
            throw new InvalidOperationException(
                "Сначала переместите или удалите ассеты из этой папки.");
        }
        if (HasNestedFolder(project, folder))
        {
            throw new InvalidOperationException(
                "Сначала удалите вложенные папки.");
        }
        var directories = new List<string>();
        foreach (var directory in ManagedFolderDirectories(projectPath, folder))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }
            if (DirectoryHasEntries(directory))
            {
                throw new InvalidOperationException(
                    "Сначала удалите файлы из этой папки.");
            }
            directories.Add(directory);
        }
        RemoveFolder(project, folder);
        foreach (var directory in directories)
        {
            Directory.Delete(directory, recursive: false);
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
        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                "Файл ассета для перемещения не найден.",
                source);
        }

        var targetDirectory = Path.Combine(
            GetAssetsDirectory(projectPath),
            folder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(targetDirectory);
        var target = UniquePath(targetDirectory, Path.GetFileName(source));
        File.Move(source, target);
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

    private static string NormalizeRelativeAssetPath(string path) =>
        path.Replace('\\', '/');

    private static bool IsManagedAssetPath(string path)
    {
        var normalized = NormalizeRelativeAssetPath(path);
        return normalized.StartsWith(
            $"{ManagedFilesDirectoryName}/",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultProjectFolder(string folder)
    {
        for (var index = 0; index < DefaultProjectFolders.Count; index++)
        {
            if (DefaultProjectFolders[index].Equals(
                folder,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetProjectDirectory(string projectPath) =>
        Path.GetDirectoryName(Path.GetFullPath(projectPath))
        ?? throw new InvalidOperationException("Не удалось определить папку проекта.");

    private static string GetManagedFolderDirectory(string root, string folder) =>
        Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));

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

    private static int EnsureFolder(
        NovelProject project,
        string folder,
        HashSet<string>? knownFolders = null)
    {
        folder = NormalizeFolder(folder);
        if (folder.Length == 0)
        {
            return 0;
        }
        knownFolders ??= BuildAssetFolderSet(project);
        var changes = 0;
        var current = string.Empty;
        foreach (var segment in folder.Split('/'))
        {
            current = current.Length == 0 ? segment : $"{current}/{segment}";
            if (knownFolders.Add(current))
            {
                project.AssetFolders.Add(current);
                changes++;
            }
        }
        return changes;
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

    private static bool FolderExists(NovelProject project, string folder)
    {
        for (var index = 0; index < project.AssetFolders.Count; index++)
        {
            if (project.AssetFolders[index].Equals(
                    folder,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasAssetInFolder(NovelProject project, string folder)
    {
        foreach (var asset in project.Assets)
        {
            if (IsInFolder(asset.Folder, folder))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasNestedFolder(NovelProject project, string folder)
    {
        foreach (var candidate in project.AssetFolders)
        {
            if (!candidate.Equals(folder, StringComparison.OrdinalIgnoreCase)
                && IsInFolder(candidate, folder))
            {
                return true;
            }
        }
        return false;
    }

    private static bool DirectoryHasEntries(string directory)
    {
        using var entries = Directory.EnumerateFileSystemEntries(directory)
            .GetEnumerator();
        return entries.MoveNext();
    }

    private static void RemoveFolder(NovelProject project, string folder)
    {
        for (var index = project.AssetFolders.Count - 1; index >= 0; index--)
        {
            if (project.AssetFolders[index].Equals(
                    folder,
                    StringComparison.OrdinalIgnoreCase))
            {
                project.AssetFolders.RemoveAt(index);
            }
        }
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
