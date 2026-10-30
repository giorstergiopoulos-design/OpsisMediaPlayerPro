using System;
using System.Diagnostics;
using MediaPlayerPro.Conv;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Opsis.Suite;

namespace MediaPlayerPro
{
    /// <summary>Χωρίς δίκτυο (selftest / demo): κάθε αίτημα «αποτυγχάνει» σιωπηλά.</summary>
    sealed class OfflineHttp : IHttp
    {
        public Task<HttpResult> GetAsync(string url, string etag, TimeSpan timeout) => Task.FromResult(new HttpResult { Status = 0 });
        public Task<bool> DownloadAsync(string url, string destination, long maxBytes, TimeSpan timeout) => Task.FromResult(false);
    }

    // Καλωσόρισμα + οδηγός, «Οι εφαρμογές της οικογένειας Opsis» και ειδοποίηση νέας έκδοσης (toast μέσα στο παράθυρο — ο player δεν έχει εικονίδιο στο tray).
    public partial class MainWindow
    {
        const string GitHubOwner = "giorstergiopoulos-design", SelfRepo = "OpsisMediaPlayerPro", SelfId = "mediaplayer";
        IKeyValueStore suiteStore = new MemoryStore(); IHttp suiteHttp = new OfflineHttp(); UpdateChecker updater;
        UpdateInfo pendingToast; Border toast; DispatcherTimer toastTimer;
        bool suiteTestMode;

        static string AppVersion()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }

        static string EmbeddedCatalogue()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("suite.apps.json"))
            using (var r = new StreamReader(s)) return r.ReadToEnd();
        }

        void InitSuite()
        {
            var args = Environment.GetCommandLineArgs();
            suiteTestMode = args.Contains("--selftest") || args.Contains("--ui-demo") || args.Contains("--snapshots") || args.Contains("--suite-demo");
            if (!suiteTestMode)
            {
                suiteStore = new JsonFileStore(System.IO.Path.Combine(AppDir, "suite.json"));          // μόνο η έκδοση που είδε ο χρήστης, ETag και ρυθμίσεις — τίποτα προσωπικό
                suiteHttp = new RealHttp();
            }
            updater = new UpdateChecker(suiteHttp, suiteStore, () => DateTime.UtcNow, GitHubOwner, SelfRepo, AppVersion());
            if (args.Contains("--suite-demo")) { StartSuiteDemo(); return; }
            if (suiteTestMode) return;

            Loaded += async (s, e) => { await Task.Delay(1500); MaybeWelcome(); ReportFailedUpdate(); };
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };                     // πρώτος έλεγχος 30 s μετά την εκκίνηση, μετά κάθε 24 ώρες
            t.Tick += async (s, e) => { t.Interval = TimeSpan.FromHours(24); await CheckUpdateAsync(false); };
            t.Start();
        }

        // ---------------- καλωσόρισμα ----------------
        void MaybeWelcome()
        {
            if (suiteStore.Get("welcome.never") == "1") return;
            if (suiteStore.Get("welcome.shown") == WelcomeWindow.MajorMinor(AppVersion())) return;      // το έχει δει σε αυτή τη major/minor έκδοση
            ShowWelcome(auto: true);
        }

        WelcomeWindow ShowWelcome(bool auto)
        {
            var w = new WelcomeWindow(this, suiteStore, AppVersion());
            w.Closed += async (s, e) =>
            {
                if (auto && w.Finished && suiteStore.Get("suite.offered") != "1") { suiteStore.Set("suite.offered", "1"); await ShowSuiteAsync(); }     // προσφέρεται ΜΙΑ φορά μετά τον οδηγό
            };
            w.Show(); return w;
        }

        async Task<SuiteWindow> ShowSuiteAsync()
        {
            var cat = await CatalogueProvider.LoadAsync(suiteHttp, suiteStore, () => DateTime.UtcNow, System.IO.Path.Combine(AppDir, "suite-apps.json"), EmbeddedCatalogue());
            var w = new SuiteWindow(this, suiteHttp, suiteStore, cat, SelfId, AppVersion());
            w.Show(); return w;
        }

        // ---------------- νέα έκδοση ----------------
        async Task CheckUpdateAsync(bool force)
        {
            UpdateInfo info;
            try { info = await updater.CheckAsync(force); } catch (Exception) { info = null; }
            if (info == null) { if (force) Say(SuiteUi.T("Δεν υπάρχει νεότερη έκδοση (ή δεν ήταν δυνατός ο έλεγχος)", "No newer version (or the check was not possible)")); return; }
            pendingToast = info; TryShowToast();
        }

        void TryShowToast()
        {
            if (pendingToast == null) return;
            if (full) { toastTimer?.Stop(); toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) }; toastTimer.Tick += (s, e) => { toastTimer.Stop(); TryShowToast(); }; toastTimer.Start(); return; }   // όχι σε πλήρη οθόνη
            var info = pendingToast; pendingToast = null;
            ShowUpdateToast(info); updater.MarkNotified(info.Tag);                                   // μία φορά ανά έκδοση
        }

        static string UpdateLog => System.IO.Path.Combine(UpdateDir, "last-update.log");

        /// <summary>Αν η προηγούμενη σιωπηλή ενημέρωση απέτυχε, ο χρήστης το μαθαίνει στην επόμενη εκκίνηση (μία φορά).</summary>
        void ReportFailedUpdate()
        {
            try
            {
                if (!File.Exists(UpdateLog)) return;
                var last = File.ReadAllLines(UpdateLog).LastOrDefault(l => l.Trim().Length > 0) ?? "";
                if (last.Contains("FAILED")) Say(SuiteUi.T("Η τελευταία ενημέρωση απέτυχε: ", "The last update failed: ") + last.Substring(Math.Min(last.Length, 20)) + SuiteUi.T(" — δες ", " — see ") + UpdateLog);
                File.Move(UpdateLog, UpdateLog + ".old", true);
            }
            catch (Exception) { }
        }

        static string UpdateDir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpsisMediaPlayerPro", "update");

        void ShowUpdateToast(UpdateInfo info)
        {
            HideToast();
            var host = Osd?.Parent as Panel; if (host == null) return;                                 // μέσα στο overlay του βίντεο, αλλιώς το VideoView το καλύπτει
            var text = SuiteUi.Text(AppTitleText() + " " + info.Tag + SuiteUi.T(" είναι διαθέσιμη", " is available"), 14, bold: true, wrap: false);
            var sub = SuiteUi.Text(SuiteUi.T("Η εγκατάσταση γίνεται σιωπηλά με έλεγχο SHA-256 και η εφαρμογή ανοίγει ξανά.", "The update installs silently after a SHA-256 check and the app reopens."), 12, "MutedBrush");
            var install = new Button { Content = SuiteUi.T("Εγκατάσταση", "Install"), Margin = new Thickness(0, 8, 8, 0), IsDefault = true };
            var later = new Button { Content = SuiteUi.T("Αργότερα", "Later"), Margin = new Thickness(0, 8, 8, 0) };
            var skip = new Button { Content = SuiteUi.T("Παράλειψη έκδοσης", "Skip this version"), Margin = new Thickness(0, 8, 8, 0) };
            var open = new Button { Content = SuiteUi.T("Τι νέο υπάρχει", "What's new"), Margin = new Thickness(0, 8, 0, 0) };
            var row = new WrapPanel(); row.Children.Add(install); row.Children.Add(later); row.Children.Add(skip); row.Children.Add(open);
            var status = SuiteUi.Text("", 12, "MutedBrush");
            var col = new StackPanel(); col.Children.Add(text); col.Children.Add(sub); col.Children.Add(row); col.Children.Add(status);
            toast = new Border { Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 14, 18, 0), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = col, MaxWidth = 480 };
            toast.SetResourceReference(Border.BackgroundProperty, "BarBrush"); toast.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            toast.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.5 };
            Panel.SetZIndex(toast, 50);
            open.Click += (s, e) => SuiteUi.OpenUrl(info.Url);
            later.Click += (s, e) => HideToast();
            skip.Click += (s, e) => { updater.SkipVersion(info.Tag); HideToast(); };
            install.Click += async (s, e) =>
            {
                install.IsEnabled = later.IsEnabled = skip.IsEnabled = false; toastTimer?.Stop();
                status.Text = SuiteUi.T("Λήψη και έλεγχος SHA-256…", "Downloading and checking SHA-256…");
                DownloadResult r;
                try { r = await SelfUpdate.PrepareAsync(suiteHttp, info, "OpsisMediaPlayerPro-Setup.exe", UpdateDir); } catch (Exception ex) { r = new DownloadResult { Status = DownloadStatus.DownloadFailed, Message = ex.Message }; }
                if (r.Status == DownloadStatus.Ok)
                {
                    try
                    {
                        string script = SelfUpdate.BuildScript(r.Path, r.Actual, Environment.ProcessId, Environment.ProcessPath ?? "", logPath: UpdateLog);
                        Process.Start(new ProcessStartInfo(TrustedExe.System32(@"WindowsPowerShell\v1.0\powershell.exe")) { ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-Command", script }, UseShellExecute = false, CreateNoWindow = true });
                        Application.Current.Shutdown(); return;
                    }
                    catch (Exception ex) { status.Text = SuiteUi.T("Αποτυχία εκκίνησης: ", "Could not start the installer: ") + ex.Message; }
                }
                else status.Text = r.Status == DownloadStatus.Mismatch ? SuiteUi.T("Η λήψη απορρίφθηκε: το SHA-256 δεν ταιριάζει (το αρχείο διαγράφηκε).", "Download refused: the SHA-256 does not match (the file was deleted).")
                                 : r.Status == DownloadStatus.NoChecksum ? SuiteUi.T("Δεν υπάρχει SHA-256 για επαλήθευση — άνοιξε τη σελίδα της έκδοσης.", "No SHA-256 to verify against — open the release page.")
                                 : SuiteUi.T("Η λήψη απέτυχε — δοκίμασε «Τι νέο υπάρχει» για χειροκίνητη λήψη.", "The download failed — use \"What's new\" for a manual download.");
                later.IsEnabled = skip.IsEnabled = true; install.IsEnabled = true;
            };
            host.Children.Add(toast);
            if (!SuiteUi.LowAnimations)
            {
                toast.RenderTransform = new System.Windows.Media.TranslateTransform();
                ((System.Windows.Media.TranslateTransform)toast.RenderTransform).BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, new DoubleAnimation(80, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
                toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            }
            toastTimer?.Stop(); toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) }; toastTimer.Tick += (s, e) => HideToast(); toastTimer.Start();
        }

        static string AppTitleText() => "Opsis Media Player Pro";

        void HideToast()
        {
            toastTimer?.Stop();
            if (toast != null && toast.Parent is Panel p) p.Children.Remove(toast);
            toast = null;
        }

        // ---------------- μενού ----------------
        bool SuiteCmd(string c)
        {
            switch (c)
            {
                case "welcome": ShowWelcome(auto: false); return true;
                case "suite": _ = ShowSuiteAsync(); return true;
                case "updcheck": _ = CheckUpdateAsync(true); return true;
                case "updnotify": updater.NotifyEnabled = UpdNotifyItem.IsChecked; Say(updater.NotifyEnabled ? SuiteUi.T("Ειδοποιήσεις νέας έκδοσης: ενεργές", "New-version notifications: on") : SuiteUi.T("Ειδοποιήσεις νέας έκδοσης: ανενεργές", "New-version notifications: off")); return true;
            }
            return false;
        }

        // ---------------- έλεγχος / δείγμα ----------------
        /// <summary>Για το selftest: ανοίγει πραγματικά τα παράθυρα, ελέγχει σελίδες, αποθήκευση «έχει δει», κάρτες και toast. Χωρίς δίκτυο.</summary>
        async Task SuiteSelfTest(Action<string, bool, string> report)
        {
            try
            {
                var store = new MemoryStore(); var w = new WelcomeWindow(this, store, "1.4.0") { MinimiseMs = 0 }; w.Show();
                int pages = w.PageCount; for (int i = 0; i < pages; i++) w.Go(i, false);
                bool langOk = WelcomeWindow.Pages().All(p => p.Title.Length > 0 && p.Body.Length > 40);
                await w.CloseAsync(true);
                report("welcome guide: pages, finish, remembered version", pages >= 4 && pages <= 6 && w.Finished && store.Get("welcome.shown") == "1.4" && langOk, pages + " pages");

                var cat = await CatalogueProvider.LoadAsync(new OfflineHttp(), new MemoryStore(), () => DateTime.UtcNow, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opsis_suite_selftest_" + Guid.NewGuid().ToString("N") + ".json"), EmbeddedCatalogue());
                var sw = new SuiteWindow(this, new OfflineHttp(), new MemoryStore(), cat, SelfId, AppVersion()) { MinimiseMs = 0 }; sw.Show();
                await sw.RefreshStatesAsync();
                report("suite showcase: one card per app of the catalogue", cat.Apps.Count >= 7 && cat.Apps.Any(a => a.Id == SelfId), cat.Apps.Count + " apps");
                await sw.CloseAsync();

                ShowUpdateToast(new UpdateInfo { Tag = "v9.9.9", Name = "x", Url = "https://github.com/" + GitHubOwner + "/" + SelfRepo + "/releases/tag/v9.9.9" });
                bool shown = toast != null && toast.Parent is Panel; HideToast();
                report("update toast appears and hides", shown && toast == null, "");
            }
            catch (Exception ex) { report("suite windows", false, ex.GetType().Name + ": " + ex.Message); }
        }

        void StartSuiteDemo()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += async (s, e) =>
            {
                t.Stop(); Topmost = true; Left = 60; Top = 80; Width = 1100; Height = 640;
                await Task.Delay(1500);
                var w = new WelcomeWindow(this, new MemoryStore(), AppVersion()) { MinimiseMs = 3000, Topmost = true }; w.Show();
                await Task.Delay(4000); w.Go(w.PageCount - 1);
                await Task.Delay(4000); _ = w.CloseAsync(true);
                await Task.Delay(5500);
                var sw = new SuiteWindow(this, new OfflineHttp(), new MemoryStore(), await CatalogueProvider.LoadAsync(new OfflineHttp(), new MemoryStore(), () => DateTime.UtcNow, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opsis_suite_demo.json"), EmbeddedCatalogue()), SelfId, AppVersion()) { MinimiseMs = 3000, Topmost = true };
                sw.Show(); await Task.Delay(5000);
                _ = sw.CloseAsync();
                await Task.Delay(4500);
                ShowUpdateToast(new UpdateInfo { Tag = "v9.9.9", Name = "x", Url = "https://github.com/" + GitHubOwner + "/" + SelfRepo + "/releases/latest" });
                await Task.Delay(4500);
                Environment.Exit(0);
            };
            t.Start();
        }
    }
}
