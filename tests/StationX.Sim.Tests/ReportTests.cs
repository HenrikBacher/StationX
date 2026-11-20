using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class ReportTests
{
    [Fact]
    public void ReportsTurnBreakthroughsIntoStandingAtARisingPrice()
    {
        var g = GameState.NewGame(7);
        g.DebugSkipToAct(Campaign.IndexOf("Their Finest Hour"));
        g.Points = 500;
        g.Reputation = 40;
        Assert.Null(g.CantReport());
        int cost = g.ReportCost;
        Assert.True(g.ReportToChiefs());
        Assert.Equal(500 - cost, g.Points);
        Assert.Equal(40 + Defs.ReportRep, g.Reputation, 6);
        Assert.Equal(cost + Defs.ReportCostStep, g.ReportCost);
        Assert.NotNull(g.CantReport());   // one a fortnight
        Assert.False(g.ReportToChiefs());
        g.Now += Defs.ReportEveryDays * Defs.MinutesPerDay;
        Assert.Null(g.CantReport());
        g.Points = g.ReportCost - 1;
        Assert.NotNull(g.CantReport());
    }
}
