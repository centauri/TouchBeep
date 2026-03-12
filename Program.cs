using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;

namespace TouchBeep;

static class Program
{
    /// <summary>True when the application was launched with --minimized (e.g. from the Run key at logon). MainForm starts minimized to the system tray.</summary>
    internal static bool StartMinimized { get; private set; }
    internal static bool IsElevated { get; private set; }
    internal static bool UserModeRequested { get; private set; }

    [STAThread]
    static int Main(string[] args)
    {
        IsElevated = IsProcessElevated();
        bool userModeArg = args.Any(a => IsUserModeArg(a.Trim()));
        bool elevatedArg = args.Any(a => IsElevatedArg(a.Trim()));
        if (userModeArg && elevatedArg)
        {
            Console.WriteLine("Cannot use --user-mode and --elevated together.");
            return 1;
        }

        var effectiveArgs = args.Where(a =>
        {
            var x = a.Trim();
            return !IsUserModeArg(x) && !IsElevatedArg(x);
        }).ToArray();

        if (userModeArg && IsElevated)
        {
            if (TryRestartAsUserMode(effectiveArgs))
                return 0;
            Console.WriteLine("Could not restart in user mode; continuing elevated.");
        }

        if (elevatedArg && !IsElevated)
        {
            if (TryRestartAsElevated(effectiveArgs))
                return 0;
            Console.WriteLine("Failed to start elevated. Run with --user-mode to stay non-elevated.");
            return 2;
        }

        UserModeRequested = userModeArg;

        ParseSoundArgs(effectiveArgs, out var firstCmdIndex);
        ParseFilterArgs(effectiveArgs);
        if (effectiveArgs.Any(a => IsFilterListArg(a.Trim())))
            return RunFilterList();
        if (effectiveArgs.Any(a => IsTouchListArg(a.Trim())))
            return RunTouchList();
        int touchAction = ParseTouchDeviceArgs(effectiveArgs);
        if (touchAction != -1) return touchAction;
        var arg = firstCmdIndex < effectiveArgs.Length ? effectiveArgs[firstCmdIndex].Trim().ToLowerInvariant() : "";
        if (arg == "install" || arg == "-install" || arg == "/install")
            return RunInstall(allUsers: true);
        if (arg == "uninstall" || arg == "-uninstall" || arg == "/uninstall")
            return RunUninstall(allUsers: true);
        if (arg == "install-user" || arg == "-install-user" || arg == "/install-user")
            return RunInstall(allUsers: false);
        if (arg == "uninstall-user" || arg == "-uninstall-user" || arg == "/uninstall-user")
            return RunUninstall(allUsers: false);
        if (arg == "?" || arg == "-?" || arg == "/?" || arg == "help" || arg == "-help")
            return PrintHelp();

        if (!UserModeRequested && !IsElevated)
        {
            if (TryRestartAsElevated(effectiveArgs))
                return 0;
            // If elevation was canceled or failed, continue in user mode.
            UserModeRequested = true;
        }

        IsElevated = IsProcessElevated();
        StartMinimized = effectiveArgs.Any(a => string.Equals(a.Trim(), "--minimized", StringComparison.OrdinalIgnoreCase));

        #if NETFRAMEWORK
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#else
        ApplicationConfiguration.Initialize();
#endif
        Application.Run(new MainForm());
        return 0;
    }

    private static void ParseSoundArgs(string[] args, out int firstCmdIndex)
    {
        firstCmdIndex = -1;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i].Trim().ToLowerInvariant();
            if ((a == "--tone" || a == "-tone" || a == "/tone") && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int hz) && hz >= 200 && hz <= 4000)
                    Settings.Default.FrequencyHz = hz;
                i++;
                continue;
            }
            if (a.StartsWith("tone=") && int.TryParse(a.Substring(5).Trim(), out int toneHz) && toneHz >= 200 && toneHz <= 4000)
            {
                Settings.Default.FrequencyHz = toneHz;
                continue;
            }
            if ((a == "--wave" || a == "-wave" || a == "/wave") && i + 1 < args.Length)
            {
                ApplyWave(args[i + 1]);
                i++;
                continue;
            }
            if (a.StartsWith("wave="))
            {
                ApplyWave(a.Substring(5).Trim());
                continue;
            }
            if ((a == "install" || a == "uninstall" || a == "install-user" || a == "uninstall-user" ||
                 a == "-install" || a == "-uninstall" || a == "help" || a == "-help" || a == "?" || a == "-?") && firstCmdIndex < 0)
                firstCmdIndex = i;
        }
        if (firstCmdIndex < 0) firstCmdIndex = 0;
    }

    private static void ApplyWave(string value)
    {
        var v = value.Trim().ToLowerInvariant();
        if (v == "sine") Settings.Default.WaveType = 0;
        else if (v == "square") Settings.Default.WaveType = 1;
        else if (v == "triangle") Settings.Default.WaveType = 2;
    }

    private static bool IsFilterListArg(string a)
    {
        var x = a.ToLowerInvariant();
        return x == "--filter-list" || x == "-filter-list" || x == "/filter-list";
    }

    private static void ParseFilterArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i].Trim().ToLowerInvariant();
            if ((a == "--filter-add" || a == "-filter-add" || a == "/filter-add") && i + 1 < args.Length)
            {
                FilterAdd(args[i + 1].Trim());
                i++;
            }
            else if ((a == "--filter-remove" || a == "-filter-remove" || a == "/filter-remove") && i + 1 < args.Length)
            {
                FilterRemove(args[i + 1].Trim());
                i++;
            }
            else if (a == "--filter-clear" || a == "-filter-clear" || a == "/filter-clear")
                FilterClear();
        }
    }

    private static void FilterAdd(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        var list = Settings.Default.AllowedProcesses;
        if (list.Any(p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase))) return;
        list.Add(processName);
        Settings.Default.AllowedProcesses = list;
    }

    private static void FilterRemove(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        var list = Settings.Default.AllowedProcesses;
        list.RemoveAll(p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase));
        Settings.Default.AllowedProcesses = list;
    }

    private static void FilterClear()
    {
        Settings.Default.AllowedProcesses = new List<string>();
    }

    private static int RunFilterList()
    {
        var list = Settings.Default.AllowedProcesses;
        if (list == null || list.Count == 0)
            Console.WriteLine("(empty - beep for all apps)");
        else
            foreach (var p in list)
                Console.WriteLine(p);
        return 0;
    }

    private static bool IsTouchListArg(string a)
    {
        var x = a.ToLowerInvariant();
        return x == "--list-touch" || x == "-list-touch" || x == "/list-touch";
    }

    private static int RunTouchList()
    {
        var devices = TouchDeviceManager.GetTouchDevices();
        if (devices.Count == 0)
        {
            Console.WriteLine("No HID-compliant touch screen devices found.");
            return 0;
        }
        Console.WriteLine($"{"#",-4}{"Status",-10}{"Monitor",-20}{"Description"}");
        Console.WriteLine(new string('-', 60));
        foreach (var d in devices)
        {
            string mon = string.IsNullOrEmpty(d.MonitorName) ? "?" : d.MonitorName;
            if (d.IsPrimaryMonitor) mon += " (primary)";
            Console.WriteLine($"{d.Index,-4}{(d.IsEnabled ? "Enabled" : "Disabled"),-10}{mon,-20}{d.Description}");
        }
        return 0;
    }

    /// <summary>
    /// Handles --disable-touch N and --enable-touch N. Returns the appropriate exit code, or -1 if no touch-related argument was found.
    /// </summary>
    private static int ParseTouchDeviceArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i].Trim().ToLowerInvariant();
            bool isDisable = a == "--disable-touch" || a == "-disable-touch" || a == "/disable-touch";
            bool isEnable  = a == "--enable-touch"  || a == "-enable-touch"  || a == "/enable-touch";
            if (!isDisable && !isEnable) continue;
            if (i + 1 >= args.Length || !int.TryParse(args[i + 1].Trim(), out int idx) || idx < 1)
            {
                Console.WriteLine($"Usage: {a} <device number>  (use --list-touch to see numbers)");
                return 1;
            }
            var devices = TouchDeviceManager.GetTouchDevices();
            if (idx > devices.Count)
            {
                Console.WriteLine($"Device #{idx} not found. Only {devices.Count} touch device(s) detected.");
                return 1;
            }
            var dev = devices[idx - 1];
            bool targetState = isEnable;
            if (TouchDeviceManager.SetDeviceEnabled(dev.InstanceId, targetState))
            {
                Console.WriteLine($"Touch device #{idx} ({dev.Description}) {(targetState ? "enabled" : "disabled")}.");
                return 0;
            }
            Console.WriteLine($"Failed to {(targetState ? "enable" : "disable")} device #{idx}. Run as administrator.");
            return 2;
        }
        return -1;
    }

    private static int RunInstall(bool allUsers)
    {
        if (allUsers)
        {
            if (!RegistryStartup.EnableForAllUsers())
            {
                Console.WriteLine("TouchBeep install failed: need Administrator rights for all-users. Run as admin or use: TouchBeep.exe install-user");
                return 2;
            }
            Console.WriteLine("TouchBeep installed for all users (start at logon).");
        }
        else
        {
            RegistryStartup.EnableForCurrentUser();
            Console.WriteLine("TouchBeep installed for current user (start at logon).");
        }
        return 0;
    }

    private static int RunUninstall(bool allUsers)
    {
        if (allUsers)
        {
            RegistryStartup.DisableForAllUsers();
            Console.WriteLine("TouchBeep uninstalled (all users).");
        }
        else
        {
            RegistryStartup.DisableForCurrentUser();
            Console.WriteLine("TouchBeep uninstalled (current user).");
        }
        return 0;
    }

    private static int PrintHelp()
    {
        Console.WriteLine(@"TouchBeep - Beep on touch for Windows (kiosk / POS).

Usage:
  TouchBeep.exe              Start the app (GUI).
  TouchBeep.exe --user-mode  Start GUI without elevation (limited app coverage).
  TouchBeep.exe --elevated   Start GUI elevated (prompts UAC if needed).
  TouchBeep.exe install      Install for ALL USERS (start at logon). Requires admin.
  TouchBeep.exe uninstall    Remove from all-users startup.
  TouchBeep.exe install-user Install for current user only (no admin).
  TouchBeep.exe uninstall-user Remove from current-user startup.
  TouchBeep.exe help         Show this help.

Sound (saved to settings; use with install or before starting GUI):
  --tone <Hz>    Frequency 200-4000 (e.g. --tone 1000).
  --wave <name>  sine | square | triangle.
  Can also use: tone=800 wave=sine

Filter (beep only when touching selected apps; empty list = all apps):
  --filter-add <name>     Add process name (e.g. Calculator).
  --filter-remove <name>  Remove from list.
  --filter-clear         Clear list (beep for all apps).
  --filter-list          Print current list and exit.

Examples:
  TouchBeep.exe install --tone 1000 --wave sine --filter-add Calculator
  TouchBeep.exe --filter-add notepad --filter-add mspaint
  TouchBeep.exe --filter-remove Calculator
  TouchBeep.exe --filter-clear
  TouchBeep.exe --filter-list

Touch device control (requires admin):
  --list-touch              List connected touchscreens and their status.
  --disable-touch <n>       Disable touchscreen #n (from --list-touch).
  --enable-touch <n>        Re-enable touchscreen #n.

Examples:
  TouchBeep.exe --list-touch
  TouchBeep.exe --disable-touch 2
  TouchBeep.exe --enable-touch 2");
        return 0;
    }

    private static bool IsUserModeArg(string a)
    {
        var x = a.ToLowerInvariant();
        return x == "--user-mode" || x == "-user-mode" || x == "/user-mode";
    }

    private static bool IsElevatedArg(string a)
    {
        var x = a.ToLowerInvariant();
        return x == "--elevated" || x == "-elevated" || x == "/elevated";
    }

    internal static bool RestartWithMode(bool userMode, bool minimized = false)
    {
        var args = new List<string>();
        args.Add(userMode ? "--user-mode" : "--elevated");
        if (minimized)
            args.Add("--minimized");
        return userMode ? TryRestartAsUserMode(args.ToArray()) : TryRestartAsElevated(args.ToArray());
    }

    private static bool TryRestartAsElevated(string[] args)
    {
        var exePath = GetExePath();
        if (string.IsNullOrEmpty(exePath)) return false;
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = BuildArgString(args),
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
        };
        try
        {
            var p = Process.Start(psi);
            return p != null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // UAC was canceled by the user
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRestartAsUserMode(string[] args)
    {
        var exePath = GetExePath();
        if (string.IsNullOrEmpty(exePath)) return false;
        string exe = exePath;
        var argString = BuildArgString(args.Prepend(exe));
        try
        {
            // Shell.Application executes via Explorer's shell context, which can start medium IL from an elevated app.
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                var shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    shellType.InvokeMember(
                        "ShellExecute",
                        BindingFlags.InvokeMethod,
                        binder: null,
                        target: shell,
                        args: new object[] { exe, BuildArgString(args), Path.GetDirectoryName(exe) ?? "", "open", 1 });
                    return true;
                }
            }
        }
        catch
        {
            // Fall back to the alternative method below.
        }

        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = argString,
                UseShellExecute = true
            });
            return p != null;
        }
        catch
        {
            return false;
        }
    }

    private static string GetExePath()
    {
#if NETFRAMEWORK
        return Application.ExecutablePath ?? "";
#else
        return Environment.ProcessPath ?? Application.ExecutablePath ?? "";
#endif
    }

    private static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity == null) return false;
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildArgString(IEnumerable<string> args)
    {
        return string.Join(" ", args.Select(QuoteArg));
    }

    private static string QuoteArg(string arg)
    {
        if (string.IsNullOrEmpty(arg))
            return "\"\"";
        if (arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            return arg;

        var sb = new System.Text.StringBuilder();
        sb.Append('"');
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
                continue;
            }

            sb.Append('\\', backslashes);
            sb.Append(c);
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}
