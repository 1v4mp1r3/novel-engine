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
        exists ??= PathExists;
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

    private static bool PathExists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }

    public static void Prune(
        string directory,
        string stem,
        int keepCount,
        Action<string>? delete = null)
    {
        delete ??= File.Delete;
        var files = Directory.GetFiles(directory, $"{stem}-*.novel.json");
        Array.Sort(
            files,
            (left, right) => File.GetLastWriteTimeUtc(right)
                .CompareTo(File.GetLastWriteTimeUtc(left)));
        for (var index = Math.Max(keepCount, 0); index < files.Length; index++)
        {
            TryDelete(files[index], delete);
        }
    }

    private static void TryDelete(string path, Action<string> delete)
    {
        try
        {
            delete(path);
        }
        catch (Exception error) when (
            error is IOException
            or UnauthorizedAccessException)
        {
            // Autosave pruning is best-effort; one locked snapshot should not
            // prevent later autosaves or pruning of other old snapshots.
        }
    }
}
