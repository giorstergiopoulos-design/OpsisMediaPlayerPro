using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MediaPlayerPro.Conv
{
    // Καθαρός κώδικας (χωρίς WPF/LibVLC) για τον μετατροπέα· μεταγλωττίζεται και μέσα στα unit tests.
    // Οι μορφές εδώ είναι ΜΟΝΟ όσες επαληθεύτηκαν με πραγματική μετατροπή στο ενσωματωμένο libvlc 3.0.21
    // (βλ. docs/CONVERTER.md). Το H.264/H.265/GIF/WebP δεν παράγονται από αυτό το libvlc και ΔΕΝ προσφέρονται.

    public sealed class VideoFormat
    {
        public string Id, Label, Ext, Mux, Vcodec; public string[] Audio; public string DefaultAudio;
    }

    public static class Formats
    {
        public static readonly VideoFormat[] Video =
        {
            new VideoFormat { Id = "mp4",  Label = "MP4 (MPEG-4 Part 2)",    Ext = "mp4",  Mux = "mp4",  Vcodec = "mp4v", Audio = new[] { "aac", "mp3", "none", "copy" }, DefaultAudio = "aac" },
            new VideoFormat { Id = "mkv",  Label = "MKV (VP9, δοκιμαστικό)",              Ext = "mkv",  Mux = "avformat{mux=matroska}", Vcodec = "VP90", Audio = new[] { "opus", "vorbis", "none" }, DefaultAudio = "opus" },
            new VideoFormat { Id = "webm9",Label = "WebM (VP9 + Opus, δοκιμαστικό)",      Ext = "webm", Mux = "avformat{mux=webm}",     Vcodec = "VP90", Audio = new[] { "opus", "vorbis", "none" }, DefaultAudio = "opus" },
            new VideoFormat { Id = "webm8",Label = "WebM (VP8 + Vorbis)",    Ext = "webm", Mux = "avformat{mux=webm}",     Vcodec = "VP80", Audio = new[] { "vorbis", "opus", "none" }, DefaultAudio = "vorbis" },
            new VideoFormat { Id = "avi",  Label = "AVI (MPEG-4)",           Ext = "avi",  Mux = "avi",  Vcodec = "mp4v", Audio = new[] { "mp3", "none" }, DefaultAudio = "mp3" },
            new VideoFormat { Id = "avimj",Label = "AVI (Motion JPEG)",      Ext = "avi",  Mux = "avi",  Vcodec = "MJPG", Audio = new[] { "mp3", "none" }, DefaultAudio = "mp3" },
            new VideoFormat { Id = "wmv",  Label = "WMV / ASF",              Ext = "wmv",  Mux = "asf",  Vcodec = "WMV2", Audio = new[] { "wma", "mp3", "none" }, DefaultAudio = "wma" },
            new VideoFormat { Id = "ts",   Label = "TS (MPEG-2)",            Ext = "ts",   Mux = "ts",   Vcodec = "mp2v", Audio = new[] { "mp3", "aac", "none" }, DefaultAudio = "mp3" },
            new VideoFormat { Id = "ogv",  Label = "OGV (Theora + Vorbis)",  Ext = "ogv",  Mux = "ogg",  Vcodec = "theo", Audio = new[] { "vorbis", "opus", "none" }, DefaultAudio = "vorbis" },
        };
        public static VideoFormat Find(string id) => Video.FirstOrDefault(f => f.Id == id);
        public static readonly string[] ImageFormats = { "png", "jpg" };      // το scene/TakeSnapshot του libvlc παράγει png και jpeg
        public static string AudioCodec(string a) => a switch { "aac" => "mp4a", "mp3" => "mpga", "opus" => "opus", "vorbis" => "vorb", "wma" => "wma2", _ => null };
        public static readonly int[] Heights = { 0, 2160, 1080, 720, 480, 360, 240 };       // 0 = όπως η πηγή
        public static readonly int[] Fps = { 0, 60, 30, 25, 24, 15, 10 };                   // 0 = όπως η πηγή
        public static readonly int[] AudioKbps = { 64, 96, 128, 192, 256 };
    }

    public enum OutKind { Video, FrameSequence, Poster, ContactSheet, CurrentFrame }
    public enum Quality { Low, Medium, High }

    public sealed class ConvertOptions
    {
        public OutKind Kind = OutKind.Video;
        public string Format = "mp4", Audio = "aac", ImageFormat = "png";
        public int Height, Fps, AudioKbps = 128; public Quality Quality = Quality.Medium;
        public double StartSec = -1, EndSec = -1;        // <0 = ολόκληρο
        public double EverySeconds = 1;                    // για ακολουθία καρέ
        public int EveryFrames;                            // αν >0 υπερισχύει
        public int SheetColumns = 4, SheetRows = 4; public double PosterAtSec = 1;
        public int JpegQuality = 90;
    }

    public static class ConvertCore
    {
        public static List<string> Validate(ConvertOptions o, double durationSec, double sourceFps)
        {
            var e = new List<string>();
            if (o == null) { e.Add("no options"); return e; }
            if (durationSec > 0 && o.StartSec >= 0 && o.StartSec >= durationSec) e.Add("start after end of media");
            if (o.EndSec >= 0 && o.StartSec >= 0 && o.EndSec <= o.StartSec) e.Add("end must be after start");
            if (o.Height < 0 || o.Height > 4320) e.Add("bad height");
            if (o.Fps < 0 || o.Fps > 120) e.Add("bad fps");
            if (o.Kind == OutKind.Video)
            {
                var f = Formats.Find(o.Format); if (f == null) { e.Add("unknown format " + o.Format); return e; }
                if (!f.Audio.Contains(o.Audio)) e.Add($"audio '{o.Audio}' is not available in {f.Label}");
                if (o.Audio != "none" && o.Audio != "copy" && !Formats.AudioKbps.Contains(o.AudioKbps)) e.Add("bad audio bitrate");
            }
            else
            {
                if (!Formats.ImageFormats.Contains(o.ImageFormat)) e.Add("unsupported image format " + o.ImageFormat + " (png, jpg only)");
                if (o.JpegQuality < 1 || o.JpegQuality > 100) e.Add("bad jpeg quality");
                if (o.Kind == OutKind.FrameSequence)
                {
                    if (o.EveryFrames <= 0 && !(o.EverySeconds > 0)) e.Add("interval must be positive");
                    if (o.EveryFrames <= 0 && sourceFps <= 0) { /* άγνωστο fps: θα χρησιμοποιηθούν 25 */ }
                }
                if (o.Kind == OutKind.ContactSheet && (o.SheetColumns < 1 || o.SheetRows < 1 || o.SheetColumns * o.SheetRows > 100)) e.Add("bad sheet grid");
                if (o.Kind == OutKind.Poster && o.PosterAtSec < 0) e.Add("bad poster time");
            }
            return e;
        }

        /// <summary>Τελικό εύρος σε δευτερόλεπτα. (0, 0) = ολόκληρο το αρχείο.</summary>
        public static (double start, double stop) Range(double startSec, double endSec, double durationSec)
        {
            double s = startSec < 0 ? 0 : startSec; double e = endSec < 0 ? 0 : endSec;
            if (durationSec > 0) { if (s > durationSec) s = durationSec; if (e > durationSec) e = durationSec; }
            if (e > 0 && e <= s) e = 0;
            if (s == 0 && e == 0) return (0, 0);
            return (s, e);
        }

        public static double EffectiveLength(double start, double stop, double durationSec) => stop > 0 ? stop - start : Math.Max(0, durationSec - start);

        static string N(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

        public static int VideoKbps(Quality q, int height, int srcHeight)
        {
            int h = height > 0 ? height : (srcHeight > 0 ? srcHeight : 720);
            double baseKbps = h >= 2000 ? 12000 : h >= 1000 ? 4500 : h >= 700 ? 2500 : h >= 450 ? 1200 : h >= 340 ? 700 : 400;
            return (int)(baseKbps * (q == Quality.Low ? 0.5 : q == Quality.High ? 1.8 : 1.0));
        }

        /// <summary>Αλυσίδα sout για βίντεο. Το path μπαίνει σε εισαγωγικά· τα εισαγωγικά μέσα στο path απορρίπτονται.</summary>
        public static string BuildVideoSout(ConvertOptions o, string outPath, int srcHeight = 0)
        {
            var f = Formats.Find(o.Format) ?? throw new ArgumentException("format");
            if (outPath.Contains('"') || outPath.Contains('\n')) throw new ArgumentException("illegal characters in output path");
            var tc = new List<string> { "vcodec=" + f.Vcodec };
            if (f.Vcodec == "MJPG") tc.Add("vb=" + (VideoKbps(o.Quality, o.Height, srcHeight) * 2)); else tc.Add("vb=" + VideoKbps(o.Quality, o.Height, srcHeight));
            if (o.Height > 0) tc.Add("height=" + o.Height);
            if (o.Fps > 0) tc.Add("fps=" + o.Fps);
            if (o.Audio == "none") tc.Add("acodec=none");
            else if (o.Audio != "copy")
            {
                string ac = Formats.AudioCodec(o.Audio) ?? throw new ArgumentException("audio");
                tc.Add("acodec=" + ac); tc.Add("ab=" + o.AudioKbps); tc.Add("channels=2");
            }
            return "#transcode{" + string.Join(",", tc) + "}:std{access=file,mux=" + f.Mux + ",dst=\"" + outPath + "\"}";
        }

        /// <summary>Ακολουθία καρέ με το φίλτρο scene (το transcode σε mp4v + dummy είναι ο μόνος τρόπος που δουλεύει στο libvlc 3.0.21).</summary>
        public static string BuildSceneSout(ConvertOptions o, string dir, string prefix, double fps)
        {
            if (dir.Contains('"') || prefix.Contains('"')) throw new ArgumentException("illegal characters");
            return "#transcode{vcodec=mp4v,vb=200,vfilter=scene{format=" + (o.ImageFormat == "jpg" ? "jpeg" : "png") + (o.ImageFormat == "jpg" ? ",quality=" + o.JpegQuality : "")
                   + ",ratio=" + SceneRatio(o, fps) + ",prefix=" + prefix + ",path=\"" + dir + "\"}}:dummy";
        }

        public static int SceneRatio(ConvertOptions o, double fps)
        {
            if (o.EveryFrames > 0) return o.EveryFrames;
            if (fps <= 0 || fps > 240) fps = 25;
            return Math.Max(1, (int)Math.Round(fps * o.EverySeconds));
        }

        /// <summary>Πόσα καρέ θα βγουν περίπου (για progress/εκτίμηση).</summary>
        public static int ExpectedFrames(ConvertOptions o, double lengthSec, double fps)
        {
            if (lengthSec <= 0) return 0; if (fps <= 0) fps = 25;
            return Math.Max(1, (int)Math.Ceiling(lengthSec * fps / SceneRatio(o, fps)));
        }

        // ---------- ονόματα αρχείων ----------
        static readonly char[] Bad = Path.GetInvalidFileNameChars();
        public static string SafeName(string s)
        {
            var t = new string((s ?? "").Select(c => Bad.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return t.Length == 0 ? "output" : (t.Length > 120 ? t.Substring(0, 120) : t);
        }

        /// <summary>Όνομα δίπλα στην πηγή (ή στο dir) με επίθημα· ποτέ δεν επιστρέφει υπάρχον αρχείο ή την ίδια την πηγή.</summary>
        public static string UniquePath(string dir, string baseName, string suffix, string ext, Func<string, bool> exists = null)
        {
            exists ??= File.Exists; string stem = SafeName(baseName) + suffix; ext = ext.TrimStart('.');
            string p = Path.Combine(dir, stem + "." + ext); int n = 2;
            while (exists(p)) p = Path.Combine(dir, $"{stem} ({n++}).{ext}");
            return p;
        }

        public static string Suffix(ConvertOptions o) => o.Kind switch
        {
            OutKind.Video => "_" + o.Format + (o.Height > 0 ? "_" + o.Height + "p" : ""),
            OutKind.Poster => "_poster", OutKind.ContactSheet => "_sheet", OutKind.CurrentFrame => "_frame", _ => "_frames"
        };

        public static string FormatEta(double sec)
        {
            if (sec < 0 || double.IsNaN(sec) || double.IsInfinity(sec)) return "--:--";
            int s = (int)Math.Round(sec); return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }

        public static double EtaSeconds(double fraction, double elapsedSec) => fraction <= 0.01 ? -1 : elapsedSec * (1 - fraction) / fraction;

        /// <summary>Η έξοδος δεν πρέπει ποτέ να είναι το αρχείο-πηγή (ο πρωτότυπος δεν αγγίζεται).</summary>
        public static bool SameFile(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }
}
