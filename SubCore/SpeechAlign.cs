using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaPlayerPro.SubCore
{
    public sealed class SegmentFit
    {
        public double Ratio = 1;        // ο λόγος ταχύτητας που εφαρμόστηκε στις γραμμές ΠΡΙΝ από την αναζήτηση της μετατόπισης
        public double Shift;            // video_time ≈ cue_time * Ratio + Shift  (δευτερόλεπτα)
        public double Score;            // κανονικοποιημένη συσχέτιση ομιλίας ↔ γραμμές, −1..1
        public double Prominence;       // πόσο ξεχωρίζει η κορυφή από την καλύτερη ΑΛΛΗ θέση (αποφυγή ψευδών ταιριασμάτων)
        public bool Good => Score >= SpeechAlign.MinScore && Prominence >= SpeechAlign.MinProminence;
    }

    public sealed class SyncSolution
    {
        public double Ratio = 1, Shift;
        public bool Good;
        public double Score;
        public string Detail = "";
    }

    /// <summary>
    /// Συγχρονισμός υποτίτλων με τον ήχο: ανιχνεύει πού ακούγεται ομιλία και τη συσχετίζει με τις γραμμές του .srt σε 3 βήματα ανάλυσης
    /// (0,5 s → 0,1 s → 0,02 s). Με δύο τμήματα του ήχου (αρχή και ~75 %) υπολογίζει και την απόκλιση ταχύτητας (π.χ. 25 ↔ 23,976 fps).
    /// Καθαρός κώδικας χωρίς UI/IO: δοκιμάζεται με συνθετικά δεδομένα.
    /// </summary>
    public static class SpeechAlign
    {
        // Βαθμονομήθηκαν με ΣΥΝΘΕΤΙΚΑ δεδομένα: σχετικός ήχος 0,55–0,97 (prominence 0,36–0,67), άσχετος ήχος ≤0,21 (prominence ≤0,042).
        // Σε πραγματικές ταινίες (μουσική, εφέ) οι τιμές είναι χαμηλότερες — αν ο έλεγχος αποτύχει, μένει ο χειροκίνητος συγχρονισμός (Y).
        public const double MinScore = 0.22, MinProminence = 0.08;
        public static readonly double[] FpsRatios = { 1.0, 25 / 23.976, 23.976 / 25, 24 / 23.976, 23.976 / 24, 25.0 / 24, 24.0 / 25, 30 / 29.97, 29.97 / 30 };

        /// <summary>Πού υπάρχει ομιλία: ενέργεια (με pre-emphasis για να μετράει η φωνή και όχι το μπάσο) ανά frame, με όριο ανάμεσα στο 20 % και το 90 % των τιμών.</summary>
        public static bool[] Voice(short[] pcm, int sampleRate, double frameSec)
        {
            int fl = Math.Max(1, (int)(sampleRate * frameSec)), n = pcm.Length / fl;
            if (n < 20) return new bool[0];
            var db = new double[n];
            for (int f = 0; f < n; f++)
            {
                double s = 0, prev = f * fl > 0 ? pcm[f * fl - 1] : 0;
                for (int k = 0; k < fl; k++) { double x = pcm[f * fl + k], d = x - 0.95 * prev; prev = x; s += d * d; }
                db[f] = 10 * Math.Log10(s / fl + 1);
            }
            var sorted = (double[])db.Clone(); Array.Sort(sorted);
            double lo = sorted[(int)(n * .2)], hi = sorted[(int)(n * .9)];
            if (hi - lo < 3) return new bool[n];                       // σχεδόν σταθερή στάθμη: δεν υπάρχει ομιλία να συσχετιστεί
            double thr = lo + .45 * (hi - lo);
            return db.Select(x => x > thr).ToArray();
        }

        private static sbyte[] CueGrid(IReadOnlyList<Cue> cues, double ratio, double gridStart, double fm, int length)
        {
            var g = new sbyte[length]; Array.Fill(g, (sbyte)-1);
            foreach (var c in cues)
            {
                int a = (int)Math.Floor((c.Start * ratio - gridStart) / fm), b = (int)Math.Ceiling((c.End * ratio - gridStart) / fm);
                if (b < 0 || a >= length) continue;
                for (int k = Math.Max(0, a); k <= Math.Min(length - 1, b); k++) g[k] = 1;
            }
            return g;
        }

        /// <summary>Αναζητά τη μετατόπιση s στο [sLo, sHi] με βήμα fm (ο ήχος στο t αντιστοιχεί στη γραμμή στο t − s).</summary>
        public static (double Shift, double Best, double Second) Search(bool[] speech, double segStart, double fm, IReadOnlyList<Cue> cues, double ratio, double sLo, double sHi, double exclusion)
        {
            int N = speech.Length; if (N == 0 || sHi < sLo) return (0, double.NegativeInfinity, double.NegativeInfinity);
            int M = (int)Math.Round((sHi - sLo) / fm);
            double g0 = segStart - sHi;                                   // η μικρότερη χρονική στιγμή γραμμής που μπορεί να χρειαστεί
            var G = CueGrid(cues, ratio, g0, fm, M + N + 1);
            var A = new sbyte[N]; for (int i = 0; i < N; i++) A[i] = (sbyte)(speech[i] ? 1 : -1);
            var scores = new double[M + 1]; int bestM = 0; double best = double.NegativeInfinity;
            for (int m = 0; m <= M; m++)
            {
                int sum = 0, off = M - m;
                for (int i = 0; i < N; i++) sum += A[i] * G[off + i];
                scores[m] = sum / (double)N;
                if (scores[m] > best) { best = scores[m]; bestM = m; }
            }
            double second = double.NegativeInfinity; int excl = (int)Math.Ceiling(exclusion / fm);
            for (int m = 0; m <= M; m++) if (Math.Abs(m - bestM) > excl && scores[m] > second) second = scores[m];
            return (sLo + bestM * fm, best, double.IsNegativeInfinity(second) ? best - 0.1 : second);
        }

        /// <summary>
        /// Βρίσκει τη μετατόπιση ΕΝΟΣ τμήματος ήχου. <paramref name="ratios"/>: υποψήφιοι λόγοι ταχύτητας (ο καλύτερος κερδίζει)·
        /// <paramref name="center"/>/<paramref name="radius"/>: πού περιμένουμε τη μετατόπιση.
        /// </summary>
        public static SegmentFit FitSegment(short[] pcm, int sampleRate, double segStartSec, IReadOnlyList<Cue> cues, double center, double radius, IEnumerable<double> ratios = null)
        {
            var fit = new SegmentFit { Score = double.NegativeInfinity };
            if (cues == null || cues.Count == 0 || pcm == null || pcm.Length < sampleRate * 20) return fit;
            var A1 = Voice(pcm, sampleRate, 0.5); if (A1.Length == 0) return fit;
            double bestShift = 0, bestRatio = 1, best = double.NegativeInfinity, second = double.NegativeInfinity;
            foreach (var r in ratios ?? new[] { 1.0 })
            {
                var s = Search(A1, segStartSec, 0.5, cues, r, center - radius, center + radius, 4.0);
                if (s.Best > best) { best = s.Best; second = s.Second; bestShift = s.Shift; bestRatio = r; }
            }
            var A2 = Voice(pcm, sampleRate, 0.1);
            var s2 = Search(A2, segStartSec, 0.1, cues, bestRatio, bestShift - 1.5, bestShift + 1.5, 0.5);
            var A3 = Voice(pcm, sampleRate, 0.02);
            var s3 = Search(A3, segStartSec, 0.02, cues, bestRatio, s2.Shift - 0.25, s2.Shift + 0.25, 0.1);
            fit.Ratio = bestRatio; fit.Shift = s3.Shift; fit.Score = s2.Best; fit.Prominence = best - second;
            return fit;
        }

        /// <summary>Υποψήφιοι λόγοι ταχύτητας ±6 % με βήμα 0,2 % για ταινίες με «περίεργη» απόκλιση που δεν είναι γνωστό ζεύγος fps.</summary>
        public static IEnumerable<double> SweepRatios() { for (int k = -30; k <= 30; k++) yield return 1 + k * 0.002; }

        /// <summary>Πρώτο τμήμα: δοκιμάζει πρώτα τα γνωστά ζεύγη fps και, ΑΝ δεν βρεθεί καλό ταίριασμα, σαρώνει όλο το ±6 %.</summary>
        public static SegmentFit FitFirst(short[] pcm, int sampleRate, double segStartSec, IReadOnlyList<Cue> cues, double center, double radius)
        {
            var fit = FitSegment(pcm, sampleRate, segStartSec, cues, center, radius, FpsRatios);
            if (fit.Good) return fit;
            var swept = FitSegment(pcm, sampleRate, segStartSec, cues, center, radius, SweepRatios());
            return swept.Good || swept.Score > fit.Score ? swept : fit;
        }

        /// <summary>
        /// Συνδυάζει τα δύο τμήματα: video = ratio·cue + shift. Οι μετατοπίσεις μετρήθηκαν ΜΕ τον λόγο rA ήδη εφαρμοσμένο στις γραμμές,
        /// άρα η υπολειπόμενη κλίση είναι 1 − 1/r_res (όχι r_res − 1: η διαφορά θα ήταν ~2 s σε 5000 s).
        /// </summary>
        public static SyncSolution Combine(SegmentFit a, double tA, SegmentFit b, double tB)
        {
            var sol = new SyncSolution { Score = a.Score };
            if (!a.Good) { sol.Detail = "weak"; return sol; }
            sol.Good = true; sol.Ratio = a.Ratio; sol.Shift = a.Shift; sol.Detail = "shift";
            if (b == null || !b.Good || Math.Abs(b.Ratio - a.Ratio) > 1e-9 || tB - tA < 600) return sol;
            double slope = (b.Shift - a.Shift) / (tB - tA);
            double rRes = 1 / (1 - slope);
            if (Math.Abs(rRes - 1) > 0.1) { sol.Detail = "shift (second segment implausible)"; return sol; }
            if (Math.Abs(rRes - 1) < 0.0002)                                 // καμία σημαντική απόκλιση: σταθερή μετατόπιση, ο μέσος όρος των δύο
            {
                sol.Shift = (a.Shift + b.Shift) / 2; sol.Score = Math.Min(a.Score, b.Score); sol.Detail = "shift (2 segments agree)"; return sol;
            }
            sol.Ratio = a.Ratio * rRes;
            sol.Shift = rRes * a.Shift - tA * (rRes - 1);
            sol.Score = Math.Min(a.Score, b.Score);
            sol.Detail = "shift+ratio";
            return sol;
        }
    }
}
