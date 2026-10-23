using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MediaPlayerPro
{
    // `OpsisMediaPlayerPro.exe --ui-demo <αρχείο>`: δείχνει όλα τα δευτερεύοντα παράθυρα (στατιστικά, ήχος, βοήθεια, λίστα επιλογής, διάλογος) πρώτα σε σκούρα και μετά σε φωτεινή
    // λειτουργία, 6 δευτερόλεπτα στη καθεμία, για να τα δει κανείς (ή να τα φωτογραφίσει ένα script) και να ελεγχθεί ότι τα γράμματα φαίνονται σωστά.
    public partial class MainWindow
    {
        void StartUiDemo()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += async (s, e) =>
            {
                t.Stop();
                string original = mode;
                Topmost = true; Left = 20; Top = 700; Width = 1100; Height = 600;            // πάνω από ό,τι άλλο είναι ανοιχτό, για να φωτογραφηθούν τα παράθυρα
                foreach (var m in new[] { "dark", "light" })
                {
                    mode = m; ApplySkin(curSkin, false);
                    var wins = new List<Window>();
                    Do("stats");
                    ShowAudio();
                    var help = new HelpWindow { Owner = this, FontFamily = FontFamily, Left = 20, Top = 60, Width = 560, Height = 620, WindowStartupLocation = WindowStartupLocation.Manual }; help.Show(); wins.Add(help);

                    var pick = new Window { Owner = this, Title = "Επιλογή υποτίτλων", Width = 560, Height = 300, Left = 600, Top = 60, WindowStartupLocation = WindowStartupLocation.Manual };
                    var lb = new ListBox { Margin = new Thickness(10), SelectedIndex = 0, ItemsSource = new[] { "[Podnapisi] el   Show.S01E02.720p.HDTV   ★52.0   ✔ ακριβής αντιστοίχιση", "[OpenSubtitles] el   Show S01E02 WEB-DL   ★33.5", "[Podnapisi] en   Show.S01E02   ★20.1" } };
                    var ok = new Button { Content = "Λήψη", IsDefault = true, Width = 90, Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right };
                    var dp = new DockPanel(); DockPanel.SetDock(ok, Dock.Bottom); dp.Children.Add(ok); dp.Children.Add(lb); pick.Content = dp; pick.Show(); wins.Add(pick);

                    var ask = new Window { Owner = this, Title = "Μετάβαση σε χρόνο (π.χ. 1:23:45)", Width = 340, Height = 135, Left = 600, Top = 400, WindowStartupLocation = WindowStartupLocation.Manual, ResizeMode = ResizeMode.NoResize };
                    var sp = new StackPanel(); sp.Children.Add(new TextBox { Text = "00:01:23", Margin = new Thickness(12, 14, 12, 6) });
                    sp.Children.Add(new Button { Content = "OK", IsDefault = true, Width = 70, Margin = new Thickness(0, 4, 12, 0), HorizontalAlignment = HorizontalAlignment.Right }); ask.Content = sp; ask.Show(); wins.Add(ask);

                    await System.Threading.Tasks.Task.Delay(6500);
                    foreach (var w in wins) w.Close();
                    foreach (Window w in Application.Current.Windows.Cast<Window>().Where(x => x != this).ToList()) w.Close();
                    await System.Threading.Tasks.Task.Delay(400);
                }
                mode = original; ApplySkin(curSkin, false);
                Environment.Exit(0);
            };
            t.Start();
        }
    }
}
