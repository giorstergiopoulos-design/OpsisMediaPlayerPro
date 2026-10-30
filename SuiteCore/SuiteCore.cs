using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Opsis.Suite
{
    // ====================================================================================================================================
    // Κοινός, καθαρός κώδικας της «οικογένειας Opsis» (χωρίς UI): σύγκριση εκδόσεων, έλεγχος νέας έκδοσης, κατάλογος εφαρμογών, ανίχνευση εγκατάστασης,
    // ασφαλής λήψη με SHA-256. Ίδιο αρχείο σε κάθε εφαρμογή του συνόλου — δοκιμάζεται με ψεύτικο HTTP/registry. Δεν στέλνει τίποτα εκτός από GET στο GitHub.
    // ====================================================================================================================================

    public static class SemVer
    {
        /// <summary>«v1.2.3», «1.2», «1.2.3-beta.1», «1.2.3+build» → αριθμοί + προέκδοση. false αν δεν είναι έκδοση.</summary>
        public static bool TryParse(string s, out int[] nums, out string pre)
        {
            nums = null; pre = "";
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim(); if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);
            int plus = s.IndexOf('+'); if (plus >= 0) s = s.Substring(0, plus);
            int dash = s.IndexOf('-'); if (dash >= 0) { pre = s.Substring(dash + 1); s = s.Substring(0, dash); }
            var parts = s.Split('.');
            if (parts.Length == 0 || parts.Length > 4) return false;
            var list = new List<int>();
            foreach (var p in parts) { if (p.Length == 0 || p.Length > 9 || !p.All(char.IsDigit)) return false; list.Add(int.Parse(p)); }
            while (list.Count < 3) list.Add(0);
            nums = list.ToArray(); return true;
        }

        public static int Compare(string a, string b)
        {
            if (!TryParse(a, out var na, out var pa) || !TryParse(b, out var nb, out var pb)) throw new FormatException("Not a version: " + a + " / " + b);
            int n = Math.Max(na.Length, nb.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < na.Length ? na[i] : 0, y = i < nb.Length ? nb[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            if (pa == pb) return 0;
            if (pa.Length == 0) return 1;            // 1.2.0 > 1.2.0-beta
            if (pb.Length == 0) return -1;
            return Math.Sign(string.CompareOrdinal(pa, pb));
        }

        /// <summary>Είναι το <paramref name="tag"/> νεότερο από την τρέχουσα έκδοση; Άκυρα ονόματα → false (ποτέ exception).</summary>
        public static bool IsNewer(string tag, string current)
        {
            try { return Compare(tag, current) > 0; } catch (FormatException) { return false; }
        }
    }

    public static class SafeUrl
    {
        private static readonly string[] Hosts = { "github.com", "api.github.com", "raw.githubusercontent.com" };
        /// <summary>Μόνο https και μόνο GitHub (το ίδιο όριο για τον κατάλογο, τα releases και τις λήψεις).</summary>
        public static bool IsAllowed(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && Hosts.Contains(u.Host, StringComparer.OrdinalIgnoreCase) && string.IsNullOrEmpty(u.UserInfo);
    }

    // ---------------------------------------------------------------- HTTP / αποθήκευση -----------------------------------------------
    public sealed class HttpResult { public int Status; public string Body = ""; public string ETag; public bool Ok => Status >= 200 && Status < 300; }

    public interface IHttp
    {
        /// <summary>GET με προαιρετικό ETag (If-None-Match). Ποτέ exception: αποτυχία = Status 0.</summary>
        Task<HttpResult> GetAsync(string url, string etag, TimeSpan timeout);
        /// <summary>Κατεβάζει σε αρχείο (έως maxBytes). Επιστρέφει true αν ολοκληρώθηκε.</summary>
        Task<bool> DownloadAsync(string url, string destination, long maxBytes, TimeSpan timeout);
    }

    public interface IKeyValueStore { string Get(string key); void Set(string key, string value); }

    public sealed class MemoryStore : IKeyValueStore
    {
        private readonly Dictionary<string, string> _d = new Dictionary<string, string>();
        public string Get(string key) => _d.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) { if (value == null) _d.Remove(key); else _d[key] = value; }
    }

    /// <summary>Μικρό JSON αρχείο στον φάκελο δεδομένων της εφαρμογής. Ατομική εγγραφή· χαλασμένο αρχείο = κενό (ποτέ exception).</summary>
    public sealed class JsonFileStore : IKeyValueStore
    {
        private readonly string _path; private readonly object _lock = new object(); private Dictionary<string, string> _d;
        public JsonFileStore(string path) { _path = path; }
        private Dictionary<string, string> Load()
        {
            if (_d != null) return _d;
            try { _d = File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new Dictionary<string, string>() : new Dictionary<string, string>(); }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException) { _d = new Dictionary<string, string>(); }
            return _d;
        }
        public string Get(string key) { lock (_lock) return Load().TryGetValue(key, out var v) ? v : null; }
        public void Set(string key, string value)
        {
            lock (_lock)
            {
                var d = Load(); if (value == null) d.Remove(key); else d[key] = value;
                try { Directory.CreateDirectory(Path.GetDirectoryName(_path)); var tmp = _path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(d)); File.Move(tmp, _path, true); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
        }
    }

    public sealed class RealHttp : IHttp
    {
        private static readonly HttpClient Client = Make();
        private static HttpClient Make() { var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { Timeout = Timeout.InfiniteTimeSpan }; c.DefaultRequestHeaders.UserAgent.ParseAdd("OpsisSuite/1.0"); return c; }

        public async Task<HttpResult> GetAsync(string url, string etag, TimeSpan timeout)
        {
            if (!SafeUrl.IsAllowed(url)) return new HttpResult { Status = 0 };
            try
            {
                using (var cts = new CancellationTokenSource(timeout))
                using (var rq = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    rq.Headers.Accept.ParseAdd("application/vnd.github+json, application/json, text/plain");
                    if (!string.IsNullOrEmpty(etag)) rq.Headers.TryAddWithoutValidation("If-None-Match", etag);
                    using (var r = await Client.SendAsync(rq, HttpCompletionOption.ResponseHeadersRead, cts.Token))
                    {
                        var res = new HttpResult { Status = (int)r.StatusCode, ETag = r.Headers.ETag?.ToString() };
                        if (r.StatusCode == System.Net.HttpStatusCode.NotModified) return res;
                        if (r.Content.Headers.ContentLength > 300_000) { res.Status = 0; return res; }          // κανένα απρόσμενα μεγάλο JSON
                        res.Body = await r.Content.ReadAsStringAsync(cts.Token);
                        return res;
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException || ex is IOException) { return new HttpResult { Status = 0 }; }
        }

        public async Task<bool> DownloadAsync(string url, string destination, long maxBytes, TimeSpan timeout)
        {
            if (!SafeUrl.IsAllowed(url)) return false;
            try
            {
                using (var cts = new CancellationTokenSource(timeout))
                using (var r = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token))
                {
                    if (!r.IsSuccessStatusCode || r.Content.Headers.ContentLength > maxBytes) return false;
                    using (var src = await r.Content.ReadAsStreamAsync(cts.Token))
                    using (var dst = File.Create(destination))
                    {
                        var buf = new byte[81920]; long total = 0; int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, cts.Token)) > 0) { total += n; if (total > maxBytes) return false; await dst.WriteAsync(buf, 0, n, cts.Token); }
                    }
                    return true;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is OperationCanceledException || ex is IOException || ex is UnauthorizedAccessException) { return false; }
        }
    }

    // ---------------------------------------------------------------- GitHub releases ---------------------------------------------------
    public sealed class ReleaseAsset { public string Name = "", Url = "", Digest; public long Size; }

    public sealed class ReleaseDto
    {
        public string Tag = "", Name = "", Url = "", Body = ""; public bool Draft, Prerelease; public List<ReleaseAsset> Assets = new List<ReleaseAsset>();

        /// <summary>null αν το JSON δεν είναι release, αν λείπει tag ή αν το html_url δεν είναι https GitHub.</summary>
        public static ReleaseDto Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 300_000) return null;
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var r = doc.RootElement; if (r.ValueKind != JsonValueKind.Object) return null;
                    string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
                    var dto = new ReleaseDto { Tag = S(r, "tag_name"), Name = S(r, "name"), Url = S(r, "html_url"), Body = S(r, "body") };
                    if (dto.Tag.Length == 0 || dto.Tag.Length > 40 || !SafeUrl.IsAllowed(dto.Url)) return null;
                    dto.Draft = r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True;
                    dto.Prerelease = r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
                    if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                        foreach (var a in assets.EnumerateArray().Take(60))
                        {
                            var asset = new ReleaseAsset { Name = S(a, "name"), Url = S(a, "browser_download_url"), Digest = S(a, "digest") };
                            if (a.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number) asset.Size = sz.GetInt64();
                            if (asset.Name.Length > 0 && SafeUrl.IsAllowed(asset.Url)) dto.Assets.Add(asset);
                        }
                    return dto;
                }
            }
            catch (JsonException) { return null; }
        }
    }

    // ---------------------------------------------------------------- Έλεγχος νέας έκδοσης ----------------------------------------------
    public sealed class UpdateInfo { public string Tag = "", Name = "", Url = "", Notes = ""; public ReleaseDto Release; }

    /// <summary>
    /// Ένα GET την ημέρα στο api.github.com/repos/&lt;owner&gt;/&lt;repo&gt;/releases/latest (με ETag, timeout 8 s, χωρίς σφάλματα προς τα έξω). Ειδοποιεί ΜΙΑ φορά ανά έκδοση.
    /// Τα pre-releases και τα drafts αγνοούνται. Καμία τηλεμετρία: τίποτα άλλο δεν αποστέλλεται.
    /// </summary>
    public sealed class UpdateChecker
    {
        public static readonly TimeSpan Interval = TimeSpan.FromHours(24), MinRetry = TimeSpan.FromHours(1), Timeout = TimeSpan.FromSeconds(8);
        private readonly IHttp _http; private readonly IKeyValueStore _store; private readonly Func<DateTime> _now; private readonly string _owner, _repo, _current;

        public UpdateChecker(IHttp http, IKeyValueStore store, Func<DateTime> utcNow, string owner, string repo, string currentVersion)
        { _http = http; _store = store; _now = utcNow; _owner = owner; _repo = repo; _current = currentVersion; }

        public bool NotifyEnabled { get => _store.Get("update.notify") != "0"; set => _store.Set("update.notify", value ? "1" : "0"); }

        private DateTime? Time(string key) => DateTime.TryParse(_store.Get(key), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : (DateTime?)null;

        public bool IsDue()
        {
            var now = _now(); var ok = Time("update.lastOk"); var attempt = Time("update.lastAttempt");
            if (attempt is DateTime a && now >= a && now - a < MinRetry) return false;            // μετά από αποτυχία: όχι πιο συχνά από 1 ώρα
            return ok == null || now < ok.Value || now - ok.Value >= Interval;                       // ρολόι πίσω = νέος έλεγχος
        }

        public string NotifiedTag => _store.Get("update.notified");
        /// <summary>«Παράλειψη έκδοσης»: αυτή η έκδοση δεν ξαναπροτείνεται (μια νεότερη ναι).</summary>
        public string SkippedTag => _store.Get("update.skipped");
        public void SkipVersion(string tag) => _store.Set("update.skipped", tag ?? "");
        public void MarkNotified(string tag) => _store.Set("update.notified", tag);

        public async Task<UpdateInfo> CheckAsync(bool force = false)
        {
            if (!force && (!NotifyEnabled || !IsDue())) return null;
            var url = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";
            _store.Set("update.lastAttempt", _now().ToString("O"));
            var etag = _store.Get("update.etag");
            var r = await _http.GetAsync(url, etag, Timeout);
            string body;
            if (r.Status == 304) body = _store.Get("update.cache");
            else if (r.Ok) { body = r.Body; _store.Set("update.etag", r.ETag); _store.Set("update.cache", body); }
            else return null;
            _store.Set("update.lastOk", _now().ToString("O"));
            var rel = ReleaseDto.Parse(body);
            if (rel == null || rel.Draft || rel.Prerelease || !SemVer.IsNewer(rel.Tag, _current)) return null;
            if (!force && (NotifiedTag == rel.Tag || SkippedTag == rel.Tag)) return null;
            return new UpdateInfo { Tag = rel.Tag, Name = string.IsNullOrEmpty(rel.Name) ? rel.Tag : rel.Name, Url = rel.Url, Notes = rel.Body ?? "", Release = rel };
        }
    }

    // ---------------------------------------------------------------- Κατάλογος εφαρμογών ---------------------------------------------
    public sealed class SuiteApp
    {
        public string Id = "", Name = "", Repo = "", InstallerAsset = "", ChecksumAsset, UninstallDisplayName = "";
        public List<string> Paths = new List<string>();
        public Dictionary<string, string> Tagline = new Dictionary<string, string>();
        public Dictionary<string, List<string>> Capabilities = new Dictionary<string, List<string>>();
        public string RepoUrl(string owner) => $"https://github.com/{owner}/{Repo}";
        public string TaglineFor(string lang) => Tagline.TryGetValue(lang, out var t) ? t : Tagline.TryGetValue("en", out var e) ? e : "";
        public List<string> CapabilitiesFor(string lang) => Capabilities.TryGetValue(lang, out var c) ? c : Capabilities.TryGetValue("en", out var e) ? e : new List<string>();
    }

    public sealed class SuiteCatalogue
    {
        public const int MaxBytes = 256 * 1024;
        public string Owner = "giorstergiopoulos-design"; public string Updated = ""; public List<SuiteApp> Apps = new List<SuiteApp>();
        private static readonly Regex IdRx = new Regex("^[a-z0-9-]{1,32}$", RegexOptions.Compiled), RepoRx = new Regex(@"^[A-Za-z0-9._-]{1,60}$", RegexOptions.Compiled);

        /// <summary>Έλεγχος εγκυρότητας: schema 1, ≤256 KB, μοναδικά id, ασφαλή ονόματα repo, έγκυρα regex, περιορισμένα μήκη. Άγνωστα πεδία αγνοούνται.</summary>
        public static SuiteCatalogue Parse(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "empty"; return null; }
            if (Encoding.UTF8.GetByteCount(json) > MaxBytes) { error = "too large"; return null; }
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var sc) || sc.ValueKind != JsonValueKind.Number || sc.GetInt32() != 1) { error = "schema"; return null; }
                    var cat = new SuiteCatalogue();
                    if (root.TryGetProperty("owner", out var ow) && ow.ValueKind == JsonValueKind.String) { var o = ow.GetString(); if (!RepoRx.IsMatch(o)) { error = "owner"; return null; } cat.Owner = o; }
                    if (root.TryGetProperty("updated", out var up) && up.ValueKind == JsonValueKind.String) cat.Updated = up.GetString().Length > 20 ? "" : up.GetString();
                    if (!root.TryGetProperty("apps", out var apps) || apps.ValueKind != JsonValueKind.Array || apps.GetArrayLength() == 0 || apps.GetArrayLength() > 30) { error = "apps"; return null; }
                    string Str(JsonElement e, string n, int max, bool required = true)
                    {
                        if (!e.TryGetProperty(n, out var v) || v.ValueKind != JsonValueKind.String) { if (required) throw new FormatException(n); return null; }
                        var s = v.GetString(); if (s.Length > max) throw new FormatException(n + " too long"); return s;
                    }
                    foreach (var a in apps.EnumerateArray())
                    {
                        var app = new SuiteApp { Id = Str(a, "id", 32), Name = Str(a, "name", 60), Repo = Str(a, "repo", 60), InstallerAsset = Str(a, "installerAsset", 120), ChecksumAsset = Str(a, "checksumAsset", 120, false), UninstallDisplayName = Str(a, "uninstallDisplayName", 60) };
                        if (!IdRx.IsMatch(app.Id) || !RepoRx.IsMatch(app.Repo)) throw new FormatException("id/repo");
                        if (cat.Apps.Any(x => x.Id == app.Id)) throw new FormatException("duplicate id");
                        try { new Regex(app.InstallerAsset, RegexOptions.None, TimeSpan.FromMilliseconds(100)); } catch (ArgumentException) { throw new FormatException("regex"); }
                        if (app.ChecksumAsset != null && (app.ChecksumAsset.Contains("/") || app.ChecksumAsset.Contains("\\"))) throw new FormatException("checksum name");
                        if (a.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array) foreach (var p in paths.EnumerateArray().Take(8)) if (p.ValueKind == JsonValueKind.String && p.GetString().Length <= 260) app.Paths.Add(p.GetString());
                        if (a.TryGetProperty("tagline", out var tl) && tl.ValueKind == JsonValueKind.Object) foreach (var kv in tl.EnumerateObject()) if (kv.Value.ValueKind == JsonValueKind.String && kv.Name.Length <= 8) app.Tagline[kv.Name] = Cap(kv.Value.GetString(), 160);
                        if (a.TryGetProperty("capabilities", out var cp) && cp.ValueKind == JsonValueKind.Object)
                            foreach (var kv in cp.EnumerateObject()) if (kv.Value.ValueKind == JsonValueKind.Array && kv.Name.Length <= 8) app.Capabilities[kv.Name] = kv.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Take(8).Select(x => Cap(x.GetString(), 200)).ToList();
                        cat.Apps.Add(app);
                    }
                    return cat;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is InvalidOperationException) { error = "invalid: " + ex.Message; return null; }
        }
        private static string Cap(string s, int n) => s.Length > n ? s.Substring(0, n) : s;
    }

    public static class CatalogueProvider
    {
        public const string Url = "https://raw.githubusercontent.com/giorstergiopoulos-design/Opsis/master/suite/apps.json";

        /// <summary>Ο κατάλογος: πρώτα το αντίγραφο της cache (αν είναι έγκυρο), αλλιώς το ενσωματωμένο. Μία λήψη την ημέρα, timeout 5 s, αποτυχία = σιωπή.</summary>
        public static async Task<SuiteCatalogue> LoadAsync(IHttp http, IKeyValueStore store, Func<DateTime> utcNow, string cacheFile, string embeddedJson)
        {
            SuiteCatalogue best = SuiteCatalogue.Parse(embeddedJson, out _);
            try { if (File.Exists(cacheFile)) { var c = SuiteCatalogue.Parse(File.ReadAllText(cacheFile), out _); if (c != null) best = c; } } catch (IOException) { } catch (UnauthorizedAccessException) { }

            bool due = !DateTime.TryParse(store.Get("catalogue.fetched"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var last) || utcNow() < last.ToUniversalTime() || utcNow() - last.ToUniversalTime() >= TimeSpan.FromHours(24);
            if (!due) return best;
            store.Set("catalogue.fetched", utcNow().ToString("O"));
            var r = await http.GetAsync(Url, store.Get("catalogue.etag"), TimeSpan.FromSeconds(5));
            if (r.Ok)
            {
                var live = SuiteCatalogue.Parse(r.Body, out _);
                if (live != null)
                {
                    best = live; store.Set("catalogue.etag", r.ETag);
                    try { Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)); var tmp = cacheFile + ".tmp"; File.WriteAllText(tmp, r.Body); File.Move(tmp, cacheFile, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            return best;
        }
    }

    // ---------------------------------------------------------------- Ανίχνευση εγκατάστασης (μόνο ανάγνωση) -------------------------
    public sealed class InstallState { public bool Installed; public string Version = "", Path = ""; }

    public interface IInstallProbe
    {
        string Expand(string template);
        bool FileExists(string path);
        IEnumerable<KeyValuePair<string, string>> UninstallEntries();      // DisplayName → DisplayVersion
    }

    public static class InstallDetector
    {
        public static InstallState Detect(SuiteApp app, IInstallProbe probe)
        {
            var st = new InstallState();
            foreach (var p in app.Paths) { var full = probe.Expand(p); if (!string.IsNullOrEmpty(full) && probe.FileExists(full)) { st.Installed = true; st.Path = full; break; } }
            foreach (var kv in probe.UninstallEntries())
                if (string.Equals(kv.Key, app.UninstallDisplayName, StringComparison.OrdinalIgnoreCase)) { st.Installed = true; if (!string.IsNullOrEmpty(kv.Value)) st.Version = kv.Value; break; }
            return st;
        }

        /// <summary>Διαθέσιμη νέα έκδοση: εγκατεστημένη με γνωστή έκδοση που είναι μικρότερη από το tag του τελευταίου release.</summary>
        public static bool UpdateAvailable(InstallState st, string latestTag) => st.Installed && st.Version.Length > 0 && SemVer.IsNewer(latestTag, st.Version);
    }

    public sealed class RealInstallProbe : IInstallProbe
    {
        public string Expand(string t) { try { return Environment.ExpandEnvironmentVariables(t); } catch (Exception) { return null; } }
        public bool FileExists(string p) { try { return File.Exists(p); } catch (Exception) { return false; } }
        public IEnumerable<KeyValuePair<string, string>> UninstallEntries()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var (hive, view) in new[] { (Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Default), (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64), (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32) })
            {
                try
                {
                    using (var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view))
                    using (var un = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (un == null) continue;
                        foreach (var name in un.GetSubKeyNames().Take(2000))
                            using (var k = un.OpenSubKey(name))
                                if (k?.GetValue("DisplayName") is string dn) list.Add(new KeyValuePair<string, string>(dn, k.GetValue("DisplayVersion") as string ?? ""));
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException) { }
            }
            return list;
        }
    }

    // ---------------------------------------------------------------- Ασφαλής λήψη με SHA-256 -----------------------------------------
    public enum DownloadStatus { Ok, NoAsset, NoChecksum, DownloadFailed, Mismatch, Refused }
    public sealed class DownloadResult { public DownloadStatus Status; public string Path = "", Expected = "", Actual = "", Message = ""; }

    public static class SafeInstaller
    {
        public const long MaxInstallerBytes = 600L * 1024 * 1024;

        public static ReleaseAsset PickAsset(ReleaseDto rel, string installerRegex)
        {
            try { var rx = new Regex(installerRegex, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); return rel.Assets.FirstOrDefault(a => rx.IsMatch(a.Name) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)); }
            catch (ArgumentException) { return null; } catch (RegexMatchTimeoutException) { return null; }
        }

        /// <summary>Αρχείο .sha256: «hash *όνομα» ή «hash  όνομα» ή μόνο hash. Επιστρέφει 64 hex πεζά ή null.</summary>
        public static string ParseChecksumText(string text, string fileName)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string only = null; int lines = 0;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim(); if (line.Length == 0) continue; lines++;
                var m = Regex.Match(line, @"^([0-9a-fA-F]{64})(?:\s+\*?(.+))?$"); if (!m.Success) continue;
                var h = m.Groups[1].Value.ToLowerInvariant(); var name = m.Groups[2].Success ? m.Groups[2].Value.Trim() : null;
                if (name != null && string.Equals(Path.GetFileName(name), fileName, StringComparison.OrdinalIgnoreCase)) return h;
                if (name == null) only = h;
            }
            return lines == 1 ? only : null;
        }

        public static string FromDigest(string digest) { var m = Regex.Match(digest ?? "", @"^sha256:([0-9a-fA-F]{64})$"); return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null; }

        public static async Task<string> ExpectedSha256Async(IHttp http, ReleaseDto rel, SuiteApp app, ReleaseAsset installer)
        {
            if (!string.IsNullOrEmpty(app.ChecksumAsset))
            {
                var sum = rel.Assets.FirstOrDefault(a => string.Equals(a.Name, app.ChecksumAsset, StringComparison.OrdinalIgnoreCase));
                if (sum != null) { var r = await http.GetAsync(sum.Url, null, TimeSpan.FromSeconds(10)); if (r.Ok) { var h = ParseChecksumText(r.Body, installer.Name); if (h != null) return h; } }
            }
            return FromDigest(installer.Digest);
        }

        public static string Sha256OfFile(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }

        /// <summary>Κατεβάζει ΜΟΝΟ αν υπάρχει αναμενόμενο SHA-256. Μετά τη λήψη ελέγχει· σε ασυμφωνία διαγράφει το αρχείο και ΑΡΝΕΙΤΑΙ. Δεν εκτελεί τίποτα.</summary>
        public static async Task<DownloadResult> DownloadVerifiedAsync(IHttp http, ReleaseAsset asset, string expectedSha256, string destinationDir)
        {
            var res = new DownloadResult { Expected = expectedSha256 ?? "" };
            if (asset == null) { res.Status = DownloadStatus.NoAsset; return res; }
            if (string.IsNullOrEmpty(expectedSha256)) { res.Status = DownloadStatus.NoChecksum; res.Message = "No checksum to verify against"; return res; }
            string name = Path.GetFileName(asset.Name);
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { res.Status = DownloadStatus.Refused; res.Message = "Unsafe file name"; return res; }
            Directory.CreateDirectory(destinationDir);
            string dest = Path.Combine(destinationDir, name), part = dest + ".part";
            for (int i = 1; File.Exists(dest); i++) dest = Path.Combine(destinationDir, Path.GetFileNameWithoutExtension(name) + " (" + i + ")" + Path.GetExtension(name));
            try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { }
            if (!await http.DownloadAsync(asset.Url, part, MaxInstallerBytes, TimeSpan.FromMinutes(10))) { SafeDelete(part); res.Status = DownloadStatus.DownloadFailed; return res; }
            res.Actual = Sha256OfFile(part);
            if (!string.Equals(res.Actual, expectedSha256, StringComparison.OrdinalIgnoreCase)) { SafeDelete(part); res.Status = DownloadStatus.Mismatch; res.Message = "SHA-256 mismatch"; return res; }
            File.Move(part, dest); res.Path = dest; res.Status = DownloadStatus.Ok; return res;
        }
        private static void SafeDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch (IOException) { } }
    }

    /// <summary>Η ίδια ροή ενημέρωσης σε όλες τις εφαρμογές: λήψη του installer ΜΟΝΟ από τα releases του repo με επαλήθευση SHA-256 (άρνηση + διαγραφή σε ασυμφωνία),
    /// μετά σιωπηλή εγκατάσταση από βοηθητικό script που ξαναελέγχει το hash και ξεκινά αφού κλείσει η εφαρμογή.</summary>
    public static class SelfUpdate
    {
        public static async Task<DownloadResult> PrepareAsync(IHttp http, UpdateInfo info, string installerAsset, string destinationDir)
        {
            var rel = info?.Release; if (rel == null) return new DownloadResult { Status = DownloadStatus.NoAsset };
            var asset = rel.Assets.FirstOrDefault(a => string.Equals(a.Name, installerAsset, StringComparison.OrdinalIgnoreCase));
            if (asset == null) return new DownloadResult { Status = DownloadStatus.NoAsset };
            var app = new SuiteApp { InstallerAsset = installerAsset, ChecksumAsset = installerAsset + ".sha256" };
            string expected = await SafeInstaller.ExpectedSha256Async(http, rel, app, asset);
            return await SafeInstaller.DownloadVerifiedAsync(http, asset, expected, destinationDir);
        }

        static string Q(string s) => "'" + (s ?? "").Replace("'", "''") + "'";

        /// <summary>PowerShell script: περιμένει να κλείσει η εφαρμογή, ξαναελέγχει το SHA-256 (TOCTOU), τρέχει τον installer σιωπηλά (με UAC, εγκατάσταση σε Program Files) και ανοίγει ξανά την εφαρμογή.</summary>
        public static string BuildScript(string setupPath, string sha256, int waitPid, string appExe, string mode = null, bool relaunch = true, bool waitForever = false, string logPath = null)
        {
            if (string.IsNullOrEmpty(sha256) || sha256.Length != 64) throw new ArgumentException("sha256");
            string L(string msg) => string.IsNullOrEmpty(logPath) ? "" : "try { Add-Content -LiteralPath " + Q(logPath) + " -Value ((Get-Date -Format s) + ' " + msg + "') } catch { }\n";
            return "$ErrorActionPreference = 'Stop'\n"
                 + L("update started")
                 + "try { Wait-Process -Id " + waitPid + (waitForever ? "" : " -Timeout 120") + " -ErrorAction SilentlyContinue } catch { }\n"
                 + "if ((Get-FileHash -LiteralPath " + Q(setupPath) + " -Algorithm SHA256).Hash.ToLowerInvariant() -ne " + Q(sha256.ToLowerInvariant()) + ") { " + L("FAILED: SHA-256 changed after download - installer not run") + "exit 2 }\n"
                 + L("hash ok, running installer")
                 + "try { $p = Start-Process -FilePath " + Q(setupPath) + " -ArgumentList '/SILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS'" + (string.IsNullOrEmpty(mode) ? "" : ",'/MODE=" + mode + "'") + " -Verb RunAs -Wait -PassThru } catch { " + L("FAILED: installer was not started (UAC declined?)") + "exit 3 }\n"
                 + "if ($p.ExitCode -ne 0) { " + L("FAILED: installer exit code ' + $p.ExitCode + '") + "exit 4 }\n"
                 + L("installed")
                 + (relaunch ? "if (Test-Path -LiteralPath " + Q(appExe) + ") { Start-Process -FilePath " + Q(appExe) + " }" : "");
        }
    }

    public static class SuiteLang
    {
        /// <summary>«el» για ελληνικό UI, αλλιώς «en».</summary>
        public static string Current() => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";
    }
}
