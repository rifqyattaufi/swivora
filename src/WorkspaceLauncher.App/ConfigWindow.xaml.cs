using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Appearance;
using WorkspaceLauncher.Core;

namespace WorkspaceLauncher.App;

public partial class ConfigWindow : Wpf.Ui.Controls.FluentWindow
{
    static readonly OwnershipOption[] Options =
    [
        new(Ownership.Adopt, "Take it over and close it later",
            "If it is already open, it becomes part of this mode or workspace and is closed when you switch away."),
        new(Ownership.Leave, "Leave it alone",
            "If it is already open, it is not touched. Otherwise it is started and closed later."),
        new(Ownership.LaunchOnly, "Start it, never close it",
            "It is started if needed and never closed for you."),
    ];

    readonly AppHost host;
    readonly ObservableCollection<TargetVm> targets = [];
    readonly ObservableCollection<HotkeyRowVm> hotkeys = [];
    Config shown = Config.Empty();
    bool dirty, loading;

    public ConfigWindow(AppHost host)
    {
        InitializeComponent();
        this.host = host;
        SystemThemeWatcher.Watch(this);

        Targets.ItemsSource = targets;
        HotkeyList.ItemsSource = hotkeys;
        OwnershipBox.ItemsSource = Options;
        targets.CollectionChanged += (_, _) => MarkDirty();

        ConfigPathText.Text = AppHost.ConfigPath;
        LoginSwitch.IsChecked = AppHost.StartsAtLogin;
        DesktopInfo.Text = host.Sessions.IsDegraded
            ? "Not available on this Windows build. Apps still start, but workspaces share your current desktop."
            : "Each workspace opens on its own Windows virtual desktop.";

        Build(host.Config);
        host.Changed += OnHostChanged;
        Closed += (_, _) => host.Changed -= OnHostChanged;
    }

    // ---- loading and saving ------------------------------------------------

    void Build(Config cfg)
    {
        loading = true;
        shown = cfg;
        targets.Clear();
        targets.Add(Watch(new TargetVm("Gaming", true, cfg.Gaming)));
        foreach (var ws in cfg.Work) targets.Add(Watch(new TargetVm(ws.Name, false, ws.Apps)));

        hotkeys.Clear();
        var merged = HotkeyDefaults.Merge(cfg.Hotkeys);
        hotkeys.Add(Row(merged, HotkeyDefaults.SwitchMode, "Switch between Gaming and Work"));
        hotkeys.Add(Row(merged, HotkeyDefaults.Picker, "Open the picker"));
        for (var i = 1; i <= 9; i++)
        {
            var label = i <= cfg.Work.Count ? $"Workspace {i}: {cfg.Work[i - 1].Name}" : $"Workspace {i}";
            hotkeys.Add(Row(merged, HotkeyDefaults.Workspace(i), label));
        }

        Targets.SelectedIndex = 0;
        loading = false;
        SetDirty(false);
    }

    HotkeyRowVm Row(System.Collections.Generic.Dictionary<string, string> merged, string key, string label) =>
        Watch(new HotkeyRowVm(key, label, merged.GetValueOrDefault(key, "")));

    T Watch<T>(T vm) where T : Observable
    {
        vm.PropertyChanged += (_, _) => MarkDirty();
        if (vm is TargetVm t)
        {
            t.Apps.CollectionChanged += (_, _) => MarkDirty();
            foreach (var a in t.Apps) Watch(a);
            t.Apps.CollectionChanged += (_, e) =>
            {
                if (e.NewItems is not null) foreach (AppVm a in e.NewItems) a.PropertyChanged += (_, _) => MarkDirty();
            };
        }
        return vm;
    }

    void MarkDirty() { if (!loading) SetDirty(true); }

    void SetDirty(bool value)
    {
        dirty = value;
        Status.Text = value ? "Unsaved changes" : "";
        Status.ClearValue(TextBlock.ForegroundProperty);
        SaveButton.IsEnabled = value;
    }

    void OnHostChanged()
    {
        // another process or a manual edit replaced config.json: follow it unless the user is mid-edit
        if (ReferenceEquals(host.Config, shown) || dirty) return;
        var selected = (Targets.SelectedItem as TargetVm)?.Name;
        Build(host.Config);
        Targets.SelectedItem = targets.FirstOrDefault(t => t.Name == selected) ?? targets[0];
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        if (Validate() is { } problem) { Fail(problem); return; }

        var cfg = new Config(
            targets[0].Apps.Select(a => a.ToEntry()).ToList(),
            targets.Skip(1).Select(t => new Workspace(t.Name.Trim(), t.Apps.Select(a => a.ToEntry()).ToList())).ToList(),
            ChangedHotkeys());
        try { host.Save(cfg); }
        catch (IOException ex) { Fail($"Could not save: {ex.Message}"); return; }

        shown = host.Config;
        SetDirty(false);
        Status.Text = "Saved";
    }

    System.Collections.Generic.Dictionary<string, string> ChangedHotkeys()
    {
        var defaults = HotkeyDefaults.Merge(new System.Collections.Generic.Dictionary<string, string>());
        return hotkeys.Where(h => h.Value != defaults.GetValueOrDefault(h.Key, ""))
                      .ToDictionary(h => h.Key, h => h.Value);
    }

    string? Validate()
    {
        var names = targets.Skip(1).Select(t => t.Name.Trim()).ToList();
        if (names.Any(string.IsNullOrEmpty)) return "Give every workspace a name.";
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count) return "Two workspaces share a name. Rename one of them.";
        foreach (var t in targets)
            foreach (var a in t.Apps)
                if (string.IsNullOrWhiteSpace(a.Exe)) return $"An app in {t.Name} has no program. Set one or remove the app.";
        foreach (var h in hotkeys.Where(h => !string.IsNullOrWhiteSpace(h.Value)))
            if (!HotkeyParser.TryParse(h.Value, out _)) return $"The hotkey for {h.Label} is not valid. Click its box and press the keys again.";
        var used = hotkeys.Where(h => !string.IsNullOrWhiteSpace(h.Value)).GroupBy(h => h.Value, StringComparer.OrdinalIgnoreCase)
                          .FirstOrDefault(g => g.Count() > 1);
        return used is null ? null : $"{used.Key} is assigned to more than one action. Clear one of them.";
    }

    void Fail(string message)
    {
        Status.Text = message;
        Status.Foreground = (System.Windows.Media.Brush)FindResource("SystemFillColorCriticalBrush");
    }

    void OnRevert(object sender, RoutedEventArgs e) => Build(host.Config);

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!dirty) return;
        var answer = System.Windows.MessageBox.Show(this, "Save your changes before closing?", "Settings",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (answer == MessageBoxResult.Yes)
        {
            OnSave(this, new RoutedEventArgs());
            if (dirty) e.Cancel = true; // validation failed: stay open
        }
    }

    // ---- workspaces and apps -----------------------------------------------

    public void AddWorkspace()
    {
        Tabs.SelectedIndex = 0;
        var baseName = "New workspace";
        var name = baseName;
        for (var i = 2; targets.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{baseName} {i}";
        var vm = Watch(new TargetVm(name, false, []));
        targets.Add(vm);
        Targets.SelectedItem = vm;
        TargetName.Focus();
        TargetName.SelectAll();
    }

    void OnAddWorkspace(object sender, RoutedEventArgs e) => AddWorkspace();

    void OnRemoveWorkspace(object sender, RoutedEventArgs e)
    {
        if (Targets.SelectedItem is not TargetVm { IsGaming: false } t) return;
        var index = targets.IndexOf(t);
        targets.Remove(t);
        Targets.SelectedIndex = Math.Min(index, targets.Count - 1);
    }

    void OnTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        var t = Targets.SelectedItem as TargetVm;
        AppsPane.DataContext = t;
        AppList.ItemsSource = t?.Apps;
        TargetName.IsEnabled = t is { IsGaming: false };
        RemoveTarget.IsEnabled = t is { IsGaming: false };
        if (t is { Apps.Count: > 0 }) AppList.SelectedIndex = 0; else AppList.SelectedItem = null;
        UpdateEmpty();
    }

    void UpdateEmpty() =>
        NoAppsText.Visibility = Targets.SelectedItem is TargetVm { Apps.Count: 0 } ? Visibility.Visible : Visibility.Collapsed;

    void OnAddApp(object sender, RoutedEventArgs e)
    {
        if (Targets.SelectedItem is not TargetVm t) return;
        var app = new AppVm();
        app.PropertyChanged += (_, _) => MarkDirty();
        t.Apps.Add(app);
        AppList.SelectedItem = app;
        UpdateEmpty();
    }

    void OnRemoveApp(object sender, RoutedEventArgs e)
    {
        if (Targets.SelectedItem is not TargetVm t || AppList.SelectedItem is not AppVm a) return;
        var index = t.Apps.IndexOf(a);
        t.Apps.Remove(a);
        AppList.SelectedIndex = Math.Min(index, t.Apps.Count - 1);
        UpdateEmpty();
    }

    void OnAppChanged(object sender, SelectionChangedEventArgs e)
    {
        var a = AppList.SelectedItem as AppVm;
        AppForm.DataContext = a;
        AppForm.Visibility = a is null ? Visibility.Collapsed : Visibility.Visible;
        NoAppText.Visibility = a is null ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not AppVm a) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Programs|*.exe;*.bat;*.cmd;*.lnk|All files|*.*",
            Title = "Choose a program",
        };
        if (dialog.ShowDialog(this) == true) a.Exe = dialog.FileName;
    }

    // ---- hotkeys -----------------------------------------------------------

    void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is HotkeyRowVm row) row.Value = "";
    }

    void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (key == Key.Tab && mods == ModifierKeys.None) return; // keep keyboard navigation working
        e.Handled = true;

        var row = (HotkeyRowVm)((FrameworkElement)sender).DataContext;
        if (key is Key.Back or Key.Delete) { row.Value = ""; return; }
        if (mods == ModifierKeys.None || KeyName(key) is not { } name) return;

        var parts = new System.Collections.Generic.List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);
        var text = string.Join("+", parts);
        if (HotkeyParser.TryParse(text, out _)) row.Value = text;
        ((System.Windows.Controls.TextBox)sender).GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
    }

    static string? KeyName(Key k) => k switch
    {
        >= Key.A and <= Key.Z => k.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (k - Key.D0))).ToString(),
        >= Key.F1 and <= Key.F24 => k.ToString(),
        Key.Space => "Space", Key.Tab => "Tab", Key.Return => "Enter", Key.Escape => "Esc",
        Key.Left => "Left", Key.Up => "Up", Key.Right => "Right", Key.Down => "Down",
        _ => null, // modifier-only presses and unsupported keys are ignored
    };

    // ---- general -----------------------------------------------------------

    void OnLoginToggled(object sender, RoutedEventArgs e) => AppHost.SetStartsAtLogin(LoginSwitch.IsChecked == true);

    void OnOpenFolder(object sender, RoutedEventArgs e) =>
        Process.Start("explorer.exe", Path.GetDirectoryName(AppHost.ConfigPath)!);
}
