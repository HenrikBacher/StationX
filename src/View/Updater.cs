using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HttpClient = System.Net.Http.HttpClient;

namespace StationX.View;

/// <summary>
/// Looks for a newer release on GitHub and, if the player asks, downloads it and installs it over this copy. Only an
/// exported build that knows its own version checks (CI stamps <c>application/config/version</c> from the tag); the
/// request carries nothing about the player. Saves and settings live in user:// and are left alone.
/// </summary>
public static class Updater
{
    const string Repo = "HenrikBacher/StationX";
    static readonly HttpClient Http = MakeClient();

    public static string Current => ProjectSettings.GetSetting("application/config/version", "").AsString();
    /// <summary>The newer version on offer, once the check has found one.</summary>
    public static string? Latest { get; private set; }
    public static string? PageUrl { get; private set; }
    static string? _assetUrl, _patchUrl, _baseUrl, _pckUrl, _pckListUrl, _codeUrl;
    static long _assetSize, _patchSize, _codeSize;
    /// <summary>
    /// The delta: which blocks of the new game pack this copy already has (PckDelta), worked out during the check when the
    /// patch fits and the release lists its pack's blocks. Null when there's none to use.
    /// </summary>
    static PckDelta.Plan? _delta;
    /// <summary>True once the small patch is known to fit this copy (same engine and .NET runtime as the new release).</summary>
    public static bool PatchFits { get; private set; }
    /// <summary>What the update will download, in bytes: the changed blocks and the code, the patch, or the whole game.</summary>
    public static long DownloadBytes => _delta != null ? _delta.FetchBytes + _codeSize : PatchFits ? _patchSize : _assetSize;
    /// <summary>What the update will download, in MB (at least 1).</summary>
    public static int DownloadMB => (int)Math.Max(1, Math.Ceiling(DownloadBytes / 1e6));
    /// <summary>The download as words: "under 1 MB", "about 32 MB".</summary>
    public static string DownloadSize => DownloadBytes < 1_000_000 ? "under 1 MB" : $"about {DownloadMB} MB";
    /// <summary>What the updater is doing, for the title screen ("Downloading 40%", or an error).</summary>
    public static string? Status { get; private set; }
    public static bool Busy { get; private set; }
    /// <summary>How much of the download has arrived, 0 to 1 (negative while the size is unknown).</summary>
    public static double Progress { get; private set; } = -1;
    public static event Action? Changed;

    static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("StationX-updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    static string? Platform => OS.GetName() switch
    {
        "Windows" => "StationX-windows-x86_64",
        "Linux" => "StationX-linux-x86_64",
        _ => null,
    };
    static string? AssetName => Platform == null ? null : Platform + (OS.GetName() == "Windows" ? ".zip" : ".tar.gz");

    /// <summary>What's new and fixed in every release since this one, newest first, for the player to read before updating.</summary>
    public static System.Collections.Generic.List<(string version, string notes)> Notes { get; } = new();

    /// <summary>The running check, so the title can wait for it before opening.</summary>
    public static Task Checking { get; private set; } = Task.CompletedTask;

    /// <summary>Asks GitHub for the releases newer than this one. Quiet on any failure: no network is no problem.</summary>
    public static void Check() { if (Checking.IsCompleted) Checking = CheckAsync(); }

    static async Task CheckAsync()
    {
        if (!Settings.Persist || !Settings.CheckUpdates || OS.HasFeature("editor") || AssetName == null || Latest != null) return;
        if (!System.Version.TryParse(Current, out var mine)) return;
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases?per_page=30"));
            var newer = doc.RootElement.EnumerateArray()
                .Where(r => !r.GetProperty("draft").GetBoolean() && !r.GetProperty("prerelease").GetBoolean())
                .Select(r => (r, v: System.Version.TryParse((r.GetProperty("tag_name").GetString() ?? "").TrimStart('v'), out var v) ? v : null))
                .Where(x => x.v != null && x.v > mine)
                .OrderByDescending(x => x.v)
                .ToList();
            if (newer.Count == 0) return;
            var latest = newer[0].r;
            JsonElement Asset(string name) => latest.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == name);
            var asset = Asset(AssetName!);
            if (asset.ValueKind != JsonValueKind.Object) return;
            _assetUrl = asset.GetProperty("browser_download_url").GetString();
            _assetSize = asset.GetProperty("size").GetInt64();
            if (Asset(Platform + "-patch.zip") is { ValueKind: JsonValueKind.Object } patch && Asset(Platform + "-base.txt") is { ValueKind: JsonValueKind.Object } bas)
            {
                _patchUrl = patch.GetProperty("browser_download_url").GetString();
                _patchSize = patch.GetProperty("size").GetInt64();
                _baseUrl = bas.GetProperty("browser_download_url").GetString();
            }
            if (Asset(Platform + ".pck") is { ValueKind: JsonValueKind.Object } pck && Asset(Platform + "-pck.txt") is { ValueKind: JsonValueKind.Object } list
                && Asset(Platform + "-code.zip") is { ValueKind: JsonValueKind.Object } code)
            {
                _pckUrl = pck.GetProperty("browser_download_url").GetString();
                _pckListUrl = list.GetProperty("browser_download_url").GetString();
                _codeUrl = code.GetProperty("browser_download_url").GetString();
                _codeSize = code.GetProperty("size").GetInt64();
            }
            PageUrl = latest.GetProperty("html_url").GetString();
            Notes.Clear();
            foreach (var (r, v) in newer) Notes.Add((v!.ToString(), NewAndFixed(r.GetProperty("body").GetString() ?? "")));
            Latest = newer[0].v!.ToString();
            Notify();
            if (_baseUrl != null)
            {
                var manifest = await Http.GetStringAsync(_baseUrl);
                string dir = Path.GetDirectoryName(OS.GetExecutablePath())!;
                PatchFits = await Task.Run(() => PatchManifest.BaseMatches(manifest, dir));
                GD.Print(PatchFits ? "Update: the small patch fits this copy." : "Update: this copy differs from the new release's base; the full download is needed.");
                Notify();
                if (PatchFits && _pckListUrl != null && File.Exists(Path.Combine(dir, "StationX.pck")))
                {
                    var blocks = PckDelta.Parse(await Http.GetStringAsync(_pckListUrl));
                    if (blocks != null)
                    {
                        _delta = await Task.Run(() => PckDelta.Match(blocks, File.ReadAllBytes(Path.Combine(dir, "StationX.pck"))));
                        GD.Print($"Update: {_delta.FetchBytes / 1e6:0.00} MB of the new game pack to fetch, the rest already here.");
                        Notify();
                    }
                }
            }
        }
        catch (Exception e) { GD.Print($"Update check failed: {e.Message}"); }
    }

    /// <summary>For screenshots: pretend a release is on offer.</summary>
    public static void DebugOffer(string version, string body)
    {
        Latest ??= version;
        Notes.Add((version, NewAndFixed(body)));
        _assetSize = 108_000_000; _patchSize = 33_000_000; PatchFits = true;
    }

    /// <summary>For screenshots: pretend a download is under way (or has failed, when not busy).</summary>
    public static void DebugProgress(double progress, string status, bool busy)
    {
        Progress = progress; Status = status; Busy = busy;
    }

    /// <summary>The "New" and "Fixes" sections at the top of the release notes (before the list of every commit).</summary>
    public static string NewAndFixed(string body)
    {
        int cut = body.IndexOf("### All changes", StringComparison.Ordinal);
        if (cut < 0) cut = body.IndexOf("### Downloads", StringComparison.Ordinal);
        var text = (cut >= 0 ? body[..cut] : body).Replace("\r", "").Trim();
        return text.Replace("\n- ", "\n•  ").Replace("**", "");
    }

    /// <summary>Downloads the new version, unpacks it beside this one, swaps it in and restarts.</summary>
    public static async void Install()
    {
        if (Busy || _assetUrl == null) return;
        Busy = true;
        bool patch = PatchFits && _patchUrl != null;
        try
        {
            string exe = OS.GetExecutablePath();
            string dir = Path.GetDirectoryName(exe)!;
            // Unpack inside the install folder, so moving the files into place is a rename on the same disk.
            string staging = Path.Combine(dir, ".update");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            string archive = Path.Combine(staging, patch ? Platform + "-patch.zip" : AssetName!);

            string files = Path.Combine(staging, "files");
            Status = "Downloading…"; Notify();
            if (patch && _delta != null && _pckUrl != null && _codeUrl != null)
            {
                try
                {
                    await InstallDelta(dir, staging, files);
                    Status = "Installing…"; Notify();
                    if (OS.GetName() == "Windows") RestartWindows(exe, dir, files, staging);
                    else RestartUnix(exe, dir, files, staging);
                    return;
                }
                catch (Exception e)
                {
                    // Anything wrong with the pieces: the patch as before.
                    GD.Print($"Update: the delta didn't work ({e.Message}); downloading the patch.");
                    _delta = null;
                    if (Directory.Exists(files)) Directory.Delete(files, true);
                }
            }
            await Download(patch ? _patchUrl! : _assetUrl!, archive);

            Progress = 1;
            Status = "Unpacking…"; Notify();
            await Task.Run(() =>
            {
                if (archive.EndsWith(".zip")) ZipFile.ExtractToDirectory(archive, files);
                else
                {
                    Directory.CreateDirectory(files);
                    using var gz = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
                    TarFile.ExtractToDirectory(gz, files, overwriteFiles: true);
                }
            });
            File.Delete(archive);
            // A patch carries the game pack and code; a full download, the whole game.
            if (!File.Exists(Path.Combine(files, patch ? "StationX.pck" : Path.GetFileName(exe))))
                throw new InvalidOperationException("The download doesn't contain the game.");

            Status = "Installing…"; Notify();
            if (OS.GetName() == "Windows") RestartWindows(exe, dir, files, staging);
            else RestartUnix(exe, dir, files, staging);
        }
        catch (Exception e)
        {
            Busy = false;
            Status = e is UnauthorizedAccessException or IOException
                ? $"Couldn't install here ({e.Message}). Download it from the releases page instead."
                : $"Update failed: {e.Message}";
            Notify();
        }
    }

    /// <summary>
    /// The delta: the new code (a small zip) and the new game pack rebuilt from this copy's pack plus only the blocks that
    /// changed, fetched as ranges. Throws if any piece is missing or the rebuilt pack isn't the release's.
    /// </summary>
    static async Task InstallDelta(string dir, string staging, string files)
    {
        var plan = _delta!;
        long total = plan.FetchBytes + _codeSize, got = 0;
        void Count(long n)
        {
            long now = System.Threading.Interlocked.Add(ref got, n);
            Progress = total > 0 ? Math.Min(1, (double)now / total) : -1;
            Status = $"Downloading… {now / 1e6:0.0} of {total / 1e6:0.0} MB";
            Notify();
        }
        string codeZip = Path.Combine(staging, "code.zip");
        await Download(_codeUrl!, codeZip, Count);
        // The changed blocks, a few requests at a time.
        var ranges = plan.Ranges();
        var fetched = new System.Collections.Concurrent.ConcurrentDictionary<long, byte[]>();
        using var gate = new System.Threading.SemaphoreSlim(4);
        await Task.WhenAll(ranges.Select(async r =>
        {
            await gate.WaitAsync();
            try { fetched[r.start] = await FetchRange(_pckUrl!, r.start, r.length, Count); }
            finally { gate.Release(); }
        }));
        Status = "Unpacking…"; Notify();
        await Task.Run(() =>
        {
            Directory.CreateDirectory(files);
            var old = File.ReadAllBytes(Path.Combine(dir, "StationX.pck"));
            if (!PckDelta.Assemble(plan, old, fetched, Path.Combine(files, "StationX.pck")))
                throw new InvalidOperationException("the rebuilt game pack doesn't match the release");
            ZipFile.ExtractToDirectory(codeZip, files);
            File.Delete(codeZip);
        });
    }

    /// <summary>One stretch of a file by a ranged request; the server must answer with exactly that stretch.</summary>
    static async Task<byte[]> FetchRange(string url, long start, long length, Action<long>? count = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, start + length - 1);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        if (resp.StatusCode != System.Net.HttpStatusCode.PartialContent) throw new InvalidOperationException($"no ranged download ({(int)resp.StatusCode})");
        var data = new byte[length];
        await using var src = await resp.Content.ReadAsStreamAsync();
        int at = 0, n;
        while (at < length && (n = await src.ReadAsync(data.AsMemory(at, (int)Math.Min(1 << 16, length - at)))) > 0)
        {
            at += n;
            count?.Invoke(n);
        }
        if (at != length) throw new IOException("the download was cut short");
        return data;
    }

    /// <summary>
    /// Downloads a file. A big one comes in four parts at once (ranged requests), which is usually much faster than one
    /// stream; if the server won't do ranges, one stream as before.
    /// </summary>
    static async Task Download(string url, string path, Action<long>? count = null)
    {
        long total = 0, got = 0;
        int lastPct = -2;
        count ??= n =>
        {
            long now = System.Threading.Interlocked.Add(ref got, n);
            int pct = total > 0 ? (int)(now * 100 / total) : -1;
            if (pct == lastPct) return;
            lastPct = pct;
            Progress = total > 0 ? (double)now / total : -1;
            Status = total > 0 ? $"Downloading… {now / 1_000_000} of {total / 1_000_000} MB" : $"Downloading… {now / 1_000_000} MB";
            Notify();
        };
        using (var head = new HttpRequestMessage(HttpMethod.Get, url))
        {
            head.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var probe = await Http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead);
            if (probe.StatusCode == System.Net.HttpStatusCode.PartialContent && probe.Content.Headers.ContentRange?.Length is long len)
                total = len;
        }
        const int parts = 4;
        if (total >= 8_000_000)
        {
            try
            {
                using (var f = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write)) f.SetLength(total);
                long size = (total + parts - 1) / parts;
                await Task.WhenAll(Enumerable.Range(0, parts).Select(async k =>
                {
                    long start = k * size, len = Math.Min(size, total - start);
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, start + len - 1);
                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    if (resp.StatusCode != System.Net.HttpStatusCode.PartialContent) throw new InvalidOperationException("no ranged download");
                    await using var src = await resp.Content.ReadAsStreamAsync();
                    await using var dst = new FileStream(path, FileMode.Open, System.IO.FileAccess.Write, FileShare.Write);
                    dst.Position = start;
                    var buf = new byte[1 << 16];
                    long left = len;
                    int n;
                    while (left > 0 && (n = await src.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, left)))) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n));
                        left -= n;
                        count(n);
                    }
                    if (left != 0) throw new IOException("the download was cut short");
                }));
                return;
            }
            catch (InvalidOperationException) { got = 0; lastPct = -2; }
        }
        // One stream.
        using var one = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        one.EnsureSuccessStatusCode();
        total = one.Content.Headers.ContentLength ?? 0;
        await using var s = await one.Content.ReadAsStreamAsync();
        await using var d = File.Create(path);
        var b = new byte[1 << 16];
        int m;
        while ((m = await s.ReadAsync(b)) > 0)
        {
            await d.WriteAsync(b.AsMemory(0, m));
            count(m);
        }
    }

    /// <summary>A running program's files can be renamed over on Linux; then start the new one and quit.</summary>
    static void RestartUnix(string exe, string dir, string files, string staging)
    {
        foreach (var src in Directory.EnumerateFiles(files, "*", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(dir, Path.GetRelativePath(files, src));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Move(src, dst, overwrite: true);
        }
        Directory.Delete(staging, true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        OS.CreateProcess(exe, Array.Empty<string>());
        Main.QuitGame();
    }

    /// <summary>
    /// Windows locks a running program's files, so a small script waits for the game to close, moves the new files
    /// in and starts it again.
    /// </summary>
    static void RestartWindows(string exe, string dir, string files, string staging)
    {
        int pid = OS.GetProcessId();
        string script = Path.Combine(Path.GetTempPath(), "stationx-update.cmd");
        File.WriteAllText(script,
            "@echo off\r\n" +
            ":wait\r\n" +
            $"tasklist /FI \"PID eq {pid}\" 2>NUL | find \"{pid}\" >NUL && (timeout /t 1 /nobreak >NUL & goto wait)\r\n" +
            $"robocopy \"{files}\" \"{dir}\" /E /MOVE /NFL /NDL /NJH /NJS /NP >NUL\r\n" +
            $"rmdir /S /Q \"{staging}\"\r\n" +
            $"start \"\" \"{exe}\"\r\n" +
            "del \"%~f0\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false });
        Main.QuitGame();
    }

    static void Notify() => Callable.From(() => Changed?.Invoke()).CallDeferred();
}
