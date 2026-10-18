using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Swivora.Core;

/// <summary>Virtual desktop control. Desktops are identified by GUID because indices shift when any desktop is removed.</summary>
public interface IDesktopBackend
{
    /// <summary>False = degraded mode: nothing is isolated, apps still launch.</summary>
    bool IsAvailable { get; }
    Guid Current();
    Guid Create();
    bool Exists(Guid desktop);
    void GoTo(Guid desktop);
    void Move(IntPtr hwnd, Guid desktop);
    void Remove(Guid desktop, Guid fallback);
}

/// <summary>Used when virtual desktops are unsupported: every call is a harmless no-op.</summary>
public sealed class NullDesktopBackend(string reason) : IDesktopBackend
{
    public string Reason { get; } = reason;
    public bool IsAvailable => false;
    public Guid Current() => Guid.Empty;
    public Guid Create() => Guid.NewGuid();
    public bool Exists(Guid desktop) => true;
    public void GoTo(Guid desktop) { }
    public void Move(IntPtr hwnd, Guid desktop) { }
    public void Remove(Guid desktop, Guid fallback) { }
}

/// <summary>VirtualDesktopAccessor.dll (MIT, Ciantic). Its internal COM ABI is tied to Windows builds, so it is gated by build number.</summary>
public sealed class VdaDesktopBackend : IDesktopBackend
{
    const string Dll = "VirtualDesktopAccessor.dll";
    public const int MinBuild = 26100, MinRevision = 2605;

    [DllImport(Dll)] static extern int GetDesktopCount();
    [DllImport(Dll)] static extern int GetCurrentDesktopNumber();
    [DllImport(Dll)] static extern int CreateDesktop();
    [DllImport(Dll)] static extern int RemoveDesktop(int remove, int fallback);
    [DllImport(Dll)] static extern int GoToDesktopNumber(int n);
    [DllImport(Dll)] static extern int MoveWindowToDesktopNumber(IntPtr hwnd, int n);
    [DllImport(Dll)] static extern Guid GetDesktopIdByNumber(int n);
    [DllImport(Dll)] static extern int GetDesktopNumberById(Guid id);

    public bool IsAvailable => true;

    /// <summary>Real backend when this Windows build and the DLL support it, otherwise a null backend that says why.</summary>
    public static IDesktopBackend Create(out string? warning)
    {
        warning = Unsupported();
        return warning is null ? new VdaDesktopBackend() : new NullDesktopBackend(warning);
    }

    static string? Unsupported()
    {
        if (!OperatingSystem.IsWindows()) return "Virtual desktops need Windows.";
        var (build, revision) = WindowsBuild();
        if (build < MinBuild || (build == MinBuild && revision < MinRevision))
            return $"Windows build {build}.{revision} is older than {MinBuild}.{MinRevision}; running without separate desktops.";
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, Dll)))
            return $"{Dll} not found next to the app; running without separate desktops.";
        try { GetDesktopCount(); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return $"{Dll} failed to load ({e.GetType().Name}); running without separate desktops."; }
        return null;
    }

    static (int build, int revision) WindowsBuild()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        _ = int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out var build);
        var revision = key?.GetValue("UBR") is int r ? r : 0;
        return (build, revision);
    }

    public Guid Current() => IdOf(GetCurrentDesktopNumber());

    public Guid Create() => IdOf(Check(CreateDesktop(), nameof(Create)));

    public bool Exists(Guid desktop) => GetDesktopNumberById(desktop) >= 0;

    public void GoTo(Guid desktop) => Check(GoToDesktopNumber(Index(desktop)), nameof(GoTo));

    public void Move(IntPtr hwnd, Guid desktop) => Check(MoveWindowToDesktopNumber(hwnd, Index(desktop)), nameof(Move));

    public void Remove(Guid desktop, Guid fallback) =>
        Check(RemoveDesktop(Index(desktop), Index(fallback)), nameof(Remove));

    static Guid IdOf(int index) => GetDesktopIdByNumber(Check(index, "GetDesktopIdByNumber"));

    static int Index(Guid id) => Check(GetDesktopNumberById(id), "GetDesktopNumberById");

    static int Check(int result, string what) =>
        result >= 0 ? result : throw new InvalidOperationException($"Virtual desktop call failed: {what}");
}
