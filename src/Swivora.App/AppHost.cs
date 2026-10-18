using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Swivora.Core;

namespace Swivora.App;

/// <summary>Everything the windows and the tray share: config, sessions, hotkey actions, messages.</summary>
public sealed class AppHost : IDisposable
{
    /// <summary>%APPDATA%\Swivora\config.json, or the file named by SWIVORA_CONFIG (portable use, testing).</summary>
    public static readonly string ConfigPath = CustomConfig() is { } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(AppData, "Swivora", "config.json");

    static string AppData => Environment.GetEnvironmentVariable("APPDATA") is { Length: > 0 } a ? a : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    // WORKSPACE_LAUNCHER_CONFIG is the name from before the app was called Swivora
    static string? CustomConfig() =>
        new[] { "SWIVORA_CONFIG", "WORKSPACE_LAUNCHER_CONFIG" }.Select(Environment.GetEnvironmentVariable).FirstOrDefault(v => v is { Length: > 0 });

    const string LegacyName = "WorkspaceLauncher";

    /// <summary>
    /// Brings over what the app was called Workspace Launcher left behind: the config and its icon pictures
    /// (copied, the old folder stays) and the start-at-sign-in entry. Safe to call on every start.
    /// </summary>
    public static void MigrateFromWorkspaceLauncher()
    {
        try
        {
            var oldDir = Path.Combine(AppData, LegacyName);
            var newDir = Path.GetDirectoryName(ConfigPath)!;
            if (CustomConfig() is null && !File.Exists(ConfigPath) && File.Exists(Path.Combine(oldDir, "config.json")))
            {
                Directory.CreateDirectory(newDir);
                File.Copy(Path.Combine(oldDir, "config.json"), ConfigPath);
                var oldIcons = Path.Combine(oldDir, "icons");
                if (Directory.Exists(oldIcons))
                {
                    Directory.CreateDirectory(Path.Combine(newDir, "icons"));
                    foreach (var f in Directory.GetFiles(oldIcons)) File.Copy(f, Path.Combine(newDir, "icons", Path.GetFileName(f)), overwrite: true);
                }
            }

            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue(LegacyName) is string)
            {
                run.DeleteValue(LegacyName, throwOnMissingValue: false);
                run.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "Swivora";

    readonly Dispatcher ui = Application.Current.Dispatcher;
    readonly List<string> messages = [];
    readonly FileSystemWatcher watcher;
    readonly DispatcherTimer debounce;
    string? lastText;
    bool loaded;
    string? lastMode; // the mode most recently asked for, so cycling continues even while a workspaces-mode waits for a pick

    Config config = Config.Empty();
    public Config Config
    {
        get => config;
        private set { config = value; Sessions.OnSwitch = value.Leave; }
    }
    public SessionManager Sessions { get; }
    public IDesktopBackend Desktops { get; }
    public IReadOnlyList<string> Messages => messages;

    /// <summary>Config, active sessions or messages changed: windows refresh themselves.</summary>
    public event Action? Changed;
    public Action<string?>? ShowPicker; // arg: "work" opens the workspace list directly

    public AppHost()
    {
        Desktops = VdaDesktopBackend.Create(out var warning);
        if (warning is not null) Add(warning);

        Sessions = new SessionManager(Desktops, new ProcessRunner())
        {
            ConfirmForceKill = app => ui.Invoke(() => System.Windows.MessageBox.Show(
                $"{app.Exe} did not close in {app.CloseTimeoutSeconds} seconds.\n\nEnd it now? Unsaved work in it will be lost.",
                "Swivora", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes),
        };
        Sessions.ConfirmLeave = (running, next) => ui.Invoke(() =>
        {
            var answer = System.Windows.MessageBox.Show(
                $"{running} still has apps open. Close them before opening {next}?\n\nYes closes them. No leaves them running. Cancel stays where you are.",
                "Swivora", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            return answer switch { MessageBoxResult.Yes => LeaveChoice.Close, MessageBoxResult.No => LeaveChoice.Keep, _ => LeaveChoice.Cancel };
        });
        Sessions.Warning += m => ui.BeginInvoke(() => Add(m));

        Load();

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        debounce = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(400) };
        debounce.Tick += (_, _) => { debounce.Stop(); Load(); };
        watcher = new FileSystemWatcher(Path.GetDirectoryName(ConfigPath)!, Path.GetFileName(ConfigPath))
        { EnableRaisingEvents = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime };
        // the store writes via a temp file + move, so Created/Renamed/Changed can all fire
        watcher.Changed += (_, _) => ui.BeginInvoke(debounce.Start);
        watcher.Created += (_, _) => ui.BeginInvoke(debounce.Start);
        watcher.Renamed += (_, _) => ui.BeginInvoke(debounce.Start);
    }

    // ---- config ------------------------------------------------------------

    /// <summary>Reads config.json. A broken file keeps the last good config and says so.</summary>
    public void Load()
    {
        try
        {
            // our own Save echoes back through the file watcher: ignore it so open windows keep their state
            var text = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : null;
            if (loaded && text == lastText) return;
            lastText = text;
            loaded = true;
            Config = ConfigStore.Load(ConfigPath);
        }
        catch (JsonException e) { Add($"config.json is not valid and was ignored: {e.Message}"); }
        catch (IOException e) { Add($"Could not read config.json: {e.Message}"); }
        Changed?.Invoke();
    }

    public void Save(Config config)
    {
        ConfigStore.Save(ConfigPath, config);
        lastText = File.ReadAllText(ConfigPath);
        Config = config;
        Changed?.Invoke();
    }

    // ---- messages ----------------------------------------------------------

    public void Add(string message)
    {
        messages.Add(message);
        if (messages.Count > 20) messages.RemoveAt(0);
        Changed?.Invoke();
    }

    public string? LastMessage => messages.Count > 0 ? messages[^1] : null;

    // ---- actions (tray, picker and hotkeys all call these) -----------------

    /// <summary>Runs a session operation and reports failures instead of crashing the tray.</summary>
    public async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) { Add($"Something went wrong: {e.Message}"); }
        Changed?.Invoke();
    }

    /// <summary>Apps-mode: start it. Workspaces-mode: go straight to its only workspace, or let the user pick one.</summary>
    public void StartMode(Mode mode)
    {
        lastMode = mode.Name;
        if (mode.Kind == ModeKind.Apps) Run(() => Sessions.StartMode(mode));
        else if (mode.WorkspaceList.Count == 1) OpenWorkspace(mode, mode.WorkspaceList[0]);
        else ShowPicker?.Invoke(mode.Name);
    }

    public void OpenWorkspace(Mode mode, Workspace workspace)
    {
        lastMode = mode.Name;
        Run(() => Sessions.StartWorkspace(mode, workspace));
    }

    public void CloseWorkspace(string name) => Run(() => Sessions.CloseWorkspace(name));

    public void QuitAll() => Run(Sessions.StopAll);

    public void HandleHotkey(string name)
    {
        var modes = Config.Modes;
        if (name == HotkeyDefaults.Picker) ShowPicker?.Invoke(null);
        else if (name == HotkeyDefaults.SwitchMode)
        {
            if (modes.Count == 0) return;
            var from = lastMode ?? Sessions.ActiveMode;
            var current = modes.FindIndex(m => string.Equals(m.Name, from, StringComparison.OrdinalIgnoreCase));
            StartMode(modes[(current + 1) % modes.Count]);
        }
        else if (TryIndex(name, "workspace", out var w))
        {
            var all = Config.AllWorkspaces();
            if (w < all.Count) OpenWorkspace(all[w].Mode, all[w].Workspace);
        }
        else if (TryIndex(name, "mode", out var m) && m < modes.Count) StartMode(modes[m]);
    }

    static bool TryIndex(string name, string prefix, out int zeroBased)
    {
        zeroBased = -1;
        return name.StartsWith(prefix) && int.TryParse(name[prefix.Length..], out var n) && n >= 1 && (zeroBased = n - 1) >= 0;
    }

    // ---- start at login ----------------------------------------------------

    public static bool StartsAtLogin
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }
    }

    public static void SetStartsAtLogin(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunName, throwOnMissingValue: false);
    }

    public void Dispose() => watcher.Dispose();
}
