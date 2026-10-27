using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Opsis.Suite;
using Xunit;

namespace Opsis.SuiteTests
{
    internal sealed class FakeHttp : IHttp
    {
        public readonly Dictionary<string, HttpResult> Responses = new Dictionary<string, HttpResult>();
        public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
        public readonly List<string> Requests = new List<string>();
        public readonly List<string> Etags = new List<string>();
        public Task<HttpResult> GetAsync(string url, string etag, TimeSpan timeout)
        {
            Requests.Add(url); Etags.Add(etag);
            return Task.FromResult(Responses.TryGetValue(url, out var r) ? r : new HttpResult { Status = 0 });
        }
        public Task<bool> DownloadAsync(string url, string destination, long maxBytes, TimeSpan timeout)
        {
            Requests.Add("DOWNLOAD " + url);
            if (!Files.TryGetValue(url, out var b)) return Task.FromResult(false);
            File.WriteAllBytes(destination, b); return Task.FromResult(true);
        }
    }

    internal sealed class FakeProbe : IInstallProbe
    {
        public readonly HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<KeyValuePair<string, string>> Entries = new List<KeyValuePair<string, string>>();
        public string Expand(string t) => t.Replace("%LocalAppData%", @"C:\Users\x\AppData\Local").Replace("%ProgramFiles%", @"C:\Program Files").Replace("%AppData%", @"C:\Users\x\AppData\Roaming");
        public bool FileExists(string p) => Files.Contains(p);
        public IEnumerable<KeyValuePair<string, string>> UninstallEntries() => Entries;
    }

    public class SemVerTests
    {
        [Theory]
        [InlineData("v1.2.3", "1.2.2", true)]
        [InlineData("1.10.0", "1.9.9", true)]               // αριθμητική, όχι αλφαβητική σύγκριση
        [InlineData("v2.0", "1.99.99", true)]
        [InlineData("v1.2.3", "1.2.3", false)]
        [InlineData("1.2.3", "v1.2.4", false)]
        [InlineData("1.3.0", "1.3.0-beta.1", true)]         // η τελική έκδοση είναι νεότερη από την προέκδοση
        [InlineData("1.3.0-beta.1", "1.3.0", false)]
        [InlineData("1.3.0-beta.2", "1.3.0-beta.1", true)]
        [InlineData("1.2.3+build5", "1.2.3", false)]
        [InlineData("1.2.3.4", "1.2.3", true)]
        public void IsNewer(string tag, string current, bool expected) => Assert.Equal(expected, SemVer.IsNewer(tag, current));

        [Theory]
        [InlineData("")] [InlineData("latest")] [InlineData("v")] [InlineData("1.x.3")] [InlineData("1.2.3.4.5")] [InlineData("99999999999.1")]
        public void Garbage_is_never_newer_and_never_throws(string tag) { Assert.False(SemVer.IsNewer(tag, "1.0.0")); Assert.False(SemVer.IsNewer("2.0.0", tag)); }
    }

    public class SafeUrlTests
    {
        [Theory]
        [InlineData("https://github.com/o/r/releases/tag/v1", true)]
        [InlineData("https://api.github.com/repos/o/r/releases/latest", true)]
        [InlineData("https://raw.githubusercontent.com/o/r/master/x.json", true)]
        [InlineData("http://github.com/o/r", false)]
        [InlineData("https://github.com.evil.com/o/r", false)]
        [InlineData("https://evilgithub.com/o/r", false)]
        [InlineData("https://user:pw@github.com/o/r", false)]
        [InlineData("https://example.com/a", false)]
        [InlineData("file:///c:/windows/system32/cmd.exe", false)]
        [InlineData("not a url", false)]
        [InlineData("", false)]
        public void Allowed(string url, bool ok) => Assert.Equal(ok, SafeUrl.IsAllowed(url));
    }

    public class ReleaseParseTests
    {
        private const string Good = "{\"tag_name\":\"v1.4.0\",\"name\":\"X 1.4.0\",\"html_url\":\"https://github.com/o/r/releases/tag/v1.4.0\",\"draft\":false,\"prerelease\":false,\"body\":\"notes\"," +
            "\"assets\":[{\"name\":\"Setup.exe\",\"browser_download_url\":\"https://github.com/o/r/releases/download/v1.4.0/Setup.exe\",\"size\":123,\"digest\":\"sha256:" + "ab" + "cdef0123456789abcdef0123456789abcdef0123456789abcdef01234567" + "\"}," +
            "{\"name\":\"evil.exe\",\"browser_download_url\":\"https://evil.example/evil.exe\",\"size\":1}]}";

        [Fact]
        public void A_valid_release_parses_and_untrusted_asset_urls_are_dropped()
        {
            var r = ReleaseDto.Parse(Good);
            Assert.NotNull(r); Assert.Equal("v1.4.0", r.Tag); Assert.Single(r.Assets); Assert.Equal("Setup.exe", r.Assets[0].Name); Assert.Equal(123, r.Assets[0].Size);
        }

        [Theory]
        [InlineData("")] [InlineData("[]")] [InlineData("{}")] [InlineData("{not json")]
        [InlineData("{\"tag_name\":\"v1\",\"html_url\":\"https://evil.example/x\"}")]
        [InlineData("{\"tag_name\":\"\",\"html_url\":\"https://github.com/o/r\"}")]
        public void Bad_releases_are_rejected(string json) => Assert.Null(ReleaseDto.Parse(json));

        [Fact]
        public void An_oversize_response_is_rejected() => Assert.Null(ReleaseDto.Parse(new string(' ', 400_000) + Good));
    }

    public class UpdateCheckerTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        private const string Url = "https://api.github.com/repos/o/r/releases/latest";
        private static string Rel(string tag, bool pre = false, bool draft = false) =>
            "{\"tag_name\":\"" + tag + "\",\"name\":\"n\",\"html_url\":\"https://github.com/o/r/releases/tag/" + tag + "\",\"prerelease\":" + (pre ? "true" : "false") + ",\"draft\":" + (draft ? "true" : "false") + ",\"assets\":[]}";

        private static (UpdateChecker C, FakeHttp H, MemoryStore S, Func<DateTime> Now, Action<TimeSpan> Advance) Make(string current = "1.0.0")
        {
            var h = new FakeHttp(); var s = new MemoryStore(); var now = T0;
            var c = new UpdateChecker(h, s, () => now, "o", "r", current);
            return (c, h, s, () => now, d => now += d);
        }

        [Fact]
        public async Task A_newer_release_is_reported_with_its_page()
        {
            var (c, h, _, _, _) = Make("1.0.0"); h.Responses[Url] = new HttpResult { Status = 200, Body = Rel("v1.1.0"), ETag = "\"a\"" };
            var u = await c.CheckAsync();
            Assert.Equal("v1.1.0", u.Tag); Assert.Equal("https://github.com/o/r/releases/tag/v1.1.0", u.Url);
        }

        [Fact]
        public async Task The_same_or_older_version_reports_nothing()
        {
            var (c, h, _, _, _) = Make("1.1.0"); h.Responses[Url] = new HttpResult { Status = 200, Body = Rel("v1.1.0") };
            Assert.Null(await c.CheckAsync());
        }

        [Fact]
        public async Task Prereleases_and_drafts_are_ignored()
        {
            var (c, h, _, _, _) = Make("1.0.0"); h.Responses[Url] = new HttpResult { Status = 200, Body = Rel("v9.0.0", pre: true) };
            Assert.Null(await c.CheckAsync(force: true));
            h.Responses[Url] = new HttpResult { Status = 200, Body = Rel("v9.0.0", draft: true) };
            Assert.Null(await c.CheckAsync(force: true));
        }

        [Fact]
        public async Task It_asks_at_most_once_in_24_hours_then_again()
        {
            var (c, h, _, _, adv) = Make(); h.Responses[Url] = new HttpResult { Status = 200, Body = Rel("v1.0.0") };
            await c.CheckAsync(); await c.CheckAsync(); await c.CheckAsync();
            Assert.Single(h.Requests);
            adv(TimeSpan.FromHours(23)); await c.CheckAsync(); Assert.Single(h.Requests);
            adv(TimeSpan.FromHours(2)); await c.CheckAsync(); Assert.Equal(2, h.Requests.Count);
        }

        [Fact]
        public async Task A_304_reuses_the_cached_release_and_sends_the_etag()
        {
            var (c, h, _, _, adv) = Make("1.0.0"); h.Responses[Url] = new HttpResult { Status = 200, Body = Rel("v1.2.0"), ETag = "\"e1\"" };
            Assert.NotNull(await c.CheckAsync());
            c.MarkNotified("v1.2.0");
            adv(TimeSpan.FromHours(25)); h.Responses[Url] = new HttpResult { Status = 304 };
            Assert.Null(await c.CheckAsync());                                       // ειδοποιήθηκε ήδη
            Assert.Equal("\"e1\"", h.Etags.Last());
            Assert.NotNull(await c.CheckAsync(force: true));                         // το force χρησιμοποιεί την cache και ξαναδείχνει
        }

        [Fact]
        public async Task Each_version_is_announced_once_but_a_newer_one_is_announced_again()
        {
            var (c, h, _, _, adv) = Make("1.0.0"); h.Responses[Url] = new HttpResult { Status = 200, Body = Rel("v1.1.0") };
            var first = await c.CheckAsync(); Assert.NotNull(first); c.MarkNotified(first.Tag);
            adv(TimeSpan.FromHours(25)); Assert.Null(await c.CheckAsync());
            h.Responses[Url] = new HttpResult { Status = 200, Body = Rel("v1.2.0") }; adv(TimeSpan.FromHours(25));
            Assert.Equal("v1.2.0", (await c.CheckAsync()).Tag);
        }

        [Fact]
        public async Task A_failed_request_is_silent_and_not_retried_for_an_hour()
        {
            var (c, h, _, _, adv) = Make();
            Assert.Null(await c.CheckAsync()); Assert.Single(h.Requests);
            adv(TimeSpan.FromMinutes(30)); Assert.Null(await c.CheckAsync()); Assert.Single(h.Requests);
            adv(TimeSpan.FromMinutes(40)); await c.CheckAsync(); Assert.Equal(2, h.Requests.Count);
            h.Responses[Url] = new HttpResult { Status = 403, Body = "rate limited" };
            adv(TimeSpan.FromHours(2)); Assert.Null(await c.CheckAsync());
        }

        [Fact]
        public async Task The_setting_switches_it_off_but_force_still_works()
        {
            var (c, h, _, _, _) = Make(); h.Responses[Url] = new HttpResult { Status = 200, Body = Rel("v2.0.0") };
            c.NotifyEnabled = false;
            Assert.Null(await c.CheckAsync()); Assert.Empty(h.Requests);
            Assert.NotNull(await c.CheckAsync(force: true));
            Assert.True(new UpdateChecker(h, new MemoryStore(), () => T0, "o", "r", "1.0.0").NotifyEnabled);        // προεπιλογή: ΕΝΕΡΓΟ
        }

        [Fact]
        public async Task A_clock_set_back_does_not_block_checks_forever()
        {
            var (c, h, _, _, adv) = Make(); h.Responses[Url] = new HttpResult { Status = 200, Body = Rel("v1.0.0") };
            await c.CheckAsync(); adv(TimeSpan.FromDays(-30)); await c.CheckAsync();
            Assert.Equal(2, h.Requests.Count);
        }

        [Fact]
        public async Task A_malicious_release_page_url_is_not_trusted()
        {
            var (c, h, _, _, _) = Make(); h.Responses[Url] = new HttpResult { Status = 200, Body = "{\"tag_name\":\"v9.0.0\",\"html_url\":\"https://evil.example/x\"}" };
            Assert.Null(await c.CheckAsync(force: true));
        }

        [Fact]
        public void The_store_persists_and_survives_a_corrupt_file()
        {
            var dir = Path.Combine(Path.GetTempPath(), "suite_" + Guid.NewGuid().ToString("N")); var f = Path.Combine(dir, "suite.json");
            try
            {
                var a = new JsonFileStore(f); a.Set("k", "v"); a.Set("update.notified", "v1.2.0");
                Assert.Equal("v1.2.0", new JsonFileStore(f).Get("update.notified"));
                File.WriteAllText(f, "{ broken");
                Assert.Null(new JsonFileStore(f).Get("k"));                           // δεν ρίχνει exception
                var b = new JsonFileStore(f); b.Set("x", "1"); Assert.Equal("1", new JsonFileStore(f).Get("x"));
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }
    }

    public class CatalogueTests
    {
        private static string Valid(string extra = "") => "{\"schema\":1,\"updated\":\"2026-10-23\",\"owner\":\"o\",\"future\":{\"x\":1},\"apps\":[{\"id\":\"a\",\"name\":\"App A\",\"repo\":\"AppA\",\"installerAsset\":\"^A-Setup\\\\.exe$\",\"checksumAsset\":\"A-Setup.exe.sha256\",\"uninstallDisplayName\":\"App A\",\"paths\":[\"%LocalAppData%\\\\Programs\\\\A\\\\a.exe\"],\"tagline\":{\"en\":\"English\",\"el\":\"Ελληνικά\"},\"capabilities\":{\"en\":[\"one\",\"two\"]},\"unknown\":true}" + extra + "]}";

        [Fact]
        public void A_valid_catalogue_parses_ignores_unknown_fields_and_falls_back_to_english()
        {
            var c = SuiteCatalogue.Parse(Valid(), out var err);
            Assert.NotNull(c); Assert.Null(err); Assert.Single(c.Apps);
            Assert.Equal("Ελληνικά", c.Apps[0].TaglineFor("el"));
            Assert.Equal("English", c.Apps[0].TaglineFor("fr"));
            Assert.Equal(2, c.Apps[0].CapabilitiesFor("el").Count);                 // δεν έχει ελληνικά: πέφτει στα αγγλικά
            Assert.Equal("https://github.com/o/AppA", c.Apps[0].RepoUrl(c.Owner));
        }

        [Theory]
        [InlineData("")] [InlineData("[]")] [InlineData("{\"schema\":2,\"apps\":[]}")] [InlineData("{\"schema\":1,\"apps\":[]}")] [InlineData("{\"apps\":[{}]}")] [InlineData("{ broken")]
        public void Malformed_catalogues_are_rejected(string json) { Assert.Null(SuiteCatalogue.Parse(json, out var err)); Assert.NotNull(err); }

        [Fact]
        public void An_oversize_catalogue_is_rejected() => Assert.Null(SuiteCatalogue.Parse(Valid() + new string(' ', 300_000), out _));

        [Fact]
        public void Duplicate_ids_bad_repo_names_and_bad_regexes_are_rejected()
        {
            var dup = ",{\"id\":\"a\",\"name\":\"B\",\"repo\":\"B\",\"installerAsset\":\"x\",\"uninstallDisplayName\":\"B\"}";
            Assert.Null(SuiteCatalogue.Parse(Valid(dup), out _));
            Assert.Null(SuiteCatalogue.Parse(Valid(",{\"id\":\"b\",\"name\":\"B\",\"repo\":\"../evil\",\"installerAsset\":\"x\",\"uninstallDisplayName\":\"B\"}"), out _));
            Assert.Null(SuiteCatalogue.Parse(Valid(",{\"id\":\"b\",\"name\":\"B\",\"repo\":\"B\",\"installerAsset\":\"([\",\"uninstallDisplayName\":\"B\"}"), out _));
            Assert.Null(SuiteCatalogue.Parse(Valid(",{\"id\":\"b\",\"name\":\"B\",\"repo\":\"B\",\"installerAsset\":\"x\",\"checksumAsset\":\"..\\\\x\",\"uninstallDisplayName\":\"B\"}"), out _));
            Assert.Null(SuiteCatalogue.Parse(Valid(",{\"id\":\"B ID\",\"name\":\"B\",\"repo\":\"B\",\"installerAsset\":\"x\",\"uninstallDisplayName\":\"B\"}"), out _));
        }

        [Fact]
        public void An_owner_with_path_characters_is_rejected() => Assert.Null(SuiteCatalogue.Parse(Valid().Replace("\"owner\":\"o\"", "\"owner\":\"o/../x\""), out _));

        [Fact]
        public async Task Provider_uses_the_embedded_copy_offline_and_fetches_at_most_once_a_day()
        {
            var dir = Path.Combine(Path.GetTempPath(), "suite_" + Guid.NewGuid().ToString("N")); var cache = Path.Combine(dir, "apps.json");
            try
            {
                var http = new FakeHttp(); var store = new MemoryStore(); var now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
                var c1 = await CatalogueProvider.LoadAsync(http, store, () => now, cache, Valid());
                Assert.Equal("a", c1.Apps[0].Id); Assert.Single(http.Requests);
                await CatalogueProvider.LoadAsync(http, store, () => now.AddHours(3), cache, Valid()); Assert.Single(http.Requests);
                await CatalogueProvider.LoadAsync(http, store, () => now.AddHours(25), cache, Valid()); Assert.Equal(2, http.Requests.Count);
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        [Fact]
        public async Task A_valid_live_catalogue_replaces_and_is_cached_an_invalid_one_is_ignored()
        {
            var dir = Path.Combine(Path.GetTempPath(), "suite_" + Guid.NewGuid().ToString("N")); var cache = Path.Combine(dir, "apps.json");
            try
            {
                var http = new FakeHttp(); var now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
                http.Responses[CatalogueProvider.Url] = new HttpResult { Status = 200, Body = "<html>captive portal</html>" };
                var bad = await CatalogueProvider.LoadAsync(http, new MemoryStore(), () => now, cache, Valid());
                Assert.Equal("App A", bad.Apps[0].Name); Assert.False(File.Exists(cache));

                http.Responses[CatalogueProvider.Url] = new HttpResult { Status = 200, Body = Valid().Replace("App A", "App A NEW"), ETag = "\"x\"" };
                var live = await CatalogueProvider.LoadAsync(http, new MemoryStore(), () => now, cache, Valid());
                Assert.Equal("App A NEW", live.Apps[0].Name); Assert.True(File.Exists(cache));

                var offline = await CatalogueProvider.LoadAsync(new FakeHttp(), new MemoryStore(), () => now.AddDays(5), cache, Valid());      // χωρίς δίκτυο: η cache
                Assert.Equal("App A NEW", offline.Apps[0].Name);
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }
    }

    public class DetectionTests
    {
        private static SuiteApp App() => new SuiteApp { Id = "a", Name = "App A", UninstallDisplayName = "App A", Paths = { @"%LocalAppData%\Programs\A\a.exe", @"%ProgramFiles%\A\a.exe" } };

        [Fact]
        public void Not_installed_when_nothing_matches()
        {
            var st = InstallDetector.Detect(App(), new FakeProbe());
            Assert.False(st.Installed); Assert.Equal("", st.Version);
        }

        [Fact]
        public void A_known_path_means_installed_with_that_path()
        {
            var p = new FakeProbe(); p.Files.Add(@"C:\Program Files\A\a.exe");
            var st = InstallDetector.Detect(App(), p);
            Assert.True(st.Installed); Assert.Equal(@"C:\Program Files\A\a.exe", st.Path);
        }

        [Fact]
        public void The_uninstall_entry_gives_the_version_and_matches_ignoring_case()
        {
            var p = new FakeProbe(); p.Entries.Add(new KeyValuePair<string, string>("Other", "9.9")); p.Entries.Add(new KeyValuePair<string, string>("app a", "1.2.0"));
            var st = InstallDetector.Detect(App(), p);
            Assert.True(st.Installed); Assert.Equal("1.2.0", st.Version);
        }

        [Theory]
        [InlineData("1.2.0", "v1.3.0", true)]
        [InlineData("1.3.0", "v1.3.0", false)]
        [InlineData("", "v1.3.0", false)]               // άγνωστη έκδοση: δεν ισχυριζόμαστε «διαθέσιμη ενημέρωση»
        public void Update_available(string installed, string latest, bool expected)
            => Assert.Equal(expected, InstallDetector.UpdateAvailable(new InstallState { Installed = true, Version = installed }, latest));

        [Fact]
        public void A_missing_app_never_shows_an_update() => Assert.False(InstallDetector.UpdateAvailable(new InstallState(), "v9.0.0"));
    }

    public class SafeInstallerTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "suite_dl_" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }
        private static string Hex(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
        private static readonly byte[] Payload = Encoding.ASCII.GetBytes("pretend installer");

        [Fact]
        public void Checksum_files_are_parsed_in_all_common_layouts()
        {
            string h = Hex(Payload);
            Assert.Equal(h, SafeInstaller.ParseChecksumText(h + " *Setup.exe\n", "Setup.exe"));
            Assert.Equal(h, SafeInstaller.ParseChecksumText(h.ToUpperInvariant() + "  Setup.exe\r\n", "setup.exe"));
            Assert.Equal(h, SafeInstaller.ParseChecksumText(h + "\n", "Setup.exe"));
            Assert.Equal(h, SafeInstaller.ParseChecksumText(Hex(new byte[] { 1 }) + " other.exe\n" + h + " *Setup.exe\n", "Setup.exe"));
            Assert.Null(SafeInstaller.ParseChecksumText(Hex(new byte[] { 1 }) + " other.exe\n" + h + " *another.exe\n", "Setup.exe"));       // κανένα όνομα δεν ταιριάζει: ΟΧΙ μαντεψιά
            Assert.Null(SafeInstaller.ParseChecksumText("not a hash", "Setup.exe")); Assert.Null(SafeInstaller.ParseChecksumText("", "Setup.exe"));
        }

        [Theory]
        [InlineData("sha256:0000000000000000000000000000000000000000000000000000000000000000", true)]
        [InlineData("sha1:abc", false)] [InlineData("", false)] [InlineData(null, false)] [InlineData("sha256:xyz", false)]
        public void Github_digest_field(string digest, bool ok) => Assert.Equal(ok, SafeInstaller.FromDigest(digest) != null);

        [Fact]
        public void The_installer_asset_is_chosen_by_regex_and_must_be_an_exe()
        {
            var rel = new ReleaseDto(); rel.Assets.Add(new ReleaseAsset { Name = "Setup.exe.sha256" }); rel.Assets.Add(new ReleaseAsset { Name = "Setup.zip" }); rel.Assets.Add(new ReleaseAsset { Name = "Setup.exe" });
            Assert.Equal("Setup.exe", SafeInstaller.PickAsset(rel, @"^Setup\.").Name);
            Assert.Null(SafeInstaller.PickAsset(rel, @"^Nothing")); Assert.Null(SafeInstaller.PickAsset(rel, "([")); Assert.Null(SafeInstaller.PickAsset(rel, @"zip$"));
        }

        [Fact]
        public async Task Expected_hash_comes_from_the_checksum_asset_then_from_the_digest()
        {
            var http = new FakeHttp(); string h = Hex(Payload);
            var rel = new ReleaseDto(); var inst = new ReleaseAsset { Name = "Setup.exe", Url = "https://github.com/o/r/releases/download/v1/Setup.exe", Digest = "sha256:" + new string('a', 64) };
            rel.Assets.Add(inst); rel.Assets.Add(new ReleaseAsset { Name = "Setup.exe.sha256", Url = "https://github.com/o/r/releases/download/v1/Setup.exe.sha256" });
            var app = new SuiteApp { ChecksumAsset = "Setup.exe.sha256" };
            http.Responses[rel.Assets[1].Url] = new HttpResult { Status = 200, Body = h + " *Setup.exe" };
            Assert.Equal(h, await SafeInstaller.ExpectedSha256Async(http, rel, app, inst));
            http.Responses[rel.Assets[1].Url] = new HttpResult { Status = 404 };                                                       // το αρχείο λείπει: digest
            Assert.Equal(new string('a', 64), await SafeInstaller.ExpectedSha256Async(http, rel, app, inst));
            inst.Digest = null; Assert.Null(await SafeInstaller.ExpectedSha256Async(http, rel, app, inst));
        }

        [Fact]
        public async Task A_matching_download_is_saved_and_not_executed()
        {
            var http = new FakeHttp(); var a = new ReleaseAsset { Name = "Setup.exe", Url = "https://github.com/o/r/releases/download/v1/Setup.exe" }; http.Files[a.Url] = Payload;
            var r = await SafeInstaller.DownloadVerifiedAsync(http, a, Hex(Payload), _dir);
            Assert.Equal(DownloadStatus.Ok, r.Status); Assert.True(File.Exists(r.Path)); Assert.Equal(Payload, File.ReadAllBytes(r.Path));
            Assert.Empty(Directory.GetFiles(_dir, "*.part"));
        }

        [Fact]
        public async Task A_mismatch_is_refused_and_the_file_is_deleted()
        {
            var http = new FakeHttp(); var a = new ReleaseAsset { Name = "Setup.exe", Url = "https://github.com/o/r/releases/download/v1/Setup.exe" }; http.Files[a.Url] = Encoding.ASCII.GetBytes("tampered");
            var r = await SafeInstaller.DownloadVerifiedAsync(http, a, Hex(Payload), _dir);
            Assert.Equal(DownloadStatus.Mismatch, r.Status); Assert.Empty(Directory.GetFiles(_dir));
        }

        [Fact]
        public async Task Without_a_checksum_nothing_is_downloaded()
        {
            var http = new FakeHttp(); var a = new ReleaseAsset { Name = "Setup.exe", Url = "https://github.com/o/r/releases/download/v1/Setup.exe" }; http.Files[a.Url] = Payload;
            var r = await SafeInstaller.DownloadVerifiedAsync(http, a, null, _dir);
            Assert.Equal(DownloadStatus.NoChecksum, r.Status); Assert.Empty(http.Requests);
        }

        [Theory]
        [InlineData("..\\evil.exe")] [InlineData("a|b.exe")] [InlineData("setup.bat")] [InlineData("")]
        public async Task Unsafe_file_names_are_refused(string name)
        {
            var http = new FakeHttp(); var r = await SafeInstaller.DownloadVerifiedAsync(http, new ReleaseAsset { Name = name, Url = "https://github.com/o/r/x" }, Hex(Payload), _dir);
            Assert.True(r.Status == DownloadStatus.Refused || r.Status == DownloadStatus.DownloadFailed || r.Status == DownloadStatus.Ok && Path.GetFileName(r.Path) == Path.GetFileName(name)); Assert.Empty(http.Requests.Where(x => x.Contains("evil")));
        }

        [Fact]
        public async Task An_existing_file_is_never_overwritten_and_a_failed_download_is_reported()
        {
            var http = new FakeHttp(); var a = new ReleaseAsset { Name = "Setup.exe", Url = "https://github.com/o/r/releases/download/v1/Setup.exe" }; http.Files[a.Url] = Payload;
            Directory.CreateDirectory(_dir); File.WriteAllText(Path.Combine(_dir, "Setup.exe"), "mine");
            var r = await SafeInstaller.DownloadVerifiedAsync(http, a, Hex(Payload), _dir);
            Assert.Equal("mine", File.ReadAllText(Path.Combine(_dir, "Setup.exe"))); Assert.Contains("(1)", r.Path);
            http.Files.Clear();
            Assert.Equal(DownloadStatus.DownloadFailed, (await SafeInstaller.DownloadVerifiedAsync(http, a, Hex(Payload), _dir)).Status);
            Assert.Equal(DownloadStatus.NoAsset, (await SafeInstaller.DownloadVerifiedAsync(http, null, Hex(Payload), _dir)).Status);
        }
    }
}
