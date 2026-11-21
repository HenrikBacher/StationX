using System;
using System.Linq;
using StationX.Sim;
using Xunit;
using Xunit.Abstractions;

namespace StationX.Tests;

/// <summary>
/// Balance report: autoplays the Enigma campaign on several seeds per difficulty.
/// Only runs with BALANCE=1 (dotnet test --filter Balance, with the variable set); otherwise it returns at once.
/// </summary>
public class Balance(ITestOutputHelper output)
{
    // Twenty seeds per difficulty: six were too few to tell a real change from luck.
    // BALANCE_SEEDS=40 for a finer reading when tuning.
    static readonly int[] Seeds = System.Linq.Enumerable.Range(0, int.TryParse(Environment.GetEnvironmentVariable("BALANCE_SEEDS"), out var n) ? n : 20)
                                                       .Select(i => 3 + i * 37).ToArray();

    /// <summary>BALANCE_DIFF=Normal,Hard plays only those difficulties.</summary>
    static readonly Difficulty[] Difficulties = Environment.GetEnvironmentVariable("BALANCE_DIFF") is { Length: > 0 } only
        ? only.Split(',').Select(Enum.Parse<Difficulty>).ToArray()
        : Enum.GetValues<Difficulty>();

    sealed record Result(Difficulty D, int Seed, bool Won, string Line);

    static Result Play(Difficulty d, int seed)
    {
        var g = GameState.NewGame(seed, d);
        var ai = new AutoPlayer(g) { UseWorkload = Environment.GetEnvironmentVariable("BALANCE_NOWORKLOAD") != "1" };
        double minRep = g.Reputation, minFunds = g.Funds;
        while (g.Status == GameStatus.Playing && g.Day < 320)
        {
            if (g.ActCompletePending) g.ContinueAfterAct();
            ai.Think();
            g.Advance(60);
            g.Events.Clear();
            minRep = Math.Min(minRep, g.Reputation);
            minFunds = Math.Min(minFunds, g.Funds);
        }
        return new(d, seed, g.Status == GameStatus.Won,
            $"{d,-6} seed {seed,3}: {g.Status,-7} day {g.Day,3} act {g.ActIndex,2} " +
            $"rep min {minRep,3:0} end {g.Reputation,3:0}  funds min £{minFunds,5:0} end £{g.Funds,5}  " +
            $"contracts {g.Stats.ContractsCompleted}/{g.Stats.ContractsCompleted + g.Stats.ContractsFailed}  breakdowns {g.Stats.Breakdowns}  kisses {g.Stats.Kisses}  standing {g.Stats.StandingAccepted} ms {g.Stats.MilestonesMet}/{g.Stats.MilestonesMet + g.Stats.MilestonesMissed}  score {g.Score()} {g.Honour().honour}");
    }

    [Fact]
    public void Report()
    {
        if (Environment.GetEnvironmentVariable("BALANCE") != "1") return;
        // Every game is seeded and shares nothing, so they run side by side; the report comes out in order.
        var games = Difficulties.SelectMany(d => Seeds.Select(seed => (d, seed))).ToArray();
        var results = new Result[games.Length];
        // Handed out one game at a time, so a long one doesn't hold back a batch.
        var order = System.Collections.Concurrent.Partitioner.Create(Enumerable.Range(0, games.Length), System.Collections.Concurrent.EnumerablePartitionerOptions.NoBuffering);
        System.Threading.Tasks.Parallel.ForEach(order, i => results[i] = Play(games[i].d, games[i].seed));
        foreach (var d in Difficulties)
        {
            var mine = results.Where(r => r.D == d).ToList();
            foreach (var r in mine) output.WriteLine(r.Line);
            output.WriteLine($"{d}: {mine.Count(r => r.Won)}/{Seeds.Length} won");
        }
    }
}

/// <summary>What the autoplayer scores in each scenario, to set the medal marks (SCENARIOS=1).</summary>
public class ScenarioCalibration
{
    readonly Xunit.Abstractions.ITestOutputHelper output;
    public ScenarioCalibration(Xunit.Abstractions.ITestOutputHelper o) => output = o;

    [Fact]
    public void Report()
    {
        if (Environment.GetEnvironmentVariable("SCENARIOS") != "1") return;
        int n = int.TryParse(Environment.GetEnvironmentVariable("SCENARIO_SEEDS"), out int k) ? k : 20;
        foreach (var s in Scenarios.All)
        {
            var scores = new int[n];
            var lost = new bool[n];
            var prep = new double[n];
            System.Threading.Tasks.Parallel.For(0, n, i =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var g = GameState.NewScenario(3 + 37 * i, Difficulty.Normal, s.Id);
                prep[i] = sw.Elapsed.TotalSeconds;
                var ai = new AutoPlayer(g);
                while (g.Status == GameStatus.Playing)
                {
                    if (g.ActCompletePending) g.ContinueAfterAct();
                    ai.Think();
                    g.Advance(60);
                    g.Events.Clear();
                }
                scores[i] = g.ScenarioScore();
                lost[i] = g.Status == GameStatus.Lost;
            });
            var sorted = scores.OrderBy(x => x).ToArray();
            output.WriteLine($"{s.Id,-9} lost {lost.Count(x => x)}/{n}  p25 {sorted[n / 4]}  median {sorted[n / 2]}  p75 {sorted[3 * n / 4]}  p90 {sorted[9 * n / 10]}  " +
                             $"prep {prep.Average():0.0}s max {prep.Max():0.0}s  medals now: {string.Join(" ", Enum.GetValues<Medal>().Select(m => $"{m} {Enumerable.Range(0, n).Count(i => !lost[i] && Scenarios.MedalFor(s, scores[i]) == m)}"))}");
        }
    }
}
