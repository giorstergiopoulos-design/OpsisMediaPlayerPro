using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace MediaPlayerPro
{
    public class MetaData
    {
        public string Kind { get; set; } public string Title { get; set; } public string Sub { get; set; }
        public string Extra { get; set; } public string Summary { get; set; } public string Poster { get; set; }
    }
    class Guess { public string Title; public int? Year, Season, Episode; }

    public partial class MainWindow
    {
        string metaKey, sideMode = "auto"; bool sideOpen = true;
        readonly DispatcherTimer sideT = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void SaveCfg(SubCfg c) => File.WriteAllText(CfgFile, JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true }));

        // ---------- Ανάλυση ονόματος αρχείου (σαν guessit) ----------
        static Guess ParseName(string path)
        {
            string n = Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(path), @"[._]+", " "); var g = new Guess(); int cut = n.Length;
            var ep = Regex.Match(n, @"\bS(\d{1,2})\s?E(\d{1,3})\b|\b(\d{1,2})x(\d{2,3})\b", RegexOptions.IgnoreCase);
            if (ep.Success) { g.Season = int.Parse(ep.Groups[1].Success ? ep.Groups[1].Value : ep.Groups[3].Value); g.Episode = int.Parse(ep.Groups[2].Success ? ep.Groups[2].Value : ep.Groups[4].Value); cut = Math.Min(cut, ep.Index); }
            var ym = Regex.Matches(n, @"(?<!\d)(19\d{2}|20\d{2})(?!\d)").Cast<Match>().FirstOrDefault(x => x.Index > 2);
            if (ym != null) { g.Year = int.Parse(ym.Value); cut = Math.Min(cut, ym.Index); }
            var q = Regex.Match(n, @"\b(480p|576p|720p|1080p|2160p|4k|bluray|brrip|bdrip|web-?dl|webrip|hdtv|dvdrip|x26[45]|h26[45]|hevc)\b", RegexOptions.IgnoreCase);
            if (q.Success) cut = Math.Min(cut, q.Index);
            g.Title = Regex.Replace(n.Substring(0, cut), @"\s+", " ").Trim(' ', '-', '(', '[');
            return g;
        }

        static string S(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static string Strip(string h) => string.IsNullOrEmpty(h) ? "" : WebUtility.HtmlDecode(Regex.Replace(h, "<.*?>", "")).Trim();
        static string Norm(string s) => Regex.Replace((s ?? "").ToLower(), @"[^\p{L}\p{N}]+", " ").Trim();
        static double Sim(string a, string b) { var x = Norm(a); var y = Norm(b); if (x == y) return 1; var ta = x.Split(' ').ToHashSet(); var tb = y.Split(' ').ToHashSet(); return (double)ta.Intersect(tb).Count() / Math.Max(1, Math.Max(ta.Count, tb.Count)); }
        static async Task<JsonDocument> GetJson(string url) { var r = await http.GetAsync(url); return r.IsSuccessStatusCode ? JsonDocument.Parse(await r.Content.ReadAsStringAsync()) : null; }
        static string Join(params string[] a) => string.Join(" • ", a.Where(x => !string.IsNullOrWhiteSpace(x)));

        // ---------- TVMaze (σειρές / επεισόδια, χωρίς key) ----------
        static async Task<MetaData> TvMaze(Guess g)
        {
            using (var sd = await GetJson("https://api.tvmaze.com/singlesearch/shows?q=" + Uri.EscapeDataString(g.Title)))
            {
                if (sd == null) return null; var s = sd.RootElement; int id = s.GetProperty("id").GetInt32();
                string genres = s.TryGetProperty("genres", out var gn) && gn.ValueKind == JsonValueKind.Array ? string.Join(", ", gn.EnumerateArray().Select(x => x.GetString())) : "";
                bool doc = S(s, "type") == "Documentary" || genres.Contains("Documentary");
                double rate = s.TryGetProperty("rating", out var rt) && rt.ValueKind == JsonValueKind.Object && rt.TryGetProperty("average", out var av) && av.ValueKind == JsonValueKind.Number ? av.GetDouble() : 0;
                string net = s.TryGetProperty("network", out var nw) ? S(nw, "name") : null;
                var md = new MetaData
                {
                    Title = S(s, "name"), Summary = Strip(S(s, "summary")), Kind = doc ? "Ντοκιμαντέρ" : "Σειρά",
                    Poster = s.TryGetProperty("image", out var im) && im.ValueKind == JsonValueKind.Object ? S(im, "original") ?? S(im, "medium") : null,
                    Extra = Join((S(s, "premiered") ?? "").Split('-')[0], rate > 0 ? "★ " + rate.ToString("0.0", CultureInfo.InvariantCulture) : null, net, genres)
                };
                if (g.Season != null && g.Episode != null)
                {
                    md.Kind = doc ? "Ντοκιμαντέρ — επεισόδιο" : "Επεισόδιο σειράς"; md.Sub = $"S{g.Season:00}E{g.Episode:00}";
                    using (var ed = await GetJson($"https://api.tvmaze.com/shows/{id}/episodebynumber?season={g.Season}&number={g.Episode}"))
                        if (ed != null) { var e = ed.RootElement; md.Sub += " • " + S(e, "name"); var es = Strip(S(e, "summary")); if (es != "") md.Summary = es; if (S(e, "airdate") != null) md.Extra = Join(S(e, "airdate"), md.Extra); }
                }
                return md;
            }
        }

        // ---------- Apple iTunes Search (ταινίες, χωρίς key) ----------
        static async Task<MetaData> ITunes(Guess g)
        {
            using (var d = await GetJson("https://itunes.apple.com/search?media=movie&entity=movie&limit=8&term=" + Uri.EscapeDataString(g.Title)))
            {
                if (d == null || !d.RootElement.TryGetProperty("results", out var rs)) return null;
                JsonElement best = default; double bs = 0;
                foreach (var r in rs.EnumerateArray())
                {
                    double sc = Sim(S(r, "trackName"), g.Title); string rd = S(r, "releaseDate") ?? "";
                    if (g.Year != null && rd.Length >= 4 && int.TryParse(rd.Substring(0, 4), out var y)) sc += Math.Abs(y - g.Year.Value) <= 1 ? 0.3 : -0.3;
                    if (sc > bs) { bs = sc; best = r; }
                }
                if (bs < 0.6) return null;
                string genre = S(best, "primaryGenreName"), art = S(best, "artworkUrl100");
                long ms = best.TryGetProperty("trackTimeMillis", out var tm) && tm.ValueKind == JsonValueKind.Number ? tm.GetInt64() : 0;
                return new MetaData
                {
                    Kind = genre == "Documentary" ? "Ντοκιμαντέρ" : "Ταινία", Title = S(best, "trackName"), Summary = S(best, "longDescription") ?? S(best, "shortDescription"),
                    Poster = art?.Replace("100x100bb", "600x600bb"), Sub = S(best, "artistName"),
                    Extra = Join((S(best, "releaseDate") ?? "").Split('-')[0], ms > 0 ? (ms / 60000) + " λεπτά" : null, genre, S(best, "contentAdvisoryRating"))
                };
            }
        }

        // ---------- Wikipedia (εφεδρική πηγή: ντοκιμαντέρ, σπάνιες ταινίες) ----------
        static async Task<MetaData> Wiki(Guess g)
        {
            using (var s = await GetJson("https://en.wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=1&srsearch=" + Uri.EscapeDataString(g.Title + (g.Year != null ? " " + g.Year : "") + " film")))
            {
                if (s == null) return null; var arr = s.RootElement.GetProperty("query").GetProperty("search"); if (arr.GetArrayLength() == 0) return null;
                string title = arr[0].GetProperty("title").GetString(); if (Sim(title, g.Title) < 0.5) return null;
                using (var d = await GetJson("https://en.wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(title.Replace(' ', '_'))))
                {
                    if (d == null) return null; var e = d.RootElement; string desc = S(e, "description") ?? "";
                    return new MetaData { Kind = desc.Contains("ocumentary") ? "Ντοκιμαντέρ" : desc.Contains("series") ? "Σειρά" : "Ταινία", Title = S(e, "title"), Extra = desc, Summary = S(e, "extract"), Poster = e.TryGetProperty("thumbnail", out var th) ? S(th, "source") : null };
                }
            }
        }

        async Task<MetaData> GetMeta(Guess g)
        {
            string key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes($"{g.Title.ToLower()}|{g.Year}|{g.Season}|{g.Episode}")));
            string f = System.IO.Path.Combine(AppDir, "meta", key + ".json");
            try { if (File.Exists(f)) return JsonSerializer.Deserialize<MetaData>(File.ReadAllText(f)); } catch { }
            MetaData md = null;
            if (g.Season != null) md = await TvMaze(g);
            if (md == null) md = await ITunes(g);
            if (md == null) md = await Wiki(g);
            if (md != null) try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f)); File.WriteAllText(f, JsonSerializer.Serialize(md)); } catch { }
            return md;
        }

        // ---------- Εμφάνιση στο πλευρικό πάνελ ----------
        async void LoadMeta(string path)
        {
            metaKey = path; MetaCard.Visibility = Visibility.Visible; Poster.Source = null; Poster.Visibility = Visibility.Collapsed;
            var g = ParseName(path);
            MetaKind.Text = "ΑΡΧΕΙΟ ΠΟΛΥΜΕΣΩΝ"; MetaTitle.Text = string.IsNullOrEmpty(g.Title) ? System.IO.Path.GetFileNameWithoutExtension(path) : g.Title;
            MetaSub.Text = g.Season != null ? $"S{g.Season:00}E{g.Episode:00}" : g.Year?.ToString() ?? ""; MetaExtra.Text = ""; MetaSummary.Text = ""; MetaSummary.ToolTip = null;
            if (!path.Contains("://")) AddTech(path);
            if (path.Contains("://") || !LoadCfg().OnlineMeta || string.IsNullOrEmpty(g.Title)) return;
            MetaSummary.Text = "Αναζήτηση πληροφοριών…";
            try
            {
                var md = await GetMeta(g); if (metaKey != path) return;
                if (md == null) { MetaSummary.Text = ""; return; }
                MetaKind.Text = (md.Kind ?? "").ToUpper(); MetaTitle.Text = md.Title ?? MetaTitle.Text; MetaSub.Text = md.Sub ?? MetaSub.Text;
                MetaExtra.Text = md.Extra ?? ""; MetaSummary.Text = md.Summary ?? ""; MetaSummary.ToolTip = md.Summary;
                if (!string.IsNullOrEmpty(md.Poster))
                {
                    var bi = new BitmapImage(); bi.BeginInit(); bi.UriSource = new Uri(md.Poster); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 420; bi.EndInit();
                    Poster.Source = bi; Poster.Visibility = Visibility.Visible;
                }
                if (sideMode == "auto") { SetSide(true); sideT.Interval = TimeSpan.FromSeconds(6); sideT.Stop(); sideT.Start(); }
            }
            catch { if (metaKey == path) MetaSummary.Text = ""; }
        }

        async void AddTech(string path)
        {
            string b = "";
            try { b = Join((new FileInfo(path).Length / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB", System.IO.Path.GetExtension(path).TrimStart('.').ToUpper()); } catch { }
            MetaTech.Text = b; await Task.Delay(1800);
            if (metaKey != path || mp.Media == null) return;
            var parts = new List<string>();
            try
            {
                foreach (var t in mp.Media.Tracks)
                {
                    string c = Encoding.ASCII.GetString(BitConverter.GetBytes(t.Codec)).Trim('\0');
                    if (t.TrackType == TrackType.Video) parts.Add($"{c} {t.Data.Video.Width}×{t.Data.Video.Height}");
                    else if (t.TrackType == TrackType.Audio) parts.Add($"{c} {t.Data.Audio.Channels}ch");
                }
            }
            catch { }
            if (parts.Count > 0) MetaTech.Text = Join(b, string.Join(" • ", parts));
        }

        // ---------- Αναδιπλούμενο πάνελ (hover) ----------
        void SetSide(bool open, bool animate = true)
        {
            sideOpen = open; double to = open ? 300 : (sideMode == "hide" ? 0 : 12);
            if (animate) SidePanel.BeginAnimation(WidthProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else { SidePanel.BeginAnimation(WidthProperty, null); SidePanel.Width = to; }
        }
        void Side_Enter(object s, System.Windows.Input.MouseEventArgs e) { sideT.Stop(); if (sideMode == "auto" && !sideOpen) SetSide(true); }
        void Side_Leave(object s, System.Windows.Input.MouseEventArgs e) { if (sideMode == "auto") { sideT.Interval = TimeSpan.FromMilliseconds(700); sideT.Stop(); sideT.Start(); } }
        void ToggleSide() { if (sideMode == "hide") { sideMode = "auto"; SaveSide(); ApplySideMode(); SetSide(true); } else SetSide(!sideOpen); }
        void SaveSide() { try { Directory.CreateDirectory(AppDir); File.WriteAllText(System.IO.Path.Combine(AppDir, "side.txt"), sideMode); } catch { } }

        void ApplySideMode()
        {
            SidePanel.Visibility = sideMode == "hide" ? Visibility.Collapsed : Visibility.Visible;
            if (sideMode == "pin") SetSide(true, false); else if (sideMode == "auto") SetSide(sideOpen, false);
            Say("Πλευρικό πάνελ: " + (sideMode == "auto" ? "αυτόματο (hover)" : sideMode == "pin" ? "πάντα ανοιχτό" : "κρυφό"));
        }

        void LoadSide()
        {
            sideT.Tick += (s, e) => { sideT.Stop(); if (sideMode == "auto" && !SidePanel.IsMouseOver) SetSide(false); };
            try { var f = System.IO.Path.Combine(AppDir, "side.txt"); if (File.Exists(f)) sideMode = File.ReadAllText(f).Trim(); } catch { }
            SidePanel.Visibility = sideMode == "hide" ? Visibility.Collapsed : Visibility.Visible;
            if (sideMode == "auto") { SetSide(true, false); sideT.Interval = TimeSpan.FromSeconds(4); sideT.Start(); } else SetSide(sideMode == "pin", false);
        }

        void SideCmd(string a)
        {
            if (a == "meta")
            {
                var c = LoadCfg(); c.OnlineMeta = !c.OnlineMeta; SaveCfg(c);
                Say(c.OnlineMeta ? "Πληροφορίες online: ενεργές" : "Πληροφορίες online: ανενεργές");
                if (c.OnlineMeta && CurPath() != null) LoadMeta(CurPath());
                return;
            }
            sideMode = a; SaveSide(); ApplySideMode();
        }
    }
}
