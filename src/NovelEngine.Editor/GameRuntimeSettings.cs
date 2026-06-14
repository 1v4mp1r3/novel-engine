namespace NovelEngine.Editor;

public sealed class GameRuntimeSettings
{
    public double MusicVolume { get; set; } = 0.55;
    public double EffectsVolume { get; set; } = 0.8;
    public double VoiceVolume { get; set; } = 0.65;
    public int TextDelayMs { get; set; } = 18;

    public GameRuntimeSettings Clone() =>
        new()
        {
            MusicVolume = MusicVolume,
            EffectsVolume = EffectsVolume,
            VoiceVolume = VoiceVolume,
            TextDelayMs = TextDelayMs,
        };

    public void CopyFrom(GameRuntimeSettings settings)
    {
        MusicVolume = settings.MusicVolume;
        EffectsVolume = settings.EffectsVolume;
        VoiceVolume = settings.VoiceVolume;
        TextDelayMs = settings.TextDelayMs;
    }
}
