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
                .Select(NormalizeEntry)
                .OfType<RecentProjectEntry>()
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
            var normalizedPath = NormalizePath(path);
            if (normalizedPath is null)
            {
                return;
            }
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
            or ArgumentException
            or NotSupportedException)
        {
            // Recent projects are a convenience cache; project opening should not fail
            // if the cache cannot be written.
        }
    }

    private static RecentProjectEntry? NormalizeEntry(RecentProjectEntry entry)
    {
        var path = NormalizePath(entry.Path);
        if (path is null || !Exists(path))
        {
            return null;
        }

        return entry with
        {
            Path = path,
            DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                ? CreateDisplayName(path)
                : entry.DisplayName,
        };
    }

    private static string? NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(
            path.Trim().Trim('"')));
    }

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
