using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace OmniCard.Controls.Themes;

public static class OmniCardTheme
{
    // Splash-inspired dark palette
    private static readonly Color DarkBackground     = Color.FromRgb(0x1E, 0x1E, 0x2E); // #1E1E2E
    private static readonly Color DarkSurface        = Color.FromRgb(0x2E, 0x2E, 0x3E); // #2E2E3E
    private static readonly Color DarkSurfaceHigh    = Color.FromRgb(0x38, 0x38, 0x4A); // #38384A
    private static readonly Color DarkBorder         = Color.FromRgb(0x44, 0x44, 0x56); // #444456
    private static readonly Color DarkForeground     = Colors.White;
    private static readonly Color DarkForegroundDim  = Color.FromRgb(0xAA, 0xAA, 0xAA); // #AAAAAA

    public static void Apply(bool isDark)
    {
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
        paletteHelper.SetTheme(theme);

        if (isDark)
            ApplyDarkOverrides();
    }

    private static void ApplyDarkOverrides()
    {
        var res = Application.Current.Resources;

        SetBrush(res, "MaterialDesign.Brush.Background",                  DarkBackground);
        SetBrush(res, "MaterialDesign.Brush.Card.Background",             DarkSurface);
        SetBrush(res, "MaterialDesign.Brush.Foreground",                  DarkForeground);
        SetBrush(res, "MaterialDesign.Brush.Foreground.Light",            DarkForegroundDim);
        SetBrush(res, "MaterialDesign.Brush.TextBox.HoverBackground",     DarkSurfaceHigh);
        SetBrush(res, "MaterialDesign.Brush.TextBox.HoverBorder",         DarkBorder);
        SetBrush(res, "MaterialDesign.Brush.DataGrid.ColumnHeader.Border", DarkBorder);
    }

    private static void SetBrush(ResourceDictionary res, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        res[key] = brush;
    }
}
