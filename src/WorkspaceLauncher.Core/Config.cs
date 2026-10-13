using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkspaceLauncher.Core;

public enum Ownership { Adopt, Leave, LaunchOnly }

public sealed record AppEntry(
    string Exe,
    string Args = "",
    string? Cwd = null,
    Ownership Ownership = Ownership.Adopt,
    int CloseTimeoutSeconds = 10,
    string? StopCommand = null);

public sealed record Workspace(string Name, List<AppEntry> Apps);

public sealed record Config(
    List<AppEntry> Gaming,
    List<Workspace> Work,
    Dictionary<string, string> Hotkeys)
{
    public static Config Empty() => new([], [], []);
}

public static class ConfigStore
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Config Load(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<Config>(File.ReadAllText(path), Options) ?? Config.Empty()
            : Config.Empty();

    public static void Save(string path, Config config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // write-then-move so a watcher never reads a half-written file
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
