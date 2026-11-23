using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Media;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;

internal static class EquipmentImages
{
    private static readonly HashSet<string> Known = ["generic", "hap_ac", "hap_ac2", "hap_ac3", "hap_ax2", "hap_ax3", "hex", "hex_s", "rb", "ccr", "crs", "cap", "wap", "lhg", "sxt", "sxtsq", "netmetal", "cube", "mantbox", "groove", "ltap", "l009", "rb5009", "chr", "board"];
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();
    private static readonly ConcurrentDictionary<string, ImageSource> DeviceCache = new();
    public static ImageSource ForDevice(Device device)
    {
        var type = device.Type.ToLowerInvariant();
        if (type.Contains("mikrotik")) return Get(device.EquipmentFamily);
        var kind = type.Contains("nas") ? "nas" : type.Contains("сервер") || type.Contains("server") ? "server" : type.Contains("пк") || type.Contains("windows") || type.Contains("computer") ? "pc" : type.Contains("принтер") ? "printer" : type.Contains("коммутатор") ? "switch" : type.Contains("ибп") ? "ups" : "other";
        return DeviceCache.GetOrAdd(kind, DrawDevice);
    }
    private static ImageSource DrawDevice(string kind)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            // These shared thumbnails use a fixed palette and must remain freezable.
            var pen = new Pen(Brushes.SlateGray, 1.5);
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 60, 36));
            if (kind == "pc")
            {
                dc.DrawRoundedRectangle(Brushes.LightSteelBlue, pen, new Rect(8, 3, 44, 24), 2, 2);
                dc.DrawLine(pen, new Point(30, 27), new Point(30, 33)); dc.DrawLine(pen, new Point(20, 33), new Point(40, 33));
            }
            else if (kind is "server" or "nas")
            {
                dc.DrawRoundedRectangle(Brushes.LightSteelBlue, pen, new Rect(13, 2, 34, 32), 3, 3);
                if (kind == "nas")
                    for (var x = 19; x <= 31; x += 12) { dc.DrawRectangle(Brushes.WhiteSmoke, pen, new Rect(x, 7, 9, 21)); dc.DrawEllipse(Brushes.SeaGreen, null, new Point(x + 4.5, 24), 1.5, 1.5); }
                else for (var y = 7; y <= 25; y += 9) { dc.DrawRectangle(Brushes.WhiteSmoke, pen, new Rect(18, y, 24, 5)); dc.DrawEllipse(Brushes.SeaGreen, null, new Point(38, y + 2.5), 1.5, 1.5); }
            }
            else
            {
                dc.DrawRoundedRectangle(Brushes.LightSteelBlue, pen, new Rect(5, 8, 50, 22), 3, 3);
                if (kind == "switch") for (var x = 11; x <= 43; x += 8) dc.DrawRectangle(Brushes.WhiteSmoke, pen, new Rect(x, 17, 5, 6));
                else if (kind == "printer") dc.DrawRectangle(Brushes.WhiteSmoke, pen, new Rect(17, 2, 26, 17));
                else if (kind == "ups") dc.DrawGeometry(Brushes.Goldenrod, null, Geometry.Parse("M 31,10 L 22,21 L 29,21 L 26,29 L 38,17 L 31,17 Z"));
                else dc.DrawEllipse(Brushes.SeaGreen, null, new Point(30, 19), 4, 4);
            }
        }
        drawing.Freeze(); var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
    public static BitmapImage Get(string family)
    {
        family = Known.Contains(family) ? family : "generic";
        return Cache.GetOrAdd(family, key =>
        {
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri("pack://application:,,,/Assets/" + key + ".png", UriKind.Absolute); image.EndInit(); image.Freeze(); return image;
        });
    }
}
