using System.Runtime.InteropServices;

namespace TouchBeep;

/// <summary>
/// Low-level mouse hook that reacts only to touch-screen events (not mouse clicks).
/// Windows converts touch to WM_LBUTTONDOWN and marks it via dwExtraInfo (MOUSE_EVENT_FROM_TOUCH).
/// </summary>
public sealed class TouchHook : IDisposable
{
    private readonly Action _onTouch;
    private readonly object _lock = new();
    private DateTime _lastBeep = DateTime.MinValue;
    /// <summary>Ensures one beep per touch; repeat events while the finger is held are ignored.</summary>
    private const int DebounceMs = 500;
    private nint _hookId = IntPtr.Zero;
    private readonly GCHandle _callbackHandle;
    private bool _disposed;

    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;

    // Windows uses this in dwExtraInfo when a mouse message was generated from touch/pen
    private const uint MOUSE_EVENT_FROM_TOUCH_PEN_MASK = 0xFF515700;
    private const uint TOUCH_FLAG = 0x80; // within that signature: 0x80 = touch, 0 = pen

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public int ptX;
        public int ptY;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    public TouchHook(Action onTouch)
    {
        _onTouch = onTouch ?? (() => { });
        LowLevelMouseProc proc = HookCallback;
        _callbackHandle = GCHandle.Alloc(proc);
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException("SetWindowsHookEx failed: " + Marshal.GetLastWin32Error());
    }

    private static bool IsTouchEvent(nint lParam)
    {
        try
        {
            var hook = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            ulong extra = hook.dwExtraInfo.ToUInt64();
            // Must match the touch/pen signature and have the touch bit set (not pen)
            return ((extra & 0xFFFFFF00) == MOUSE_EVENT_FROM_TOUCH_PEN_MASK) && ((extra & TOUCH_FLAG) == TOUCH_FLAG);
        }
        catch
        {
            return false;
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && wParam == (nint)WM_LBUTTONDOWN && IsTouchEvent(lParam))
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastBeep).TotalMilliseconds >= DebounceMs)
                {
                    _lastBeep = now;
                    try { _onTouch(); } catch { /* Ignore callback errors */ }
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _callbackHandle.Free();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
