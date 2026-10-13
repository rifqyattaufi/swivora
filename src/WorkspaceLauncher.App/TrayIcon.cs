using System;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Forms;

namespace WorkspaceLauncher.App;

/// <summary>Tray icon with a menu rebuilt on every open so it always reflects what is running.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon icon;
    readonly AppHost host;
    readonly Action<string?> showPicker;
    readonly Action<bool> showConfig;
    readonly Action<bool> quit;

    public TrayIcon(AppHost host, Action<string?> showPicker, Action<bool> showConfig, Action<bool> quit)
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
            Text = "Workspace Launcher",
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

        menu.Items.Add(new ToolStripMenuItem("Gaming", null, (_, _) => host.StartGaming()) { Checked = sessions.GamingActive });

        var work = new ToolStripMenuItem("Work");
        var active = sessions.ActiveWorkspaces;
        foreach (var ws in host.Config.Work)
        {
            var name = ws.Name;
            work.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => host.OpenWorkspace(name))
            { Checked = active.Contains(name, StringComparer.OrdinalIgnoreCase) });
        }
        if (host.Config.Work.Count > 0) work.DropDownItems.Add(new ToolStripSeparator());
        work.DropDownItems.Add(new ToolStripMenuItem("Add workspace…", null, (_, _) => showConfig(true)));
        menu.Items.Add(work);

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
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => showConfig(false)));
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
