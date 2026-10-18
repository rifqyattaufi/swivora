using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Swivora.App;

/// <summary>Swivora's colors. The accent is the brand blue, applied over the Windows accent.</summary>
public static class Brand
{
    public static readonly Color Blue = Color.FromRgb(0x3B, 0x82, 0xF6);
    public static readonly Color Violet = Color.FromRgb(0x8B, 0x5C, 0xF6);
    public static readonly Color Indigo = Color.FromRgb(0x1E, 0x1B, 0x4B);

    public static void ApplyAccent(ApplicationTheme? theme = null) =>
        ApplicationAccentColorManager.Apply(Blue, theme ?? ApplicationThemeManager.GetAppTheme());
}
