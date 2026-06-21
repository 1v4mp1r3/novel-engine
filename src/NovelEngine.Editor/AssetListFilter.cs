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
        var tokens = SplitSearchQuery(normalizedQuery);
        var visibleAssets = new List<NovelAsset>();
        var folderAssetCount = 0;
        foreach (var asset in assets)
        {
            if (normalizedFolder is not null
                && !asset.Folder.Equals(
                    normalizedFolder,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            folderAssetCount++;
            if (tokens.Length == 0 || MatchesSearch(asset, tokens))
            {
                visibleAssets.Add(asset);
            }
        }

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

    private static string[] SplitSearchQuery(string query) =>
        query.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool MatchesSearch(NovelAsset asset, IReadOnlyList<string> tokens) =>
        tokens.All(token =>
            ContainsSearchToken(asset.Id, token)
            || ContainsSearchToken(asset.Path, token)
            || ContainsSearchToken(asset.Folder, token)
            || ContainsSearchToken(Path.GetFileName(asset.Path), token)
            || ContainsSearchToken(asset.Kind.ToString(), token)
            || ContainsSearchToken(AssetKindLabel(asset.Kind), token));

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
