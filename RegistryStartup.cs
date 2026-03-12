using Microsoft.Win32;

namespace TouchBeep;

public static class RegistryStartup
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TouchBeep";

    #if NETFRAMEWORK
    private static string ExePath => "\"" + (Application.ExecutablePath ?? "") + "\"";
#else
    private static string ExePath => "\"" + (Environment.ProcessPath ?? Application.ExecutablePath ?? "") + "\"";
#endif

    /// <summary>Command-line suffix written to the Run key so the application starts minimized when launched at logon.</summary>
    private const string MinimizedSuffix = " --minimized";

    /// <summary>True if a Run entry exists for all users (HKLM).</summary>
    public static bool IsEnabledForAllUsers
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(KeyPath, false);
                var path = key?.GetValue(ValueName) as string;
                return !string.IsNullOrEmpty(path);
            }
            catch { return false; }
        }
    }

    /// <summary>True if a Run entry exists for the current user (HKCU).</summary>
    public static bool IsEnabledForCurrentUser
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath, false);
                var path = key?.GetValue(ValueName) as string;
                return !string.IsNullOrEmpty(path);
            }
            catch { return false; }
        }
    }

    /// <summary>True if run at logon is enabled for all users or the current user (used for the GUI checkbox).</summary>
    public static bool IsEnabled => IsEnabledForAllUsers || IsEnabledForCurrentUser;

    /// <summary>Enables run at logon for all users. Requires administrator rights. Returns true on success.</summary>
    public static bool EnableForAllUsers()
    {
        var path = ExePath;
        if (string.IsNullOrEmpty(path) || path == "\"\"") return false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, true);
            if (key == null) return false;
            key.SetValue(ValueName, path + MinimizedSuffix);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch { return false; }
    }

    /// <summary>Disables run at logon for all users. Requires administrator rights.</summary>
    public static void DisableForAllUsers()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, true);
            key?.DeleteValue(ValueName, false);
        }
        catch { /* Ignore registry errors */ }
    }

    /// <summary>Enables run at logon for the current user only. Administrator rights are not required.</summary>
    public static void EnableForCurrentUser()
    {
        var path = ExePath;
        if (string.IsNullOrEmpty(path) || path == "\"\"") return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
            key?.SetValue(ValueName, path + MinimizedSuffix);
        }
        catch { }
    }

    /// <summary>Disables run at logon for the current user.</summary>
    public static void DisableForCurrentUser()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
            key?.DeleteValue(ValueName, false);
        }
        catch { }
    }

    /// <summary>Enables run at logon. Used by the GUI when "Start with Windows" is checked. Tries HKLM (all users) first; falls back to HKCU if administrator rights are not available.</summary>
    public static void Enable()
    {
        if (EnableForAllUsers()) return;
        EnableForCurrentUser();
    }

    /// <summary>Disables run at logon from both HKLM and HKCU.</summary>
    public static void Disable()
    {
        DisableForAllUsers();
        DisableForCurrentUser();
    }
}
