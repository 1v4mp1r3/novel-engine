using System.IO;

namespace NovelEngine.Editor;

internal static class AutoSaveStore
{
    public static string CreateSnapshotPath(
        string directory,
        string stem,
        DateTime timestampUtc,
        Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var timestamp = timestampUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(directory, $"{stem}-{timestamp}.novel.json");
        if (!exists(path))
        {
            return path;
        }

        var suffix = 2;
        while (true)
        {
            var candidate = Path.Combine(
                directory,
                $"{stem}-{timestamp}-{suffix++}.novel.json");
            if (!exists(candidate))
            {
                return candidate;
            }
        }
    }

    public static void Prune(string directory, string stem, int keepCount)
    {
        foreach (var file in Directory
            .EnumerateFiles(directory, $"{stem}-*.novel.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(keepCount))
        {
            File.Delete(file);
        }
    }
}
