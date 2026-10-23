using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaPlayerPro.SubCore
{
    /// <summary>Μετατροπή άλλων μορφών υποτίτλων (WebVTT, ASS/SSA) σε SRT και αναγνώριση κωδικοποίησης χαρακτήρων.</summary>
    public static class SubFormats
    {
        public static readonly string[] Extensions = { ".srt", ".vtt", ".ass", ".ssa" };

        /// <summary>Επιστρέφει κείμενο SRT, ή null αν η μορφή δεν υποστηρίζεται / δεν βρέθηκαν γραμμές.</summary>
        public static string ToSrt(string text, string extension)
        {
            if (text == null) return null;
            List<Cue> cues;
            switch ((extension ?? ".srt").ToLowerInvariant())
            {
                case ".ass": case ".ssa": cues = ParseAss(text); break;
                case ".vtt":
                    cues = Srt.Parse(text);
                    foreach (var c in cues) c.Text = Regex.Replace(c.Text, @"<(?!/?(?:i|b|u|font)\b)[^>]*>", "");     // <c.xxx>, <v Name>, <00:01.000>
                    break;
                default: cues = Srt.Parse(text); break;
            }
            return cues.Count == 0 ? null : Srt.Write(cues);
        }

        public static List<Cue> ParseAss(string text)
        {
            var cues = new List<Cue>();
            int iStart = 1, iEnd = 2, iText = 9, columns = 10;
            bool inEvents = false;
            foreach (var raw in text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal)) { inEvents = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase); continue; }
                if (!inEvents) continue;
                if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
                {
                    var names = line.Substring(7).Split(',').Select(s => s.Trim().ToLowerInvariant()).ToArray();
                    columns = names.Length; iStart = Array.IndexOf(names, "start"); iEnd = Array.IndexOf(names, "end"); iText = Array.IndexOf(names, "text");
                    if (iStart < 0 || iEnd < 0 || iText < 0) return cues;
                    continue;
                }
                if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)) continue;
                var f = line.Substring(9).TrimStart().Split(new[] { ',' }, columns);        // το κείμενο μπορεί να έχει κόμματα: κόβουμε στις πρώτες (columns-1)
                if (f.Length < columns) continue;
                try
                {
                    double a = AssTime(f[iStart]), b = AssTime(f[iEnd]);
                    var t = Regex.Replace(f[iText], @"\{[^}]*\}", "").Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " ").Trim();
                    if (t.Length > 0 && !f[iText].Contains("\\p1")) cues.Add(new Cue(a, Math.Max(a, b), t));
                }
                catch (FormatException) { }
            }
            return cues.OrderBy(c => c.Start).ToList();
        }

        private static double AssTime(string s)        // h:mm:ss.cc
        {
            var p = s.Trim().Split(':');
            var sec = p[2].Split('.');
            double cs = sec.Length > 1 ? int.Parse(sec[1].PadRight(2, '0').Substring(0, 2), CultureInfo.InvariantCulture) / 100.0 : 0;
            return int.Parse(p[0], CultureInfo.InvariantCulture) * 3600 + int.Parse(p[1], CultureInfo.InvariantCulture) * 60 + int.Parse(sec[0], CultureInfo.InvariantCulture) + cs;
        }

        /// <summary>
        /// Κωδικοποίηση χαρακτήρων: BOM (UTF-8/16/32) → UTF-16 χωρίς BOM (πολλά μηδενικά byte) → αυστηρό UTF-8 → κωδική σελίδα της γλώσσας.
        /// Το αποτέλεσμα ΔΕΝ περιέχει BOM και δεν ρίχνει ποτέ exception.
        /// </summary>
        public static string Decode(byte[] b, int fallbackCodePage)
        {
            if (b == null || b.Length == 0) return "";
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return new UTF8Encoding(false).GetString(b, 3, b.Length - 3);
            if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0) return new UTF32Encoding(false, false).GetString(b, 4, b.Length - 4);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return new UnicodeEncoding(false, false).GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return new UnicodeEncoding(true, false).GetString(b, 2, b.Length - 2);
            int zeroEven = 0, zeroOdd = 0, probe = Math.Min(b.Length, 2000);
            for (int i = 0; i < probe; i++) if (b[i] == 0) { if ((i & 1) == 0) zeroEven++; else zeroOdd++; }
            if (probe > 20 && zeroOdd > probe / 4 && zeroEven == 0) return new UnicodeEncoding(false, false).GetString(b);
            if (probe > 20 && zeroEven > probe / 4 && zeroOdd == 0) return new UnicodeEncoding(true, false).GetString(b);
            try { return new UTF8Encoding(false, true).GetString(b); }
            catch (ArgumentException) { }
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(fallbackCodePage).GetString(b);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException) { return Encoding.Latin1.GetString(b); }
        }
    }
}
