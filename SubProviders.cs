using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayerPro.SubCore;

namespace MediaPlayerPro
{
    /// <summary>Ένα αρχείο υποτίτλων όπως ήρθε από τον πάροχο: τα bytes και η επέκταση (.srt/.vtt/.ass/.ssa).</summary>
    public class SubBlob { public byte[] Data; public string Ext = ".srt"; }

    public class Cand
    {
        public string Prov, Lang, Name; public double Score; public bool Hash; public Func<Task<SubBlob>> Get;
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

        /// <summary>Τίτλος για αναζήτηση χωρίς σεζόν/επεισόδιο/έτος (αυτά πάνε ως ξεχωριστά φίλτρα) — π.χ. «The Mandalorian».</summary>
        static string TitleQuery(string video, ReleaseInfo vi)
        {
            var t = Regex.Replace(vi.Title ?? "", @"\s+", " ").Trim();
            return t.Length >= 2 ? t : CleanQuery(video, out _);
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

        // ---------- HTTP με επανάληψη: 429/5xx και timeouts ξαναδοκιμάζονται (1η, 2η φορά) με αναμονή· οι υπόλοιπα σφάλματα δεν ξαναδοκιμάζονται ----------
        static async Task<HttpResponseMessage> SendRetry(Func<HttpRequestMessage> make, int attempts = 3)
        {
            for (int i = 1; ; i++)
            {
                try
                {
                    var r = await http.SendAsync(make());
                    bool transient = r.StatusCode == (HttpStatusCode)429 || (int)r.StatusCode >= 500;
                    if (!transient || i >= attempts) return r;
                    var wait = r.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(i);
                    r.Dispose(); await Task.Delay(wait > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : wait);
                }
                catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && i < attempts) { await Task.Delay(TimeSpan.FromSeconds(i)); }
            }
        }

        static int JInt(JsonElement d, string name)
        {
            if (!d.TryGetProperty(name, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var m) ? m : 0;
        }

        // ---------- Πάροχος 1: Podnapisi (ανώνυμο API) ----------
        async Task<List<Cand>> SearchPod(string video, string[] langs)
        {
            var res = new List<Cand>(); var vi = ReleaseInfo.Parse(System.IO.Path.GetFileNameWithoutExtension(video)); string kwTitle = TitleQuery(video, vi), kwPlain = CleanQuery(video, out _);
            foreach (var lang in langs)
            {
                // πρώτα με φίλτρο σεζόν/επεισοδίου/έτους, και αν δεν βρεθεί τίποτα ξανά χωρίς φίλτρα
                var filters = new List<string> { "" };
                string f = (vi.Season != null && vi.Episode != null ? "&seasons=" + vi.Season + "&episodes=" + vi.Episode : "") + (vi.Year != null && vi.Season == null ? "&year=" + vi.Year : "");
                if (f.Length > 0) filters.Insert(0, f);
                foreach (var filter in filters)
                {
                    var r = await SendRetry(() =>
                    {
                        var rq = new HttpRequestMessage(HttpMethod.Get, "https://www.podnapisi.net/subtitles/search/advanced?keywords=" + Uri.EscapeDataString(filter.Length > 0 ? kwTitle : kwPlain) + "&language=" + lang + filter);
                        rq.Headers.Add("Accept", "application/json"); return rq;
                    });
                    if (!r.IsSuccessStatusCode) continue;
                    var part = new List<Cand>();
                    using (var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync()))
                    {
                        if (!doc.RootElement.TryGetProperty("data", out var data)) continue;
                        foreach (var d in data.EnumerateArray())
                        {
                            if (!d.TryGetProperty("id", out var idp)) continue;
                            string id = idp.ToString(), raw = d.GetRawText().ToLower();
                            double dl = d.TryGetProperty("stats", out var st) && st.TryGetProperty("downloads", out var dv) && dv.ValueKind == JsonValueKind.Number ? Math.Min(dv.GetDouble(), 1e6) / 1e6 : 0;
                            var rels = d.TryGetProperty("releases", out var rl) && rl.ValueKind == JsonValueKind.Array ? rl.EnumerateArray().Select(x => x.ToString()).ToList() : new List<string>();
                            string title = d.TryGetProperty("title", out var tt) ? tt.ToString() : id, nm = rels.Count > 0 ? rels[0] : title;
                            // η καλύτερη από τις εκδόσεις (releases) του υπότιτλου όσον αφορά ομάδα/πηγή/ανάλυση
                            double best = double.NegativeInfinity;
                            foreach (var rel in rels.Count > 0 ? rels : new List<string> { title })
                                best = Math.Max(best, SubRank.Score(vi, title + " " + rel, raw.Contains("hearing"), dl, JInt(d, "season") is int se && se > 0 ? se : (int?)null, JInt(d, "episode") is int ep && ep > 0 ? ep : (int?)null, JInt(d, "year") is int yr && yr > 0 ? yr : (int?)null));
                            part.Add(new Cand { Prov = "Podnapisi", Lang = lang, Name = nm, Score = best, Get = () => PodGet(id) });
                        }
                    }
                    if (part.Count > 0) { res.AddRange(part.OrderByDescending(c => c.Score).Take(8)); break; }
                }
            }
            return res;
        }

        static async Task<SubBlob> PodGet(string id)
        {
            var r = await SendRetry(() => new HttpRequestMessage(HttpMethod.Get, "https://www.podnapisi.net/subtitles/" + id + "/download?container=zip"));
            r.EnsureSuccessStatusCode();
            using (var z = new ZipArchive(new MemoryStream(await r.Content.ReadAsByteArrayAsync())))
                return BestEntry(z);
        }

        /// <summary>Από ένα zip διαλέγει το καλύτερο αρχείο υποτίτλων: προτιμά .srt, μετά .vtt/.ass/.ssa, και το μεγαλύτερο (πλήρες) από ίδια επέκταση.</summary>
        static SubBlob BestEntry(ZipArchive z)
        {
            var en = z.Entries.Where(e => e.Length > 0 && SubFormats.Extensions.Contains(System.IO.Path.GetExtension(e.Name).ToLowerInvariant()))
                              .OrderBy(e => Array.IndexOf(SubFormats.Extensions, System.IO.Path.GetExtension(e.Name).ToLowerInvariant())).ThenByDescending(e => e.Length).FirstOrDefault();
            if (en == null) return null;
            using (var s = en.Open()) { var ms = new MemoryStream(); s.CopyTo(ms); return new SubBlob { Data = ms.ToArray(), Ext = System.IO.Path.GetExtension(en.Name).ToLowerInvariant() }; }
        }

        // ---------- Πάροχος 2: OpenSubtitles.com (επίσημο API, χρειάζεται API key + λογαριασμό) ----------
        async Task<JsonDocument> OsApi(HttpMethod m, string url, SubCfg cfg, object body = null, string token = null)
        {
            var r = await SendRetry(() =>
            {
                var rq = new HttpRequestMessage(m, "https://api.opensubtitles.com/api/v1/" + url);
                rq.Headers.Add("Api-Key", cfg.OsApiKey); rq.Headers.Add("Accept", "application/json");
                if (token != null) rq.Headers.Add("Authorization", "Bearer " + token);
                if (body != null) rq.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                return rq;
            });
            var s = await r.Content.ReadAsStringAsync();
            if (!r.IsSuccessStatusCode) throw new Exception("OpenSubtitles " + (int)r.StatusCode + " " + s);
            return JsonDocument.Parse(s);
        }

        async Task<List<Cand>> SearchOs(string video, SubCfg cfg, string[] langs)
        {
            var res = new List<Cand>(); if (cfg.OsApiKey.Length == 0) return res;
            string hash = await Task.Run(() => MovieHash(video)); var vi = ReleaseInfo.Parse(System.IO.Path.GetFileNameWithoutExtension(video)); string kw = TitleQuery(video, vi);
            string q = "languages=" + string.Join(",", langs) + (hash != null ? "&moviehash=" + hash : "") + "&order_by=download_count&query=" + Uri.EscapeDataString(kw)
                     + (vi.Season != null && vi.Episode != null ? "&season_number=" + vi.Season + "&episode_number=" + vi.Episode : vi.Year != null ? "&year=" + vi.Year : "");
            using (var doc = await OsApi(HttpMethod.Get, "subtitles?" + q, cfg))
                foreach (var d in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    var a = d.GetProperty("attributes"); var files = a.GetProperty("files"); if (files.GetArrayLength() == 0) continue;
                    long fid = files[0].GetProperty("file_id").GetInt64();
                    bool hm = a.TryGetProperty("moviehash_match", out var mm) && mm.ValueKind == JsonValueKind.True;
                    bool hi = a.TryGetProperty("hearing_impaired", out var hv) && hv.ValueKind == JsonValueKind.True;
                    string l = a.GetProperty("language").GetString(), rel = a.TryGetProperty("release", out var rr) && rr.ValueKind == JsonValueKind.String ? rr.GetString() : "";
                    double dc = a.TryGetProperty("download_count", out var dv) && dv.ValueKind == JsonValueKind.Number ? Math.Min(dv.GetDouble(), 1e6) / 1e5 : 0;
                    int? se = null, ep = null, yr = null;
                    if (a.TryGetProperty("feature_details", out var fd) && fd.ValueKind == JsonValueKind.Object)
                    {
                        if (JInt(fd, "season_number") > 0) se = JInt(fd, "season_number"); if (JInt(fd, "episode_number") > 0) ep = JInt(fd, "episode_number"); if (JInt(fd, "year") > 0) yr = JInt(fd, "year");
                        if (rel.Length == 0 && fd.TryGetProperty("title", out var ft)) rel = ft.ToString();
                    }
                    double score = SubRank.Score(vi, rel, hi, dc, se, ep, yr) + (hm ? 100 : 0);
                    res.Add(new Cand { Prov = "OpenSubtitles", Lang = l, Name = rel, Hash = hm, Score = score, Get = () => OsGet(cfg, fid) });
                }
            return res;
        }

        async Task<SubBlob> OsGet(SubCfg cfg, long fid)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (osToken == null) { using (var lg = await OsApi(HttpMethod.Post, "login", cfg, new { username = cfg.OsUsername, password = cfg.OsPassword })) osToken = lg.RootElement.GetProperty("token").GetString(); }
                    string link; using (var dl = await OsApi(HttpMethod.Post, "download", cfg, new { file_id = fid, sub_format = "srt" }, osToken)) link = dl.RootElement.GetProperty("link").GetString();
                    var r = await SendRetry(() => new HttpRequestMessage(HttpMethod.Get, link)); r.EnsureSuccessStatusCode();
                    return new SubBlob { Data = await r.Content.ReadAsByteArrayAsync(), Ext = ".srt" };
                }
                catch (Exception ex) when (attempt == 0 && ex.Message.Contains(" 401")) { osToken = null; }     // έληξε το token: νέο login και μία ακόμη προσπάθεια
            }
        }

        // ---------- Συνδυασμός πηγών ----------
        Task<List<Cand>> Safe(Func<Task<List<Cand>>> f) => Task.Run(async () => { try { return await f(); } catch (Exception ex) { subErr = ex.Message; return new List<Cand>(); } });
        static int LangRank(string l, string[] langs) { int i = Array.IndexOf(langs, l); return i < 0 ? 99 : i; }

        /// <summary>Κατεβάζει έναν υποψήφιο και τον μετατρέπει σε γραμμές. null αν δεν κατέβηκε ή δεν περιέχει υπότιτλους.</summary>
        async Task<(SubBlob Blob, List<Cue> Cues)?> Fetch(Cand c)
        {
            try
            {
                var blob = await c.Get(); if (blob?.Data == null) return null;
                string srt = SubFormats.ToSrt(SubFormats.Decode(blob.Data, CodePage(c.Lang)), blob.Ext);
                return srt == null ? ((SubBlob, List<Cue>)?)null : (blob, Srt.Parse(srt));
            }
            catch (Exception ex) { subErr = ex.Message; return null; }
        }

        async Task DownloadSubs(string video, string mode = "auto")
        {
            var cfg = LoadCfg(); var langs = cfg.Languages.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            Say("Αναζήτηση υποτίτλων…"); subErr = null;
            var jobs = new List<Task<List<Cand>>>();
            if (mode != "os") jobs.Add(Safe(() => SearchPod(video, langs)));
            if (mode != "pod") jobs.Add(Safe(() => SearchOs(video, cfg, langs)));
            var all = (await Task.WhenAll(jobs)).SelectMany(x => x).ToList();
            if (CurPath() != video) return;
            if (all.Count == 0)
            {
                Say(subErr != null ? "Σφάλμα υποτίτλων: " + subErr : mode == "os" && cfg.OsApiKey.Length == 0 ? "OpenSubtitles: συμπλήρωσε τα στοιχεία στις Ρυθμίσεις υποτίτλων" : "Δεν βρέθηκαν υπότιτλοι");
                return;
            }
            all = all.OrderByDescending(c => c.Hash).ThenBy(c => LangRank(c.Lang, langs)).ThenByDescending(c => c.Score).ToList();
            try
            {
                Cand pick; (SubBlob Blob, List<Cue> Cues)? got = null; double plaus = -1;
                if (mode == "pick") pick = PickCand(all);
                else if (all[0].Hash) pick = all[0];
                else
                {
                    // Κατεβάζουμε τους 3 καλύτερους της ΙΔΙΑΣ γλώσσας και κρατάμε αυτόν που ταιριάζει καλύτερα στη διάρκεια του βίντεο (μικρά αρχεία, παράλληλα)
                    Say("Σύγκριση υποτίτλων…");
                    var top = all.Where(c => c.Lang == all[0].Lang).Take(3).ToList();
                    var fetched = await Task.WhenAll(top.Select(Fetch));
                    double vlen = mp.Length > 0 ? mp.Length / 1000.0 : 0; pick = null; double bestTotal = double.NegativeInfinity;
                    for (int i = 0; i < top.Count; i++)
                    {
                        if (fetched[i] == null) continue;
                        double p = vlen > 0 ? SubRank.Plausibility(fetched[i].Value.Cues, vlen) : 0.5;
                        double total = top[i].Score + 25 * p;
                        if (total > bestTotal) { bestTotal = total; pick = top[i]; got = fetched[i]; plaus = p; }
                    }
                    if (pick == null) { Say(subErr != null ? "Σφάλμα υποτίτλων: " + subErr : "Ο υπότιτλος δεν περιείχε υποστηριζόμενο αρχείο"); return; }
                }
                if (pick == null) return;
                if (got == null) { Say("Λήψη από " + pick.Prov + "…"); got = await Fetch(pick); if (got == null) { Say(subErr != null ? "Σφάλμα υποτίτλων: " + subErr : "Ο υπότιτλος δεν περιείχε υποστηριζόμενο αρχείο"); return; } }
                if (CurPath() != video) return;
                var target = SaveSub(video, pick.Lang, got.Value.Blob);
                AttachSub(target);
                bool doubtful = plaus >= 0 && plaus < 0.5;
                Say($"Υπότιτλοι ({pick.Lang}) από {pick.Prov}" + (pick.Hash ? " — ακριβής αντιστοίχιση" : doubtful ? " — ΠΡΟΣΟΧΗ: η διάρκεια δεν φαίνεται να ταιριάζει" : ""));
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
        /// <summary>Αποθηκεύει ως .srt (UTF-8 με BOM). Μετατρέπει .vtt/.ass/.ssa. Αν δεν γράφεται δίπλα στο βίντεο (π.χ. μόνο-ανάγνωσης) πάει στον φάκελο subs της εφαρμογής.</summary>
        string SaveSub(string video, string lang, SubBlob blob)
        {
            string text = SubFormats.ToSrt(SubFormats.Decode(blob.Data, CodePage(lang)), blob.Ext);
            if (text == null) throw new InvalidDataException("Δεν βρέθηκαν γραμμές υποτίτλων στο αρχείο");
            string target = System.IO.Path.ChangeExtension(video, null) + "." + lang + ".srt";
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
                SubBlob blob;
                if (System.IO.Path.GetExtension(path).ToLower() == ".zip")
                {
                    using (var z = ZipFile.OpenRead(path))
                    {
                        blob = BestEntry(z);
                        if (blob == null) { Say("Το zip δεν περιέχει υπότιτλους (.srt, .vtt, .ass — τα .rar δεν υποστηρίζονται)"); return; }
                    }
                }
                else blob = new SubBlob { Data = File.ReadAllBytes(path), Ext = System.IO.Path.GetExtension(path).ToLowerInvariant() };
                var cfg = LoadCfg(); var target = SaveSub(v, cfg.Languages.Split(',')[0].Trim(), blob); AttachSub(target);
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
                var ext = System.IO.Path.GetExtension(e.FullPath).ToLower(); if (ext != ".zip" && ext != ".srt" && ext != ".vtt" && ext != ".ass" && ext != ".ssa") return;
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
