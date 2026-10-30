using System;
using MediaPlayerPro.Conv;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Opsis.Suite;

namespace MediaPlayerPro
{
    /// <summary>Κοινά για τα δύο παράθυρα: κείμενα και «μικραίνει μέσα στο παράθυρο» (κλίμακα + μετακίνηση + fade ~400 ms προς το εικονίδιο της εφαρμογής).</summary>
    static class SuiteUi
    {
        public static string L => SuiteLang.Current();
        public static string T(string el, string en) => L == "el" ? el : en;

        public static bool LowAnimations => !SystemParameters.ClientAreaAnimation;

        static void Res(FrameworkElement e, DependencyProperty p, string key) => e.SetResourceReference(p, key);

        public static TextBlock Text(string s, double size, string fg = "FgBrush", bool bold = false, bool wrap = true)
        {
            var t = new TextBlock { Text = s, FontSize = size, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
            Res(t, TextBlock.ForegroundProperty, fg); return t;
        }

        /// <summary>Το σημείο (σε DIP, οθόνης) όπου «κατοικεί» το εικονίδιο της εφαρμογής: πάνω-αριστερά στη γραμμή τίτλου του κύριου παραθύρου.</summary>
        public static Rect IconTarget(Window main)
        {
            try
            {
                var src = PresentationSource.FromVisual(main);
                var topLeft = main.PointToScreen(new Point(0, 0));
                if (src?.CompositionTarget != null) topLeft = src.CompositionTarget.TransformFromDevice.Transform(topLeft);
                return new Rect(topLeft.X + 8, topLeft.Y - 26, 20, 20);          // η γραμμή τίτλου είναι ~30 px πάνω από την περιοχή του πελάτη
            }
            catch (Exception) { return new Rect(10, 10, 20, 20); }
        }

        /// <summary>Κλείνει το παράθυρο με animation προς το <paramref name="target"/>· χωρίς animations των Windows (ή ms ≤ 0) απλό fade 150 ms.</summary>
        public static async Task MinimiseAsync(Window w, Rect target, int ms = 420)
        {
            if (!w.IsLoaded) { w.Close(); return; }
            bool simple = LowAnimations || ms <= 0;
            int dur = simple ? 150 : ms;
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var done = new TaskCompletionSource<bool>();
            w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(dur)) { EasingFunction = ease });
            if (!simple)
            {
                w.BeginAnimation(Window.LeftProperty, new DoubleAnimation(w.Left, target.Left, TimeSpan.FromMilliseconds(dur)) { EasingFunction = ease });
                w.BeginAnimation(Window.TopProperty, new DoubleAnimation(w.Top, target.Top, TimeSpan.FromMilliseconds(dur)) { EasingFunction = ease });
                w.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(w.ActualWidth, target.Width, TimeSpan.FromMilliseconds(dur)) { EasingFunction = ease });
                var h = new DoubleAnimation(w.ActualHeight, target.Height, TimeSpan.FromMilliseconds(dur)) { EasingFunction = ease };
                h.Completed += (s, e) => done.TrySetResult(true);
                w.BeginAnimation(FrameworkElement.HeightProperty, h);
            }
            else Task.Delay(dur).ContinueWith(_ => done.TrySetResult(true));
            await done.Task;
            w.Close();
        }

        public static void OpenUrl(string url)
        {
            if (!SafeUrl.IsAllowed(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { }
        }

        /// <summary>Παράθυρο χωρίς πλαίσιο με στρογγυλεμένη «κάρτα» μέσα σε Viewbox: το περιεχόμενο κλιμακώνεται όταν μικραίνει το παράθυρο.</summary>
        public static Window Shell(Window owner, double w, double h, out Grid content)
        {
            var win = new Window
            {
                Width = w, Height = h, WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, Background = Brushes.Transparent
            };
            if (owner != null && owner.IsVisible) win.Owner = owner;
            content = new Grid { Width = w - 24, Height = h - 24 };
            var card = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Margin = new Thickness(12), Child = content, Effect = null };
            Res(card, Border.BackgroundProperty, "BarBrush"); Res(card, Border.BorderBrushProperty, "HoverBrush");
            var shadowHost = new Grid { Width = w, Height = h };
            shadowHost.Children.Add(new Border { CornerRadius = new CornerRadius(16), Margin = new Thickness(12), Background = Brushes.Black, Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 0 }, Opacity = 0 });
            shadowHost.Children.Add(card);
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.55, Color = Colors.Black };
            win.Content = new Viewbox { Stretch = Stretch.Uniform, Child = shadowHost };
            return win;
        }
    }

    // ===============================================================================================================================
    //  Καλωσόρισμα + οδηγός χρήσης
    // ===============================================================================================================================
    public sealed class WelcomeWindow : Window
    {
        public sealed class Page { public string Title, Body; }
        public static List<Page> Pages()
        {
            string T(string el, string en) => SuiteUi.T(el, en);
            return new List<Page>
            {
                new Page { Title = T("Καλώς ήρθες στο Opsis Media Player Pro", "Welcome to Opsis Media Player Pro"),
                           Body = T("Ένας γρήγορος player βίντεο και ήχου με τον κινητήρα του VLC και τις συντομεύσεις του Media Player Classic.\n\nΣύρε αρχεία ή φακέλους μέσα στο παράθυρο, ή πάτα Ctrl+O. Παίζει σχεδόν κάθε μορφή, streams, φακέλους DVD και κάμερες.",
                                    "A fast video and audio player on the VLC engine with Media Player Classic shortcuts.\n\nDrop files or folders on the window, or press Ctrl+O. It plays almost any format, streams, DVD folders and cameras.") },
                new Page { Title = T("Οι κύριες περιοχές", "The main areas"),
                           Body = T("•  Το βίντεο: κλικ = παύση, διπλό κλικ = πλήρης οθόνη, ροδέλα = ένταση.\n•  Η γραμμή ελέγχου κάτω: αναπαραγωγή, χρόνος, ταχύτητα, σίγαση, κουμπί σκούρου/φωτεινού, λίστα, πλήρης οθόνη.\n•  Το πλευρικό πάνελ (P): λίστα αναπαραγωγής και πληροφορίες ταινίας.\n•  Το μενού (Ctrl+M για απόκρυψη).",
                                    "•  The video: click = pause, double click = full screen, wheel = volume.\n•  The control bar: play, time, speed, mute, the dark/light button, playlist, full screen.\n•  The side panel (P): playlist and movie information.\n•  The menu (Ctrl+M hides it).") },
                new Page { Title = T("Οι 3 πιο χρήσιμες ενέργειες", "The 3 most useful actions"),
                           Body = T("1.  Space: παύση · ←/→: ∓5″ (Shift ∓15″, Ctrl ∓30″) · [ ]: ταχύτητα.\n2.  G: λήψη υποτίτλων· ο συγχρονισμός με τον ήχο γίνεται αυτόματα. Z/X μετακινούν τους υπότιτλους κατά 100 ms.\n3.  Ctrl+E: ενισχυτής ήχου (equalizer και εφέ).\n\nΌλες οι συντομεύσεις: F1.",
                                    "1.  Space: pause · ←/→: ∓5″ (Shift ∓15″, Ctrl ∓30″) · [ ]: speed.\n2.  G: download subtitles; they are synced to the audio automatically. Z/X move them by 100 ms.\n3.  Ctrl+E: audio enhancer (equalizer and effects).\n\nAll shortcuts: F1.") },
                new Page { Title = T("Υπότιτλοι και ιδιωτικότητα", "Subtitles and privacy"),
                           Body = T("Η αυτόματη λήψη υποτίτλων και οι πληροφορίες ταινίας είναι ΕΝΕΡΓΕΣ από προεπιλογή: στέλνουν έναν καθαρισμένο τίτλο (και, για το OpenSubtitles, ένα hash του αρχείου) στο Podnapisi/OpenSubtitles, TVmaze, iTunes και Wikipedia. Για αρχεία ήχου δεν γίνεται αναζήτηση.\n\nΚλείνουν από Ρυθμίσεις → Υπότιτλοι → Ρυθμίσεις υποτίτλων και από Προβολή → Πλευρικό πάνελ. Δεν υπάρχει τηλεμετρία.",
                                    "Automatic subtitle download and movie information are ON by default: they send a cleaned-up title (and, for OpenSubtitles, a file hash) to Podnapisi/OpenSubtitles, TVmaze, iTunes and Wikipedia. Audio files are never searched.\n\nTurn them off in Settings → Subtitles → subtitle settings and View → Side panel. There is no telemetry.") },
                new Page { Title = T("Ασφάλεια και ρυθμίσεις", "Safety and settings"),
                           Body = T("Ο player δεν αλλάζει αρχεία ή ρυθμίσεις των Windows. Οι μετατροπές (MP4/MP3, κλιπ A-B) γράφουν ΝΕΑ αρχεία και δεν πειράζουν τα πρωτότυπα. Θέση αναπαραγωγής, θέμα, σελιδοδείκτες και μέγεθος παραθύρου αποθηκεύονται στο %AppData%\\MediaPlayerPro.\n\nΕνημερώσεις: κάθε ~24 ώρες γίνεται ένα αίτημα προς το GitHub για τη νέα έκδοση (απενεργοποιείται από το μενού Βοήθεια).",
                                    "The player does not change Windows files or settings. Conversions (MP4/MP3, A-B clips) write NEW files and never touch the originals. Playback position, theme, bookmarks and window size are saved in %AppData%\\MediaPlayerPro.\n\nUpdates: about every 24 hours one request goes to GitHub to look for a new version (switch it off in the Help menu).") },
                new Page { Title = T("Οι εφαρμογές της οικογένειας Opsis", "The Opsis family"),
                           Body = T("Το Opsis Media Player Pro είναι μέρος μιας οικογένειας εφαρμογών: Opsis (εξατομίκευση επιφάνειας εργασίας), News Crawler, Appearance, GearWin, Kombos και Waveframe.\n\nΑπό Βοήθεια → «Οι εφαρμογές της οικογένειας Opsis» βλέπεις τι κάνει η καθεμία, ποιες έχεις εγκατεστημένες και μπορείς να τις ανοίξεις ή να τις κατεβάσεις (με έλεγχο SHA-256).",
                                    "Opsis Media Player Pro is part of a family of apps: Opsis (desktop customisation), News Crawler, Appearance, GearWin, Kombos and Waveframe.\n\nHelp → \"The Opsis family\" shows what each does, which ones you have, and lets you open or download them (with a SHA-256 check).") },
            };
        }

        private readonly List<Page> _pages = Pages();
        private int _i;
        private readonly TextBlock _title, _body, _count;
        private readonly Button _back, _next, _skip;
        private readonly CheckBox _never;
        private readonly StackPanel _dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly Window _main; private readonly IKeyValueStore _store; private readonly string _version;
        private bool _closing;
        public bool Finished { get; private set; }
        public int MinimiseMs { get; set; } = 420;

        public WelcomeWindow(Window main, IKeyValueStore store, string version)
        {
            _main = main; _store = store; _version = version;
            var shell = SuiteUi.Shell(main, 660, 500, out var g);
            // μεταφέρουμε το «κέλυφος» σε αυτό το παράθυρο (το Window δεν μπορεί να κληρονομήσει από το αποτέλεσμα του Shell)
            Width = shell.Width; Height = shell.Height; WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = Brushes.Transparent;
            WindowStartupLocation = main != null && main.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen; if (main != null && main.IsVisible) Owner = main;
            var content = shell.Content; shell.Content = null; Content = content;

            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new Grid { Margin = new Thickness(26, 22, 26, 6) }; head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition());
            var logo = new AnimatedLogo { Width = 96, Height = 96 }; head.Children.Add(logo);
            var heading = new StackPanel { Margin = new Thickness(16, 6, 0, 0), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(heading, 1);
            heading.Children.Add(SuiteUi.Text("Opsis Media Player Pro", 26, bold: true, wrap: false));
            heading.Children.Add(SuiteUi.Text(SuiteUi.T("Έκδοση ", "Version ") + _version, 13, "MutedBrush"));
            _count = SuiteUi.Text("", 12, "MutedBrush"); heading.Children.Add(_count); head.Children.Add(heading);
            g.Children.Add(head);

            var mid = new StackPanel { Margin = new Thickness(30, 8, 30, 8) }; Grid.SetRow(mid, 1);
            _title = SuiteUi.Text("", 20, "AccentBrush", true); _body = SuiteUi.Text("", 14.5); _body.Margin = new Thickness(0, 10, 0, 0); _body.LineHeight = 22;
            mid.Children.Add(_title); mid.Children.Add(_body); g.Children.Add(mid);

            var foot = new Grid { Margin = new Thickness(26, 6, 26, 20) }; Grid.SetRow(foot, 2);
            foot.ColumnDefinitions.Add(new ColumnDefinition()); foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel(); _never = new CheckBox { Content = SuiteUi.T("Να μην εμφανιστεί ξανά", "Don't show again"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
            left.Children.Add(_dots); left.Children.Add(_never); foot.Children.Add(left);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom }; Grid.SetColumn(buttons, 1);
            _skip = new Button { Content = SuiteUi.T("Παράλειψη", "Skip"), Margin = new Thickness(0, 0, 8, 0), MinWidth = 90 };
            _back = new Button { Content = SuiteUi.T("Πίσω", "Back"), Margin = new Thickness(0, 0, 8, 0), MinWidth = 80 };
            _next = new Button { MinWidth = 110, IsDefault = true };
            buttons.Children.Add(_skip); buttons.Children.Add(_back); buttons.Children.Add(_next); foot.Children.Add(buttons); g.Children.Add(foot);

            _skip.Click += async (s, e) => await CloseAsync(false);
            _back.Click += (s, e) => Go(_i - 1);
            _next.Click += async (s, e) => { if (_i >= _pages.Count - 1) await CloseAsync(true); else Go(_i + 1); };
            PreviewKeyDown += async (s, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; await CloseAsync(false); }
                else if (e.Key == Key.Right) { e.Handled = true; if (_i < _pages.Count - 1) Go(_i + 1); }
                else if (e.Key == Key.Left) { e.Handled = true; Go(_i - 1); }
            };
            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button) { try { DragMove(); } catch (InvalidOperationException) { } } };
            Loaded += (s, e) =>
            {
                Focus(); _next.Focus();
                if (!SuiteUi.LowAnimations) { Opacity = 0; BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))); }
            };
            BuildDots(); Go(0, false);
        }

        public int PageIndex => _i;
        public int PageCount => _pages.Count;
        public void Go(int i, bool animate = true)
        {
            i = Math.Max(0, Math.Min(_pages.Count - 1, i)); bool changed = i != _i; _i = i;
            _title.Text = _pages[i].Title; _body.Text = _pages[i].Body;
            _count.Text = (i + 1) + " / " + _pages.Count;
            _back.IsEnabled = i > 0; _next.Content = i >= _pages.Count - 1 ? SuiteUi.T("Τέλος", "Finish") : SuiteUi.T("Επόμενο", "Next");
            _skip.Visibility = i >= _pages.Count - 1 ? Visibility.Collapsed : Visibility.Visible;
            BuildDots();
            if (animate && changed && !SuiteUi.LowAnimations)
            {
                var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                _title.BeginAnimation(OpacityProperty, a); _body.BeginAnimation(OpacityProperty, a);
            }
        }

        private void BuildDots()
        {
            _dots.Children.Clear();
            for (int k = 0; k < _pages.Count; k++)
            {
                var d = new System.Windows.Shapes.Ellipse { Width = k == _i ? 22 : 8, Height = 8, Margin = new Thickness(3, 0, 3, 8) };
                var geom = new System.Windows.Shapes.Rectangle { Width = k == _i ? 22 : 8, Height = 8, RadiusX = 4, RadiusY = 4, Margin = new Thickness(3, 0, 3, 8) };
                geom.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, k == _i ? "AccentBrush" : "HoverBrush"); _dots.Children.Add(geom);
            }
            _dots.HorizontalAlignment = HorizontalAlignment.Left;
        }

        /// <summary>Κλείνει με animation προς το εικονίδιο του κύριου παραθύρου και αποθηκεύει «δεν θα εμφανιστεί ξανά»/την έκδοση που είδε ο χρήστης.</summary>
        public async Task CloseAsync(bool finished)
        {
            if (_closing) return; _closing = true; Finished = finished;
            _store.Set("welcome.shown", MajorMinor(_version));
            if (_never.IsChecked == true) _store.Set("welcome.never", "1");
            await SuiteUi.MinimiseAsync(this, SuiteUi.IconTarget(_main), MinimiseMs);
        }

        public static string MajorMinor(string v) { if (SemVer.TryParse(v, out var n, out _)) return n[0] + "." + n[1]; return v; }
    }

    // ===============================================================================================================================
    //  «Οι εφαρμογές της οικογένειας Opsis»
    // ===============================================================================================================================
    public sealed class SuiteWindow : Window
    {
        private readonly Window _main; private readonly IHttp _http; private readonly IKeyValueStore _store; private readonly SuiteCatalogue _cat; private readonly string _selfId, _selfVersion;
        private readonly Dictionary<string, (TextBlock State, Button Open, Button Install)> _cards = new Dictionary<string, (TextBlock, Button, Button)>();
        private readonly Dictionary<string, (InstallState St, ReleaseDto Latest)> _info = new Dictionary<string, (InstallState, ReleaseDto)>();
        private bool _closing;
        public int MinimiseMs { get; set; } = 420;
        public IInstallProbe Probe { get; set; } = new RealInstallProbe();

        public SuiteWindow(Window main, IHttp http, IKeyValueStore store, SuiteCatalogue catalogue, string selfId, string selfVersion)
        {
            _main = main; _http = http; _store = store; _cat = catalogue; _selfId = selfId; _selfVersion = selfVersion;
            var shell = SuiteUi.Shell(main, 960, 680, out var g);
            Width = shell.Width; Height = shell.Height; WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Background = Brushes.Transparent;
            WindowStartupLocation = main != null && main.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen; if (main != null && main.IsVisible) Owner = main;
            var content = shell.Content; shell.Content = null; Content = content;

            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var head = new DockPanel { Margin = new Thickness(26, 18, 26, 6), LastChildFill = true };
            var close = new Button { Content = SuiteUi.T("Κλείσιμο", "Close"), MinWidth = 90, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
            var titles = new StackPanel();
            titles.Children.Add(SuiteUi.Text(SuiteUi.T("Οι εφαρμογές της οικογένειας Opsis", "The Opsis family"), 24, bold: true, wrap: false));
            titles.Children.Add(SuiteUi.Text(SuiteUi.T("Τι κάνει η καθεμία, ποιες έχεις και πώς τις παίρνεις. Οι λήψεις ελέγχονται με SHA-256.", "What each one does, which ones you have and how to get them. Downloads are checked with SHA-256."), 13, "MutedBrush"));
            head.Children.Add(titles); g.Children.Add(head);

            var wrap = new WrapPanel { Margin = new Thickness(18, 6, 18, 6) };
            foreach (var app in _cat.Apps) wrap.Children.Add(BuildCard(app));
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = wrap }; Grid.SetRow(scroll, 1); g.Children.Add(scroll);
            var foot = SuiteUi.Text(SuiteUi.T("Ανιχνεύεται μόνο ανάγνωση (registry/φάκελοι)· τίποτα δεν εγκαθίσταται χωρίς τη δική σου επιβεβαίωση.", "Detection is read-only (registry/folders); nothing is installed without your confirmation."), 11.5, "MutedBrush");
            foot.Margin = new Thickness(26, 4, 26, 14); Grid.SetRow(foot, 2); g.Children.Add(foot);

            close.Click += async (s, e) => await CloseAsync();
            PreviewKeyDown += async (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; await CloseAsync(); } };
            Loaded += async (s, e) =>
            {
                if (!SuiteUi.LowAnimations) { Opacity = 0; BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))); }
                await RefreshStatesAsync();
            };
        }

        private FrameworkElement BuildCard(SuiteApp app)
        {
            string L = SuiteUi.L; bool me = app.Id == _selfId;
            var card = new Border { Width = 420, Margin = new Thickness(8), Padding = new Thickness(16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "BgBrush"); card.SetResourceReference(Border.BorderBrushProperty, "HoverBrush");
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(me ? (UIElement)new AnimatedLogo { Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Top } : AppBadge(app));
            var col = new StackPanel { Margin = new Thickness(14, 0, 0, 0) }; Grid.SetColumn(col, 1);
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(SuiteUi.Text(app.Name, 17, bold: true, wrap: false));
            if (me) { var tag = SuiteUi.Text("  " + SuiteUi.T("Είσαι εδώ", "You are here"), 12, "AccentBrush", true, false); tag.VerticalAlignment = VerticalAlignment.Center; nameRow.Children.Add(tag); }
            col.Children.Add(nameRow);
            col.Children.Add(SuiteUi.Text(app.TaglineFor(L), 12.5, "MutedBrush"));
            foreach (var b in app.CapabilitiesFor(L).Take(6)) { var t = SuiteUi.Text("•  " + b, 12.5); t.Margin = new Thickness(0, 3, 0, 0); col.Children.Add(t); }
            var state = SuiteUi.Text(SuiteUi.T("Έλεγχος…", "Checking…"), 12.5, "AccentBrush", true); state.Margin = new Thickness(0, 10, 0, 6); col.Children.Add(state);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var open = new Button { Content = SuiteUi.T("Άνοιγμα", "Open"), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
            var install = new Button { Content = SuiteUi.T("Λήψη", "Download"), Margin = new Thickness(0, 0, 8, 0), Visibility = me ? Visibility.Collapsed : Visibility.Visible };
            var details = new Button { Content = SuiteUi.T("Λεπτομέρειες", "Details") };
            open.Click += (s, e) => OpenApp(app); install.Click += async (s, e) => await InstallAsync(app, install);
            details.Click += (s, e) => SuiteUi.OpenUrl(app.RepoUrl(_cat.Owner));
            row.Children.Add(open); row.Children.Add(install); row.Children.Add(details); col.Children.Add(row);
            g.Children.Add(col); card.Child = g;
            _cards[app.Id] = (state, open, install);
            card.MouseEnter += (s, e) => { if (!SuiteUi.LowAnimations) card.BeginAnimation(OpacityProperty, new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(250))); };
            return card;
        }

        /// <summary>Κινούμενο εικονίδιο για τις εφαρμογές που δεν έχουμε το πραγματικό τους: δαχτυλίδι που γυρίζει γύρω από το αρχικό της εφαρμογής.</summary>
        /// <summary>Το ΠΡΑΓΜΑΤΙΚΟ εικονίδιο της εφαρμογής (ενσωματωμένο στο exe, μεγαλύτερο frame του .ico ώστε να είναι καθαρό στο 100/150/200 %), ή null αν δεν υπάρχει.</summary>
        internal static System.Windows.Media.Imaging.BitmapSource EmbeddedIcon(string appId)
        {
            try
            {
                using var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("suite.icon." + appId + ".ico"); if (s == null) return null;
                var dec = new System.Windows.Media.Imaging.IconBitmapDecoder(s, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var best = dec.Frames.OrderByDescending(f => f.PixelWidth).First(); best.Freeze(); return best;
            }
            catch { return null; }
        }

        private static UIElement AppBadge(SuiteApp app)
        {
            var ico = EmbeddedIcon(app.Id);
            if (ico != null) { var im = new System.Windows.Controls.Image { Width = 64, Height = 64, Source = ico, VerticalAlignment = VerticalAlignment.Top, SnapsToDevicePixels = true }; RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality); return im; }
            var hue = Math.Abs(app.Id.GetHashCode()) % 360;
            var color = FromHsv(hue, 0.65, 0.95);
            var grid = new Grid { Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Top };
            var ring = new System.Windows.Shapes.Ellipse { Width = 62, Height = 62, StrokeThickness = 3.5, StrokeDashArray = new DoubleCollection { 4, 2 }, Stroke = new SolidColorBrush(color), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform() };
            var disc = new System.Windows.Shapes.Ellipse { Width = 48, Height = 48, Fill = new LinearGradientBrush(Color.FromRgb(0x1E, 0x2F, 0x55), Color.FromRgb(0x0B, 0x14, 0x26), 90) };
            var letter = new TextBlock { Text = app.Name.Replace("Opsis ", "").Substring(0, 1).ToUpperInvariant(), FontSize = 24, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(ring); grid.Children.Add(disc); grid.Children.Add(letter);
            if (!SuiteUi.LowAnimations) ((RotateTransform)ring.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(9 + hue % 5)) { RepeatBehavior = RepeatBehavior.Forever });
            return grid;
        }

        private static Color FromHsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c; double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; } else if (h < 120) { r = x; g = c; b = 0; } else if (h < 180) { r = 0; g = c; b = x; } else if (h < 240) { r = 0; g = x; b = c; } else if (h < 300) { r = x; g = 0; b = c; } else { r = c; g = 0; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        /// <summary>Κατάσταση κάθε εφαρμογής: ανίχνευση (μόνο ανάγνωση) και, για τις εγκατεστημένες, το τελευταίο release για «Διαθέσιμη ενημέρωση».</summary>
        public async Task RefreshStatesAsync()
        {
            foreach (var app in _cat.Apps)
            {
                var c = _cards[app.Id];
                var st = await Task.Run(() => app.Id == _selfId ? new InstallState { Installed = true, Version = _selfVersion } : InstallDetector.Detect(app, Probe));
                ReleaseDto latest = null;
                if (st.Installed && app.Id != _selfId) latest = await LatestAsync(app);
                _info[app.Id] = (st, latest);
                string text;
                if (app.Id == _selfId) text = SuiteUi.T("Εγκατεστημένο · v", "Installed · v") + _selfVersion;
                else if (!st.Installed) text = SuiteUi.T("Δεν είναι εγκατεστημένο", "Not installed");
                else if (latest != null && InstallDetector.UpdateAvailable(st, latest.Tag)) text = SuiteUi.T("Διαθέσιμη ενημέρωση: ", "Update available: ") + latest.Tag + (st.Version.Length > 0 ? " (" + SuiteUi.T("έχεις ", "you have ") + st.Version + ")" : "");
                else text = SuiteUi.T("Εγκατεστημένο", "Installed") + (st.Version.Length > 0 ? " · v" + st.Version : "");
                c.State.Text = text;
                bool canOpen = st.Installed && st.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && app.Id != _selfId;
                c.Open.Visibility = canOpen ? Visibility.Visible : Visibility.Collapsed;
                if (app.Id != _selfId) c.Install.Content = !st.Installed ? SuiteUi.T("Λήψη", "Download") : latest != null && InstallDetector.UpdateAvailable(st, latest.Tag) ? SuiteUi.T("Λήψη ενημέρωσης", "Download update") : SuiteUi.T("Λήψη ξανά", "Download again");
            }
        }

        private async Task<ReleaseDto> LatestAsync(SuiteApp app)
        {
            string key = "rel." + app.Repo;
            var r = await _http.GetAsync($"https://api.github.com/repos/{_cat.Owner}/{app.Repo}/releases/latest", _store.Get(key + ".etag"), TimeSpan.FromSeconds(8));
            string body = r.Status == 304 ? _store.Get(key + ".body") : r.Ok ? r.Body : null;
            if (r.Ok) { _store.Set(key + ".etag", r.ETag); _store.Set(key + ".body", r.Body.Length < 200_000 ? r.Body : null); }
            var rel = ReleaseDto.Parse(body); return rel == null || rel.Draft || rel.Prerelease ? null : rel;
        }

        private void OpenApp(SuiteApp app)
        {
            if (!_info.TryGetValue(app.Id, out var i) || string.IsNullOrEmpty(i.St.Path) || !File.Exists(i.St.Path)) return;
            try { if (TrustedExe.IsLaunchable(i.St.Path)) Process.Start(new ProcessStartInfo(i.St.Path) { UseShellExecute = true }); } catch (Exception) { }
        }

        /// <summary>Λήψη: επιβεβαίωση με όνομα/μέγεθος/πηγή, επαλήθευση SHA-256, ΑΡΝΗΣΗ σε ασυμφωνία. Χωρίς checksum ή asset ανοίγει μόνο τη σελίδα του release. Δεν εκτελεί τίποτα χωρίς ερώτηση.</summary>
        private async Task InstallAsync(SuiteApp app, Button btn)
        {
            btn.IsEnabled = false;
            try
            {
                var rel = (_info.TryGetValue(app.Id, out var i) ? i.Latest : null) ?? await LatestAsync(app);
                if (rel == null) { SuiteUi.OpenUrl(app.RepoUrl(_cat.Owner) + "/releases/latest"); return; }
                var asset = SafeInstaller.PickAsset(rel, app.InstallerAsset);
                if (asset == null) { SuiteUi.OpenUrl(rel.Url); return; }
                var expected = await SafeInstaller.ExpectedSha256Async(_http, rel, app, asset);
                if (expected == null)
                {
                    MessageBox.Show(this, SuiteUi.T("Δεν υπάρχει checksum για να επαληθευτεί το αρχείο, οπότε δεν θα κατέβει αυτόματα. Ανοίγω τη σελίδα της έκδοσης.", "There is no checksum to verify the file against, so it will not be downloaded automatically. Opening the release page."), app.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                    SuiteUi.OpenUrl(rel.Url); return;
                }
                string size = asset.Size > 0 ? (asset.Size / 1048576.0).ToString("0.0") + " MB" : "?";
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
                var ask = MessageBox.Show(this, SuiteUi.T("Να κατέβει ο installer;\n\nΑρχείο: ", "Download the installer?\n\nFile: ") + asset.Name + "\n" + SuiteUi.T("Μέγεθος: ", "Size: ") + size + "\n" + SuiteUi.T("Πηγή: ", "Source: ") + new Uri(asset.Url).Host + "\n" + "SHA-256: " + expected.Substring(0, 16) + "…\n" + SuiteUi.T("Προορισμός: ", "Destination: ") + dir + "\n\n" + SuiteUi.T("Δεν θα εκτελεστεί αυτόματα.", "It will not be run automatically."), app.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (ask != MessageBoxResult.Yes) return;
                btn.Content = SuiteUi.T("Λήψη…", "Downloading…");
                var res = await SafeInstaller.DownloadVerifiedAsync(_http, asset, expected, dir);
                if (res.Status == DownloadStatus.Ok)
                {
                    var run = MessageBox.Show(this, SuiteUi.T("Κατέβηκε και επαληθεύτηκε (SHA-256).\n\n", "Downloaded and verified (SHA-256).\n\n") + res.Path + "\n\n" + SuiteUi.T("Να ανοίξει ο φάκελος;", "Open the folder?"), app.Name, MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
                    if (run == MessageBoxResult.Yes) { try { Process.Start(TrustedExe.Explorer, "/select,\"" + res.Path + "\""); } catch (Exception) { } }
                }
                else if (res.Status == DownloadStatus.Mismatch) MessageBox.Show(this, SuiteUi.T("ΑΠΟΡΡΙΦΘΗΚΕ: το SHA-256 του αρχείου δεν ταιριάζει με το αναμενόμενο. Το αρχείο διαγράφηκε.", "REFUSED: the file's SHA-256 does not match the expected value. The file was deleted."), app.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                else MessageBox.Show(this, SuiteUi.T("Η λήψη απέτυχε. Δοκίμασε από τη σελίδα της έκδοσης.", "The download failed. Try the release page."), app.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { btn.IsEnabled = true; await RefreshStatesAsync(); }
        }

        public async Task CloseAsync()
        {
            if (_closing) return; _closing = true;
            await SuiteUi.MinimiseAsync(this, SuiteUi.IconTarget(_main), MinimiseMs);
        }
    }
}
