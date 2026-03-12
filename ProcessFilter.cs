using System.Linq;
using System.Runtime.InteropServices;

namespace TouchBeep;

public static class ProcessFilter
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>
    /// Returns true if a beep should be played: an empty list means beep for all processes; otherwise beep only when the foreground process is in the list (case-insensitive).
    /// </summary>
    public static bool ShouldBeep(List<string> allowedProcesses)
    {
        if (allowedProcesses == null || allowedProcesses.Count == 0)
            return true;
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            if (GetWindowThreadProcessId(hwnd, out uint pid) == 0) return false;
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            string name = proc.ProcessName ?? "";
            return allowedProcesses.Any(p => string.Equals(p.Trim(), name, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
