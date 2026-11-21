using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using StationX.View;
using Xunit;

namespace StationX.Tests;

public class PckDeltaTests
{
    /// <summary>The manifest as tools/pckblocks.py writes it.</summary>
    static string ManifestOf(byte[] data, int bs)
    {
        var lines = new List<string> { $"pckblocks 1 {data.Length} {bs} {Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()}" };
        for (int i = 0; i < data.Length; i += bs)
        {
            var block = data.AsSpan(i, Math.Min(bs, data.Length - i));
            lines.Add($"{PckDelta.Weak(block):x8} {Convert.ToHexString(SHA256.HashData(block)).ToLowerInvariant()}");
        }
        return string.Join("\n", lines) + "\n";
    }

    static byte[] Random(int n, int seed)
    {
        var b = new byte[n];
        new Random(seed).NextBytes(b);
        return b;
    }

    [Fact]
    public void AShiftedAndEditedPackFetchesOnlyWhatChanged()
    {
        const int bs = 4096;
        var old = Random(300_000, 1);
        // The new pack: a few bytes inserted early (everything after shifts), one stretch rewritten, a bit appended.
        var nw = old.Take(10_000).Concat(Random(37, 2)).Concat(old.Skip(10_000)).ToArray();
        Random(5000, 3).CopyTo(nw, 150_000);
        nw = nw.Concat(Random(1234, 4)).ToArray();

        var m = PckDelta.Parse(ManifestOf(nw, bs))!;
        Assert.NotNull(m);
        var plan = PckDelta.Match(m, old);
        Assert.True(plan.FetchBytes < 8 * bs, $"fetched {plan.FetchBytes}");
        var fetched = plan.Ranges(bridge: 0).ToDictionary(r => r.start, r => nw.AsSpan((int)r.start, (int)r.length).ToArray());
        string path = Path.Combine(Path.GetTempPath(), $"pckdelta-{Guid.NewGuid():N}.pck");
        try
        {
            Assert.True(PckDelta.Assemble(plan, old, fetched, path));
            Assert.Equal(nw, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AWrongPieceIsCaught()
    {
        const int bs = 4096;
        var old = Random(50_000, 5);
        var nw = Random(50_000, 6);
        var m = PckDelta.Parse(ManifestOf(nw, bs))!;
        var plan = PckDelta.Match(m, old);
        Assert.Equal(nw.Length, plan.FetchBytes);
        var fetched = plan.Ranges().ToDictionary(r => r.start, r => new byte[r.length]);
        string path = Path.Combine(Path.GetTempPath(), $"pckdelta-{Guid.NewGuid():N}.pck");
        try { Assert.False(PckDelta.Assemble(plan, old, fetched, path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BadManifestsAreRefused()
    {
        Assert.Null(PckDelta.Parse(""));
        Assert.Null(PckDelta.Parse("pckblocks 2 10 4 ab\n00000000 aa\n"));
        Assert.Null(PckDelta.Parse("pckblocks 1 10 4 ab\n00000000 aa\n"));   // 10 bytes need 3 blocks
    }

    /// <summary>With PCK_OLD, PCK_NEW and PCK_MANIFEST (from tools/pckblocks.py) set: the Python and C# sides agree.</summary>
    [Fact]
    public void RealPacks()
    {
        string? o = Environment.GetEnvironmentVariable("PCK_OLD"), n = Environment.GetEnvironmentVariable("PCK_NEW"), mf = Environment.GetEnvironmentVariable("PCK_MANIFEST");
        if (o == null || n == null || mf == null) return;
        var m = PckDelta.Parse(File.ReadAllText(mf))!;
        Assert.NotNull(m);
        var old = File.ReadAllBytes(o);
        var nw = File.ReadAllBytes(n);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var plan = PckDelta.Match(m, old);
        Console.WriteLine($"match {sw.ElapsedMilliseconds} ms, fetch {plan.FetchBytes / 1e6:0.00} MB in {plan.Ranges().Count} ranges");
        var fetched = plan.Ranges().ToDictionary(r => r.start, r => nw.AsSpan((int)r.start, (int)r.length).ToArray());
        string path = Path.Combine(Path.GetTempPath(), $"pckdelta-{Guid.NewGuid():N}.pck");
        try { Assert.True(PckDelta.Assemble(plan, old, fetched, path)); }
        finally { File.Delete(path); }
    }
}

public class RangedDownloadLive
{
    /// <summary>With LIVE_RANGE=1: GitHub's release downloads answer a ranged request with just that range, across the redirect.</summary>
    [Fact]
    public async System.Threading.Tasks.Task RangeSurvivesTheRedirect()
    {
        if (Environment.GetEnvironmentVariable("LIVE_RANGE") != "1") return;
        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("StationX-updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get,
            "https://github.com/HenrikBacher/StationX/releases/download/v0.46.0/StationX-linux-x86_64-patch.zip");
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1000, 1999);
        using var resp = await http.SendAsync(req);
        Assert.Equal(System.Net.HttpStatusCode.PartialContent, resp.StatusCode);
        Assert.Equal(1000, (await resp.Content.ReadAsByteArrayAsync()).Length);
    }
}
