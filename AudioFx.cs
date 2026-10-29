using System;
using MediaPlayerPro.Conv;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace MediaPlayerPro
{
    // Μοντέλο εμπνευσμένο από το FxSound: 10-band EQ (±12 dB) + 5 εφέ με επίπεδο 0–10 + προεπιλογές
    public class AudioFx
    {
        public bool On { get; set; } = true;
        public string Preset { get; set; } = "Γενικό (Flat)";
        public float Pre { get; set; }
        public float[] Bands { get; set; } = new float[10];
        public double Clarity { get; set; }
        public double Ambience { get; set; }
        public double Surround { get; set; }
        public double Dynamic { get; set; }
        public double Bass { get; set; }

        public AudioFx Clone() => JsonSerializer.Deserialize<AudioFx>(JsonSerializer.Serialize(this));
        public void SetEffect(int i, double v) { switch (i) { case 0: Clarity = v; break; case 1: Ambience = v; break; case 2: Surround = v; break; case 3: Dynamic = v; break; default: Bass = v; break; } }
        public double GetEffect(int i) => i == 0 ? Clarity : i == 1 ? Ambience : i == 2 ? Surround : i == 3 ? Dynamic : Bass;

        static AudioFx P(string n, float[] b, double cla = 0, double amb = 0, double sur = 0, double dyn = 0, double bas = 0) =>
            new AudioFx { Preset = n, Bands = b, Clarity = cla, Ambience = amb, Surround = sur, Dynamic = dyn, Bass = bas };
        // Ζώνες: 60, 170, 310, 600, 1K, 3K, 6K, 12K, 14K, 16K Hz
        static readonly AudioFx[] All =
        {
            P("Γενικό (Flat)", new float[10]),
            P("Bass Booster", new float[] { 6, 5, 3, 1, 0, 0, 0, 0, 0, 0 }, bas: 6),
            P("Treble Boost", new float[] { 0, 0, 0, 0, 0, 2, 4, 5, 5, 5 }, cla: 3),
            P("Καθαρή φωνή", new float[] { -3, -2, 0, 2, 3, 4, 3, 1, 0, -1 }, cla: 5, dyn: 3),
            P("Cinema", new float[] { 4, 3, 1, 0, 0, 1, 2, 3, 3, 3 }, amb: 2, sur: 6, dyn: 3, bas: 4),
            P("Μουσική", new float[] { 3, 2, 1, 0, -1, 0, 1, 3, 4, 4 }, cla: 3),
            P("Rock", new float[] { 5, 3, 1, -1, -2, 0, 3, 4, 5, 5 }),
            P("Pop", new float[] { -1, 1, 3, 4, 3, 1, 0, -1, -1, -2 }),
            P("Jazz", new float[] { 3, 2, 1, 2, -1, -1, 0, 2, 3, 4 }, amb: 2),
            P("Classical", new float[] { 4, 3, 2, 1, -1, -1, 0, 2, 3, 4 }, amb: 3),
            P("Dance / EDM", new float[] { 7, 5, 2, 0, 0, -2, -1, 2, 4, 5 }, sur: 3, bas: 5),
            P("Gaming", new float[] { 2, 1, 0, 0, 1, 3, 4, 3, 2, 1 }, cla: 4, sur: 7),
            P("Νυχτερινή λειτουργία", new float[] { -2, -1, 0, 0, 1, 2, 2, 0, -1, -2 }, dyn: 8),
            P("Ηχεία laptop", new float[] { -4, -2, 3, 4, 3, 3, 2, 2, 1, 0 }, cla: 6, bas: 3),
            P("Ακουστικά", new float[] { 2, 1, 0, 0, -1, 0, 1, 2, 3, 3 }, amb: 1, sur: 4),
        };
        public static readonly Dictionary<string, AudioFx> Presets = All.ToDictionary(x => x.Preset);
    }

    public partial class MainWindow
    {
        internal AudioFx fx = new AudioFx();
        readonly Dictionary<string, AudioFx> customFx = new Dictionary<string, AudioFx>();
        AudioWindow audioWin; string appliedSig = "";
        readonly DispatcherTimer fxT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        static readonly double[] ClW = { 0, 0, 0, 0, 0, 0.5, 0.8, 1, 1, 0.9 }, BsW = { 1, 0.85, 0.55, 0.2, 0, 0, 0, 0, 0, 0 };
        static string AFile(string n) => System.IO.Path.Combine(AppDir, n);

        string FxSig() => $"{fx.On}|{fx.Ambience}|{fx.Surround}|{fx.Dynamic}";

        void LoadAudio()
        {
            fxT.Tick += (s, e) => { fxT.Stop(); if (FxSig() != appliedSig) { Say(Loc.T("Εφαρμογή εφέ ήχου (σύντομη παύση, η θέση διατηρείται)", "Applying audio effect (short pause, position is kept)")); ReplayHere(); } };
            try
            {
                if (File.Exists(AFile("audio.json"))) fx = JsonSerializer.Deserialize<AudioFx>(File.ReadAllText(AFile("audio.json"))) ?? fx;
                if (File.Exists(AFile("audio-presets.json"))) foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, AudioFx>>(File.ReadAllText(AFile("audio-presets.json")))) customFx[kv.Key] = kv.Value;
            }
            catch { }
            if (fx.Bands == null || fx.Bands.Length != 10) fx.Bands = new float[10];
            ApplyAudio();
        }

        void SaveAudio() { try { Directory.CreateDirectory(AppDir); File.WriteAllText(AFile("audio.json"), JsonSerializer.Serialize(fx)); } catch { } }

        // Ζωντανή εφαρμογή EQ (Equalizer του LibVLC) με αυτόματο headroom ώστε να μη γίνεται clipping
        internal void ApplyAudio(bool persist = true)
        {
            if (mp == null) return;
            if (!fx.On) { mp.UnsetEqualizer(); return; }
            var g = new float[10]; float max = 0;
            for (int i = 0; i < 10; i++) { g[i] = (float)Math.Max(-20, Math.Min(20, fx.Bands[i] + fx.Clarity * ClW[i] * 0.6 + fx.Bass * BsW[i] * 0.8)); max = Math.Max(max, g[i]); }
            using (var eq = new Equalizer())
            {
                eq.SetPreamp(Math.Max(-20, Math.Min(20, fx.Pre - max * 0.5f)));
                for (uint i = 0; i < 10; i++) eq.SetAmp(g[i], i);
                mp.SetEqualizer(eq);
            }
        }

        // Εφέ που βασίζονται σε φίλτρα LibVLC (χρειάζονται επανεκκίνηση της αναπαραγωγής)
        void FxOpts(List<string> af, List<string> o)
        {
            appliedSig = FxSig(); if (!fx.On) return;
            string I(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
            if (fx.Ambience > 0) { af.Add("spatializer"); o.Add(":spatializer-wet=" + I(fx.Ambience * 0.8)); o.Add(":spatializer-dry=" + I(10 - fx.Ambience * 0.4)); o.Add(":spatializer-roomsize=" + I(0.6 + fx.Ambience * 0.045)); }
            if (fx.Surround > 0) { af.Add("stereo_widen"); o.Add(":stereo-widen-delay=" + I(5 + fx.Surround * 3)); o.Add(":stereo-widen-crossfeed=" + I(0.15 + fx.Surround * 0.03)); }
            if (fx.Dynamic > 0) { if (!af.Contains("compressor")) af.Add("compressor"); o.Add(":compressor-threshold=" + I(-8 - fx.Dynamic * 2.2)); o.Add(":compressor-ratio=" + I(2 + fx.Dynamic * 0.4)); o.Add(":compressor-makeup-gain=" + I(fx.Dynamic * 1.1)); }
        }

        internal void OnFxChanged(bool filters)
        {
            ApplyAudio(); SaveAudio();
            if (filters && FxSig() != appliedSig) { fxT.Stop(); fxT.Start(); }
        }

        internal IEnumerable<string> AllPresetNames() => AudioFx.Presets.Keys.Concat(customFx.Keys);

        internal void ApplyPreset(string name)
        {
            AudioFx p; if (!AudioFx.Presets.TryGetValue(name, out p) && !customFx.TryGetValue(name, out p)) return;
            bool on = fx.On; fx = p.Clone(); fx.On = on; fx.Preset = name;
            ApplyAudio(); SaveAudio(); if (FxSig() != appliedSig) { fxT.Stop(); fxT.Start(); }
            audioWin?.Refresh(); Say("Ήχος: " + name);
        }

        internal string PromptName() => Prompt("Όνομα προεπιλογής ήχου", fx.Preset == "Προσαρμοσμένο" ? "Η προεπιλογή μου" : fx.Preset);

        internal void SaveCustomPreset(string name)
        {
            var c = fx.Clone(); c.On = true; c.Preset = name; customFx[name] = c; fx.Preset = name; SaveAudio();
            try { Directory.CreateDirectory(AppDir); File.WriteAllText(AFile("audio-presets.json"), JsonSerializer.Serialize(customFx)); } catch { }
        }

        internal bool DeleteCustomPreset(string name)
        {
            if (!customFx.Remove(name)) return false;
            try { File.WriteAllText(AFile("audio-presets.json"), JsonSerializer.Serialize(customFx)); } catch { }
            fx.Preset = "Προσαρμοσμένο"; SaveAudio(); return true;
        }

        void ShowAudio() { if (audioWin != null) { audioWin.Activate(); return; } audioWin = new AudioWindow(this); audioWin.Show(); }
        internal void AudioClosed() => audioWin = null;

        void FillAudioMenu()
        {
            EqMenu.Items.Clear();
            foreach (var n in AllPresetNames()) { var name = n; var m = new System.Windows.Controls.MenuItem { Header = name, IsChecked = fx.Preset == name }; m.Click += (s, e) => ApplyPreset(name); EqMenu.Items.Add(m); }
        }
    }
}
