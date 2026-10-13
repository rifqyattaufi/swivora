using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using Wpf.Ui.Appearance;
using WorkspaceLauncher.Core;

namespace WorkspaceLauncher.App;

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

    public void ShowPicker(string? page)
    {
        if (host is null) return;
        if (picker is null)
        {
            picker = new PickerWindow(host, ShowConfig);
            picker.Closed += (_, _) => picker = null;
        }
        picker.Open(page);
    }

    public void ShowConfig(bool newWorkspace)
    {
        if (host is null) return;
        if (config is null)
        {
            config = new ConfigWindow(host);
            config.Closed += (_, _) => config = null;
            config.Show();
        }
        if (newWorkspace) config.AddWorkspace();
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
