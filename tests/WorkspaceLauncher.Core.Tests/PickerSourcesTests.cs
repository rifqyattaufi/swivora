using WorkspaceLauncher.Core;

public class PickerSourcesTests
{
    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe")]
    [InlineData("code")]
    [InlineData(@"""C:\Program Files\Microsoft VS Code\code.cmd""")]
    public void Presets_FoundByProgramName(string exe) => Assert.NotEmpty(ArgPresets.For(exe));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(@"C:\Tools\unknown-thing.exe")]
    public void Presets_EmptyForUnknownPrograms(string? exe) => Assert.Empty(ArgPresets.For(exe));

    [Fact]
    public void Build_QuotesFoldersAndFiles_NotText()
    {
        var folder = ArgPresets.For("explorer")[0];
        Assert.Equal(@"""D:\My Projects""", folder.Build(@"D:\My Projects"));
        var site = ArgPresets.For("chrome").First(p => p.Template.StartsWith("--app"));
        Assert.Equal("--app=https://example.com", site.Build("https://example.com"));
        Assert.Equal("-n", ArgPresets.For("code").First(p => p.Template == "-n").Build(null));
    }

    [Fact]
    public void InstalledApps_ReadsShortcutTarget()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wl-lnk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            dynamic link = shell.CreateShortcut(Path.Combine(dir, "Test Notepad.lnk"));
            link.TargetPath = notepad;
            link.Arguments = "readme.txt";
            link.Save();

            var app = Assert.Single(InstalledApps.List([dir]));
            Assert.Equal("Test Notepad", app.Name);
            Assert.Equal(notepad, app.Exe, ignoreCase: true);
            Assert.Equal("readme.txt", app.Args);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void InstalledApps_RealStartMenu_HasEntries() => Assert.NotEmpty(InstalledApps.List());
}
