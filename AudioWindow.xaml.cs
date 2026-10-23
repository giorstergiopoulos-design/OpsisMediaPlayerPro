using System;
using System.Windows;
using System.Windows.Controls;

namespace MediaPlayerPro
{
    public partial class AudioWindow : Window
    {
        readonly MainWindow m; readonly Slider[] bs = new Slider[10], es = new Slider[5]; readonly TextBlock[] bt = new TextBlock[10], et = new TextBlock[5];
        Slider ps; TextBlock pt; bool loading;
        static readonly string[] Freq = { "60", "170", "310", "600", "1K", "3K", "6K", "12K", "14K", "16K" };
        static readonly string[] EffN = { "Clarity", "Ambience", "Surround", "Dynamic Boost", "Bass Boost" };
        static string Db(double v) => v.ToString("+0.#;-0.#;0");

        public AudioWindow(MainWindow owner)
        {
            InitializeComponent(); m = owner; Owner = owner; FontFamily = owner.FontFamily;
            foreach (var k in new[] { "BgBrush", "BarBrush", "FgBrush", "MutedBrush", "AccentBrush", "HoverBrush" }) Resources[k] = owner.Resources[k];
            Build(); BuildChips(); Refresh(); Closed += (s, e) => m.AudioClosed();
        }

        TextBlock T(string text, double size, string res, bool bold = false)
        {
            var t = new TextBlock { Text = text, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
            t.SetResourceReference(TextBlock.ForegroundProperty, res); return t;
        }

        StackPanel Col(string label, Slider s, TextBlock val)
        {
            var p = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            s.Style = (Style)FindResource("VS"); s.Minimum = -12; s.Maximum = 12; s.TickFrequency = 0.5; s.IsSnapToTickEnabled = true; s.Height = 220; s.HorizontalAlignment = HorizontalAlignment.Center;
            p.Children.Add(val); p.Children.Add(s); p.Children.Add(T(label, 12, "FgBrush", true)); return p;
        }

        void Custom() { m.fx.Preset = "Προσαρμοσμένο"; foreach (RadioButton r in Chips.Children) r.IsChecked = false; }

        void Build()
        {
            for (int i = 0; i < 5; i++)
            {
                int k = i; var s = new Slider { Minimum = 0, Maximum = 10, TickFrequency = 0.5, IsSnapToTickEnabled = true }; es[k] = s; et[k] = T("0.0", 12, "MutedBrush");
                s.ValueChanged += (a, b) => { if (loading) return; m.fx.SetEffect(k, s.Value); et[k].Text = s.Value.ToString("0.0"); m.OnFxChanged(k >= 1 && k <= 3); Custom(); };
                var sp = new StackPanel { Margin = new Thickness(8, 0, 8, 0) }; sp.Children.Add(T(EffN[k], 12.5, "FgBrush", true)); sp.Children.Add(s); sp.Children.Add(et[k]); Effects.Children.Add(sp);
            }
            ps = new Slider(); pt = T("0", 12, "MutedBrush");
            ps.ValueChanged += (a, b) => { if (loading) return; m.fx.Pre = (float)ps.Value; pt.Text = Db(ps.Value); m.OnFxChanged(false); Custom(); };
            Bands.Children.Add(Col("Preamp", ps, pt));
            for (int i = 0; i < 10; i++)
            {
                int k = i; var s = new Slider(); bs[k] = s; bt[k] = T("0", 12, "MutedBrush");
                s.ValueChanged += (a, b) => { if (loading) return; m.fx.Bands[k] = (float)s.Value; bt[k].Text = Db(s.Value); m.OnFxChanged(false); Custom(); };
                Bands.Children.Add(Col(Freq[k], s, bt[k]));
            }
        }

        void BuildChips()
        {
            Chips.Children.Clear();
            foreach (var n in m.AllPresetNames())
            {
                var name = n; var r = new RadioButton { Content = name, Style = (Style)FindResource("Chip"), GroupName = "p", IsChecked = name == m.fx.Preset };
                r.Checked += (a, b) => { if (!loading) m.ApplyPreset(name); }; Chips.Children.Add(r);
            }
        }

        public void Refresh()
        {
            loading = true;
            OnBox.IsChecked = m.fx.On; ps.Value = m.fx.Pre; pt.Text = Db(m.fx.Pre);
            for (int i = 0; i < 10; i++) { bs[i].Value = m.fx.Bands[i]; bt[i].Text = Db(m.fx.Bands[i]); }
            for (int i = 0; i < 5; i++) { es[i].Value = m.fx.GetEffect(i); et[i].Text = m.fx.GetEffect(i).ToString("0.0"); }
            foreach (RadioButton r in Chips.Children) r.IsChecked = (string)r.Content == m.fx.Preset;
            loading = false;
        }

        void On_Changed(object s, RoutedEventArgs e) { if (loading) return; m.fx.On = OnBox.IsChecked == true; m.OnFxChanged(true); }
        void Reset_Click(object s, RoutedEventArgs e) => m.ApplyPreset("Γενικό (Flat)");
        void Save_Click(object s, RoutedEventArgs e) { var n = m.PromptName(); if (!string.IsNullOrWhiteSpace(n)) { m.SaveCustomPreset(n.Trim()); BuildChips(); Refresh(); } }
        void Delete_Click(object s, RoutedEventArgs e) { if (m.DeleteCustomPreset(m.fx.Preset)) { BuildChips(); Refresh(); } }
        void Close_Click(object s, RoutedEventArgs e) => Close();
    }
}
