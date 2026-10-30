using System;
using System.IO;

namespace MediaPlayerPro.Conv
{
    /// <summary>Εκτελέσιμα των Windows μόνο με πλήρη διαδρομή (ποτέ μέσω PATH / τρέχοντος φακέλου) και εκκίνηση μόνο υπαρκτών τοπικών .exe.</summary>
    public static class TrustedExe
    {
        public static string Windows(string exe) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), exe);
        public static string System32(string exe) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), exe);
        public static string Explorer => Windows("explorer.exe");

        public static bool IsLaunchable(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal)) return false;
                return string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
            }
            catch (Exception) { return false; }
        }
    }
}
