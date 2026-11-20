using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SoundFlip
{
    // Состояние отрисовки: какое устройство выбрано и «степень» наведения / выделения / нажатия каждой кнопки (0..1).
    class WidgetVisual
    {
        public int Slot = -1;
        public float PillX;
        public bool Slide; // подчёркивание/точка едут за PillX (иначе гаснут на старом месте и вырастают на новом)
        public float[] Hover = new float[0], Sel = new float[0], Press = new float[0];

        public static WidgetVisual Static(int n, int slot, int hover, float pillX)
        {
            var v = new WidgetVisual { Slot = slot, PillX = pillX, Hover = new float[n], Sel = new float[n], Press = new float[n] };
            if (hover >= 0 && hover < n && hover != slot) v.Hover[hover] = 1;
            if (slot >= 0 && slot < n) v.Sel[slot] = 1;
            return v;
        }
    }

    // Переключатель устройств, встроенный прямо в панель задач слева от области значков (стрелка ^, Wi-Fi, звук).
    // Это дочернее окно Shell_TrayWnd с попиксельной прозрачностью, поэтому выглядит как часть панели.
    class TaskbarWidget : NativeWindow
    {
        enum Kind { Pill, Soft, Capsule, Underline, Dot }

        // Стиль = вид индикатора + высота: обычная (32) или как подсветка значков трея (40) + размер значка.
        static readonly Kind[] StyleKinds = { Kind.Pill, Kind.Pill, Kind.Capsule, Kind.Soft, Kind.Underline, Kind.Dot };
        static readonly bool[] StyleTall = { false, true, false, true, true, true };
        static readonly float[] StyleIcon = { 16, 19, 17, 19, 20, 20 };
        public static readonly string[] StyleNames =
        {
            "Пилюля", "Пилюля (высота трея)", "Капсула", "Подсветка как в трее", "Подчёркивание", "Точка"
        };

        static int Norm(int style) { return style < 0 || style >= StyleNames.Length ? 0 : style; }
        public static bool IsPillStyle(int style) { return Inner(style); }
        // Стили, где индикатор может плавно переезжать: пилюли, капсула, подчёркивание, точка.
        public static bool CanSlide(int style) { var k = StyleKinds[Norm(style)]; return k != Kind.Soft; }

        // Скорости анимаций (постоянные времени, мс) — подобраны под системные кнопки Windows 11.
        const float TauHoverIn = 25, TauHoverOut = 60, TauSel = 35, TauPress = 20, TauSlide = 40;

        readonly TrayApp app;
        IntPtr taskbar;
        float s = 1f;
        int w, h;
        int hover = -1, pressed = -1, slot = -1;
        readonly WidgetVisual vis = new WidgetVisual();
        float pillTarget;
        readonly Timer anim;
        readonly Stopwatch clock = new Stopwatch();
        long lastTick;
        bool tracking;
        string themeKey;
        readonly HoverTip tip = new HoverTip();
        readonly Timer tipTimer = new Timer();
        Point lastPos = new Point(int.MinValue, 0);

        public TaskbarWidget(TrayApp app)
        {
            this.app = app;
            anim = new Timer();
            anim.Interval = 10;
            anim.Tick += delegate { Step(); };
            clock.Start();
            tipTimer.Interval = 500; // как у подписей значков панели задач
            tipTimer.Tick += delegate { tipTimer.Stop(); ShowTip(hover); };
        }

        public bool IsAlive
        {
            get { return Handle != IntPtr.Zero && IsWindow(Handle) && taskbar == FindWindow("Shell_TrayWnd", null); }
        }

        int Count { get { return Math.Max(1, app.Shown.Count); } }
        int Style { get { return Norm(app.Cfg.Style); } }
        bool Slides { get { return CanSlide(Style) && app.Cfg.SlidePill; } }

        // ---- геометрия (общая для окна и для превью) ----
        // Пилюли: внешняя подложка, внутри плашки; промежуток между плашками = отступу плашки от края подложки.
        static bool Inner(int style) { var k = StyleKinds[Norm(style)]; return k == Kind.Pill || k == Kind.Capsule; }
        // Всё в целых пикселях — иначе края плашек размываются.
        static float Px(float v) { return (float)Math.Round(v); }
        static float Edge(int style, float s) { return Inner(style) ? Px(3 * s) : 0; }            // от плашки до края подложки
        public static float Gap(int style, float s) { return Px((Inner(style) ? 3 : 2) * s); }   // между плашками
        public static float CellH(int style, float s) { return Px((StyleTall[Norm(style)] ? 40 : 32) * s); }
        public static float PlateW(int style, float s) { return Px((Inner(style) ? 36 : 40) * s); }
        public static float PlateH(int style, float s) { return CellH(style, s) - 2 * Edge(style, s); }
        public static float Pad(float s) { return Px(1 * s); }

        // Прямоугольник плашки i (сюда рисуется выбранная плашка, подсветка и значок).
        public static RectangleF CellAt(int style, int i, float s)
        {
            float e = Edge(style, s), g = Gap(style, s), pw = PlateW(style, s);
            return new RectangleF(Pad(s) + e + i * (pw + g), Pad(s) + e, pw, PlateH(style, s));
        }

        public static RectangleF TrackAt(int style, int n, float s)
        {
            return new RectangleF(Pad(s), Pad(s), n * PlateW(style, s) + (n - 1) * Gap(style, s) + 2 * Edge(style, s), CellH(style, s));
        }

        RectangleF Cell(int i) { return CellAt(Style, i, s); }

        void Layout()
        {
            var t = TrackAt(Style, Count, s);
            w = (int)Math.Ceiling(t.Width + Pad(s) * 2);
            h = (int)Math.Ceiling(t.Height + Pad(s) * 2);
        }

        public bool Attach(int currentSlot)
        {
            Detach();
            taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero || FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null) == IntPtr.Zero) return false;

            using (var g = Graphics.FromHwnd(IntPtr.Zero)) s = g.DpiX / 96f;
            Layout();

            var cp = new CreateParams();
            cp.Caption = "SoundFlip";
            cp.Parent = taskbar;
            cp.Style = unchecked((int)(WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS));
            cp.ExStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW;
            cp.Width = w;
            cp.Height = h;
            try { CreateHandle(cp); } catch { return false; }
            if (Handle == IntPtr.Zero) return false;

            SnapTo(currentSlot);
            lastPos = new Point(int.MinValue, 0);
            Reposition();
            Render();
            return true;
        }

        // Изменился список устройств или стиль.
        public void Rebuild(int currentSlot)
        {
            if (Handle == IntPtr.Zero) return;
            Layout();
            SnapTo(currentSlot);
            lastPos = new Point(int.MinValue, 0);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, w, h, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
            Reposition();
            Render();
        }

        // Мгновенно привести всё к текущему состоянию, без анимации.
        void SnapTo(int newSlot)
        {
            anim.Stop();
            slot = newSlot;
            hover = -1;
            pressed = -1;
            int n = Count;
            vis.Hover = new float[n];
            vis.Press = new float[n];
            vis.Sel = new float[n];
            if (slot >= 0 && slot < n) vis.Sel[slot] = 1;
            vis.Slot = slot;
            if (slot >= 0) vis.PillX = pillTarget = Cell(slot).X;
        }

        public void Detach()
        {
            anim.Stop();
            HideTip();
            if (Handle != IntPtr.Zero)
            {
                try { if (IsWindow(Handle)) DestroyWindow(Handle); } catch { }
                try { ReleaseHandle(); } catch { }
            }
        }

        // Вызывается по таймеру: следим за положением трея (он меняет ширину), темой и текущим устройством.
        public void Tick(int currentSlot)
        {
            Reposition();
            string key = Theme.TaskbarDark + "|" + Theme.Accent(Theme.TaskbarDark).ToArgb();
            if (currentSlot != slot) SetSlot(currentSlot);
            else if (key != themeKey && !anim.Enabled) Render();
        }

        void SetSlot(int newSlot)
        {
            bool wasNone = slot < 0;
            slot = newSlot;
            vis.Slot = slot;
            if (slot >= 0)
            {
                pillTarget = Cell(slot).X;
                // Пилюля переезжает, только если так выбрано в настройках; иначе — мгновенно.
                if (wasNone || !Slides) vis.PillX = pillTarget;
                if (Inner(Style))
                    for (int i = 0; i < vis.Sel.Length; i++) vis.Sel[i] = i == slot ? 1 : 0;
            }
            StartAnim();
        }

        void StartAnim()
        {
            if (!anim.Enabled) { lastTick = clock.ElapsedMilliseconds; anim.Start(); }
            Step();
        }

        static float Approach(float v, float target, float dt, float tau)
        {
            float k = 1 - (float)Math.Exp(-dt / tau);
            v += (target - v) * k;
            return Math.Abs(target - v) < 0.004f ? target : v;
        }

        // Один кадр анимации: всё плавно стремится к целевым значениям.
        void Step()
        {
            long now = clock.ElapsedMilliseconds;
            float dt = Math.Max(1, Math.Min(50, now - lastTick));
            lastTick = now;
            bool busy = false;
            int n = vis.Hover.Length;
            for (int i = 0; i < n; i++)
            {
                // В пилюлях выбранную плашку не подсвечиваем (она и так залита), в остальных стилях — подсвечиваем.
                float ht = (i == hover && (i != slot || !Inner(Style))) ? 1 : 0;
                vis.Hover[i] = Approach(vis.Hover[i], ht, dt, ht > vis.Hover[i] ? TauHoverIn : TauHoverOut);
                float st = i == slot ? 1 : 0;
                vis.Sel[i] = Approach(vis.Sel[i], st, dt, TauSel);
                float pt = i == pressed ? 1 : 0;
                vis.Press[i] = Approach(vis.Press[i], pt, dt, TauPress);
                busy |= vis.Hover[i] != ht || vis.Sel[i] != st || vis.Press[i] != pt;
            }
            if (slot >= 0)
            {
                float d = pillTarget - vis.PillX;
                if (Math.Abs(d) < 0.3f) vis.PillX = pillTarget;
                else { vis.PillX += d * (1 - (float)Math.Exp(-dt / TauSlide)); busy = true; }
            }
            Render();
            if (!busy) anim.Stop();
        }

        void Reposition()
        {
            IntPtr notify = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            RECT tb, nr;
            if (notify == IntPtr.Zero || !GetWindowRect(taskbar, out tb) || !GetWindowRect(notify, out nr)) return;
            int x = nr.L - tb.L - w - (int)(4 * s);
            int y = ((tb.B - tb.T) - h) / 2;
            // XAML-содержимое панели Windows 11 — тоже дочернее окно; держим себя поверх него.
            bool onTop = GetWindow(taskbar, 5 /*GW_CHILD*/) == Handle;
            if (x == lastPos.X && y == lastPos.Y && onTop) return;
            lastPos = new Point(x, y);
            SetWindowPos(Handle, IntPtr.Zero /*HWND_TOP*/, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }

        void Render()
        {
            if (Handle == IntPtr.Zero) return;
            bool dark = Theme.TaskbarDark;
            Color accent = Theme.Accent(dark);
            themeKey = dark + "|" + accent.ToArgb();
            var glyphs = new List<string>();
            foreach (var d in app.Shown) glyphs.Add(d.Glyph);
            if (glyphs.Count == 0) glyphs.Add(Theme.GlyphSettings);
            vis.Slide = Slides;
            using (var bmp = Paint(Style, glyphs, vis, s, dark, accent))
                Push(bmp);
        }

        // Для превью: статичная картинка.
        public static Bitmap Paint(int style, IList<string> glyphs, int slot, float pillX, int hover, float s, bool dark, Color accent)
        {
            return Paint(style, glyphs, WidgetVisual.Static(glyphs.Count, slot, hover, pillX), s, dark, accent);
        }

        // Рисует переключатель в битмап.
        public static Bitmap Paint(int style, IList<string> glyphs, WidgetVisual v, float s, bool dark, Color accent)
        {
            style = Norm(style);
            Kind kind = StyleKinds[style];
            int n = glyphs.Count;
            var track = TrackAt(style, n, s);
            int w = (int)Math.Ceiling(track.Width + Pad(s) * 2), h = (int)Math.Ceiling(track.Height + Pad(s) * 2);
            Color fg = dark ? Color.White : Color.Black;
            Color onAccent = dark ? Color.Black : Color.White;
            Color dim = Color.FromArgb(dark ? 150 : 140, fg);
            bool inner = Inner(style);
            float g0 = Gap(style, s);
            // Скругления как в Windows 11: 4 у подсветки значков; у пилюль внешнее = внутреннее + отступ (концентрично).
            float r = (inner ? 5 : 4) * s;
            float plateR = kind == Kind.Capsule ? PlateH(style, s) / 2 : r;

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.FromArgb(1, 0, 0, 0)); // почти прозрачный фон — чтобы ловить клики по всей площади

                if (kind == Kind.Pill)
                    FillRound(g, track, r + Edge(style, s), Color.FromArgb(dark ? 18 : 14, fg));
                else if (kind == Kind.Capsule)
                    FillRound(g, track, track.Height / 2, Color.FromArgb(dark ? 18 : 14, fg));

                // Наведение (плавно появляется/гаснет; при нажатии подсветка чуть тускнеет, как у системных кнопок).
                for (int i = 0; i < n && i < v.Hover.Length; i++)
                {
                    if (kind == Kind.Soft && i == v.Slot) continue; // для активной плашки — см. ниже (она становится ярче)
                    float k = v.Hover[i] * (1 - 0.4f * v.Press[i]);
                    if (k <= 0.003f) continue;
                    var rc = CellAt(style, i, s);
                    if (inner) FillRound(g, rc, plateR, Color.FromArgb((int)((dark ? 22 : 18) * k), fg));
                    else HoverPlate(g, rc, plateR, dark, k);
                }

                // Выбранное устройство.
                if (v.Slot >= 0)
                {
                    if (inner)
                    {
                        var pc = new RectangleF(v.PillX, CellAt(style, 0, s).Y, PlateW(style, s), PlateH(style, s));
                        FillRound(g, pc, plateR, accent);
                    }
                    else if (v.Slide && (kind == Kind.Underline || kind == Kind.Dot))
                    {
                        // Индикатор плавно переезжает под новое устройство.
                        var pc = new RectangleF(v.PillX, CellAt(style, 0, s).Y, PlateW(style, s), PlateH(style, s));
                        if (kind == Kind.Underline)
                        {
                            float lw = 16 * s, lh = 3 * s;
                            FillRound(g, new RectangleF(pc.X + (pc.Width - lw) / 2, pc.Bottom - lh - 1.5f * s, lw, lh), lh / 2, accent);
                        }
                        else
                        {
                            float d = 5 * s;
                            using (var b = new SolidBrush(accent))
                                g.FillEllipse(b, pc.X + (pc.Width - d) / 2, pc.Bottom - 1.5f * s - 2.5f * s - d / 2, d, d);
                        }
                    }
                    else
                        for (int i = 0; i < n && i < v.Sel.Length; i++)
                        {
                            float k = v.Sel[i];
                            if (k <= 0.003f) continue;
                            var pc = CellAt(style, i, s);
                            switch (kind)
                            {
                                case Kind.Soft: // как подсветка значка в трее; старая гаснет, новая появляется
                                    {
                                        // При наведении плашка плавно становится такой, как у активного окна на панели задач.
                                        float hv = i < v.Hover.Length ? v.Hover[i] : 0;
                                        float pk = k * (1 - 0.3f * v.Press[i]);
                                        if (hv < 0.997f) HoverPlate(g, pc, r, dark, pk * (1 - hv));
                                        if (hv > 0.003f) ActivePlate(g, pc, r, dark, pk * hv);
                                    }
                                    break;
                                case Kind.Underline: // полоска «вырастает» из центра, как индикатор приложений
                                    {
                                        float lw = (6 + 10 * k) * s, lh = 3 * s;
                                        FillRound(g, new RectangleF(pc.X + (pc.Width - lw) / 2, pc.Bottom - lh - 1.5f * s, lw, lh), lh / 2,
                                            Color.FromArgb((int)(255 * Math.Min(1, k * 1.5f)), accent));
                                    }
                                    break;
                                case Kind.Dot:
                                    {
                                        float d = (2 + 3 * k) * s;
                                        using (var b = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1, k * 1.5f)), accent)))
                                            g.FillEllipse(b, pc.X + (pc.Width - d) / 2, pc.Bottom - 1.5f * s - 2.5f * s - d / 2, d, d);
                                    }
                                    break;
                            }
                        }
                }

                for (int i = 0; i < n; i++)
                {
                    var cell = CellAt(style, i, s);
                    Color c;
                    if (inner)
                    {
                        float on = v.Slot < 0 ? 0 : Math.Max(0, 1 - Math.Abs(v.PillX - cell.X) / (cell.Width + g0)); // плавно, пока плашка едет
                        c = Mix(fg, onAccent, on);
                    }
                    else if (v.Slide && (kind == Kind.Underline || kind == Kind.Dot))
                    {
                        float on = v.Slot < 0 ? 0 : Math.Max(0, 1 - Math.Abs(v.PillX - cell.X) / (cell.Width + g0)); // ярче, пока индикатор подъезжает
                        c = Mix(dim, fg, on);
                    }
                    else c = Mix(dim, fg, i < v.Sel.Length ? v.Sel[i] : 0);
                    // При нажатии значок чуть сжимается — как у системных кнопок.
                    float press = i < v.Press.Length ? v.Press[i] : 0;
                    Theme.DrawGlyphCentered(g, glyphs[i], StyleIcon[style] * s * (1 - 0.1f * press), c, cell);
                }
            }
            return bmp;
        }

        // Плашка наведения Windows 11: ~6% белого (чёрного в светлой теме) и светлая кромка сверху, плавно исчезающая.
        static void HoverPlate(Graphics g, RectangleF rc, float r, bool dark, float k)
        {
            Color baseC = dark ? Color.White : Color.Black;
            FillRound(g, rc, r, Color.FromArgb((int)((dark ? 16 : 10) * k), baseC));
            var edge = new RectangleF(rc.X + 0.5f, rc.Y + 0.5f, rc.Width - 1, rc.Height - 1);
            float eh = Math.Max(2, r * 1.5f);
            using (var p = Theme.RoundRect(edge, Math.Max(0, r - 0.5f)))
            using (var br = new LinearGradientBrush(new PointF(0, rc.Top), new PointF(0, rc.Top + eh),
                Color.FromArgb((int)((dark ? 30 : 22) * k), baseC), Color.FromArgb(0, baseC)))
            {
                br.WrapMode = WrapMode.TileFlipXY;
                g.SetClip(new RectangleF(rc.X, rc.Y, rc.Width, eh));
                using (var pen = new Pen(br, 1f)) g.DrawPath(pen, p);
                g.ResetClip();
            }
        }

        // Плашка активного окна Windows 11 (замерено): заливка ~8% белого, яркая кромка сверху,
        // нижний край чуть темнее основной заливки.
        static void ActivePlate(Graphics g, RectangleF rc, float r, bool dark, float k)
        {
            Color baseC = dark ? Color.White : Color.Black;
            FillRound(g, rc, r, Color.FromArgb((int)((dark ? 22 : 12) * k), baseC));
            var edge = new RectangleF(rc.X + 0.5f, rc.Y + 0.5f, rc.Width - 1, rc.Height - 1);
            using (var p = Theme.RoundRect(edge, Math.Max(0, r - 0.5f)))
            {
                float eh = Math.Max(2, r * 1.5f);
                using (var br = new LinearGradientBrush(new PointF(0, rc.Top), new PointF(0, rc.Top + eh),
                    Color.FromArgb((int)((dark ? 34 : 24) * k), baseC), Color.FromArgb(0, baseC)))
                {
                    br.WrapMode = WrapMode.TileFlipXY;
                    g.SetClip(new RectangleF(rc.X, rc.Y, rc.Width, eh));
                    using (var pen = new Pen(br, 1f)) g.DrawPath(pen, p);
                    g.ResetClip();
                }
                // Нижний край: заливка там слабее, плашка будто приподнята.
                var bottom = dark ? Color.Black : Color.White;
                float bh = Math.Max(2, r * 0.75f);
                using (var br = new LinearGradientBrush(new PointF(0, rc.Bottom - bh - 0.01f), new PointF(0, rc.Bottom),
                    Color.FromArgb(0, bottom), Color.FromArgb((int)((dark ? 18 : 10) * k), bottom)))
                {
                    br.WrapMode = WrapMode.TileFlipXY;
                    g.SetClip(new RectangleF(rc.X, rc.Bottom - bh, rc.Width, bh));
                    using (var pb = Theme.RoundRect(rc, r)) g.FillPath(br, pb);
                    g.ResetClip();
                }
            }
        }

        static void FillRound(Graphics g, RectangleF rc, float r, Color c)
        {
            using (var p = Theme.RoundRect(rc, r))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        void Push(Bitmap bmp)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(mem, hbmp);
            try
            {
                var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
                var src = new POINT();
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screen, IntPtr.Zero, ref size, mem, ref src, 0, ref blend, 2 /*ULW_ALPHA*/);
            }
            finally
            {
                SelectObject(mem, old);
                DeleteObject(hbmp);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        int HitTest(IntPtr lParam)
        {
            int x = (short)(lParam.ToInt64() & 0xFFFF);
            float half = Gap(Style, s) / 2;
            for (int i = 0; i < Count; i++)
            {
                var c = Cell(i);
                if (x >= c.X - half && x < c.Right + half) return i; // промежутки между плашками тоже кликабельны
            }
            return -1;
        }

        void SetHover(int i)
        {
            if (i == hover) return;
            hover = i;
            if (pressed >= 0 && pressed != i) pressed = -1;
            if (i < 0) HideTip();
            else if (tip.Visible) ShowTip(i);      // подсказка уже открыта — сразу переключаем на другое устройство
            else { tipTimer.Stop(); tipTimer.Start(); }
            StartAnim();
        }

        void HideTip()
        {
            tipTimer.Stop();
            tip.HideTip();
        }

        void ShowTip(int i)
        {
            if (i < 0 || i >= app.Shown.Count || Handle == IntPtr.Zero) return;
            RECT wr, tb;
            if (!GetWindowRect(Handle, out wr) || !GetWindowRect(taskbar, out tb)) return;
            var c = Cell(i);
            tip.Show(app.DeviceName(i), new Point(wr.L + (int)(c.X + c.Width / 2), tb.T), s);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0021: // WM_MOUSEACTIVATE — не забираем фокус у панели задач
                    m.Result = (IntPtr)3;
                    return;
                case 0x0200: // WM_MOUSEMOVE
                    if (!tracking)
                    {
                        var t = new TRACKMOUSEEVENT { cbSize = Marshal.SizeOf(typeof(TRACKMOUSEEVENT)), dwFlags = 2 /*TME_LEAVE*/, hwndTrack = Handle };
                        tracking = TrackMouseEvent(ref t);
                    }
                    SetHover(HitTest(m.LParam));
                    break;
                case 0x02A3: // WM_MOUSELEAVE
                    tracking = false;
                    pressed = -1;
                    SetHover(-1);
                    break;
                case 0x0201: // WM_LBUTTONDOWN
                    HideTip();
                    pressed = HitTest(m.LParam);
                    StartAnim();
                    return;
                case 0x0202: // WM_LBUTTONUP
                    {
                        int i = HitTest(m.LParam);
                        bool click = i >= 0 && i == pressed;
                        pressed = -1;
                        StartAnim();
                        if (app.Shown.Count == 0) app.OpenSettings();
                        else if (click) app.Select(i);
                    }
                    return;
                case 0x0205: // WM_RBUTTONUP
                    HideTip();
                    app.ShowMenuAtCursor();
                    return;
            }
            base.WndProc(ref m);
        }

        const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
        const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] struct TRACKMOUSEEVENT { public int cbSize; public uint dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT t);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, IntPtr pptDst, ref SIZE size, IntPtr src, ref POINT pptSrc, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    }
}
