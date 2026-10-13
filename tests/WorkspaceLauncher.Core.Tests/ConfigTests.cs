using WorkspaceLauncher.Core;

public class ConfigTests
{
    [Fact]
    public void RoundTrip_KeepsAppsAndOwnership()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wl-{Guid.NewGuid()}", "config.json");
        var cfg = new Config(
            [new AppEntry("discord.exe")],
            [new Workspace("X", [new AppEntry("code", "D:/x", Ownership: Ownership.Leave)])],
            new() { ["switchMode"] = "Ctrl+Alt+M" });
        ConfigStore.Save(path, cfg);
        var back = ConfigStore.Load(path);
        Assert.Equal("discord.exe", back.Gaming[0].Exe);
        Assert.Equal(Ownership.Leave, back.Work[0].Apps[0].Ownership);
        Assert.Equal("Ctrl+Alt+M", back.Hotkeys["switchMode"]);
    }

    [Fact]
    public void MissingFile_GivesEmptyConfig() =>
        Assert.Empty(ConfigStore.Load(Path.Combine(Path.GetTempPath(), "nope", "c.json")).Work);

    [Fact]
    public void MissingFields_UseDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wl-{Guid.NewGuid()}.json");
        File.WriteAllText(path, """{"gaming":[{"exe":"steam.exe"}],"work":[],"hotkeys":{}}""");
        var app = ConfigStore.Load(path).Gaming[0];
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
}
