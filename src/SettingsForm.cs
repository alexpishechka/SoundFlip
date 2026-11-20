using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SoundFlip
{
    // Окно настроек в стиле «Параметров» Windows 11.
    class SettingsForm : Form
    {
        class DeviceRow
        {
            public string Id;
            public string Glyph;
            public bool Connected;
            public ToggleSwitch Toggle;
            public GlyphButton IconButton;
            public CardRow Card;
        }

        readonly TrayApp app;
        readonly Palette pal;
        readonly List<DeviceRow> devices = new List<DeviceRow>();
        readonly Panel content;
        readonly int contentW;
        int y;
        ToggleSwitch slideToggle;
        CardRow slideRow;

        public SettingsForm(TrayApp app)
        {
            this.app = app;
            float s;
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            pal = Palette.Create(s);

            Text = "Настройки SoundFlip";
            BackColor = pal.Bg;
            ForeColor = pal.Text;
            Font = pal.Body;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            Icon = Theme.AppIcon(256) ?? Icon;

            int pad = pal.P(28);
            contentW = pal.P(600) - 2 * pad;
            content = new Panel();
            content.BackColor = pal.Bg;
            content.AutoScroll = true;
            content.Location = Point.Empty;
            Controls.Add(content);

            y = pal.P(24);
            AddHeader(pad);
            AddSection(pad, "Устройства на панели задач", "Включи устройства, между которыми хочешь переключаться. Нажми на значок устройства, чтобы сменить иконку. Тяни за точки справа, чтобы поменять порядок.");
            AddDevices(pad);

            AddSection(pad, "Внешний вид", null);
            var styleBtn = new FluentButton(pal, TaskbarWidget.StyleNames[Math.Max(0, Math.Min(TaskbarWidget.StyleNames.Length - 1, app.Cfg.Style))], true);
            styleBtn.Click += delegate { PickStyle(styleBtn); };
            AddRow(pad, new CardRow(pal, "Вид переключателя", "Как выглядит плашка на панели задач", null, styleBtn));

            slideToggle = new ToggleSwitch(pal, app.Cfg.SlidePill);
            slideToggle.CheckedChanged += delegate { app.Cfg.SlidePill = slideToggle.Checked; app.ConfigChanged(); };
            slideRow = new CardRow(pal, "Плавное перемещение плашки", "Индикатор плавно переезжает на выбранное устройство", null, slideToggle);
            AddRow(pad, slideRow);
            UpdateSlideEnabled();

            AddSection(pad, "Система", null);
            var auto = new ToggleSwitch(pal, Installer.Autostart);
            auto.CheckedChanged += delegate { try { Installer.Autostart = auto.Checked; } catch { } };
            AddRow(pad, new CardRow(pal, "Запускать вместе с Windows", "SoundFlip появится на панели задач сразу после входа", null, auto));

            var sound = new CardRow(pal, "Звуковые параметры Windows", null, null, null);
            sound.Clickable = true;
            sound.TrailingGlyph = ""; // «открыть в новом окне»
            sound.Click += delegate { try { System.Diagnostics.Process.Start("ms-settings:sound"); } catch { } };
            AddRow(pad, sound);

            AddUpdateRow(pad);

            var del = new FluentButton(pal, "Удалить", false);
            del.Danger = true;
            del.Click += delegate
            {
                if (MessageBox.Show(this, "Удалить SoundFlip с этого компьютера?", Program.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                { Installer.Uninstall(); app.Quit(); }
            };
            AddRow(pad, new CardRow(pal, "Удалить SoundFlip", "Убрать программу, автозапуск и ярлык из меню «Пуск»", null, del));

            y += pal.P(16);
            var ver = new Label();
            ver.AutoSize = true;
            ver.Font = pal.Caption;
            ver.ForeColor = pal.Sub;
            ver.Text = "SoundFlip " + Application.ProductVersion.Substring(0, Application.ProductVersion.LastIndexOf('.'));
            ver.Location = new Point(pad, y);
            content.Controls.Add(ver);
            y += ver.PreferredHeight + pal.P(20);

            int maxH = (int)(Screen.FromPoint(Cursor.Position).WorkingArea.Height * 0.85);
            ClientSize = new Size(contentW + 2 * pad, Math.Min(y, maxH));
            content.Size = ClientSize;
            ApplyFrame();
        }

        // Обновления: проверка только по нажатию кнопки, без фоновых запросов.
        void AddUpdateRow(int pad)
        {
            string idle = "Установлена версия " + Updater.CurrentText;
            var btn = new FluentButton(pal, "Проверить", false);
            var row = new CardRow(pal, "Обновления", idle, null, btn);
            Updater.Result found = null;
            Action<string, string> state = delegate(string sub, string text)
            {
                row.Sub = sub; row.Invalidate();
                btn.SetText(text);
            };
            btn.Click += delegate
            {
                if (!btn.Enabled) return;
                if (found == null)
                {
                    btn.Enabled = false;
                    state("Проверяю…", "Проверить");
                    Updater.Run(this, Updater.Check, delegate(Updater.Result r)
                    {
                        btn.Enabled = true;
                        if (!r.Ok) { state(r.Error, "Повторить"); return; }
                        if (!r.Available) { state("Установлена последняя версия (" + Updater.CurrentText + ")", "Проверить"); return; }
                        found = r;
                        btn.Accent = true;
                        state(r.Version == Updater.CurrentText ? "Доступна обновлённая сборка " + r.Version : "Доступна версия " + r.Version, "Обновить");
                    });
                }
                else
                {
                    btn.Enabled = false;
                    state("Скачиваю обновление…", "Обновить");
                    var r = found;
                    Updater.Run(this, () => Updater.DownloadAndRun(r), delegate(string err)
                    {
                        if (err == null) { state("Устанавливаю…", "Обновить"); return; } // установщик сам закроет и перезапустит программу
                        btn.Enabled = true;
                        state("Не удалось обновить: " + err, "Обновить");
                    });
                }
            };
            AddRow(pad, row);
        }

        void AddHeader(int pad)
        {
            int iconSize = pal.P(36);
            var pic = new PictureBox();
            pic.Size = new Size(iconSize, iconSize);
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            pic.Location = new Point(pad, y + pal.P(2));
            try { using (var ic = Theme.AppIcon(256)) if (ic != null) pic.Image = ic.ToBitmap(); } catch { }
            content.Controls.Add(pic);

            var title = new Label();
            title.AutoSize = true;
            title.Font = pal.Title;
            title.ForeColor = pal.Text;
            title.Text = "SoundFlip";
            title.Location = new Point(pad + iconSize + pal.P(12), y);
            content.Controls.Add(title);
            y += Math.Max(iconSize, title.PreferredHeight) + pal.P(8);
        }

        void AddSection(int pad, string header, string hint)
        {
            y += pal.P(20);
            var l = new Label();
            l.AutoSize = true;
            l.Font = pal.Subtitle;
            l.ForeColor = pal.Text;
            l.Text = header;
            l.Location = new Point(pad, y);
            content.Controls.Add(l);
            y += l.PreferredHeight + pal.P(4);
            if (hint != null)
            {
                var h = new Label();
                h.AutoSize = true;
                h.MaximumSize = new Size(contentW, 0);
                h.Font = pal.Caption;
                h.ForeColor = pal.Sub;
                h.Text = hint;
                h.Location = new Point(pad, y);
                content.Controls.Add(h);
                y += h.PreferredHeight + pal.P(6);
            }
            y += pal.P(4);
        }

        void AddRow(int pad, CardRow row)
        {
            row.Location = new Point(pad, y);
            row.Width = contentW;
            content.Controls.Add(row);
            y += row.Height + pal.P(4);
        }

        static string IconName(string glyph)
        {
            for (int i = 0; i < Theme.IconChoices.GetLength(0); i++)
                if (Theme.IconChoices[i, 0] == glyph) return Theme.IconChoices[i, 1];
            return "Звук";
        }

        void AddDevices(int pad)
        {
            var outputs = Audio.GetOutputs();
            var names = new Dictionary<string, string>();
            foreach (var d in outputs) names[d.Id] = d.Name;

            // Сначала уже выбранные (в их порядке), затем остальные подключённые.
            foreach (var e in app.Cfg.Devices)
                AddDevice(pad, e.Id, names.ContainsKey(e.Id) ? names[e.Id] : "Устройство не подключено", e.Glyph, true, names.ContainsKey(e.Id));
            foreach (var d in outputs)
                if (app.Cfg.IndexOf(d.Id) < 0) AddDevice(pad, d.Id, d.Name, Theme.GuessGlyph(d.Name), false, true);

            if (devices.Count == 0)
                AddRow(pad, new CardRow(pal, "Устройства вывода звука не найдены", "Подключи наушники или колонки — они появятся здесь", null, null));
        }

        void AddDevice(int pad, string id, string name, string glyph, bool on, bool connected)
        {
            var row = new DeviceRow { Id = id, Glyph = glyph, Connected = connected };
            row.IconButton = new GlyphButton(pal, glyph);
            row.IconButton.Click += delegate { PickIcon(row); };
            row.Toggle = new ToggleSwitch(pal, on);
            row.Toggle.CheckedChanged += delegate { Apply(); };
            row.Card = new CardRow(pal, name, Subtitle(row), row.IconButton, row.Toggle);
            row.Card.Gripper = true;
            if (devices.Count == 0) devTop = y;
            AddRow(pad, row.Card);
            devPitch = row.Card.Height + pal.P(4);
            devices.Add(row);

            // Перетаскивание карточки меняет порядок кнопок на панели.
            row.Card.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                dragRow = row; dragStartY = Cursor.Position.Y; dragging = false;
                dragOffset = content.PointToClient(Cursor.Position).Y - row.Card.Top;
            };
            row.Card.MouseMove += delegate { DragMove(); };
            row.Card.MouseUp += delegate { DragEnd(); };
        }

        int devTop, devPitch, dragStartY, dragOffset;
        DeviceRow dragRow;
        bool dragging;

        void DragMove()
        {
            if (dragRow == null) return;
            if (!dragging)
            {
                if (Math.Abs(Cursor.Position.Y - dragStartY) < pal.P(4) || devices.Count < 2) return;
                dragging = true;
                dragRow.Card.BringToFront();
                dragRow.Card.Cursor = Cursors.SizeAll;
            }
            int scroll = content.AutoScrollPosition.Y;
            int top = content.PointToClient(Cursor.Position).Y - dragOffset;
            int minTop = devTop + scroll, maxTop = devTop + scroll + (devices.Count - 1) * devPitch;
            top = Math.Max(minTop, Math.Min(maxTop, top));
            dragRow.Card.Top = top;

            int target = (int)Math.Round((top - minTop) / (double)devPitch);
            int cur = devices.IndexOf(dragRow);
            if (target != cur)
            {
                devices.RemoveAt(cur);
                devices.Insert(target, dragRow);
                LayoutDevices();
            }
        }

        void LayoutDevices()
        {
            int scroll = content.AutoScrollPosition.Y;
            for (int i = 0; i < devices.Count; i++)
                if (devices[i] != dragRow || !dragging) devices[i].Card.Top = devTop + scroll + i * devPitch;
        }

        void DragEnd()
        {
            if (dragRow == null) return;
            bool was = dragging;
            dragRow.Card.Cursor = Cursors.Default;
            dragging = false;
            dragRow = null;
            LayoutDevices();
            if (was) Apply();
        }

        static string Subtitle(DeviceRow r)
        {
            return (r.Connected ? "" : "Не подключено · ") + "Иконка: " + IconName(r.Glyph);
        }

        void PickIcon(DeviceRow row)
        {
            var items = new List<FluentMenu.Item>();
            for (int i = 0; i < Theme.IconChoices.GetLength(0); i++)
            {
                string glyph = Theme.IconChoices[i, 0];
                var item = new FluentMenu.Item { Text = Theme.IconChoices[i, 1], Glyph = glyph, Selected = row.Glyph == glyph };
                item.OnPick = delegate
                {
                    row.Glyph = glyph;
                    row.IconButton.Glyph = glyph;
                    row.IconButton.Invalidate();
                    row.Card.Sub = Subtitle(row);
                    row.Card.Invalidate();
                    if (!row.Toggle.Checked) row.Toggle.Checked = true; // выбрал иконку — значит, хочет видеть устройство
                    else Apply();
                };
                items.Add(item);
            }
            new FluentMenu(pal, items).ShowFor(row.IconButton);
        }

        void PickStyle(FluentButton btn)
        {
            var items = new List<FluentMenu.Item>();
            for (int i = 0; i < TaskbarWidget.StyleNames.Length; i++)
            {
                int idx = i;
                var item = new FluentMenu.Item { Text = TaskbarWidget.StyleNames[i], Selected = app.Cfg.Style == i };
                item.OnPick = delegate
                {
                    app.Cfg.Style = idx;
                    btn.SetText(TaskbarWidget.StyleNames[idx]);
                    UpdateSlideEnabled();
                    app.ConfigChanged();
                };
                items.Add(item);
            }
            new FluentMenu(pal, items).ShowFor(btn);
        }

        void UpdateSlideEnabled()
        {
            bool en = TaskbarWidget.CanSlide(app.Cfg.Style);
            slideRow.Enabled = en;
            slideRow.Invalidate();
        }

        void Apply()
        {
            var list = new List<DeviceEntry>();
            foreach (var r in devices)
                if (r.Toggle.Checked) list.Add(new DeviceEntry { Id = r.Id, Glyph = r.Glyph });
            app.Cfg.Devices = list;
            app.ConfigChanged();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyFrame();
        }

        // Системный заголовок Windows 11 в цвет темы приложений.
        // Дескриптор окна появляется ещё в конструкторе (до палитры), поэтому вызываем и оттуда.
        void ApplyFrame()
        {
            bool dark = pal != null ? pal.Dark : Theme.AppsDark;
            try
            {
                int v = dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref v, 4);
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027); // SWP_FRAMECHANGED | NOMOVE | NOSIZE | NOZORDER
            }
            catch { }
        }

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, int flags);

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
    }
}
