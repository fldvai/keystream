using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Keystream.Windows.Interception;

/// <summary>
/// Real keyboard names for the deck picker (§61). Interception only yields
/// hardware IDs, so names are read from Windows itself, in order:
/// 1. The HID product string (+ manufacturer) of the matching HID
///    interface path - the brand name burned into the device.
/// 2. The SetupDi friendly name / device description of the matching HID
///    device node.
/// Generic placeholders ("HID Keyboard Device", "USB Input Device", ...)
/// are rejected: a rejected name falls back to the VID/PID display, never
/// to an invented name. Every failure is swallowed, per device and overall
/// - the picker list must never fail because a name could not be read.
/// Twins (identical VID/PID) share one product string, so identical names
/// stay possible: the device number + hardware ID detail and the
/// "ID identik" warning remain the tiebreakers, plus the wizard.
/// Only interfaces/nodes matching a listed Interception device are opened,
/// so unrelated devices are never touched. Handles are opened read-only
/// (or access 0 as fallback) and never used for input.</summary>
public static class HidDeviceNameResolver
{
    private static readonly Guid HidInterfaceGuid = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    private static readonly Guid HidClassGuid = new("745A17A0-74D3-11D0-B6EC-00A0C90F57F");

    private const uint DIGCF_PRESENT = 0x2;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const uint SPDRP_DEVICEDESC = 0x0;
    private const uint SPDRP_HARDWAREID = 0x1;
    private const uint SPDRP_FRIENDLYNAME = 0xC;
    private static readonly IntPtr InvalidHandle = new(-1);
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 0x3;

    private static readonly string[] GenericFragments =
    {
        "hid keyboard device", "hid-compliant", "usb input device",
        "usb composite device", "hid device",
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize,
        out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property,
        out uint propertyRegDataType, byte[]? propertyBuffer, uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetProductString(IntPtr hidDeviceObject, byte[] buffer, uint bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetManufacturerString(IntPtr hidDeviceObject, byte[] buffer, uint bufferLength);

    private sealed record Target(
        InterceptionDeviceEnumerator.DeviceInfo Device, string Vid, string Pid, string? Mi);

    /// <summary>Hardware ID → real name, only for devices a name was found for.
    /// Never throws; an empty map means "keep the VID/PID display".</summary>
    public static Dictionary<string, string> Resolve(
        IReadOnlyList<InterceptionDeviceEnumerator.DeviceInfo> devices)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var targets = new List<Target>();
            foreach (var d in devices)
            {
                var m = Regex.Match(d.HardwareId,
                    @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})(?:&MI_([0-9A-Fa-f]{2}))?");
                if (!m.Success) continue;
                targets.Add(new Target(d,
                    m.Groups[1].Value.ToUpperInvariant(),
                    m.Groups[2].Value.ToUpperInvariant(),
                    m.Groups[3].Success ? m.Groups[3].Value.ToUpperInvariant() : null));
            }
            if (targets.Count == 0) return result;

            foreach (var (name, hwId) in ResolveFromHidInterfaces(targets))
                result[hwId] = name;
            var missing = targets.Where(t => !result.ContainsKey(t.Device.HardwareId)).ToList();
            if (missing.Count > 0)
                foreach (var (name, hwId) in ResolveFromDeviceNodes(missing))
                    result[hwId] = name;
        }
        catch
        {
            // Nama gagal total = daftar tetap tampil dengan VID/PID.
        }
        return result;
    }

    private static bool Matches(Target t, string vid, string pid, string? mi)
    {
        if (t.Vid != vid || t.Pid != pid) return false;
        if (t.Mi is not null && mi is not null && t.Mi != mi) return false;
        return true;
    }

    private static List<(string Name, string HardwareId)> ResolveFromHidInterfaces(List<Target> targets)
    {
        var found = new List<(string, string)>();
        var foundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IntPtr set;
        try
        {
            var guid = HidInterfaceGuid;
            set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        }
        catch
        {
            return found;
        }
        if (set == InvalidHandle) return found;
        try
        {
            uint index = 0;
            while (true)
            {
                SP_DEVICE_INTERFACE_DATA ifData;
                try
                {
                    ifData = new SP_DEVICE_INTERFACE_DATA
                    {
                        cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>(),
                    };
                    var guid = HidInterfaceGuid;
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index++, ref ifData))
                        break;
                }
                catch
                {
                    break;
                }
                string? path;
                try { path = GetInterfacePath(set, ref ifData); }
                catch { continue; }
                if (path is null) continue;
                var pm = Regex.Match(path,
                    @"vid_([0-9a-fA-F]{4})&pid_([0-9a-fA-F]{4})(?:&mi_([0-9a-fA-F]{2}))?");
                if (!pm.Success) continue;
                string vid = pm.Groups[1].Value.ToUpperInvariant();
                string pid = pm.Groups[2].Value.ToUpperInvariant();
                string? mi = pm.Groups[3].Success ? pm.Groups[3].Value.ToUpperInvariant() : null;
                var matched = targets.Where(t => Matches(t, vid, pid, mi)).ToList();
                if (matched.Count == 0) continue;
                if (matched.All(t => foundKeys.Contains(t.Device.HardwareId))) continue;
                string? name;
                try { name = ReadProductName(path); }
                catch { continue; }
                if (name is null) continue;
                foreach (var t in matched)
                {
                    if (foundKeys.Add(t.Device.HardwareId))
                        found.Add((name, t.Device.HardwareId));
                }
            }
        }
        finally
        {
            try { SetupDiDestroyDeviceInfoList(set); } catch { }
        }
        return found;
    }

    private static string? GetInterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA ifData)
    {
        if (!SetupDiGetDeviceInterfaceDetail(set, ref ifData, IntPtr.Zero, 0, out uint required, IntPtr.Zero)
            && required == 0)
            return null;
        IntPtr buf = Marshal.AllocHGlobal((int)required);
        try
        {
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref ifData, buf, required, out _, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringUni(buf + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static string? ReadProductName(string path)
    {
        foreach (uint access in new[] { GenericRead, 0u })
        {
            IntPtr handle;
            try
            {
                handle = CreateFile(path, access, FileShareRead | FileShareWrite,
                    IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            }
            catch
            {
                continue;
            }
            if (handle == InvalidHandle) continue;
            try
            {
                var buf = new byte[256];
                string? product = null;
                string? maker = null;
                try
                {
                    if (HidD_GetProductString(handle, buf, (uint)buf.Length))
                        product = CleanName(Encoding.Unicode.GetString(buf));
                }
                catch { }
                Array.Clear(buf, 0, buf.Length);
                try
                {
                    if (HidD_GetManufacturerString(handle, buf, (uint)buf.Length))
                        maker = CleanName(Encoding.Unicode.GetString(buf));
                }
                catch { }
                return Combine(maker, product);
            }
            finally
            {
                try { CloseHandle(handle); } catch { }
            }
        }
        return null;
    }

    private static List<(string Name, string HardwareId)> ResolveFromDeviceNodes(List<Target> targets)
    {
        var found = new List<(string, string)>();
        IntPtr set;
        try
        {
            var guid = HidClassGuid;
            set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DIGCF_PRESENT);
        }
        catch
        {
            return found;
        }
        if (set == InvalidHandle) return found;
        try
        {
            uint index = 0;
            while (true)
            {
                SP_DEVINFO_DATA info;
                try
                {
                    info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    if (!SetupDiEnumDeviceInfo(set, index++, ref info)) break;
                }
                catch
                {
                    break;
                }
                List<string> hwIds;
                try { hwIds = GetRegistryMultiString(set, ref info, SPDRP_HARDWAREID); }
                catch { continue; }
                if (hwIds.Count == 0) continue;
                foreach (var t in targets)
                {
                    bool match = false;
                    foreach (var h in hwIds)
                    {
                        if (h.IndexOf("VID_" + t.Vid, StringComparison.OrdinalIgnoreCase) < 0
                            || h.IndexOf("PID_" + t.Pid, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        if (t.Mi is not null)
                        {
                            var mm = Regex.Match(h, @"MI_([0-9A-Fa-f]{2})");
                            if (mm.Success && mm.Groups[1].Value.ToUpperInvariant() != t.Mi)
                                continue;
                        }
                        match = true;
                        break;
                    }
                    if (!match) continue;
                    string? name = null;
                    try { name = GetRegistryString(set, ref info, SPDRP_FRIENDLYNAME); }
                    catch { }
                    if (name is null)
                    {
                        try { name = GetRegistryString(set, ref info, SPDRP_DEVICEDESC); }
                        catch { }
                    }
                    if (name is not null) found.Add((name, t.Device.HardwareId));
                }
            }
        }
        finally
        {
            try { SetupDiDestroyDeviceInfoList(set); } catch { }
        }
        return found;
    }

    private static string? GetRegistryString(IntPtr set, ref SP_DEVINFO_DATA info, uint property)
    {
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, property, out _,
                null, 0, out uint required) && required == 0)
            return null;
        var buf = new byte[required];
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, property, out _,
                buf, (uint)buf.Length, out _))
            return null;
        return CleanName(Encoding.Unicode.GetString(buf));
    }

    private static List<string> GetRegistryMultiString(IntPtr set, ref SP_DEVINFO_DATA info, uint property)
    {
        var list = new List<string>();
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, property, out _,
                null, 0, out uint required) && required == 0)
            return list;
        var buf = new byte[required];
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, property, out _,
                buf, (uint)buf.Length, out _))
            return list;
        string all = Encoding.Unicode.GetString(buf);
        foreach (var part in all.Split('\0'))
        {
            string s = part.Trim();
            if (s.Length > 0) list.Add(s);
        }
        return list;
    }

    /// <summary>Null-terminated Unicode → trimmed single-space text, or null
    /// when empty or a generic placeholder.</summary>
    private static string? CleanName(string raw)
    {
        int nul = raw.IndexOf('\0');
        string s = (nul >= 0 ? raw[..nul] : raw).Trim();
        s = Regex.Replace(s, @"\s+", " ");
        if (s.Length == 0) return null;
        string lower = s.ToLowerInvariant();
        if (GenericFragments.Any(g => lower.Contains(g))) return null;
        return s;
    }

    private static string? Combine(string? maker, string? product)
    {
        if (product is null) return null;
        if (maker is null || maker.Length == 0) return product;
        if (product.IndexOf(maker, StringComparison.OrdinalIgnoreCase) >= 0) return product;
        if (GenericFragments.Any(g => maker.ToLowerInvariant().Contains(g))) return product;
        return maker + " " + product;
    }
}
