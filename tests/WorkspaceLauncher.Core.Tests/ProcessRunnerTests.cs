using System.Diagnostics;
using WorkspaceLauncher.Core;

// winver.exe is a classic Win32 dialog that closes on CloseMainWindow and ships with Windows.
[Collection("gui")] // tests open real windows: never run two GUI classes at once
public class ProcessRunnerTests : IDisposable
{
    readonly ProcessRunner runner = new();
    readonly AppEntry winver = new("winver.exe");

    public ProcessRunnerTests() => KillAll();
    public void Dispose() => KillAll();
    static void KillAll() { foreach (var p in Process.GetProcessesByName("winver")) { p.Kill(); p.WaitForExit(); } }

    [Fact]
    public void Adopt_NotRunning_LaunchesAndOwns()
    {
        var owned = runner.Acquire(winver);
        Assert.NotNull(owned);
        Assert.True(owned!.CloseOnSwitch);
    }

    [Fact]
    public void Adopt_AlreadyRunning_TakesOverSameProcess_NoSecondInstance()
    {
        var first = runner.Acquire(winver)!;
        Thread.Sleep(1000);
        var second = runner.Acquire(winver)!;
        Assert.Equal(first.Process.Id, second.Process.Id);
        Assert.Single(Process.GetProcessesByName("winver"));
    }

    [Fact]
    public void Leave_AlreadyRunning_ReturnsNull_AndDoesNotLaunch()
    {
        runner.Launch(winver);
        Thread.Sleep(1000);
        Assert.Null(runner.Acquire(winver with { Ownership = Ownership.Leave }));
        Assert.Single(Process.GetProcessesByName("winver"));
    }

    [Fact]
    public void LaunchOnly_NotRunning_LaunchesButNeverCloses()
    {
        var owned = runner.Acquire(winver with { Ownership = Ownership.LaunchOnly });
        Assert.False(owned!.CloseOnSwitch);
    }

    [Fact]
    public void CloseGracefully_ClosesWindowedApp()
    {
        var owned = runner.Acquire(winver)!;
        Thread.Sleep(1500);
        Assert.True(runner.CloseGracefully(owned.Process, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Launch_MissingExe_ThrowsWin32Exception()
    {
        Assert.Throws<System.ComponentModel.Win32Exception>(
            () => runner.Launch(new AppEntry("definitely-not-installed-9f3.exe")));
    }

    [Fact]
    public void CloseGracefully_AlreadyExited_ReturnsTrue()
    {
        var owned = runner.Acquire(winver)!;
        Thread.Sleep(1500);
        owned.Process.Kill();
        owned.Process.WaitForExit();
        Assert.True(runner.CloseGracefully(owned.Process, TimeSpan.FromSeconds(1)));
    }
}
