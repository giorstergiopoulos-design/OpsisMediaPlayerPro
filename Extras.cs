using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using Microsoft.Win32;

namespace MediaPlayerPro
{
    public partial class MainWindow
    {
        string after = "none";
        static readonly string RecentFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaPlayerPro", "recent.txt");

        bool ExtraCmd(string c)
        {
            switch (c)
            {
                case "dvd": { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) AddFiles(new[] { "dvd:///" + d.FolderName.TrimEnd('\\') }, true); return true; }
                case "cam": AddFiles(new[] { "dshow://" }, true); return true;
                case "info": ShowInfo(); return true;
                case "minimal": { var v = MenuBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; MenuBar.Visibility = Controls.Visibility = Seek.Visibility = v; return true; }
                case "plclear": mp.Stop(); items.Clear(); idx = -1; return true;
                case "plrem": if (List.SelectedIndex >= 0) { int k = List.SelectedIndex; items.RemoveAt(k); if (k < idx) idx--; else if (k == idx) idx = -1; } return true;
            }
            if (c.StartsWith("after:")) { after = c.Substring(6); Say("Μετά την αναπαραγωγή: " + after); return true; }
            if (c.StartsWith("zoom:")) { mp.Scale = float.Parse(c.Substring(5), CultureInfo.InvariantCulture); return true; }
            if (c.StartsWith("crop:")) { mp.CropGeometry = c.Length > 5 ? c.Substring(5) : null; return true; }
            return false;
        }

        void AfterPlayback()
        {
            if (after == "exit") Close();
            else if (after == "shutdown") { Process.Start("shutdown", "/s /t 30"); Say("Τερματισμός υπολογιστή σε 30″ (shutdown /a για ακύρωση)"); }
        }

        void AddRecent(string p)
        {
            try
            {
                var l = File.Exists(RecentFile) ? File.ReadAllLines(RecentFile).ToList() : new List<string>();
                l.Remove(p); l.Insert(0, p);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RecentFile)); File.WriteAllLines(RecentFile, l.Take(10));
            }
            catch { }
        }

        void FillRecent()
        {
            RecentMenu.Items.Clear();
            try
            {
                if (File.Exists(RecentFile))
                    foreach (var p in File.ReadAllLines(RecentFile)) { var q = p; var m = new MenuItem { Header = q.Contains("://") ? q : System.IO.Path.GetFileName(q) }; m.Click += (a, b) => AddFiles(new[] { q }, true); RecentMenu.Items.Add(m); }
            }
            catch { }
        }

        void ShowInfo()
        {
            var m = mp.Media; if (m == null) { Say("Δεν παίζει κάποιο αρχείο"); return; }
            var sb = new StringBuilder(); sb.AppendLine(m.Mrl); sb.AppendLine("Διάρκεια: " + Fmt(mp.Length)); sb.AppendLine();
            foreach (var t in m.Tracks)
            {
                string codec = Encoding.ASCII.GetString(BitConverter.GetBytes(t.Codec)).Trim('\0');
                if (t.TrackType == TrackType.Video) sb.AppendLine($"Βίντεο: {codec}  {t.Data.Video.Width}×{t.Data.Video.Height}  {t.Bitrate / 1000} kbps");
                else if (t.TrackType == TrackType.Audio) sb.AppendLine($"Ήχος: {codec}  {t.Data.Audio.Channels} κανάλια  {t.Data.Audio.Rate} Hz  {t.Bitrate / 1000} kbps");
                else if (t.TrackType == TrackType.Text) sb.AppendLine("Υπότιτλοι: " + codec);
            }
            MessageBox.Show(sb.ToString(), "Πληροφορίες πολυμέσου");
        }
    }
}
