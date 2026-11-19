using System.Diagnostics;
using System.Reflection;
using StationX.Sim;
using Xunit;
using Xunit.Abstractions;

namespace StationX.Tests;

/// <summary>
/// Simulation speed and determinism. Opt-in:
/// PERF=1 [PERF_DAYS=300] [PERF_SEEDS=3,40] dotnet test tests/StationX.Sim.Tests --filter Perf --logger "console;verbosity=detailed"
/// prints milliseconds per simulated day with a breakdown by phase of the tick.
/// FINGERPRINT=path writes the end state of a set of autoplayed games to that file, or compares against it if it exists,
/// so a pure optimisation can be checked to leave every game bit-identical.
/// </summary>
public class Perf(ITestOutputHelper output)
{
    static readonly string[] Phases =
    {
        "OnNewDay", "TickYService", "TickContracts", "TickStaff", "MoveStaff", "WorkStations", "TickRemote", "TickRaids", "TickCrises",
        "TickInspection", "TickOperations", "TickSupplies", "TickGreatOp",
        "AllotCover", "ReleaseHeld", "AssignCarriers", "Schedule", "FinishRemovals", "CheckGoals",
    };

    /// <summary>Plays one game hour by hour like the balance report, timing each phase of StepOnce by reflection.</summary>
    [Fact]
    public void TimePerDay()
    {
        if (Environment.GetEnvironmentVariable("PERF") != "1") return;
        int days = int.Parse(Environment.GetEnvironmentVariable("PERF_DAYS") ?? "300");
        var seeds = (Environment.GetEnvironmentVariable("PERF_SEEDS") ?? "3,40").Split(',').Select(int.Parse);
        var diff = Enum.Parse<Difficulty>(Environment.GetEnvironmentVariable("PERF_DIFF") ?? "Easy");
        foreach (int seed in seeds)
        {
            var g = GameState.NewGame(seed, diff);
            var ai = new AutoPlayer(g);
            var calls = Phases.Select(p => (p, Bind(g, p))).ToArray();
            var phaseTicks = new long[Phases.Length];
            long aiTicks = 0;
            var total = Stopwatch.StartNew();
            var cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
            var block = Stopwatch.StartNew();
            int blockStart = 0;
            var sw = new Stopwatch();
            while (g.Status == GameStatus.Playing && g.Day < days)
            {
                if (g.ActCompletePending) g.ContinueAfterAct();
                long t0 = Stopwatch.GetTimestamp();
                ai.Think();
                aiTicks += Stopwatch.GetTimestamp() - t0;
                // One hour of StepOnce, phase by phase.
                for (int k = 0; k < 60 / Defs.Tick && g.Status == GameStatus.Playing && !g.ActCompletePending; k++)
                {
                    int dayBefore = g.Day;
                    g.Now += Defs.Tick;
                    for (int i = 0; i < calls.Length; i++)
                    {
                        var (name, f) = calls[i];
                        if (name == "OnNewDay" && g.Day == dayBefore) continue;
                        if (name == "AllotCover")
                        {
                            if (g.Now < g.NextScheduleAt) { i = Array.IndexOf(Phases, "CheckGoals"); continue; }
                            g.NextScheduleAt = g.Now + 2;
                        }
                        long a = Stopwatch.GetTimestamp();
                        f();
                        phaseTicks[i] += Stopwatch.GetTimestamp() - a;
                    }
                }
                g.Events.Clear();
                if (g.Day >= blockStart + 50)
                {
                    output.WriteLine($"  seed {seed} days {blockStart}-{g.Day}: {block.Elapsed.TotalMilliseconds / (g.Day - blockStart):0.0} ms/day, staff {g.Staff.Count}, stations {g.Stations.Count}, messages {g.Messages.Count}");
                    blockStart = g.Day;
                    block.Restart();
                }
            }
            double ms = total.Elapsed.TotalMilliseconds;
            // CPU time is the steadier measure on a busy machine: wall time counts the waits for a core.
            double cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds;
            output.WriteLine($"seed {seed} {diff}: {g.Status} day {g.Day}, {ms / Math.Max(1, g.Day):0.0} ms/day wall ({ms / 1000:0.0} s), " +
                             $"{cpu / Math.Max(1, g.Day):0.0} ms/day CPU, staff {g.Staff.Count}");
            double f2ms = 1000.0 / Stopwatch.Frequency;
            output.WriteLine($"    {"AutoPlayer.Think",-16} {aiTicks * f2ms,8:0} ms");
            foreach (var i in Enumerable.Range(0, Phases.Length).OrderByDescending(i => phaseTicks[i]))
                output.WriteLine($"    {Phases[i],-16} {phaseTicks[i] * f2ms,8:0} ms");
        }
    }

    static Action Bind(GameState g, string name)
    {
        var mi = typeof(GameState).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        if (mi.GetParameters().Length == 1) { var d = mi.CreateDelegate<Action<double>>(g); return () => d(Defs.Tick); }
        return mi.CreateDelegate<Action>(g);
    }

    /// <summary>The end state of one autoplayed game, precise enough to catch any change in the simulation.</summary>
    public static string Fingerprint(Difficulty d, int seed, int days)
    {
        var g = GameState.NewGame(seed, d);
        var ai = new AutoPlayer(g);
        while (g.Status == GameStatus.Playing && g.Day < days)
        {
            if (g.ActCompletePending) g.ContinueAfterAct();
            ai.Think();
            g.Advance(60);
            g.Events.Clear();
        }
        long h = 17;
        void Mix(double v) => h = unchecked(h * 31 + BitConverter.DoubleToInt64Bits(v));
        foreach (var s in g.Staff) { Mix(s.Id); Mix(s.X); Mix(s.Y); Mix(s.Energy); Mix(s.Morale); Mix(s.Path.Count); }
        foreach (var st in g.Stations) { Mix(st.Id); Mix(st.WorkDone); Mix(st.Wear); Mix(st.Input.Count); Mix(st.Output.Count); }
        foreach (var m in g.Messages.Values) { Mix(m.Id); Mix((int)m.Loc); Mix(m.LocId); }
        return $"{d} {seed}: {g.Status} day {g.Day} now {g.Now} act {g.ActIndex} funds {g.Funds} rep {g.Reputation:R} rng {g.Rng.State} " +
               $"delivered {g.Stats.DeliveredTotal} done {g.Stats.ContractsCompleted} failed {g.Stats.ContractsFailed} " +
               $"staff {g.Staff.Count} stations {g.Stations.Count} messages {g.Messages.Count} hash {h:x16}";
    }

    [Fact]
    public void CompareFingerprint()
    {
        if (Environment.GetEnvironmentVariable("FINGERPRINT") is not { Length: > 0 } path) return;
        int days = int.Parse(Environment.GetEnvironmentVariable("FINGERPRINT_DAYS") ?? "200");
        var games = new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard }
            .SelectMany(d => new[] { 3, 40, 77, 114 }.Select(s => (d, s))).ToArray();
        var lines = new string[games.Length];
        Parallel.For(0, games.Length, i => lines[i] = Fingerprint(games[i].d, games[i].s, days));
        foreach (var l in lines) output.WriteLine(l);
        if (!File.Exists(path)) { File.WriteAllLines(path, lines); return; }
        var want = File.ReadAllLines(path);
        Assert.Equal(want, lines);
    }

    /// <summary>The same seed played twice gives the same game: nothing in the simulation leans on process-random hashing.</summary>
    [Fact]
    public void SameSeedSameGame()
    {
        var a = Fingerprint(Difficulty.Normal, 11, 12);
        var b = Fingerprint(Difficulty.Normal, 11, 12);
        Assert.Equal(a, b);
    }
}
