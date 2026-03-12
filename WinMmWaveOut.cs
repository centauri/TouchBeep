using System.Runtime.InteropServices;

namespace TouchBeep;

/// <summary>
/// Plays raw PCM (16-bit mono) via winmm. The device is opened once and reused.
/// A short silent buffer is played on first use to reduce artifacts after device open or window focus change.
/// </summary>
internal static class WinMmWaveOut
{
    private const int WAVE_MAPPER = -1;
    private const uint WAVE_FORMAT_PCM = 1;
    private const uint WHDR_DONE = 0x00000001;
    private const int MMSYSERR_NOERROR = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID, ref WAVEFORMATEX lpFormat, IntPtr dwCallback, IntPtr dwCallbackInstance, int fdwOpen);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr pWaveOutHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr pWaveOutHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr pWaveOutHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr hWaveOut);

    [DllImport("winmm.dll")]
    private static extern int timeBeginPeriod(uint uMilliseconds);

    private static readonly int WaveHdrSize = Marshal.SizeOf<WAVEHDR>();
    private static readonly WAVEFORMATEX Format = new()
    {
        wFormatTag = (ushort)WAVE_FORMAT_PCM,
        nChannels = 1,
        nSamplesPerSec = 44100,
        wBitsPerSample = 16,
        nBlockAlign = 2,
        nAvgBytesPerSec = 44100 * 2u,
        cbSize = 0
    };

    private static IntPtr _device = IntPtr.Zero;
    private static bool _primed;
    private static readonly object _lock = new();
    private static readonly short[] SilencePrime = new short[220];

    /// <summary>Plays 16-bit mono PCM at 44100 Hz. One buffer per beep. The device is primed only once when it is first opened.</summary>
    public static void Play(short[] samples, int sampleRate = 44100)
    {
        if (sampleRate != 44100) return;
        EnsureDeviceOpen();
        if (_device == IntPtr.Zero) return;
        if (!_primed)
        {
            PlayBufferAndWait(SilencePrime);
            _primed = true;
        }
        PlayBufferAndWait(samples);
    }

    private static void EnsureDeviceOpen()
    {
        lock (_lock)
        {
            if (_device != IntPtr.Zero) return;
            var fmt = Format;
            int r = waveOutOpen(out _device, WAVE_MAPPER, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (r != MMSYSERR_NOERROR || _device == IntPtr.Zero) return;
            timeBeginPeriod(1);
        }
    }

    private static void PlayBufferAndWait(short[] samples)
    {
        int byteCount = samples.Length * 2;
        IntPtr bufferPtr = Marshal.AllocHGlobal(byteCount);
        IntPtr hdrPtr = Marshal.AllocHGlobal(WaveHdrSize);
        bool headerPrepared = false;
        try
        {
            Marshal.Copy(ToByteArray(samples), 0, bufferPtr, byteCount);
            var hdr = new WAVEHDR
            {
                lpData = bufferPtr,
                dwBufferLength = (uint)byteCount,
                dwFlags = 0
            };
            Marshal.StructureToPtr(hdr, hdrPtr, false);

            lock (_lock)
            {
                if (_device == IntPtr.Zero) return;
                if (waveOutPrepareHeader(_device, hdrPtr, WaveHdrSize) != MMSYSERR_NOERROR) return;
                headerPrepared = true;
                if (waveOutWrite(_device, hdrPtr, WaveHdrSize) != MMSYSERR_NOERROR) return;
            }

            while (true)
            {
                var hdrBack = Marshal.PtrToStructure<WAVEHDR>(hdrPtr);
                if ((hdrBack.dwFlags & WHDR_DONE) != 0) break;
                Thread.Sleep(1);
            }
        }
        finally
        {
            if (headerPrepared)
            {
                lock (_lock)
                {
                    if (_device != IntPtr.Zero)
                        waveOutUnprepareHeader(_device, hdrPtr, WaveHdrSize);
                }
            }
            Marshal.FreeHGlobal(hdrPtr);
            Marshal.FreeHGlobal(bufferPtr);
        }
    }

    private static byte[] ToByteArray(short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
