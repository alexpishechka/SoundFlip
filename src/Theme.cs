using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SoundFlip
{
    // Всё, что нужно, чтобы выглядеть «как Windows»: тема, акцентный цвет, системный шрифт иконок.
    static class Theme
    {
        const string Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public const string GlyphHeadphones = "";
        public const string GlyphSpeakers = "";
        public const string GlyphVolume = "";
        public const string GlyphSettings = "";

        // Иконки, которые можно выбрать для устройства.
        public static readonly string[,] IconChoices =
        {
            { "", "Наушники" },
            { "", "Гарнитура" },
            { "", "Колонки" },
            { "", "Монитор / ТВ" },
            { "", "Bluetooth" },
            { "", "Игры" },
            { "", "Звук" },
        };

        public static string GuessGlyph(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (Has(n, "гарнитур", "headset")) return "";
            if (Has(n, "наушник", "headphone", "earphone", "airpods", "buds", "earpods")) return GlyphHeadphones;
            if (Has(n, "динамик", "speaker", "колонк")) return GlyphSpeakers;
            if (Has(n, "hdmi", "displayport", "monitor", "монитор", "nvidia", "amd high definition", "tv")) return "";
            return GlyphVolume;
        }

        static bool Has(string s, params string[] words)
        {
            foreach (var w in words) if (s.Contains(w)) return true;
            return false;
        }

        static string iconFont;
        public static string IconFont
        {
            get
            {
                if (iconFont == null)
                {
                    iconFont = "Segoe MDL2 Assets";
                    using (var fc = new InstalledFontCollection())
                        foreach (var f in fc.Families)
                            if (f.Name == "Segoe Fluent Icons") { iconFont = f.Name; break; }
                }
                return iconFont;
            }
        }

        public static bool AppsDark { get { return ReadDword(Personalize, "AppsUseLightTheme", 1) == 0; } }
        public static bool TaskbarDark { get { return ReadDword(Personalize, "SystemUsesLightTheme", 0) == 0; } }

        public static Color Accent(bool dark)
        {
            try
            {
                var pal = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentPalette", null) as byte[];
                if (pal != null && pal.Length >= 32)
                {
                    int i = dark ? 1 : 4; // Light2 для тёмной темы, Dark1 для светлой — как в Windows 11
                    return Color.FromArgb(pal[i * 4], pal[i * 4 + 1], pal[i * 4 + 2]);
                }
            }
            catch { }
            return dark ? Color.FromArgb(0x60, 0xCD, 0xFF) : Color.FromArgb(0x00, 0x5F, 0xB8);
        }

        static int ReadDword(string key, string name, int def)
        {
            try
            {
                object v = Registry.GetValue(key, name, null);
                if (v is int) return (int)v;
            }
            catch { }
            return def;
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void DrawGlyph(Graphics g, string glyph, float pixelSize, Color color, RectangleF r)
        {
            using (var f = new Font(IconFont, pixelSize, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(color))
            using (var sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.DrawString(glyph, f, br, r, sf);
            }
        }

        // Рисует значок контуром и ставит его точно по центру прямоугольника (по реальным границам фигуры).
        public static void DrawGlyphCentered(Graphics g, string glyph, float pixelSize, Color color, RectangleF r)
        {
            using (var ff = new FontFamily(IconFont))
            using (var path = new GraphicsPath())
            using (var sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                path.AddString(glyph, ff, (int)FontStyle.Regular, pixelSize, PointF.Empty, sf);
                RectangleF b = path.GetBounds();
                if (b.Width <= 0) return;
                using (var m = new Matrix())
                {
                    // Округляем до половины пикселя — так линии значка чётче.
                    float dx = (float)Math.Round((r.X + (r.Width - b.Width) / 2 - b.X) * 2) / 2;
                    float dy = (float)Math.Round((r.Y + (r.Height - b.Height) / 2 - b.Y) * 2) / 2;
                    m.Translate(dx, dy);
                    path.Transform(m);
                }
                var old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new SolidBrush(color)) g.FillPath(br, path);
                g.SmoothingMode = old;
            }
        }

        // Иконка программы прямо из ресурсов exe — не зависит от кэша иконок Windows.
        public static Icon AppIcon(int size)
        {
            try
            {
                using (var st = typeof(Theme).Assembly.GetManifestResourceStream("SoundFlip.app.ico"))
                    if (st != null) return new Icon(st, size, size);
            }
            catch { }
            return null;
        }

        public static Icon MakeTrayIcon(string glyph, bool white, int size)
        {
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    DrawGlyph(g, glyph, size * 0.8f, white ? Color.White : Color.Black, new RectangleF(0, 0, size, size));
                }
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return icon;
            }
        }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        // Тёмные/светлые системные меню (недокументированное API uxtheme, то же, что использует Проводник).
        [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();

        public static void ApplyMenuTheme()
        {
            try { SetPreferredAppMode(AppsDark ? 2 : 3); FlushMenuThemes(); } catch { }
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        public static void StyleWindow(IntPtr hwnd, Color border)
        {
            try
            {
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref round, 4);
                int c = border.R | (border.G << 8) | (border.B << 16);
                DwmSetWindowAttribute(hwnd, 34, ref c, 4);
            }
            catch { }
        }
    }
}
