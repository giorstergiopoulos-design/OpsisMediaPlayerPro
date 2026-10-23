using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace MediaPlayerPro
{
    // Λειτουργίες που έχουν το Media Player Classic και το VLC και έλειπαν: συσκευή εξόδου ήχου, κανάλια ήχου (στερεοφωνικό/μονοφωνικό/
    // αντίστροφο/αριστερό/δεξί/Dolby), κανάλι βίντεο, τίτλοι και κεφάλαια ως μενού, επανάληψη αρχείου, στατιστικά (Ctrl+J), προσαρμογή
    // παραθύρου στο βίντεο, εναλλαγή υποτίτλων/ήχου με ένα πλήκτρο (V/B), χρόνος που απομένει (T), μεγάλα/μικρά άλματα, επαναφορά
    // ταχύτητας, απόκρυψη δείκτη σε πλήρη οθόνη και Home/End για προηγούμενο/επόμενο αρχείο.
    public partial class MainWindow
    {
        bool remaining;
        readonly DispatcherTimer cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        Point lastMouse;

        static readonly string WindowFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaPlayerPro", "window.txt");

        // Θυμάται θέση/μέγεθος παραθύρου (όπως το MPC-HC). Αν η οθόνη άλλαξε και το παράθυρο θα έπεφτε εκτός, αγνοείται.
        void RestoreWindow()
        {
            try
            {
                if (!File.Exists(WindowFile)) return;
                var p = File.ReadAllText(WindowFile).Split('|');
                if (p.Length < 5) return;
                double l = double.Parse(p[0], CultureInfo.InvariantCulture), t = double.Parse(p[1], CultureInfo.InvariantCulture), w = double.Parse(p[2], CultureInfo.InvariantCulture), h = double.Parse(p[3], CultureInfo.InvariantCulture);
                var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                if (w < MinWidth || h < MinHeight || !vs.Contains(new Point(l + 60, t + 20)) || !vs.Contains(new Point(l + Math.Min(w, 200), t + Math.Min(h, 100)))) return;
                WindowStartupLocation = WindowStartupLocation.Manual; Left = l; Top = t; Width = w; Height = h;
                if (p[4] == "Maximized") WindowState = WindowState.Maximized;
            }
            catch (Exception) { }
        }

        void SaveWindow()
        {
            try
            {
                if (full) return;
                var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(WindowFile));
                File.WriteAllText(WindowFile, string.Join("|", b.Left.ToString(CultureInfo.InvariantCulture), b.Top.ToString(CultureInfo.InvariantCulture), b.Width.ToString(CultureInfo.InvariantCulture), b.Height.ToString(CultureInfo.InvariantCulture), WindowState == WindowState.Maximized ? "Maximized" : "Normal"));
            }
            catch (Exception) { }
        }

        void InitMc()
        {
            RestoreWindow();
            Closing += (s, e) => SaveWindow();
            MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.XButton1) Do("prevfile"); else if (e.ChangedButton == MouseButton.XButton2) Do("nextfile"); };   // πλαϊνά κουμπιά ποντικιού
            cursorTimer.Tick += (s, e) => { cursorTimer.Stop(); if (full) Mouse.OverrideCursor = Cursors.None; };
            MouseMove += (s, e) =>
            {
                var p = e.GetPosition(this);
                if (Math.Abs(p.X - lastMouse.X) < 2 && Math.Abs(p.Y - lastMouse.Y) < 2) return;     // ο δείκτης δεν κουνήθηκε πραγματικά
                lastMouse = p;
                if (Mouse.OverrideCursor != null) Mouse.OverrideCursor = null;
                if (full) { cursorTimer.Stop(); cursorTimer.Start(); }
            };
        }

        bool McCmd(string c)
        {
            switch (c)
            {
                case "remain": remaining = !remaining; Say(remaining ? "Χρόνος: απομένων" : "Χρόνος: τρέχων"); return true;
                case "stats": ShowStats(); return true;
                case "speed1": mp.SetRate(1); SpeedText.Text = "1×"; Say("Ταχύτητα 1×"); return true;
                case "subtoggle": ToggleSubs(); return true;
                case "subcycle": CycleSpu(); return true;
                case "audiocycle": CycleAudio(); return true;
                case "stopafter": Say(StopAfterItem.IsChecked ? "Διακοπή μετά το τρέχον αρχείο" : "Συνεχής αναπαραγωγή"); return true;
                case "prevfile": Play(Math.Max(idx - 1, 0)); return true;
                case "nextfile": if (idx + 1 < items.Count) Play(idx + 1); return true;
                case "openurl": { var u = Prompt("Άνοιγμα URL / stream (http, rtsp, mms, udp…)", ""); if (!string.IsNullOrWhiteSpace(u) && u.Contains("://")) AddFiles(new[] { u.Trim() }, true); return true; }
                case "listfolder": { if (List.SelectedItem is Item it && System.IO.File.Exists(it.Path)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + it.Path + "\""); return true; }
                case "listcopy": { if (List.SelectedItem is Item it) Clipboard.SetText(it.Path); return true; }
                case "sd0": mp.SetSpuDelay(0); Say("Καθυστέρηση υποτίτλων 0"); return true;
                case "plup": MoveSelected(-1); return true;
                case "pldown": MoveSelected(+1); return true;
                case "snapclip": SnapshotToClipboard(); return true;
            }
            if (c.StartsWith("fit:")) { FitWindow(double.Parse(c.Substring(4), System.Globalization.CultureInfo.InvariantCulture)); return true; }
            if (c.StartsWith("ch:")) { SetChannelMode(c.Substring(3)); return true; }
            return false;
        }

        void MoveSelected(int delta)
        {
            int k = List.SelectedIndex, n = k + delta;
            if (k < 0 || n < 0 || n >= items.Count) return;
            items.Move(k, n);
            if (idx == k) idx = n; else if (idx == n) idx = k;
            List.SelectedIndex = n;
        }

        void SnapshotToClipboard()
        {
            var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opsis_snap_" + Guid.NewGuid().ToString("N") + ".png");
            if (!mp.TakeSnapshot(0, tmp, 0, 0)) { Say("Δεν υπάρχει εικόνα βίντεο"); return; }
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                try
                {
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit(); bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.UriSource = new Uri(tmp); bi.EndInit();
                    Clipboard.SetImage(bi); Say("Στιγμιότυπο στο πρόχειρο");
                }
                catch (Exception) { Say("Δεν υπάρχει εικόνα βίντεο"); }
                finally { try { File.Delete(tmp); } catch (Exception) { } }
            };
            t.Start();
        }

        void SetChannelMode(string mode)
        {
            var map = new Dictionary<string, (AudioOutputChannel Ch, string Name)>
            {
                ["stereo"] = (AudioOutputChannel.Stereo, "Στερεοφωνικό"), ["rstereo"] = (AudioOutputChannel.RStereo, "Αντίστροφο στερεοφωνικό"),
                ["left"] = (AudioOutputChannel.Left, "Μόνο αριστερό"), ["right"] = (AudioOutputChannel.Right, "Μόνο δεξί"),
                ["dolby"] = (AudioOutputChannel.Dolbys, "Dolby Surround")
            };
            if (!map.TryGetValue(mode, out var m)) return;
            Say(mp.SetChannel(m.Ch) ? "Κανάλια ήχου: " + m.Name : "Δεν υποστηρίζεται από την έξοδο ήχου");
        }

        void ToggleSubs()
        {
            var tracks = mp.SpuDescription;
            if (mp.Spu >= 0) { lastSpu = mp.Spu; mp.SetSpu(-1); Say("Υπότιτλοι: ανενεργοί"); }
            else
            {
                var pick = tracks.FirstOrDefault(t => t.Id == lastSpu && t.Id >= 0);
                if (pick.Id == 0 && pick.Name == null) pick = tracks.FirstOrDefault(t => t.Id >= 0);
                if (pick.Name != null) { mp.SetSpu(pick.Id); Say("Υπότιτλοι: " + pick.Name); } else Say("Δεν υπάρχουν υπότιτλοι");
            }
        }
        int lastSpu = -1;

        void CycleSpu()
        {
            var list = mp.SpuDescription.ToList();
            if (list.Count == 0) { Say("Δεν υπάρχουν υπότιτλοι"); return; }
            int i = list.FindIndex(t => t.Id == mp.Spu), n = list[(i + 1) % list.Count].Id;
            mp.SetSpu(n); Say("Υπότιτλοι: " + list.First(t => t.Id == n).Name);
        }

        void CycleAudio()
        {
            var list = mp.AudioTrackDescription.Where(t => t.Id >= 0).ToList();
            if (list.Count == 0) { Say("Δεν υπάρχουν κανάλια ήχου"); return; }
            int i = list.FindIndex(t => t.Id == mp.AudioTrack), n = list[(i + 1) % list.Count].Id;
            mp.SetAudioTrack(n); Say("Ήχος: " + list.First(t => t.Id == n).Name);
        }

        // Μέγεθος παραθύρου = (scale × μέγεθος βίντεο), όπως το Alt+1/2 του MPC (scale 0 = ρυθμίστηκε από την οθόνη).
        void FitWindow(double scale)
        {
            if (full) return;
            uint w = 0, h = 0;
            if (!mp.Size(0, ref w, ref h) || w == 0 || h == 0) { Say("Δεν υπάρχει βίντεο"); return; }
            var wa = SystemParameters.WorkArea;
            double chromeW = ActualWidth - Video.ActualWidth, chromeH = ActualHeight - Video.ActualHeight;
            var src = PresentationSource.FromVisual(this);
            double dpi = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1;
            double vw = w * dpi * scale, vh = h * dpi * scale;
            double k = Math.Min(1, Math.Min((wa.Width - chromeW) / vw, (wa.Height - chromeH) / vh));
            WindowState = WindowState.Normal;
            Width = vw * k + chromeW; Height = vh * k + chromeH;
            Left = wa.Left + (wa.Width - Width) / 2; Top = wa.Top + (wa.Height - Height) / 2;
            Say($"Παράθυρο {(int)(scale * k * 100)}%");
        }

        void ShowStats()
        {
            var m = mp.Media; if (m == null) { Say("Δεν παίζει τίποτα"); return; }
            var s = m.Statistics;
            var d = new Window
            {
                Title = "Στατιστικά αναπαραγωγής", Width = 380, Height = 300, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };
            var tb = new TextBlock { Margin = new Thickness(18), FontSize = 13.5, FontFamily = new System.Windows.Media.FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
            void Fill()
            {
                var st = m.Statistics;
                tb.Text = $"Είσοδος (bitrate):      {st.InputBitrate * 8000:0} kb/s\nΑνάγνωση:              {st.ReadBytes / 1048576.0:0.0} MB\n" +
                          $"Demux (bitrate):       {st.DemuxBitrate * 8000:0} kb/s\nΑποκωδικοποιημένα:     {st.DecodedVideo} καρέ\n" +
                          $"Εμφανισμένα καρέ:      {st.DisplayedPictures}\nΧαμένα καρέ:           {st.LostPictures}\n" +
                          $"Ήχος (αποκωδικ.):      {st.DecodedAudio} blocks\nΧαμένα blocks ήχου:    {st.LostAudioBuffers}\n" +
                          $"Ταχύτητα:              {mp.Rate:0.##}×   Θέση: {mp.Position * 100:0.0}%";
            }
            Fill(); d.Content = tb;
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; t.Tick += (a, b) => { try { Fill(); } catch { } };
            d.Closed += (a, b) => t.Stop(); t.Start();
            d.Show();
        }

        // Δυναμικά μενού: συσκευή εξόδου ήχου, κανάλι βίντεο, τίτλοι, κεφάλαια.
        void FillMc()
        {
            AudioDevices.Items.Clear(); VideoTracks.Items.Clear(); TitleMenu.Items.Clear(); ChapterMenu.Items.Clear();
            try
            {
                var cur = mp.OutputDevice;
                foreach (var dev in mp.AudioOutputDeviceEnum)
                {
                    var id = dev.DeviceIdentifier; var mi = new MenuItem { Header = dev.Description, IsChecked = id == cur };
                    mi.Click += (a, b) => { mp.SetOutputDevice(id); Say("Έξοδος ήχου: " + dev.Description); };
                    AudioDevices.Items.Add(mi);
                }
            }
            catch (Exception) { }
            try
            {
                foreach (var t in mp.VideoTrackDescription)
                {
                    var id = t.Id; var mi = new MenuItem { Header = t.Name, IsChecked = id == mp.VideoTrack };
                    mi.Click += (a, b) => mp.SetVideoTrack(id); VideoTracks.Items.Add(mi);
                }
            }
            catch (Exception) { }
            try
            {
                var titles = mp.TitleDescription;
                if (titles != null && titles.Length > 1)
                    for (int i = 0; i < titles.Length; i++)
                    {
                        int n = i; var mi = new MenuItem { Header = string.IsNullOrWhiteSpace(titles[i].Name) ? "Τίτλος " + (i + 1) : titles[i].Name, IsChecked = mp.Title == i };
                        mi.Click += (a, b) => mp.Title = n; TitleMenu.Items.Add(mi);
                    }
                var chapters = mp.FullChapterDescriptions(-1);
                if (chapters != null)
                    for (int i = 0; i < chapters.Length; i++)
                    {
                        long off = chapters[i].TimeOffset; var name = string.IsNullOrWhiteSpace(chapters[i].Name) ? "Κεφάλαιο " + (i + 1) : chapters[i].Name;
                        var mi = new MenuItem { Header = Fmt(off) + "  " + name }; mi.Click += (a, b) => mp.Time = off; ChapterMenu.Items.Add(mi);
                    }
            }
            catch (Exception) { }
            if (AudioDevices.Items.Count == 0) AudioDevices.Items.Add(new MenuItem { Header = "(δεν βρέθηκαν συσκευές)", IsEnabled = false });
            if (VideoTracks.Items.Count == 0) VideoTracks.Items.Add(new MenuItem { Header = "(κανένα)", IsEnabled = false });
            if (TitleMenu.Items.Count == 0) TitleMenu.Items.Add(new MenuItem { Header = "(ένας τίτλος)", IsEnabled = false });
            if (ChapterMenu.Items.Count == 0) ChapterMenu.Items.Add(new MenuItem { Header = "(δεν υπάρχουν κεφάλαια)", IsEnabled = false });
        }

        // Άλματα όπως στο MPC/VLC: Alt ±1″, (τίποτα) ±5″, Shift ±15″, Ctrl ±30″, Ctrl+Shift ±5′.
        bool McKey(KeyEventArgs e)
        {
            var mod = Keyboard.Modifiers; bool ctrl = (mod & ModifierKeys.Control) != 0, shift = (mod & ModifierKeys.Shift) != 0, alt = (mod & ModifierKeys.Alt) != 0;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if ((key == Key.Left || key == Key.Right) && (alt || shift))
            {
                double s = ctrl && shift ? 300 : alt ? 1 : 15; Jump(key == Key.Left ? -s : s); return true;
            }
            switch (key)
            {
                case Key.Home when !ctrl: Do("prevfile"); return true;
                case Key.End when !ctrl: Do("nextfile"); return true;
                case Key.OemPlus when !ctrl: case Key.Back: Do("speed1"); return true;
                case Key.V when !ctrl: Do("subcycle"); return true;
                case Key.B when !ctrl: Do("audiocycle"); return true;
                case Key.T when !ctrl: Do("remain"); return true;
                case Key.J when ctrl: Do("stats"); return true;
                case Key.Z when !ctrl: Do("sd-"); return true;       // υπότιτλοι 100 ms νωρίτερα
                case Key.X when !ctrl: Do("sd+"); return true;       // υπότιτλοι 100 ms αργότερα
                case Key.C when ctrl && !shift: Do("snapclip"); return true;
                case Key.Up when alt: Do("plup"); return true;
                case Key.Down when alt: Do("pldown"); return true;
                case Key.Q when ctrl: Close(); return true;
                case Key.D1 when ctrl: Do("fit:1"); return true;
                case Key.D2 when ctrl: Do("fit:2"); return true;
                case Key.D5 when ctrl: Do("fit:0.5"); return true;
            }
            return false;
        }
    }
}
