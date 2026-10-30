using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaPlayerPro.Conv;
using static MediaPlayerPro.Conv.Loc;

namespace MediaPlayerPro
{
    /// <summary>Διάλογος «Μετατροπή βίντεο / εικόνας» με ουρά, πρόοδο, ακύρωση και ETA.</summary>
    public sealed class ConvertWindow : Window
    {
        public static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaPlayerPro", "convert-dir.txt");
        public static string DefaultOutDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Opsis Media Player");
        public static string OutDir
        {
            get { try { var t = File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile).Trim() : ""; return t.Length > 0 ? t : DefaultOutDir; } catch { return DefaultOutDir; } }
            set { try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)); File.WriteAllText(SettingsFile, value ?? ""); } catch { } }
        }

        readonly string source; readonly double a, b;
        readonly ComboBox kind = new(), fmt = new(), aud = new(), hgt = new(), fps = new(), qual = new(), img = new(), every = new();
        readonly TextBox start = new() { Width = 70 }, end = new() { Width = 70 }, dir = new(), cols = new() { Text = "4", Width = 36 }, rowsBox = new() { Text = "4", Width = 36 }, posterAt = new() { Text = "1", Width = 50 };
        readonly ListBox queue = new() { MinHeight = 90 }; readonly ProgressBar bar = new() { Height = 14, Maximum = 1 }; readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
        readonly List<ConvertJob> jobs = new(); CancellationTokenSource cts; bool running; Stopwatch clock;

        public ConvertWindow(string sourcePath, double abStartSec, double abEndSec)
        {
            source = sourcePath; a = abStartSec; b = abEndSec;
            Title = T("Μετατροπή βίντεο / εικόνας", "Convert video / image"); Width = 560; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 40; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SetResourceReference(BackgroundProperty, "BgBrush"); SetResourceReference(ForegroundProperty, "FgBrush");
            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(new TextBlock { Text = Path.GetFileName(source), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 8) });
            kind.ItemsSource = new[] { T("Βίντεο", "Video"), T("Ακολουθία καρέ (εικόνες)", "Frame sequence (images)"), T("Poster (ένα καρέ)", "Poster (one frame)"), "Contact sheet", };
            kind.SelectedIndex = 0; kind.SelectionChanged += (_, _) => Sync();
            fmt.ItemsSource = Formats.Video.Select(f => f.Label).ToList(); fmt.SelectedIndex = 0; fmt.SelectionChanged += (_, _) => Sync();
            hgt.ItemsSource = Formats.Heights.Select(h => h == 0 ? T("Όπως η πηγή", "Same as source") : h + "p").ToList(); hgt.SelectedIndex = 0;
            fps.ItemsSource = Formats.Fps.Select(h => h == 0 ? T("Όπως η πηγή", "Same as source") : h + " fps").ToList(); fps.SelectedIndex = 0;
            qual.ItemsSource = new[] { T("Χαμηλή (μικρό αρχείο)", "Low (small file)"), T("Μεσαία", "Medium"), T("Υψηλή", "High") }; qual.SelectedIndex = 1;
            img.ItemsSource = new[] { "PNG", "JPEG" }; img.SelectedIndex = 0;
            every.ItemsSource = new[] { T("κάθε 1 δευτερόλεπτο", "every 1 second"), T("κάθε 2 δευτερόλεπτα", "every 2 seconds"), T("κάθε 5 δευτερόλεπτα", "every 5 seconds"), T("κάθε 10 δευτερόλεπτα", "every 10 seconds"), T("κάθε 30 δευτερόλεπτα", "every 30 seconds"), T("κάθε 10 καρέ", "every 10 frames"), T("κάθε 25 καρέ", "every 25 frames") }; every.SelectedIndex = 0;
            Row(sp, T("Έξοδος", "Output"), kind); Row(sp, T("Μορφή βίντεο", "Video format"), fmt); Row(sp, T("Ήχος", "Audio"), aud); Row(sp, T("Ανάλυση", "Resolution"), hgt); Row(sp, T("Καρέ/δευτ.", "Frames/sec"), fps); Row(sp, T("Ποιότητα", "Quality"), qual);
            Row(sp, T("Μορφή εικόνας", "Image format"), img); Row(sp, T("Ρυθμός καρέ", "Frame interval"), every);
            var g = new StackPanel { Orientation = Orientation.Horizontal }; g.Children.Add(cols); g.Children.Add(new TextBlock { Text = " × ", VerticalAlignment = VerticalAlignment.Center }); g.Children.Add(rowsBox);
            g.Children.Add(new TextBlock { Text = T("   Poster στα (δευτ.) ", "   Poster at (sec) "), VerticalAlignment = VerticalAlignment.Center }); g.Children.Add(posterAt); Row(sp, "Contact sheet", g);
            var t = new StackPanel { Orientation = Orientation.Horizontal }; t.Children.Add(new TextBlock { Text = T("από ", "from "), VerticalAlignment = VerticalAlignment.Center }); t.Children.Add(start);
            t.Children.Add(new TextBlock { Text = T("  έως ", "  to "), VerticalAlignment = VerticalAlignment.Center }); t.Children.Add(end);
            var ab = new Button { Content = T("Από A-B", "From A-B"), Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2), IsEnabled = a >= 0 && b > a }; ab.Click += (_, _) => { start.Text = a.ToString("0.##"); end.Text = b.ToString("0.##"); }; t.Children.Add(ab);
            Row(sp, T("Εύρος (δευτ., κενό = όλο)", "Range (sec, empty = all)"), t);
            dir.Text = OutDir; var db = new Button { Content = "…", Width = 34, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(4, 0, 0, 0) }; db.Click += (_, _) => { var d = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = dir.Text }; if (d.ShowDialog(this) == true) dir.Text = d.FolderName; };
            var dp = new DockPanel(); DockPanel.SetDock(db, Dock.Right); dp.Children.Add(db); dp.Children.Add(dir); Row(sp, T("Φάκελος εξόδου", "Output folder"), dp);
            sp.Children.Add(new TextBlock { Text = T("Το πρωτότυπο δεν αγγίζεται ποτέ· τα αρχεία παίρνουν επίθημα και δεν αντικαθιστούν υπάρχοντα. Το H.264/H.265, GIF και WebP δεν προσφέρονται γιατί το ενσωματωμένο libvlc δεν τα παράγει.", "The original is never touched; files get a suffix and never replace existing ones. H.264/H.265, GIF and WebP are not offered because the bundled libvlc cannot produce them."), TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 6, 0, 8) });
            var bp = new WrapPanel();
            var add = Btn(T("Προσθήκη στην ουρά", "Add to queue")); var go = Btn(T("Έναρξη", "Start")); var cancel = Btn(T("Ακύρωση", "Cancel")); var open = Btn(T("Άνοιγμα φακέλου", "Open folder"));
            add.Click += (_, _) => AddJob(); go.Click += async (_, _) => await Go(); cancel.Click += (_, _) => cts?.Cancel(); open.Click += (_, _) => { try { Directory.CreateDirectory(dir.Text); Process.Start(TrustedExe.Explorer, "\"" + dir.Text + "\""); } catch { } };
            foreach (var x in new[] { add, go, cancel, open }) bp.Children.Add(x);
            sp.Children.Add(bp); sp.Children.Add(new TextBlock { Text = T("Ουρά", "Queue"), Margin = new Thickness(0, 10, 0, 2) }); sp.Children.Add(queue); bar.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(bar); sp.Children.Add(status);
            var root = new Border { Child = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }; root.SetResourceReference(Border.BackgroundProperty, "BgBrush"); Content = root;
            Closing += (_, e) => { if (running) { e.Cancel = true; status.Text = T("Τρέχει μετατροπή — πάτα Ακύρωση πρώτα.", "A conversion is running — press Cancel first."); } else OutDir = dir.Text; };
            Sync();
        }

        static Button Btn(string t) => new() { Content = t, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 3, 10, 3) };
        static void Row(StackPanel p, string label, UIElement e)
        {
            var d = new DockPanel { Margin = new Thickness(0, 2, 0, 2) }; var l = new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(l, Dock.Left); d.Children.Add(l); d.Children.Add(e); p.Children.Add(d); d.Tag = e;
        }
        static void Show(UIElement e, bool v) { if (e.GetType() == typeof(DockPanel)) { } var p = (e as FrameworkElement)?.Parent as UIElement; (p ?? e).Visibility = v ? Visibility.Visible : Visibility.Collapsed; }

        void Sync()
        {
            bool vid = kind.SelectedIndex == 0, seq = kind.SelectedIndex == 1, pos = kind.SelectedIndex == 2, sh = kind.SelectedIndex == 3;
            foreach (var x in new UIElement[] { fmt, aud, hgt, fps, qual }) Show(x, vid);
            Show(img, !vid && !sh); Show(every, seq); Show((UIElement)((FrameworkElement)cols.Parent), sh || pos);
            var f = Formats.Video[Math.Max(0, fmt.SelectedIndex)];
            var labels = new Dictionary<string, string> { ["aac"] = "AAC", ["mp3"] = "MPEG audio (MP2/MP3)", ["opus"] = "Opus", ["vorbis"] = "Vorbis", ["wma"] = "WMA", ["none"] = T("Χωρίς ήχο", "No audio"), ["copy"] = T("Αντιγραφή όπως είναι", "Copy as is") };
            aud.ItemsSource = f.Audio.Select(x => labels[x]).ToList(); aud.SelectedIndex = Math.Max(0, Array.IndexOf(f.Audio, f.DefaultAudio));
        }

        static double? Num(string s) => double.TryParse((s ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

        void AddJob()
        {
            var o = new ConvertOptions();
            o.Kind = kind.SelectedIndex switch { 0 => OutKind.Video, 1 => OutKind.FrameSequence, 2 => OutKind.Poster, _ => OutKind.ContactSheet };
            var f = Formats.Video[Math.Max(0, fmt.SelectedIndex)]; o.Format = f.Id; o.Audio = f.Audio[Math.Max(0, aud.SelectedIndex)];
            o.Height = Formats.Heights[Math.Max(0, hgt.SelectedIndex)]; o.Fps = Formats.Fps[Math.Max(0, fps.SelectedIndex)]; o.Quality = (Quality)Math.Max(0, qual.SelectedIndex);
            o.ImageFormat = img.SelectedIndex == 1 ? "jpg" : "png";
            if (!string.IsNullOrWhiteSpace(start.Text)) { var v = Num(start.Text); if (v == null) { status.Text = T("Μη έγκυρη αρχή.", "Invalid start."); return; } o.StartSec = v.Value; }
            if (!string.IsNullOrWhiteSpace(end.Text)) { var v = Num(end.Text); if (v == null) { status.Text = T("Μη έγκυρο τέλος.", "Invalid end."); return; } o.EndSec = v.Value; }
            var ev = new[] { (1.0, 0), (2.0, 0), (5.0, 0), (10.0, 0), (30.0, 0), (0.0, 10), (0.0, 25) }[Math.Max(0, every.SelectedIndex)]; o.EverySeconds = ev.Item1; o.EveryFrames = ev.Item2;
            if (int.TryParse(cols.Text, out var c)) o.SheetColumns = c; if (int.TryParse(rowsBox.Text, out var r)) o.SheetRows = r; var pa = Num(posterAt.Text); if (pa != null) o.PosterAtSec = pa.Value;
            var errs = ConvertCore.Validate(o, 0, 0); if (errs.Count > 0) { status.Text = string.Join("; ", errs); return; }
            jobs.Add(new ConvertJob { Source = source, OutDir = dir.Text, Opt = o }); Refresh(); status.Text = T("Προστέθηκε στην ουρά.", "Added to the queue.");
        }

        void Refresh() => queue.ItemsSource = jobs.Select(j => $"{(j.State == "done" ? "✔" : j.State == "failed" ? "✘" : j.State == "running" ? "▶" : j.State == "cancelled" ? "⏹" : "•")}  {j.Title}  {(j.State == "failed" ? "— " + j.Error : j.State == "done" ? "→ " + Path.GetFileName(j.Result) : j.State == "running" ? (int)(j.Progress * 100) + "%" : "")}").ToList();

        async System.Threading.Tasks.Task Go()
        {
            if (running) return; if (jobs.All(j => j.State != "queued")) AddJob(); if (jobs.All(j => j.State != "queued")) return;
            running = true; cts = new CancellationTokenSource();
            try
            {
                foreach (var j in jobs.Where(x => x.State == "queued").ToList())
                {
                    clock = Stopwatch.StartNew();
                    await ConvertRunner.Run(j, jj => Dispatcher.Invoke(() => { bar.Value = jj.Progress; Refresh(); status.Text = jj.State == "running" ? $"{(int)(jj.Progress * 100)}% — " + T("υπολειπόμενος χρόνος ≈ ", "time left ≈ ") + $"{ConvertCore.FormatEta(ConvertCore.EtaSeconds(jj.Progress, clock.Elapsed.TotalSeconds))}" : jj.State == "failed" ? T("Σφάλμα: ", "Error: ") + jj.Error : status.Text; }), cts.Token);
                    if (cts.IsCancellationRequested) break;
                }
            }
            finally { running = false; Refresh(); bar.Value = 0; if (!status.Text.StartsWith(T("Σφάλμα", "Error"))) status.Text = cts.IsCancellationRequested ? T("Ακυρώθηκε.", "Cancelled.") : T("Ολοκληρώθηκε.", "Done."); foreach (var j in jobs.Where(x => x.State == "queued")) j.State = "queued"; }
        }
    }
}
