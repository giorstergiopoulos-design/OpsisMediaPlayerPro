using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MediaPlayerPro
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // ΚΑΘΕ παράθυρο (και όσα φτιάχνονται από κώδικα: στατιστικά, επιλογή υποτίτλων, διάλογοι) παίρνει τη γραμμή τίτλου του θέματος μόλις εμφανιστεί
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, ev) => ThemeChrome.Apply((Window)s)));
            base.OnStartup(e);
        }
    }

    /// <summary>
    /// Η γραμμή τίτλου και το πλαίσιο των παραθύρων ακολουθούν το θέμα: σκούρο/φωτεινό chrome των Windows (DWM) και χρώματα γραμμής τίτλου/κειμένου
    /// από τα χρώματα του θέματος (Windows 11 22H2+, σε παλαιότερα Windows αγνοούνται σιωπηλά).
    /// </summary>
    static class ThemeChrome
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        static Color? BrushColor(string key) => (Application.Current?.Resources[key] as SolidColorBrush)?.Color;
        static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

        /// <summary>Φωτεινότητα 0..1 (ίδιος τύπος με τη λειτουργία light/dark της εφαρμογής).</summary>
        public static double Luma(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

        public static void Apply(Window w)
        {
            try
            {
                var h = new WindowInteropHelper(w).Handle; if (h == IntPtr.Zero) return;
                var bar = BrushColor("BarBrush"); var fg = BrushColor("FgBrush"); var edge = BrushColor("HoverBrush");
                if (bar == null || fg == null) return;
                int dark = Luma(bar.Value) < 0.5 ? 1 : 0, caption = ColorRef(bar.Value), text = ColorRef(fg.Value);
                DwmSetWindowAttribute(h, 20, ref dark, sizeof(int));                 // DWMWA_USE_IMMERSIVE_DARK_MODE: κουμπιά/πλαίσιο σε σκούρο ή φωτεινό
                DwmSetWindowAttribute(h, 35, ref caption, sizeof(int));              // DWMWA_CAPTION_COLOR
                DwmSetWindowAttribute(h, 36, ref text, sizeof(int));                 // DWMWA_TEXT_COLOR
                if (edge != null) { int b = ColorRef(edge.Value); DwmSetWindowAttribute(h, 34, ref b, sizeof(int)); }   // DWMWA_BORDER_COLOR
            }
            catch (Exception) { }
        }

        /// <summary>Μετά από αλλαγή θέματος / λειτουργίας: ανανέωση όλων των ανοιχτών παραθύρων.</summary>
        public static void ApplyAll()
        {
            if (Application.Current == null) return;
            foreach (Window w in Application.Current.Windows) Apply(w);
        }
    }
}
