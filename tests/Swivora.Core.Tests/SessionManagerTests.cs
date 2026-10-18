using System.Diagnostics;
using Swivora.Core;

class FakeDesktops : IDesktopBackend
{
    public bool IsAvailable => true;
    public readonly Guid Home = Guid.NewGuid();
    public readonly List<Guid> Desktops = [];
    public readonly List<Guid> Visited = [];
    public readonly List<(Guid removed, Guid fallback)> Removed = [];
    public FakeDesktops() => Desktops.Add(Home);
    public Guid Current() => Visited.Count > 0 ? Visited[^1] : Home;
    public Guid Create() { var g = Guid.NewGuid(); Desktops.Add(g); return g; }
    public bool Exists(Guid d) => Desktops.Contains(d);
    public void GoTo(Guid d) => Visited.Add(d);
    public void Move(IntPtr hwnd, Guid d) { }
    public void Remove(Guid d, Guid fallback) { Desktops.Remove(d); Removed.Add((d, fallback)); }
}

static class ManagerTestExtensions
{
    public static readonly Mode WorkMode = new("Work", ModeKind.Workspaces);
    public static Task StartWorkspace(this SessionManager m, Workspace w) => m.StartWorkspace(WorkMode, w);
}

// Real windows: winver.exe and charmap.exe are classic Win32 apps that ship with Windows and close on WM_CLOSE.
[Collection("gui")]
public class SessionManagerTests : IDisposable
{
    readonly FakeDesktops desktops = new();
    readonly SessionManager manager;
    readonly AppEntry winver = new("winver.exe", CloseTimeoutSeconds: 5);
    readonly AppEntry charmap = new("charmap.exe", CloseTimeoutSeconds: 5);

    public SessionManagerTests()
    {
        KillAll();
        manager = new SessionManager(desktops, new ProcessRunner()) { WindowWait = TimeSpan.FromSeconds(8) };
    }

    public void Dispose() => KillAll();

    static void KillAll()
    {
        foreach (var name in new[] { "winver", "charmap" })
            foreach (var p in Process.GetProcessesByName(name)) { p.Kill(); p.WaitForExit(); }
    }

    static int Count(string name) => Process.GetProcessesByName(name).Length;

    static Mode GamingWith(params AppEntry[] apps) => new("Gaming", ModeKind.Apps, [.. apps]);

    static Mode Streaming(params AppEntry[] apps) => new("Streaming", ModeKind.Apps, [.. apps]);

    [Fact]
    public async Task Keep_LeavesTheOldModeAppsRunning()
    {
        manager.OnSwitch = LeavePolicy.Keep;
        await manager.StartMode(GamingWith(winver));
        await manager.StartMode(Streaming(charmap));
        Assert.Equal("Streaming", manager.ActiveMode);
        Assert.Equal(1, Count("winver"));
        Assert.Equal(1, Count("charmap"));
    }

    [Fact]
    public async Task Keep_LeavesWorkspaceDesktopsAndAppsAlone()
    {
        manager.OnSwitch = LeavePolicy.Keep;
        await manager.StartWorkspace(new Workspace("A", [winver]));
        await manager.StartMode(Streaming(charmap));
        Assert.Equal(1, Count("winver"));
        Assert.Equal(2, desktops.Desktops.Count); // the workspace desktop was not removed
        Assert.Empty(manager.ActiveWorkspaces);
    }

    [Fact]
    public async Task Ask_Cancel_StaysInTheCurrentMode()
    {
        manager.OnSwitch = LeavePolicy.Ask;
        manager.ConfirmLeave = (_, _) => LeaveChoice.Cancel;
        await manager.StartMode(GamingWith(winver));
        await manager.StartMode(Streaming(charmap));
        Assert.Equal("Gaming", manager.ActiveMode);
        Assert.Equal(1, Count("winver"));
        Assert.Equal(0, Count("charmap"));
    }

    [Theory]
    [InlineData(LeaveChoice.Close, 0)]
    [InlineData(LeaveChoice.Keep, 1)]
    public async Task Ask_FollowsTheAnswer_AndNamesBothModes(LeaveChoice answer, int winversLeft)
    {
        manager.OnSwitch = LeavePolicy.Ask;
        string? seen = null;
        manager.ConfirmLeave = (running, next) => { seen = $"{running}>{next}"; return answer; };
        await manager.StartMode(GamingWith(winver));
        await manager.StartMode(Streaming(charmap));
        Assert.Equal("Gaming>Streaming", seen);
        Assert.Equal("Streaming", manager.ActiveMode);
        Assert.Equal(winversLeft, Count("winver"));
        Assert.Equal(1, Count("charmap"));
    }

    [Fact]
    public async Task Ask_IsSkippedWhenNothingIsRunning()
    {
        manager.OnSwitch = LeavePolicy.Ask;
        var asked = false;
        manager.ConfirmLeave = (_, _) => { asked = true; return LeaveChoice.Cancel; };
        await manager.StartMode(GamingWith());
        await manager.StartMode(Streaming(charmap));
        Assert.False(asked);
        Assert.Equal("Streaming", manager.ActiveMode);
    }

    [Fact]
    public async Task StartWorkspace_CreatesOwnDesktop_GoesThere_LaunchesApps()
    {
        await manager.StartWorkspace(new Workspace("A", [winver]));
        Assert.Equal(2, desktops.Desktops.Count);
        Assert.Equal(desktops.Desktops[1], desktops.Visited[^1]);
        Assert.Equal(1, Count("winver"));
        Assert.Equal(["A"], manager.ActiveWorkspaces);
    }

    [Fact]
    public async Task StartingSameWorkspaceTwice_JustSwitches_NoSecondDesktopOrInstance()
    {
        var ws = new Workspace("A", [winver]);
        await manager.StartWorkspace(ws);
        await manager.StartWorkspace(ws);
        Assert.Equal(2, desktops.Desktops.Count);
        Assert.Equal(1, Count("winver"));
    }

    [Fact]
    public async Task TwoWorkspaces_GetSeparateDesktops_AndSwitchBetweenThem()
    {
        await manager.StartWorkspace(new Workspace("A", [winver]));
        var a = desktops.Desktops[1];
        await manager.StartWorkspace(new Workspace("B", [charmap]));
        var b = desktops.Desktops[2];
        await manager.SwitchTo("A");
        Assert.Equal(a, desktops.Visited[^1]);
        await manager.SwitchTo("B");
        Assert.Equal(b, desktops.Visited[^1]);
        Assert.Equal(1, Count("winver"));
        Assert.Equal(1, Count("charmap"));
    }

    [Fact]
    public async Task CloseWorkspace_ClosesItsApps_AndRemovesDesktopFallingBackHome()
    {
        await manager.StartWorkspace(new Workspace("A", [winver]));
        await manager.StartWorkspace(new Workspace("B", [charmap]));
        await manager.CloseWorkspace("A");
        Assert.Equal(0, Count("winver"));
        Assert.Equal(1, Count("charmap")); // other workspace untouched
        Assert.Single(desktops.Removed);
        Assert.Equal(desktops.Home, desktops.Removed[0].fallback);
        Assert.Equal(["B"], manager.ActiveWorkspaces);
    }

    [Fact]
    public async Task WorkToGaming_ClosesWorkApps_ButKeepsAppsGamingAlsoNeeds()
    {
        await manager.StartWorkspace(new Workspace("A", [winver, charmap]));
        var winverPid = Process.GetProcessesByName("winver").Single().Id;
        await manager.StartMode(GamingWith(winver));
        Assert.Equal(0, Count("charmap"));
        Assert.Equal(winverPid, Process.GetProcessesByName("winver").Single().Id); // not closed, not relaunched
        Assert.Equal("Gaming", manager.ActiveMode);
        Assert.Empty(manager.ActiveWorkspaces);
        Assert.Single(desktops.Desktops); // the workspace desktop is gone, only home remains
    }

    [Fact]
    public async Task GamingToWork_ClosesGamingApps_ButKeepsSharedApps()
    {
        await manager.StartMode(GamingWith(winver, charmap));
        var winverPid = Process.GetProcessesByName("winver").Single().Id;
        await manager.StartWorkspace(new Workspace("A", [winver]));
        Assert.Equal(0, Count("charmap"));
        Assert.Equal(winverPid, Process.GetProcessesByName("winver").Single().Id);
        Assert.Equal("Work", manager.ActiveMode);
    }

    [Fact]
    public async Task OneBadApp_WarnsAndDoesNotBlockTheOthers()
    {
        var warnings = new List<string>();
        manager.Warning += warnings.Add;
        await manager.StartWorkspace(new Workspace("A", [new AppEntry("definitely-not-installed-9f3.exe"), winver]));
        Assert.Single(warnings);
        Assert.Equal(1, Count("winver"));
    }

    [Fact]
    public async Task StopCommand_RunsAfterClose()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"wl-stop-{Guid.NewGuid()}.txt");
        var app = winver with { StopCommand = $"echo done> \"{marker}\"" };
        await manager.StartWorkspace(new Workspace("A", [app]));
        await manager.CloseWorkspace("A");
        Assert.True(File.Exists(marker));
        File.Delete(marker);
    }

    [Fact]
    public async Task StopAll_ClosesEverything()
    {
        await manager.StartWorkspace(new Workspace("A", [winver]));
        await manager.StartWorkspace(new Workspace("B", [charmap]));
        await manager.StopAll();
        Assert.Equal(0, Count("winver") + Count("charmap"));
        Assert.Empty(manager.ActiveWorkspaces);
        Assert.Single(desktops.Desktops);
    }

    [Fact]
    public async Task SwitchingBetweenTwoAppsModes_ClosesTheFirst_KeepsSharedApps()
    {
        await manager.StartMode(new Mode("Gaming", ModeKind.Apps, [winver, charmap]));
        var winverPid = Process.GetProcessesByName("winver").Single().Id;
        await manager.StartMode(new Mode("Streaming", ModeKind.Apps, [winver]));
        Assert.Equal("Streaming", manager.ActiveMode);
        Assert.Equal(0, Count("charmap"));
        Assert.Equal(winverPid, Process.GetProcessesByName("winver").Single().Id);
    }

    [Fact]
    public async Task StartingAModeThatIsAlreadyRunning_DoesNothing()
    {
        var gaming = new Mode("Gaming", ModeKind.Apps, [winver]);
        await manager.StartMode(gaming);
        await manager.StartMode(gaming);
        Assert.Equal(1, Count("winver"));
    }

    [Fact]
    public async Task WorkspacesOfAnotherWorkspacesMode_ReplaceTheRunningOnes()
    {
        var study = new Mode("Study", ModeKind.Workspaces);
        await manager.StartWorkspace(new Workspace("A", [winver, charmap]));
        await manager.StartWorkspace(study, new Workspace("Exam", [winver]));
        Assert.Equal("Study", manager.ActiveMode);
        Assert.Equal(["Exam"], manager.ActiveWorkspaces);
        Assert.Equal(0, Count("charmap"));
        Assert.Equal(1, Count("winver"));
        Assert.Equal(2, desktops.Desktops.Count); // Work's desktop removed, Study's created
    }

    [Fact]
    public async Task ClosingTheLastWorkspace_ClearsTheActiveMode()
    {
        await manager.StartWorkspace(new Workspace("A", [winver]));
        await manager.CloseWorkspace("A");
        Assert.Null(manager.ActiveMode);
    }

    [Fact]
    public async Task AppThatIgnoresClose_SurvivesUnlessUserConfirmsKill()
    {
        // a runner whose close request is always ignored, standing in for an app that never closes
        var stubborn = new SessionManager(desktops, new IgnoresClose()) { WindowWait = TimeSpan.FromSeconds(8) };
        var asked = 0;
        stubborn.ConfirmForceKill = _ => { asked++; return false; };
        await stubborn.StartWorkspace(new Workspace("A", [winver]));
        await stubborn.CloseWorkspace("A");
        Assert.Equal(1, asked);
        Assert.Equal(1, Count("winver")); // declined: left alone

        stubborn.ConfirmForceKill = _ => true;
        await stubborn.StartWorkspace(new Workspace("B", [winver])); // adopts the running one
        await stubborn.CloseWorkspace("B");
        Thread.Sleep(500);
        Assert.Equal(0, Count("winver"));
    }

    class IgnoresClose : ProcessRunner
    {
        public override bool CloseGracefully(Process p, TimeSpan timeout) => false;
    }
}
