namespace NovelEngine.Core;

public static class VoicePlaybackCadence
{
    public static bool ShouldPlay(int voicedCharacterCount, int everyNthCharacter)
    {
        if (voicedCharacterCount <= 0)
        {
            return false;
        }

        everyNthCharacter = Math.Clamp(everyNthCharacter, 1, 12);
        return voicedCharacterCount % everyNthCharacter == 0;
    }
}
