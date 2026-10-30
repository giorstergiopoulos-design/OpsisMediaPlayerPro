using System.Text.RegularExpressions;
using MediaPlayerPro.Conv;
using Xunit;

namespace MediaPlayerPro.Tests
{
    // Κάθε κείμενο του μετατροπέα και των νέων μενού υπάρχει στις ΔΥΟ γλώσσες της εφαρμογής (el, en)· δεν επιτρέπονται γυμνά ελληνικά literals.
    public class LocalisationTests
    {
        static string Root() { string r = System.AppContext.BaseDirectory; while (r != null && !System.IO.File.Exists(System.IO.Path.Combine(r, "ConvertWindow.cs"))) r = System.IO.Path.GetDirectoryName(r); return r; }
        static readonly Regex Greek = new Regex(@"[Ͱ-Ͽἀ-῿]");
        const string Str = @"""(?:[^""\\]|\\.)*""";
        static readonly Regex Pair = new Regex(@"T\((" + Str + @"),\s*(" + Str + @")\)");
        static readonly Regex Pair3 = new Regex(@"Loc\.T\((" + Str + @"),\s*(" + Str + @")\)");

        [Theory]
        [InlineData("ConvertWindow.cs")]
        [InlineData("ConvertCore/ConvertCore.cs")]
        [InlineData("SettingsWindow.cs")]
        public void NoUntranslatedGreekLiterals(string file)
        {
            var src = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), file));
            src = Pair3.Replace(src, ""); src = Pair.Replace(src, ""); src = Regex.Replace(src, "//.*", "");
            foreach (Match m in Regex.Matches(src, Str)) Assert.False(Greek.IsMatch(m.Value), file + ": untranslated Greek literal " + m.Value);
        }

        [Fact] public void EveryPairHasBothLanguages()
        {
            int n = 0;
            foreach (var f in new[] { "ConvertWindow.cs", "ConvertCore/ConvertCore.cs", "ConvertSelfTest.cs", "SubProviders.cs", "Subtitles.cs", "SettingsWindow.cs" })
            {
                var src = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), f));
                foreach (var rx in new[] { Pair, Pair3 }) foreach (Match m in rx.Matches(src))
                {
                    n++; string el = m.Groups[1].Value, en = m.Groups[2].Value;
                    Assert.True(el.Length > 2 && en.Length > 2, f + ": empty text");
                    Assert.False(Greek.IsMatch(en), f + ": English text contains Greek: " + en);
                    Assert.Equal(Regex.Matches(el, @"\{\d+\}").Count, Regex.Matches(en, @"\{\d+\}").Count);
                }
            }
            Assert.True(n > 40, "found only " + n);
        }

        [Fact] public void MenuTableHasBothLanguages()
        {
            var src = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), "ConvertSelfTest.cs"));
            var block = src.Substring(src.IndexOf("NewMenuText ="), 1400);
            foreach (Match m in Regex.Matches(block, @"\((""[^""]+""), (""[^""]+""), (""[^""]+"")\)")) Assert.False(Greek.IsMatch(m.Groups[3].Value), m.Value);
        }

        [Fact] public void LocSwitchesLanguage()
        {
            string old = Loc.Lang;
            try { Loc.Lang = "el"; Assert.Equal("α", Loc.T("α", "a")); Loc.Lang = "en"; Assert.Equal("a", Loc.T("α", "a")); } finally { Loc.Lang = old; }
        }
    }
}
