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
    static string? _assetUrl, _patchUrl, _baseUrl;
    static long _assetSize, _patchSize;
    /// <summary>True once the small patch is known to fit this copy (same engine and .NET runtime as the new release).</summary>
    public static bool PatchFits { get; private set; }
    /// <summary>What the update will download, in MB.</summary>
    public static int DownloadMB => (int)Math.Ceiling((PatchFits ? _patchSize : _assetSize) / 1e6);
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

            Status = "Downloading…"; Notify();
            using (var resp = await Http.GetAsync(patch ? _patchUrl : _assetUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(archive);
                var buf = new byte[1 << 16];
                long got = 0;
                int n, lastPct = -2;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    got += n;
                    int pct = total > 0 ? (int)(got * 100 / total) : -1;
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        Progress = total > 0 ? (double)got / total : -1;
                        Status = total > 0 ? $"Downloading… {got / 1_000_000} of {total / 1_000_000} MB" : $"Downloading… {got / 1_000_000} MB";
                        Notify();
                    }
                }
            }

            Progress = 1;
            Status = "Unpacking…"; Notify();
            string files = Path.Combine(staging, "files");
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
