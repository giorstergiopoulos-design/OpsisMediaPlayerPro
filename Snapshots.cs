using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaPlayerPro.Conv;

namespace MediaPlayerPro
{
    // `OpsisMediaPlayerPro.exe --snapshots <φάκελος> <αρχείο-πηγή>`: φωτογραφίζει τα πραγματικά παράθυρα (κύριο, μετατροπέας, ήχος, οικογένεια Opsis) σε σκούρα και φωτεινή λειτουργία
    // για το README και ελέγχει τη διάταξη του μετατροπέα στο 150 % (WPF render στα 144 dpi + LayoutTransform 1,5).
    public partial class MainWindow
    {
        static void SaveShot(FrameworkElement el, string path, double dpiScale = 1)
        {
            int w = (int)Math.Ceiling(el.ActualWidth * dpiScale), h = (int)Math.Ceiling(el.ActualHeight * dpiScale);
            if (w < 10 || h < 10) return;
            var rtb = new RenderTargetBitmap(w, h, 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);
            var dv = new DrawingVisual(); using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, el.ActualWidth, el.ActualHeight));
            rtb.Render(dv); var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path); enc.Save(fs);
        }

        void StartSnapshots()
        {
            var args = Environment.GetCommandLineArgs().ToList(); int i = args.IndexOf("--snapshots");
            string dir = i + 1 < args.Count ? args[i + 1] : "docs/img", src = i + 2 < args.Count ? args[i + 2] : null;
            Directory.CreateDirectory(dir);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += async (s, e) =>
            {
                t.Stop(); string original = mode; var log = new System.Text.StringBuilder();
                try
                {
                    Topmost = true; Left = 20; Top = 20; Width = 1100; Height = 640;
                    foreach (var m in new[] { "dark", "light" })
                    {
                        mode = m; ApplySkin(curSkin, false); await System.Threading.Tasks.Task.Delay(600);
                        SaveShot((FrameworkElement)Content, System.IO.Path.Combine(dir, "player-" + m + ".png"));
                        if (src != null)
                        {
                            var cw = new ConvertWindow(src, 5, 15) { Owner = this, Left = 1150, Top = 20 }; cw.Show(); await System.Threading.Tasks.Task.Delay(700);
                            SaveShot((FrameworkElement)cw.Content, System.IO.Path.Combine(dir, "converter-" + m + ".png"));
                            var sv = (ScrollViewer)((Border)cw.Content).Child; var inner = (FrameworkElement)sv.Content; inner.LayoutTransform = new ScaleTransform(1.5, 1.5); inner.UpdateLayout();
                            double need = inner.ActualHeight * 1; log.AppendLine($"converter {m} @150%: content {inner.ActualWidth:0}x{inner.ActualHeight:0} px in a window {cw.ActualWidth:0}x{cw.ActualHeight:0}; ScrollViewer scrollable={sv.ScrollableHeight > 0 || cw.ActualHeight >= need}");
                            SaveShot(cw, System.IO.Path.Combine(dir, "converter-" + m + "-150.png")); inner.LayoutTransform = Transform.Identity; cw.Close();
                        }
                        ShowAudio(); Do("suite"); await System.Threading.Tasks.Task.Delay(2500);
                        int k = 0; foreach (Window w in Application.Current.Windows.Cast<Window>().Where(x => x != this && x.IsVisible).ToList())
                        { SaveShot(w, System.IO.Path.Combine(dir, (w.GetType().Name.ToLowerInvariant().Contains("audio") ? "audio" : "window" + (k++)) + "-" + m + ".png")); w.Close(); }
                    }
                }
                catch (Exception ex) { log.AppendLine("SNAPSHOT ERROR " + ex); }
                File.WriteAllText(System.IO.Path.Combine(dir, "snapshots.log"), log.ToString());
                mode = original; ApplySkin(curSkin, false); Environment.Exit(0);
            };
            t.Start();
        }
    }
}
