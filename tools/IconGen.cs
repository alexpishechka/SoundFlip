// Генератор иконки программы (src/app.ico): синяя плитка, белые полупрозрачные наушники и круговые стрелки «замены».
// Каждый размер рисуется отдельно в 8-кратном разрешении и уменьшается с качественной фильтрацией.
// Размеры меньше 256 хранятся как несжатые 32-битные DIB (лучше всего читаются оболочкой Windows), 256 — как PNG.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace SoundFlip
{
    static class IconGen
    {
        const string Headphones = "";

        static void Draw(Graphics g, float S)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // Плитка на всю клетку, как у иконок Windows 11 и большинства современных приложений.
            var tile = new RectangleF(0, 0, S, S);

            // Плитка: голубой → синий градиент и мягкий блик сверху.
            using (var p = Theme.RoundRect(tile, tile.Width * 0.225f))
            {
                using (var b = new LinearGradientBrush(new PointF(0, tile.Top), new PointF(0, tile.Bottom),
                    Color.FromArgb(0x4C, 0xA8, 0xFF), Color.FromArgb(0x00, 0x5A, 0xD6))) g.FillPath(b, p);
                g.SetClip(p);
                using (var b = new LinearGradientBrush(new PointF(0, tile.Top - 1), new PointF(0, tile.Top + tile.Height * 0.5f),
                    Color.FromArgb(50, Color.White), Color.FromArgb(0, Color.White)))
                    g.FillRectangle(b, tile.X, tile.Y, tile.Width, tile.Height * 0.5f);
                g.ResetClip();
            }

            // Стрелки и наушники — на одном слое, прозрачность накладывается на слой целиком (без наложений).
            int n = (int)Math.Ceiling(S);
            float t = S * 0.049f; // толщина линий: одинаковая у стрелок и наушников
            using (var layer = new Bitmap(n, n, PixelFormat.Format32bppArgb))
            {
                using (var lg = Graphics.FromImage(layer))
                {
                    lg.SmoothingMode = SmoothingMode.AntiAlias;
                    lg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    SwapArrows(lg, S / 2, S / 2, S * 0.395f, t, Color.White);
                    DrawHeadphones(lg, S, t);
                }
                var cm = new ColorMatrix();
                cm.Matrix33 = 215 / 255f;
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    g.DrawImage(layer, new Rectangle(0, 0, n, n), 0, 0, n, n, GraphicsUnit.Pixel, ia);
                }
            }

        }

        // Наушники (глиф E7F6), утолщённые обводкой до толщины стрелок; внешний размер — как у глифа размером 0.49·S.
        // Чуть выше центра — их низ визуально тяжелее.
        static void DrawHeadphones(Graphics g, float S, float t)
        {
            using (var path = new GraphicsPath())
            using (var ff = new FontFamily(Theme.IconFont))
            {
                path.AddString(Headphones, ff, 0, 100f, PointF.Empty, StringFormat.GenericTypographic);
                var b0 = path.GetBounds();
                float stroke100 = GlyphStroke(path, b0);           // толщина линии глифа при размере 100
                float hTarget = b0.Height * (S * 0.49f / 100f);    // внешняя высота как у прежних наушников
                float k = (hTarget - t) / (b0.Height - stroke100); // масштаб глифа
                float pen = t - stroke100 * k;                      // обводка добавляет недостающую толщину
                using (var m = new Matrix()) { m.Scale(k, k); path.Transform(m); }
                var b = path.GetBounds();
                using (var m = new Matrix())
                {
                    m.Translate(S / 2 - (b.X + b.Width / 2), S / 2 - S * 0.016f - (b.Y + b.Height / 2));
                    path.Transform(m);
                }
                g.FillPath(Brushes.White, path);
                if (pen > 0) using (var p = new Pen(Color.White, pen) { LineJoin = LineJoin.Round }) g.DrawPath(p, path);
            }
        }

        // Толщина дужки глифа: длина первой заполненной серии сверху по центральной вертикали.
        static float GlyphStroke(GraphicsPath path, RectangleF b)
        {
            int w = (int)Math.Ceiling(b.Right) + 2, h = (int)Math.Ceiling(b.Bottom) + 2, scale = 10;
            using (var bmp = new Bitmap(w * scale, h * scale))
            using (var g = Graphics.FromImage(bmp))
            using (var m = new Matrix())
            using (var p = (GraphicsPath)path.Clone())
            {
                m.Scale(scale, scale); p.Transform(m);
                g.Clear(Color.Black);
                g.FillPath(Brushes.White, p);
                int x = (int)((b.X + b.Width / 2) * scale), y = (int)(b.Y * scale), run = 0;
                while (y < bmp.Height && bmp.GetPixel(x, y).R < 128) y++;
                while (y < bmp.Height && bmp.GetPixel(x, y).R >= 128) { run++; y++; }
                return run / (float)scale;
            }
        }

        static void SwapArrows(Graphics g, float cx, float cy, float R, float t, Color c)
        {
            using (var pen = new Pen(c, t))
            using (var brush = new SolidBrush(c))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Flat;
                var rc = new RectangleF(cx - R, cy - R, 2 * R, 2 * R);
                const float sweep = 125f;
                foreach (float start in new[] { 200f, 20f })
                {
                    g.DrawArc(pen, rc, start, sweep);
                    double a = (start + sweep) * Math.PI / 180;
                    var tip = new PointF(cx + R * (float)Math.Cos(a), cy + R * (float)Math.Sin(a));
                    var dir = new PointF(-(float)Math.Sin(a), (float)Math.Cos(a));
                    var nrm = new PointF((float)Math.Cos(a), (float)Math.Sin(a));
                    float L = t * 2.1f, W = t * 1.9f;
                    g.FillPolygon(brush, new[]
                    {
                        new PointF(tip.X + dir.X * L, tip.Y + dir.Y * L),
                        new PointF(tip.X + nrm.X * W - dir.X * t * 0.2f, tip.Y + nrm.Y * W - dir.Y * t * 0.2f),
                        new PointF(tip.X - nrm.X * W - dir.X * t * 0.2f, tip.Y - nrm.Y * W - dir.Y * t * 0.2f),
                    });
                }
            }
        }

        static Bitmap Render(int size)
        {
            int S = size * 8;
            using (var big = new Bitmap(S, S, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(big)) { g.Clear(Color.Transparent); Draw(g, S); }
                var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.DrawImage(big, new Rectangle(0, 0, size, size));
                }
                return bmp;
            }
        }

        static byte[] Dib(Bitmap bmp)
        {
            int n = bmp.Width;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(40); w.Write(n); w.Write(n * 2); w.Write((short)1); w.Write((short)32);
                w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                for (int y = n - 1; y >= 0; y--)
                    for (int x = 0; x < n; x++)
                    {
                        var c = bmp.GetPixel(x, y);
                        w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
                    }
                w.Write(new byte[((n + 31) / 32) * 4 * n]); // AND-маска (не используется при 32 битах)
                return ms.ToArray();
            }
        }

        static void Main(string[] args)
        {
            string outPath = args.Length > 0 ? args[0] : "app.ico";
            string previewDir = args.Length > 1 ? args[1] : null;
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
            var images = new List<byte[]>();
            foreach (int sz in sizes)
                using (var bmp = Render(sz))
                {
                    if (previewDir != null) bmp.Save(Path.Combine(previewDir, "icon_" + sz + ".png"));
                    if (sz >= 256)
                        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); images.Add(ms.ToArray()); }
                    else images.Add(Dib(bmp));
                }

            using (var fs = File.Create(outPath))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte b = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(b); w.Write(b); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32); w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
            Console.WriteLine("OK: " + outPath);
        }
    }
}
