namespace NovelEngine.Core;

public static class ChoiceAvailability
{
    public static bool CanEnable(bool choicesReady, bool paused, bool transitioning) =>
        choicesReady && !paused && !transitioning;
}
