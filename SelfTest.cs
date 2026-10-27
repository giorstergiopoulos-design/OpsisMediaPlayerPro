using System;
using System.Threading.Tasks;
using MediaPlayerPro.SubCore;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MediaPlayerPro
{
    // `OpsisMediaPlayerPro.exe --selftest <αρχείο ήχου/βίντεο>`: εκτελεί ΚΑΘΕ κουμπί και ΚΑΘΕ στοιχείο μενού (εκτός από όσα ανοίγουν modal παράθυρο ή
    // κάνουν κάτι μη αναστρέψιμο) και γράφει το αποτέλεσμα στο %TEMP%\opsis_mediaplayer_selftest.txt. Κάθε εντολή που ρίχνει exception ή αφήνει τον player
    // σε κατάσταση Error αναφέρεται ως FAIL. Βγαίνει με κωδικό 0 αν όλα πέρασαν.
    public partial class MainWindow
    {
        static readonly HashSet<string> SelfTestSkip = new HashSet<string>
        {
            "subfile", "loadpl", "savepl", "stream", "dvd", "cam", "openurl", "goto", "cv:mp4", "cv:mp3", "cv:clip", "subdl", "subdl:pod", "subdl:os", "subdl:pick", "subdl:s4f",
            "subcfg", "info", "fx", "exit", "after:exit", "after:shutdown", "listfolder", "snapclip", "welcome", "suite", "subsync", "subline", "plclear", "plrem", "stop",
        };

        void StartSelfTest()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += async (s, e) => { t.Stop(); try { await RunSelfTest(); } catch (Exception ex) { File.WriteAllText(SelfTestFile, "SELFTEST CRASHED: " + ex); Environment.Exit(2); } };
            t.Start();
        }

        static readonly string SelfTestFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opsis_mediaplayer_selftest.txt");

        static IEnumerable<DependencyObject> Walk(DependencyObject d)
        {
            if (d == null) yield break;
            yield return d;
            foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
                foreach (var x in Walk(c)) yield return x;
        }

        async Task RunSelfTest()
        {
            var log = new StringBuilder(); int pass = 0, fail = 0, skipped = 0;
            void Report(string name, bool ok, string detail = "") { if (ok) pass++; else fail++; log.AppendLine((ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? "  " + detail : "")); }

            // 1) τα δυναμικά μενού ανοίγουν χωρίς σφάλμα
            try { Menu_Opened(this, null); Report("dynamic menus fill (Menu_Opened)", true, $"audio devices={AudioDevices.Items.Count}, audio tracks={AudioTracks.Items.Count}, video tracks={VideoTracks.Items.Count}, skins={SkinMenu.Items.Count}"); }
            catch (Exception ex) { Report("dynamic menus fill (Menu_Opened)", false, ex.Message); }

            // 2) όλα τα Tag των μενού και των κουμπιών
            var tags = new List<string>();
            foreach (var d in Walk(this).Concat(new DependencyObject[] { Video }.SelectMany(Walk)))
            {
                string tag = (d as MenuItem)?.Tag as string ?? (d as Button)?.Tag as string;
                if (!string.IsNullOrEmpty(tag) && !tags.Contains(tag)) tags.Add(tag);
            }
            foreach (var m in MenuBar.Items.OfType<MenuItem>().SelectMany(Flatten)) { if (m.Tag is string tg && !tags.Contains(tg)) tags.Add(tg); }
            foreach (var tag in tags)
            {
                if (SelfTestSkip.Contains(tag) || tag.StartsWith("help:")) { skipped++; log.AppendLine("SKIP  " + tag + "  (modal dialog / network / irreversible)"); continue; }
                try
                {
                    Do(tag);
                    var st = mp.State;
                    Report("command " + tag, st != LibVLCSharp.Shared.VLCState.Error, "state=" + st);
                    if (tag == "full" || tag == "menu" || tag == "minimal" || tag == "top" || tag == "list") Do(tag);    // ξανά για να γυρίσει
                    if (tag.StartsWith("sleep:")) Do("sleep:0");
                }
                catch (Exception ex) { Report("command " + tag, false, ex.GetType().Name + ": " + ex.Message); }
            }
            try { Do("sleep:0"); Do("vreset"); Do("sp:1"); Do("ar:"); Do("crop:"); Do("zoom:0"); } catch (Exception) { }

            // 3) κουμπιά/μενού που δεν έχουν Tag: κίνηση στο ρολόι, θέμα, σίγαση
            try { var before = mode; Do("modenext"); Do("modenext"); Do("modenext"); Report("mode button cycles dark -> light -> auto -> dark", mode == before, "mode=" + mode + ", icon=" + ModeBtn.Content); }
            catch (Exception ex) { Report("mode button", false, ex.Message); }
            try { int n = AllSkins().Count; for (int i = 0; i < n; i++) NextSkin(); Report("every skin applies (" + n + ")", true); }
            catch (Exception ex) { Report("skins", false, ex.Message); }
            try
            {
                Play(0);                                       // η λίστα έχει τελειώσει (next/prev στο τέλος σταματούν από σχεδίαση): ξαναπαίζει
                System.Threading.Thread.Sleep(1500);
                Report("playback restarts after the command run", mp.State == LibVLCSharp.Shared.VLCState.Playing || mp.State == LibVLCSharp.Shared.VLCState.Opening || mp.State == LibVLCSharp.Shared.VLCState.Buffering, "state=" + mp.State);
            }
            catch (Exception ex) { Report("playback state", false, ex.Message); }

            // 4) ΠΡΑΓΜΑΤΙΚΟΣ συγχρονισμός υποτίτλων: ήχος με «ομιλία» στις γραμμές + 7,3 s, υπότιτλος χωρίς τη μετατόπιση → το .synced.srt πρέπει να ευθυγραμμιστεί
            try { await SyncSelfTest(Report); }
            catch (Exception ex) { Report("subtitle sync end-to-end", false, ex.GetType().Name + ": " + ex.Message); }

            await SuiteSelfTest(Report);

            log.AppendLine($"TOTAL: {pass} passed, {fail} failed, {skipped} skipped");
            File.WriteAllText(SelfTestFile, log.ToString());
            Environment.Exit(fail == 0 ? 0 : 1);
        }

        async Task SyncSelfTest(Action<string, bool, string> report)
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opsis_synctest_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                string wav = System.IO.Path.Combine(dir, "sync.wav"), srt = System.IO.Path.Combine(dir, "sync.en.srt");
                const double shift = 7.3; const int rate = 8000, seconds = 300;
                var rng = new Random(5); var cues = new List<Cue>(); double t = 5;
                while (t < seconds - 15) { double d = 1.5 + rng.NextDouble() * 2.5; cues.Add(new Cue(t, t + d, "line " + cues.Count)); t += d + 1.0 + rng.NextDouble() * 4.0; }
                var pcm = new short[rate * seconds];
                for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)rng.Next(-100, 101);
                foreach (var c in cues) for (int i = (int)((c.Start + shift) * rate); i < Math.Min(pcm.Length, (int)((c.End + shift) * rate)); i++) pcm[i] = (short)rng.Next(-6000, 6001);
                using (var fs = File.Create(wav))
                using (var w = new BinaryWriter(fs))
                {
                    w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm.Length * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                    w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16); w.Write(Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length * 2);
                    foreach (var v in pcm) w.Write(v);
                }
                File.WriteAllText(srt, Srt.Write(cues), new UTF8Encoding(true));
                AddFiles(new[] { wav }, true);
                await Task.Delay(2500);
                string expect = System.IO.Path.Combine(dir, "sync.en.synced.srt");
                await RunSync(wav, srt);
                if (!File.Exists(expect)) { report("subtitle sync end-to-end", false, "no .synced.srt was written (status: " + Status.Text + ")"); return; }
                var fixedCues = Srt.Parse(File.ReadAllText(expect));
                double worst = 0; for (int i = 0; i < Math.Min(fixedCues.Count, cues.Count); i++) worst = Math.Max(worst, Math.Abs(fixedCues[i].Start - (cues[i].Start + shift)));
                report("subtitle sync end-to-end (real LibVLC audio extraction, +" + shift + " s)", fixedCues.Count == cues.Count && worst < 0.2, "cues=" + fixedCues.Count + "/" + cues.Count + ", worst error " + worst.ToString("0.000") + " s; " + Status.Text);
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
        }

        static IEnumerable<MenuItem> Flatten(MenuItem m)
        {
            yield return m;
            foreach (var c in m.Items.OfType<MenuItem>()) foreach (var x in Flatten(c)) yield return x;
        }
    }
}
