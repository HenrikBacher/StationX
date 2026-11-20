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
