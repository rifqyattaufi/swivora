using System.Diagnostics;
using System.Runtime.InteropServices;

// Feasibility spike: proves create / move / switch / remove virtual desktop works on this Windows build.
// Needs VirtualDesktopAccessor.dll next to the exe (see plan). Throwaway: not referenced by the product.
static class Vda
{
    const string Dll = "VirtualDesktopAccessor.dll";
    [DllImport(Dll)] public static extern int GetDesktopCount();
    [DllImport(Dll)] public static extern int GetCurrentDesktopNumber();
    [DllImport(Dll)] public static extern int CreateDesktop();
    [DllImport(Dll)] public static extern int RemoveDesktop(int remove, int fallback);
    [DllImport(Dll)] public static extern int GoToDesktopNumber(int n);
    [DllImport(Dll)] public static extern int MoveWindowToDesktopNumber(IntPtr hwnd, int n);
    [DllImport(Dll)] public static extern int GetWindowDesktopNumber(IntPtr hwnd);
}

static class Program
{
    static int failures;

    static void Check(string label, bool ok)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label}");
        if (!ok) failures++;
    }

    static int Main()
    {
        var startCount = Vda.GetDesktopCount();
        var origin = Vda.GetCurrentDesktopNumber();
        Check($"read state (count={startCount}, current={origin})", startCount > 0 && origin >= 0);
        if (failures > 0) return 1;

        var created = -1;
        Process? app = null;
        try
        {
            created = Vda.CreateDesktop();
            Check($"CreateDesktop -> index {created}", created >= 0 && Vda.GetDesktopCount() == startCount + 1);

            app = Process.Start(new ProcessStartInfo("winver.exe") { UseShellExecute = true })!;
            while (app.MainWindowHandle == IntPtr.Zero && !app.HasExited) { Thread.Sleep(100); app.Refresh(); }
            Check("MoveWindowToDesktopNumber", Vda.MoveWindowToDesktopNumber(app.MainWindowHandle, created) == 1
                && Vda.GetWindowDesktopNumber(app.MainWindowHandle) == created);

            Check("GoToDesktopNumber(new)", Vda.GoToDesktopNumber(created) == 1 && Vda.GetCurrentDesktopNumber() == created);
            Thread.Sleep(1500); // look at the screen: the winver window should be visible here
            Check("GoToDesktopNumber(origin)", Vda.GoToDesktopNumber(origin) == 1 && Vda.GetCurrentDesktopNumber() == origin);
        }
        finally
        {
            if (app is { HasExited: false }) { app.CloseMainWindow(); app.WaitForExit(3000); }
            if (created >= 0)
                Check("RemoveDesktop", Vda.RemoveDesktop(created, origin) == 1 && Vda.GetDesktopCount() == startCount);
        }

        Console.WriteLine(failures == 0 ? "SPIKE OK" : $"SPIKE FAILED ({failures})");
        return failures == 0 ? 0 : 1;
    }
}
