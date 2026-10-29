using System.Globalization;

namespace MediaPlayerPro.Conv
{
    /// <summary>Ελληνικά / English για τα κείμενα του μετατροπέα και των νέων μενού: «el» όταν η γλώσσα UI των Windows είναι ελληνική, αλλιώς «en» (όπως το SuiteLang).</summary>
    public static class Loc
    {
        public static string Lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";
        public static string T(string el, string en) => Lang == "el" ? el : en;
    }
}
