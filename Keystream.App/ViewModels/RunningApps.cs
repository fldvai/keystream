using Keystream.Core.Localization;
namespace Keystream.App.ViewModels;

/// <summary>One currently-running app with a visible window, offered as
/// an auto-profile trigger candidate.</summary>
public sealed record RunningAppOption(string Exe, string Display)
{
    public override string ToString() => Display;
}

/// <summary>Lists running windowed apps for pickers (auto-profile
/// triggers). Best-effort: a process exiting mid-scan is skipped, and
/// any failure yields an empty list rather than an error.</summary>
public static class RunningApps
{
    public static List<RunningAppOption> List()
    {
        var result = new List<RunningAppOption>();
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var proc in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (proc.MainWindowHandle == IntPtr.Zero) continue;
                    string exe = proc.ProcessName + Localizer.Get("S0837");
                    if (!seen.Add(exe)) continue;
                    string title = string.IsNullOrWhiteSpace(proc.MainWindowTitle) ? "" : " — " + proc.MainWindowTitle;
                    result.Add(new RunningAppOption(exe, exe + title));
                }
                catch { /* process exited mid-scan */ }
            }
        }
        catch { /* listing is a convenience, never fatal */ }
        return result;
    }

    /// <summary>Every app that owns ANY top-level window - enumerated
    /// the way the reference picker does it (EnumWindows), not just
    /// processes with a "main" window, which hid most apps (v2.9.25).
    /// Carries the full .exe path via QueryFullProcessImageName (works
    /// for more processes than MainModule). Apps running elevated stay
    /// invisible to a non-elevated app - a Windows integrity rule, not
    /// a filter of ours.</summary>
    public static List<RunningAppOption> ListWithPaths()
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            EnumWindows((hWnd, _) =>
            {
                try
                {
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid == 0) return true;
                    string? path = QueryProcessPath(pid) ?? MainModulePath(pid);
                    if (string.IsNullOrWhiteSpace(path)) return true;
                    string title = WindowTitle(hWnd);
                    if (title is "Default IME" or "MSCTFIME UI") title = "";
                    if (!titles.TryGetValue(path, out string? cur) || (cur.Length == 0 && title.Length > 0))
                        titles[path] = title;
                }
                catch { /* a window vanished mid-scan */ }
                return true;
            }, IntPtr.Zero);
        }
        catch { /* listing is a convenience, never fatal */ }

        // Fallback sweep: windowed processes the enumeration missed.
        try
        {
            foreach (var proc in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (proc.MainWindowHandle == IntPtr.Zero) continue;
                    string? path = proc.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path) || titles.ContainsKey(path)) continue;
                    titles[path] = string.IsNullOrWhiteSpace(proc.MainWindowTitle) ? "" : proc.MainWindowTitle;
                }
                catch { /* exited mid-scan */ }
            }
        }
        catch { }

        return titles
            .Select(kv => new RunningAppOption(kv.Key,
                System.IO.Path.GetFileName(kv.Key) + (kv.Value.Length > 0 ? " — " + kv.Value : "")))
            .OrderBy(o => o.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string WindowTitle(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len <= 0) return "";
        var sb = new System.Text.StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    private static string? QueryProcessPath(uint pid)
    {
        IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    private static string? MainModulePath(uint pid)
    {
        try { return System.Diagnostics.Process.GetProcessById((int)pid).MainModule?.FileName; }
        catch { return null; }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, System.Text.StringBuilder exeName, ref uint size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
