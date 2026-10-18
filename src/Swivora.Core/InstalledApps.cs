namespace Swivora.Core;

public sealed record InstalledApp(string Name, string Exe, string Args, string? WorkDir);

/// <summary>The programs in the Start menu, as the Windows app list shows them. Desktop programs only: Store apps have no exe path to launch.</summary>
public static class InstalledApps
{
    public static List<InstalledApp> List(IEnumerable<string>? roots = null)
    {
        roots ??= [Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs)];
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return [];
        dynamic shell = Activator.CreateInstance(shellType)!;

        var found = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
            foreach (var path in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (found.ContainsKey(name) || name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                    dynamic link = shell.CreateShortcut(path);
                    var exe = Environment.ExpandEnvironmentVariables((string)link.TargetPath);
                    if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) continue;
                    if (Path.GetFileName(exe).StartsWith("unins", StringComparison.OrdinalIgnoreCase)) continue;
                    var dir = (string)link.WorkingDirectory;
                    found[name] = new InstalledApp(name, exe, (string)link.Arguments, string.IsNullOrWhiteSpace(dir) ? null : dir);
                }
                catch (Exception) { } // a broken shortcut must not hide the rest
            }
        return [.. found.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}
