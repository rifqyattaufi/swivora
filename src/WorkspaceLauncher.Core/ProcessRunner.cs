using System.ComponentModel;
using System.Diagnostics;

namespace WorkspaceLauncher.Core;

/// <summary>A running app the daemon manages. CloseOnSwitch=false means never close it automatically.</summary>
public sealed record Owned(Process Process, bool CloseOnSwitch);

public sealed class ProcessRunner
{
    public Process? FindRunning(AppEntry app)
    {
        var name = Path.GetFileNameWithoutExtension(app.Exe);
        var wantPath = Path.IsPathRooted(app.Exe) ? Path.GetFullPath(app.Exe) : null;
        foreach (var p in Process.GetProcessesByName(name))
        {
            if (wantPath is null) return p;
            try
            {
                if (string.Equals(p.MainModule?.FileName, wantPath, StringComparison.OrdinalIgnoreCase)) return p;
            }
            catch (Win32Exception) { } // access denied (elevated process): cannot compare, skip
        }
        return null;
    }

    public Process? Launch(AppEntry app) => Process.Start(new ProcessStartInfo
    {
        FileName = app.Exe,
        Arguments = app.Args,
        WorkingDirectory = app.Cwd ?? "",
        UseShellExecute = true,
    });

    /// <summary>Applies the ownership rule. Returns null when the app must be left completely alone.</summary>
    public Owned? Acquire(AppEntry app)
    {
        var running = FindRunning(app);
        return app.Ownership switch
        {
            Ownership.Adopt => running is not null ? new(running, true) : Started(app, true),
            Ownership.Leave => running is not null ? null : Started(app, true),
            Ownership.LaunchOnly => running is not null ? null : Started(app, false),
            _ => null,
        };
    }

    Owned? Started(AppEntry app, bool closeOnSwitch) =>
        Launch(app) is { } p ? new(p, closeOnSwitch) : null;

    /// <summary>Asks the main window to close and waits. False means still alive: caller decides on force kill.</summary>
    public bool CloseGracefully(Process p, TimeSpan timeout)
    {
        if (p.HasExited) return true;
        p.CloseMainWindow();
        return p.WaitForExit(timeout);
    }
}
