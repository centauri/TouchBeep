using System.Collections.Concurrent;

namespace TouchBeep;

/// <summary>
/// Plays short tones via winmm waveOut (not SoundPlayer) so that sound is consistent whether the form is open or minimized.
/// Uses a dedicated audio thread with one beep at a time.
/// </summary>
public static class ToneGenerator
{
    private const int SampleRate = 44100;
    private const int BitsPerSample = 16;
    private const int DurationMs = 80;

    public enum WaveType { Sine, Square, Triangle }

    private static readonly BlockingCollection<(int Freq, WaveType Wave)> Queue = new();
    private static readonly Thread AudioThread;
    private const int MaxQueueSize = 8;

    static ToneGenerator()
    {
        AudioThread = new Thread(AudioLoop)
        {
            IsBackground = true,
            Name = "TouchBeep-Audio",
            Priority = ThreadPriority.Highest
        };
        AudioThread.SetApartmentState(ApartmentState.STA);
        AudioThread.Start();
    }

    private static void AudioLoop()
    {
        foreach (var (frequencyHz, wave) in Queue.GetConsumingEnumerable())
        {
            try
            {
                PlaySync(frequencyHz, wave);
            }
            catch { /* Ignore playback errors */ }
        }
    }

    /// <summary>Requests a beep; it is played on the dedicated audio thread so playback is independent of the UI.</summary>
    public static void PlayAsync(int frequencyHz, WaveType wave)
    {
        if (Queue.Count >= MaxQueueSize) return;
        try { Queue.Add((frequencyHz, wave)); } catch (ObjectDisposedException) { }
    }

    private static void PlaySync(int frequencyHz, WaveType wave)
    {
        int numSamples = SampleRate * DurationMs / 1000;
        var samples = new short[numSamples];
        double amplitude = 16000;

        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / SampleRate;
            double value = wave switch
            {
                WaveType.Sine => Math.Sin(2 * Math.PI * frequencyHz * t),
                WaveType.Square => Math.Sign(Math.Sin(2 * Math.PI * frequencyHz * t)),
                WaveType.Triangle => 2 * Math.Abs(2 * (t * frequencyHz - Math.Floor(t * frequencyHz + 0.5))) - 1,
                _ => 0
            };
            samples[i] = (short)(value * amplitude);
        }

        WinMmWaveOut.Play(samples, SampleRate);
    }
}
