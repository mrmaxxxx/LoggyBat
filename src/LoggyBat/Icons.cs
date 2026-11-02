using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;   // System.IO.Path is in the implicit usings

namespace LoggyBat;

// The approved icons and colours of the "Шкалы" design (Max, 2026-11-03). Path data is the same as
// docs/design/icons.svg on a 24x24 grid with 1.5 strokes; change both together. Vector paths stay
// sharp from 16 to 30 px. On the taskbar the contours use the system text colour (like the percent);
// in the flyout and settings the contour, track and divider colours follow the light/dark theme
// (callers pass the element's ActualTheme and redraw on ActualThemeChanged); bar and bolt do not.
internal static class Icons
{
    private const string Mouse =
        "M5.9 8.1A6.1 6.1 0 0 1 18.1 8.1V15.9A6.1 6.1 0 0 1 5.9 15.9Z M5.9 10.8H18.1 M12 5V7.9";

    private const string Headset =
        "M3.9 10.1A8.1 8.1 0 0 1 20.1 10.1 " +
        "M4.6 9.6H5A2.1 2.1 0 0 1 7.1 11.7V16.3A2.1 2.1 0 0 1 5 18.4H4.6A2.1 2.1 0 0 1 2.5 16.3V11.7A2.1 2.1 0 0 1 4.6 9.6Z " +
        "M19 9.6H19.4A2.1 2.1 0 0 1 21.5 11.7V16.3A2.1 2.1 0 0 1 19.4 18.4H19A2.1 2.1 0 0 1 16.9 16.3V11.7A2.1 2.1 0 0 1 19 9.6Z " +
        "M19.2 18.4C18.9 20.3 16.5 20.9 14.4 20.9 " +
        "M11.45 19.7H13.25A1.15 1.15 0 0 1 14.4 20.85A1.15 1.15 0 0 1 13.25 22H11.45A1.15 1.15 0 0 1 10.3 20.85A1.15 1.15 0 0 1 11.45 19.7Z";

    private const string Microphone =
        "M8.2 5.9A3.85 3.85 0 0 1 15.9 5.9V11.1A3.85 3.85 0 0 1 8.2 11.1Z M10.4 6.3H13.7M10.4 9.1H13.7 " +
        "M6.4 10.5A5.6 5.6 0 0 0 17.6 10.5 M12 16.1V19.9 " +
        "M8.85 19.9H15.25A1.05 1.05 0 0 1 16.3 20.95A1.05 1.05 0 0 1 15.25 22H8.85A1.05 1.05 0 0 1 7.8 20.95A1.05 1.05 0 0 1 8.85 19.9Z";

    private const string Keyboard =
        "M3.8 6.4H20.2A1.8 1.8 0 0 1 22 8.2V15.8A1.8 1.8 0 0 1 20.2 17.6H3.8A1.8 1.8 0 0 1 2 15.8V8.2A1.8 1.8 0 0 1 3.8 6.4Z " +
        "M4.9 9.2H5.1M8.4 9.2H8.6M11.9 9.2H12.1M15.4 9.2H15.6M18.9 9.2H19.1" +
        "M4.9 12H5.1M8.4 12H8.6M11.9 12H12.1M15.4 12H15.6M18.9 12H19.1 M5.2 15H18.8";

    private const string Adapter =
        "M10.4 2.3V5.2M13.1 2.3V5.2 " +
        "M9.5 5.2H14.5A1.6 1.6 0 0 1 16.1 6.8V13.8A1.6 1.6 0 0 1 14.5 15.4H9.5A1.6 1.6 0 0 1 7.9 13.8V6.8A1.6 1.6 0 0 1 9.5 5.2Z " +
        "M12 15.4V18.4C12 19.8 13.8 19.2 14.8 20C15.7 20.7 16 21.3 16 22";

    private const string BoltPath = "M15.4 2L6 13.8H11.6L8.5 22L18 10H13.2Z";

    public static readonly Color Bar = Rgb(0x2C, 0xC9, 0xDC);          // turquoise, both themes
    public static readonly Color BoltColor = Rgb(0xFA, 0xB3, 0x3F);    // orange, both themes

    public static Color Contour(ElementTheme theme) => theme == ElementTheme.Light ? Rgb(0x3A, 0x3F, 0x45) : Rgb(0xD0, 0xD2, 0xD8);
    public static Color Track(ElementTheme theme) => theme == ElementTheme.Light ? Rgb(0xDD, 0xE1, 0xE6) : Rgb(0x32, 0x39, 0x41);
    public static Color Divider(ElementTheme theme) => theme == ElementTheme.Light ? Rgb(0xE2, 0xE5, 0xE9) : Rgb(0x38, 0x3D, 0x44);

    // kind: DeviceList.IconKind (mouse, headset, keyboard, microphone, adapter).
    private static string DataFor(string kind) => kind switch
    {
        "mouse" => Mouse,
        "headset" => Headset,
        "keyboard" => Keyboard,
        "microphone" => Microphone,
        _ => Adapter,
    };

    // Flyout and settings: the approved contour colour of the theme.
    public static FrameworkElement Device(string kind, double size, ElementTheme theme) =>
        Make(DataFor(kind), size, stroke: Contour(theme), fill: null);

    // Taskbar: the same colour as the percent text next to it - the theme's primary text colour
    // (TextFillColorPrimaryBrush). As a ThemeResource it follows theme changes by itself.
    public static FrameworkElement DeviceInTextColor(string kind, double size)
    {
        string px = size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return (FrameworkElement)XamlReader.Load(
            "<Viewbox xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            $"Width='{px}' Height='{px}' VerticalAlignment='Center'><Canvas Width='24' Height='24'>" +
            $"<Path Data='{DataFor(kind)}' Stroke='{{ThemeResource TextFillColorPrimaryBrush}}' StrokeThickness='1.5' " +
            "StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/></Canvas></Viewbox>");
    }

    public static FrameworkElement Bolt(double size) => Make(BoltPath, size, stroke: null, fill: BoltColor);

    private static FrameworkElement Make(string data, double size, Color? stroke, Color? fill)
    {
        // A Geometry can belong to one element only, so every icon parses its own copy.
        var path = new XamlPath
        {
            Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        if (stroke is { } s)
            path.Stroke = new SolidColorBrush(s);
        if (fill is { } f)
            path.Fill = new SolidColorBrush(f);

        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
}
