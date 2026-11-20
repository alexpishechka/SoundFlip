using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SoundFlip
{
    // Небольшой набор элементов в стиле «Параметров» Windows 11: карточки-строки, тумблеры, кнопки.
    class Palette
    {
        public bool Dark;
        public float S;
        public Color Bg, Card, CardHover, CardPressed, Border, Text, Sub, Disabled, Accent, OnAccent, Control, ControlHover, ControlBorder;
        public Font Title, Subtitle, Body, BodyStrong, Caption;

        public static Palette Create(float s)
        {
            bool dark = Theme.AppsDark;
            var p = new Palette { Dark = dark, S = s };
            if (dark)
            {
                p.Bg = Color.FromArgb(0x20, 0x20, 0x20);
                p.Card = Color.FromArgb(0x2B, 0x2B, 0x2B);
                p.CardHover = Color.FromArgb(0x32, 0x32, 0x32);
                p.CardPressed = Color.FromArgb(0x27, 0x27, 0x27);
                p.Border = Color.FromArgb(0x1C, 0x1C, 0x1C);
                p.Text = Color.White;
                p.Sub = Color.FromArgb(0xC8, 0xC8, 0xC8);
                p.Disabled = Color.FromArgb(0x78, 0x78, 0x78);
                p.Control = Color.FromArgb(0x37, 0x37, 0x37);
                p.ControlHover = Color.FromArgb(0x3C, 0x3C, 0x3C);
                p.ControlBorder = Color.FromArgb(0x45, 0x45, 0x45);
                p.OnAccent = Color.Black;
            }
            else
            {
                p.Bg = Color.FromArgb(0xF3, 0xF3, 0xF3);
                p.Card = Color.FromArgb(0xFB, 0xFB, 0xFB);
                p.CardHover = Color.FromArgb(0xF6, 0xF6, 0xF6);
                p.CardPressed = Color.FromArgb(0xF2, 0xF2, 0xF2);
                p.Border = Color.FromArgb(0xE5, 0xE5, 0xE5);
                p.Text = Color.FromArgb(0x1B, 0x1B, 0x1B);
                p.Sub = Color.FromArgb(0x5F, 0x5F, 0x5F);
                p.Disabled = Color.FromArgb(0xA0, 0xA0, 0xA0);
                p.Control = Color.White;
                p.ControlHover = Color.FromArgb(0xF9, 0xF9, 0xF9);
                p.ControlBorder = Color.FromArgb(0xD8, 0xD8, 0xD8);
                p.OnAccent = Color.White;
            }
            p.Accent = Theme.Accent(dark);

            string text = HasFont("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
            string disp = HasFont("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
            p.Title = new Font(disp, 28 * s, FontStyle.Bold, GraphicsUnit.Pixel);
            p.Subtitle = new Font(text, 14 * s, FontStyle.Bold, GraphicsUnit.Pixel);
            p.Body = new Font(text, 14 * s, GraphicsUnit.Pixel);
            p.BodyStrong = new Font(text, 14 * s, FontStyle.Bold, GraphicsUnit.Pixel);
            p.Caption = new Font(text, 12 * s, GraphicsUnit.Pixel);
            return p;
        }

        static bool HasFont(string name)
        {
            using (var fc = new InstalledFontCollection())
                foreach (var f in fc.Families) if (f.Name == name) return true;
            return false;
        }

        public int P(float v) { return (int)Math.Round(v * S); }
    }

    abstract class FluentControl : Control
    {
        protected readonly Palette Pal;
        protected bool Hot, Down;

        protected FluentControl(Palette p)
        {
            Pal = p;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Default;
        }

        protected Color ParentBack { get { return Parent is CardRow ? ((CardRow)Parent).CurrentBack : Pal.Bg; } }

        protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hot = false; Down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected Graphics Prepare(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(ParentBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return g;
        }

        protected static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            using (var p = Theme.RoundRect(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        protected static void Stroke(Graphics g, RectangleF r, float rad, Color c)
        {
            using (var p = Theme.RoundRect(r, rad)) using (var pen = new Pen(c, 1f)) g.DrawPath(pen, p);
        }
    }

    // Строка-карточка: [значок] Заголовок / подпись ............ [элемент справа]
    class CardRow : FluentControl
    {
        public string Title, Sub;
        public Control LeftCtl, RightCtl;
        public bool Clickable;
        public string TrailingGlyph; // например, «›» для строк-ссылок
        public bool Gripper;         // ручка перетаскивания справа (строку можно таскать)

        int GripW { get { return Gripper ? Pal.P(32) : 0; } }
        bool OverGrip(Point p) { return Gripper && p.X >= Width - Pal.P(8) - GripW; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = OverGrip(e.Location) || Capture ? (Gripper ? Cursors.SizeAll : Cursors.Default) : Cursors.Default;
        }

        public CardRow(Palette p, string title, string sub, Control left, Control right) : base(p)
        {
            Title = title; Sub = sub; LeftCtl = left; RightCtl = right;
            Height = p.P(sub == null ? 52 : 66);
            if (left != null) Controls.Add(left);
            if (right != null) Controls.Add(right);
        }

        public Color CurrentBack
        {
            get { return Clickable && Down ? Pal.CardPressed : Clickable && Hot ? Pal.CardHover : Pal.Card; }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (LeftCtl != null) LeftCtl.Location = new Point(Pal.P(12), (Height - LeftCtl.Height) / 2);
            int rightPad = Gripper ? Pal.P(8) + GripW : Pal.P(16);
            if (RightCtl != null) RightCtl.Location = new Point(Width - rightPad - RightCtl.Width, (Height - RightCtl.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Pal.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rc = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, rc, Pal.P(4), CurrentBack);
            Stroke(g, rc, Pal.P(4), Pal.Border);

            if (Gripper)
            {
                // Ручка перетаскивания: 2×3 точки (как re-order dots во Fluent); ярче, когда строка под курсором.
                float d = Pal.P(3), step = Pal.P(6);
                float cx = Width - Pal.P(8) - GripW / 2f, cy = Height / 2f;
                using (var b = new SolidBrush(Hot ? Pal.Text : Pal.Sub))
                    for (int col = 0; col < 2; col++)
                        for (int row = 0; row < 3; row++)
                            g.FillEllipse(b, cx + (col - 0.5f) * step - d / 2, cy + (row - 1) * step - d / 2, d, d);
            }

            int x = LeftCtl != null ? LeftCtl.Right + Pal.P(12) : Pal.P(16);
            int right = (RightCtl != null ? RightCtl.Left : Width - (TrailingGlyph != null ? Pal.P(44) : Pal.P(16))) - Pal.P(12);
            var fl = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            Color tc = Enabled ? Pal.Text : Pal.Disabled, sc = Enabled ? Pal.Sub : Pal.Disabled;
            if (Sub == null)
                TextRenderer.DrawText(g, Title, Pal.Body, new Rectangle(x, 0, right - x, Height), tc, CurrentBack, fl);
            else
            {
                int th = Pal.Body.Height, sh = Pal.Caption.Height, top = (Height - th - sh) / 2;
                TextRenderer.DrawText(g, Title, Pal.Body, new Rectangle(x, top, right - x, th), tc, CurrentBack, fl);
                TextRenderer.DrawText(g, Sub, Pal.Caption, new Rectangle(x, top + th, right - x, sh), sc, CurrentBack, fl);
            }
            if (TrailingGlyph != null)
                Theme.DrawGlyphCentered(g, TrailingGlyph, Pal.P(12), Pal.Sub, new RectangleF(Width - Pal.P(44), 0, Pal.P(28), Height));
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); InvalidateAll(); }
        protected override void OnMouseLeave(EventArgs e)
        {
            // Курсор ушёл на дочерний элемент — строка остаётся «под курсором».
            if (ClientRectangle.Contains(PointToClient(Cursor.Position))) return;
            base.OnMouseLeave(e); InvalidateAll();
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); InvalidateAll(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); InvalidateAll(); }

        void InvalidateAll()
        {
            Invalidate();
            foreach (Control c in Controls) c.Invalidate();
        }
    }

    // Тумблер Windows 11 с подписью «Вкл.» / «Откл.» слева.
    class ToggleSwitch : FluentControl
    {
        bool on;
        float knob; // 0 — слева, 1 — справа
        readonly Timer anim = new Timer();
        public event EventHandler CheckedChanged;

        public ToggleSwitch(Palette p, bool value) : base(p)
        {
            on = value; knob = value ? 1 : 0;
            Size = new Size(p.P(96), p.P(24));
            anim.Interval = 10;
            anim.Tick += delegate
            {
                float t = on ? 1 : 0;
                knob += (t - knob) * 0.35f;
                if (Math.Abs(t - knob) < 0.02f) { knob = t; anim.Stop(); }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; anim.Start(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled) Checked = !Checked; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            float sw = Pal.P(40), sh = Pal.P(20);
            var r = new RectangleF(Width - sw - 0.5f, (Height - sh) / 2f, sw, sh);
            Color text = Enabled ? Pal.Text : Pal.Disabled;
            TextRenderer.DrawText(g, on ? "Вкл." : "Откл.", Pal.Body, new Rectangle(0, 0, (int)r.Left - Pal.P(10), Height), text, ParentBack,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            Color accent = Enabled ? Pal.Accent : Pal.Disabled;
            float k = knob;
            // Фон: при включении заливка цветом акцента.
            if (k > 0.01f) Fill(g, r, sh / 2, Color.FromArgb((int)(255 * k), accent));
            if (k < 0.99f)
            {
                Color border = Enabled ? Color.FromArgb((int)(Pal.Dark ? 200 : 150 * 1f), Pal.Sub) : Pal.Disabled;
                using (var p = Theme.RoundRect(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), sh / 2 - 0.5f))
                using (var pen = new Pen(Color.FromArgb((int)(border.A * (1 - k)), border), 1f)) g.DrawPath(pen, p);
                if (Hot && Enabled) Fill(g, r, sh / 2, Color.FromArgb((int)(14 * (1 - k)), Pal.Text));
            }
            // Ползунок: чуть больше при наведении, вытягивается при нажатии — как в Windows 11.
            float d = Pal.P(Down ? 14 : Hot ? 14 : 12);
            float dw = Down ? d + Pal.P(3) : d;
            float pad = (sh - d) / 2;
            float x0 = r.X + pad + (Pal.P(Down ? 2 : 2)), x1 = r.Right - pad - dw - Pal.P(2);
            float kx = x0 + (x1 - x0) * k;
            Color knobC = k > 0.5f ? Pal.OnAccent : (Enabled ? Pal.Sub : Pal.Disabled);
            Fill(g, new RectangleF(kx, r.Y + pad, dw, d), d / 2, knobC);
        }
    }

    // Кнопка-значок (например, выбор иконки устройства).
    class GlyphButton : FluentControl
    {
        public string Glyph;
        public GlyphButton(Palette p, string glyph) : base(p)
        {
            Glyph = glyph;
            Size = new Size(p.P(40), p.P(40));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Fill(g, r, Pal.P(4), Down ? Pal.CardPressed : Hot ? Pal.ControlHover : Pal.Control);
            Stroke(g, r, Pal.P(4), Pal.ControlBorder);
            Theme.DrawGlyphCentered(g, Glyph, Pal.P(Down ? 17 : 18), Pal.Text, new RectangleF(0, 0, Width, Height));
        }
    }

    // Обычная кнопка / выпадающая кнопка со стрелкой.
    class FluentButton : FluentControl
    {
        public bool Chevron;
        public bool Danger;
        public bool Accent;

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        public FluentButton(Palette p, string text, bool chevron) : base(p)
        {
            Chevron = chevron;
            SetText(text);
        }

        public void SetText(string t)
        {
            Text = t;
            Size sz = TextRenderer.MeasureText(t, Pal.Body);
            Size = new Size(sz.Width + Pal.P(Chevron ? 48 : 28), Pal.P(32));
            if (Parent != null) Parent.PerformLayout();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prepare(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Color bg;
            Color tc;
            if (Accent && Enabled)
            {
                // Основная кнопка Windows 11: заливка цветом акцента, при наведении чуть прозрачнее.
                bg = Mix(Pal.Bg, Pal.Accent, Down ? 0.8f : Hot ? 0.9f : 1f);
                Fill(g, r, Pal.P(4), bg);
                tc = Pal.OnAccent;
            }
            else
            {
                bg = Down && Enabled ? Pal.CardPressed : Hot && Enabled ? Pal.ControlHover : Pal.Control;
                Fill(g, r, Pal.P(4), bg);
                Stroke(g, r, Pal.P(4), Pal.ControlBorder);
                tc = !Enabled ? Pal.Disabled : Danger && Hot ? Color.FromArgb(0xE8, 0x4C, 0x4C) : Pal.Text;
            }
            int textW = Width - Pal.P(Chevron ? 34 : 0);
            TextRenderer.DrawText(g, Text, Pal.Body, new Rectangle(Chevron ? Pal.P(12) : 0, 0, Chevron ? textW - Pal.P(12) : Width, Height), tc, bg,
                (Chevron ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Chevron)
                Theme.DrawGlyphCentered(g, "", Pal.P(10), Pal.Sub, new RectangleF(Width - Pal.P(32), Down ? Pal.P(1) : 0, Pal.P(24), Height));
        }
    }
}
