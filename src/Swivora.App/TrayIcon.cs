using System;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using Swivora.Core;

namespace Swivora.App;

/// <summary>Tray icon with a menu rebuilt on every open so it always reflects what is running.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon icon;
    readonly AppHost host;
    readonly Action<string?> showPicker;
    readonly Action<ConfigIntent, string?> showConfig;
    readonly Action<bool> quit;

    public TrayIcon(AppHost host, Action<string?> showPicker, Action<ConfigIntent, string?> showConfig, Action<bool> quit)
    {
        this.host = host;
        this.showPicker = showPicker;
        this.showConfig = showConfig;
        this.quit = quit;

        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico")).Stream;
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => Rebuild(menu);
        icon = new NotifyIcon
        {
            Icon = new Icon(stream),
            Text = "Swivora",
            Visible = true,
            ContextMenuStrip = menu,
        };
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) showPicker(null); };
    }

    void Rebuild(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        var sessions = host.Sessions;

        if (host.LastMessage is { } note)
        {
            var text = note.Length > 70 ? note[..70] + "…" : note;
            menu.Items.Add(new ToolStripMenuItem(text) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add(new ToolStripMenuItem("Open picker", null, (_, _) => showPicker(null)) { Font = new Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());

        var active = sessions.ActiveWorkspaces;
        foreach (var mode in host.Config.Modes)
        {
            var m = mode;
            var running = string.Equals(sessions.ActiveMode, m.Name, StringComparison.OrdinalIgnoreCase);
            if (m.Kind == ModeKind.Apps)
            {
                menu.Items.Add(new ToolStripMenuItem(m.Name, null, (_, _) => host.StartMode(m)) { Checked = running });
                continue;
            }
            var submenu = new ToolStripMenuItem(m.Name) { Checked = running };
            foreach (var ws in m.WorkspaceList)
            {
                var w = ws;
                submenu.DropDownItems.Add(new ToolStripMenuItem(w.Name, null, (_, _) => host.OpenWorkspace(m, w))
                { Checked = running && active.Contains(w.Name, StringComparer.OrdinalIgnoreCase) });
            }
            if (m.WorkspaceList.Count > 0) submenu.DropDownItems.Add(new ToolStripSeparator());
            submenu.DropDownItems.Add(new ToolStripMenuItem("Add workspace…", null, (_, _) => showConfig(ConfigIntent.AddWorkspace, m.Name)));
            menu.Items.Add(submenu);
        }

        var add = new ToolStripMenuItem("Add mode");
        add.DropDownItems.Add(new ToolStripMenuItem("With apps, like Gaming…", null, (_, _) => showConfig(ConfigIntent.AddAppsMode, null)));
        add.DropDownItems.Add(new ToolStripMenuItem("With workspaces, like Work…", null, (_, _) => showConfig(ConfigIntent.AddWorkspacesMode, null)));
        menu.Items.Add(add);

        if (active.Count > 0)
        {
            var close = new ToolStripMenuItem("Close workspace");
            foreach (var name in active)
            {
                var n = name;
                close.DropDownItems.Add(new ToolStripMenuItem(n, null, (_, _) => host.CloseWorkspace(n)));
            }
            menu.Items.Add(close);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => showConfig(ConfigIntent.Open, null)));
        menu.Items.Add(new ToolStripMenuItem("Start at login", null, (_, _) => AppHost.SetStartsAtLogin(!AppHost.StartsAtLogin))
        { Checked = AppHost.StartsAtLogin });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit (leave apps running)", null, (_, _) => quit(false)));
        menu.Items.Add(new ToolStripMenuItem("Close all apps and quit", null, (_, _) => quit(true)));
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
