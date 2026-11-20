using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Reflection;

[assembly: AssemblyTitle("SoundFlip")]
[assembly: AssemblyProduct("SoundFlip")]
[assembly: AssemblyCompany("alexpishechka")]
[assembly: AssemblyDescription("Переключение наушников и колонок на панели задач")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace SoundFlip
{
    static class Program
    {
        public const string AppName = "SoundFlip";
        public static readonly int OpenSettingsMessage = RegisterWindowMessage("SoundFlip.OpenSettings");

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int RegisterWindowMessage(string name);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // SoundFlip.exe --uninstall — тихое полное удаление (закрывает запущенную копию).
            if (Array.IndexOf(args, "--uninstall") >= 0)
            {
                foreach (var p in Process.GetProcessesByName(AppName))
                    if (p.Id != Process.GetCurrentProcess().Id) try { p.Kill(); p.WaitForExit(3000); } catch { }
                Installer.Uninstall();
                return;
            }

            // Запуск не из папки установки (например, только что скачанный exe) — ставим себя и перезапускаемся оттуда.
            if (!Installer.IsInstalledCopy)
            {
                try { Installer.Install(); }
                catch (Exception ex)
                {
                    MessageBox.Show("Не получилось установить SoundFlip:\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            bool created;
            using (var m = new Mutex(true, "SoundFlip_single_instance", out created))
            {
                // Уже запущен — просим открыть настройки и выходим.
                if (!created) { PostMessage((IntPtr)0xFFFF, OpenSettingsMessage, IntPtr.Zero, IntPtr.Zero); return; }
                bool justInstalled = Array.IndexOf(args, "--installed") >= 0;
                Application.Run(new TrayApp(justInstalled));
            }
        }
    }

    class DeviceEntry
    {
        public string Id;
        public string Glyph;
    }

    // Список устройств, которые показываются на панели (в этом порядке), и иконка каждого.
    class Config
    {
        public List<DeviceEntry> Devices = new List<DeviceEntry>();
        public int Style = 0; // см. TaskbarWidget.StyleNames
        public bool SlidePill = true; // плашка в пилюлях переезжает плавно (иначе — мгновенно)

        static string Path { get { return System.IO.Path.Combine(Installer.Dir, "config.txt"); } }
        public static bool Exists { get { return File.Exists(Path); } }

        public int IndexOf(string id)
        {
            for (int i = 0; i < Devices.Count; i++) if (Devices[i].Id == id) return i;
            return -1;
        }

        public static Config Load()
        {
            var c = new Config();
            bool newOrder = false;
            try
            {
                foreach (var line in File.ReadAllLines(Path))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    if (k == "style") { int st; if (int.TryParse(v, out st)) c.Style = st; }
                    else if (k == "order") newOrder = v == "2";
                    else if (k == "slide") c.SlidePill = v != "0";
                    else if (k == "device")
                    {
                        int bar = v.LastIndexOf('|');
                        if (bar <= 0) continue;
                        string glyph = Theme.GlyphVolume;
                        try { glyph = ((char)Convert.ToInt32(v.Substring(bar + 1), 16)).ToString(); } catch { }
                        c.Devices.Add(new DeviceEntry { Id = v.Substring(0, bar), Glyph = glyph });
                    }
                    // формат версии 1.0
                    else if (k == "headphones" && v != "") c.Devices.Add(new DeviceEntry { Id = v, Glyph = Theme.GlyphHeadphones });
                    else if (k == "speakers" && v != "") c.Devices.Add(new DeviceEntry { Id = v, Glyph = Theme.GlyphSpeakers });
                }
            }
            catch { }
            // В старых настройках «Подсветка» и «Капсула» шли в обратном порядке.
            if (!newOrder && (c.Style == 2 || c.Style == 3)) c.Style = 5 - c.Style;
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Installer.Dir);
                var lines = new List<string>();
                lines.Add("order=2");
                lines.Add("style=" + Style);
                lines.Add("slide=" + (SlidePill ? "1" : "0"));
                foreach (var d in Devices) lines.Add("device=" + d.Id + "|" + ((int)d.Glyph[0]).ToString("X4"));
                File.WriteAllLines(Path, lines.ToArray());
            }
            catch { }
        }
    }

    static class Installer
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Program.AppName); } }
        public static string Exe { get { return Path.Combine(Dir, Program.AppName + ".exe"); } }
        static string Shortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.AppName + ".lnk"); } }

        public static bool IsInstalledCopy
        {
            get { return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase); }
        }

        public static void Install()
        {
            // Если уже запущена старая версия — закрываем её, чтобы можно было заменить файл (обновление).
            foreach (var p in Process.GetProcessesByName(Program.AppName))
            {
                try
                {
                    if (p.Id != Process.GetCurrentProcess().Id && string.Equals(p.MainModule.FileName, Exe, StringComparison.OrdinalIgnoreCase))
                    { p.Kill(); p.WaitForExit(3000); }
                }
                catch { }
            }

            Directory.CreateDirectory(Dir);
            File.Copy(Application.ExecutablePath, Exe, true);
            CreateShortcut();
            RefreshIconCache();
            if (!Config.Exists) Autostart = true; // по умолчанию — запуск вместе с Windows
            else if (Autostart) { using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.SetValue(Program.AppName, "\"" + Exe + "\""); } // обновить путь
            Process.Start(Exe, "--installed");
        }

        public static bool Autostart
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    if (k == null || k.GetValue(Program.AppName) == null) return false;
                // Отключено в диспетчере задач («Автозагрузка»): первый байт записи — нечётный (3).
                using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                {
                    var b = k == null ? null : k.GetValue(Program.AppName) as byte[];
                    return b == null || b.Length == 0 || (b[0] & 1) == 0;
                }
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(Program.AppName, "\"" + Exe + "\"");
                    else k.DeleteValue(Program.AppName, false);
                }
                // Сбрасываем отметку «отключено» из диспетчера задач, чтобы не оставалось висячих записей.
                try { using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey, true)) if (k != null) k.DeleteValue(Program.AppName, false); } catch { }
            }
        }

        static void CreateShortcut()
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object lnk = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { Shortcut });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { Exe });
                lt.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { Dir });
                lt.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { "Переключение наушников и колонок" });
                string ico = WriteIconFile();
                if (ico != null) lt.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { ico + ",0" });
                lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { }
        }

        // Иконка отдельным файлом с хешем в имени: Windows кэширует иконки по пути файла,
        // и после обновления на панели задач и в «Пуске» оставалась старая. Новое имя — новая запись в кэше.
        static string WriteIconFile()
        {
            try
            {
                byte[] data;
                using (var st = typeof(Installer).Assembly.GetManifestResourceStream("SoundFlip.app.ico"))
                using (var ms = new MemoryStream()) { st.CopyTo(ms); data = ms.ToArray(); }
                string hash;
                using (var sha = System.Security.Cryptography.SHA1.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").Substring(0, 8).ToLowerInvariant();
                string path = Path.Combine(Dir, "icon-" + hash + ".ico");
                foreach (var old in Directory.GetFiles(Dir, "icon-*.ico"))
                    if (!string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) try { File.Delete(old); } catch { }
                if (!File.Exists(path)) File.WriteAllBytes(path, data);
                return path;
            }
            catch { return null; }
        }

        // Просим оболочку обновить кэш иконок (без перезапуска проводника).
        static void RefreshIconCache()
        {
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "ie4uinit.exe"), "-show");
                psi.CreateNoWindow = true; psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch { }
            try { SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

        // Отметка «включено/отключено» из диспетчера задач без самой записи автозапуска — убираем.
        public static void CleanStartupOrphan()
        {
            try
            {
                using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
                    if (run != null && run.GetValue(Program.AppName) != null) return;
                using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                    if (k != null) k.DeleteValue(Program.AppName, false);
            }
            catch { }
        }

        // Полная очистка следов в реестре текущего пользователя (что пишет сама программа и что Windows запоминает о ней).
        static void CleanRegistry()
        {
            var cu = Registry.CurrentUser;
            string dir = Dir.ToLowerInvariant();
            Func<string, bool> ours = s =>
            {
                if (string.IsNullOrEmpty(s)) return false;
                s = s.ToLowerInvariant();
                return s.Contains(dir) || s.Contains(@"\programs\soundflip") || s.Contains("programssoundflipsoundflip.exe") ||
                       s.EndsWith(@"\soundflip.exe") || s.Contains("soundflip.lnk");
            };

            DeleteValue(cu, RunKey, Program.AppName);
            DeleteValue(cu, ApprovedKey, Program.AppName);
            DeleteValue(cu, @"Software\Microsoft\Windows\CurrentVersion\RunNotification", "StartupTNotiSoundFlip");

            // Значок в трее (запасной режим): настройки области уведомлений и сгенерированный для него AppUserModelID.
            ForEachSubKey(cu, @"Control Panel\NotifyIconSettings", (parent, name) =>
            {
                using (var k = parent.OpenSubKey(name))
                {
                    string exe = k.GetValue("ExecutablePath") as string, tip = k.GetValue("InitialTooltip") as string;
                    if (!ours(exe) && (tip == null || !tip.StartsWith(Program.AppName))) return false;
                }
                try { cu.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\NotifyIconGeneratedAumid_" + name, false); } catch { }
                return true;
            });

            // Статистика запусков и переключений окон, история совместимости.
            ForEachSubKey(cu, @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage", (parent, name) =>
            {
                DeleteValues(parent, name, ours);
                return false;
            });
            DeleteValues(cu, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store", ours);
            DeleteValues(cu, @"Software\Microsoft\Windows\CurrentVersion\UFH\SHC", null, ours);
            ForEachSubKey(cu, @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist", (parent, name) =>
            {
                DeleteValues(parent, name + @"\Count", n => ours(Rot13(n)));
                return false;
            });

            // Плитки и кэш меню «Пуск».
            // Ключи здесь вложены по пути: TileProperties\W~C:\Users\...\Programs\SoundFlip\SoundFlip.exe
            try { cu.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Start\TileProperties\W~" + Dir, false); } catch { }
            foreach (var store in new[] { "Cloud", "Current" })
                ForEachSubKey(cu, @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\" + store, (parent, name) =>
                {
                    if (name.IndexOf("appleveltilelist", StringComparison.OrdinalIgnoreCase) >= 0)
                        ForEachSubKey(parent, name, (p2, n2) => ours(n2));
                    return false;
                });
            ForEachSubKey(cu, @"Software\Microsoft\Windows\CurrentVersion\AppListBackup", (parent, name) =>
            {
                using (var k = parent.OpenSubKey(name))
                    foreach (var v in k.GetValueNames())
                        if (ours(k.GetValue(v) as string)) return true;
                return false;
            });
        }

        static string Rot13(string s)
        {
            var c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++)
            {
                char x = c[i];
                if (x >= 'a' && x <= 'z') c[i] = (char)('a' + (x - 'a' + 13) % 26);
                else if (x >= 'A' && x <= 'Z') c[i] = (char)('A' + (x - 'A' + 13) % 26);
            }
            return new string(c);
        }

        static void DeleteValue(RegistryKey root, string path, string name)
        {
            try { using (var k = root.OpenSubKey(path, true)) if (k != null) k.DeleteValue(name, false); } catch { }
        }

        // Удаляет значения, у которых имя (byName) или данные (byData) указывают на SoundFlip.
        static void DeleteValues(RegistryKey root, string path, Func<string, bool> byName, Func<string, bool> byData = null)
        {
            try
            {
                using (var k = root.OpenSubKey(path, true))
                {
                    if (k == null) return;
                    foreach (var v in k.GetValueNames())
                    {
                        bool hit = byName != null && byName(v);
                        if (!hit && byData != null)
                        {
                            object d = k.GetValue(v);
                            var arr = d as string[];
                            hit = arr != null ? Array.Exists(arr, x => byData(x)) : byData(d as string);
                        }
                        if (hit) try { k.DeleteValue(v, false); } catch { }
                    }
                }
            }
            catch { }
        }

        // Обходит подключи; если функция вернула true — подключ удаляется целиком.
        static void ForEachSubKey(RegistryKey root, string path, Func<RegistryKey, string, bool> f)
        {
            try
            {
                using (var k = root.OpenSubKey(path, true))
                {
                    if (k == null) return;
                    foreach (var name in k.GetSubKeyNames())
                    {
                        bool del = false;
                        try { del = f(k, name); } catch { }
                        if (del) try { k.DeleteSubKeyTree(name, false); } catch { }
                    }
                }
            }
            catch { }
        }

        public static void Uninstall()
        {
            Autostart = false;
            try { File.Delete(Shortcut); } catch { }
            try { Directory.Delete(Path.Combine(Path.GetTempPath(), "SoundFlip-update"), true); } catch { }
            // «Пуск» реагирует на удаление ярлыка не сразу и сам дописывает записи о плитке — чистим после него.
            CleanRegistry();
            Thread.Sleep(2000);
            CleanRegistry();
            // После выхода процесса: удалить папку и последние записи, которые «Пуск» дописывает с задержкой.
            string script =
                "Start-Sleep 2; Remove-Item -LiteralPath '" + Dir.Replace("'", "''") + "' -Recurse -Force -EA 0; " +
                "for ($i = 0; $i -lt 6; $i++) { Start-Sleep 2; " +
                "Get-ChildItem 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\AppListBackup' -EA 0 | " +
                "Where-Object { $k = $_; ($k.GetValueNames() | ForEach-Object { [string]$k.GetValue($_) }) -match 'Programs\\\\SoundFlip\\\\' } | Remove-Item -Recurse -Force -EA 0; " +
                "Remove-Item -LiteralPath 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Start\\TileProperties\\W~" + Dir.Replace("'", "''") + "' -Recurse -Force -EA 0 }";
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand " +
                Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetTempPath();
            Process.Start(psi);
        }
    }
}
