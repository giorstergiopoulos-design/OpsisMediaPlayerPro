using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaPlayerPro
{
    /// <summary>Πού ψάχνουμε / πού γράφουμε τον υπότιτλο μιας ταινίας: δίπλα στο βίντεο ΚΑΙ στον φάκελο που όρισε ο χρήστης (ώστε να μην ξανακατεβαίνει σε κάθε αναπαραγωγή).</summary>
    public static class SubLocation
    {
        static readonly string[] Exts = { ".srt", ".vtt", ".ass", ".ssa" };

        public static IEnumerable<string> Candidates(string video, string userFolder, IEnumerable<string> languages)
        {
            if (string.IsNullOrEmpty(video)) yield break;
            string stem = Path.GetFileNameWithoutExtension(video);
            var folders = new List<string> { Path.GetDirectoryName(video) };
            if (!string.IsNullOrWhiteSpace(userFolder) && !folders.Contains(userFolder, StringComparer.OrdinalIgnoreCase)) folders.Add(userFolder);
            var langs = (languages ?? Array.Empty<string>()).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            foreach (var f in folders)
                foreach (var e in Exts)
                {
                    yield return Path.Combine(f, stem + e);
                    foreach (var l in langs) yield return Path.Combine(f, stem + "." + l + e);
                    yield return Path.Combine(f, stem + ".synced" + e);
                }
        }

        public static string Find(string video, string userFolder, IEnumerable<string> languages, Func<string, bool> exists = null)
        {
            exists ??= File.Exists;
            return Candidates(video, userFolder, languages).FirstOrDefault(exists);
        }

        /// <summary>Ποτέ δεν αντικαθιστά υπάρχον αρχείο: αν το «ταινία.srt» υπάρχει, γράφει «ταινία.γλώσσα.srt» και μετά «(2)», «(3)»…</summary>
        public static string TargetPath(string video, string folder, string lang, bool plainName, bool primary, Func<string, bool> exists = null)
        {
            exists ??= File.Exists;
            string stem = Path.GetFileNameWithoutExtension(video); folder = string.IsNullOrWhiteSpace(folder) ? Path.GetDirectoryName(video) : folder;
            string p = Path.Combine(folder, stem + (plainName && primary ? "" : "." + lang) + ".srt");
            if (!exists(p)) return p;
            if (plainName && primary) { p = Path.Combine(folder, stem + "." + lang + ".srt"); if (!exists(p)) return p; }
            for (int i = 2; ; i++) { p = Path.Combine(folder, stem + "." + lang + " (" + i + ").srt"); if (!exists(p)) return p; }
        }
    }
}

namespace MediaPlayerPro
{
    /// <summary>Όρια για μη αξιόπιστα δεδομένα (αρχεία από το διαδίκτυο): ένας υπότιτλος ή ένα zip δεν μπορεί να φουσκώσει τη μνήμη (zip bomb / ψευδές μέγεθος στην κεφαλίδα).</summary>
    public static class SubLimits
    {
        public const int MaxSubtitleBytes = 5 * 1024 * 1024;

        /// <summary>Διαβάζει το πολύ max bytes· πετά InvalidDataException αν υπάρχουν περισσότερα (μετριούνται τα πραγματικά bytes, όχι η κεφαλίδα).</summary>
        public static byte[] ReadLimited(System.IO.Stream s, int max = MaxSubtitleBytes)
        {
            var ms = new System.IO.MemoryStream(); var buf = new byte[81920]; int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0) { if (ms.Length + n > max) throw new System.IO.InvalidDataException("Subtitle larger than " + max / 1024 / 1024 + " MB"); ms.Write(buf, 0, n); }
            return ms.ToArray();
        }

        /// <summary>Το όνομα μέσα στο zip είναι μόνο ετικέτα (δεν εξάγουμε αρχεία στον δίσκο)· τα ονόματα με διαδρομές «..» απορρίπτονται παρ' όλα αυτά.</summary>
        public static bool SafeEntryName(string fullName) => !string.IsNullOrEmpty(fullName) && !fullName.Contains("..") && !System.IO.Path.IsPathRooted(fullName);
    }
}
