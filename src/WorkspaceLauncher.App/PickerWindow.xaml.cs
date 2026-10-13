using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Appearance;
using WorkspaceLauncher.Core;

namespace WorkspaceLauncher.App;

public partial class PickerWindow : Wpf.Ui.Controls.FluentWindow
{
    readonly AppHost host;
    readonly Action<ConfigIntent, string?> showConfig;
    string? workMode; // the workspaces-mode whose list is showing, or null on the mode page

    public PickerWindow(AppHost host, Action<ConfigIntent, string?> showConfig)
    {
        InitializeComponent();
        this.host = host;
        this.showConfig = showConfig;
        SystemThemeWatcher.Watch(this);
        host.Changed += Refresh;
        Closed += (_, _) => host.Changed -= Refresh;
        Refresh();
    }

    /// <param name="modeName">Null shows the mode list; a workspaces-mode name shows its workspaces.</param>
    public void Open(string? modeName)
    {
        if (!IsVisible) Show();
        workMode = host.Config.FindMode(modeName ?? "") is { Kind: ModeKind.Workspaces } m ? m.Name : null;
        Refresh();
        FocusFirst(workMode is null ? ModeList : WorkspaceList, workMode is null ? null : AddButton);
        Activate();
    }

    void FocusFirst(Panel list, Control? fallback)
    {
        (list.Children.OfType<Control>().FirstOrDefault() ?? fallback)?.Focus();
    }

    void Refresh()
    {
        var cfg = host.Config;
        var running = host.Sessions.ActiveWorkspaces;
        var activeMode = host.Sessions.ActiveMode;

        // mode page
        ModeList.Children.Clear();
        foreach (var mode in cfg.Modes)
        {
            var m = mode;
            var isActive = string.Equals(activeMode, m.Name, StringComparison.OrdinalIgnoreCase);
            var info = m.Kind == ModeKind.Apps
                ? Plural(m.AppList.Count, "app") + (isActive ? ", running" : "")
                : Plural(m.WorkspaceList.Count, "workspace") + (isActive && running.Count > 0 ? $", {running.Count} running" : "");
            var button = Row(m.Name, info, 16, new Thickness(16, 12, 16, 12), "", () =>
            {
                if (m.Kind == ModeKind.Apps) { host.StartMode(m); Close(); }
                else { workMode = m.Name; Refresh(); FocusFirst(WorkspaceList, AddButton); }
            });
            AutomationProperties.SetName(button, $"{m.Name}, {info}");
            ModeList.Children.Add(button);
        }
        NoModesText.Visibility = cfg.Modes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // workspace page for the chosen workspaces-mode
        var chosen = workMode is null ? null : cfg.FindMode(workMode);
        if (chosen is null) workMode = null;
        ModePage.Visibility = chosen is null ? Visibility.Visible : Visibility.Collapsed;
        WorkPage.Visibility = chosen is null ? Visibility.Collapsed : Visibility.Visible;

        WorkspaceList.Children.Clear();
        if (chosen is not null)
        {
            WorkTitle.Text = chosen.Name;
            foreach (var ws in chosen.WorkspaceList)
            {
                var w = ws;
                var isRunning = string.Equals(activeMode, chosen.Name, StringComparison.OrdinalIgnoreCase)
                                && running.Contains(w.Name, StringComparer.OrdinalIgnoreCase);
                var button = Row(w.Name, Plural(w.Apps.Count, "app"), 15, new Thickness(16, 10, 16, 10),
                                 isRunning ? "Running" : "", () => { host.OpenWorkspace(chosen, w); Close(); });
                AutomationProperties.SetName(button, isRunning ? $"{w.Name}, running" : w.Name);
                WorkspaceList.Children.Add(button);
            }
            EmptyText.Visibility = chosen.WorkspaceList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        Status.Text = host.LastMessage ?? (host.Sessions.IsDegraded ? "Separate desktops are not available on this Windows build." : "");
    }

    /// <summary>A full-width two-line button: title, subtitle, and an optional accent label on the right.</summary>
    Button Row(string title, string subtitle, double titleSize, Thickness padding, string state, Action onClick)
    {
        var row = new DockPanel();
        var stateText = new TextBlock
        {
            Text = state,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentTextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(stateText, Dock.Right);
        row.Children.Add(stateText);
        row.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = title, FontSize = titleSize, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = subtitle, Opacity = 0.7 },
            },
        });
        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = padding,
            Margin = new Thickness(0, 0, 0, 8),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    void OnBack(object sender, RoutedEventArgs e) { workMode = null; Refresh(); FocusFirst(ModeList, null); }
    void OnAdd(object sender, RoutedEventArgs e) { showConfig(ConfigIntent.AddWorkspace, workMode); Close(); }
    void OnSettings(object sender, RoutedEventArgs e) { showConfig(ConfigIntent.Open, null); Close(); }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (workMode is not null) OnBack(sender, e); else Close();
        e.Handled = true;
    }
}
