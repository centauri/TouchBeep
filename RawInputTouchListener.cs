using System.Runtime.InteropServices;

namespace TouchBeep;

/// <summary>
/// Receives touch input via the Raw Input API (digitizer). Works when the user touches the taskbar, Start menu, Edge, and similar surfaces,
/// because those use the pointer/touch path and do not always generate mouse messages.
/// </summary>
public sealed class RawInputTouchListener : IDisposable
{
    private readonly Action _onTouch;
    private bool _registered;
    private bool _disposed;

    private const int WM_INPUT = 0x00FF;
    private const int RIDEV_INPUTSINK = 0x00000100;
    private const int RID_INPUT = 0x10000003;
    private const ushort HID_USAGE_PAGE_DIGITIZER = 0x0D;
    private const ushort HID_USAGE_TOUCH_SCREEN = 0x04;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref int pcbSize, int cbSizeHeader);

    private const int RAWINPUTHEADER_SIZE = 24; // sizeof(RAWINPUTHEADER) approx; we only need to read header.type

    public const int WmInput = WM_INPUT;

    public RawInputTouchListener(Action onTouch)
    {
        _onTouch = onTouch ?? (() => { });
    }

    /// <summary>
    /// Registers to receive raw touch input. Call from the UI thread when the window has a valid handle.
    /// Tries touch screen usage (0x04) first, then the whole digitizer page (0x00) as a fallback.
    /// </summary>
    public bool Register(IntPtr hwndTarget)
    {
        if (hwndTarget == IntPtr.Zero || _disposed) return false;
        var size = (uint)Marshal.SizeOf<RAWINPUTDEVICE>();
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = HID_USAGE_PAGE_DIGITIZER,
            usUsage = HID_USAGE_TOUCH_SCREEN,
            dwFlags = RIDEV_INPUTSINK,
            hwndTarget = hwndTarget
        };
        _registered = RegisterRawInputDevices(new[] { device }, 1, size);
        if (!_registered && Marshal.GetLastWin32Error() != 0)
        {
            device.usUsage = 0; // all digitizer usages (touch + pen)
            _registered = RegisterRawInputDevices(new[] { device }, 1, size);
        }
        return _registered;
    }

    /// <summary>
    /// Process WM_INPUT (lParam). Forwards every report to callback; MainForm uses gap logic so only first report of a touch beeps.
    /// </summary>
    public bool ProcessInput(IntPtr lParam)
    {
        if (!_registered || lParam == IntPtr.Zero) return false;
        int size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, RAWINPUTHEADER_SIZE);
        if (size < RAWINPUTHEADER_SIZE) return false;
        try { _onTouch(); } catch { /* Ignore callback errors */ }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _registered = false;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
