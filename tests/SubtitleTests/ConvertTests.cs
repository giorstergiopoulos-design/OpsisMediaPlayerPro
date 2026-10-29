using System.Linq;
using MediaPlayerPro.Conv;
using Xunit;

namespace MediaPlayerPro.Tests
{
    public class ConvertTests
    {
        [Fact] public void EveryOfferedFormatBuildsSout()
        {
            foreach (var f in Formats.Video) foreach (var a in f.Audio)
            {
                var s = ConvertCore.BuildVideoSout(new ConvertOptions { Format = f.Id, Audio = a }, @"C:\x\o." + f.Ext);
                Assert.Contains("vcodec=" + f.Vcodec, s); Assert.Contains("mux=" + f.Mux, s); Assert.Contains("dst=\"C:", s);
            }
        }
        [Fact] public void NoH264OrGifOffered()
        {
            Assert.DoesNotContain(Formats.Video, f => f.Vcodec == "h264" || f.Vcodec == "hevc");
            Assert.DoesNotContain("gif", Formats.ImageFormats); Assert.DoesNotContain("webp", Formats.ImageFormats);
        }
        [Fact] public void ValidationCatchesBadInput()
        {
            Assert.NotEmpty(ConvertCore.Validate(new ConvertOptions { Format = "nope" }, 10, 25));
            Assert.NotEmpty(ConvertCore.Validate(new ConvertOptions { Format = "avi", Audio = "opus" }, 10, 25));
            Assert.NotEmpty(ConvertCore.Validate(new ConvertOptions { StartSec = 5, EndSec = 3 }, 10, 25));
            Assert.NotEmpty(ConvertCore.Validate(new ConvertOptions { StartSec = 20 }, 10, 25));
            Assert.NotEmpty(ConvertCore.Validate(new ConvertOptions { Kind = OutKind.FrameSequence, ImageFormat = "gif" }, 10, 25));
            Assert.NotEmpty(ConvertCore.Validate(new ConvertOptions { Kind = OutKind.FrameSequence, EverySeconds = 0 }, 10, 25));
            Assert.Empty(ConvertCore.Validate(new ConvertOptions { Format = "webm8", Audio = "vorbis", Height = 720 }, 10, 25));
        }
        [Fact] public void QuotesInPathRejected() => Assert.Throws<System.ArgumentException>(() => ConvertCore.BuildVideoSout(new ConvertOptions(), "a\"b.mp4"));
        [Fact] public void HeightFpsAndNoAudio()
        {
            var s = ConvertCore.BuildVideoSout(new ConvertOptions { Format = "mp4", Audio = "none", Height = 480, Fps = 24 }, "o.mp4");
            Assert.Contains("height=480", s); Assert.Contains("fps=24", s); Assert.Contains("acodec=none", s);
            Assert.DoesNotContain("acodec=", ConvertCore.BuildVideoSout(new ConvertOptions { Format = "mp4", Audio = "copy" }, "o.mp4"));
        }
        [Fact] public void RangeMaths()
        {
            Assert.Equal((0.0, 0.0), ConvertCore.Range(-1, -1, 100));
            Assert.Equal((10.0, 20.0), ConvertCore.Range(10, 20, 100));
            Assert.Equal((10.0, 0.0), ConvertCore.Range(10, -1, 100));
            Assert.Equal((10.0, 0.0), ConvertCore.Range(10, 5, 100));
            Assert.Equal((10.0, 100.0), ConvertCore.Range(10, 500, 100));
            Assert.Equal(10.0, ConvertCore.EffectiveLength(10, 20, 100)); Assert.Equal(90.0, ConvertCore.EffectiveLength(10, 0, 100));
        }
        [Fact] public void SceneRatio()
        {
            Assert.Equal(50, ConvertCore.SceneRatio(new ConvertOptions { EverySeconds = 2 }, 25));
            Assert.Equal(10, ConvertCore.SceneRatio(new ConvertOptions { EveryFrames = 10 }, 25));
            Assert.Equal(25, ConvertCore.SceneRatio(new ConvertOptions { EverySeconds = 1 }, 0));
            Assert.Equal(10, ConvertCore.ExpectedFrames(new ConvertOptions { EverySeconds = 1 }, 10, 25));
            Assert.Contains("ratio=30", ConvertCore.BuildSceneSout(new ConvertOptions { EverySeconds = 1 }, @"C:\t", "f", 30));
        }
        [Fact] public void UniquePathNeverCollides()
        {
            var taken = new System.Collections.Generic.HashSet<string>();
            string p1 = ConvertCore.UniquePath(@"C:\v", "movie", "_mp4", "mp4", taken.Contains); taken.Add(p1);
            string p2 = ConvertCore.UniquePath(@"C:\v", "movie", "_mp4", "mp4", taken.Contains); taken.Add(p2);
            string p3 = ConvertCore.UniquePath(@"C:\v", "movie", "_mp4", "mp4", taken.Contains);
            Assert.Equal(3, new[] { p1, p2, p3 }.Distinct().Count()); Assert.EndsWith("movie_mp4 (3).mp4", p3);
            Assert.True(ConvertCore.SameFile(@"C:\a\b.mp4", @"C:\A\x\..\B.MP4"));
        }
        [Fact] public void SafeNameAndEta()
        {
            Assert.Equal("a_b_c", ConvertCore.SafeName("a:b*c")); Assert.Equal("output", ConvertCore.SafeName("..."));
            Assert.Equal("1:05", ConvertCore.FormatEta(65)); Assert.Equal("1:01:01", ConvertCore.FormatEta(3661)); Assert.Equal("--:--", ConvertCore.FormatEta(-1));
            Assert.Equal(30.0, ConvertCore.EtaSeconds(0.25, 10), 3); Assert.Equal(-1.0, ConvertCore.EtaSeconds(0, 10));
        }
        [Fact] public void QualityScalesBitrate() => Assert.True(ConvertCore.VideoKbps(Quality.High, 720, 0) > ConvertCore.VideoKbps(Quality.Low, 720, 0));
    }
}
namespace MediaPlayerPro.Tests
{
    public class FamilyIconTests
    {
        [Fact] public void EveryAppInCatalogueHasAnEmbeddableIcon()
        {
            string root = System.AppContext.BaseDirectory; while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root, "suite", "apps.json"))) root = System.IO.Path.GetDirectoryName(root);
            Assert.NotNull(root);
            var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "suite", "apps.json")));
            var ids = doc.RootElement.GetProperty("apps").EnumerateArray().Select(a => a.GetProperty("id").GetString()).ToList();
            Assert.Equal(7, ids.Count);
            foreach (var id in ids) Assert.True(System.IO.File.Exists(System.IO.Path.Combine(root, "suite", "icons", id + ".ico")), "missing icon for " + id);
            var csproj = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "OpsisMediaPlayerPro.csproj")); Assert.Contains("suite/icons/*.ico", csproj);
        }
    }
}
