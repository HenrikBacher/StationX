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
    static readonly int[] Seeds = System.Linq.Enumerable.Range(0, 20).Select(i => 3 + i * 37).ToArray();

    [Fact]
    public void Report()
    {
        if (Environment.GetEnvironmentVariable("BALANCE") != "1") return;
        foreach (var d in Enum.GetValues<Difficulty>())
        {
            int wins = 0;
            foreach (var seed in Seeds)
            {
                var g = GameState.NewGame(seed, d);
                var ai = new AutoPlayer(g);
                double minRep = g.Reputation, minFunds = g.Funds;
                while (g.Status == GameStatus.Playing && g.Day < 120)
                {
                    if (g.ActCompletePending) g.ContinueAfterAct();
                    ai.Think();
                    g.Advance(60);
                    g.Events.Clear();
                    minRep = Math.Min(minRep, g.Reputation);
                    minFunds = Math.Min(minFunds, g.Funds);
                }
                if (g.Status == GameStatus.Won) wins++;
                output.WriteLine($"{d,-6} seed {seed,3}: {g.Status,-7} day {g.Day,3} act {g.ActIndex,2} " +
                                 $"rep min {minRep,3:0} end {g.Reputation,3:0}  funds min £{minFunds,5:0} end £{g.Funds,5}  " +
                                 $"contracts {g.Stats.ContractsCompleted}/{g.Stats.ContractsCompleted + g.Stats.ContractsFailed}  breakdowns {g.Stats.Breakdowns}  score {g.Score()} {g.Honour().honour}");
            }
            output.WriteLine($"{d}: {wins}/{Seeds.Length} won");
        }
    }
}
