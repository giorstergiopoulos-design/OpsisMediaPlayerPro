using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaPlayerPro.SubCore;
using Xunit;

namespace MediaPlayerPro.Tests
{
    public class SrtTests
    {
        [Fact]
        public void Parses_a_normal_file_with_crlf_bom_and_multiline_text()
        {
            var text = "﻿1\r\n00:00:01,500 --> 00:00:03,000\r\nHello\r\nworld\r\n\r\n2\r\n01:02:03,004 --> 01:02:05,000\r\nSecond\r\n";
            var c = Srt.Parse(text);
            Assert.Equal(2, c.Count);
            Assert.Equal(1.5, c[0].Start, 3); Assert.Equal(3.0, c[0].End, 3); Assert.Equal("Hello\nworld", c[0].Text);
            Assert.Equal(3723.004, c[1].Start, 3);
        }

        [Fact]
        public void Tolerates_missing_numbers_dot_separators_and_short_fractions()
        {
            var c = Srt.Parse("00:00:01.5 --> 00:00:02.25\nA\n\n00:00:05,05 --> 00:00:06,5\nB\n");
            Assert.Equal(2, c.Count);
            Assert.Equal(1.5, c[0].Start, 3); Assert.Equal(2.25, c[0].End, 3);
            Assert.Equal(5.05, c[1].Start, 3);         // το κλάσμα διαβάζεται ως ψηφία δεκαδικού: ",05" = 0,05 s, ",5" = 0,5 s
        }

        [Fact]
        public void Garbage_and_empty_input_give_no_cues_not_exceptions()
        {
            Assert.Empty(Srt.Parse(null)); Assert.Empty(Srt.Parse("")); Assert.Empty(Srt.Parse("not a subtitle\nat all"));
        }

        [Fact]
        public void An_end_before_the_start_is_clamped()
        {
            var c = Srt.Parse("00:00:05,000 --> 00:00:04,000\nX\n");
            Assert.Equal(5, c[0].End, 3);
        }

        [Fact]
        public void Write_then_parse_round_trips()
        {
            var cues = new List<Cue> { new Cue(1.234, 2.5, "a\nb"), new Cue(3600.5, 3601, "late") };
            var back = Srt.Parse(Srt.Write(cues));
            Assert.Equal(2, back.Count);
            Assert.Equal(1.234, back[0].Start, 3); Assert.Equal("a\nb", back[0].Text); Assert.Equal(3600.5, back[1].Start, 3);
        }

        [Theory]
        [InlineData(0.9996, "00:00:01,000")]       // ποτέ «00:00:00,1000»
        [InlineData(59.9999, "00:01:00,000")]
        [InlineData(3599.9996, "01:00:00,000")]
        [InlineData(-3, "00:00:00,000")]
        public void Timestamps_round_once_and_never_show_1000_ms(double t, string expected) => Assert.Equal(expected, Srt.Ts(t));

        [Fact]
        public void Transform_scales_shifts_and_drops_lines_that_end_before_zero()
        {
            var cues = new List<Cue> { new Cue(1, 2, "gone"), new Cue(10, 12, "kept") };
            var r = Srt.Transform(cues, 2.0, -5);
            Assert.Single(r);                          // 2*2-5 = -1 <= 0 → αφαιρείται
            Assert.Equal(15, r[0].Start, 6); Assert.Equal(19, r[0].End, 6);
        }

        [Fact]
        public void Transform_clamps_a_start_below_zero()
        {
            var r = Srt.Transform(new List<Cue> { new Cue(1, 4, "x") }, 1, -2);
            Assert.Equal(0, r[0].Start, 6); Assert.Equal(2, r[0].End, 6);
        }
    }

    public class FormatTests
    {
        [Fact]
        public void WebVtt_without_hours_and_with_cue_settings_converts()
        {
            var vtt = "WEBVTT\n\n00:01.000 --> 00:03.500 align:start position:0%\n<v Bob>Hello <c.yellow>there</c>\n\n01:00:00.000 --> 01:00:02.000\n<i>Italic</i>\n";
            var cues = Srt.Parse(SubFormats.ToSrt(vtt, ".vtt"));
            Assert.Equal(2, cues.Count);
            Assert.Equal(1.0, cues[0].Start, 3); Assert.Equal(3.5, cues[0].End, 3);
            Assert.Equal("Hello there", cues[0].Text);
            Assert.Equal("<i>Italic</i>", cues[1].Text);            // οι τεκμηριωμένες ετικέτες i/b/u/font μένουν
        }

        [Fact]
        public void Ass_dialogue_converts_with_commas_in_text_and_tags_removed()
        {
            var ass = "[Script Info]\nTitle: x\n\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
                      "Dialogue: 0,0:00:01.50,0:00:03.00,Default,,0,0,0,,{\\i1}Hello, there{\\i0}\\Nsecond line\n" +
                      "Dialogue: 0,0:00:05.00,0:00:06.00,Default,,0,0,0,,{\\p1}m 0 0 l 10 10{\\p0}\n" +
                      "Comment: 0,0:00:07.00,0:00:08.00,Default,,0,0,0,,ignored\n";
            var cues = Srt.Parse(SubFormats.ToSrt(ass, ".ass"));
            Assert.Single(cues);                                      // το σχέδιο (\p1) και το Comment αγνοούνται
            Assert.Equal(1.5, cues[0].Start, 3); Assert.Equal("Hello, there\nsecond line", cues[0].Text);
        }

        [Fact]
        public void Unknown_or_empty_conversion_returns_null()
        {
            Assert.Null(SubFormats.ToSrt("nothing", ".srt"));
            Assert.Null(SubFormats.ToSrt("[Events]\nFormat: Foo\n", ".ass"));
        }

        [Fact]
        public void Decoding_handles_boms_utf16_utf8_and_legacy_code_pages()
        {
            const string greek = "Καλημέρα κόσμε";
            Assert.Equal(greek, SubFormats.Decode(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(greek)).ToArray(), 1253));
            Assert.Equal(greek, SubFormats.Decode(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(greek)).ToArray(), 1253));
            Assert.Equal(greek, SubFormats.Decode(Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(greek)).ToArray(), 1253));
            Assert.Equal(greek, SubFormats.Decode(Encoding.Unicode.GetBytes(greek.PadRight(60, ' ')), 1253).TrimEnd());      // UTF-16 χωρίς BOM
            Assert.Equal(greek, SubFormats.Decode(Encoding.UTF8.GetBytes(greek), 1253));
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Assert.Equal(greek, SubFormats.Decode(Encoding.GetEncoding(1253).GetBytes(greek), 1253));                     // ISO/Windows ελληνικά
            Assert.Equal("Привет", SubFormats.Decode(Encoding.GetEncoding(1251).GetBytes("Привет"), 1251));
        }

        [Fact]
        public void Decoding_never_throws_on_junk()
        {
            Assert.Equal("", SubFormats.Decode(null, 1252)); Assert.Equal("", SubFormats.Decode(new byte[0], 1252));
            var junk = new byte[] { 0xFF, 0xFE, 0xFD, 0x80, 0x81 };
            Assert.NotNull(SubFormats.Decode(junk, 99999));            // άκυρη κωδική σελίδα → Latin1
        }
    }

    public class RankTests
    {
        private static double S(string video, string cand, bool hi = false, double dl = 0) => SubRank.Score(ReleaseInfo.Parse(video), cand, hi, dl);

        [Fact]
        public void Release_names_are_parsed()
        {
            var r = ReleaseInfo.Parse("The.Mandalorian.S02E05.1080p.WEB-DL.x264-NTb.mkv");
            Assert.Equal(2, r.Season); Assert.Equal(5, r.Episode); Assert.Equal("1080p", r.Resolution); Assert.Equal("web", r.Source); Assert.Equal("x264", r.Codec); Assert.Equal("ntb", r.Group);
            Assert.Contains("mandalorian", r.TitleTokens); Assert.DoesNotContain("the", r.TitleTokens);
            var m = ReleaseInfo.Parse("Blade Runner 2049 (2017) 2160p BluRay HEVC-GRP");
            Assert.Equal(2017, m.Year); Assert.Equal("2160p", m.Resolution); Assert.Equal("bluray", m.Source); Assert.Equal("x265", m.Codec); Assert.Null(m.Season);
        }

        [Theory]
        [InlineData("Show.1x07.HDTV.avi", 1, 7)]
        [InlineData("Show Season 3 Episode 12 720p", 3, 12)]
        [InlineData("show.s10e101.mkv", 10, 101)]
        public void Season_episode_notations(string name, int s, int e)
        {
            var r = ReleaseInfo.Parse(name); Assert.Equal(s, r.Season); Assert.Equal(e, r.Episode);
        }

        [Fact]
        public void A_title_that_starts_with_a_year_keeps_its_title()
        {
            var r = ReleaseInfo.Parse("2012 (2009) 1080p BluRay x264-GRP");
            Assert.Equal(2009, r.Year);
            Assert.Contains("2012", r.TitleTokens);
        }

        [Fact]
        public void The_right_episode_beats_a_more_popular_wrong_episode()
        {
            string video = "Show.Name.S01E02.720p.HDTV.x264-LOL";
            double right = S(video, "Show Name S01E02 720p HDTV x264-LOL", dl: 0.0);
            double wrong = S(video, "Show Name S01E03 720p HDTV x264-LOL", dl: 1.0);
            Assert.True(right > wrong + 50);
        }

        [Fact]
        public void The_same_group_and_source_rank_higher()
        {
            string video = "Movie.Title.2019.1080p.BluRay.x264-SPARKS";
            double same = S(video, "Movie Title 2019 1080p BluRay x264-SPARKS");
            double other = S(video, "Movie Title 2019 720p WEB-DL x264-RARBG");
            Assert.True(same > other + 20);
        }

        [Fact]
        public void A_different_year_is_penalised_and_hearing_impaired_slightly()
        {
            string video = "Movie Title (2019) 1080p";
            Assert.True(S(video, "Movie Title 2019") > S(video, "Movie Title 1994"));
            Assert.True(S(video, "Movie Title 2019", hi: false) > S(video, "Movie Title 2019", hi: true));
        }

        [Fact]
        public void Metadata_from_the_provider_overrides_the_text_parse()
        {
            var v = ReleaseInfo.Parse("Show.S01E02.720p");
            double a = SubRank.Score(v, "Show", false, 0, candSeason: 1, candEpisode: 2);
            double b = SubRank.Score(v, "Show", false, 0, candSeason: 1, candEpisode: 9);
            Assert.True(a > b + 100);
        }

        [Fact]
        public void Plausibility_prefers_a_subtitle_that_ends_near_the_end_of_the_video()
        {
            var good = Enumerable.Range(0, 100).Select(i => new Cue(i * 60, i * 60 + 3)).ToList();                 // τελειώνει στα ~5943 s
            var half = Enumerable.Range(0, 50).Select(i => new Cue(i * 60, i * 60 + 3)).ToList();                  // τελειώνει στα ~2943 s
            Assert.True(SubRank.Plausibility(good, 6000) > 0.9);
            Assert.True(SubRank.Plausibility(half, 6000) < 0.1);
            Assert.True(SubRank.Plausibility(good, 3000) < 0.1);                                                      // 2 φορές μεγαλύτερος από το βίντεο
            Assert.Equal(0, SubRank.Plausibility(new List<Cue>(), 6000));
            Assert.Equal(0, SubRank.Plausibility(good, 0));
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Συγχρονισμός με συνθετικά δεδομένα: ξέρουμε την «αλήθεια» και ελέγχουμε ότι ανακτάται.
    // ---------------------------------------------------------------------------------------------------------------
    public class SyncTests
    {
        private const int Rate = 8000;

        private static List<Cue> MakeCues(int seed, double length)
        {
            var rng = new Random(seed); var cues = new List<Cue>(); double t = 5;
            while (t < length) { double d = 1.2 + rng.NextDouble() * 3.0; cues.Add(new Cue(t, t + d, "line " + cues.Count)); t += d + 0.8 + rng.NextDouble() * 6.0; }
            return cues;
        }

        /// <summary>Ήχος 8 kHz: ομιλία (δυνατός θόρυβος) όταν μιλά η γραμμή στο ΒΙΝΤΕΟ (video = ratio*cue + shift), ήσυχος θόρυβος αλλού, και «μουσική» που δεν έχει υπότιτλο.</summary>
        private static short[] Audio(IReadOnlyList<Cue> cues, double ratio, double shift, double segStart, double segLen, int seed, double musicFraction = 0.0)
        {
            var rng = new Random(seed); int n = (int)(segLen * Rate); var pcm = new short[n];
            for (int i = 0; i < n; i++) pcm[i] = (short)rng.Next(-120, 121);
            void Burst(double a, double b, int amp)
            {
                int i0 = Math.Max(0, (int)((a - segStart) * Rate)), i1 = Math.Min(n, (int)((b - segStart) * Rate));
                for (int i = i0; i < i1; i++) pcm[i] = (short)rng.Next(-amp, amp + 1);
            }
            foreach (var c in cues) Burst(c.Start * ratio + shift, c.End * ratio + shift, 5000);
            if (musicFraction > 0)
            {
                int bursts = (int)(segLen / 30 * musicFraction * 6);
                for (int k = 0; k < bursts; k++) { double a = segStart + rng.NextDouble() * segLen; Burst(a, a + 2 + rng.NextDouble() * 5, 5000); }
            }
            return pcm;
        }

        private static double MaxError(SyncSolution s, double ratio, double shift, double upTo = 4000)
        {
            double worst = 0;
            for (double t = 0; t <= upTo; t += 100) worst = Math.Max(worst, Math.Abs((s.Ratio * t + s.Shift) - (ratio * t + shift)));
            return worst;
        }

        [Theory]
        [InlineData(37.4)]
        [InlineData(-12.8)]
        [InlineData(0.0)]
        [InlineData(214.62)]
        public void A_constant_shift_is_recovered_within_a_tenth_of_a_second(double shift)
        {
            var cues = MakeCues(1, 4000);
            var pcm = Audio(cues, 1, shift, 0, 600, seed: 11);
            var fit = SpeechAlign.FitSegment(pcm, Rate, 0, cues, 0, 300);
            Assert.True(fit.Good, $"score {fit.Score}, prominence {fit.Prominence}");
            Assert.InRange(fit.Shift, shift - 0.1, shift + 0.1);
            Assert.True(fit.Score > 0.5);
        }

        [Fact]
        public void The_fps_conversion_25_to_23976_is_found_and_applied_across_the_whole_film()
        {
            double r = 25 / 23.976, s = 3.2;
            var cues = MakeCues(2, 4000);
            var a = SpeechAlign.FitFirst(Audio(cues, r, s, 0, 600, 21), Rate, 0, cues, 0, 120);
            var b = SpeechAlign.FitSegment(Audio(cues, r, s, 3000, 600, 22), Rate, 3000, cues, a.Shift, 40, new[] { a.Ratio });
            Assert.Equal(r, a.Ratio, 6);
            var sol = SpeechAlign.Combine(a, 300, b, 3300);
            Assert.True(sol.Good);
            Assert.True(MaxError(sol, r, s) < 0.15, "max error " + MaxError(sol, r, s));
        }

        [Fact]
        public void An_unknown_ratio_that_is_not_a_standard_fps_pair_is_still_solved_from_two_segments()
        {
            double r = 1.0123, s = -8.5;
            var cues = MakeCues(3, 4000);
            var a = SpeechAlign.FitFirst(Audio(cues, r, s, 0, 600, 31), Rate, 0, cues, 0, 100);
            Assert.True(a.Good, $"score {a.Score}, prominence {a.Prominence}, ratio {a.Ratio}");
            var b = SpeechAlign.FitSegment(Audio(cues, r, s, 3000, 600, 32), Rate, 3000, cues, a.Shift, 90, new[] { a.Ratio });
            var sol = SpeechAlign.Combine(a, 300, b, 3300);
            Assert.Equal("shift+ratio", sol.Detail);
            Assert.True(MaxError(sol, r, s) < 0.2, "max error " + MaxError(sol, r, s));
        }

        [Fact]
        public void Two_agreeing_segments_give_a_constant_shift_without_inventing_a_ratio()
        {
            var cues = MakeCues(4, 4000);
            var a = SpeechAlign.FitSegment(Audio(cues, 1, 5.5, 0, 600, 41), Rate, 0, cues, 0, 60);
            var b = SpeechAlign.FitSegment(Audio(cues, 1, 5.5, 3000, 600, 42), Rate, 3000, cues, a.Shift, 20);
            var sol = SpeechAlign.Combine(a, 300, b, 3300);
            Assert.Equal(1.0, sol.Ratio);
            Assert.InRange(sol.Shift, 5.4, 5.6);
        }

        [Fact]
        public void Music_without_subtitles_in_the_audio_does_not_break_the_match()
        {
            var cues = MakeCues(5, 4000);
            var fit = SpeechAlign.FitSegment(Audio(cues, 1, 20.0, 0, 600, 51, musicFraction: 0.5), Rate, 0, cues, 0, 120);
            Assert.True(fit.Good, $"score {fit.Score}, prominence {fit.Prominence}");
            Assert.InRange(fit.Shift, 19.9, 20.1);
        }

        [Fact]
        public void Unrelated_audio_is_rejected_instead_of_applying_a_random_shift()
        {
            var cues = MakeCues(6, 4000);
            var other = MakeCues(60, 4000);                       // ΑΛΛΗ ταινία: άσχετες γραμμές
            var bad = 0;
            for (int seed = 0; seed < 4; seed++)
            {
                var fit = SpeechAlign.FitSegment(Audio(other, 1, 0, 0, 600, 61 + seed), Rate, 0, cues, 0, 300);
                if (fit.Good) bad++;
            }
            Assert.Equal(0, bad);
        }

        [Fact]
        public void Silence_or_a_too_short_clip_cannot_be_aligned()
        {
            var cues = MakeCues(7, 4000);
            Assert.False(SpeechAlign.FitSegment(new short[Rate * 600], Rate, 0, cues, 0, 60).Good);
            Assert.False(SpeechAlign.FitSegment(new short[Rate * 5], Rate, 0, cues, 0, 60).Good);
            Assert.False(SpeechAlign.FitSegment(null, Rate, 0, cues, 0, 60).Good);
            Assert.False(SpeechAlign.FitSegment(Audio(cues, 1, 0, 0, 600, 71), Rate, 0, new List<Cue>(), 0, 60).Good);
        }

        [Fact]
        public void A_weak_first_segment_means_nothing_is_applied()
        {
            var sol = SpeechAlign.Combine(new SegmentFit { Score = 0.01, Prominence = 0 }, 300, null, 0);
            Assert.False(sol.Good);
        }

        [Fact]
        public void An_implausible_second_segment_is_ignored_and_the_constant_shift_is_kept()
        {
            var a = new SegmentFit { Ratio = 1, Shift = 5, Score = 0.6, Prominence = 0.3 };
            var b = new SegmentFit { Ratio = 1, Shift = 900, Score = 0.6, Prominence = 0.3 };       // «κλίση» 25 % — αδύνατο
            var sol = SpeechAlign.Combine(a, 300, b, 3300);
            Assert.Equal(1.0, sol.Ratio); Assert.Equal(5, sol.Shift, 6);
        }

        [Fact]
        public void The_transformed_file_lines_up_with_the_audio_end_to_end()
        {
            double r = 25 / 23.976, s = 2.0;
            var cues = MakeCues(8, 4000);
            var a = SpeechAlign.FitSegment(Audio(cues, r, s, 0, 600, 81), Rate, 0, cues, 0, 60, SpeechAlign.FpsRatios);
            var b = SpeechAlign.FitSegment(Audio(cues, r, s, 3000, 600, 82), Rate, 3000, cues, a.Shift, 40, new[] { a.Ratio });
            var sol = SpeechAlign.Combine(a, 300, b, 3300);
            var fixedCues = Srt.Parse(Srt.Write(Srt.Transform(cues, sol.Ratio, sol.Shift)));
            for (int i = 0; i < cues.Count; i += 37)
                Assert.InRange(fixedCues[i].Start, cues[i].Start * r + s - 0.25, cues[i].Start * r + s + 0.25);
        }
    }
}
