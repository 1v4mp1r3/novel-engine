namespace NovelEngine.Core;

public static class VoiceSoundPicker
{
    public static string Pick(IReadOnlyList<string> soundPaths, Random? random = null)
    {
        if (soundPaths.Count == 0)
        {
            return string.Empty;
        }
        if (soundPaths.Count == 1)
        {
            return soundPaths[0];
        }

        random ??= Random.Shared;
        return soundPaths[random.Next(soundPaths.Count)];
    }
}
