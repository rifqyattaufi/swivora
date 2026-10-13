using WorkspaceLauncher.Core;

public class HotkeyTests
{
    [Fact]
    public void Parses_ModifiersAndLetter()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+M", out var h));
        Assert.Equal(3u, h.Modifiers);
        Assert.Equal((uint)'M', h.Vk);
    }

    [Theory]
    [InlineData("ctrl + shift + f5", 6u, 0x74u)]
    [InlineData("Win+Left", 8u, 0x25u)]
    [InlineData("Alt+1", 1u, (uint)'1')]
    public void Parses_CaseSpacesAndNamedKeys(string text, uint mods, uint vk)
    {
        Assert.True(HotkeyParser.TryParse(text, out var h));
        Assert.Equal(new Hotkey(mods, vk), h);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("M")]            // no modifier: would swallow normal typing
    [InlineData("Ctrl+Alt")]     // no key
    [InlineData("Ctrl+A+B")]     // two keys
    [InlineData("Ctrl+Banana")]  // unknown key
    [InlineData("Ctrl+F25")]
    public void Rejects_Invalid(string? text) => Assert.False(HotkeyParser.TryParse(text, out _));

    [Fact]
    public void Defaults_AreOverriddenByUser_AndBlankDisables()
    {
        var all = HotkeyDefaults.Merge(new Dictionary<string, string> { ["switchMode"] = "Ctrl+Shift+G", ["picker"] = "" });
        Assert.Equal("Ctrl+Shift+G", all["switchMode"]);
        Assert.Equal("", all["picker"]);
        Assert.Equal("Ctrl+Alt+3", all["workspace3"]);
    }
}
