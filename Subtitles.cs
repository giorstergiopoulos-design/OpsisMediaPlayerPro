using System;
using MediaPlayerPro.SubCore;
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
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
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
        // Η λογική (ανίχνευση ομιλίας, συσχέτιση, απόκλιση fps) είναι στο SubCore\SpeechAlign.cs και δοκιμάζεται με συνθετικά δεδομένα.
        async Task SyncCurrent()
        {
            var v = CurPath(); var s = lastSub ?? FindSrt(v);
            if (v == null || s == null) { Say("Δεν υπάρχει αρχείο υποτίτλων για συγχρονισμό"); return; }
            await RunSync(v, s);
        }

        const int PcmRate = 8000;

        /// <summary>Βγάζει ένα τμήμα του ήχου ως 8 kHz μονοφωνικό PCM (μέσω LibVLC, χωρίς ffmpeg). null αν αποτύχει.</summary>
        async Task<short[]> ExtractPcm(string video, double startSec, double lenSec, string label)
        {
            string wav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mpp_" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                using (var l2 = new LibVLC("--no-video", "--quiet"))
                using (var m = new Media(l2, video, FromType.FromPath))
                {
                    m.AddOption(":sout=#transcode{acodec=s16l,channels=1,samplerate=" + PcmRate + "}:std{access=file,mux=wav,dst=\"" + wav + "\"}");
                    m.AddOption(":no-sout-all"); m.AddOption(":sout-keep");
                    if (startSec > 0) m.AddOption(":start-time=" + startSec.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    m.AddOption(":stop-time=" + (startSec + lenSec).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    using (var p = new MediaPlayer(m))
                    {
                        p.Play(); var sw = Stopwatch.StartNew();
                        while (sw.Elapsed.TotalMinutes < 10)
                        {
                            await Task.Delay(400); var st = p.State;
                            if (st == VLCState.Ended || st == VLCState.Stopped || st == VLCState.Error) break;
                            Say($"Ανάλυση ήχου ({label})… " + Math.Min(99, (int)((p.Time / 1000.0 - startSec) / lenSec * 100)) + "%");
                        }
                        p.Stop();
                    }
                }
                return File.Exists(wav) && new FileInfo(wav).Length > PcmRate * 2 * 20 ? ReadWav16(wav) : null;
            }
            finally { try { File.Delete(wav); } catch { } }
        }

        static short[] ReadWav16(string wav)
        {
            var b = File.ReadAllBytes(wav); int o0 = 44;
            for (int i = 12; i < Math.Min(b.Length - 8, 4096); i++) if (b[i] == 'd' && b[i + 1] == 'a' && b[i + 2] == 't' && b[i + 3] == 'a') { o0 = i + 8; break; }
            var pcm = new short[Math.Max(0, (b.Length - o0) / 2)];
            Buffer.BlockCopy(b, o0, pcm, 0, pcm.Length * 2);
            return pcm;
        }

        static string Ts(double t) => Srt.Ts(t);

        /// <summary>
        /// Συγχρονισμός: τμήμα Α (πρώτα 10 λεπτά) για τη μετατόπιση και τον λόγο fps, και — για ταινίες >40′ — τμήμα Β (στο 70 %) για να βρεθεί ΚΑΙ η απόκλιση
        /// ταχύτητας σε όλη την ταινία. Αν η βεβαιότητα είναι χαμηλή δεν αλλάζει τίποτα (μένει ο χειροκίνητος συγχρονισμός Y).
        /// </summary>
        async Task RunSync(string video, string sub)
        {
            try
            {
                Say("Συγχρονισμός υποτίτλων με τον ήχο…");
                var cues = Srt.Parse(SubFormats.Decode(File.ReadAllBytes(sub), 1252));
                if (cues.Count < 5) { Say("Δεν διαβάστηκαν αρκετές γραμμές υποτίτλων"); return; }
                double L = mp.Length > 0 ? mp.Length / 1000.0 : 0, lenA = L > 0 ? Math.Min(600, L) : 600;
                var pcmA = await ExtractPcm(video, 0, lenA, "1/2");
                if (pcmA == null) { Say("Αποτυχία εξαγωγής ήχου — δοκίμασε το Y"); return; }
                var fitA = await Task.Run(() => SpeechAlign.FitFirst(pcmA, PcmRate, 0, cues, 0, 300));
                if (!fitA.Good) { Say($"Χαμηλή βεβαιότητα συγχρονισμού ({fitA.Score:0.00}) — δεν εφαρμόστηκε (δοκίμασε το Y)"); return; }

                SegmentFit fitB = null; double tA = lenA / 2, tB = 0;
                if (L > 2400)
                {
                    double startB = L * 0.7;
                    var pcmB = await ExtractPcm(video, startB, 600, "2/2");
                    if (pcmB != null) { fitB = await Task.Run(() => SpeechAlign.FitSegment(pcmB, PcmRate, startB, cues, fitA.Shift, Math.Max(20, 0.08 * startB), new[] { fitA.Ratio })); tB = startB + 300; }
                }
                var sol = SpeechAlign.Combine(fitA, tA, fitB, tB);
                if (CurPath() != video) return;                                          // ο χρήστης άνοιξε άλλο βίντεο όσο γινόταν η ανάλυση
                string outp = System.IO.Path.ChangeExtension(sub, null) + ".synced.srt";
                if (outp.EndsWith(".synced.synced.srt")) outp = outp.Replace(".synced.synced.srt", ".synced.srt");
                File.WriteAllText(outp, Srt.Write(Srt.Transform(cues, sol.Ratio, sol.Shift)), new UTF8Encoding(true));
                lastSub = outp; mp.AddSlave(MediaSlaveType.Subtitle, new Uri(outp).AbsoluteUri, true);
                Say($"Συγχρονισμός: {sol.Shift:+0.0;-0.0}s" + (Math.Abs(sol.Ratio - 1) > 0.0002 ? $", ταχύτητα ×{sol.Ratio:0.0000}" : "") + $"  (βεβαιότητα {sol.Score:0.00}" + (fitB != null ? ", 2 τμήματα" : "") + ")");
            }
            catch (Exception ex) { Say("Σφάλμα συγχρονισμού: " + ex.Message); }
        }

        // Χειροκίνητος γρήγορος συγχρονισμός (Y)
        void SyncToLine()
        {
            var s = lastSub ?? FindSrt(CurPath());
            if (s == null || mp.Length <= 0) { Say("Δεν υπάρχει αρχείο .srt για συγχρονισμό"); return; }
            var cues = Srt.Parse(SubFormats.Decode(File.ReadAllBytes(s), 1252)).Select(c => c.Start).ToList();
            if (cues.Count == 0) return;
            double heard = mp.Time / 1000.0, shown = heard - mp.SpuDelay / 1e6, nearest = cues.OrderBy(x => Math.Abs(x - shown)).First();
            mp.SetSpuDelay((long)((heard - nearest) * 1e6));
            Say("Συγχρονισμός στη γραμμή: " + (long)((heard - nearest) * 1000) + "ms");
        }
    }
}
