using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaPlayerPro.SubCore
{
    /// <summary>Μία γραμμή υποτίτλων (σε δευτερόλεπτα).</summary>
    public sealed class Cue
    {
        public double Start, End;
        public string Text = "";
        public Cue() { }
        public Cue(double start, double end, string text = "") { Start = start; End = end; Text = text; }
    }

    /// <summary>Ανάγνωση / εγγραφή / μετασχηματισμός SubRip (.srt). Δεν εξαρτάται από το UI — δοκιμάζεται σε ξεχωριστό test project.</summary>
    public static class Srt
    {
        // hh:mm:ss,mmm ή (WebVTT) mm:ss.mmm
        private const string T = @"(?:\d{1,2}:)?\d{1,2}:\d{2}[,.]\d{1,3}";
        private static readonly Regex TimeLine = new Regex("(?<a>" + T + @")\s*-->\s*(?<b>" + T + ")", RegexOptions.Compiled);
        private static readonly Regex BlankLines = new Regex(@"\n[ \t]*\n", RegexOptions.Compiled);

        public static double ParseTime(string s)
        {
            s = s.Trim().Replace(',', '.');
            var parts = s.Split(':');
            double h = 0, m, sec;
            if (parts.Length == 3) { h = int.Parse(parts[0], CultureInfo.InvariantCulture); m = int.Parse(parts[1], CultureInfo.InvariantCulture); sec = ParseSeconds(parts[2]); }
            else { m = int.Parse(parts[0], CultureInfo.InvariantCulture); sec = ParseSeconds(parts[1]); }
            return h * 3600 + m * 60 + sec;
        }

        private static double ParseSeconds(string s)
        {
            int dot = s.IndexOf('.');
            if (dot < 0) return int.Parse(s, CultureInfo.InvariantCulture);
            string frac = s.Substring(dot + 1).PadRight(3, '0').Substring(0, 3);          // "5" = 500 ms, "50" = 500 ms, "500" = 500 ms
            return int.Parse(s.Substring(0, dot), CultureInfo.InvariantCulture) + int.Parse(frac, CultureInfo.InvariantCulture) / 1000.0;
        }

        public static string Ts(double t)
        {
            long ms = (long)Math.Round(Math.Max(0, t) * 1000.0);                           // ένα ενιαίο στρογγύλεμα: ποτέ «1000 ms»
            long h = ms / 3600000, m = ms / 60000 % 60, s = ms / 1000 % 60, f = ms % 1000;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00},{3:000}", h, m, s, f);
        }

        /// <summary>Διαβάζει .srt (και WebVTT). Ανεκτικό: BOM, CRLF, λείπουν αριθμοί γραμμών, «.» αντί «,», κενές γραμμές μέσα στο κείμενο δεν σπάνε τη γραμμή.</summary>
        public static List<Cue> Parse(string text)
        {
            var cues = new List<Cue>();
            if (string.IsNullOrWhiteSpace(text)) return cues;
            text = text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var block in BlankLines.Split(text))
            {
                var lines = block.Split('\n');
                int at = Array.FindIndex(lines, l => TimeLine.IsMatch(l));
                if (at < 0) continue;
                var m = TimeLine.Match(lines[at]);
                double a, b;
                try { a = ParseTime(m.Groups["a"].Value); b = ParseTime(m.Groups["b"].Value); } catch (FormatException) { continue; } catch (OverflowException) { continue; }
                if (b < a) b = a;
                cues.Add(new Cue(a, b, string.Join("\n", lines.Skip(at + 1).Select(l => l.TrimEnd()).Where(l => l.Length > 0))));
            }
            return cues;
        }

        public static string Write(IEnumerable<Cue> cues)
        {
            var sb = new StringBuilder(); int i = 1;
            foreach (var c in cues)
            {
                sb.Append(i++).Append("\r\n").Append(Ts(c.Start)).Append(" --> ").Append(Ts(c.End)).Append("\r\n");
                sb.Append(c.Text.Replace("\r\n", "\n").Replace("\n", "\r\n")).Append("\r\n\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Νέος χρόνος = παλιός × ratio + shift (δευτερόλεπτα). Γραμμές που τελειώνουν πριν το 0 αφαιρούνται, οι αρχές κόβονται στο 0.</summary>
        public static List<Cue> Transform(IEnumerable<Cue> cues, double ratio, double shift)
        {
            var res = new List<Cue>();
            foreach (var c in cues)
            {
                double a = c.Start * ratio + shift, b = c.End * ratio + shift;
                if (b <= 0) continue;
                res.Add(new Cue(Math.Max(0, a), Math.Max(0, b), c.Text));
            }
            return res;
        }
    }
}
