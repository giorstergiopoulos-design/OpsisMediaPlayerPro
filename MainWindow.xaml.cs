using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Win32;

namespace MediaPlayerPro
{
    public class Item { public string Name { get; set; } public string Path { get; set; } }

    public partial class MainWindow : Window
    {
        static readonly string[] Exts = { ".mp4",".mkv",".avi",".mov",".wmv",".webm",".flv",".m4v",".ts",".m2ts",".mpg",".mpeg",".vob",".3gp",".ogv",
                                          ".mp3",".flac",".wav",".aac",".m4a",".ogg",".wma",".opus",".ape",".alac" };
        static readonly string[] AudioOnly = { ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".wma", ".opus", ".ape", ".alac" };
        static readonly string[] SubExts = { ".srt", ".ass", ".ssa", ".sub", ".vtt" };
        static readonly string ResumeFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaPlayerPro", "resume.txt");

        readonly LibVLC lib; readonly MediaPlayer mp;
        readonly ObservableCollection<Item> items = new ObservableCollection<Item>();
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        readonly DispatcherTimer sleep = new DispatcherTimer();
        readonly Random rnd = new Random();
        System.Collections.Generic.Dictionary<string, long> resume = new System.Collections.Generic.Dictionary<string, long>();
        int idx = -1, ab; long pendingSeek, A, B; bool dragging, full, muted;
        float br = 1, ct = 1, sa = 1, ga = 1;
        WindowState prevState; WindowStyle prevStyle;

        public MainWindow()
        {
            Core.Initialize();
            InitializeComponent();
            lib = new LibVLC(LibOptions());
            mp = new MediaPlayer(lib) { EnableHardwareDecoding = true };
            Video.MediaPlayer = mp; mp.Volume = 80;
            mp.EndReached += (s, e) => Dispatcher.BeginInvoke(new Action(Next));
            mp.Playing += (s, e) => { if (pendingSeek > 0) { mp.Time = pendingSeek; pendingSeek = 0; } Dispatcher.BeginInvoke(new Action(UpdateIcon)); Dispatcher.BeginInvoke(new Action(() => ApplyAudio(false))); };
            mp.Paused += (s, e) => Dispatcher.BeginInvoke(new Action(UpdateIcon));
            mp.Stopped += (s, e) => Dispatcher.BeginInvoke(new Action(UpdateIcon));
            List.ItemsSource = items;
            try { if (File.Exists(ResumeFile)) resume = File.ReadAllLines(ResumeFile).Select(l => l.Split('|')).Where(a => a.Length == 2)
                    .GroupBy(a => a[0]).ToDictionary(g => g.Key, g => long.Parse(g.Last()[1])); } catch { }
            timer.Tick += (s, e) => Tick(); timer.Start();
            sleep.Tick += (s, e) => { sleep.Stop(); Do("stop"); Say("Χρονόμετρο ύπνου: διακοπή"); };
            InitMc();
            if (Environment.GetCommandLineArgs().Contains("--selftest")) StartSelfTest();
            var args = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToArray();
            if (args.Length > 0) AddFiles(args, true);
        }

        void Say(string t) { Status.Text = t; ShowOsd(t); }
        void UpdateIcon() { PlayBtn.Content = mp.IsPlaying ? "\uE769" : "\uE768"; var st = mp.State; Logo.Visibility = (st == VLCState.Playing || st == VLCState.Paused || st == VLCState.Opening || st == VLCState.Buffering) ? Visibility.Collapsed : Visibility.Visible; }

        // ---------- Playlist ----------
        void AddFiles(string[] paths, bool play)
        {
            int first = items.Count;
            foreach (var p in paths)
            {
                var e = System.IO.Path.GetExtension(p).ToLower();
                if (e == ".zip") { ImportSubZip(p); } else if (SubExts.Contains(e)) { mp.AddSlave(MediaSlaveType.Subtitle, new Uri(p).AbsoluteUri, true); Say("Υπότιτλοι: " + System.IO.Path.GetFileName(p)); }
                else if (Exts.Contains(e) || p.Contains("://")) items.Add(new Item { Name = p.Contains("://") ? p : System.IO.Path.GetFileName(p), Path = p });
            }
            if (play && items.Count > first) Play(first);
        }

        void Play(int i)
        {
            if (i < 0 || i >= items.Count) return;
            SaveResume(); idx = i; AddRecent(items[i].Path); List.SelectedIndex = i;
            var path = items[i].Path;
            resume.TryGetValue(path, out pendingSeek);
            using (var m = new Media(lib, path, path.Contains("://") ? FromType.FromLocation : FromType.FromPath)) { foreach (var o in MediaOpts()) m.AddOption(o); mp.Play(m); }
            Title = items[i].Name + " - Opsis Media Player Pro"; Say(items[i].Name); ab = 0; LoadMeta(path);
            var sub = SubExts.Select(x => System.IO.Path.ChangeExtension(path, x)).FirstOrDefault(File.Exists);
            if (sub != null && !path.Contains("://")) { lastSub = sub; mp.AddSlave(MediaSlaveType.Subtitle, new Uri(sub).AbsoluteUri, true); } else { lastSub = null; if (!path.Contains("://") && !AudioOnly.Contains(System.IO.Path.GetExtension(path).ToLower())) AutoSub(path); }   // ήχος: δεν ψάχνουμε υπότιτλους
        }

        void SaveResume()
        {
            try
            {
                if (idx >= 0 && idx < items.Count && mp.Length > 0)
                    resume[items[idx].Path] = (mp.Time > 10000 && mp.Time < mp.Length - 10000) ? mp.Time : 0;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ResumeFile));
                File.WriteAllLines(ResumeFile, resume.Where(kv => kv.Value > 0).Select(kv => kv.Key + "|" + kv.Value));
            } catch { }
        }

        void Next()
        {
            if (items.Count == 0) return;
            if (RepeatOneItem.IsChecked && idx >= 0) { Play(idx); return; }
            if (StopAfterItem.IsChecked) { mp.Stop(); AfterPlayback(); return; }
            if (ShuffleItem.IsChecked) Play(rnd.Next(items.Count));
            else if (idx + 1 < items.Count) Play(idx + 1);
            else if (LoopItem.IsChecked) Play(0);
            else { mp.Stop(); idx = -1; AfterPlayback(); }
        }

        void Jump(double sec) { if (mp.Length > 0) mp.Time = Math.Max(0, Math.Min(mp.Length, mp.Time + (long)(sec * 1000))); }

        void Tick()
        {
            if (mp.Length > 0)
            {
                Seek.Maximum = mp.Length / 1000.0;
                if (!dragging) Seek.Value = mp.Time / 1000.0;
                TimeText.Text = (remaining ? "-" + Fmt(Math.Max(0, mp.Length - mp.Time)) : Fmt(mp.Time)) + " / " + Fmt(mp.Length);
                if (ab == 2 && mp.Time >= B) mp.Time = A;
            }
        }
        static string Fmt(long ms) { var t = TimeSpan.FromMilliseconds(ms); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss"); }

        // ---------- Commands ----------
        void Cmd(object s, RoutedEventArgs e) => Do((string)((FrameworkElement)s).Tag);

        void Do(string c)
        {
            switch (c)
            {
                case "pp": if (mp.Media == null) { if (items.Count > 0) Play(Math.Max(idx, 0)); else OpenDialog(); } else mp.Pause(); break;
                case "stop": SaveResume(); mp.Stop(); Seek.Value = 0; break;
                case "next": Next(); break;
                case "prev": if (mp.Time > 3000) mp.Time = 0; else Play(Math.Max(idx - 1, 0)); break;
                case "frame": mp.SetPause(true); mp.NextFrame(); break;
                case "chnext": mp.NextChapter(); break;
                case "chprev": mp.PreviousChapter(); break;
                case "ab":
                    if (ab == 0) { A = mp.Time; ab = 1; Say("A-B: σημείο Α " + Fmt(A)); }
                    else if (ab == 1) { B = mp.Time; if (B > A) { ab = 2; Say("A-B επανάληψη ενεργή"); } else ab = 0; }
                    else { ab = 0; Say("A-B: ανενεργή"); } break;
                case "mute": muted = !muted; mp.Mute = muted; MuteBtn.Content = muted ? "\uE74F" : "\uE767"; break;
                case "list": ToggleSide(); break;
                case "full": ToggleFull(); break;
                case "top": Topmost = !Topmost; Say(Topmost ? "Πάντα στο προσκήνιο" : "Κανονικό παράθυρο"); break;
                case "exit": Close(); break;
                case "help": MessageBox.Show("Space: Play/Pause   E: καρέ-καρέ   L: A-B\n←/→: ∓5″  Ctrl+←/→: ∓30″   PgUp/PgDn: κεφάλαια\n↑/↓: Ένταση (έως 200%)   M: Σίγαση\nF11/Enter: Πλήρης οθόνη   Esc: έξοδος\n[ ] : Ταχύτητα   S: Στιγμιότυπο   P: Λίστα\nCtrl+O: Άνοιγμα   Ctrl+T: Στο προσκήνιο   N: Επόμενο", "Συντομεύσεις"); break;
                case "ad-": mp.SetAudioDelay(mp.AudioDelay - 50000); Say("Καθυστέρηση ήχου " + mp.AudioDelay / 1000 + "ms"); break;
                case "ad+": mp.SetAudioDelay(mp.AudioDelay + 50000); Say("Καθυστέρηση ήχου " + mp.AudioDelay / 1000 + "ms"); break;
                case "sd-": mp.SetSpuDelay(mp.SpuDelay - 100000); Say("Καθυστέρηση υποτίτλων " + mp.SpuDelay / 1000 + "ms"); break;
                case "sd+": mp.SetSpuDelay(mp.SpuDelay + 100000); Say("Καθυστέρηση υποτίτλων " + mp.SpuDelay / 1000 + "ms"); break;
                case "subfile":
                    var d = new OpenFileDialog { Filter = "Υπότιτλοι|" + string.Join(";", SubExts.Select(x => "*" + x)) };
                    if (d.ShowDialog() == true) mp.AddSlave(MediaSlaveType.Subtitle, new Uri(d.FileName).AbsoluteUri, true); break;
                case "snap":
                    var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                    var f = System.IO.Path.Combine(dir, "MPP_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                    mp.TakeSnapshot(0, f, 0, 0); Say("Στιγμιότυπο: " + f); break;
                case "stream":
                    var t = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
                    if (t.Contains("://")) AddFiles(new[] { t }, true); else MessageBox.Show("Αντίγραψε πρώτα ένα URL (http, rtsp, κ.λπ.) στο πρόχειρο."); break;
                case "savepl":
                    var sv = new SaveFileDialog { Filter = "M3U|*.m3u" };
                    if (sv.ShowDialog() == true) File.WriteAllLines(sv.FileName, new[] { "#EXTM3U" }.Concat(items.Select(i => i.Path))); break;
                case "loadpl":
                    var ld = new OpenFileDialog { Filter = "M3U|*.m3u;*.m3u8" };
                    if (ld.ShowDialog() == true) AddFiles(File.ReadAllLines(ld.FileName).Where(l => l.Length > 0 && !l.StartsWith("#")).ToArray(), true); break;
                case "vreset": br = ct = sa = ga = 1; Adj(); Say("Εικόνα: επαναφορά"); break;
                case "b+": br = Math.Min(2, br + .1f); Adj(); break; case "b-": br = Math.Max(0, br - .1f); Adj(); break;
                case "c+": ct = Math.Min(2, ct + .1f); Adj(); break; case "c-": ct = Math.Max(0, ct - .1f); Adj(); break;
                case "s+": sa = Math.Min(3, sa + .1f); Adj(); break; case "s-": sa = Math.Max(0, sa - .1f); Adj(); break;
                case "g+": ga = Math.Min(10, ga + .1f); Adj(); break; case "g-": ga = Math.Max(.1f, ga - .1f); Adj(); break;
                default:
                    if (SubCmd(c)) { } else if (c.StartsWith("sp:")) { var v = float.Parse(c.Substring(3), CultureInfo.InvariantCulture); mp.SetRate(v); SpeedText.Text = v + "×"; }
                    else if (c.StartsWith("ar:")) mp.AspectRatio = c.Length > 3 ? c.Substring(3) : null;
                    else if (c.StartsWith("sleep:")) { int m = int.Parse(c.Substring(6)); sleep.Stop(); if (m > 0) { sleep.Interval = TimeSpan.FromMinutes(m); sleep.Start(); Say("Ύπνος σε " + m + " λεπτά"); } }
                    break;
            }
        }

        void Adj()
        {
            mp.SetAdjustInt(VideoAdjustOption.Enable, 1);
            mp.SetAdjustFloat(VideoAdjustOption.Brightness, br); mp.SetAdjustFloat(VideoAdjustOption.Contrast, ct);
            mp.SetAdjustFloat(VideoAdjustOption.Saturation, sa); mp.SetAdjustFloat(VideoAdjustOption.Gamma, ga);
            Say($"Φωτεινότητα {br:0.0}  Αντίθεση {ct:0.0}  Κορεσμός {sa:0.0}  Gamma {ga:0.0}");
        }

        // ---------- Dynamic menus ----------
        void Menu_Opened(object s, RoutedEventArgs e)
        {
            FillRecent(); FillMore(); FillSkins(); FillMc(); AudioTracks.Items.Clear(); SubTracks.Items.Clear(); EqMenu.Items.Clear();
            foreach (var t in mp.AudioTrackDescription) { var id = t.Id; var m = new MenuItem { Header = t.Name, IsChecked = id == mp.AudioTrack }; m.Click += (a, b) => mp.SetAudioTrack(id); AudioTracks.Items.Add(m); }
            foreach (var t in mp.SpuDescription) { var id = t.Id; var m = new MenuItem { Header = t.Name, IsChecked = id == mp.Spu }; m.Click += (a, b) => mp.SetSpu(id); SubTracks.Items.Add(m); }
            FillAudioMenu();
        }

        // ---------- UI ----------
        void Seek_Down(object s, MouseButtonEventArgs e) => dragging = true;
        void Seek_Up(object s, MouseButtonEventArgs e) { dragging = false; mp.Time = (long)(Seek.Value * 1000); }
        void Vol_Changed(object s, RoutedPropertyChangedEventArgs<double> e) { if (mp != null) { mp.Volume = (int)Vol.Value; Say("Ένταση " + (int)Vol.Value + "%"); } }
        void List_DoubleClick(object s, MouseButtonEventArgs e) { if (List.SelectedIndex >= 0) Play(List.SelectedIndex); }

        void OpenDialog()
        {
            var d = new OpenFileDialog { Multiselect = true, Filter = "Πολυμέσα|" + string.Join(";", Exts.Concat(SubExts).Select(x => "*" + x)) + "|Όλα|*.*" };
            if (d.ShowDialog() == true) AddFiles(d.FileNames, true);
        }
        void Open_Click(object s, RoutedEventArgs e) => OpenDialog();
        void OpenFolder_Click(object s, RoutedEventArgs e)
        {
            var d = new OpenFolderDialog();
            if (d.ShowDialog() == true) AddFiles(Directory.GetFiles(d.FolderName).OrderBy(x => x).ToArray(), true);
        }

        void ToggleFull()
        {
            full = !full;
            if (full) { prevState = WindowState; prevStyle = WindowStyle; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; ListCol.Width = new GridLength(0); }
            else { WindowStyle = prevStyle; WindowState = prevState; ListCol.Width = GridLength.Auto; }
            var v = full ? Visibility.Collapsed : Visibility.Visible;
            MenuBar.Visibility = v; Controls.Visibility = v; Seek.Visibility = v;
        }

        void Window_Drop(object s, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] f)
                AddFiles(f.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p) : new[] { p }).ToArray(), mp.Media == null);
        }
        void Window_Closing(object s, System.ComponentModel.CancelEventArgs e) { SaveResume(); mp.Stop(); }

        void Window_KeyDown(object s, KeyEventArgs e)
        {
            if (McKey(e)) { e.Handled = true; return; }
            bool ctrl = Keyboard.Modifiers == ModifierKeys.Control; e.Handled = true;
            switch (e.Key)
            {
                case Key.Space: Do("pp"); break;
                case Key.Left: Jump(ctrl ? -30 : -5); break;
                case Key.Right: Jump(ctrl ? 30 : 5); break;
                case Key.Up: Vol.Value = Math.Min(200, Vol.Value + 5); break;
                case Key.Down: Vol.Value = Math.Max(0, Vol.Value - 5); break;
                case Key.M when ctrl: Do("menu"); break; case Key.M: Do("mute"); break; case Key.F1: Do("help:3"); break; case Key.T when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift): Do("themenext"); break;
                case Key.F11: case Key.Enter: Do("full"); break;
                case Key.Escape: if (full) ToggleFull(); break;
                case Key.P: Do("list"); break;
                case Key.O when ctrl: OpenDialog(); break;
                case Key.T when ctrl: Do("top"); break;
                case Key.E when ctrl: Do("fx"); break; case Key.E: Do("frame"); break;
                case Key.L when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift): Do("modenext"); break; case Key.L: Do("ab"); break;
                case Key.S: Do("snap"); break;
                case Key.N: Do("next"); break; case Key.Y: Do("subline"); break; case Key.G when ctrl: Do("goto"); break; case Key.B when ctrl: Do("bm"); break; case Key.G: Do("subdl"); break; case Key.Delete: Do("plrem"); break; case Key.I when ctrl: Do("info"); break;
                case Key.PageDown: Do("chnext"); break;
                case Key.PageUp: Do("chprev"); break;
                case Key.OemOpenBrackets: { var v = Math.Max(.25f, mp.Rate - .25f); mp.SetRate(v); SpeedText.Text = v + "×"; break; }
                case Key.OemCloseBrackets: { var v = Math.Min(4f, mp.Rate + .25f); mp.SetRate(v); SpeedText.Text = v + "×"; break; }
                default: e.Handled = false; break;
            }
        }
    }
}
