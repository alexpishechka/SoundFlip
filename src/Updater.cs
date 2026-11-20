using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SoundFlip
{
    // Проверка обновлений по кнопке в настройках. Источник — последний релиз на GitHub.
    static class Updater
    {
        public const string Repo = "alexpishechka/SoundFlip";
        public static string ReleasesPage { get { return "https://github.com/" + Repo + "/releases"; } }

        public class Result
        {
            public bool Ok;            // проверка прошла
            public bool Available;     // есть что ставить
            public string Version;     // версия в релизе (без «v»)
            public string Error;       // текст ошибки для пользователя
            internal string AssetApiUrl, AssetUrl;
        }

        public static Version Current { get { return typeof(Updater).Assembly.GetName().Version; } }

        public static string CurrentText
        {
            get { var v = Current; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        // Выполняет work в фоне и возвращает результат в поток интерфейса через owner.
        public static void Run<T>(Control owner, Func<T> work, Action<T> done)
        {
            var t = new Thread(() =>
            {
                T r = work();
                try { if (!owner.IsDisposed) owner.BeginInvoke(done, r); } catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        public static Result Check()
        {
            var res = new Result();
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                string api = "https://api.github.com/repos/" + Repo + "/releases/latest";
                string json = null;
                HttpStatusCode code;
                json = Get(api, null, out code);
                if (json == null) { res.Error = code == 0 ? "Нет подключения к интернету" : "GitHub ответил ошибкой " + (int)code; return res; }

                var rel = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
                string tag = rel.ContainsKey("tag_name") ? rel["tag_name"] as string : null;
                if (tag == null) { res.Error = "Релиз без номера версии"; return res; }
                res.Version = tag.TrimStart('v', 'V');
                string digest = null;
                if (rel.ContainsKey("assets") && rel["assets"] is IEnumerable)
                    foreach (var o in (IEnumerable)rel["assets"])
                    {
                        var a = o as Dictionary<string, object>;
                        if (a == null || !string.Equals(a["name"] as string, "SoundFlip.exe", StringComparison.OrdinalIgnoreCase)) continue;
                        res.AssetApiUrl = a["url"] as string;
                        res.AssetUrl = a["browser_download_url"] as string;
                        if (a.ContainsKey("digest")) digest = a["digest"] as string;
                    }
                if (res.AssetApiUrl == null) { res.Error = "В релизе нет файла SoundFlip.exe"; return res; }

                Version remote;
                try { remote = new Version(res.Version); } catch { res.Error = "Непонятный номер версии: " + tag; return res; }
                var cur = Current;
                int cmp = new Version(remote.Major, remote.Minor, Math.Max(0, remote.Build)).CompareTo(new Version(cur.Major, cur.Minor, cur.Build));
                // Та же версия, но файл в релизе заменили — тоже считаем обновлением (сравниваем SHA-256).
                bool rebuilt = cmp == 0 && digest != null && digest.StartsWith("sha256:") &&
                               !string.Equals(digest.Substring(7), OwnSha256(), StringComparison.OrdinalIgnoreCase);
                res.Available = cmp > 0 || rebuilt;
                res.Ok = true;
            }
            catch (Exception ex) { res.Error = ex.Message; }
            return res;
        }

        // Скачивает новый SoundFlip.exe во временную папку и запускает его — он сам заменит установленную копию и перезапустит программу.
        public static string DownloadAndRun(Result r)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                string dir = Path.Combine(Path.GetTempPath(), "SoundFlip-update");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "SoundFlip.exe");
                var req = (HttpWebRequest)WebRequest.Create(r.AssetUrl);
                req.UserAgent = "SoundFlip";
                req.Accept = "application/octet-stream";
                req.AllowAutoRedirect = true;
                using (var resp = req.GetResponse())
                using (var src = resp.GetResponseStream())
                using (var dst = File.Create(file))
                    src.CopyTo(dst);
                if (new FileInfo(file).Length < 50000) return "Файл обновления повреждён";
                Process.Start(file);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        static string Get(string url, string token, out HttpStatusCode code)
        {
            code = 0;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "SoundFlip";
            req.Accept = "application/vnd.github+json";
            req.Timeout = 15000;
            if (token != null) req.Headers["Authorization"] = "Bearer " + token;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                { code = resp.StatusCode; return sr.ReadToEnd(); }
            }
            catch (WebException ex)
            {
                var hr = ex.Response as HttpWebResponse;
                if (hr != null) code = hr.StatusCode;
                return null;
            }
        }

        static string OwnSha256()
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(Application.ExecutablePath))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }
    }
}
