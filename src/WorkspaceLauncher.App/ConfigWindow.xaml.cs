using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Wpf.Ui.Appearance;
using WorkspaceLauncher.Core;

namespace WorkspaceLauncher.App;

public partial class ConfigWindow : Wpf.Ui.Controls.FluentWindow
{
    const int DirectModeHotkeys = 5;

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
        AppVm.Startup = StartupEntries.Snapshot();
        // someone may change Task Manager > Startup apps while Settings stays open
        Activated += (_, _) =>
        {
            AppVm.Startup = StartupEntries.Snapshot();
            foreach (var app in targets.SelectMany(t => t.Apps)) app.RefreshStartup();
        };

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
        foreach (var mode in cfg.Modes)
        {
            if (mode.Kind == ModeKind.Apps)
            {
                targets.Add(Watch(new TargetVm(mode.Name, TargetKind.AppsMode, mode.AppList)));
                continue;
            }
            var parent = Watch(new TargetVm(mode.Name, TargetKind.WorkspacesMode, []));
            targets.Add(parent);
            foreach (var ws in mode.WorkspaceList) targets.Add(Watch(new TargetVm(ws.Name, TargetKind.Workspace, ws.Apps, parent)));
        }

        hotkeys.Clear();
        var merged = HotkeyDefaults.Merge(cfg.Hotkeys);
        hotkeys.Add(Row(merged, HotkeyDefaults.SwitchMode, "Switch to the next mode"));
        hotkeys.Add(Row(merged, HotkeyDefaults.Picker, "Open the picker"));
        var all = cfg.AllWorkspaces();
        for (var i = 1; i <= 9; i++)
        {
            var label = i <= all.Count ? $"Workspace {i}: {all[i - 1].Workspace.Name}" : $"Workspace {i}";
            hotkeys.Add(Row(merged, HotkeyDefaults.Workspace(i), label));
        }
        for (var i = 1; i <= DirectModeHotkeys; i++)
        {
            var label = i <= cfg.Modes.Count ? $"Open mode {i}: {cfg.Modes[i - 1].Name}" : $"Open mode {i}";
            hotkeys.Add(Row(merged, HotkeyDefaults.Mode(i), label));
        }

        if (targets.Count > 0) Targets.SelectedIndex = 0; else OnTargetChanged(Targets, null);
        loading = false;
        SetDirty(false);
    }

    HotkeyRowVm Row(Dictionary<string, string> merged, string key, string label) =>
        Watch(new HotkeyRowVm(key, label, merged.GetValueOrDefault(key, "")));

    T Watch<T>(T vm) where T : Observable
    {
        vm.PropertyChanged += OnVmChanged;
        if (vm is TargetVm t)
        {
            foreach (var a in t.Apps) Watch(a);
            t.Apps.CollectionChanged += (_, e) =>
            {
                MarkDirty();
                if (e.NewItems is not null) foreach (AppVm a in e.NewItems) a.PropertyChanged += OnVmChanged;
            };
        }
        return vm;
    }

    /// <summary>The start-with-Windows warning follows the system, not the config, so it never counts as an edit.</summary>
    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(AppVm.HasStartup) or nameof(AppVm.StartupMessage))) MarkDirty();
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
        Targets.SelectedItem = targets.FirstOrDefault(t => t.Name == selected) ?? targets.FirstOrDefault();
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        if (Validate() is { } problem) { Fail(problem); return; }

        try { host.Save(new Config(BuildModes(), ChangedHotkeys())); }
        catch (IOException ex) { Fail($"Could not save: {ex.Message}"); return; }

        shown = host.Config;
        SetDirty(false);
        Status.Text = "Saved";
    }

    List<Mode> BuildModes() => targets.Where(t => t.IsMode).Select(t => t.Kind == TargetKind.AppsMode
        ? new Mode(t.Name.Trim(), ModeKind.Apps, t.Apps.Select(a => a.ToEntry()).ToList())
        : new Mode(t.Name.Trim(), ModeKind.Workspaces, null,
            targets.Where(c => c.Parent == t).Select(c => new Workspace(c.Name.Trim(), c.Apps.Select(a => a.ToEntry()).ToList())).ToList())).ToList();

    Dictionary<string, string> ChangedHotkeys()
    {
        var defaults = HotkeyDefaults.Merge(new Dictionary<string, string>());
        return hotkeys.Where(h => h.Value != defaults.GetValueOrDefault(h.Key, ""))
                      .ToDictionary(h => h.Key, h => h.Value);
    }

    string? Validate()
    {
        var modes = targets.Where(t => t.IsMode).ToList();
        if (modes.Any(m => string.IsNullOrWhiteSpace(m.Name))) return "Give every mode a name.";
        if (Duplicate(modes.Select(m => m.Name))) return "Two modes share a name. Rename one of them.";
        foreach (var m in modes.Where(m => m.Kind == TargetKind.WorkspacesMode))
        {
            var names = targets.Where(c => c.Parent == m).Select(c => c.Name).ToList();
            if (names.Any(string.IsNullOrWhiteSpace)) return $"Give every workspace in {m.Name} a name.";
            if (Duplicate(names)) return $"Two workspaces in {m.Name} share a name. Rename one of them.";
        }
        foreach (var t in targets)
            foreach (var a in t.Apps)
                if (string.IsNullOrWhiteSpace(a.Exe)) return $"An app in {t.Name} has no program. Set one or remove the app.";
        foreach (var h in hotkeys.Where(h => !string.IsNullOrWhiteSpace(h.Value)))
            if (!HotkeyParser.TryParse(h.Value, out _)) return $"The hotkey for {h.Label} is not valid. Click its box and press the keys again.";
        var used = hotkeys.Where(h => !string.IsNullOrWhiteSpace(h.Value)).GroupBy(h => h.Value, StringComparer.OrdinalIgnoreCase)
                          .FirstOrDefault(g => g.Count() > 1);
        return used is null ? null : $"{used.Key} is assigned to more than one action. Clear one of them.";
    }

    static bool Duplicate(IEnumerable<string> names)
    {
        var list = names.Select(n => n.Trim()).ToList();
        return list.Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count;
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

    // ---- modes, workspaces and apps ----------------------------------------

    string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; targets.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{baseName} {i}";
        return name;
    }

    void Select(TargetVm vm)
    {
        Tabs.SelectedIndex = 0;
        Targets.SelectedItem = vm;
        Targets.ScrollIntoView(vm);
        TargetName.Focus();
        TargetName.SelectAll();
    }

    public void AddMode(ModeKind kind)
    {
        var vm = Watch(new TargetVm(UniqueName("New mode"), kind == ModeKind.Apps ? TargetKind.AppsMode : TargetKind.WorkspacesMode, []));
        targets.Add(vm);
        Select(vm);
    }

    /// <summary>Adds a workspace to the named workspaces-mode, else the selected one, else the first. Creates "Work" if there is none.</summary>
    public void AddWorkspace(string? modeName = null)
    {
        var modes = targets.Where(t => t.Kind == TargetKind.WorkspacesMode).ToList();
        var selected = Targets.SelectedItem as TargetVm;
        var mode = modes.FirstOrDefault(m => modeName is not null && string.Equals(m.Name, modeName, StringComparison.OrdinalIgnoreCase))
                   ?? (selected?.Kind == TargetKind.WorkspacesMode ? selected : selected?.Parent)
                   ?? modes.FirstOrDefault();
        if (mode is null)
        {
            mode = Watch(new TargetVm(UniqueName("Work"), TargetKind.WorkspacesMode, []));
            targets.Add(mode);
        }

        var vm = Watch(new TargetVm(UniqueName("New workspace"), TargetKind.Workspace, [], mode));
        var index = targets.IndexOf(mode) + 1;
        while (index < targets.Count && targets[index].Parent == mode) index++;
        targets.Insert(index, vm);
        Select(vm);
    }

    void OnAddWorkspace(object sender, RoutedEventArgs e) => AddWorkspace();

    void OnAddMode(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddModeButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var withApps = new MenuItem { Header = "With apps, like Gaming" };
        withApps.Click += (_, _) => AddMode(ModeKind.Apps);
        var withWorkspaces = new MenuItem { Header = "With workspaces, like Work" };
        withWorkspaces.Click += (_, _) => AddMode(ModeKind.Workspaces);
        menu.Items.Add(withApps);
        menu.Items.Add(withWorkspaces);
        menu.IsOpen = true;
    }

    void OnRemoveTarget(object sender, RoutedEventArgs e)
    {
        if (Targets.SelectedItem is not TargetVm t) return;
        var index = targets.IndexOf(t);
        foreach (var child in targets.Where(c => c.Parent == t).ToList()) targets.Remove(child);
        targets.Remove(t);
        if (targets.Count > 0) Targets.SelectedIndex = Math.Min(index, targets.Count - 1);
        else OnTargetChanged(Targets, null);
    }

    void OnTargetChanged(object sender, SelectionChangedEventArgs? e)
    {
        var t = Targets.SelectedItem as TargetVm;
        AppsPane.DataContext = t;
        AppList.ItemsSource = t?.Apps;
        TargetName.IsEnabled = t is not null;
        RemoveTarget.IsEnabled = t is not null;
        AppsSection.Visibility = t is { HasApps: true } ? Visibility.Visible : Visibility.Collapsed;
        ShowWorkspaces(t is { Kind: TargetKind.WorkspacesMode } ? t : null);
        if (t is { Apps.Count: > 0 }) AppList.SelectedIndex = 0; else AppList.SelectedItem = null;
        UpdateEmpty();
        if (t is { Kind: TargetKind.WorkspacesMode }) NoAppText.Visibility = Visibility.Collapsed; // nothing to edit on the right yet
        UpdateBar();
    }

    /// <summary>List buttons only belong to the Apps tab, and the middle ones follow what the middle pane shows.</summary>
    void UpdateBar()
    {
        var onApps = Tabs.SelectedIndex == 0;
        ModeButtons.Visibility = onApps ? Visibility.Visible : Visibility.Collapsed;
        AppButtons.Visibility = onApps && AppsSection.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceButtons.Visibility = onApps && WorkspacesSection.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Tabs) UpdateBar();
    }

    /// <summary>Status sits left of Save and Revert when it fits, above them when the window is narrow.</summary>
    void OnBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = Bar.ColumnDefinitions[4].ActualWidth < 340;
        Grid.SetRow(Status, narrow ? 0 : 1);
        Grid.SetColumnSpan(Status, narrow ? 2 : 1);
        Status.Margin = new Thickness(0, 0, narrow ? 0 : 16, narrow ? 8 : 0);
    }

    void ShowWorkspaces(TargetVm? mode)
    {
        WorkspacesSection.Visibility = mode is null ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceList.ItemsSource = mode is null ? null : new ListCollectionView(targets) { Filter = o => ((TargetVm)o).Parent == mode };
        UpdateEmpty();
    }

    // picking a workspace in the mode's list opens it for editing
    void OnWorkspaceListChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkspaceList.SelectedItem is TargetVm ws) Select(ws);
    }

    void UpdateEmpty()
    {
        NoWorkspacesText.Visibility = WorkspacesSection.Visibility == Visibility.Visible && WorkspaceList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmptyApps();
    }

    void UpdateEmptyApps() =>
        NoAppsText.Visibility = Targets.SelectedItem is TargetVm { HasApps: true, Apps.Count: 0 } ? Visibility.Visible : Visibility.Collapsed;

    void OnAddApp(object sender, RoutedEventArgs e)
    {
        if (Targets.SelectedItem is not TargetVm { HasApps: true } t) return;
        var app = new AppVm();
        app.PropertyChanged += OnVmChanged;
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

    void OnTurnOffStartup(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not AppVm a) return;
        try
        {
            foreach (var hit in a.StartupHits) StartupEntries.Disable(hit);
        }
        catch (Exception ex)
        {
            Fail($"Could not turn off start with Windows: {ex.Message} Open Task Manager > Startup apps and turn it off there.");
            return;
        }
        var names = string.Join(", ", a.StartupHits.Select(h => h.Display).Distinct());
        AppVm.Startup = StartupEntries.Snapshot();
        foreach (var app in targets.SelectMany(t => t.Apps)) app.RefreshStartup();
        Status.Text = $"{names} no longer starts with Windows.";
        Status.ClearValue(TextBlock.ForegroundProperty);
    }

    void OnPickInstalled(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not AppVm a) return;
        var dialog = new InstalledAppsWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } app) return;
        a.Exe = app.Exe;
        if (string.IsNullOrWhiteSpace(a.Args)) a.Args = app.Args;
    }

    /// <summary>Fills Arguments from a ready-made line. Folders and files are asked for; text kinds leave the cursor at the end to finish typing.</summary>
    void OnPreset(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is not ArgPreset p || AppList.SelectedItem is not AppVm a) return;
        PresetBox.SelectedIndex = -1; // back to the empty box so the same preset can be picked again
        string? value = null;
        switch (p.Input)
        {
            case PresetInput.Folder:
                using (var folder = new System.Windows.Forms.FolderBrowserDialog { UseDescriptionForTitle = true, Description = p.Label })
                {
                    if (folder.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    value = folder.SelectedPath;
                }
                break;
            case PresetInput.File:
                var file = new Microsoft.Win32.OpenFileDialog { Title = p.Label, Filter = p.FileFilter ?? "All files|*.*" };
                if (file.ShowDialog(this) != true) return;
                value = file.FileName;
                break;
        }
        a.Args = p.Build(value);
        ArgsBox.Focus();
        if (p.Input == PresetInput.Text)
        {
            if (!a.Args.EndsWith('=')) a.Args += " ";
            ArgsBox.CaretIndex = a.Args.Length;
        }
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

        var parts = new List<string>();
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
