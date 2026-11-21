using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace StationX.View;

/// <summary>
/// The updater's delta download of the game pack. A release lists the new StationX.pck as blocks (tools/pckblocks.py:
/// rsync's weak rolling checksum and a SHA-256 for each 64 KB block). Most of a new pack is the old one's textures and
/// scenes, perhaps shifted by a few bytes, so the updater rolls the weak checksum over its own pack, confirms each hit
/// with the SHA-256, and fetches only the blocks it couldn't find (ranged requests on the raw pack). The result is checked
/// against the whole new pack's SHA-256 before it's used. Plain C#, so the sim tests cover it.
/// </summary>
public static class PckDelta
{
    const int M = 65521;

    public sealed record Block(uint Weak, string Strong);

    public sealed record Manifest(long Size, int BlockSize, string Sha256, List<Block> Blocks);

    /// <summary>Where each new block comes from: an offset in the old pack, or -1 to fetch it.</summary>
    public sealed record Plan(Manifest Manifest, long[] From)
    {
        public long FetchBytes
        {
            get
            {
                long n = 0;
                for (int i = 0; i < From.Length; i++) if (From[i] < 0) n += BlockLength(i);
                return n;
            }
        }

        public int BlockLength(int i) => (int)Math.Min(Manifest.BlockSize, Manifest.Size - (long)i * Manifest.BlockSize);

        /// <summary>The bytes to fetch as ranges (start, length): neighbouring missing blocks joined, small gaps bridged.</summary>
        public List<(long start, long length)> Ranges(long bridge = 256 * 1024)
        {
            var list = new List<(long, long)>();
            for (int i = 0; i < From.Length; i++)
            {
                if (From[i] >= 0) continue;
                long start = (long)i * Manifest.BlockSize, end = start + BlockLength(i);
                if (list.Count > 0 && start - (list[^1].Item1 + list[^1].Item2) <= bridge)
                    list[^1] = (list[^1].Item1, end - list[^1].Item1);
                else list.Add((start, end - start));
            }
            return list;
        }
    }

    public static Manifest? Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return null;
        var head = lines[0].Split(' ');
        if (head.Length != 5 || head[0] != "pckblocks" || head[1] != "1") return null;
        if (!long.TryParse(head[2], out long size) || !int.TryParse(head[3], out int bs) || bs <= 0) return null;
        var blocks = new List<Block>();
        for (int i = 1; i < lines.Length; i++)
        {
            var p = lines[i].Split(' ');
            if (p.Length != 2 || !uint.TryParse(p[0], NumberStyles.HexNumber, null, out uint w)) return null;
            blocks.Add(new Block(w, p[1].ToLowerInvariant()));
        }
        if (blocks.Count != (int)((size + bs - 1) / bs)) return null;
        return new Manifest(size, bs, head[4].ToLowerInvariant(), blocks);
    }

    /// <summary>rsync's weak checksum of a block: b &lt;&lt; 16 | a.</summary>
    public static uint Weak(ReadOnlySpan<byte> block)
    {
        long a = 0, b = 0;
        int n = block.Length;
        for (int k = 0; k < n; k++)
        {
            a += block[k];
            b += (long)(n - k) * block[k];
        }
        return (uint)(((b % M) << 16) | (a % M));
    }

    static string Sha(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>Finds every full block of the new pack somewhere in the old one.</summary>
    public static Plan Match(Manifest m, byte[] old)
    {
        var from = new long[m.Blocks.Count];
        Array.Fill(from, -1L);
        int bs = m.BlockSize;
        // Weak checksum → the full-size blocks that have it (the last, short block is always fetched).
        var byWeak = new Dictionary<uint, List<int>>();
        for (int i = 0; i < m.Blocks.Count; i++)
        {
            if ((long)(i + 1) * bs > m.Size) continue;
            if (!byWeak.TryGetValue(m.Blocks[i].Weak, out var l)) byWeak[m.Blocks[i].Weak] = l = new List<int>();
            l.Add(i);
        }
        if (old.Length < bs || byWeak.Count == 0) return new Plan(m, from);
        int left = byWeak.Values.Sum(l => l.Count);
        long a = 0, b = 0;
        void Start(int at)
        {
            a = 0; b = 0;
            for (int k = 0; k < bs; k++) { a += old[at + k]; b += (long)(bs - k) * old[at + k]; }
            a %= M; b %= M;
        }
        int i0 = 0;
        Start(0);
        while (true)
        {
            uint w = (uint)((b << 16) | a);
            bool hit = false;
            if (byWeak.TryGetValue(w, out var cands))
            {
                string? strong = null;
                foreach (int c in cands)
                {
                    if (from[c] >= 0) continue;
                    strong ??= Sha(old.AsSpan(i0, bs));
                    if (strong == m.Blocks[c].Strong) { from[c] = i0; left--; hit = true; }
                }
            }
            if (left == 0) break;
            if (hit && i0 + 2 * bs <= old.Length)
            {
                // A block found: the next is most likely right after it.
                i0 += bs;
                Start(i0);
                continue;
            }
            if (i0 + bs >= old.Length) break;
            byte outB = old[i0], inB = old[i0 + bs];
            a = (a - outB + inB) % M; if (a < 0) a += M;
            b = (b - (long)bs * outB + a) % M; if (b < 0) b += M;
            i0++;
        }
        return new Plan(m, from);
    }

    /// <summary>
    /// Writes the new pack: blocks found in the old pack copied across, the rest from the fetched ranges (start → bytes).
    /// Returns false if the result isn't the release's pack.
    /// </summary>
    public static bool Assemble(Plan plan, byte[] old, IReadOnlyDictionary<long, byte[]> fetched, string path)
    {
        var m = plan.Manifest;
        var ranges = new List<(long start, byte[] data)>();
        foreach (var kv in fetched) ranges.Add((kv.Key, kv.Value));
        using (var f = File.Create(path))
        {
            for (int i = 0; i < m.Blocks.Count; i++)
            {
                int len = plan.BlockLength(i);
                long at = (long)i * m.BlockSize;
                if (plan.From[i] >= 0)
                {
                    f.Write(old, (int)plan.From[i], len);
                    continue;
                }
                var r = ranges.Find(x => x.start <= at && at + len <= x.start + x.data.Length);
                if (r.data == null) return false;
                f.Write(r.data, (int)(at - r.start), len);
            }
        }
        using var check = File.OpenRead(path);
        return check.Length == m.Size && Convert.ToHexString(SHA256.HashData(check)).Equals(m.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
