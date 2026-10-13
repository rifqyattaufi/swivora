using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkspaceLauncher.Core;

public enum Ownership { Adopt, Leave, LaunchOnly }

/// <summary>Apps = one flat list started together (Gaming). Workspaces = several named groups, each on its own desktop (Work).</summary>
public enum ModeKind { Apps, Workspaces }

public sealed record AppEntry(
    string Exe,
    string Args = "",
    string? Cwd = null,
    Ownership Ownership = Ownership.Adopt,
    int CloseTimeoutSeconds = 10,
    string? StopCommand = null);

public sealed record Workspace(string Name, List<AppEntry> Apps);

public sealed record Mode(
    string Name,
    ModeKind Kind = ModeKind.Apps,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<AppEntry>? Apps = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<Workspace>? Workspaces = null)
{
    [JsonIgnore] public List<AppEntry> AppList => Apps ?? [];
    [JsonIgnore] public List<Workspace> WorkspaceList => Workspaces ?? [];
}

public sealed record Config(List<Mode> Modes, Dictionary<string, string> Hotkeys)
{
    /// <summary>First run: the two modes people expect, both empty.</summary>
    public static Config Empty() => new([new Mode("Gaming"), new Mode("Work", ModeKind.Workspaces)], []);

    public Mode? FindMode(string name) =>
        Modes.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every workspace of every workspaces-mode, in config order. Hotkeys workspace1..9 index into this.</summary>
    public List<(Mode Mode, Workspace Workspace)> AllWorkspaces() =>
        [.. Modes.Where(m => m.Kind == ModeKind.Workspaces).SelectMany(m => m.WorkspaceList.Select(w => (m, w)))];
}

public static class ConfigStore
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Shape of the file. "gaming" and "work" are the pre-modes format and are still read.</summary>
    sealed record Raw(
        List<Mode>? Modes = null,
        List<AppEntry>? Gaming = null,
        List<Workspace>? Work = null,
        Dictionary<string, string>? Hotkeys = null);

    public static Config Load(string path)
    {
        if (!File.Exists(path)) return Config.Empty();
        var raw = JsonSerializer.Deserialize<Raw>(File.ReadAllText(path), Options) ?? new Raw();
        var modes = raw.Modes ?? [new Mode("Gaming", ModeKind.Apps, raw.Gaming ?? []), new Mode("Work", ModeKind.Workspaces, null, raw.Work ?? [])];
        return new Config(modes, raw.Hotkeys ?? []);
    }

    public static void Save(string path, Config config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // write-then-move so a watcher never reads a half-written file
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
