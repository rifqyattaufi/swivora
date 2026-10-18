using Swivora.Core;

public class ConfigTests
{
    [Fact]
    public void RoundTrip_KeepsIconsAppNamesAndPickerStyle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wl-{Guid.NewGuid()}", "config.json");
        ConfigStore.Save(path, new Config(
            [new Mode("Gaming", ModeKind.Apps, [new AppEntry("steam.exe", Name: "Steam")], null, "🎮"), new Mode("Plain")],
            [], Config.Grid));
        var json = File.ReadAllText(path);
        Assert.DoesNotContain("isGrid", json);
        Assert.Contains("\"pickerStyle\": \"grid\"", json);
        var back = ConfigStore.Load(path);
        Assert.True(back.IsGrid);
        Assert.Equal("🎮", back.Modes[0].Icon);
        Assert.Equal("Steam", back.Modes[0].AppList[0].Name);
        Assert.Null(back.Modes[1].Icon);
        Assert.False(ConfigStore.Load(Path.Combine(Path.GetTempPath(), "nope", "c.json")).IsGrid);
    }

    [Fact]
    public void OnSwitch_DefaultsToClose_AndRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wl-{Guid.NewGuid()}", "config.json");
        ConfigStore.Save(path, new Config([new Mode("A")], []));
        Assert.DoesNotContain("onSwitch", File.ReadAllText(path));
        Assert.Equal(LeavePolicy.Close, ConfigStore.Load(path).Leave);

        ConfigStore.Save(path, new Config([new Mode("A")], [], null, LeavePolicy.Keep));
        Assert.Contains("\"onSwitch\": \"keep\"", File.ReadAllText(path));
        Assert.Equal(LeavePolicy.Keep, ConfigStore.Load(path).Leave);
    }

    [Fact]
    public void RoundTrip_KeepsAppsAndOwnership()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wl-{Guid.NewGuid()}", "config.json");
        var cfg = new Config(
            [
                new Mode("Gaming", ModeKind.Apps, [new AppEntry("discord.exe")]),
                new Mode("Work", ModeKind.Workspaces, null, [new Workspace("X", [new AppEntry("code", "D:/x", Ownership: Ownership.Leave)])]),
                new Mode("Streaming", ModeKind.Apps, [new AppEntry("obs64.exe")]),
            ],
            new() { ["switchMode"] = "Ctrl+Alt+M" });
        ConfigStore.Save(path, cfg);
        var back = ConfigStore.Load(path);
        Assert.Equal(["Gaming", "Work", "Streaming"], back.Modes.Select(m => m.Name));
        Assert.Equal("discord.exe", back.Modes[0].AppList[0].Exe);
        Assert.Equal(Ownership.Leave, back.Modes[1].WorkspaceList[0].Apps[0].Ownership);
        Assert.Equal(ModeKind.Apps, back.Modes[2].Kind);
        Assert.Equal("Ctrl+Alt+M", back.Hotkeys["switchMode"]);
    }

    [Fact]
    public void MissingFile_GivesEmptyConfig() =>
        Assert.Equal(["Gaming", "Work"], ConfigStore.Load(Path.Combine(Path.GetTempPath(), "nope", "c.json")).Modes.Select(m => m.Name));

    [Fact]
    public void MissingFields_UseDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wl-{Guid.NewGuid()}.json");
        File.WriteAllText(path, """{"modes":[{"name":"Gaming","apps":[{"exe":"steam.exe"}]}],"hotkeys":{}}""");
        var app = ConfigStore.Load(path).Modes[0].AppList[0];
        Assert.Equal(Ownership.Adopt, app.Ownership);
        Assert.Equal(10, app.CloseTimeoutSeconds);
    }

    [Fact]
    public void InvalidJson_Throws_SoCallerCanKeepLastGoodConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wl-{Guid.NewGuid()}.json");
        File.WriteAllText(path, "{ not json");
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ConfigStore.Load(path));
    }

    [Fact]
    public void Save_LeavesNoTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wl-{Guid.NewGuid()}", "config.json");
        ConfigStore.Save(path, Config.Empty());
        ConfigStore.Save(path, Config.Empty()); // overwrite path
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void OldFormat_GamingAndWork_BecomesTwoModes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wl-{Guid.NewGuid()}.json");
        File.WriteAllText(path, """{"gaming":[{"exe":"steam.exe"}],"work":[{"name":"P","apps":[{"exe":"code"}]}],"hotkeys":{"picker":"Ctrl+Alt+K"}}""");
        var cfg = ConfigStore.Load(path);
        Assert.Equal("steam.exe", cfg.FindMode("gaming")!.AppList[0].Exe);
        Assert.Equal(ModeKind.Workspaces, cfg.FindMode("Work")!.Kind);
        Assert.Equal("P", cfg.AllWorkspaces()[0].Workspace.Name);
        Assert.Equal("Ctrl+Alt+K", cfg.Hotkeys["picker"]);
    }

    [Fact]
    public void Saved_AppsMode_HasNoEmptyWorkspacesNoise()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wl-{Guid.NewGuid()}.json");
        ConfigStore.Save(path, new Config([new Mode("Gaming", ModeKind.Apps, [new AppEntry("a.exe")])], []));
        Assert.DoesNotContain("workspaces", File.ReadAllText(path));
    }

    [Fact]
    public void AllWorkspaces_SpansEveryWorkspacesMode_InOrder()
    {
        var cfg = new Config(
            [
                new Mode("Work", ModeKind.Workspaces, null, [new Workspace("A", []), new Workspace("B", [])]),
                new Mode("Gaming"),
                new Mode("Study", ModeKind.Workspaces, null, [new Workspace("C", [])]),
            ], []);
        Assert.Equal(["A", "B", "C"], cfg.AllWorkspaces().Select(x => x.Workspace.Name));
    }
}
