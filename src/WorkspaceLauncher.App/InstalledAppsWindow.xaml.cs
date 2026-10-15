using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Wpf.Ui.Appearance;
using WorkspaceLauncher.Core;

namespace WorkspaceLauncher.App;

/// <summary>Pick a program from the Start menu list. Result is set when the user chooses one.</summary>
public partial class InstalledAppsWindow : Wpf.Ui.Controls.FluentWindow
{
    ICollectionView? view;

    public InstalledApp? Result { get; private set; }

    public InstalledAppsWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
        Loaded += async (_, _) =>
        {
            Search.Focus();
            List<InstalledApp> list;
            try { list = await Task.Run(() => InstalledApps.List()); }
            catch (Exception ex) { Message.Text = $"Could not read the app list: {ex.Message} Use Browse instead."; return; }
            view = CollectionViewSource.GetDefaultView(list);
            Apps.ItemsSource = view;
            Refilter();
        };
    }

    void OnSearch(object sender, TextChangedEventArgs e) => Refilter();

    void Refilter()
    {
        if (view is null) return;
        var q = Search.Text.Trim();
        view.Filter = string.IsNullOrEmpty(q) ? null : o => ((InstalledApp)o).Name.Contains(q, StringComparison.CurrentCultureIgnoreCase);
        var any = !view.IsEmpty;
        Apps.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        Message.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        Message.Text = "No app matches. Try another name, or use Browse to find the program yourself.";
        if (any && Apps.SelectedItem is null) Apps.SelectedIndex = 0;
    }

    void OnSelected(object sender, SelectionChangedEventArgs e) => ChooseButton.IsEnabled = Apps.SelectedItem is not null;

    void OnDoubleClick(object sender, MouseButtonEventArgs e) { if (Apps.SelectedItem is not null) OnChoose(sender, e); }

    void OnChoose(object sender, RoutedEventArgs e)
    {
        Result = Apps.SelectedItem as InstalledApp;
        DialogResult = Result is not null;
    }

    void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: DialogResult = false; break;
            case Key.Enter when Apps.SelectedItem is not null: OnChoose(sender, e); e.Handled = true; break;
            case Key.Down when Search.IsKeyboardFocused && Apps.Items.Count > 0:
                Apps.SelectedIndex = Math.Min(Apps.Items.Count - 1, Apps.SelectedIndex + 1);
                Apps.ScrollIntoView(Apps.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when Search.IsKeyboardFocused && Apps.Items.Count > 0:
                Apps.SelectedIndex = Math.Max(0, Apps.SelectedIndex - 1);
                Apps.ScrollIntoView(Apps.SelectedItem);
                e.Handled = true;
                break;
        }
    }
}
