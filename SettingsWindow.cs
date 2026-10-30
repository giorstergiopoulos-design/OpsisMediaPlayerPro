using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MediaPlayerPro.Conv;
using static MediaPlayerPro.Conv.Loc;

namespace MediaPlayerPro
{
    public partial class MainWindow
    {
        SettingsWindow settingsWin;

        void ShowSettingsWindow(int tab = 0)
        {
            if (settingsWin != null && settingsWin.IsLoaded) { settingsWin.Select(tab); settingsWin.Activate(); return; }
            settingsWin = new SettingsWindow(this, tab) { Owner = this };
            settingsWin.Closed += (s, e) => settingsWin = null;
            settingsWin.Show();
        }

        internal SubCfg SettingsCfg() => LoadCfg();
        internal void SettingsSave(SubCfg c) => SaveCfg(c);
        internal string UiStyle => uiStyle;
        internal void SettingsSetInterface(string style) => SetInterface(style);
        internal bool UpdateNotify { get => updater.NotifyEnabled; set => updater.NotifyEnabled = value; }
        internal void SettingsCommand(string cmd) => Do(cmd);
        internal IEnumerable<string> SkinNames => AllSkins().Select(k => k.Name);
        internal string CurrentSkin => curSkin;
        internal void SettingsApplySkin(string n) => ApplySkin(n);
    }

    /// <summary>Ρυθμίσεις ανά κατηγορία (Εμφάνιση, Ήχος, Βίντεο, Υπότιτλοι, Μετατροπέας, Ενημερώσεις) — το ίδιο περιεχόμενο που πριν ήταν σκόρπιο στα μενού.</summary>
    public sealed class SettingsWindow : Window
    {
        readonly MainWindow main; readonly ListBox cats = new() { Width = 170, BorderThickness = new Thickness(0) };
        readonly ContentControl body = new() { Margin = new Thickness(18, 14, 18, 14) };
        readonly List<(string Name, Func<UIElement> Build)> pages = new();
        bool building;

        public SettingsWindow(MainWindow m, int tab)
        {
            main = m; Title = T("Ρυθμίσεις", "Settings"); Width = 760; Height = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SetResourceReference(BackgroundProperty, "BgBrush"); SetResourceReference(ForegroundProperty, "FgBrush");
            pages.Add((T("Εμφάνιση", "Appearance"), AppearancePage)); pages.Add((T("Ήχος", "Audio"), AudioPage)); pages.Add((T("Βίντεο", "Video"), VideoPage));
            pages.Add((T("Υπότιτλοι", "Subtitles"), SubtitlesPage)); pages.Add((T("Μετατροπέας", "Converter"), ConverterPage)); pages.Add((T("Ενημερώσεις", "Updates"), UpdatesPage));
            cats.ItemsSource = pages.Select(p => p.Name).ToList(); cats.SelectionChanged += (_, _) => { if (cats.SelectedIndex >= 0) body.Content = new ScrollViewer { Content = pages[cats.SelectedIndex].Build(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; };
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            cats.Margin = new Thickness(8); Grid.SetColumn(body, 1); grid.Children.Add(cats); grid.Children.Add(body); Content = grid;
            Select(tab);
        }

        public void Select(int i) => cats.SelectedIndex = Math.Clamp(i, 0, pages.Count - 1);

        static TextBlock Head(string t) => new() { Text = t, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        static TextBlock Note(string t) => new() { Text = t, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
        CheckBox Check(string label, bool value, Action<bool> set) { var c = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) }; c.Click += (_, _) => { if (!building) set(c.IsChecked == true); }; return c; }
        Button Btn(string label, Action a) { var b = new Button { Content = label, Margin = new Thickness(0, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 5, 14, 5) }; b.Click += (_, _) => a(); return b; }

        UIElement AppearancePage()
        {
            var p = new StackPanel(); p.Children.Add(Head(T("Εμφάνιση", "Appearance")));
            p.Children.Add(Note(T("Η εμφάνιση αλλάζει αμέσως, χωρίς επανεκκίνηση. Το Wave χρησιμοποιεί τις παλέτες του Waveframe.", "The look changes at once, no restart. Wave uses the Waveframe palettes.")));
            foreach (var (key, label) in new[] { ("classic", "Classic (MPC)"), ("wave", T("Wave (τύπου Waveframe)", "Wave (Waveframe style)")) })
            { var k = key; var r = new RadioButton { Content = label, GroupName = "ui", IsChecked = main.UiStyle == k, Margin = new Thickness(0, 4, 0, 4) }; r.Checked += (_, _) => { if (!building) { main.SettingsSetInterface(k); Select(0); } }; p.Children.Add(r); }
            p.Children.Add(new TextBlock { Text = T("Θέμα", "Theme"), Margin = new Thickness(0, 14, 0, 4) });
            var cb = new ComboBox { ItemsSource = main.SkinNames.ToList(), SelectedItem = main.CurrentSkin, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
            cb.SelectionChanged += (_, _) => { if (!building && cb.SelectedItem is string n) main.SettingsApplySkin(n); };
            p.Children.Add(cb);
            p.Children.Add(Note(T("Σκούρο / φωτεινό / αυτόματο: το κουμπί στη γραμμή ελέγχου (Ctrl+Shift+L).", "Dark / light / automatic: the button in the control bar (Ctrl+Shift+L).")));
            return p;
        }

        UIElement AudioPage()
        {
            var p = new StackPanel(); p.Children.Add(Head(T("Ήχος", "Audio")));
            p.Children.Add(Note(T("Equalizer 10 ζωνών, εφέ (Clarity, Ambience, Surround, Dynamic Boost, Bass Boost) και προεπιλογές.", "10-band equalizer, effects (Clarity, Ambience, Surround, Dynamic Boost, Bass Boost) and presets.")));
            p.Children.Add(Btn(T("Άνοιγμα Equalizer και εφέ…", "Open equalizer and effects…"), () => main.SettingsCommand("fx")));
            p.Children.Add(Check(T("Κανονικοποίηση έντασης", "Volume normalisation"), false, _ => main.SettingsCommand("af:norm")));
            p.Children.Add(Check(T("Συμπιεστής δυναμικής", "Dynamic range compressor"), false, _ => main.SettingsCommand("af:comp")));
            p.Children.Add(Note(T("Τα δύο τελευταία αλλάζουν την εκτέλεση με μικρή παύση (όριο του libvlc).", "The last two restart the stream briefly (a libvlc limit).")));
            return p;
        }

        UIElement VideoPage()
        {
            var p = new StackPanel(); p.Children.Add(Head(T("Βίντεο", "Video")));
            p.Children.Add(Btn(T("Στιγμιότυπο", "Snapshot"), () => main.SettingsCommand("snap")));
            p.Children.Add(Check(T("Αποπλεξία", "Deinterlace"), false, _ => main.SettingsCommand("vf:deint")));
            p.Children.Add(Check(T("Όξυνση", "Sharpen"), false, _ => main.SettingsCommand("vf:sharp")));
            p.Children.Add(Note(T("Περιστροφή, αναλογία εικόνας, φωτεινότητα και οπτικοποιήσεις: μενού Προβολή.", "Rotation, aspect ratio, brightness and visualisations: the View menu.")));
            return p;
        }

        UIElement SubtitlesPage()
        {
            building = true; var c = main.SettingsCfg(); var p = new StackPanel(); p.Children.Add(Head(T("Υπότιτλοι", "Subtitles")));
            p.Children.Add(Check(T("Αυτόματη λήψη υποτίτλων", "Download subtitles automatically"), c.AutoDownload, v => { var x = main.SettingsCfg(); x.AutoDownload = v; main.SettingsSave(x); }));
            p.Children.Add(Check(T("Αυτόματος συγχρονισμός με τον ήχο", "Synchronise with the audio automatically"), c.AutoSync, v => { var x = main.SettingsCfg(); x.AutoSync = v; main.SettingsSave(x); }));
            p.Children.Add(Check(T("Όνομα όπως της ταινίας (ταινία.srt) για την κύρια γλώσσα", "Same name as the movie (movie.srt) for the main language"), c.PlainName, v => { var x = main.SettingsCfg(); x.PlainName = v; main.SettingsSave(x); }));
            p.Children.Add(Check(T("Πληροφορίες ταινίας/σειράς από το διαδίκτυο", "Online movie / series information"), c.OnlineMeta, v => { var x = main.SettingsCfg(); x.OnlineMeta = v; main.SettingsSave(x); }));
            p.Children.Add(new TextBlock { Text = T("Γλώσσες (με σειρά προτεραιότητας, π.χ. el,en)", "Languages (in order of preference, e.g. el,en)"), Margin = new Thickness(0, 10, 0, 4) });
            var tb = new TextBox { Text = c.Languages, Width = 220, HorizontalAlignment = HorizontalAlignment.Left }; tb.LostFocus += (_, _) => { var x = main.SettingsCfg(); x.Languages = string.IsNullOrWhiteSpace(tb.Text) ? "el,en" : tb.Text.Trim(); main.SettingsSave(x); }; p.Children.Add(tb);
            p.Children.Add(new TextBlock { Text = T("Φάκελος υποτίτλων (κενό = δίπλα στο βίντεο)", "Subtitle folder (empty = next to the video)"), Margin = new Thickness(0, 10, 0, 4) });
            var fb = new TextBox { Text = c.SubFolder, Width = 360, HorizontalAlignment = HorizontalAlignment.Left }; fb.LostFocus += (_, _) => { var x = main.SettingsCfg(); x.SubFolder = fb.Text.Trim(); main.SettingsSave(x); }; p.Children.Add(fb);
            p.Children.Add(Note(T("Πηγές στον browser (GreekSubtitles, SubDL, …): μενού Υπότιτλοι.", "Browser sources (GreekSubtitles, SubDL, …): the Subtitles menu.")));
            building = false; return p;
        }

        UIElement ConverterPage()
        {
            var p = new StackPanel(); p.Children.Add(Head(T("Μετατροπέας", "Converter")));
            p.Children.Add(new TextBlock { Text = T("Φάκελος εξόδου", "Output folder") });
            var tb = new TextBox { Text = ConvertWindow.OutDir, Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 6) };
            tb.LostFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(tb.Text)) ConvertWindow.OutDir = tb.Text.Trim(); };
            p.Children.Add(tb);
            p.Children.Add(Btn(T("Μετατροπή βίντεο / εικόνας…", "Convert video / image…"), () => main.SettingsCommand("cv:dlg")));
            p.Children.Add(Note(T("Μόνο μορφές που παράγει πραγματικά το ενσωματωμένο libvlc (βλ. docs/CONVERTER.md).", "Only formats the bundled libvlc really produces (see docs/CONVERTER.md).")));
            return p;
        }

        UIElement UpdatesPage()
        {
            var p = new StackPanel(); p.Children.Add(Head(T("Ενημερώσεις", "Updates")));
            p.Children.Add(Check(T("Ειδοποίηση για νέες εκδόσεις (έλεγχος μία φορά το 24ωρο)", "Notify about new versions (checked once a day)"), main.UpdateNotify, v => main.UpdateNotify = v));
            p.Children.Add(Btn(T("Έλεγχος τώρα", "Check now"), () => main.SettingsCommand("updcheck")));
            p.Children.Add(Note(T("Η λήψη γίνεται μόνο από τα releases του repository με έλεγχο SHA-256 και η εγκατάσταση είναι σιωπηλή.", "Downloads come only from the repository's releases with a SHA-256 check and install silently.")));
            return p;
        }
    }
}
