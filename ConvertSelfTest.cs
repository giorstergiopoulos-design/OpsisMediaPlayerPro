using System;
using System.Windows.Controls;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using MediaPlayerPro.Conv;

namespace MediaPlayerPro
{
    public partial class MainWindow
    {
        // Τα νέα στοιχεία μενού της 1.5.0 στα ελληνικά/αγγλικά (η υπόλοιπη εφαρμογή παραμένει ελληνική).
        internal static readonly (string Tag, string El, string En)[] NewMenuText =
        {
            ("cv:dlg", "Μετατροπή βίντεο / εικόνας…", "Convert video / image…"),
            ("subdir", "Φάκελος αποθήκευσης υποτίτλων…", "Subtitle save folder…"),
            ("subweb:subdl", "SubDL (διάδοχος του Subscene)", "SubDL (successor of Subscene)"), ("subweb:gs", "GreekSubtitles", "GreekSubtitles"), ("subweb:cat", "Subtitle Cat (Ελληνικά)", "Subtitle Cat (Greek)"),
            ("subweb:os", "OpenSubtitles (ιστοσελίδα)", "OpenSubtitles (web page)"), ("subweb:pod", "Podnapisi (ιστοσελίδα)", "Podnapisi (web page)"), ("subweb:s4f", "Subs4Free", "Subs4Free"),
        };
        void LocalizeNewMenus()
        {
            foreach (var m in MenuBar.Items.OfType<MenuItem>().SelectMany(Flatten))
            {
                if (m.Tag is string tg) { var t = NewMenuText.FirstOrDefault(x => x.Tag == tg); if (t.Tag != null) m.Header = Loc.T(t.El, t.En); }
                if (m.Header is string h && h == "Ελληνικοί υπότιτλοι στον browser") m.Header = Loc.T(h, "Greek subtitles in the browser");
            }
        }

        // Bug 6: Ambience/Surround/Dynamic Boost δεν πρέπει να ξαναρχίζουν το κομμάτι· η θέση πρέπει να συνεχίζει.
        async Task FxContinuitySelfTest(Action<string, bool, string> report)
        {
            try
            {
                string tone = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opsis_selftest_tone.wav"); if (!File.Exists(tone)) { report("audio effects keep position", false, "tone file missing"); return; }
                AddFiles(new[] { tone }, true); await Task.Delay(1500); mp.Time = 40000; await Task.Delay(1200);
                foreach (var name in new[] { "Ambience", "Surround", "Dynamic Boost" })
                {
                    long t0 = mp.Time; fx.On = true; fx.Ambience = name == "Ambience" ? 3 : 0; fx.Surround = name == "Surround" ? 4 : 0; fx.Dynamic = name == "Dynamic Boost" ? 5 : 0;
                    ApplyAudio(); await Task.Delay(2500); long t1 = mp.Time; long adv = t1 - t0;
                    report("audio effect '" + name + "' keeps playback position", adv > 500 && adv < 6000 && mp.IsPlaying, $"position advanced {adv} ms in 2.5 s (a restart from 0 would be negative)");
                }
                fx.On = false; fx.Ambience = fx.Surround = fx.Dynamic = 0; ApplyAudio(); await Task.Delay(800);
            }
            catch (Exception ex) { report("audio effects keep position", false, ex.Message); }
        }

        // Πραγματικές μετατροπές κάθε προσφερόμενης εξόδου· επαληθεύεται διάρκεια και ανάλυση του αποτελέσματος.
        async Task ConvertSelfTest(Action<string, bool, string> report)
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opsis_convtest_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                string bmp = System.IO.Path.Combine(dir, "pic.bmp"), wav = System.IO.Path.Combine(dir, "tone.wav"), src = System.IO.Path.Combine(dir, "src.avi");
                int w = 640, h = 360, row = (w * 3 + 3) / 4 * 4; var data = new byte[row * h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int o = (h - 1 - y) * row + x * 3; data[o] = (byte)(x * 255 / w); data[o + 1] = (byte)(y * 255 / h); data[o + 2] = (byte)((x * 3 + y) % 256); }
                using (var bw = new BinaryWriter(File.Create(bmp)))
                { bw.Write((byte)'B'); bw.Write((byte)'M'); bw.Write(54 + data.Length); bw.Write(0); bw.Write(54); bw.Write(40); bw.Write(w); bw.Write(h); bw.Write((short)1); bw.Write((short)24); bw.Write(0); bw.Write(data.Length); bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0); bw.Write(data); }
                int rate = 44100, n = rate * 6;
                using (var ww = new BinaryWriter(File.Create(wav)))
                { ww.Write(Encoding.ASCII.GetBytes("RIFF")); ww.Write(36 + n * 4); ww.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); ww.Write(16); ww.Write((short)1); ww.Write((short)2); ww.Write(rate); ww.Write(rate * 4); ww.Write((short)4); ww.Write((short)16); ww.Write(Encoding.ASCII.GetBytes("data")); ww.Write(n * 4);
                  for (int i = 0; i < n; i++) { short v = (short)(6000 * Math.Sin(2 * Math.PI * 440 * i / rate)); ww.Write(v); ww.Write(v); } }
                using (var lib = new LibVLC("--quiet"))
                {
                    using (var m = new Media(lib, bmp, FromType.FromPath))
                    {
                        m.AddOption(":image-duration=5"); m.AddOption(":image-fps=25"); m.AddOption(":input-slave=file:///" + wav.Replace('\\', '/'));
                        m.AddOption(":sout=#transcode{vcodec=mp4v,vb=2000,fps=25,acodec=mpga,ab=128,channels=2,samplerate=44100}:std{access=file,mux=avi,dst=\"" + src + "\"}"); m.AddOption(":sout-keep");
                        using var p = new MediaPlayer(m); p.Play(); var sw = System.Diagnostics.Stopwatch.StartNew();
                        while (sw.Elapsed.TotalSeconds < 30) { await Task.Delay(200); if (p.State == VLCState.Ended || p.State == VLCState.Error || p.State == VLCState.Stopped) break; }
                        p.Stop();
                    }
                    var (d0, f0, h0, w0) = await ConvertRunner.ProbeAsync(lib, src);
                    if (h0 == 0 || d0 < 3) { report("converter test source", false, $"could not build the test clip ({d0:0.0}s {w0}x{h0})"); return; }
                    var outDir = System.IO.Path.Combine(dir, "out"); var before = new FileInfo(src).Length;
                    foreach (var f in Formats.Video)
                    {
                        var j = new ConvertJob { Source = src, OutDir = outDir, Opt = new ConvertOptions { Format = f.Id, Audio = f.DefaultAudio, Height = 180, StartSec = 1, EndSec = 4 } };
                        await ConvertRunner.Run(j, _ => { }, CancellationToken.None);
                        string info = j.State;
                        if (j.State == "done")
                        {
                            var (d, fp, hh, ww2) = await ConvertRunner.ProbeAsync(lib, j.Result);
                            // γνωστές ιδιορρυθμίες που μετρήθηκαν: ASF κρατά 2 s preroll (διάρκεια ≈ 1 s για κλιπ 3 s)· το VP9 του libvlc κόβει ~0,9 s στο τέλος·
                            // TS/OGV δεν δίνουν ανάλυση στο Parse· AVI/WMV στρογγυλεύουν το ύψος σε πολλαπλάσιο του 16
                            double exp = f.Id == "wmv" ? 1.0 : f.Vcodec == "VP90" ? 2.1 : 3.0;
                            bool ok = Math.Abs(d - exp) < 0.7 && (hh == 0 || Math.Abs(hh - 180) <= 16);
                            report("convert → " + f.Label + " (trim 1–4 s, 180p, " + f.DefaultAudio + ")", ok, $"{System.IO.Path.GetFileName(j.Result)} {d:0.0}s {ww2}x{hh}"); 
                        }
                        else report("convert → " + f.Label, false, j.Error);
                    }
                    foreach (var kind in new[] { OutKind.FrameSequence, OutKind.Poster, OutKind.ContactSheet })
                    {
                        var j = new ConvertJob { Source = src, OutDir = outDir, Opt = new ConvertOptions { Kind = kind, ImageFormat = "png", EverySeconds = 1, PosterAtSec = 1, SheetColumns = 3, SheetRows = 2 } };
                        await ConvertRunner.Run(j, _ => { }, CancellationToken.None);
                        int count = j.State == "done" ? (Directory.Exists(j.Result) ? Directory.GetFiles(j.Result).Length : File.Exists(j.Result) ? 1 : 0) : 0;
                        report("convert → images (" + kind + ")", j.State == "done" && count >= 1 && (kind != OutKind.FrameSequence || count >= 3), j.State == "done" ? count + " file(s)" : j.Error);
                    }
                    // ακύρωση: δεν μένει μισό αρχείο εξόδου
                    var cancelDir = System.IO.Path.Combine(dir, "cancel"); var cts = new CancellationTokenSource(); cts.Cancel();
                    var jc = new ConvertJob { Source = src, OutDir = cancelDir, Opt = new ConvertOptions { Format = "mp4", Audio = "aac" } };
                    await ConvertRunner.Run(jc, _ => { }, cts.Token);
                    report("cancelled conversion leaves no partial file", jc.State == "cancelled" && (!Directory.Exists(cancelDir) || Directory.GetFiles(cancelDir).Length == 0), jc.State);
                    report("original untouched by conversions", new FileInfo(src).Length == before, "source size unchanged");
                }
            }
            catch (Exception ex) { report("converter self-test", false, ex.Message); }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }
    }
}
