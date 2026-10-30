using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using MediaPlayerPro.Conv;

namespace MediaPlayerPro
{
    public sealed class ConvertJob
    {
        public string Source, OutDir; public ConvertOptions Opt; public string Result, Error; public double Progress; public string State = "queued";
        public string Title => Path.GetFileName(Source) + "  →  " + (Opt.Kind == OutKind.Video ? Formats.Find(Opt.Format)?.Label : Opt.Kind.ToString());
    }

    /// <summary>Εκτελεί μια εργασία με libvlc. Το αρχείο-πηγή ανοίγεται μόνο για ανάγνωση και δεν τροποποιείται ποτέ.</summary>
    public static class ConvertRunner
    {
        /// <summary>Ασύγχρονα (ποτέ Wait στο UI thread): διάρκεια, fps, ανάλυση.</summary>
        public static async Task<(double dur, double fps, int h, int w)> ProbeAsync(LibVLC lib, string path)
        {
            using var m = new Media(lib, path, FromType.FromPath);
            await Task.WhenAny(m.Parse(MediaParseOptions.ParseLocal), Task.Delay(10000));
            double fps = 0; int h = 0, w = 0;
            foreach (var t in m.Tracks) if (t.TrackType == TrackType.Video) { h = (int)t.Data.Video.Height; w = (int)t.Data.Video.Width; if (t.Data.Video.FrameRateDen > 0) fps = (double)t.Data.Video.FrameRateNum / t.Data.Video.FrameRateDen; break; }
            return (m.Duration / 1000.0, fps, h, w);
        }

        public static async Task Run(ConvertJob job, Action<ConvertJob> changed, CancellationToken ct)
        {
            string tmpDir = null, partial = null;             // partial = μισό αρχείο εξόδου που σβήνεται σε ακύρωση/σφάλμα
            try
            {
                if (!File.Exists(job.Source)) throw new FileNotFoundException(job.Source);
                job.State = "running"; changed(job);
                using var lib = new LibVLC("--quiet");
                var (dur, fps, sh, sw0) = await ProbeAsync(lib, job.Source);
                var errs = ConvertCore.Validate(job.Opt, dur, fps); if (errs.Count > 0) throw new InvalidOperationException(string.Join("; ", errs));
                var (st, sp) = ConvertCore.Range(job.Opt.StartSec, job.Opt.EndSec, dur);
                double len = ConvertCore.EffectiveLength(st, sp, dur);
                string baseName = Path.GetFileNameWithoutExtension(job.Source);
                Directory.CreateDirectory(job.OutDir);
                string sout, outPath; var o = job.Opt;
                if (o.Kind == OutKind.Video)
                {
                    outPath = ConvertCore.UniquePath(job.OutDir, baseName, ConvertCore.Suffix(o), Formats.Find(o.Format).Ext);
                    if (ConvertCore.SameFile(outPath, job.Source)) throw new InvalidOperationException("output equals source");
                    sout = ConvertCore.BuildVideoSout(o, outPath, sh); partial = outPath;
                }
                else
                {
                    tmpDir = Path.Combine(Path.GetTempPath(), "opsis_frames_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmpDir);
                    var oo = o;
                    if (o.Kind == OutKind.Poster) { oo = new ConvertOptions { Kind = OutKind.FrameSequence, ImageFormat = o.ImageFormat, JpegQuality = o.JpegQuality, EveryFrames = 1 }; st = Math.Min(o.PosterAtSec, Math.Max(0, dur - 0.5)); sp = st + 0.6; len = 0.6; }
                    else if (o.Kind == OutKind.ContactSheet) { int n = o.SheetColumns * o.SheetRows; oo = new ConvertOptions { Kind = OutKind.FrameSequence, ImageFormat = "png", EverySeconds = Math.Max(0.04, len / (n + 1)) }; }
                    sout = ConvertCore.BuildSceneSout(oo, tmpDir, "f", fps); outPath = tmpDir;
                    job.Opt = o;
                }
                using (var m = new Media(lib, job.Source, FromType.FromPath))
                {
                    m.AddOption(":sout=" + sout); m.AddOption(":sout-keep");
                    if (sp > 0 || st > 0) { m.AddOption(":start-time=" + st.ToString("0.###", CultureInfo.InvariantCulture)); if (sp > 0) m.AddOption(":stop-time=" + sp.ToString("0.###", CultureInfo.InvariantCulture)); }
                    using var p = new MediaPlayer(m); p.Play(); var clock = Stopwatch.StartNew();
                    await Task.Delay(500);
                    while (true)
                    {
                        if (ct.IsCancellationRequested) { p.Stop(); await Task.Delay(300); TryDelete(partial); job.State = "cancelled"; job.Error = "cancelled"; changed(job); return; }
                        var s = p.State; if (s == VLCState.Ended || s == VLCState.Stopped) break; if (s == VLCState.Error) throw new InvalidOperationException("libvlc error");
                        if (clock.Elapsed.TotalHours > 6) throw new TimeoutException();
                        if (len > 0) job.Progress = Math.Max(0, Math.Min(0.99, (p.Time / 1000.0 - st) / len)); else job.Progress = p.Position;
                        changed(job); await Task.Delay(300);
                    }
                    p.Stop();
                }
                if (o.Kind == OutKind.Video)
                {
                    if (!File.Exists(outPath) || new FileInfo(outPath).Length < 1000) { TryDelete(outPath); throw new InvalidOperationException("libvlc produced no usable output for " + Formats.Find(o.Format).Label); }
                    job.Result = outPath;
                }
                else
                {
                    var files = Directory.GetFiles(tmpDir).OrderBy(f => f, StringComparer.Ordinal).ToList();
                    if (files.Count == 0) throw new InvalidOperationException("no frames produced");
                    if (o.Kind == OutKind.Poster) { job.Result = Move(files[0], job.OutDir, baseName, "_poster", o.ImageFormat); }
                    else if (o.Kind == OutKind.ContactSheet) job.Result = Sheet.Compose(files, o.SheetColumns, o.SheetRows, ConvertCore.UniquePath(job.OutDir, baseName, "_sheet", "png"));
                    else
                    {
                        string sub = ConvertCore.UniquePath(job.OutDir, baseName, "_frames", "d", p => Directory.Exists(p.Substring(0, p.Length - 2))); sub = sub.Substring(0, sub.Length - 2); Directory.CreateDirectory(sub);
                        int i = 1; foreach (var f in files) File.Move(f, Path.Combine(sub, baseName + "_" + i++.ToString("00000") + "." + o.ImageFormat));
                        job.Result = sub;
                    }
                }
                job.Progress = 1; job.State = "done"; changed(job);
            }
            catch (Exception ex) { TryDelete(partial); job.State = "failed"; job.Error = ex.Message; changed(job); }
            finally { if (tmpDir != null) try { Directory.Delete(tmpDir, true); } catch { } }
        }

        static string Move(string f, string dir, string baseName, string suf, string ext)
        {
            string dst = ConvertCore.UniquePath(dir, baseName, suf, ext); File.Move(f, dst); return dst;
        }
        static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
    }

    static class Sheet
    {
        public static string Compose(List<string> frames, int cols, int rows, string dst)
        {
            int n = Math.Min(frames.Count, cols * rows); if (n == 0) throw new InvalidOperationException("no frames");
            var first = new System.Windows.Media.Imaging.BitmapImage(); first.BeginInit(); first.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; first.UriSource = new Uri(frames[0]); first.EndInit();
            int cw = 320, ch = Math.Max(1, (int)(320.0 * first.PixelHeight / first.PixelWidth)), g = 6;
            int R = (n + cols - 1) / cols; int W = cols * (cw + g) + g, H = R * (ch + g) + g;
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.Black, null, new System.Windows.Rect(0, 0, W, H));
                for (int i = 0; i < n; i++)
                {
                    var b = new System.Windows.Media.Imaging.BitmapImage(); b.BeginInit(); b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; b.UriSource = new Uri(frames[i]); b.EndInit();
                    dc.DrawImage(b, new System.Windows.Rect(g + i % cols * (cw + g), g + i / cols * (ch + g), cw, ch));
                }
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(W, H, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); rtb.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var fs = File.Create(dst)) enc.Save(fs);
            return dst;
        }
    }
}
