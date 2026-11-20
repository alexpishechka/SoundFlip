using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SoundFlip
{
    // Выпадающий список в стиле Windows 11 (как у ComboBox в «Параметрах»):
    // выбранный пункт открывается прямо поверх кнопки и помечен синей полоской слева.
    class FluentMenu : Form
    {
        public class Item
        {
            public string Text;
            public string Glyph;
            public bool Selected;
            public Action OnPick;
        }

        readonly Palette pal;
        readonly List<Item> items;
        readonly bool glyphs;
        readonly int itemH, pad;
        readonly Color bg, border, hoverC;
        int hover = -1, down = -1;
        readonly Timer fade = new Timer();

        public FluentMenu(Palette p, List<Item> items)
        {
            pal = p;
            this.items = items;
            foreach (var it in items) if (it.Glyph != null) glyphs = true;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            bg = p.Dark ? Color.FromArgb(0x2C, 0x2C, 0x2C) : Color.FromArgb(0xF9, 0xF9, 0xF9);
            border = p.Dark ? Color.FromArgb(0x45, 0x45, 0x45) : Color.FromArgb(0xD5, 0xD5, 0xD5);
            hoverC = p.Dark ? Color.FromArgb(0x38, 0x38, 0x38) : Color.FromArgb(0xEC, 0xEC, 0xEC);
            BackColor = bg;
            // Один и тот же отступ везде: от краёв списка и между пунктами.
            itemH = p.P(32);
            pad = p.P(4);
            Opacity = 0;
            fade.Interval = 10;
            fade.Tick += delegate { Opacity = Math.Min(1, Opacity + 0.2); if (Opacity >= 1) fade.Stop(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4);
                int c = border.R | (border.G << 8) | (border.B << 16); DwmSetWindowAttribute(Handle, 34, ref c, 4);
            }
            catch { }
        }

        // Показать над/под кнопкой так, чтобы выбранный пункт совпал с кнопкой.
        public void ShowFor(Control owner)
        {
            int textW = 0;
            foreach (var it in items) textW = Math.Max(textW, TextRenderer.MeasureText(it.Text, pal.Body).Width);
            int w = Math.Max(owner.Width + 2 * pad, textW + pal.P(glyphs ? 72 : 44));
            int h = items.Count * (itemH + pad) + pad;
            int sel = Math.Max(0, items.FindIndex(i => i.Selected));
            var o = owner.PointToScreen(Point.Empty);
            int x = o.X - pad, y = o.Y + (owner.Height - itemH) / 2 - pad - sel * (itemH + pad);
            var wa = Screen.FromControl(owner).WorkingArea;
            x = Math.Max(wa.Left + 4, Math.Min(x, wa.Right - w - 4));
            y = Math.Max(wa.Top + 4, Math.Min(y, wa.Bottom - h - 4));
            Bounds = new Rectangle(x, y, w, h);
            hover = sel;
            Show(owner.FindForm());
            Activate();
            fade.Start();
        }

        Rectangle ItemRect(int i) { return new Rectangle(pad, pad + i * (itemH + pad), Width - 2 * pad, itemH); }

        int HitTest(Point p)
        {
            // Промежутки между пунктами делим пополам, чтобы курсор не «проваливался».
            for (int i = 0; i < items.Count; i++)
            {
                var r = ItemRect(i);
                r.Inflate(0, pad / 2 + 1);
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < items.Count; i++)
            {
                var r = ItemRect(i);
                var plate = new RectangleF(r.X, r.Y, r.Width, r.Height);
                Color back = bg;
                if (i == down) back = pal.Dark ? Color.FromArgb(0x32, 0x32, 0x32) : Color.FromArgb(0xEF, 0xEF, 0xEF);
                else if (i == hover || items[i].Selected) back = hoverC;
                if (back != bg)
                    using (var p = Theme.RoundRect(plate, pal.P(4))) using (var b = new SolidBrush(back)) g.FillPath(b, p);
                if (items[i].Selected)
                {
                    float ph = pal.P(16), pw = pal.P(3);
                    var pill = new RectangleF(r.X, r.Y + (r.Height - ph) / 2, pw, ph);
                    using (var p = Theme.RoundRect(pill, pw / 2)) using (var b = new SolidBrush(pal.Accent)) g.FillPath(b, p);
                }
                int tx = r.X + pal.P(12);
                if (glyphs)
                {
                    if (items[i].Glyph != null)
                        Theme.DrawGlyphCentered(g, items[i].Glyph, pal.P(16), pal.Text, new RectangleF(tx, r.Y, pal.P(20), r.Height));
                    tx += pal.P(32);
                }
                TextRenderer.DrawText(g, items[i].Text, pal.Body, new Rectangle(tx, r.Y, r.Right - tx - pal.P(8), r.Height), pal.Text, back,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; down = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = HitTest(e.Location); Invalidate(); base.OnMouseDown(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            bool ok = i >= 0 && i == down;
            down = -1;
            if (ok) Pick(i); else Invalidate();
            base.OnMouseUp(e);
        }

        void Pick(int i)
        {
            Close();
            if (items[i].OnPick != null) items[i].OnPick();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Down) { hover = Math.Min(items.Count - 1, hover + 1); Invalidate(); }
            else if (e.KeyCode == Keys.Up) { hover = Math.Max(0, hover - 1); Invalidate(); }
            else if (e.KeyCode == Keys.Enter && hover >= 0) Pick(hover);
            base.OnKeyDown(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!IsDisposed) BeginInvoke((Action)Close);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    }
}
