using System.IO;
using System.Text.Json;

namespace NovelEngine.Editor;

internal sealed record RecentProjectEntry(
    string Path,
    string DisplayName,
    DateTime LastOpenedUtc);

internal static class RecentProjectsStore
{
    private const int MaxEntries = 8;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static IReadOnlyList<RecentProjectEntry> Load()
    {
        try
        {
            var path = GetStorePath();
            if (!File.Exists(path))
            {
                return [];
            }

            var entries = JsonSerializer.Deserialize<List<RecentProjectEntry>>(
                File.ReadAllText(path),
                JsonOptions) ?? [];
            return entries
                .Where(entry => IsUsableEntry(entry))
                .OrderByDescending(entry => entry.LastOpenedUtc)
                .Take(MaxEntries)
                .ToList();
        }
        catch (Exception error) when (
            error is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            return [];
        }
    }

    public static void Remember(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var normalizedPath = Path.GetFullPath(path);
            var entries = Load()
                .Where(entry => !entry.Path.Equals(
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase))
                .Prepend(
                    new RecentProjectEntry(
                        normalizedPath,
                        CreateDisplayName(normalizedPath),
                        DateTime.UtcNow))
                .Take(MaxEntries)
                .ToList();

            var storePath = GetStorePath();
            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            File.WriteAllText(
                storePath,
                JsonSerializer.Serialize(entries, JsonOptions));
        }
        catch (Exception error) when (
            error is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            // Recent projects are a convenience cache; project opening should not fail
            // if the cache cannot be written.
        }
    }

    private static bool IsUsableEntry(RecentProjectEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Path) && Exists(entry.Path);

    private static bool Exists(string path) =>
        File.Exists(path) || Directory.Exists(path);

    private static string CreateDisplayName(string path)
    {
        if (File.Exists(path))
        {
            return Path.GetFileNameWithoutExtension(
                Path.GetFileNameWithoutExtension(path));
        }

        return Path.GetFileName(path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
    }

    private static string GetStorePath()
    {
        var overridePath = Environment.GetEnvironmentVariable(
            "NOVEL_ENGINE_RECENT_PROJECTS_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "NovelEngine", "recent-projects.json");
    }
}
