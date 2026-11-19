using System;
using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class GreatOpsTests
{
    static GameState At(string title, int seed = 9)
    {
        var g = GameState.NewGame(seed);
        g.DebugSkipToAct(Campaign.IndexOf(title));
        g.Funds = 20000;
        return g;
    }

    /// <summary>Counts the act's before-D-Day deliveries as done.</summary>
    static void Deliver(GameState g, double share)
    {
        foreach (var goal in g.Act.Goals.Where(x => x.Soft))
            g.Stats.Delivered[goal.Net!.Value] = Stats.Get(g.ActBaseline.Delivered, goal.Net!.Value) + (int)Math.Ceiling(goal.Target * share);
    }

    static void RunTo(GameState g, double at)
    {
        // The sim pauses on a finished act until the summary is read.
        while (g.Now < at && g.Status == GameStatus.Playing && !g.ActCompletePending) { g.Dilemmas.RemoveAll(d => d.Tag != "op"); g.Advance(30); }
    }

    [Fact]
    public void EveryOperationHasItsAct()
    {
        foreach (var op in GreatOps.All)
        {
            var act = Campaign.Acts.Single(a => a.Op == op.Id);
            Assert.Contains(act.Goals, x => x.Soft);
            Assert.Contains(act.Goals, x => x.Kind == GoalKind.Scripted && x.Tag == "op:" + op.Id && !x.Soft);
        }
        // In order, after their neighbours.
        Assert.True(Campaign.IndexOf("Barbarossa") == Campaign.IndexOf("U-110") + 1);
        Assert.True(Campaign.IndexOf("D-Day") > Campaign.IndexOf("Colossus"));
        Assert.Equal(Campaign.Acts.Length - 1, Campaign.IndexOf("Victory in Europe"));
    }

    [Fact]
    public void AWellPreparedInvasionSucceedsAndTheChapterMovesOn()
    {
        var g = At("D-Day");
        Assert.NotNull(g.CurrentOp);
        Deliver(g, 1);
        RunTo(g, g.DDayAt + 60);
        Assert.Equal(OpOutcome.Success, g.OpOutcomes["overlord"]);
        Assert.Equal(GameStatus.Playing, g.Status);
        Assert.True(g.Stats.RepByCause.GetValueOrDefault("Great operations") > 0);
        Assert.Null(g.CurrentOp);
        g.Advance(10);
        Assert.True(g.ActCompletePending, "the act is done once D-Day is past, whatever the soft goals");
    }

    [Fact]
    public void AnUnpreparedInvasionFailsAndEndsTheCampaign()
    {
        var g = At("D-Day");
        Assert.Equal(OpOutcome.Failure, GameState.OutcomeFor(g.Readiness()));
        RunTo(g, g.DDayAt + 60);
        Assert.Equal(OpOutcome.Failure, g.OpOutcomes["overlord"]);
        Assert.Equal(GameStatus.Lost, g.Status);
    }

    [Fact]
    public void AFailedOperationElsewhereCostsReputationOnly()
    {
        var g = At("Market Garden");
        g.Reputation = 100;
        RunTo(g, g.DDayAt + 60);
        Assert.Equal(OpOutcome.Failure, g.OpOutcomes["marketgarden"]);
        Assert.Equal(GameStatus.Playing, g.Status);
        Assert.True(g.Stats.RepByCause.GetValueOrDefault("Great operations") < 0);
    }

    [Fact]
    public void TheDecisionComesBeforeTheDayAndCounts()
    {
        var g = At("Market Garden");
        Deliver(g, 0.6);
        double before = g.Readiness();
        RunTo(g, g.DDayAt - 2 * Defs.MinutesPerDay);
        var d = g.Dilemmas.Single(x => x.Tag == "op");
        Assert.Equal("Panzers at Arnhem", d.Title);
        Assert.True(g.ResolveDilemma(d.Id, 0));
        Assert.True(g.Readiness() > before + 0.15);
        // Suspicion on a watched service eats readiness; a lockdown takes all of the secrecy share.
        double r = g.Readiness();
        g.AddSuspicion(Service.Heer, 80);
        Assert.True(g.Readiness() < r);
    }
}
