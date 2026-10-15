namespace WorkspaceLauncher.Core;

public enum PresetInput { None, Folder, File, Text }

/// <summary>A ready-made argument line for a well-known program. {0} in the template is the value the user picks or types.</summary>
public sealed record ArgPreset(string Label, string Template, PresetInput Input = PresetInput.None, string? FileFilter = null)
{
    /// <summary>Folders and files are quoted so spaces survive; typed text (a URL, a profile name) goes in as is.</summary>
    public string Build(string? value)
    {
        var v = value ?? "";
        if (Input is PresetInput.Folder or PresetInput.File) v = $"\"{v.Trim('"')}\"";
        return Template.Replace("{0}", v).Trim();
    }
}

public static class ArgPresets
{
    static readonly ArgPreset[] Editor =
    [
        new("Open a folder", "{0}", PresetInput.Folder),
        new("Open a workspace file", "{0}", PresetInput.File, "Workspace files|*.code-workspace|All files|*.*"),
        new("Open a file", "{0}", PresetInput.File),
        new("Open a folder in a new window", "-n {0}", PresetInput.Folder),
        new("Always open a new window", "-n"),
        new("Reuse the last window", "-r"),
    ];

    static readonly ArgPreset[] Chromium =
    [
        new("Open a website", "{0}", PresetInput.Text),
        new("Open a website in a new window", "--new-window {0}", PresetInput.Text),
        new("Open a website as an app window", "--app={0}", PresetInput.Text),
    ];

    static readonly Dictionary<string, ArgPreset[]> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["explorer"] = [new("Open a folder", "{0}", PresetInput.Folder), new("Open a folder with a file selected", "/select,{0}", PresetInput.File)],
        ["code"] = Editor,
        ["code-insiders"] = Editor,
        ["cursor"] = Editor,
        ["chrome"] = [.. Chromium, new("Private window", "--incognito")],
        ["msedge"] = [.. Chromium, new("Private window", "--inprivate")],
        ["brave"] = [.. Chromium, new("Private window", "--incognito")],
        ["firefox"] = [new("Open a website", "{0}", PresetInput.Text), new("Open a website in a new window", "-new-window {0}", PresetInput.Text), new("Private window", "-private-window")],
        ["wt"] = [new("Open in a folder", "-d {0}", PresetInput.Folder), new("Open a profile", "-p {0}", PresetInput.Text)],
        ["steam"] = [new("Start minimized", "-silent"), new("Start in Big Picture", "steam://open/bigpicture")],
        ["spotify"] = [new("Start minimized", "--minimized")],
        ["notepad"] = [new("Open a file", "{0}", PresetInput.File)],
        ["notepad++"] = [new("Open a file", "{0}", PresetInput.File), new("Open in a new window", "-multiInst")],
        ["devenv"] = [new("Open a solution", "{0}", PresetInput.File, "Solutions|*.sln;*.slnx|All files|*.*"), new("Open a folder", "/Folder {0}", PresetInput.Folder)],
    };

    /// <summary>Presets for the program at this path or command (code, Code.exe, C:\...\Code.exe). Empty when we know nothing about it.</summary>
    public static IReadOnlyList<ArgPreset> For(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return [];
        var name = Path.GetFileNameWithoutExtension(exe.Trim().Trim('"'));
        return Table.TryGetValue(name, out var list) ? list : [];
    }
}
