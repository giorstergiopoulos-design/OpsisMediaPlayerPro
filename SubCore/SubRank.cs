using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MediaPlayerPro.SubCore
{
    /// <summary>Τι «λέει» το όνομα ενός βίντεο / μιας έκδοσης υποτίτλων: τίτλος, σεζόν/επεισόδιο, έτος, ανάλυση, πηγή, κωδικοποιητής, ομάδα.</summary>
    public sealed class ReleaseInfo
    {
        public string Title = "";
        public HashSet<string> TitleTokens = new HashSet<string>();
        public int? Year, Season, Episode;
        public string Resolution, Source, Codec, Group;

        private static readonly Regex SxE = new Regex(@"(?<![a-z0-9])s(?<s>\d{1,2})[ ._-]?e(?<e>\d{1,3})(?![0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NxM = new Regex(@"(?<![a-z0-9])(?<s>\d{1,2})x(?<e>\d{2,3})(?![0-9a-z])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Long = new Regex(@"season[ ._-]*(?<s>\d{1,2})[ ._-]*(?:episode|ep)[ ._-]*(?<e>\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex YearRx = new Regex(@"(?<![0-9])(19[3-9]\d|20[0-4]\d)(?![0-9])", RegexOptions.Compiled);
        private static readonly Regex ResRx = new Regex(@"(?<![0-9])(2160|1080|720|576|480)[pi]?(?![0-9])|\b4k\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SrcRx = new Regex(@"\b(blu-?ray|bdrip|brrip|web-?dl|web-?rip|webrip|web|hdtv|dvdrip|hdrip|remux|amzn|nf|dsnp)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CodecRx = new Regex(@"\b(x264|x265|h\.?264|h\.?265|hevc|avc|xvid|divx|av1)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex GroupRx = new Regex(@"-(?<g>[a-z0-9]{2,12})(?:\[[^\]]*\])?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly HashSet<string> Noise = new HashSet<string> { "the", "a", "an", "and", "of", "in", "to", "en", "el", "gr", "greek", "english", "sub", "subs", "srt", "hi", "ita", "fra", "spa" };

        private static string NormSource(string s) => s == null ? null : Regex.Replace(s.ToLowerInvariant(), @"[-]", "").Replace("webdl", "web").Replace("webrip", "web").Replace("bdrip", "bluray").Replace("brrip", "bluray");
        private static string NormCodec(string s) => s == null ? null : s.ToLowerInvariant().Replace(".", "").Replace("h264", "x264").Replace("avc", "x264").Replace("h265", "x265").Replace("hevc", "x265");

        public static ReleaseInfo Parse(string name)
        {
            var r = new ReleaseInfo();
            if (string.IsNullOrWhiteSpace(name)) return r;
            string n = name.Trim();
            int dot = n.LastIndexOf('.'); if (dot > n.Length - 6 && dot > 0 && Regex.IsMatch(n.Substring(dot), @"^\.[a-z0-9]{2,4}$", RegexOptions.IgnoreCase)) n = n.Substring(0, dot);   // επέκταση αρχείου
            var gm = GroupRx.Match(n); if (gm.Success && !Regex.IsMatch(gm.Groups["g"].Value, @"^\d+[pi]?$")) r.Group = gm.Groups["g"].Value.ToLowerInvariant();
            string spaced = Regex.Replace(n, @"[._\[\]\(\)]+", " ");

            Match m = SxE.Match(spaced); if (!m.Success) m = NxM.Match(spaced); if (!m.Success) m = Long.Match(spaced);
            int cut = spaced.Length;
            if (m.Success) { r.Season = int.Parse(m.Groups["s"].Value); r.Episode = int.Parse(m.Groups["e"].Value); cut = Math.Min(cut, m.Index); }
            var rm = ResRx.Match(spaced); if (rm.Success) { r.Resolution = rm.Value.ToLowerInvariant().Replace("i", "p"); if (r.Resolution == "4k") r.Resolution = "2160p"; if (!r.Resolution.EndsWith("p")) r.Resolution += "p"; cut = Math.Min(cut, rm.Index); }
            var sm = SrcRx.Match(spaced); if (sm.Success) { r.Source = NormSource(sm.Value); cut = Math.Min(cut, sm.Index); }
            var cm = CodecRx.Match(spaced); if (cm.Success) { r.Codec = NormCodec(cm.Value); cut = Math.Min(cut, cm.Index); }
            // Έτος = το ΤΕΛΕΥΤΑΙΟ πριν από τα στοιχεία ποιότητας («Blade Runner 2049 (2017) 2160p» → 2017, «2012 (2009)» → 2009)
            int quality = Math.Min(cut, spaced.Length);
            var years = YearRx.Matches(spaced).Cast<Match>().Where(x => x.Index > 0).ToList();
            var y = years.LastOrDefault(x => x.Index < quality) ?? years.LastOrDefault();
            if (y != null) { r.Year = int.Parse(y.Value); cut = Math.Min(cut, y.Index); }

            r.Title = spaced.Substring(0, Math.Max(0, cut)).Trim().ToLowerInvariant();
            r.TitleTokens = new HashSet<string>(Regex.Split(r.Title, @"\W+").Where(t => t.Length > 1 && !Noise.Contains(t)));
            return r;
        }
    }

    public static class SubRank
    {
        /// <summary>
        /// Βαθμολογία ενός υποψήφιου υποτίτλου για ένα βίντεο. Το επεισόδιο/σεζόν που δεν ταιριάζει είναι σχεδόν απορριπτέο (−100),
        /// η ίδια ομάδα/πηγή/ανάλυση δίνουν μικρά μπόνους. Μεγαλύτερη τιμή = καλύτερος υποψήφιος.
        /// </summary>
        public static double Score(ReleaseInfo video, string candidateText, bool hearingImpaired = false, double downloads01 = 0, int? candSeason = null, int? candEpisode = null, int? candYear = null)
        {
            var c = ReleaseInfo.Parse(candidateText ?? "");
            if (candSeason != null) c.Season = candSeason; if (candEpisode != null) c.Episode = candEpisode; if (candYear != null) c.Year = candYear;
            double s = 0;

            if (video.TitleTokens.Count > 0)
            {
                var all = new HashSet<string>(Regex.Split((candidateText ?? "").ToLowerInvariant(), @"\W+").Where(t => t.Length > 1));
                int hit = video.TitleTokens.Count(t => all.Contains(t));
                s += 30.0 * hit / video.TitleTokens.Count;
                if (hit == 0) s -= 20;
            }
            if (video.Season != null && video.Episode != null)
            {
                if (c.Season == video.Season && c.Episode == video.Episode) s += 40;
                else if (c.Season != null && c.Episode != null) s -= 100;                  // ΑΛΛΟ επεισόδιο: μην το διαλέξεις ποτέ αν υπάρχει εναλλακτική
                else s -= 5;
            }
            else if (c.Season != null && c.Episode != null && video.Season == null) s -= 10;      // το βίντεο μοιάζει με ταινία, ο υπότιτλος με επεισόδιο
            if (video.Year != null && c.Year != null) s += video.Year == c.Year ? 8 : Math.Abs(video.Year.Value - c.Year.Value) > 1 ? -15 : 0;
            if (video.Resolution != null && c.Resolution == video.Resolution) s += 6;
            if (video.Source != null && c.Source == video.Source) s += 10;
            if (video.Codec != null && c.Codec == video.Codec) s += 3;
            if (!string.IsNullOrEmpty(video.Group) && (c.Group == video.Group || (candidateText ?? "").ToLowerInvariant().Contains(video.Group))) s += 15;
            if (hearingImpaired) s -= 2;
            s += Math.Clamp(downloads01, 0, 1) * 10;
            return s;
        }

        /// <summary>
        /// Πιθανότητα (0..1) ότι ένας υπότιτλος ανήκει σε βίντεο διάρκειας <paramref name="videoSeconds"/>: η τελευταία γραμμή πρέπει να τελειώνει κοντά στο τέλος
        /// (75–102 % της διάρκειας· οι τίτλοι τέλους δεν έχουν συνήθως υπότιτλους) και οι γραμμές να είναι σε σειρά.
        /// </summary>
        public static double Plausibility(IReadOnlyList<Cue> cues, double videoSeconds)
        {
            if (cues == null || cues.Count < 5 || videoSeconds <= 60) return 0;
            double last = cues.Max(c => c.End), f = last / videoSeconds;
            double fit = f >= 0.75 && f <= 1.02 ? 1 : f < 0.75 ? Math.Max(0, (f - 0.5) / 0.25) : Math.Max(0, 1 - (f - 1.02) / 0.23);
            int outOfOrder = 0; for (int i = 1; i < cues.Count; i++) if (cues[i].Start + 1 < cues[i - 1].Start) outOfOrder++;
            if (outOfOrder > cues.Count / 10) fit *= 0.5;
            return fit;
        }
    }
}
