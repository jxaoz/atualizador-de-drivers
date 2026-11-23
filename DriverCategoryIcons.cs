using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace DriverUpdater
{
    internal sealed class DriverCategoryIcons : IDisposable
    {
        private enum Category
        {
            Video, Audio, Multimedia, Network, Bluetooth, Usb, Storage, Input, Print,
            Camera, System, Firmware, Power, Software, Other
        }

        private sealed class Appearance
        {
            public readonly string Label;
            public readonly Color Accent;

            public Appearance(string label, int red, int green, int blue)
            {
                Label = label;
                Accent = Color.FromArgb(red, green, blue);
            }
        }

        private static readonly Dictionary<Category, Appearance> Palette = new Dictionary<Category, Appearance>
        {
            { Category.Video, new Appearance("Vídeo", 63, 58, 178) },
            { Category.Audio, new Appearance("Áudio", 168, 31, 118) },
            { Category.Multimedia, new Appearance("Áudio e multimídia", 168, 31, 118) },
            { Category.Network, new Appearance("Rede", 0, 102, 134) },
            { Category.Bluetooth, new Appearance("Bluetooth", 105, 51, 173) },
            { Category.Usb, new Appearance("USB", 0, 106, 77) },
            { Category.Storage, new Appearance("Armazenamento", 163, 75, 0) },
            { Category.Input, new Appearance("Dispositivos de entrada", 25, 89, 185) },
            { Category.Print, new Appearance("Impressão", 130, 49, 151) },
            { Category.Camera, new Appearance("Câmera e imagem", 176, 52, 59) },
            { Category.System, new Appearance("Sistema", 65, 73, 119) },
            { Category.Firmware, new Appearance("Firmware", 123, 62, 164) },
            { Category.Power, new Appearance("Energia e bateria", 110, 105, 0) },
            { Category.Software, new Appearance("Componentes de software", 49, 83, 141) },
            { Category.Other, new Appearance("Outros dispositivos", 71, 82, 113) }
        };

        private readonly Dictionary<Tuple<Category, int, bool>, Bitmap> icons =
            new Dictionary<Tuple<Category, int, bool>, Bitmap>();

        public Bitmap GetIcon(string driverClass, int dpi)
        {
            Category category = GetCategory(driverClass);
            int size = Math.Max(1, (int)Math.Round(24F * (dpi > 0 ? dpi : 96) / 96F));
            bool highContrast = SystemInformation.HighContrast;
            var key = Tuple.Create(category, size, highContrast);
            Bitmap icon;
            if (!icons.TryGetValue(key, out icon))
            {
                icon = DrawIcon(category, size, highContrast);
                icons.Add(key, icon);
            }
            return icon;
        }

        public static string GetLabel(string driverClass)
        {
            return Palette[GetCategory(driverClass)].Label;
        }

        public static string ResolveOfferClass(string driverClass, string title)
        {
            if (!string.IsNullOrWhiteSpace(driverClass) &&
                !string.Equals(driverClass, "OtherHardware", StringComparison.OrdinalIgnoreCase))
                return driverClass;

            if (string.IsNullOrWhiteSpace(title))
                return driverClass;

            // Some Windows Update offers use a generic catalog class but name the setup class in the title.
            Match match = Regex.Match(title, @"-\s*(?<class>[A-Za-z0-9]+)\s*-\s*\d|\b(?<class>[A-Za-z0-9]+)\s+Driver Update\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            string candidate = match.Groups["class"].Value;
            return GetLabel(candidate) == GetLabel(null) ? driverClass : candidate;
        }

        public void Dispose()
        {
            foreach (Bitmap icon in icons.Values)
                icon.Dispose();
            icons.Clear();
        }

        private static Category GetCategory(string driverClass)
        {
            switch ((driverClass ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "DISPLAY":
                case "MONITOR":
                    return Category.Video;
                case "AUDIOENDPOINT":
                case "AUDIOPROCESSINGOBJECT":
                    return Category.Audio;
                case "MEDIA":
                    return Category.Multimedia;
                case "NET":
                case "NETCLIENT":
                case "NETSERVICE":
                case "NETTRANS":
                case "MODEM":
                    return Category.Network;
                case "BLUETOOTH":
                    return Category.Bluetooth;
                case "USB":
                case "USBDEVICE":
                case "USBFUNCTION":
                case "UCM":
                case "WCEUSBS":
                    return Category.Usb;
                case "CDROM":
                case "DISKDRIVE":
                case "FDC":
                case "FLOPPYDISK":
                case "HDC":
                case "MEDIUMCHANGER":
                case "SCSIADAPTER":
                case "STORAGE":
                case "TAPEDRIVE":
                case "VOLUME":
                case "VOLUMESNAPSHOT":
                    return Category.Storage;
                case "HIDCLASS":
                case "KEYBOARD":
                case "MOUSE":
                    return Category.Input;
                case "DOT4PRINT":
                case "PRINTER":
                case "PRINTQUEUE":
                case "PRINTUPGRADE":
                    return Category.Print;
                case "CAMERA":
                case "IMAGE":
                    return Category.Camera;
                case "COMPUTER":
                case "PROCESSOR":
                case "SECURITYDEVICES":
                case "SYSTEM":
                    return Category.System;
                case "FIRMWARE":
                    return Category.Firmware;
                case "BATTERY":
                    return Category.Power;
                case "SOFTWARECOMPONENT":
                case "SOFTWAREDEVICE":
                    return Category.Software;
                default:
                    return Category.Other;
            }
        }

        private static Bitmap DrawIcon(Category category, int size, bool highContrast)
        {
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.ScaleTransform(size / 24F, size / 24F);
                Color accent = Palette[category].Accent;
                Color ink = highContrast ? SystemColors.WindowText : accent;
                var bounds = new RectangleF(1, 1, 22, 22);
                using (var background = new LinearGradientBrush(bounds,
                    highContrast ? SystemColors.Window : Tint(accent, .90F),
                    highContrast ? SystemColors.Window : Tint(accent, .73F), 65F))
                using (var rim = new Pen(highContrast ? SystemColors.WindowText : Tint(accent, .69F), .75F))
                using (var pen = new Pen(ink, 1.55F)
                {
                    LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round
                })
                using (var fill = new SolidBrush(ink))
                using (var tile = AppStyle.RoundedRectangle(bounds, 5.6F))
                {
                    graphics.FillPath(background, tile);
                    graphics.DrawPath(rim, tile);
                    DrawSymbol(graphics, pen, fill, category);
                }
            }
            return bitmap;
        }

        private static void DrawSymbol(Graphics graphics, Pen pen, Brush fill, Category category)
        {
            switch (category)
            {
                case Category.Video:
                    DrawRounded(graphics, pen, 5.6F, 6.4F, 12.8F, 8.9F, 1.2F);
                    graphics.DrawLine(pen, 12, 15.4F, 12, 18);
                    graphics.DrawLine(pen, 9.1F, 18, 14.9F, 18);
                    break;
                case Category.Audio:
                case Category.Multimedia:
                    graphics.DrawPolygon(pen, new[]
                    {
                        new PointF(5.4F, 9.6F), new PointF(8.4F, 9.6F), new PointF(12F, 6.5F),
                        new PointF(12F, 17.5F), new PointF(8.4F, 14.4F), new PointF(5.4F, 14.4F)
                    });
                    graphics.DrawArc(pen, 10.3F, 8.6F, 6.4F, 6.8F, -50, 100);
                    graphics.DrawArc(pen, 9.4F, 5.7F, 10.1F, 12.6F, -48, 96);
                    break;
                case Category.Network:
                    graphics.DrawArc(pen, 3.5F, 7, 17F, 14.8F, 221, 98);
                    graphics.DrawArc(pen, 6.4F, 10.3F, 11.2F, 9.5F, 220, 100);
                    graphics.DrawArc(pen, 9.3F, 13.3F, 5.4F, 5.4F, 215, 110);
                    graphics.FillEllipse(fill, 10.8F, 16.4F, 2.4F, 2.4F);
                    break;
                case Category.Bluetooth:
                    graphics.DrawLines(pen, new[]
                    {
                        new PointF(7.3F, 8.3F), new PointF(16.3F, 15.7F), new PointF(12.2F, 19.2F),
                        new PointF(12.2F, 4.8F), new PointF(16.3F, 8.3F), new PointF(7.3F, 15.7F)
                    });
                    break;
                case Category.Usb:
                    graphics.DrawLine(pen, 12, 5.2F, 12, 17.9F);
                    graphics.DrawLines(pen, new[] { new PointF(10, 7.1F), new PointF(12, 5.2F), new PointF(14, 7.1F) });
                    graphics.DrawLines(pen, new[] { new PointF(12, 14.8F), new PointF(7.4F, 12.3F), new PointF(7.4F, 10.6F) });
                    graphics.DrawLines(pen, new[] { new PointF(12, 12.8F), new PointF(16.4F, 10.9F), new PointF(16.4F, 9.4F) });
                    graphics.FillRectangle(fill, 15.2F, 7.5F, 2.4F, 2.4F);
                    graphics.FillEllipse(fill, 6.2F, 8.5F, 2.4F, 2.4F);
                    graphics.FillEllipse(fill, 10.5F, 16.5F, 3, 3);
                    break;
                case Category.Storage:
                    DrawRounded(graphics, pen, 5.4F, 6.3F, 13.2F, 11.4F, 1.5F);
                    graphics.DrawLine(pen, 5.9F, 12, 18.1F, 12);
                    graphics.FillEllipse(fill, 7.2F, 8.2F, 2.1F, 2.1F);
                    graphics.FillEllipse(fill, 7.2F, 13.9F, 2.1F, 2.1F);
                    graphics.DrawLine(pen, 12.7F, 9.3F, 16.2F, 9.3F);
                    graphics.DrawLine(pen, 12.7F, 15, 16.2F, 15);
                    break;
                case Category.Input:
                    DrawRounded(graphics, pen, 4.9F, 7.7F, 14.2F, 8.7F, 1.6F);
                    for (int key = 0; key < 4; key++)
                        graphics.FillEllipse(fill, 7F + key * 2.7F, 9.7F, 1.6F, 1.6F);
                    graphics.DrawLine(pen, 8.4F, 13.8F, 15.6F, 13.8F);
                    break;
                case Category.Print:
                    graphics.DrawLines(pen, new[]
                    {
                        new PointF(8, 9.5F), new PointF(8, 5.6F), new PointF(16, 5.6F), new PointF(16, 9.5F)
                    });
                    DrawRounded(graphics, pen, 5.3F, 9.8F, 13.4F, 5.6F, 1.3F);
                    graphics.DrawRectangle(pen, 8.1F, 13.3F, 7.8F, 5.4F);
                    graphics.FillEllipse(fill, 15.8F, 10.9F, 1.6F, 1.6F);
                    break;
                case Category.Camera:
                    DrawRounded(graphics, pen, 5.2F, 8, 13.6F, 9.6F, 1.7F);
                    graphics.DrawLines(pen, new[]
                    {
                        new PointF(8.5F, 7.9F), new PointF(9.8F, 5.9F), new PointF(14.2F, 5.9F), new PointF(15.5F, 7.9F)
                    });
                    graphics.DrawEllipse(pen, 9.5F, 10.5F, 5, 5);
                    break;
                case Category.System:
                    DrawRounded(graphics, pen, 7.3F, 7.3F, 9.4F, 9.4F, 1.3F);
                    graphics.FillRectangle(fill, 10.3F, 10.3F, 3.4F, 3.4F);
                    for (int pin = 0; pin < 2; pin++)
                    {
                        float position = 10F + 4F * pin;
                        graphics.DrawLine(pen, position, 5.4F, position, 7.3F);
                        graphics.DrawLine(pen, position, 16.7F, position, 18.6F);
                        graphics.DrawLine(pen, 5.4F, position, 7.3F, position);
                        graphics.DrawLine(pen, 16.7F, position, 18.6F, position);
                    }
                    break;
                case Category.Firmware:
                    graphics.DrawPolygon(pen, new[]
                    {
                        new PointF(12, 5.1F), new PointF(17.6F, 7.8F), new PointF(17.1F, 13.9F),
                        new PointF(12, 18.8F), new PointF(6.9F, 13.9F), new PointF(6.4F, 7.8F)
                    });
                    graphics.DrawLines(pen, new[] { new PointF(13.1F, 8.2F), new PointF(10.2F, 12.1F), new PointF(13.8F, 12.1F), new PointF(11.2F, 15.6F) });
                    break;
                case Category.Power:
                    DrawRounded(graphics, pen, 5.2F, 8.2F, 12.4F, 7.6F, 1.4F);
                    graphics.DrawLine(pen, 19.1F, 10.5F, 19.1F, 13.5F);
                    graphics.FillRectangle(fill, 7.3F, 10.3F, 6F, 3.4F);
                    break;
                case Category.Software:
                    for (int y = 0; y < 2; y++)
                        for (int x = 0; x < 2; x++)
                            DrawRounded(graphics, pen, 6.1F + x * 6.4F, 6.1F + y * 6.4F, 5.4F, 5.4F, 1.15F);
                    break;
                default:
                    graphics.DrawPolygon(pen, new[]
                    {
                        new PointF(12, 5.5F), new PointF(17.6F, 8.7F), new PointF(17.6F, 15.3F),
                        new PointF(12, 18.5F), new PointF(6.4F, 15.3F), new PointF(6.4F, 8.7F)
                    });
                    graphics.DrawLines(pen, new[] { new PointF(6.4F, 8.7F), new PointF(12, 12), new PointF(17.6F, 8.7F) });
                    graphics.DrawLine(pen, 12, 12, 12, 18.4F);
                    break;
            }
        }

        private static void DrawRounded(Graphics graphics, Pen pen, float x, float y, float width, float height, float radius)
        {
            using (var path = AppStyle.RoundedRectangle(new RectangleF(x, y, width, height), radius))
                graphics.DrawPath(pen, path);
        }

        private static Color Tint(Color color, float white)
        {
            return Color.FromArgb(color.R + (int)((255 - color.R) * white),
                color.G + (int)((255 - color.G) * white), color.B + (int)((255 - color.B) * white));
        }
    }
}
