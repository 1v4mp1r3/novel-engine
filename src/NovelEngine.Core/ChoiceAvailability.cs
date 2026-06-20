namespace NovelEngine.Core;

public static class ChoiceAvailability
{
    public static bool CanEnable(bool choicesReady, bool paused, bool transitioning) =>
        choicesReady && !paused && !transitioning;

    public static bool CanChooseByShortcut(
        bool choicesReady,
        bool paused,
        bool transitioning,
        int choiceIndex,
        int choiceCount) =>
        CanEnable(choicesReady, paused, transitioning)
        && choiceIndex >= 0
        && choiceIndex < choiceCount;
}
