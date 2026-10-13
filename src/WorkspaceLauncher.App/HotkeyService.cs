using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using WorkspaceLauncher.Core;

namespace WorkspaceLauncher.App;

/// <summary>Global hotkeys through a message-only window. Re-registered whenever the config changes.</summary>
public sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    const int WM_HOTKEY = 0x0312;
    const uint MOD_NOREPEAT = 0x4000;
    static readonly IntPtr HWND_MESSAGE = new(-3);

    readonly HwndSource source = new(new HwndSourceParameters("WorkspaceLauncher.Hotkeys") { ParentWindow = HWND_MESSAGE });
    readonly Dictionary<int, string> registered = [];

    public event Action<string>? Pressed;

    public HotkeyService() => source.AddHook(Hook);

    /// <summary>Registers every parsable entry. Returns one human-readable problem per failure.</summary>
    public List<string> Apply(IReadOnlyDictionary<string, string> hotkeys)
    {
        foreach (var id in registered.Keys) UnregisterHotKey(source.Handle, id);
        registered.Clear();

        var problems = new List<string>();
        var next = 1;
        foreach (var (name, text) in hotkeys)
        {
            if (string.IsNullOrWhiteSpace(text)) continue; // blank = disabled
            if (!HotkeyParser.TryParse(text, out var hk)) { problems.Add($"Hotkey '{text}' for {name} is not valid."); continue; }
            if (!RegisterHotKey(source.Handle, next, hk.Modifiers | MOD_NOREPEAT, hk.Vk))
            { problems.Add($"{text} ({name}) is already used by another app."); continue; }
            registered[next++] = name;
        }
        return problems;
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && registered.TryGetValue(wParam.ToInt32(), out var name))
        {
            handled = true;
            Pressed?.Invoke(name);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in registered.Keys) UnregisterHotKey(source.Handle, id);
        source.Dispose();
    }
}
