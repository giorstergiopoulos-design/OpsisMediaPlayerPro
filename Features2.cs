using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LibVLCSharp.Shared;
using Microsoft.Win32;

namespace MediaPlayerPro
{
    public partial class MainWindow
    {
        static readonly string BmFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaPlayerPro", "bookmarks.txt");
        string rot, viz; bool deint, sharp, norm, comp;
        RendererDiscoverer rd; readonly List<RendererItem> renderers = new List<RendererItem>();

        string[] LibOptions()
        {
            var c = LoadCfg();
            return new[] { "--no-video-title-show", "--freetype-rel-fontsize=" + c.SubFontSize, "--freetype-color=" + c.SubColor, "--network-caching=" + c.NetworkCachingMs };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e); LoadSkin();
            PreviewMouseWheel += (s, w) => { Vol.Value = Math.Max(0, Math.Min(200, Vol.Value + (w.Delta > 0 ? 5 : -5))); w.Handled = true; };
            try { rd = new RendererDiscoverer(lib, "microdns_renderer"); rd.ItemAdded += (s, ev) => { lock (renderers) renderers.Add(ev.RendererItem); }; rd.Start(); } catch { }
        }

        List<string> MediaOpts()
        {
            var o = new List<string>(); var vf = new List<string>(); var af = new List<string>();
            if (rot != null) { vf.Add("transform"); o.Add(":transform-type=" + rot); }
            if (sharp) vf.Add("sharpen");
            if (vf.Count > 0) o.Add(":video-filter=" + string.Join(":", vf));
            if (deint) { o.Add(":deinterlace=1"); o.Add(":deinterlace-mode=yadif"); }
            if (norm) af.Add("normvol"); if (comp) af.Add("compressor");
            FxOpts(af, o);
            if (af.Count > 0) o.Add(":audio-filter=" + string.Join(":", af));
            if (viz != null) { o.Add(":audio-visual=visual"); o.Add(":effect-list=" + viz); }
            return o;
        }

        void ReplayHere() { if (idx < 0 || idx >= items.Count) return; long t = mp.Time; Play(idx); pendingSeek = t; }

        void FillMore()
        {
            RendererMenu.Items.Clear();
            var loc = new MenuItem { Header = "Τοπική αναπαραγωγή" }; loc.Click += (s, ev) => { mp.SetRenderer(null); ReplayHere(); }; RendererMenu.Items.Add(loc);
            lock (renderers) foreach (var r in renderers.ToList()) { var it = r; var m = new MenuItem { Header = it.Name }; m.Click += (s, ev) => { mp.SetRenderer(it); ReplayHere(); Say("Αποστολή σε " + it.Name); }; RendererMenu.Items.Add(m); }
            BookmarkMenu.Items.Clear();
            try
            {
                var cp = CurPath();
                if (cp != null && File.Exists(BmFile))
                    foreach (var ln in File.ReadAllLines(BmFile)) { var parts = ln.Split('|'); if (parts.Length == 2 && parts[0] == cp) { long ms = long.Parse(parts[1]); var m = new MenuItem { Header = Fmt(ms) }; m.Click += (s, ev) => mp.Time = ms; BookmarkMenu.Items.Add(m); } }
            }
            catch { }
        }

        string Prompt(string title, string init)
        {
            var w = new Window { Title = title, Width = 340, Height = 135, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
            var tb = new TextBox { Text = init, Margin = new Thickness(12, 14, 12, 6) };
            var ok = new Button { Content = "OK", IsDefault = true, Width = 70, Margin = new Thickness(0, 4, 12, 0), HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += (s, ev) => w.DialogResult = true;
            var sp = new StackPanel(); sp.Children.Add(tb); sp.Children.Add(ok); w.Content = sp;
            w.Loaded += (s, ev) => { tb.Focus(); tb.SelectAll(); };
            return w.ShowDialog() == true ? tb.Text : null;
        }

        static long ParseTime(string t) { double s = 0; foreach (var p in t.Trim().Split(':')) s = s * 60 + double.Parse(p, CultureInfo.InvariantCulture); return (long)(s * 1000); }

        void Theme(string t)
        {
            string bg, bar, fg;
            switch (t) { case "amoled": bg = "#000000"; bar = "#0A0A0A"; fg = "#FFFFFF"; break; case "blue": bg = "#0B1426"; bar = "#101C36"; fg = "#E6EEFF"; break;
                         case "forest": bg = "#0C1712"; bar = "#12211A"; fg = "#E4F2E8"; break; default: bg = "#111214"; bar = "#181A1D"; fg = "#E6E8EB"; break; }
            Brush B(string h) => (Brush)new BrushConverter().ConvertFromString(h);
            Background = B(bg); Controls.Background = B(bar); List.Background = B(bar); List.Foreground = B(fg); MenuBar.Background = B(bar);
        }

        bool MoreCmd(string c)
        {
            switch (c)
            {
                case "menu": ToggleMenu(); return true;
                case "themenext": NextSkin(); return true;
                case "modenext": mode = mode == "dark" ? "light" : mode == "light" ? "auto" : "dark"; ApplySkin(curSkin); return true;
                case "fx": ShowAudio(); return true;
                case "goto": { var t = Prompt("Μετάβαση σε χρόνο (π.χ. 1:23:45)", Fmt(mp.Time)); try { if (t != null) mp.Time = ParseTime(t); } catch { Say("Μη έγκυρος χρόνος"); } return true; }
                case "bm": { var p = CurPath(); if (p != null) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(BmFile)); File.AppendAllText(BmFile, p + "|" + mp.Time + "\n"); Say("Σελιδοδείκτης " + Fmt(mp.Time)); } return true; }
                case "plsort": { var cur = CurPath(); var s2 = items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList(); items.Clear(); foreach (var i in s2) items.Add(i); idx = items.ToList().FindIndex(i => i.Path == cur); return true; }
                case "vf:deint": deint = !deint; ReplayHere(); return true;
                case "vf:sharp": sharp = !sharp; ReplayHere(); return true;
                case "af:norm": norm = !norm; ReplayHere(); return true;
                case "af:comp": comp = !comp; ReplayHere(); return true;
                case "cv:dlg": { var v = CurPath(); if (v == null) { Say("Δεν παίζει τοπικό αρχείο"); return true; } new ConvertWindow(v, ab == 2 ? A / 1000.0 : -1, ab == 2 ? B / 1000.0 : -1) { Owner = this }.Show(); return true; }
                case "cv:mp4": case "cv:mp3": case "cv:clip": _ = ConvertMedia(c); return true;
            }
            if (c.StartsWith("side:")) { SideCmd(c.Substring(5)); return true; }
            if (c.StartsWith("help:")) { ShowHelp(int.Parse(c.Substring(5))); return true; }
            if (c.StartsWith("vf:rot:")) { var v = c.Substring(7); rot = v == "off" ? null : v; ReplayHere(); return true; }
            if (c.StartsWith("viz:")) { var v = c.Substring(4); viz = v == "off" ? null : v; ReplayHere(); return true; }
            return McCmd(c);
        }

        async Task ConvertMedia(string kind)
        {
            var v = CurPath(); if (v == null) { Say("Δεν παίζει τοπικό αρχείο"); return; }
            double st = 0, sp = 0; string sout, ext;
            if (kind == "cv:clip") { if (ab != 2) { Say("Όρισε πρώτα επανάληψη A-B (L, L)"); return; } st = A / 1000.0; sp = B / 1000.0; sout = "#std{access=file,mux=ts,dst={OUT}}"; ext = "_clip.ts"; }
            else if (kind == "cv:mp3") { sout = "#transcode{acodec=mp3,ab=192,channels=2,samplerate=44100,vcodec=none}:std{access=file,mux=raw,dst={OUT}}"; ext = ".mp3"; }
            else { sout = "#transcode{vcodec=h264,acodec=mp4a,ab=192,channels=2,samplerate=48000,scodec=none}:std{access=file,mux=mp4,dst={OUT}}"; ext = "_conv.mp4"; }
            Directory.CreateDirectory(ConvertWindow.OutDir);
            var d = new SaveFileDialog { FileName = System.IO.Path.GetFileNameWithoutExtension(v) + ext, InitialDirectory = ConvertWindow.OutDir };
            if (d.ShowDialog() != true) return;
            try
            {
                using (var l2 = new LibVLC("--quiet"))
                using (var m = new Media(l2, v, FromType.FromPath))
                {
                    m.AddOption(":sout=" + sout.Replace("{OUT}", "\"" + d.FileName + "\"")); m.AddOption(":sout-keep");
                    if (sp > 0) { m.AddOption(":start-time=" + st.ToString(CultureInfo.InvariantCulture)); m.AddOption(":stop-time=" + sp.ToString(CultureInfo.InvariantCulture)); }
                    using (var p = new LibVLCSharp.Shared.MediaPlayer(m))
                    {
                        p.Play(); var sw = Stopwatch.StartNew(); await Task.Delay(1000);
                        while (sw.Elapsed.TotalHours < 4)
                        {
                            await Task.Delay(500); var s = p.State;
                            if (s == VLCState.Ended || s == VLCState.Stopped || s == VLCState.Error) break;
                            double pct = sp > 0 ? (p.Time / 1000.0 - st) / (sp - st) * 100 : p.Position * 100;
                            Say("Επεξεργασία… " + Math.Max(0, Math.Min(99, (int)pct)) + "%");
                        }
                        p.Stop();
                    }
                }
                Say("Ολοκληρώθηκε: " + d.FileName);
            }
            catch (Exception ex) { Say("Σφάλμα μετατροπής: " + ex.Message); }
        }
    }
}
