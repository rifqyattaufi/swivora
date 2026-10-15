using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using WorkspaceLauncher.Core;

namespace WorkspaceLauncher.App;

/// <summary>Minimal INotifyPropertyChanged. The config records are immutable, so the editor works on these copies.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
    }

    protected void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AppVm : Observable
{
    string exe = "", name = "", args = "", cwd = "", stopCommand = "";
    Ownership ownership = Ownership.Adopt;
    int closeTimeout = 10;

    public string Exe { get => exe; set { Set(ref exe, value); Name = ""; Raise(nameof(Icon)); Raise(nameof(Title)); Raise(nameof(Summary)); Raise(nameof(Presets)); Raise(nameof(HasPresets)); RefreshStartup(); } }
    /// <summary>Shown in lists. Empty means use the program's own product name.</summary>
    public string Name { get => name; set { Set(ref name, value); Raise(nameof(Title)); } }
    public string Args { get => args; set { Set(ref args, value); Raise(nameof(Summary)); } }
    public string Cwd { get => cwd; set => Set(ref cwd, value); }
    public Ownership Ownership { get => ownership; set => Set(ref ownership, value); }
    public int CloseTimeout { get => closeTimeout; set => Set(ref closeTimeout, value); }
    public string StopCommand { get => stopCommand; set => Set(ref stopCommand, value); }

    /// <summary>What Windows starts at sign-in, read when Settings opens. Shared by every app row.</summary>
    public static IReadOnlyList<StartupEntry> Startup { get; set; } = [];

    public IReadOnlyList<StartupEntry> StartupHits => StartupEntries.Matching(Startup, Exe);
    public bool HasStartup => StartupHits.Count > 0;
    public string StartupMessage
    {
        get
        {
            var names = string.Join(", ", StartupHits.Select(h => h.Display).Distinct());
            return $"{names} starts when you sign in to Windows, so it can be running before you pick a mode. " +
                   "Turning it off keeps the entry. Task Manager > Startup apps can turn it back on.";
        }
    }

    public void RefreshStartup() { Raise(nameof(HasStartup)); Raise(nameof(StartupMessage)); }

    public IReadOnlyList<ArgPreset> Presets => ArgPresets.For(Exe);
    public bool HasPresets => Presets.Count > 0;

    public string Title => !string.IsNullOrWhiteSpace(Name) ? Name : string.IsNullOrWhiteSpace(Exe) ? "New app" : AppIcons.NameOf(Exe);
    public System.Windows.Media.ImageSource? Icon => AppIcons.IconOf(Exe);
    public string Summary => string.IsNullOrWhiteSpace(Args) ? Exe : Args;

    public static AppVm From(AppEntry a) => new()
    {
        exe = a.Exe, name = a.Name ?? "", args = a.Args, cwd = a.Cwd ?? "", ownership = a.Ownership,
        closeTimeout = a.CloseTimeoutSeconds, stopCommand = a.StopCommand ?? "",
    };

    public AppEntry ToEntry() => new(
        Exe.Trim(), Args.Trim(),
        string.IsNullOrWhiteSpace(Cwd) ? null : Cwd.Trim(),
        Ownership, Math.Max(1, CloseTimeout),
        string.IsNullOrWhiteSpace(StopCommand) ? null : StopCommand.Trim(),
        string.IsNullOrWhiteSpace(Name) ? null : Name.Trim());
}

public enum TargetKind { AppsMode, WorkspacesMode, Workspace }

/// <summary>One row of the left list: a mode (with its own apps, or holding workspaces) or a workspace under a mode.</summary>
public sealed class TargetVm : Observable
{
    string name, icon;
    TargetVm? parent;

    public TargetVm(string name, TargetKind kind, IEnumerable<AppEntry> apps, TargetVm? parent = null, string? icon = null)
    {
        this.name = name;
        this.icon = icon ?? "";
        Kind = kind;
        Apps = new ObservableCollection<AppVm>(apps.Select(AppVm.From));
        Apps.CollectionChanged += (_, _) => Raise(nameof(Summary));
        Parent = parent;
    }

    public TargetKind Kind { get; }
    public bool IsMode => Kind != TargetKind.Workspace;
    public bool HasApps => Kind != TargetKind.WorkspacesMode;
    public string Name { get => name; set { Set(ref name, value); Raise(nameof(Badge)); } }

    /// <summary>An emoji, or "img:..." for a picture. Empty means the default for the mode.</summary>
    public string Icon
    {
        get => icon;
        set { Set(ref icon, value); Raise(nameof(Badge)); Raise(nameof(Emoji)); }
    }

    /// <summary>The emoji part of Icon for the text box. Only the first character is kept, so pasting a sentence does no harm.</summary>
    public string Emoji
    {
        get => ModeIcons.IsImage(icon) ? "" : icon;
        set => Icon = string.IsNullOrWhiteSpace(value) ? "" : System.Globalization.StringInfo.GetNextTextElement(value.Trim());
    }

    /// <summary>The picture for the list. Workspaces have none. A new element each time, so it can sit in several places.</summary>
    public System.Windows.FrameworkElement? Badge => IsMode ? ModeIcons.Badge(Name, Icon, 32) : null;
    public ObservableCollection<AppVm> Apps { get; }

    public TargetVm? Parent
    {
        get => parent;
        private set
        {
            parent = value;
            if (value is not null) value.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Name)) Raise(nameof(Subtitle)); };
        }
    }

    public string Summary => Apps.Count switch { 0 => "No apps yet", 1 => "1 app", _ => $"{Apps.Count} apps" };

    public string Subtitle => Kind switch
    {
        TargetKind.AppsMode => "Mode with apps",
        TargetKind.WorkspacesMode => "Mode with workspaces",
        _ => $"Workspace in {Parent?.Name}",
    };

    public System.Windows.Thickness Indent => Kind == TargetKind.Workspace ? new(24, 4, 4, 4) : new(4, 4, 4, 4);
}

public sealed class HotkeyRowVm : Observable
{
    string value;
    public HotkeyRowVm(string key, string label, string value) { Key = key; Label = label; this.value = value; }
    public string Key { get; }
    public string Label { get; }
    public string Value { get => value; set => Set(ref this.value, value); }
}

public sealed record OwnershipOption(Ownership Value, string Label, string Description);
