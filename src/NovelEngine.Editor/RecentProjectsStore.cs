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
            return NormalizeEntries(entries);
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
            var existingEntries = Load();
            var entries = new List<RecentProjectEntry>(
                Math.Min(MaxEntries, existingEntries.Count + 1))
            {
                new(
                    normalizedPath,
                    CreateDisplayName(normalizedPath),
                    DateTime.UtcNow),
            };

            for (var index = 0;
                 index < existingEntries.Count && entries.Count < MaxEntries;
                 index++)
            {
                var entry = existingEntries[index];
                if (entry.Path.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entries.Add(entry);
            }

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

    private static List<RecentProjectEntry> NormalizeEntries(
        IReadOnlyList<RecentProjectEntry> entries)
    {
        var newestByPath = new Dictionary<string, RecentProjectEntry>(
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entries.Count; index++)
        {
            var normalized = NormalizeEntry(entries[index]);
            if (normalized is null)
            {
                continue;
            }

            if (!newestByPath.TryGetValue(normalized.Path, out var existing)
                || normalized.LastOpenedUtc > existing.LastOpenedUtc)
            {
                newestByPath[normalized.Path] = normalized;
            }
        }

        var result = new List<RecentProjectEntry>(newestByPath.Count);
        foreach (var entry in newestByPath.Values)
        {
            result.Add(entry);
        }

        result.Sort(static (left, right) =>
        {
            var openedCompare = right.LastOpenedUtc.CompareTo(left.LastOpenedUtc);
            return openedCompare != 0
                ? openedCompare
                : string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
        });
        if (result.Count > MaxEntries)
        {
            result.RemoveRange(MaxEntries, result.Count - MaxEntries);
        }

        return result;
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
