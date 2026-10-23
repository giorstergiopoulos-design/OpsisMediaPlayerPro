using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MediaPlayerPro
{
    public class Skin
    {
        public string Name { get; set; }
        public string Bg { get; set; } = "#111214";
        public string Bar { get; set; } = "#181A1D";
        public string Fg { get; set; } = "#E6E8EB";
        public string Muted { get; set; } = "#9AA0A6";
        public string Accent { get; set; } = "#3B9EFF";
        public string Hover { get; set; } = "#2A2D33";
        public double Radius { get; set; } = 20;
        public string Font { get; set; }   // προαιρετικό, π.χ. "Consolas"
    }

    public partial class MainWindow
    {
        const string DefFont = "Segoe UI Variable Text, Segoe UI";
        static readonly Skin[] BuiltIn =
        {
            new Skin { Name = "Σκούρο (MPC)" },
            new Skin { Name = "AMOLED", Bg = "#000000", Bar = "#0A0A0A", Hover = "#1C1C1C" },
            new Skin { Name = "Μπλε", Bg = "#0B1426", Bar = "#101C36", Fg = "#E6EEFF", Muted = "#8CA3C7", Hover = "#1B2E58" },
            new Skin { Name = "Πράσινο", Bg = "#0C1712", Bar = "#12211A", Fg = "#E4F2E8", Muted = "#86A895", Accent = "#34D399", Hover = "#1C3327" },
            new Skin { Name = "Ωκεανός", Bg = "#06141F", Bar = "#0B2233", Fg = "#DFF3FF", Muted = "#7FA7BF", Accent = "#00C2FF", Hover = "#12384F", Radius = 14 },
            new Skin { Name = "Ηλιοβασίλεμα", Bg = "#1B0F0F", Bar = "#2A1616", Fg = "#FFE9D6", Muted = "#C49A86", Accent = "#FF7A2F", Hover = "#3D2020", Radius = 6 },
            new Skin { Name = "Βελούδο", Bg = "#140B1F", Bar = "#1E1230", Fg = "#EDE4FA", Muted = "#A795C4", Accent = "#A78BFA", Hover = "#2D1B4A", Radius = 12 },
            new Skin { Name = "Γραφίτης", Bg = "#1C1C1E", Bar = "#2C2C2E", Fg = "#F2F2F7", Muted = "#98989F", Accent = "#0A84FF", Hover = "#3A3A3C", Radius = 10 },
            new Skin { Name = "Neon", Bg = "#0D0221", Bar = "#1B0B3A", Fg = "#F5E1FF", Muted = "#B08CD6", Accent = "#FF2BD6", Hover = "#2E1065" },
            new Skin { Name = "Winamp Classic", Bg = "#000000", Bar = "#1A1A1A", Fg = "#00FF00", Muted = "#00A000", Accent = "#00FF00", Hover = "#003300", Radius = 0 },
            new Skin { Name = "Retro Terminal", Bg = "#0A0F0A", Bar = "#101810", Fg = "#7CFF7C", Muted = "#3FA03F", Accent = "#7CFF7C", Hover = "#1A2E1A", Radius = 0, Font = "Consolas" },
            new Skin { Name = "Υψηλή αντίθεση", Bg = "#000000", Bar = "#000000", Fg = "#FFFFFF", Muted = "#FFFF00", Accent = "#FFFF00", Hover = "#333300", Radius = 0 },
            new Skin { Name = "Φωτεινό", Bg = "#F2F3F5", Bar = "#E3E5E8", Fg = "#111214", Muted = "#5F6368", Accent = "#1A73E8", Hover = "#D0D4DA", Radius = 8 },
            new Skin { Name = "Σέπια", Bg = "#F4ECD8", Bar = "#E8DCC0", Fg = "#3B2F1E", Muted = "#7A6A50", Accent = "#B5651D", Hover = "#D9C9A3", Radius = 8 },
        };
        static readonly (string n, string hex)[] Accents =
        {
            ("Μπλε", "#3B9EFF"), ("Κυανό", "#00C2FF"), ("Πράσινο", "#34D399"), ("Κίτρινο", "#FACC15"),
            ("Πορτοκαλί", "#FF7A2F"), ("Κόκκινο", "#F43F5E"), ("Ροζ", "#FF2BD6"), ("Μωβ", "#8B5CF6"),
        };
        static readonly string AppDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaPlayerPro");
        readonly DispatcherTimer clickT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        readonly DispatcherTimer osdT = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        string curSkin = "Σκούρο (MPC)", accentOverride, mode = "dark"; bool autoSkin => mode == "auto";

        List<Skin> AllSkins()
        {
            var l = new List<Skin>(BuiltIn);
            foreach (var dir in new[] { System.IO.Path.Combine(AppContext.BaseDirectory, "Skins"), System.IO.Path.Combine(AppDir, "Skins") })
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.json"))
                        try { var k = JsonSerializer.Deserialize<Skin>(File.ReadAllText(f)); if (k != null) { if (string.IsNullOrEmpty(k.Name)) k.Name = System.IO.Path.GetFileNameWithoutExtension(f); l.Add(k); } } catch { }
            return l;
        }

        static bool SystemLight()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) return k != null && k.GetValue("AppsUseLightTheme") is int v && v == 1; }
            catch { return false; }
        }

        static double Luma(string hex) { var c = (Color)ColorConverter.ConvertFromString(hex); return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255; }

        void ApplySkin(string name, bool announce = true)
        {
            if (name == "@auto") { mode = "auto"; name = curSkin; }
            var sk = AllSkins().FirstOrDefault(k => k.Name == name) ?? BuiltIn[0]; curSkin = sk.Name;
            bool wantLight = mode == "light" || (mode == "auto" && SystemLight());
            if (wantLight != (Luma(sk.Bg) > 0.5))   // light/dark mode: ίδιο θέμα (accent, σχήμα, γραμματοσειρά) με φωτεινή ή σκούρη παλέτα
            {
                var b = wantLight ? BuiltIn.First(k => k.Name == "Φωτεινό") : BuiltIn[0];
                sk = new Skin { Name = sk.Name, Bg = b.Bg, Bar = b.Bar, Fg = b.Fg, Muted = b.Muted, Hover = b.Hover, Accent = sk.Accent, Radius = sk.Radius, Font = sk.Font };
            }
            string acc = accentOverride ?? sk.Accent;
            if (mode == "auto" && accentOverride == null) { var c = SystemParameters.WindowGlassColor; acc = $"#{c.R:X2}{c.G:X2}{c.B:X2}"; }
            void S(string key, string hex) => Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            try
            {
                S("BgBrush", sk.Bg); S("BarBrush", sk.Bar); S("FgBrush", sk.Fg); S("MutedBrush", sk.Muted); S("AccentBrush", acc); S("HoverBrush", sk.Hover);
                Resources["BtnRadius"] = new CornerRadius(sk.Radius);
                FontFamily = new System.Windows.Media.FontFamily(string.IsNullOrEmpty(sk.Font) ? DefFont : sk.Font);
                Directory.CreateDirectory(AppDir);
                File.WriteAllText(System.IO.Path.Combine(AppDir, "skin.txt"), curSkin);
                File.WriteAllText(System.IO.Path.Combine(AppDir, "mode.txt"), mode);
                File.WriteAllText(System.IO.Path.Combine(AppDir, "accent.txt"), accentOverride ?? "");
                if (ModeBtn != null)
                {
                    ModeBtn.Content = mode == "light" ? "\uE706" : mode == "auto" ? "\uE771" : "\uE708";
                    ModeBtn.ToolTip = (mode == "dark" ? "Σκούρη λειτουργία" : mode == "light" ? "Φωτεινή λειτουργία" : "Αυτόματη λειτουργία (ακολουθεί τα Windows)") + " — κλικ για αλλαγή (Ctrl+Shift+L)";
                }
                if (announce) Say("Θέμα: " + curSkin + " • " + (mode == "dark" ? "σκούρη" : mode == "light" ? "φωτεινή" : "αυτόματη") + " λειτουργία");
            }
            catch { Say("Μη έγκυρο θέμα"); }
        }

        void NextSkin()
        {
            var l = AllSkins(); int i = l.FindIndex(k => k.Name == curSkin);
            ApplySkin(l[(i + 1) % l.Count].Name);
        }

        void LoadSkin()
        {
            LoadSide(); LoadAudio();
            clickT.Tick += (s, e) => { clickT.Stop(); Do("pp"); };
            osdT.Tick += (s, e) => { osdT.Stop(); if (Osd != null) Osd.Visibility = Visibility.Collapsed; };
            SystemEvents.UserPreferenceChanged += (s, e) => { if (autoSkin) Dispatcher.BeginInvoke(new Action(() => ApplySkin("@auto", false))); };
            try
            {
                var a = System.IO.Path.Combine(AppDir, "accent.txt"); if (File.Exists(a)) { var h = File.ReadAllText(a).Trim(); accentOverride = h.StartsWith("#") ? h : null; }
                var mf = System.IO.Path.Combine(AppDir, "mode.txt"); if (File.Exists(mf)) mode = File.ReadAllText(mf).Trim();
                var f = System.IO.Path.Combine(AppDir, "skin.txt"); ApplySkin(File.Exists(f) ? File.ReadAllText(f).Trim() : curSkin, false);
            }
            catch { }
        }

        void FillSkins()
        {
            SkinMenu.Items.Clear();
            // Η λειτουργία light / dark / αυτόματη ΔΕΝ είναι πια στο μενού: έχει δικό της κουμπί στη γραμμή ελέγχου (ModeBtn) και Ctrl+Shift+L.
            foreach (var k in AllSkins()) { var n = k.Name; var m = new MenuItem { Header = n, IsChecked = n == curSkin }; m.Click += (s, e) => ApplySkin(n); SkinMenu.Items.Add(m); }
            var ac = new MenuItem { Header = "Χρώμα έμφασης" };
            var def = new MenuItem { Header = "Από το θέμα", IsChecked = accentOverride == null }; def.Click += (s, e) => { accentOverride = null; ApplySkin(curSkin); }; ac.Items.Add(def);
            foreach (var (n, hex) in Accents)
            {
                var h = hex;
                var m = new MenuItem { Header = n, IsChecked = accentOverride == h, Icon = new System.Windows.Shapes.Rectangle { Width = 12, Height = 12, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)) } };
                m.Click += (s, e) => { accentOverride = h; ApplySkin(curSkin); }; ac.Items.Add(m);
            }
            SkinMenu.Items.Add(ac);
            var o = new MenuItem { Header = "Άνοιγμα φακέλου δικών μου θεμάτων…" };
            o.Click += (s, e) => { var d = System.IO.Path.Combine(AppDir, "Skins"); Directory.CreateDirectory(d); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + d + "\"") { UseShellExecute = true }); };
            SkinMenu.Items.Add(o);
            SideAuto.IsChecked = sideMode == "auto"; SidePin.IsChecked = sideMode == "pin"; SideHide.IsChecked = sideMode == "hide"; MetaToggle.IsChecked = LoadCfg().OnlineMeta;
        }

        // ---------- Overlay πάνω από το βίντεο: κλικ, ροδέλα, OSD ----------
        void ShowOsd(string t) { if (Osd == null) return; Osd.Text = t; Osd.Visibility = Visibility.Visible; osdT.Stop(); osdT.Start(); }

        void Video_Down(object s, MouseButtonEventArgs e)
        {
            ((UIElement)s).Focus();
            if (e.ClickCount >= 2) { clickT.Stop(); Do("full"); }
            else { clickT.Stop(); clickT.Start(); }
        }

        void Video_Wheel(object s, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control) Jump(e.Delta > 0 ? 5 : -5);
            else Vol.Value = Math.Max(0, Math.Min(200, Vol.Value + (e.Delta > 0 ? 5 : -5)));
            e.Handled = true;
        }
    }
}
