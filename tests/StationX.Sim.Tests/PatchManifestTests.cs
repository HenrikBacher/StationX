using System;
using System.Diagnostics;
using System.IO;
using StationX.View;
using Xunit;

namespace StationX.Tests;

public class PatchManifestTests
{
    /// <summary>A fake install, and its manifest written by sha256sum the way export.sh writes it.</summary>
    static (string dir, string manifest) Install()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stationx-patch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "data_StationX_linuxbsd_x86_64"));
        File.WriteAllText(Path.Combine(dir, "StationX.x86_64"), "engine");
        File.WriteAllText(Path.Combine(dir, "StationX.pck"), "game pack");
        File.WriteAllText(Path.Combine(dir, "data_StationX_linuxbsd_x86_64", "StationX.dll"), "game code");
        File.WriteAllText(Path.Combine(dir, "data_StationX_linuxbsd_x86_64", "System.Private.CoreLib.dll"), "runtime");
        File.WriteAllText(Path.Combine(dir, "data_StationX_linuxbsd_x86_64", "A file.dll"), "spaced");
        var psi = new ProcessStartInfo("bash", new[] { "-c",
            "find . -type f ! -name StationX.pck ! -path './data_*/StationX.*' | LC_ALL=C sort | tr '\\n' '\\0' | xargs -0 sha256sum" })
            { WorkingDirectory = dir, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        string manifest = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (dir, manifest);
    }

    [Fact]
    public void ThePatchFitsOnlyAnUnchangedBase()
    {
        if (OperatingSystem.IsWindows()) return;   // the manifest is made with bash, as in CI
        var (dir, manifest) = Install();
        try
        {
            Assert.Contains("./StationX.x86_64", manifest);
            Assert.DoesNotContain("StationX.pck", manifest);
            Assert.DoesNotContain("/StationX.dll", manifest);
            Assert.True(PatchManifest.BaseMatches(manifest, dir));

            // The game's own files and extra files don't matter.
            File.WriteAllText(Path.Combine(dir, "StationX.pck"), "an older game pack");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "the player's own file");
            Assert.True(PatchManifest.BaseMatches(manifest, dir));

            // A different runtime does, and so does a missing file.
            File.WriteAllText(Path.Combine(dir, "data_StationX_linuxbsd_x86_64", "System.Private.CoreLib.dll"), "another runtime");
            Assert.False(PatchManifest.BaseMatches(manifest, dir));
            File.WriteAllText(Path.Combine(dir, "data_StationX_linuxbsd_x86_64", "System.Private.CoreLib.dll"), "runtime");
            File.Delete(Path.Combine(dir, "data_StationX_linuxbsd_x86_64", "A file.dll"));
            Assert.False(PatchManifest.BaseMatches(manifest, dir));

            Assert.False(PatchManifest.BaseMatches("", dir));
            Assert.False(PatchManifest.BaseMatches("not a manifest", dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
