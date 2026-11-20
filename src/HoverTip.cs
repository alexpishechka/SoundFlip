using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SoundFlip
{
    // Всплывающая подсказка над панелью задач в стиле Windows 11 (как подписи у значков панели).
    // Окно не активируется и пропускает клики сквозь себя.
    class HoverTip : NativeWindow
    {
        readonly Timer fade = new Timer();
        float alpha, target;
        Bitmap bmp;
        Point pos;
        string text;

        public HoverTip()
        {
            fade.Interval = 10;
            fade.Tick += delegate
            {
                alpha += (target - alpha) * 0.35f;
                if (Math.Abs(target - alpha) < 0.02f) { alpha = target; fade.Stop(); if (alpha == 0) Hide(); }
                Push();
            };
        }

        public bool Visible { get { return Handle != IntPtr.Zero && target > 0; } }

        // anchor — верхний центр области, над которой показать подсказку (экранные координаты).
        public void Show(string newText, Point anchor, float s)
        {
            if (Handle == IntPtr.Zero)
            {
                var cp = new CreateParams();
                cp.Style = unchecked((int)0x80000000); // WS_POPUP
                cp.ExStyle = 0x80000 | 0x80 | 0x08000000 | 0x8 | 0x20; // LAYERED | TOOLWINDOW | NOACTIVATE | TOPMOST | TRANSPARENT
                CreateHandle(cp);
            }
            bool wasVisible = Visible;
            if (newText != text || bmp == null)
            {
                text = newText;
                if (bmp != null) bmp.Dispose();
                bmp = Render(text, s);
            }
            int shadow = (int)Math.Round(8 * s);
            pos = new Point(anchor.X - bmp.Width / 2, anchor.Y - bmp.Height + shadow - (int)Math.Round(10 * s));
            var wa = Screen.FromPoint(anchor).Bounds;
            pos.X = Math.Max(wa.Left, Math.Min(pos.X, wa.Right - bmp.Width));
            ShowWindow(Handle, 4 /*SW_SHOWNOACTIVATE*/);
            target = 1;
            if (!wasVisible) alpha = 0;
            Push();
            fade.Start();
        }

        public void HideTip()
        {
            if (Handle == IntPtr.Zero || target == 0) return;
            target = 0;
            fade.Start();
        }

        void Hide() { if (Handle != IntPtr.Zero) ShowWindow(Handle, 0 /*SW_HIDE*/); }

        static Bitmap Render(string text, float s)
        {
            bool dark = Theme.AppsDark;
            Color bg = dark ? Color.FromArgb(0x2C, 0x2C, 0x2C) : Color.FromArgb(0xF9, 0xF9, 0xF9);
            Color border = dark ? Color.FromArgb(0x48, 0x48, 0x48) : Color.FromArgb(0xD9, 0xD9, 0xD9);
            Color fg = dark ? Color.White : Color.FromArgb(0x1B, 0x1B, 0x1B);
            string family = "Segoe UI";
            using (var fc = new InstalledFontCollection())
                foreach (var f in fc.Families) if (f.Name == "Segoe UI Variable Text") { family = f.Name; break; }

            using (var font = new Font(family, 12 * s, GraphicsUnit.Pixel))
            {
                Size ts;
                using (var tmp = new Bitmap(1, 1)) using (var g = Graphics.FromImage(tmp))
                    ts = Size.Ceiling(g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic));
                int padX = (int)Math.Round(9 * s), padY = (int)Math.Round(6 * s), shadow = (int)Math.Round(8 * s);
                int w = ts.Width + 2 * padX, h = ts.Height + 2 * padY;
                var bmp = new Bitmap(w + 2 * shadow, h + 2 * shadow, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float r = 4 * s;
                    // Мягкая тень.
                    for (int i = shadow; i > 0; i--)
                    {
                        var sr = new RectangleF(shadow - i, shadow - i + i * 0.4f, w + 2 * i, h + 2 * i);
                        using (var p = Theme.RoundRect(sr, r + i))
                        using (var b = new SolidBrush(Color.FromArgb((int)(dark ? 9 : 5) * (shadow - i + 1) / shadow, 0, 0, 0))) g.FillPath(b, p);
                    }
                    var rc = new RectangleF(shadow + 0.5f, shadow + 0.5f, w - 1, h - 1);
                    using (var p = Theme.RoundRect(rc, r))
                    {
                        using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                        using (var pen = new Pen(border, 1f)) g.DrawPath(pen, p);
                    }
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (var b = new SolidBrush(fg))
                        g.DrawString(text, font, b, shadow + padX, shadow + padY, StringFormat.GenericTypographic);
                }
                return bmp;
            }
        }

        void Push()
        {
            if (Handle == IntPtr.Zero || bmp == null) return;
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(mem, hbmp);
            try
            {
                var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
                var src = new POINT();
                // Появляется с лёгким подъёмом снизу.
                var dst = new POINT { x = pos.X, y = pos.Y + (int)((1 - alpha) * 4) };
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = (byte)(255 * Math.Max(0, Math.Min(1, alpha))), AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
            }
            finally
            {
                SelectObject(mem, old);
                DeleteObject(hbmp);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        public void Dispose()
        {
            fade.Stop();
            if (Handle != IntPtr.Zero) DestroyHandle();
            if (bmp != null) bmp.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pptDst, ref SIZE size, IntPtr src, ref POINT pptSrc, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    }
}
