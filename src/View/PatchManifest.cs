using System;
using System.IO;
using System.Security.Cryptography;

namespace StationX.View;

/// <summary>
/// The updater's check that a small patch fits: the release lists the SHA-256 of every file besides the game's own
/// (`sha256sum` output, from export.sh), and the patch fits when this copy has all of them unchanged. Plain C#, so the
/// sim tests can cover it.
/// </summary>
public static class PatchManifest
{
    /// <summary>
    /// Whether every file the patch relies on (each line of the manifest: SHA-256, two spaces, ./relative path) is here
    /// with exactly that content. Extra files don't matter.
    /// </summary>
    public static bool BaseMatches(string manifest, string dir)
    {
        int checkedFiles = 0;
        foreach (var raw in manifest.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            int sep = line.IndexOf("  ", StringComparison.Ordinal);
            if (sep != 64) return false;
            var rel = line[(sep + 2)..];
            if (rel.StartsWith("./")) rel = rel[2..];
            var path = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) return false;
            using var f = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(f)).Equals(line[..64], StringComparison.OrdinalIgnoreCase)) return false;
            checkedFiles++;
        }
        return checkedFiles > 0;
    }
}
