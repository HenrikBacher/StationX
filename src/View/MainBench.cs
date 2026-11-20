using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>Measuring tools for screenshot runs: --bench=N, --nofx and STX_COUNT (see CLAUDE.md).</summary>
public partial class Main
{
    // ------------------------------------------------------------- --bench=N
    // Runs N frames at 6x (or --speed) after the shot's warm-up and prints where the time went, per frame:
    // the sim, events and the HUD's reaction, stations, people, the rest of the world, and the light and weather;
    // then the engine's own process and physics times and the measured render time on CPU and GPU.

    int _bench, _benchFrame;
    bool _benching;
    long _benchAt;
    readonly double[] _benchMs = new double[6], _benchThis = new double[6], _benchWorst = new double[6];
    double _benchWorstFrame;
    (int g0, int g1, int g2) _benchGc;
    readonly List<(double ms, string what)> _benchSpikes = new();
    string _benchWorstAt = "";
    double _benchProcess, _benchCpu, _benchGpu, _benchDraws, _benchWall;
    long _benchStart;
    static readonly string[] BenchParts = { "sim", "events", "stations", "people", "world", "daylight" };

    void BenchMark(int part)
    {
        if (!_benching) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (part >= 0)
        {
            double ms = (now - _benchAt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _benchMs[part] += ms;
            _benchThis[part] = ms;
        }
        _benchAt = now;
    }

    void BenchFrame()
    {
        if (_benchFrame == 0) _benchStart = System.Diagnostics.Stopwatch.GetTimestamp();
        double total = _benchThis.Sum();
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        string gcs = (gc2 > _benchGc.g2 ? " gen2" : gc1 > _benchGc.g1 ? " gen1" : gc0 > _benchGc.g0 ? " gen0" : "");
        _benchGc = (gc0, gc1, gc2);
        if (_benchFrame > 0)
        {
            _benchSpikes.Add((total, $"{total,6:0.0} ms day {State.Day} {State.Now % 1440 / 60:00}:{State.Now % 60:00}{gcs}: " +
                string.Join(", ", BenchParts.Select((p, i) => $"{p} {_benchThis[i]:0.0}"))));
            if (_benchSpikes.Count > 40) { _benchSpikes.Sort((a, b) => b.ms.CompareTo(a.ms)); _benchSpikes.RemoveRange(5, _benchSpikes.Count - 5); }
        }
        if (_benchFrame > 0 && total > _benchWorstFrame)
        {
            _benchWorstFrame = total;
            _benchThis.CopyTo(_benchWorst, 0);
            _benchWorstAt = $"day {State.Day} {State.Now % 1440 / 60:00}:{State.Now % 60:00}{gcs}";
        }
        var vp = GetViewport().GetViewportRid();
        _benchProcess += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000;
        _benchCpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp) + RenderingServer.GetFrameSetupTimeCpu();
        _benchGpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
        _benchDraws += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        if (++_benchFrame < _bench) return;
        int n = _benchFrame;
        _benchWall = (System.Diagnostics.Stopwatch.GetTimestamp() - _benchStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        GD.Print($"BENCH {n} frames at {Speed}x, {State.Staff.Count} staff, {State.Stations.Count} stations, day {State.Day}");
        for (int i = 0; i < BenchParts.Length; i++) GD.Print($"BENCH {BenchParts[i],-10} {_benchMs[i] / n,7:0.000} ms");
        GD.Print($"BENCH {"main",-10} {_benchMs.Sum() / n,7:0.000} ms (of process {_benchProcess / n:0.000} ms)");
        GD.Print($"BENCH {"worst",-10} {_benchWorstFrame,7:0.000} ms at {_benchWorstAt}: " + string.Join(", ", BenchParts.Select((p, i) => $"{p} {_benchWorst[i]:0.0}")));
        _benchSpikes.Sort((a, b) => b.ms.CompareTo(a.ms));
        foreach (var (_, what) in _benchSpikes.Take(5)) GD.Print($"BENCH spike {what}");
        GD.Print($"BENCH {"render cpu",-10} {_benchCpu / n,7:0.000} ms, gpu {_benchGpu / n:0.000} ms, {_benchDraws / n:0} draws");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string json = SaveGame.Save(State);
        double first = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        json = SaveGame.Save(State);
        GD.Print($"BENCH {"save",-10} {sw.Elapsed.TotalMilliseconds,7:0.000} ms ({first:0} ms the first time), {json.Length / 1024} KB");
        GD.Print($"BENCH {"frame",-10} {_benchWall / n,7:0.000} ms wall ({n * 1000 / _benchWall:0} fps)");
        QuitGame();
    }

    /// <summary>STX_COUNT=1 with --shot: visible surfaces by owner (stations by type, buildings by kind and material).</summary>
    void PrintSurfaceCounts()
    {
        var counts = new System.Collections.Generic.Dictionary<string, int>();
        void Walk(Node n, string owner, bool vis)
        {
            foreach (var c in n.GetChildren())
            {
                bool v = vis && (c is not Node3D n3 || n3.Visible);
                string o = owner;
                if (n == this || n == _park) o = owner + "/" + c.Name;
                if (n == _stationRoot && c is StationModel sm) o = "/Stations/" + sm.Type;
                if (n.Name == "Buildings") o = "/Park/Buildings/" + c.GetType().Name + (c is Node3D { } cc && cc.GetChildCount() > 0 ? "(container)" : "") +
                    (c is MeshInstance3D bm && bm.Mesh != null ? $" {(bm.MaterialOverride ?? bm.GetSurfaceOverrideMaterial(0) ?? bm.Mesh.SurfaceGetMaterial(0)) switch { BaseMaterial3D b3 => $"{b3.Transparency} {(b3.AlbedoTexture == null ? "notex" : b3.AlbedoTexture.ResourcePath == "" ? "codetex" + b3.AlbedoTexture.GetRid().Id % 1000 : b3.AlbedoTexture.ResourcePath)} {b3.AlbedoColor.ToHtml()} uv{b3.Uv1Scale.X:0.##} tri{b3.Uv1Triplanar} nm{b3.NormalEnabled}", ShaderMaterial => "shader", _ => "?" }}" : "");
                if (c is GeometryInstance3D gi && v) { int surf = gi is MeshInstance3D mi && mi.Mesh != null ? mi.Mesh.GetSurfaceCount() : 1; counts[o] = counts.GetValueOrDefault(o) + surf; }
                Walk(c, o, v);
            }
        }
        Walk(this, "", true);
        foreach (var (k, v) in counts.OrderByDescending(x => x.Value).Take(25)) GD.Print($"COUNT {v,5} {k}");
    }

    /// <summary>--nofx=ssao,ssil,... : switches effects off, to price each with --bench.</summary>
    void NoFx(string list)
    {
        foreach (var fx in list.Split(','))
        {
            var e = _env.Environment;
            switch (fx)
            {
                case "ssao": e.SsaoEnabled = false; break;
                case "ssil": e.SsilEnabled = false; break;
                case "glow": e.GlowEnabled = false; break;
                case "fog": e.FogEnabled = false; break;
                case "sky": e.AmbientLightSource = Godot.Environment.AmbientSource.Color; e.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled; break;
                case "shadow": _sun.ShadowEnabled = false; break;
                case "msaa": GetViewport().Msaa3D = Viewport.Msaa.Disabled; break;
                case "half": GetViewport().Scaling3DScale = 0.5f; break;
                case "orth": _sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal; break;
                case "split2": _sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits; break;
                case "4k": RenderingServer.DirectionalShadowAtlasSetSize(4096, true); break;
                case "2k": RenderingServer.DirectionalShadowAtlasSetSize(2048, true); break;
                case "adjust": e.AdjustmentEnabled = false; break;
            }
        }
    }

    void StartBench(int frames, bool speedGiven)
    {
        _bench = frames;
        if (!speedGiven) Speed = 6;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
    }

    /// <summary>Hands an event to the HUD, and names it if it took over a millisecond.</summary>
    void BenchEvent(GameEvent e)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        Hud.OnEvent(e);
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (ms > 1) GD.Print($"BENCH event {ms:0.0} ms {e.Kind}: {e.Text[..Math.Min(70, e.Text.Length)]}");
    }
}
