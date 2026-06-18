namespace NovelEngine.Editor;

internal static class CodeEditorPerformancePolicy
{
    public const int MaxHighlightedCodeLength = 40_000;
    public const int MaxLiveCodeAnalysisLength = 80_000;
    public const int MaxHighlightedSyntaxSpans = 2_500;

    public static bool ShouldTrackCursorPosition(int sourceLength) =>
        sourceLength <= MaxHighlightedCodeLength;

    public static bool ShouldRunLiveAnalysis(int sourceLength) =>
        sourceLength <= MaxLiveCodeAnalysisLength;

    public static bool ShouldApplyFullSyntaxHighlighting(int sourceLength) =>
        sourceLength <= MaxHighlightedCodeLength;

    public static bool ShouldApplyLiveErrorHighlighting(int sourceLength) =>
        sourceLength <= MaxHighlightedCodeLength;
}
