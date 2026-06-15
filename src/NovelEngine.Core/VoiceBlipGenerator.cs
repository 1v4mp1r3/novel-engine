using System.Text;

namespace NovelEngine.Core;

public sealed record VoiceBlipOptions
{
    public double Pitch { get; init; } = 50;
    public double Volume { get; init; } = 75;
    public double Gender { get; init; } = 50;
    public double Tone { get; init; } = 50;
    public double Speed { get; init; } = 50;
    public double Randomness { get; init; } = 20;
    public int SampleRate { get; init; } = 44_100;
}

public static class VoiceBlipGenerator
{
    public static void WriteWaveFile(string path, VoiceBlipOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate(options);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var samples = RenderSamples(options);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        WriteWaveHeader(writer, options.SampleRate, samples.Length);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }
    }

    public static byte[] CreateWaveBytes(VoiceBlipOptions options)
    {
        Validate(options);
        var samples = RenderSamples(options);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            WriteWaveHeader(writer, options.SampleRate, samples.Length);
            foreach (var sample in samples)
            {
                writer.Write(sample);
            }
        }
        return stream.ToArray();
    }

    private static short[] RenderSamples(VoiceBlipOptions options)
    {
        var pitch = Normalize(options.Pitch);
        var volume = Normalize(options.Volume);
        var gender = Normalize(options.Gender);
        var tone = Normalize(options.Tone);
        var speed = Normalize(options.Speed);
        var randomness = Normalize(options.Randomness);

        var durationSeconds = Lerp(0.18, 0.035, speed);
        var sampleCount = Math.Max(1, (int)Math.Round(options.SampleRate * durationSeconds));
        var samples = new short[sampleCount];
        var baseFrequency = Lerp(115, 980, pitch) * Lerp(0.72, 1.42, gender);
        var brightness = Lerp(0.15, 0.85, tone);
        var seed = HashCode.Combine(
            Quantize(options.Pitch),
            Quantize(options.Gender),
            Quantize(options.Tone),
            Quantize(options.Speed),
            Quantize(options.Randomness));
        var random = new Random(seed);
        var phase = 0d;
        var pitchDrift = 1 + (random.NextDouble() * 2 - 1) * randomness * 0.08;

        for (var index = 0; index < samples.Length; index++)
        {
            var progress = index / (double)Math.Max(1, samples.Length - 1);
            var attack = Math.Clamp(progress / 0.12, 0, 1);
            var release = Math.Pow(1 - progress, 1.8);
            var envelope = attack * release;
            var flutter = 1
                + Math.Sin(progress * Math.Tau * 7.5) * randomness * 0.035
                + (random.NextDouble() * 2 - 1) * randomness * 0.015;
            var frequency = baseFrequency * pitchDrift * flutter;
            phase += Math.Tau * frequency / options.SampleRate;
            var sine = Math.Sin(phase);
            var harmonic = Math.Sin(phase * 2.01) * 0.35 + Math.Sin(phase * 3.02) * 0.18;
            var square = sine >= 0 ? 1 : -1;
            var noise = (random.NextDouble() * 2 - 1) * randomness * 0.24;
            var signal = sine * (1 - brightness)
                + (sine * 0.45 + harmonic + square * 0.22) * brightness
                + noise;
            signal = Math.Tanh(signal * 1.35);
            samples[index] = (short)Math.Round(
                Math.Clamp(signal * envelope * volume * short.MaxValue, short.MinValue, short.MaxValue));
        }
        return samples;
    }

    private static void WriteWaveHeader(BinaryWriter writer, int sampleRate, int sampleCount)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        var dataSize = sampleCount * channels * bitsPerSample / 8;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);
    }

    private static void Validate(VoiceBlipOptions options)
    {
        if (options.SampleRate is < 8_000 or > 192_000)
        {
            throw new InvalidDataException("Sample rate блипа должен быть от 8000 до 192000.");
        }
        ValidatePercent(options.Pitch, "Pitch");
        ValidatePercent(options.Volume, "Volume");
        ValidatePercent(options.Gender, "Gender");
        ValidatePercent(options.Tone, "Tone");
        ValidatePercent(options.Speed, "Speed");
        ValidatePercent(options.Randomness, "Random");
    }

    private static void ValidatePercent(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 100)
        {
            throw new InvalidDataException($"{name} должен быть от 0 до 100.");
        }
    }

    private static double Normalize(double value) => Math.Clamp(value / 100d, 0, 1);

    private static double Lerp(double from, double to, double amount) =>
        from + (to - from) * amount;

    private static int Quantize(double value) => (int)Math.Round(value * 100);
}
