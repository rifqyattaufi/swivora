using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Appearance;

namespace WorkspaceLauncher.App;

public partial class PickerWindow : Wpf.Ui.Controls.FluentWindow
{
    readonly AppHost host;
    readonly Action<bool> showConfig;

    public PickerWindow(AppHost host, Action<bool> showConfig)
    {
        InitializeComponent();
        this.host = host;
        this.showConfig = showConfig;
        SystemThemeWatcher.Watch(this);
        host.Changed += Refresh;
        Closed += (_, _) => host.Changed -= Refresh;
        Refresh();
    }

    public void Open(string? page)
    {
        if (!IsVisible) Show();
        if (page == "work") ShowWork(); else ShowMode();
        Activate();
    }

    void ShowMode()
    {
        WorkPage.Visibility = Visibility.Collapsed;
        ModePage.Visibility = Visibility.Visible;
        GamingButton.Focus();
    }

    void ShowWork()
    {
        ModePage.Visibility = Visibility.Collapsed;
        WorkPage.Visibility = Visibility.Visible;
        (WorkspaceList.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault() as IInputElement ?? AddButton).Focus();
    }

    void Refresh()
    {
        var cfg = host.Config;
        var running = host.Sessions.ActiveWorkspaces;

        GamingInfo.Text = Plural(cfg.Gaming.Count, "app") + (host.Sessions.GamingActive ? ", running" : "");
        WorkInfo.Text = Plural(cfg.Work.Count, "workspace") + (running.Count > 0 ? $", {running.Count} running" : "");
        AutomationProperties.SetName(GamingButton, $"Gaming, {GamingInfo.Text}");
        AutomationProperties.SetName(WorkButton, $"Work, {WorkInfo.Text}");

        WorkspaceList.Children.Clear();
        foreach (var ws in cfg.Work)
        {
            var name = ws.Name;
            var isRunning = running.Contains(name, StringComparer.OrdinalIgnoreCase);
            var row = new DockPanel();
            var state = new TextBlock
            {
                Text = isRunning ? "Running" : "",
                Foreground = (System.Windows.Media.Brush)FindResource("AccentTextFillColorPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(state, Dock.Right);
            row.Children.Add(state);
            row.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = Plural(ws.Apps.Count, "app"), Opacity = 0.7 },
                },
            });
            var button = new System.Windows.Controls.Button
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(16, 10, 16, 10),
                Margin = new Thickness(0, 0, 0, 8),
            };
            AutomationProperties.SetName(button, isRunning ? $"{name}, running" : name);
            button.Click += (_, _) => { host.OpenWorkspace(name); Close(); };
            WorkspaceList.Children.Add(button);
        }
        EmptyText.Visibility = cfg.Work.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Status.Text = host.LastMessage ?? (host.Sessions.IsDegraded ? "Separate desktops are not available on this Windows build." : "");
    }

    static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    void OnGaming(object sender, RoutedEventArgs e) { host.StartGaming(); Close(); }
    void OnWork(object sender, RoutedEventArgs e) => ShowWork();
    void OnBack(object sender, RoutedEventArgs e) => ShowMode();
    void OnAdd(object sender, RoutedEventArgs e) { showConfig(true); Close(); }
    void OnSettings(object sender, RoutedEventArgs e) { showConfig(false); Close(); }

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (WorkPage.Visibility == Visibility.Visible) ShowMode(); else Close();
        e.Handled = true;
    }
}
