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
