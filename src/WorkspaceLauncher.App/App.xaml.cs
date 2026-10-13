using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using Wpf.Ui.Appearance;
using WorkspaceLauncher.Core;

namespace WorkspaceLauncher.App;

/// <summary>What the Settings window should do when it opens.</summary>
public enum ConfigIntent { Open, AddWorkspace, AddAppsMode, AddWorkspacesMode }

public partial class App : Application
{
    const string SingleInstanceName = "WorkspaceLauncher.SingleInstance";
    const string ShowSignalName = "WorkspaceLauncher.ShowPicker";

    Mutex? singleInstance;
    EventWaitHandle? showSignal;
    AppHost? host;
    HotkeyService? hotkeys;
    TrayIcon? tray;
    PickerWindow? picker;
    ConfigWindow? config;
    string lastHotkeyProblems = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        singleInstance = new Mutex(true, SingleInstanceName, out var first);
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        if (!first)
        {
            // already running: ask that instance to show its picker, then leave
            showSignal.Set();
            Shutdown();
            return;
        }

        ApplicationThemeManager.ApplySystemTheme();
        DispatcherUnhandledException += (_, args) =>
        {
            host?.Add($"Unexpected error: {args.Exception.Message}");
            args.Handled = true;
        };

        host = new AppHost { ShowPicker = ShowPicker };
        hotkeys = new HotkeyService();
        hotkeys.Pressed += host.HandleHotkey;
        host.Changed += ApplyHotkeys;
        ApplyHotkeys();

        tray = new TrayIcon(host, ShowPicker, ShowConfig, Quit);

        var waiter = new Thread(() =>
        {
            while (showSignal.WaitOne()) Dispatcher.BeginInvoke(() => ShowPicker(null));
        }) { IsBackground = true };
        waiter.Start();

        ShowPicker(null);
    }

    void ApplyHotkeys()
    {
        if (hotkeys is null || host is null) return;
        var problems = hotkeys.Apply(HotkeyDefaults.Merge(host.Config.Hotkeys));
        var joined = string.Join("\n", problems);
        if (joined != lastHotkeyProblems && problems.Count > 0)
            foreach (var p in problems) host.Add(p);
        lastHotkeyProblems = joined;
    }

    /// <param name="modeName">Null shows the mode list; a workspaces-mode name shows that mode's workspaces.</param>
    public void ShowPicker(string? modeName)
    {
        if (host is null) return;
        if (picker is null)
        {
            picker = new PickerWindow(host, ShowConfig);
            picker.Closed += (_, _) => picker = null;
        }
        picker.Open(modeName);
    }

    /// <param name="modeName">For AddWorkspace: the mode the new workspace belongs to.</param>
    public void ShowConfig(ConfigIntent intent, string? modeName = null)
    {
        if (host is null) return;
        if (config is null)
        {
            config = new ConfigWindow(host);
            config.Closed += (_, _) => config = null;
            config.Show();
        }
        switch (intent)
        {
            case ConfigIntent.AddWorkspace: config.AddWorkspace(modeName); break;
            case ConfigIntent.AddAppsMode: config.AddMode(ModeKind.Apps); break;
            case ConfigIntent.AddWorkspacesMode: config.AddMode(ModeKind.Workspaces); break;
        }
        config.Activate();
    }

    void Quit(bool closeApps)
    {
        if (host is null) return;
        if (closeApps) host.Run(async () => { await host.Sessions.StopAll(); Shutdown(); });
        else Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        hotkeys?.Dispose();
        host?.Dispose();
        singleInstance?.Dispose();
        base.OnExit(e);
    }
}
