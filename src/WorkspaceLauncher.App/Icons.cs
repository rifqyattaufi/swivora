using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorkspaceLauncher.Core;

namespace WorkspaceLauncher.App;

/// <summary>Program icons and names, read from the exe itself. Results are frozen so the Start menu list can load them off the UI thread.</summary>
public static class AppIcons
{
    static readonly ConcurrentDictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Full path of a program given as a path or a command such as code or notepad.exe, or null when it cannot be found.</summary>
    public static string? Resolve(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return null;
        var text = Environment.ExpandEnvironmentVariables(exe.Trim().Trim('"'));
        if (Path.IsPathRooted(text)) return File.Exists(text) ? text : null;
        if (!Path.HasExtension(text)) text += ".exe";
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Append(Environment.SystemDirectory).Append(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        foreach (var dir in dirs)
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), text);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { } // a malformed PATH entry
        }
        return null;
    }

    public static ImageSource? IconOf(string? exe)
    {
        var path = Resolve(exe);
        return path is null ? null : Icons.GetOrAdd(path, Extract);
    }

    static ImageSource? Extract(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception) { return null; } // no icon is fine, the row just has no picture
    }

    /// <summary>The name a person knows the program by (Visual Studio Code, not Code.exe), falling back to the file name.</summary>
    public static string NameOf(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return "";
        var stem = Path.GetFileNameWithoutExtension(exe.Trim().Trim('"'));
        var path = Resolve(exe);
        return path is null ? stem : Names.GetOrAdd(path, p => ReadName(p) ?? stem);
    }

    static string? ReadName(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            // system tools all share "Microsoft Windows Operating System" as product, their description is the useful one
            var product = info.ProductName?.Trim();
            if (string.IsNullOrEmpty(product) || product.Contains("Operating System", StringComparison.OrdinalIgnoreCase)) product = info.FileDescription?.Trim();
            return string.IsNullOrEmpty(product) ? null : product;
        }
        catch (Exception) { return null; }
    }
}

/// <summary>Mode icons: an emoji, or a picture stored beside the config file ("img:icons\x.png"). Modes with neither get their first letter.</summary>
public static class ModeIcons
{
    public const string ImagePrefix = "img:";

    static readonly (string Name, string Emoji)[] Defaults = [("gaming", "🎮"), ("work", "💼")];

    public static string IconsFolder => Path.Combine(Path.GetDirectoryName(AppHost.ConfigPath)!, "icons");

    public static bool IsImage(string? icon) => icon?.StartsWith(ImagePrefix, StringComparison.Ordinal) == true;

    /// <summary>The emoji or letter to draw when there is no picture.</summary>
    public static string Glyph(string name, string? icon)
    {
        if (!string.IsNullOrWhiteSpace(icon) && !IsImage(icon)) return icon;
        var known = Defaults.FirstOrDefault(d => string.Equals(d.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (known.Emoji is not null) return known.Emoji;
        var first = name.Trim();
        return first.Length == 0 ? "?" : StringInfo.GetNextTextElement(first).ToUpperInvariant();
    }

    public static ImageSource? Picture(string? icon)
    {
        if (!IsImage(icon)) return null;
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(AppHost.ConfigPath)!, icon![ImagePrefix.Length..]);
            if (!File.Exists(path)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // do not keep the file open
            image.DecodePixelWidth = 256;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }

    /// <summary>A rounded square holding the picture, emoji or letter. Size is the side length.</summary>
    public static FrameworkElement Badge(string name, string? icon, double size)
    {
        var picture = Picture(icon);
        UIElement content = picture is not null
            ? new Image { Source = picture, Stretch = Stretch.Uniform, Margin = new Thickness(size * 0.12) }
            : new TextBlock
            {
                Text = Glyph(name, icon),
                FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI"),
                FontSize = size * 0.55,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.FindResource("TextFillColorPrimaryBrush"),
            };
        var badge = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(Math.Max(6, size * 0.2)),
            Background = (Brush)Application.Current.FindResource("ControlFillColorSecondaryBrush"),
            Child = content,
        };
        return badge;
    }

    /// <summary>Copies a chosen picture beside the config so the mode keeps working if the original moves.</summary>
    public static string ImportImage(string source)
    {
        Directory.CreateDirectory(IconsFolder);
        var name = $"{Guid.NewGuid():N}{Path.GetExtension(source).ToLowerInvariant()}";
        File.Copy(source, Path.Combine(IconsFolder, name));
        return $"{ImagePrefix}icons\\{name}";
    }

    /// <summary>Opens the Windows emoji panel (Win + .) for whatever text box has focus.</summary>
    public static void OpenEmojiPanel()
    {
        const byte LWin = 0x5B, Period = 0xBE;
        const uint KeyUp = 2;
        keybd_event(LWin, 0, 0, 0);
        keybd_event(Period, 0, 0, 0);
        keybd_event(Period, 0, KeyUp, 0);
        keybd_event(LWin, 0, KeyUp, 0);
    }

    [DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte scan, uint flags, nuint extra);
}
