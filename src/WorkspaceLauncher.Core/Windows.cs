using System.Runtime.InteropServices;

namespace WorkspaceLauncher.Core;

/// <summary>Top-level window helpers. "Real" windows = visible, titled, un-owned, not tool windows (what Alt+Tab shows).</summary>
public static class Windows
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);

    const uint GW_OWNER = 4, WM_CLOSE = 0x0010;
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TOOLWINDOW = 0x80;

    public static HashSet<IntPtr> Snapshot()
    {
        var set = new HashSet<IntPtr>();
        EnumWindows((h, _) =>
        {
            if (IsReal(h)) set.Add(h);
            return true;
        }, IntPtr.Zero);
        return set;
    }

    static bool IsReal(IntPtr h) =>
        IsWindowVisible(h)
        && GetWindowTextLength(h) > 0
        && GetWindow(h, GW_OWNER) == IntPtr.Zero
        && (GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) == 0;

    public static int PidOf(IntPtr h)
    {
        GetWindowThreadProcessId(h, out var pid);
        return (int)pid;
    }

    public static List<IntPtr> OfProcess(int pid) => Snapshot().Where(h => PidOf(h) == pid).ToList();

    public static bool IsAlive(IntPtr h) => IsWindow(h);

    /// <summary>Asks the window to close (like clicking X); the app may still prompt to save.</summary>
    public static void Close(IntPtr h) => PostMessageW(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
}
