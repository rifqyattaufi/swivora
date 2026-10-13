using System.Diagnostics;

namespace WorkspaceLauncher.Core;

public sealed record Managed(AppEntry App, Owned Owned);

/// <summary>One running group of apps: the gaming mode (no desktop) or one work workspace (own virtual desktop).</summary>
public sealed class Session(string name, Guid? desktop)
{
    public string Name { get; } = name;
    public Guid? Desktop { get; } = desktop;
    public List<Managed> Apps { get; } = [];
    public HashSet<IntPtr> Windows { get; } = [];
}

/// <summary>
/// Owns the lifecycle of gaming mode and work workspaces. All public operations are serialized, so hotkeys
/// pressed during a slow switch queue up instead of racing.
/// </summary>
public sealed class SessionManager(IDesktopBackend backend, ProcessRunner runner)
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly Dictionary<string, Session> workspaces = new(StringComparer.OrdinalIgnoreCase);
    Session? gaming;
    Guid? home; // the desktop we return to when a workspace desktop is removed

    // ponytail: window discovery is "what appeared after launch"; an unrelated window opened in that
    // few-second window would be adopted too. Per-app window matching is the upgrade if that bites.
    public TimeSpan WindowWait { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Asked once per app that ignored the close request. Return true to kill it. Default: never kill.</summary>
    public Func<AppEntry, bool> ConfirmForceKill { get; set; } = _ => false;
    public event Action<string>? Warning;

    public bool GamingActive => gaming is not null;
    public IReadOnlyList<string> ActiveWorkspaces => workspaces.Keys.ToList();
    public bool IsDegraded => !backend.IsAvailable;

    static string Key(AppEntry a) => $"{Path.GetFileNameWithoutExtension(a.Exe).ToLowerInvariant()}|{a.Args.Trim()}";

    // ---- public operations -------------------------------------------------

    public Task StartGaming(Config config) => Locked(async () =>
    {
        if (gaming is not null) return;
        var keep = config.Gaming.Select(Key).ToHashSet();
        foreach (var s in workspaces.Values.ToList()) await CloseSession(s, keep);
        workspaces.Clear();
        gaming = new Session("gaming", null);
        await LaunchApps(gaming, config.Gaming);
    });

    /// <summary>Starts the workspace on a fresh desktop, or just switches to it when it is already running.</summary>
    public Task StartWorkspace(Workspace workspace) => Locked(async () =>
    {
        if (workspaces.TryGetValue(workspace.Name, out var existing))
        {
            GoToDesktop(existing);
            return;
        }
        if (gaming is { } g)
        {
            await CloseSession(g, workspace.Apps.Select(Key).ToHashSet());
            gaming = null;
        }
        home ??= SafeCurrent();
        var session = new Session(workspace.Name, SafeCreate());
        workspaces[workspace.Name] = session;
        GoToDesktop(session);
        await LaunchApps(session, workspace.Apps);
    });

    public Task SwitchTo(string workspaceName) => Locked(() =>
    {
        if (workspaces.TryGetValue(workspaceName, out var s)) GoToDesktop(s);
        return Task.CompletedTask;
    });

    public Task CloseWorkspace(string workspaceName) => Locked(async () =>
    {
        if (!workspaces.Remove(workspaceName, out var s)) return;
        await CloseSession(s, []);
        RemoveDesktop(s);
    });

    public Task StopAll() => Locked(async () =>
    {
        foreach (var s in workspaces.Values.ToList()) await CloseSession(s, []);
        foreach (var s in workspaces.Values.ToList()) RemoveDesktop(s);
        workspaces.Clear();
        if (gaming is { } g) await CloseSession(g, []);
        gaming = null;
    });

    // ---- internals ---------------------------------------------------------

    async Task Locked(Func<Task> body)
    {
        await gate.WaitAsync();
        try { await body(); }
        finally { gate.Release(); }
    }

    async Task LaunchApps(Session session, IEnumerable<AppEntry> apps)
    {
        var before = Windows.Snapshot();
        foreach (var app in apps)
        {
            try
            {
                if (runner.Acquire(app) is not { } owned) continue;
                session.Apps.Add(new Managed(app, owned));
                // an adopted app lives on some other desktop: bring its windows here
                if (session.Desktop is { } d)
                    foreach (var h in Windows.OfProcess(owned.Process.Id)) SafeMove(h, d);
            }
            catch (Exception e)
            {
                Warning?.Invoke($"Could not start {app.Exe}: {e.Message}");
            }
        }
        await CollectNewWindows(session, before);
    }

    /// <summary>Waits for windows that appeared since <paramref name="before"/> to settle, then records them (and moves them to the session's desktop).</summary>
    async Task CollectNewWindows(Session session, HashSet<IntPtr> before)
    {
        var clock = Stopwatch.StartNew();
        var lastChange = TimeSpan.Zero;
        while (clock.Elapsed < WindowWait)
        {
            await Task.Delay(300);
            var fresh = Windows.Snapshot().Where(h => !before.Contains(h) && !session.Windows.Contains(h)).ToList();
            foreach (var h in fresh)
            {
                session.Windows.Add(h);
                if (session.Desktop is { } d) SafeMove(h, d);
                lastChange = clock.Elapsed;
            }
            var quiet = clock.Elapsed - lastChange;
            if (session.Windows.Count > 0 && quiet > TimeSpan.FromSeconds(2.5)) break;
            if (session.Windows.Count == 0 && clock.Elapsed > TimeSpan.FromSeconds(4)) break;
        }
    }

    async Task CloseSession(Session session, HashSet<string> keep)
    {
        var closing = session.Apps.Where(m => m.Owned.CloseOnSwitch && !keep.Contains(Key(m.App))).ToList();
        var spared = session.Apps.Except(closing).Select(m => m.Owned.Process.Id).ToHashSet();

        // windows that single-instance apps opened in a process we never launched
        foreach (var h in session.Windows.Where(Windows.IsAlive))
            if (!spared.Contains(Windows.PidOf(h))) Windows.Close(h);

        await Task.WhenAll(closing.Select(m => Task.Run(() => CloseApp(m))));
        session.Apps.RemoveAll(closing.Contains);
    }

    void CloseApp(Managed m)
    {
        var p = m.Owned.Process;
        try
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(1, m.App.CloseTimeoutSeconds));
            if (!runner.CloseGracefully(p, timeout) && ConfirmForceKill(m.App)) p.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Warning?.Invoke($"Could not close {m.App.Exe}: {e.Message}");
        }
        if (!string.IsNullOrWhiteSpace(m.App.StopCommand)) RunStopCommand(m.App);
    }

    void RunStopCommand(AppEntry app)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {app.StopCommand}")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = app.Cwd ?? "" });
            p?.WaitForExit(TimeSpan.FromSeconds(60));
        }
        catch (Exception e)
        {
            Warning?.Invoke($"Stop command for {app.Exe} failed: {e.Message}");
        }
    }

    void GoToDesktop(Session s)
    {
        if (s.Desktop is not { } d) return;
        try
        {
            if (backend.Exists(d)) backend.GoTo(d);
            else Warning?.Invoke($"The desktop of workspace '{s.Name}' was removed outside the launcher.");
        }
        catch (InvalidOperationException e) { Warning?.Invoke(e.Message); }
    }

    void RemoveDesktop(Session s)
    {
        if (s.Desktop is not { } d) return;
        try
        {
            var fallback = home is { } h && backend.Exists(h) ? h : backend.Current();
            if (backend.Exists(d) && d != fallback) backend.Remove(d, fallback);
        }
        catch (InvalidOperationException e) { Warning?.Invoke(e.Message); }
    }

    Guid SafeCreate()
    {
        try { return backend.Create(); }
        catch (InvalidOperationException e) { Warning?.Invoke(e.Message); return Guid.NewGuid(); }
    }

    Guid SafeCurrent()
    {
        try { return backend.Current(); }
        catch (InvalidOperationException) { return Guid.Empty; }
    }

    void SafeMove(IntPtr hwnd, Guid desktop)
    {
        try { backend.Move(hwnd, desktop); }
        catch (InvalidOperationException) { /* window closed or elevated: skip */ }
    }
}
