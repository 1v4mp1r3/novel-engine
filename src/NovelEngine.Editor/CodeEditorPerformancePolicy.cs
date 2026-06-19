namespace NovelEngine.Editor;

internal static class CodeEditorPerformancePolicy
{
    public const int MaxHighlightedCodeLength = 40_000;
    public const int MaxAutomaticCompletionSourceLength = 60_000;
    public const int MaxTrackedCaretSourceLength = 60_000;
    public const int MaxLiveCodeAnalysisLength = 80_000;
    public const int MaxHighlightedSyntaxSpans = 2_500;

    public static bool ShouldTrackCursorPosition(int sourceLength) =>
        sourceLength <= MaxHighlightedCodeLength;

    public static bool ShouldRunLiveAnalysis(int sourceLength) =>
        sourceLength <= MaxLiveCodeAnalysisLength;

    public static bool ShouldRunAutomaticCompletions(int sourceLength) =>
        sourceLength <= MaxAutomaticCompletionSourceLength;

    public static bool ShouldTrackLiveCaret(int sourceLength) =>
        sourceLength <= MaxTrackedCaretSourceLength;

    public static bool ShouldApplyFullSyntaxHighlighting(int sourceLength) =>
        sourceLength <= MaxHighlightedCodeLength;

    public static bool ShouldApplyLiveErrorHighlighting(int sourceLength) =>
        sourceLength <= MaxHighlightedCodeLength;
}
