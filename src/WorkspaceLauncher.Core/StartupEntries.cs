using Microsoft.Win32;

namespace WorkspaceLauncher.Core;

public enum StartupKind { RunUser, RunMachine, RunMachine32, FolderUser, FolderCommon }

/// <summary>One program Windows starts when you sign in: a Run registry value or a shortcut in a Startup folder.</summary>
public sealed record StartupEntry(StartupKind Kind, string Name, string Command, bool Enabled)
{
    public string Display => Kind is StartupKind.FolderUser or StartupKind.FolderCommon ? Path.GetFileNameWithoutExtension(Name) : Name;
}

/// <summary>
/// Finds programs that start with Windows and turns them off the way Task Manager does: a flag under
/// HKCU StartupApproved, so the entry stays and can be switched back on in Task Manager > Startup apps.
/// Scheduled tasks and Store app startup tasks are not covered.
/// </summary>
public static class StartupEntries
{
    const string Windows = @"Software\Microsoft\Windows\CurrentVersion";

    public static List<StartupEntry> Snapshot()
    {
        var list = new List<StartupEntry>();
        ReadRun(list, Registry.CurrentUser, $@"{Windows}\Run", StartupKind.RunUser);
        ReadRun(list, Registry.LocalMachine, $@"{Windows}\Run", StartupKind.RunMachine);
        ReadRun(list, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", StartupKind.RunMachine32);
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupKind.FolderUser);
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupKind.FolderCommon);
        return list;
    }

    /// <summary>Entries that are on and launch this program (a path, a file name, or a command on PATH).</summary>
    public static List<StartupEntry> Matching(IEnumerable<StartupEntry> entries, string? exe) =>
        string.IsNullOrWhiteSpace(exe) ? [] : [.. entries.Where(e => e.Enabled && Launches(e.Command, exe))];

    public static bool Launches(string command, string exe)
    {
        var file = Path.GetFileName(exe.Trim().Trim('"'));
        if (file.Length == 0) return false;
        if (!Path.HasExtension(file)) file += ".exe";
        for (var at = 0; (at = command.IndexOf(file, at, StringComparison.OrdinalIgnoreCase)) >= 0; at += file.Length)
        {
            var before = at == 0 || command[at - 1] is '\\' or '/' or '"' or ' ';
            var end = at + file.Length;
            var after = end == command.Length || command[end] is '"' or ' ' or ',';
            if (before && after) return true;
        }
        return false;
    }

    public static void Disable(StartupEntry entry)
    {
        var data = new byte[12];
        data[0] = 3; // 3 = turned off by the user, as Task Manager writes it
        BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        using var key = Registry.CurrentUser.CreateSubKey($@"{Windows}\Explorer\StartupApproved\{ApprovedKey(entry.Kind)}");
        key.SetValue(entry.Name, data, RegistryValueKind.Binary);
    }

    /// <summary>Test and cleanup helper: removes the flag so the entry counts as on again.</summary>
    public static void Enable(StartupEntry entry)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{Windows}\Explorer\StartupApproved\{ApprovedKey(entry.Kind)}", writable: true);
        key?.DeleteValue(entry.Name, throwOnMissingValue: false);
    }

    static string ApprovedKey(StartupKind kind) => kind switch
    {
        StartupKind.RunUser or StartupKind.RunMachine => "Run",
        StartupKind.RunMachine32 => "Run32",
        _ => "StartupFolder",
    };

    static bool IsOn(RegistryKey hive, StartupKind kind, string name)
    {
        using var key = hive.OpenSubKey($@"{Windows}\Explorer\StartupApproved\{ApprovedKey(kind)}");
        return key?.GetValue(name) is not byte[] { Length: > 0 } data || (data[0] & 1) == 0;
    }

    static bool IsOn(StartupKind kind, string name) =>
        IsOn(Registry.CurrentUser, kind, name) && IsOn(Registry.LocalMachine, kind, name);

    static void ReadRun(List<StartupEntry> list, RegistryKey hive, string path, StartupKind kind)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
                if (key.GetValue(name) is string command && command.Length > 0)
                    list.Add(new StartupEntry(kind, name, Environment.ExpandEnvironmentVariables(command), IsOn(kind, name)));
        }
        catch (Exception) { } // an unreadable key just means fewer warnings
    }

    static void ReadFolder(List<StartupEntry> list, string folder, StartupKind kind)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            foreach (var path in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                var file = Path.GetFileName(path);
                dynamic link = shell.CreateShortcut(path);
                var command = $"\"{Environment.ExpandEnvironmentVariables((string)link.TargetPath)}\" {(string)link.Arguments}".Trim();
                list.Add(new StartupEntry(kind, file, command, IsOn(kind, file)));
            }
        }
        catch (Exception) { }
    }
}
