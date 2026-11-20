using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SoundFlip
{
    class TrayApp : ApplicationContext
    {
        public Config Cfg;
        NotifyIcon tray;
        Timer poll;
        TaskbarWidget widget;
        Form menuHost;
        SettingsForm settings;
        int reattachDelay;
        string iconKey;
        Icon icon;

        public TrayApp(bool justInstalled)
        {
            Installer.CleanStartupOrphan();
            bool firstRun = !Config.Exists;
            Cfg = Config.Load();
            if (firstRun) { AutoSetup(); Cfg.Save(); }

            // Запасной вариант, если встроиться в панель задач не получилось: значок в трее.
            tray = new NotifyIcon();
            tray.Text = Program.AppName;
            tray.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) Cycle();
                else if (e.Button == MouseButtons.Right) ShowMenuAtCursor();
            };

            // Основной режим — переключатель прямо в панели задач.
            widget = new TaskbarWidget(this);
            bool embedded = widget.Attach(CurrentSlot());
            tray.Visible = !embedded;
            Refresh();

            messenger = new Messenger(this);

            poll = new Timer();
            poll.Interval = 500;
            poll.Tick += delegate { Refresh(); };
            poll.Start();

            if (justInstalled && !embedded)
                tray.ShowBalloonTip(4000, "SoundFlip работает",
                    "Левый клик — следующее устройство.\nПравый клик — настройки.", ToolTipIcon.None);
        }

        public List<DeviceEntry> Shown { get { return Cfg.Devices; } }

        public string DeviceName(int i)
        {
            if (i < 0 || i >= Cfg.Devices.Count) return "";
            string id = Cfg.Devices[i].Id;
            foreach (var d in Audio.GetOutputs()) if (d.Id == id) return d.Name;
            return "Устройство не подключено";
        }

        // Скрытое окно: ловит сигнал «открой настройки» от повторного запуска программы (например, из «Пуска»).
        class Messenger : NativeWindow
        {
            readonly TrayApp app;
            public Messenger(TrayApp app) { this.app = app; CreateHandle(new CreateParams { Caption = "SoundFlip.Messenger" }); }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Program.OpenSettingsMessage) { app.OpenSettings(); return; }
                base.WndProc(ref m);
            }
        }
        Messenger messenger;

        // Индекс текущего устройства по умолчанию среди показываемых, или -1.
        public int CurrentSlot()
        {
            string id = Audio.GetDefaultId();
            return id == null ? -1 : Cfg.IndexOf(id);
        }

        public void Select(int i)
        {
            if (i < 0 || i >= Cfg.Devices.Count) return;
            try { Audio.SetDefault(Cfg.Devices[i].Id); } catch { }
            Refresh();
        }

        void Cycle()
        {
            if (Cfg.Devices.Count == 0) { OpenSettings(); return; }
            Select((CurrentSlot() + 1) % Cfg.Devices.Count);
        }

        public void Refresh()
        {
            int slot = CurrentSlot();

            if (tray.Visible)
            {
                bool white = Theme.TaskbarDark;
                int size = SystemInformation.SmallIconSize.Width;
                string glyph = slot >= 0 ? Cfg.Devices[slot].Glyph : Theme.GlyphVolume;
                string key = glyph + "|" + white + "|" + size;
                if (key != iconKey)
                {
                    iconKey = key;
                    var old = icon;
                    icon = Theme.MakeTrayIcon(glyph, white, size);
                    tray.Icon = icon;
                    if (old != null) old.Dispose();
                }
            }

            if (widget.IsAlive) { widget.Tick(slot); reattachDelay = 0; }
            else if (reattachDelay-- <= 0)
            {
                // Проводник перезапустился или панель пересоздана — встраиваемся заново.
                bool ok = widget.Attach(slot);
                tray.Visible = !ok;
                iconKey = null;
                reattachDelay = ok ? 0 : 6;
            }
        }

        // Вызывается окном настроек после любого изменения.
        public void ConfigChanged()
        {
            Cfg.Save();
            iconKey = null;
            widget.Rebuild(CurrentSlot());
            Refresh();
        }

        public void ShowMenuAtCursor()
        {
            if (menuHost == null)
            {
                menuHost = new Form();
                menuHost.FormBorderStyle = FormBorderStyle.None;
                menuHost.ShowInTaskbar = false;
                menuHost.StartPosition = FormStartPosition.Manual;
                menuHost.Size = new Size(1, 1);
                menuHost.Opacity = 0;
                menuHost.TopMost = true;
            }
            var menu = new ContextMenu();
            var s = new MenuItem("Настройки…", delegate { OpenSettings(); });
            s.DefaultItem = true;
            menu.MenuItems.Add(s);
            menu.MenuItems.Add("Звуковые параметры Windows", delegate { try { System.Diagnostics.Process.Start("ms-settings:sound"); } catch { } });
            menu.MenuItems.Add("-");
            menu.MenuItems.Add("Выход", delegate { Quit(); });

            menuHost.Location = Cursor.Position;
            menuHost.Show();
            menuHost.Activate();
            Theme.ApplyMenuTheme();
            menu.Show(menuHost, Point.Empty);
            menuHost.Hide();
        }

        public void OpenSettings()
        {
            if (settings == null || settings.IsDisposed) settings = new SettingsForm(this);
            settings.Show();
            if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            settings.Activate();
        }

        // Первый запуск: показываем все подключённые устройства с подходящими иконками.
        void AutoSetup()
        {
            foreach (var d in Audio.GetOutputs())
                Cfg.Devices.Add(new DeviceEntry { Id = d.Id, Glyph = Theme.GuessGlyph(d.Name) });
        }

        public void Quit()
        {
            poll.Stop();
            widget.Detach();
            tray.Visible = false;
            tray.Dispose();
            if (settings != null && !settings.IsDisposed) settings.Close();
            ExitThread();
        }
    }
}
