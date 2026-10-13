using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WorkspaceLauncher.Core;

namespace WorkspaceLauncher.App;

/// <summary>Everything the windows and the tray share: config, sessions, hotkey actions, messages.</summary>
public sealed class AppHost : IDisposable
{
    /// <summary>%APPDATA%\WorkspaceLauncher\config.json, or the file named by WORKSPACE_LAUNCHER_CONFIG (portable use, testing).</summary>
    public static readonly string ConfigPath = Environment.GetEnvironmentVariable("WORKSPACE_LAUNCHER_CONFIG") is { Length: > 0 } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkspaceLauncher", "config.json");

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "WorkspaceLauncher";

    readonly Dispatcher ui = Application.Current.Dispatcher;
    readonly List<string> messages = [];
    readonly FileSystemWatcher watcher;
    readonly DispatcherTimer debounce;
    string? lastText;
    bool loaded;

    public Config Config { get; private set; } = Config.Empty();
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
                "Workspace Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes),
        };
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

    public void StartGaming() => Run(() => Sessions.StartGaming(Config));

    public void OpenWorkspace(string name)
    {
        var ws = Config.Work.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
        if (ws is not null) Run(() => Sessions.StartWorkspace(ws));
    }

    public void CloseWorkspace(string name) => Run(() => Sessions.CloseWorkspace(name));

    public void QuitAll() => Run(Sessions.StopAll);

    public void HandleHotkey(string name)
    {
        if (name == HotkeyDefaults.Picker) ShowPicker?.Invoke(null);
        else if (name == HotkeyDefaults.SwitchMode)
        {
            if (Sessions.GamingActive) ShowPicker?.Invoke("work");
            else StartGaming();
        }
        else if (name.StartsWith("workspace") && int.TryParse(name["workspace".Length..], out var n)
                 && n >= 1 && n <= Config.Work.Count)
            OpenWorkspace(Config.Work[n - 1].Name);
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
