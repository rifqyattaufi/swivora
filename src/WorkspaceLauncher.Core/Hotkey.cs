namespace WorkspaceLauncher.Core;

/// <summary>Win32 RegisterHotKey modifiers (Alt=1, Ctrl=2, Shift=4, Win=8) plus a virtual-key code.</summary>
public readonly record struct Hotkey(uint Modifiers, uint Vk);

public static class HotkeyParser
{
    static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Esc"] = 0x1B,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
    };

    /// <summary>Parses "Ctrl+Alt+M". At least one modifier is required so a bare key can never be hijacked.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        uint mods = 0, vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "alt": mods |= 1; continue;
                case "ctrl" or "control": mods |= 2; continue;
                case "shift": mods |= 4; continue;
                case "win" or "windows": mods |= 8; continue;
            }
            if (vk != 0 || !TryKey(raw, out vk)) return false;
        }
        if (mods == 0 || vk == 0) return false;
        hotkey = new Hotkey(mods, vk);
        return true;
    }

    static bool TryKey(string k, out uint vk)
    {
        vk = 0;
        if (k.Length == 1 && char.IsAsciiLetterOrDigit(k[0])) { vk = char.ToUpperInvariant(k[0]); return true; }
        if (k.Length is 2 or 3 && (k[0] is 'F' or 'f') && int.TryParse(k[1..], out var n) && n is >= 1 and <= 24)
        { vk = (uint)(0x70 + n - 1); return true; }
        return Named.TryGetValue(k, out vk);
    }
}

public static class HotkeyDefaults
{
    public const string SwitchMode = "switchMode";
    public const string Picker = "picker";
    public static string Workspace(int n) => $"workspace{n}";
    /// <summary>Direct hotkey for the Nth mode. Off by default.</summary>
    public static string Mode(int n) => $"mode{n}";

    /// <summary>Defaults first, then the user's entries override them. A blank value disables that hotkey.</summary>
    public static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> user)
    {
        var all = new Dictionary<string, string>
        {
            [SwitchMode] = "Ctrl+Alt+M",
            [Picker] = "Ctrl+Alt+P",
        };
        for (var i = 1; i <= 9; i++) all[Workspace(i)] = $"Ctrl+Alt+{i}";
        foreach (var (k, v) in user) all[k] = v;
        return all;
    }
}
