using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace MediaPlayerPro
{
    public class Cand
    {
        public string Prov, Lang, Name; public double Score; public bool Hash; public Func<Task<byte[]>> Get;
        public override string ToString() => $"[{Prov}] {Lang}   {Name}   ★{Score:0.0}" + (Hash ? "   ✔ ακριβής αντιστοίχιση" : "");
    }

    public partial class MainWindow
    {
        string osToken, subErr; FileSystemWatcher dlw;

        static string CleanQuery(string video, out List<string> toks)
        {
            string name = Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(video), @"[._\[\]\(\)]+", " ");
            toks = Regex.Split(name.ToLower(), @"\W+").Where(t => t.Length > 1).Distinct().ToList();
            return Regex.Replace(name, @"\b(480p|576p|720p|1080p|2160p|4k|bluray|brrip|bdrip|web-?dl|webrip|hdtv|dvdrip|x26[45]|h26[45]|hevc)\b.*$", "", RegexOptions.IgnoreCase).Trim();
        }

        static string MovieHash(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                long len = fs.Length; if (len < 131072) return null;
                ulong h = (ulong)len; var buf = new byte[65536];
                fs.ReadExactly(buf, 0, buf.Length); for (int i = 0; i < buf.Length; i += 8) h += BitConverter.ToUInt64(buf, i);
                fs.Seek(len - 65536, SeekOrigin.Begin);
                fs.ReadExactly(buf, 0, buf.Length); for (int i = 0; i < buf.Length; i += 8) h += BitConverter.ToUInt64(buf, i);
                return h.ToString("x16");
            }
        }

        // ---------- Πάροχος 1: Podnapisi (ανώνυμο API) ----------
        async Task<List<Cand>> SearchPod(string video, string[] langs)
        {
            var res = new List<Cand>(); string kw = CleanQuery(video, out var toks);
            foreach (var lang in langs)
            {
                var rq = new HttpRequestMessage(HttpMethod.Get, "https://www.podnapisi.net/subtitles/search/advanced?keywords=" + Uri.EscapeDataString(kw) + "&language=" + lang);
                rq.Headers.Add("Accept", "application/json");
                var r = await http.SendAsync(rq); if (!r.IsSuccessStatusCode) continue;
                using (var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync()))
                {
                    if (!doc.RootElement.TryGetProperty("data", out var data)) continue;
                    var part = new List<Cand>();
                    foreach (var d in data.EnumerateArray())
                    {
                        if (!d.TryGetProperty("id", out var idp)) continue;
                        string id = idp.ToString(), raw = d.GetRawText().ToLower(), lg = lang;
                        double dl = d.TryGetProperty("stats", out var st) && st.TryGetProperty("downloads", out var dv) && dv.ValueKind == JsonValueKind.Number ? Math.Min(dv.GetDouble(), 1e6) / 1e6 : 0;
                        string nm = d.TryGetProperty("releases", out var rl) && rl.ValueKind == JsonValueKind.Array && rl.GetArrayLength() > 0 ? rl[0].ToString() : (d.TryGetProperty("title", out var tt) ? tt.ToString() : id);
                        part.Add(new Cand { Prov = "Podnapisi", Lang = lg, Name = nm, Score = toks.Count(t => raw.Contains(t)) + dl, Get = () => PodGet(id) });
                    }
                    res.AddRange(part.OrderByDescending(c => c.Score).Take(8));
                }
            }
            return res;
        }

        static async Task<byte[]> PodGet(string id)
        {
            var zip = await http.GetByteArrayAsync("https://www.podnapisi.net/subtitles/" + id + "/download?container=zip");
            using (var z = new ZipArchive(new MemoryStream(zip)))
            {
                var en = z.Entries.FirstOrDefault(e => e.Name.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)); if (en == null) return null;
                using (var s = en.Open()) { var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); }
            }
        }

        // ---------- Πάροχος 2: OpenSubtitles.com (επίσημο API, χρειάζεται API key + λογαριασμό) ----------
        async Task<JsonDocument> OsApi(HttpMethod m, string url, SubCfg cfg, object body = null, string token = null)
        {
            var rq = new HttpRequestMessage(m, "https://api.opensubtitles.com/api/v1/" + url);
            rq.Headers.Add("Api-Key", cfg.OsApiKey); rq.Headers.Add("Accept", "application/json");
            if (token != null) rq.Headers.Add("Authorization", "Bearer " + token);
            if (body != null) rq.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var r = await http.SendAsync(rq); var s = await r.Content.ReadAsStringAsync();
            if (!r.IsSuccessStatusCode) throw new Exception("OpenSubtitles " + (int)r.StatusCode + " " + s);
            return JsonDocument.Parse(s);
        }

        async Task<List<Cand>> SearchOs(string video, SubCfg cfg, string[] langs)
        {
            var res = new List<Cand>(); if (cfg.OsApiKey.Length == 0) return res;
            string hash = await Task.Run(() => MovieHash(video)); string kw = CleanQuery(video, out _);
            string q = "languages=" + string.Join(",", langs) + (hash != null ? "&moviehash=" + hash : "") + "&order_by=download_count&query=" + Uri.EscapeDataString(kw);
            using (var doc = await OsApi(HttpMethod.Get, "subtitles?" + q, cfg))
                foreach (var d in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    var a = d.GetProperty("attributes"); var files = a.GetProperty("files"); if (files.GetArrayLength() == 0) continue;
                    long fid = files[0].GetProperty("file_id").GetInt64();
                    bool hm = a.TryGetProperty("moviehash_match", out var mm) && mm.ValueKind == JsonValueKind.True;
                    string l = a.GetProperty("language").GetString(), rel = a.TryGetProperty("release", out var rr) && rr.ValueKind == JsonValueKind.String ? rr.GetString() : "";
                    double dc = a.TryGetProperty("download_count", out var dv) && dv.ValueKind == JsonValueKind.Number ? Math.Min(dv.GetDouble(), 1e6) / 1e5 : 0;
                    res.Add(new Cand { Prov = "OpenSubtitles", Lang = l, Name = rel, Hash = hm, Score = dc + (hm ? 10 : 0), Get = () => OsGet(cfg, fid) });
                }
            return res;
        }

        async Task<byte[]> OsGet(SubCfg cfg, long fid)
        {
            if (osToken == null) { using (var lg = await OsApi(HttpMethod.Post, "login", cfg, new { username = cfg.OsUsername, password = cfg.OsPassword })) osToken = lg.RootElement.GetProperty("token").GetString(); }
            string link; using (var dl = await OsApi(HttpMethod.Post, "download", cfg, new { file_id = fid, sub_format = "srt" }, osToken)) link = dl.RootElement.GetProperty("link").GetString();
            return await http.GetByteArrayAsync(link);
        }

        // ---------- Συνδυασμός πηγών ----------
        Task<List<Cand>> Safe(Func<Task<List<Cand>>> f) => Task.Run(async () => { try { return await f(); } catch (Exception ex) { subErr = ex.Message; return new List<Cand>(); } });
        static int LangRank(string l, string[] langs) { int i = Array.IndexOf(langs, l); return i < 0 ? 99 : i; }

        async Task DownloadSubs(string video, string mode = "auto")
        {
            var cfg = LoadCfg(); var langs = cfg.Languages.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            Say("Αναζήτηση υποτίτλων…"); subErr = null;
            var jobs = new List<Task<List<Cand>>>();
            if (mode != "os") jobs.Add(Safe(() => SearchPod(video, langs)));
            if (mode != "pod") jobs.Add(Safe(() => SearchOs(video, cfg, langs)));
            var all = (await Task.WhenAll(jobs)).SelectMany(x => x).ToList();
            if (all.Count == 0)
            {
                Say(subErr != null ? "Σφάλμα υποτίτλων: " + subErr : mode == "os" && cfg.OsApiKey.Length == 0 ? "OpenSubtitles: συμπλήρωσε τα στοιχεία στις Ρυθμίσεις υποτίτλων" : "Δεν βρέθηκαν υπότιτλοι");
                return;
            }
            all = all.OrderByDescending(c => c.Hash).ThenBy(c => LangRank(c.Lang, langs)).ThenByDescending(c => c.Score).ToList();
            var pick = mode == "pick" ? PickCand(all) : all[0]; if (pick == null) return;
            try
            {
                Say("Λήψη από " + pick.Prov + "…");
                var bytes = await pick.Get(); if (bytes == null) { Say("Ο υπότιτλος δεν περιείχε .srt"); return; }
                if (CurPath() != video) return;
                var target = SaveSub(video, pick.Lang, bytes); AttachSub(target);
                Say($"Υπότιτλοι ({pick.Lang}) από {pick.Prov}" + (pick.Hash ? " — ακριβής αντιστοίχιση" : ""));
                if (cfg.AutoSync && !pick.Hash) await RunSync(video, target);
            }
            catch (Exception ex) { Say("Σφάλμα υποτίτλων: " + ex.Message); }
        }

        Cand PickCand(List<Cand> l)
        {
            var w = new Window { Title = "Επιλογή υποτίτλων", Width = 760, Height = 420, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var lb = new ListBox { ItemsSource = l, Margin = new Thickness(10), SelectedIndex = 0 }; Cand pick = null;
            var ok = new Button { Content = "Λήψη", IsDefault = true, Width = 90, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += (s, e) => { pick = lb.SelectedItem as Cand; w.DialogResult = true; };
            lb.MouseDoubleClick += (s, e) => { if (lb.SelectedItem != null) ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            var g = new DockPanel(); DockPanel.SetDock(ok, Dock.Bottom); g.Children.Add(ok); g.Children.Add(lb); w.Content = g;
            w.ShowDialog(); return pick;
        }

        // ---------- Αποθήκευση / φόρτωση ----------
        static string DecodeSub(byte[] b, string lang)
        {
            try { return new UTF8Encoding(false, true).GetString(b).TrimStart('\uFEFF'); }
            catch { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(CodePage(lang)).GetString(b); }
        }

        string SaveSub(string video, string lang, byte[] b)
        {
            string text = DecodeSub(b, lang), target = System.IO.Path.ChangeExtension(video, null) + "." + lang + ".srt";
            try { File.WriteAllText(target, text, new UTF8Encoding(true)); }
            catch { var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(CfgFile), "subs"); Directory.CreateDirectory(dir); target = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(target)); File.WriteAllText(target, text, new UTF8Encoding(true)); }
            return target;
        }

        void AttachSub(string t) { lastSub = t; mp.AddSlave(MediaSlaveType.Subtitle, new Uri(t).AbsoluteUri, true); }

        void ImportSubZip(string p) => ImportSubFile(p);

        void ImportSubFile(string path)
        {
            var v = CurPath(); if (v == null) { Say("Άνοιξε πρώτα ένα βίντεο"); return; }
            try
            {
                byte[] b;
                if (System.IO.Path.GetExtension(path).ToLower() == ".zip")
                {
                    using (var z = ZipFile.OpenRead(path))
                    {
                        var en = z.Entries.FirstOrDefault(x => x.Name.EndsWith(".srt", StringComparison.OrdinalIgnoreCase));
                        if (en == null) { Say("Το zip δεν περιέχει .srt (τα .rar δεν υποστηρίζονται)"); return; }
                        using (var s = en.Open()) { var ms = new MemoryStream(); s.CopyTo(ms); b = ms.ToArray(); }
                    }
                }
                else b = File.ReadAllBytes(path);
                var cfg = LoadCfg(); var target = SaveSub(v, cfg.Languages.Split(',')[0].Trim(), b); AttachSub(target);
                Say("Υπότιτλοι φορτώθηκαν: " + System.IO.Path.GetFileName(path));
                if (cfg.AutoSync) _ = RunSync(v, target);
            }
            catch (Exception ex) { Say("Σφάλμα: " + ex.Message); }
        }

        // ---------- Subs4Free: ανοίγει στον browser σου (το site έχει προστασία anti-bot) ----------
        void OpenSubs4Free(string video)
        {
            string kw = CleanQuery(video, out _);
            Process.Start(new ProcessStartInfo("https://www.subs4free.info/search_report.php?search=" + Uri.EscapeDataString(kw) + "&searchType=1") { UseShellExecute = true });
            WatchDownloads(video);
            Say("Subs4Free: κατέβασε τον υπότιτλο (.zip ή .srt) — θα φορτωθεί αυτόματα");
        }

        void WatchDownloads(string video)
        {
            dlw?.Dispose();
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); if (!Directory.Exists(dir)) return;
            var w = dlw = new FileSystemWatcher(dir) { EnableRaisingEvents = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            FileSystemEventHandler h = async (s, e) =>
            {
                var ext = System.IO.Path.GetExtension(e.FullPath).ToLower(); if (ext != ".zip" && ext != ".srt") return;
                for (int i = 0; i < 20; i++) { await Task.Delay(500); try { using (File.Open(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.None)) { } break; } catch { } }
                await Dispatcher.InvokeAsync(() => { if (CurPath() == video && dlw == w) { ImportSubFile(e.FullPath); w.Dispose(); dlw = null; } });
            };
            w.Created += h; w.Renamed += (s, e) => h(s, e);
            var t = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            t.Tick += (s, e) => { t.Stop(); if (dlw == w) { w.Dispose(); dlw = null; } };
            t.Start();
        }
    }
}
