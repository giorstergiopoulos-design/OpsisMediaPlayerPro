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
using LibVLCSharp.Shared;

namespace MediaPlayerPro
{
    public class SubCfg
    {
        public string Languages { get; set; } = "el,en";
        public bool AutoDownload { get; set; } = true;
        public bool AutoSync { get; set; } = true;
        public string OsApiKey { get; set; } = "";      // προαιρετικό: OpenSubtitles.com API key (opensubtitles.com/consumers)
        public string OsUsername { get; set; } = "";
        public string OsPassword { get; set; } = "";
        public bool OnlineMeta { get; set; } = true;       // αναζήτηση πληροφοριών ταινίας/σειράς στο διαδίκτυο
        public int SubFontSize { get; set; } = 16;          // μέγεθος γραμματοσειράς υποτίτλων (σχετικό, ισχύει μετά από επανεκκίνηση)
        public int SubColor { get; set; } = 16777215;       // χρώμα υποτίτλων σε δεκαδικό RGB (16777215 = λευκό, 16776960 = κίτρινο)
        public int NetworkCachingMs { get; set; } = 1000;   // buffer για δικτυακά streams
    }

    public partial class MainWindow
    {
        static readonly HttpClient http = new HttpClient();
        static MainWindow() { http.DefaultRequestHeaders.UserAgent.ParseAdd("MediaPlayerPro/1.0"); }
        static readonly string CfgFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaPlayerPro", "settings.json");
        string lastSub;

        bool SubCmd(string c)
        {
            switch (c)
            {
                case "subdl": case "subdl:pod": case "subdl:os": case "subdl:pick": case "subdl:s4f":
                    { var v = CurPath(); if (v != null) { if (c == "subdl:s4f") OpenSubs4Free(v); else _ = DownloadSubs(v, c == "subdl" ? "auto" : c.Substring(6)); } return true; }
                case "subsync": _ = SyncCurrent(); return true;
                case "subline": SyncToLine(); return true;
                case "subcfg": LoadCfg(); Process.Start(new ProcessStartInfo("notepad.exe", "\"" + CfgFile + "\"") { UseShellExecute = true }); return true;
            }
            return ExtraCmd(c) || MoreCmd(c);
        }

        string CurPath() => idx >= 0 && idx < items.Count && !items[idx].Path.Contains("://") ? items[idx].Path : null;

        SubCfg LoadCfg()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CfgFile));
                if (!File.Exists(CfgFile)) File.WriteAllText(CfgFile, JsonSerializer.Serialize(new SubCfg(), new JsonSerializerOptions { WriteIndented = true }));
                var txt = File.ReadAllText(CfgFile); var c = JsonSerializer.Deserialize<SubCfg>(txt) ?? new SubCfg();
                if (!txt.Contains("OsApiKey")) File.WriteAllText(CfgFile, JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true }));
                return c;
            }
            catch { return new SubCfg(); }
        }

        void AutoSub(string path) { if (LoadCfg().AutoDownload) _ = DownloadSubs(path); }

        static string FindSrt(string video) =>
            video == null ? null : new[] { ".srt", ".el.srt", ".en.srt", ".synced.srt" }.Select(x => System.IO.Path.ChangeExtension(video, null) + x).FirstOrDefault(File.Exists);

        static int CodePage(string l) => l == "el" ? 1253 : "ru bg sr uk".Contains(l) ? 1251 : l == "tr" ? 1254 : l == "he" ? 1255 : l == "ar" ? 1256 :
                                         "pl cs sk hu ro hr sl".Contains(l) ? 1250 : 1252;

        // ---------- Ενσωματωμένος αυτόματος συγχρονισμός με τον ήχο ----------
        async Task SyncCurrent()
        {
            var v = CurPath(); var s = lastSub ?? FindSrt(v);
            if (v == null || s == null) { Say("Δεν υπάρχει αρχείο υποτίτλων για συγχρονισμό"); return; }
            await RunSync(v, s);
        }

        async Task<bool> ExtractAudio(string video, string wav)
        {
            using (var l2 = new LibVLC("--no-video", "--quiet"))
            using (var m = new Media(l2, video, FromType.FromPath))
            {
                m.AddOption(":sout=#transcode{acodec=s16l,channels=1,samplerate=8000}:std{access=file,mux=wav,dst=\"" + wav + "\"}");
                m.AddOption(":no-sout-all"); m.AddOption(":sout-keep"); m.AddOption(":stop-time=1800");
                using (var p = new MediaPlayer(m))
                {
                    p.Play(); var sw = Stopwatch.StartNew();
                    while (sw.Elapsed.TotalMinutes < 20)
                    {
                        await Task.Delay(500); var st = p.State;
                        if (st == VLCState.Ended || st == VLCState.Stopped || st == VLCState.Error) break;
                        Say("Ανάλυση ήχου… " + Math.Min(99, p.Time / 18000) + "%");
                    }
                    p.Stop();
                }
            }
            return File.Exists(wav) && new FileInfo(wav).Length > 100000;
        }

        static bool[] Envelope(string wav)
        {
            var b = File.ReadAllBytes(wav); int o0 = 44;
            for (int i = 12; i < Math.Min(b.Length - 4, 512); i++) if (b[i] == 'd' && b[i + 1] == 'a' && b[i + 2] == 't' && b[i + 3] == 'a') { o0 = i + 8; break; }
            int n = (b.Length - o0) / 1600; if (n < 100) return null;
            var db = new double[n];
            for (int f = 0; f < n; f++) { double s = 0; int o = o0 + f * 1600; for (int k = 0; k < 800; k++) { short v = BitConverter.ToInt16(b, o + k * 2); s += (double)v * v; } db[f] = 10 * Math.Log10(s / 800 + 1); }
            var sorted = (double[])db.Clone(); Array.Sort(sorted);
            double lo = sorted[(int)(n * .2)], hi = sorted[(int)(n * .9)], thr = lo + .45 * (hi - lo);
            return db.Select(x => x > thr).ToArray();
        }

        static readonly Regex CueRx = new Regex(@"(\d+):(\d+):(\d+)[,.](\d+)\s*-->\s*(\d+):(\d+):(\d+)[,.](\d+)");
        static double Sec(Match m, int g) => int.Parse(m.Groups[g].Value) * 3600 + int.Parse(m.Groups[g + 1].Value) * 60 + int.Parse(m.Groups[g + 2].Value) + int.Parse(m.Groups[g + 3].Value.PadRight(3, '0').Substring(0, 3)) / 1000.0;
        static List<(double s, double e)> Cues(string text) => CueRx.Matches(text).Cast<Match>().Select(m => (Sec(m, 1), Sec(m, 5))).ToList();

        // Διασταυρωτική συσχέτιση "ομιλία ήχου" ↔ "γραμμές υποτίτλων" για μετατόπιση και απόκλιση fps
        static (int shift, double ratio, double score) Align(bool[] sp, List<(double s, double e)> cues)
        {
            int N = sp.Length, R = 2400; var A = sp.Select(x => (sbyte)(x ? 1 : -1)).ToArray();
            (int, double, double) best = (0, 1, double.MinValue);
            foreach (var r in new[] { 1.0, 25 / 23.976, 23.976 / 25, 24 / 23.976, 23.976 / 24, 25.0 / 24, 24.0 / 25 })
            {
                var S = new sbyte[N + 2 * R]; Array.Fill(S, (sbyte)-1);
                foreach (var c in cues) { int a = Math.Max(0, (int)(c.s * r * 10) + R), b = Math.Min(S.Length - 1, (int)(c.e * r * 10) + R); for (int k = a; k <= b; k++) S[k] = 1; }
                for (int sh = -R; sh <= R; sh++)
                {
                    int off = R - sh, sum = 0;
                    for (int i = 0; i < N; i++) sum += A[i] * S[i + off];
                    if (sum > best.Item3) best = (sh, r, sum);
                }
            }
            return (best.Item1, best.Item2, best.Item3 / N);
        }

        static string Ts(double t) { var ts = TimeSpan.FromSeconds(Math.Max(0, t)); return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}"; }

        async Task RunSync(string video, string sub)
        {
            string wav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mpp_" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                Say("Συγχρονισμός υποτίτλων με τον ήχο…");
                if (!await ExtractAudio(video, wav)) { Say("Αποτυχία εξαγωγής ήχου — δοκίμασε το Y"); return; }
                string text = File.ReadAllText(sub); var cues = Cues(text);
                if (cues.Count == 0) { Say("Δεν διαβάστηκαν γραμμές υποτίτλων"); return; }
                var res = await Task.Run(() => { var sp = Envelope(wav); return sp == null ? ((int, double, double)?)null : Align(sp, cues); });
                if (res == null || res.Value.Item3 < 0.04) { Say("Χαμηλή βεβαιότητα συγχρονισμού — δεν εφαρμόστηκε (δοκίμασε το Y)"); return; }
                var (shift, ratio, _) = res.Value;
                string outp = System.IO.Path.ChangeExtension(sub, null) + ".synced.srt";
                string fixedText = Regex.Replace(text, @"^.*-->.*$", ln => Regex.Replace(ln.Value, @"(\d+):(\d+):(\d+)[,.](\d+)", m =>
                    Ts(Sec(Regex.Match(m.Value + " -->  0:0:0,0", @"(\d+):(\d+):(\d+)[,.](\d+)\s*-->\s*(\d+):(\d+):(\d+)[,.](\d+)"), 1) * ratio + shift / 10.0)), RegexOptions.Multiline);
                File.WriteAllText(outp, fixedText, new UTF8Encoding(true));
                lastSub = outp; mp.AddSlave(MediaSlaveType.Subtitle, new Uri(outp).AbsoluteUri, true);
                Say($"Συγχρονισμός: {shift / 10.0:+0.0;-0.0}s, ταχύτητα ×{ratio:0.0000}");
            }
            catch (Exception ex) { Say("Σφάλμα συγχρονισμού: " + ex.Message); }
            finally { try { File.Delete(wav); } catch { } }
        }

        // Χειροκίνητος γρήγορος συγχρονισμός (Y)
        void SyncToLine()
        {
            var s = lastSub ?? FindSrt(CurPath());
            if (s == null || mp.Length <= 0) { Say("Δεν υπάρχει αρχείο .srt για συγχρονισμό"); return; }
            var cues = Cues(File.ReadAllText(s)).Select(c => c.s).ToList();
            if (cues.Count == 0) return;
            double heard = mp.Time / 1000.0, shown = heard - mp.SpuDelay / 1e6, nearest = cues.OrderBy(x => Math.Abs(x - shown)).First();
            mp.SetSpuDelay((long)((heard - nearest) * 1e6));
            Say("Συγχρονισμός στη γραμμή: " + (long)((heard - nearest) * 1000) + "ms");
        }
    }
}
