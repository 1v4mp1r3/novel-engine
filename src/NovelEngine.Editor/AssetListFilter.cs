using System.IO;
using NovelEngine.Core;

namespace NovelEngine.Editor;

internal static class AssetListFilter
{
    public static AssetListFilterResult Apply(
        IEnumerable<NovelAsset> assets,
        string? selectedFolder,
        string query)
    {
        var normalizedQuery = query.Trim();
        var normalizedFolder = selectedFolder is null
            ? null
            : ProjectAssets.NormalizeFolder(selectedFolder);
        var filteredByFolder = new List<NovelAsset>();
        foreach (var asset in assets)
        {
            if (normalizedFolder is null
                || asset.Folder.Equals(
                    normalizedFolder,
                    StringComparison.OrdinalIgnoreCase))
            {
                filteredByFolder.Add(asset);
            }
        }

        var folderAssetCount = filteredByFolder.Count;
        var visibleAssets = normalizedQuery.Length == 0
            ? filteredByFolder
            : filteredByFolder
                .Where(asset => MatchesSearch(asset, normalizedQuery))
                .ToList();
        visibleAssets.Sort(CompareAssets);
        return new AssetListFilterResult(
            normalizedQuery,
            folderAssetCount,
            visibleAssets);
    }

    private static int CompareAssets(NovelAsset left, NovelAsset right)
    {
        var kind = left.Kind.CompareTo(right.Kind);
        return kind != 0
            ? kind
            : StringComparer.CurrentCultureIgnoreCase.Compare(left.Id, right.Id);
    }

    private static bool MatchesSearch(NovelAsset asset, string query)
    {
        var tokens = query.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 0 || tokens.All(token =>
            ContainsSearchToken(asset.Id, token)
            || ContainsSearchToken(asset.Path, token)
            || ContainsSearchToken(asset.Folder, token)
            || ContainsSearchToken(Path.GetFileName(asset.Path), token)
            || ContainsSearchToken(asset.Kind.ToString(), token)
            || ContainsSearchToken(AssetKindLabel(asset.Kind), token));
    }

    private static bool ContainsSearchToken(string value, string token) =>
        value.Contains(token, StringComparison.CurrentCultureIgnoreCase);

    private static string AssetKindLabel(AssetKind kind) =>
        kind switch
        {
            AssetKind.Image => "изображение картинка image",
            AssetKind.Audio => "аудио музыка звук audio",
            _ => "файл file",
        };
}

internal sealed record AssetListFilterResult(
    string Query,
    int FolderAssetCount,
    IReadOnlyList<NovelAsset> Assets);
