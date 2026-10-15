using Microsoft.Win32;
using WorkspaceLauncher.Core;

public class StartupEntriesTests
{
    [Theory]
    [InlineData(@"""C:\Program Files (x86)\Steam\steam.exe"" -silent", @"C:\Program Files (x86)\Steam\steam.exe", true)]
    [InlineData(@"""C:\Users\x\AppData\Local\Discord\Update.exe"" --processStart Discord.exe", "Discord.exe", true)]
    [InlineData(@"C:\Apps\Docker Desktop.exe", "Docker Desktop.exe", true)]
    [InlineData(@"C:\Apps\mysteam.exe", "steam.exe", false)]
    [InlineData(@"C:\Apps\steam.exe.bak", "steam.exe", false)]
    [InlineData(@"""C:\Apps\spotify.exe"" --minimized", "spotify", true)]
    public void Launches_MatchesWholeFileNames(string command, string exe, bool expected) =>
        Assert.Equal(expected, StartupEntries.Launches(command, exe));

    [Fact]
    public void Matching_IgnoresEntriesThatAreAlreadyOff()
    {
        var on = new StartupEntry(StartupKind.RunUser, "A", @"C:\x\app.exe", true);
        var off = new StartupEntry(StartupKind.RunUser, "B", @"C:\y\app.exe", false);
        Assert.Equal([on], StartupEntries.Matching([on, off], "app.exe"));
        Assert.Empty(StartupEntries.Matching([on], ""));
    }

    [Fact]
    public void Disable_TurnsAnEntryOff_AndEnableBringsItBack()
    {
        var name = "WorkspaceLauncherTest" + Guid.NewGuid().ToString("N");
        const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using (var run = Registry.CurrentUser.OpenSubKey(runPath, writable: true)!)
            run.SetValue(name, @"""C:\Windows\System32\notepad.exe"" /test");
        try
        {
            var entry = StartupEntries.Matching(StartupEntries.Snapshot(), "notepad.exe").Single(e => e.Name == name);

            StartupEntries.Disable(entry);
            Assert.Empty(StartupEntries.Matching(StartupEntries.Snapshot(), "notepad.exe").Where(e => e.Name == name));

            StartupEntries.Enable(entry);
            Assert.Single(StartupEntries.Matching(StartupEntries.Snapshot(), "notepad.exe"), e => e.Name == name);
        }
        finally
        {
            using var run = Registry.CurrentUser.OpenSubKey(runPath, writable: true)!;
            run.DeleteValue(name, false);
            StartupEntries.Enable(new StartupEntry(StartupKind.RunUser, name, "", true));
        }
    }
}
