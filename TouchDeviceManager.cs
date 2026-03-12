using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace TouchBeep;

/// <summary>Represents a single HID-compliant touch screen device as enumerated by the system.</summary>
public class TouchDeviceInfo
{
    public string InstanceId { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string MonitorName { get; set; } = "";
    public bool IsPrimaryMonitor { get; set; }
    public int Index { get; set; }
}

/// <summary>
/// Enumerates and enables or disables HID-compliant touch screen devices via SetupAPI.
/// Maps each touch device to its display monitor using GetPointerDevices.
/// </summary>
public static class TouchDeviceManager
{
    // ── SetupAPI constants ──────────────────────────────────────────────
    private static readonly Guid GUID_DEVCLASS_HIDCLASS = new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
    private const int DIGCF_PRESENT = 0x02;
    private const int SPDRP_DEVICEDESC = 0x00;
    private const int DIF_PROPERTYCHANGE = 0x12;
    private const int DICS_ENABLE = 1;
    private const int DICS_DISABLE = 2;
    private const int DICS_FLAG_GLOBAL = 1;
    private const int CR_SUCCESS = 0;

    // ── SetupAPI structures ─────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SP_CLASSINSTALL_HEADER
    {
        public int cbSize;
        public int InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SP_PROPCHANGE_PARAMS
    {
        public SP_CLASSINSTALL_HEADER ClassInstallHeader;
        public int StateChange;
        public int Scope;
        public int HwProfile;
    }

    // ── SetupAPI P/Invoke ───────────────────────────────────────────────
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        int property, out int propertyRegDataType,
        byte[]? propertyBuffer, int propertyBufferSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInstanceIdW(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        char[] deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiSetClassInstallParams(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        ref SP_PROPCHANGE_PARAMS classInstallParams, int classInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(int installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    // ── cfgmgr32 for device status ─────────────────────────────────────
    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out int status, out int problemNumber, uint devInst, int flags);

    // ── GetPointerDevices for monitor mapping ───────────────────────────
    private const int POINTER_DEVICE_TYPE_TOUCH = 0x03;
    private const int POINTER_DEVICE_PRODUCT_STRING_MAX = 520;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct POINTER_DEVICE_INFO
    {
        public uint displayOrientation;
        public uint pointerDeviceType;
        public IntPtr monitor;
        public uint startingCursorId;
        public ushort maxActiveContacts;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = POINTER_DEVICE_PRODUCT_STRING_MAX)]
        public string productString;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetPointerDevices(ref uint deviceCount, [Out] POINTER_DEVICE_INFO[]? pointerDevices);

    // ── GetMonitorInfo for monitor name ─────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private const int MONITORINFOF_PRIMARY = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    // ── EnumDisplayDevices for monitor friendly name (make/model) ────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevicesW(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    // ── Physical Monitor API (dxva2) for EDID monitor description ────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    // ── QueryDisplayConfig / DisplayConfigGetDeviceInfo (Control Panel monitor names) ─
    private const int QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const int ERROR_SUCCESS = 0;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public DISPLAYCONFIG_LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public DISPLAYCONFIG_LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public DISPLAYCONFIG_LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate; // 8 bytes, not uint
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    // DISPLAYCONFIG_MODE_INFO = 16-byte header + 48-byte union (largest member). Must match native size to avoid heap corruption.
    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public DISPLAYCONFIG_LUID adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        public byte[] unionPadding;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray, ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName);

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>Enumerates HID-compliant touch screen devices and maps each to its display monitor.</summary>
    public static List<TouchDeviceInfo> GetTouchDevices()
    {
        var devices = EnumerateHidTouchDevices();
        var monitorMap = GetPointerDeviceMonitorMap();
        var displayConfigNames = GetDisplayConfigMonitorNames();
        for (int i = 0; i < devices.Count; i++)
        {
            devices[i].Index = i + 1;
            if (i < monitorMap.Count)
            {
                devices[i].MonitorName = monitorMap[i].monitorName;
                devices[i].IsPrimaryMonitor = monitorMap[i].isPrimary;
            }
            // If the pointer map did not provide a name (or is empty), use DisplayConfig or WMI by device index
            if (string.IsNullOrWhiteSpace(devices[i].MonitorName))
            {
                if (i < displayConfigNames.Count && !string.IsNullOrWhiteSpace(displayConfigNames[i]))
                    devices[i].MonitorName = displayConfigNames[i];
                else
                    devices[i].MonitorName = GetMonitorNameFromWmiByDisplayIndex(i);
            }
        }
        return devices;
    }

    /// <summary>Enables or disables a device by instance ID. Returns true on success.</summary>
    public static bool SetDeviceEnabled(string instanceId, bool enabled)
    {
        var guid = GUID_DEVCLASS_HIDCLASS;
        IntPtr devs = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);
        if (devs == new IntPtr(-1)) return false;
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(devs, i, ref data); i++)
            {
                string id = GetDeviceInstanceId(devs, ref data);
                if (!string.Equals(id, instanceId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var pcp = new SP_PROPCHANGE_PARAMS
                {
                    ClassInstallHeader = new SP_CLASSINSTALL_HEADER
                    {
                        cbSize = Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                        InstallFunction = DIF_PROPERTYCHANGE
                    },
                    StateChange = enabled ? DICS_ENABLE : DICS_DISABLE,
                    Scope = DICS_FLAG_GLOBAL,
                    HwProfile = 0
                };

                if (!SetupDiSetClassInstallParams(devs, ref data, ref pcp, Marshal.SizeOf<SP_PROPCHANGE_PARAMS>()))
                    return false;
                return SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, devs, ref data);
            }
            return false;
        }
        finally { SetupDiDestroyDeviceInfoList(devs); }
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private static List<string>? _displayConfigNamesCache;

    /// <summary>Gets monitor friendly names (same as Control Panel and resolution dialog) via QueryDisplayConfig. Returns names in path order. Results are cached until the next refresh.</summary>
    private static List<string> GetDisplayConfigMonitorNames()
    {
        if (_displayConfigNamesCache != null)
            return _displayConfigNamesCache;
        var result = new List<string>();
        try
        {
            int err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
            if (err != ERROR_SUCCESS || pathCount == 0) { _displayConfigNamesCache = result; return result; }
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            for (int i = 0; i < modes.Length; i++)
                modes[i].unionPadding = new byte[48];
            err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (err != ERROR_SUCCESS) { _displayConfigNamesCache = result; return result; }
            for (int i = 0; i < pathCount; i++)
            {
                ref readonly var target = ref paths[i].targetInfo;
                var nameReq = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME)),
                        adapterId = target.adapterId,
                        id = target.id
                    }
                };
                if (DisplayConfigGetDeviceInfo(ref nameReq) == ERROR_SUCCESS)
                {
                    string name = (nameReq.monitorFriendlyDeviceName ?? "").Trim();
                    result.Add(name);
                }
                else
                    result.Add("");
            }
        }
        catch { }
        _displayConfigNamesCache = result;
        return result;
    }

    private static List<TouchDeviceInfo> EnumerateHidTouchDevices()
    {
        var result = new List<TouchDeviceInfo>();
        var guid = GUID_DEVCLASS_HIDCLASS;
        IntPtr devs = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);
        if (devs == new IntPtr(-1)) return result;
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(devs, i, ref data); i++)
            {
                string desc = GetDeviceStringProperty(devs, ref data, SPDRP_DEVICEDESC);
                if (desc.IndexOf("touch screen", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string instanceId = GetDeviceInstanceId(devs, ref data);
                bool isEnabled = IsDeviceEnabled(data.DevInst);

                result.Add(new TouchDeviceInfo
                {
                    InstanceId = instanceId,
                    Description = desc,
                    IsEnabled = isEnabled
                });
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devs); }
        return result;
    }

    private static string GetDeviceStringProperty(IntPtr devs, ref SP_DEVINFO_DATA data, int property)
    {
        SetupDiGetDeviceRegistryPropertyW(devs, ref data, property, out _, null, 0, out int size);
        if (size <= 0) return "";
        var buf = new byte[size];
        if (!SetupDiGetDeviceRegistryPropertyW(devs, ref data, property, out _, buf, size, out _))
            return "";
        return System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0');
    }

    private static string GetDeviceInstanceId(IntPtr devs, ref SP_DEVINFO_DATA data)
    {
        var buf = new char[512];
        if (!SetupDiGetDeviceInstanceIdW(devs, ref data, buf, buf.Length, out int needed))
            return "";
        return new string(buf, 0, Math.Max(0, needed - 1));
    }

    private static bool IsDeviceEnabled(uint devInst)
    {
        int ret = CM_Get_DevNode_Status(out int status, out int problem, devInst, 0);
        if (ret != CR_SUCCESS) return false;
        // DN_STARTED (0x08) indicates the device is running; problem code 22 is CM_PROB_DISABLED
        return problem != 22;
    }

    /// <summary>
    /// Resolves the monitor display name. Tries multiple sources in order; each fallback is used when the previous one fails, to support different drivers and configurations.
    /// Order: (1) DisplayConfig (Control Panel name), (2) Physical Monitor API (dxva2), (3) EnumDisplayDevices, (4) WMI WmiMonitorID, (5) "Display N".
    /// </summary>
    private static string GetMonitorFriendlyName(IntPtr hMonitor, string deviceName)
    {
        // 1. QueryDisplayConfig — same name as in Control Panel and resolution dialog (most reliable)
        if (!string.IsNullOrEmpty(deviceName))
        {
            int displayIndex = GetDisplayIndexFromDeviceName(deviceName);
            if (displayIndex >= 0)
            {
                var dcNames = GetDisplayConfigMonitorNames();
                if (displayIndex < dcNames.Count)
                {
                    string name = (dcNames[displayIndex] ?? "").Trim();
                    if (name.Length > 0)
                        return name;
                }
            }
        }

        // 2. Physical Monitor API (dxva2) — EDID monitor description (make/model)
        try
        {
            if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
            {
                var physical = new PHYSICAL_MONITOR[count];
                if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical))
                {
                    try
                    {
                        string desc = (physical[0].szPhysicalMonitorDescription ?? "").Trim();
                        if (desc.Length > 0 && !string.Equals(desc, "Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
                            return desc;
                    }
                    finally
                    {
                        DestroyPhysicalMonitors(count, physical);
                    }
                }
            }
        }
        catch { /* dxva2 or API unavailable */ }

        // 3. EnumDisplayDevices — second call with adapter DeviceName and iDevNum=0 for monitor DeviceString
        if (!string.IsNullOrEmpty(deviceName))
        {
            try
            {
                var dd = new DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (EnumDisplayDevicesW(deviceName, 0, ref dd, 0))
                {
                    string devStr = (dd.DeviceString ?? "").Trim();
                    if (devStr.Length > 0)
                        return devStr;
                }
            }
            catch { }
        }

        // 4. WMI WmiMonitorID — ManufacturerName and UserFriendlyName from EDID
        if (!string.IsNullOrEmpty(deviceName))
        {
            int displayIndex = GetDisplayIndexFromDeviceName(deviceName);
            if (displayIndex >= 0)
            {
                string wmiName = GetMonitorNameFromWmiByDisplayIndex(displayIndex);
                if (!string.IsNullOrEmpty(wmiName))
                    return wmiName;
            }
        }

        // 5. Fallback: format device name in a readable form (e.g. "Display 1")
        if (!string.IsNullOrEmpty(deviceName))
        {
            const string prefix = "\\\\.\\DISPLAY";
            if (deviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                deviceName.Length > prefix.Length &&
                int.TryParse(deviceName.Substring(prefix.Length), out int num))
                return $"Display {num}";
            return deviceName;
        }

        return "";
    }

    /// <summary>Parses a device name (e.g. \\.\DISPLAY1) to a 0-based display index, or returns -1 if invalid.</summary>
    private static int GetDisplayIndexFromDeviceName(string deviceName)
    {
        const string prefix = "\\\\.\\DISPLAY";
        if (!deviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || deviceName.Length <= prefix.Length)
            return -1;
        return int.TryParse(deviceName.Substring(prefix.Length), out int num) ? num - 1 : -1;
    }

    /// <summary>Returns an ordered list of monitor names from WMI WmiMonitorID (ManufacturerName and UserFriendlyName). Results are cached until the next call to GetTouchDevices.</summary>
    private static List<string>? _wmiMonitorNamesCache;

    private static string GetMonitorNameFromWmiByDisplayIndex(int displayIndex)
    {
        try
        {
            if (_wmiMonitorNamesCache == null)
            {
                _wmiMonitorNamesCache = new List<string>();
                using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorID");
                foreach (ManagementBaseObject obj in searcher.Get())
                {
                    string maker = DecodeWmiMonitorIdString(obj["ManufacturerName"]);
                    string friendly = DecodeWmiMonitorIdString(obj["UserFriendlyName"]);
                    string combined = string.IsNullOrWhiteSpace(maker)
                        ? (friendly ?? "")
                        : (string.IsNullOrWhiteSpace(friendly) ? maker : $"{maker} {friendly}".Trim());
                    _wmiMonitorNamesCache.Add(combined.Trim());
                }
            }
            if (displayIndex >= 0 && displayIndex < _wmiMonitorNamesCache.Count)
            {
                string name = _wmiMonitorNamesCache[displayIndex];
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch { }
        return "";
    }

    /// <summary>Decodes a WmiMonitorID byte or ushort array (EDID format) to an ASCII string.</summary>
    private static string DecodeWmiMonitorIdString(object? value)
    {
        if (value == null) return "";
        byte[]? bytes = null;
        if (value is byte[] ba)
            bytes = ba;
        else if (value is ushort[] ua)
        {
            bytes = new byte[ua.Length];
            for (int i = 0; i < ua.Length; i++)
                bytes[i] = (byte)(ua[i] & 0xFF);
        }
        else if (value is Array arr && arr.Rank == 1)
        {
            try
            {
                if (arr.GetValue(0) is byte)
                {
                    bytes = new byte[arr.Length];
                    for (int i = 0; i < arr.Length; i++)
                        bytes[i] = Convert.ToByte(arr.GetValue(i));
                }
                else
                {
                    bytes = new byte[arr.Length];
                    for (int i = 0; i < arr.Length; i++)
                        bytes[i] = (byte)(Convert.ToUInt16(arr.GetValue(i)) & 0xFF);
                }
            }
            catch { return ""; }
        }
        if (bytes == null || bytes.Length == 0) return "";
        int len = 0;
        while (len < bytes.Length && bytes[len] != 0) len++;
        if (len == 0) return "";
        return Encoding.ASCII.GetString(bytes, 0, len).Trim();
    }

    /// <summary>
    /// Builds an ordered list of (monitor name, is-primary) for touch devices using GetPointerDevices.
    /// Requires Windows 8 or later; returns an empty list if the API is unavailable.
    /// </summary>
    private static List<(string monitorName, bool isPrimary)> GetPointerDeviceMonitorMap()
    {
        _wmiMonitorNamesCache = null;
        _displayConfigNamesCache = null; // Reset caches so the next call uses fresh data
        var result = new List<(string, bool)>();
        try
        {
            uint count = 0;
            if (!GetPointerDevices(ref count, null) || count == 0) return result;
            var infos = new POINTER_DEVICE_INFO[count];
            if (!GetPointerDevices(ref count, infos)) return result;

            int touchIndex = 0;
            foreach (var info in infos)
            {
                if (info.pointerDeviceType != POINTER_DEVICE_TYPE_TOUCH) continue;
                string monName = "";
                bool isPrimary = false;
                if (info.monitor != IntPtr.Zero)
                {
                    var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                    if (GetMonitorInfoW(info.monitor, ref mi))
                    {
                        isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                        monName = GetMonitorFriendlyName(info.monitor, mi.szDevice ?? "");
                    }
                }
                // If no monitor handle or name is available, use WMI by touch index (order typically matches display order)
                if (string.IsNullOrEmpty(monName))
                    monName = GetMonitorNameFromWmiByDisplayIndex(touchIndex);
                touchIndex++;
                result.Add((monName, isPrimary));
            }
        }
        catch { }
        return result;
    }
}
