using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace MediaPlayerPro
{
    public partial class MainWindow
    {
        bool menuShown = true;

        void ToggleMenu()
        {
            menuShown = !menuShown;
            var st = (System.Windows.Media.ScaleTransform)MenuBar.LayoutTransform;
            var t = TimeSpan.FromMilliseconds(220);
            if (menuShown) MenuBar.Visibility = Visibility.Visible;
            var h = new DoubleAnimation(menuShown ? 1 : 0, t) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            if (!menuShown) h.Completed += (s, e) => { if (!menuShown) MenuBar.Visibility = Visibility.Collapsed; };
            st.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, h);
            MenuBar.BeginAnimation(OpacityProperty, new DoubleAnimation(menuShown ? 1 : 0, t));
            Say(menuShown ? "Μενού: εμφάνιση" : "Μενού: απόκρυψη (Ctrl+M για εμφάνιση)");
        }

        void ShowHelp(int tab) { var w = new HelpWindow { Owner = this, FontFamily = FontFamily };
            foreach (var k in new[] { "BgBrush", "BarBrush", "FgBrush", "MutedBrush", "AccentBrush", "HoverBrush" }) w.Resources[k] = Resources[k]; w.Tabs.SelectedIndex = tab; w.ShowDialog(); }
    }
}
