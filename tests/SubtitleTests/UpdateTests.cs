using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Opsis.Suite;
using Opsis.SuiteTests;
using Xunit;

namespace MediaPlayerPro.Tests
{
    public class UpdateFlowTests
    {
        static string Hex(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

        static (FakeHttp http, UpdateInfo info, byte[] setup) Make(string tag, byte[] checksumFor = null, bool withSum = true)
        {
            var setup = Encoding.ASCII.GetBytes("MZ fake installer " + tag + new string('x', 5000));
            var http = new FakeHttp();
            var rel = new ReleaseDto { Tag = tag, Name = tag, Url = "https://github.com/giorstergiopoulos-design/OpsisMediaPlayerPro/releases/tag/" + tag };
            rel.Assets.Add(new ReleaseAsset { Name = "OpsisMediaPlayerPro-Setup.exe", Url = "https://github.com/giorstergiopoulos-design/OpsisMediaPlayerPro/releases/download/" + tag + "/OpsisMediaPlayerPro-Setup.exe" });
            http.Files[rel.Assets[0].Url] = setup;
            if (withSum)
            {
                var sumUrl = rel.Assets[0].Url + ".sha256"; rel.Assets.Add(new ReleaseAsset { Name = "OpsisMediaPlayerPro-Setup.exe.sha256", Url = sumUrl });
                http.Responses[sumUrl] = new HttpResult { Status = 200, Body = Hex(checksumFor ?? setup) + " *OpsisMediaPlayerPro-Setup.exe\n" };
            }
            return (http, new UpdateInfo { Tag = tag, Release = rel, Url = rel.Url }, setup);
        }

        static string Tmp() { var d = Path.Combine(Path.GetTempPath(), "opsis_upd_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

        [Fact] public async Task VerifiedDownloadSucceeds()
        {
            var (http, info, setup) = Make("v9.0.0"); var dir = Tmp();
            try { var r = await SelfUpdate.PrepareAsync(http, info, "OpsisMediaPlayerPro-Setup.exe", dir); Assert.Equal(DownloadStatus.Ok, r.Status); Assert.Equal(Hex(setup), r.Actual); Assert.True(File.Exists(r.Path)); }
            finally { Directory.Delete(dir, true); }
        }

        [Fact] public async Task HashMismatchIsRefusedAndDeleted()
        {
            var (http, info, _) = Make("v9.0.0", checksumFor: Encoding.ASCII.GetBytes("something else")); var dir = Tmp();
            try { var r = await SelfUpdate.PrepareAsync(http, info, "OpsisMediaPlayerPro-Setup.exe", dir); Assert.Equal(DownloadStatus.Mismatch, r.Status); Assert.Empty(Directory.GetFiles(dir)); }
            finally { Directory.Delete(dir, true); }
        }

        [Fact] public async Task NoChecksumMeansNoDownload()
        {
            var (http, info, _) = Make("v9.0.0", withSum: false); var dir = Tmp();
            try { var r = await SelfUpdate.PrepareAsync(http, info, "OpsisMediaPlayerPro-Setup.exe", dir); Assert.Equal(DownloadStatus.NoChecksum, r.Status); Assert.DoesNotContain(http.Requests, q => q.StartsWith("DOWNLOAD")); }
            finally { Directory.Delete(dir, true); }
        }

        [Fact] public async Task MissingAssetOrRelease()
        {
            var (http, info, _) = Make("v9.0.0"); var dir = Tmp();
            try
            {
                Assert.Equal(DownloadStatus.NoAsset, (await SelfUpdate.PrepareAsync(http, info, "Other-Setup.exe", dir)).Status);
                Assert.Equal(DownloadStatus.NoAsset, (await SelfUpdate.PrepareAsync(http, new UpdateInfo { Tag = "v1" }, "OpsisMediaPlayerPro-Setup.exe", dir)).Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact] public void SkipVersionIsRemembered_NewerStillAnnounced()
        {
            var st = new MemoryStore(); var u = new UpdateChecker(new FakeHttp(), st, () => DateTime.UtcNow, "o", "r", "1.0.0");
            Assert.Equal("", u.SkippedTag ?? ""); u.SkipVersion("v1.1.0"); Assert.Equal("v1.1.0", u.SkippedTag); u.SkipVersion(null); Assert.Equal("", u.SkippedTag ?? "");
        }

        [Fact] public async Task SkippedVersionNotOfferedUntilNewerOne()
        {
            var http = new FakeHttp(); var st = new MemoryStore(); var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
            string Json(string tag) => "{\"tag_name\":\"" + tag + "\",\"name\":\"" + tag + "\",\"html_url\":\"https://github.com/o/r/releases/tag/" + tag + "\",\"body\":\"notes\",\"draft\":false,\"prerelease\":false,\"assets\":[]}";
            http.Responses["https://api.github.com/repos/o/r/releases/latest"] = new HttpResult { Status = 200, Body = Json("v1.1.0"), ETag = "e1" };
            var u = new UpdateChecker(http, st, () => now, "o", "r", "1.0.0");
            var first = await u.CheckAsync(); Assert.NotNull(first); Assert.Equal("notes", first.Notes);
            u.SkipVersion("v1.1.0"); now = now.AddDays(2);
            Assert.Null(await u.CheckAsync());
            http.Responses["https://api.github.com/repos/o/r/releases/latest"] = new HttpResult { Status = 200, Body = Json("v1.2.0"), ETag = "e2" };
            now = now.AddDays(2); var newer = await u.CheckAsync(); Assert.NotNull(newer); Assert.Equal("v1.2.0", newer.Tag);
        }

        [Fact] public void ScriptRechecksHashAndEscapesPaths()
        {
            string sha = new string('a', 64); var s = SelfUpdate.BuildScript(@"C:\Users\O'Brien\setup.exe", sha, 1234, @"C:\Program Files\Opsis Media Player Pro\OpsisMediaPlayerPro.exe");
            Assert.Contains("Get-FileHash", s); Assert.Contains(sha, s); Assert.Contains("O''Brien", s); Assert.Contains("Wait-Process -Id 1234", s); Assert.Contains("/SILENT", s); Assert.Contains("-Verb RunAs", s);
            Assert.Throws<ArgumentException>(() => SelfUpdate.BuildScript("a.exe", "short", 1, "b.exe"));
        }

        [Fact] public void TrustedExePaths()
        {
            Assert.True(Path.IsPathFullyQualified(MediaPlayerPro.Conv.TrustedExe.Explorer));
            Assert.True(MediaPlayerPro.Conv.TrustedExe.IsLaunchable(MediaPlayerPro.Conv.TrustedExe.Windows("notepad.exe")) || MediaPlayerPro.Conv.TrustedExe.IsLaunchable(MediaPlayerPro.Conv.TrustedExe.System32("notepad.exe")));
            Assert.False(MediaPlayerPro.Conv.TrustedExe.IsLaunchable(@"\\srv\x\a.exe")); Assert.False(MediaPlayerPro.Conv.TrustedExe.IsLaunchable("notepad.exe")); Assert.False(MediaPlayerPro.Conv.TrustedExe.IsLaunchable(null));
        }
    }
}

namespace MediaPlayerPro.Tests
{
    public class UpdateLogTests
    {
        [Fact] public void ScriptLogsEveryStageAndFailures()
        {
            var s = Opsis.Suite.SelfUpdate.BuildScript(@"C:\u\setup.exe", new string('c', 64), 7, @"C:\p\a.exe", logPath: @"C:\u\last-update.log");
            Assert.Contains("last-update.log", s); Assert.Contains("FAILED: SHA-256 changed", s); Assert.Contains("UAC declined", s); Assert.Contains("exit code", s); Assert.Contains("installed", s);
        }
    }
}
