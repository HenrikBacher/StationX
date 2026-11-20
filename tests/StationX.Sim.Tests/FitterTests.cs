using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class FitterTests
{
    [Fact]
    public void AFitterOnWatchMendsABreakdownFastAndCheap()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var bombe = g.Stations.First(s => s.Type == StationType.Bombe);
        var fitter = TestUtil.Add(g, Role.Fitter, bombe.UseTile.x + 0.5, bombe.UseTile.y + 1.5);
        g.Funds = 5000;
        g.DebugBreakDown(bombe.Id);
        Assert.Equal(5000 - (int)System.Math.Round(Defs.RepairCost * Defs.FitterPartsShare), g.Funds);
        Assert.Equal(1, g.Stats.FitterRepairs);
        double outside = Defs.RepairMinutesFor(StationType.Bombe);
        double until = g.Now + outside;
        while (bombe.IsDown(g.Now) && g.Now < until) { g.Advance(5); g.Events.Clear(); }
        Assert.False(bombe.IsDown(g.Now));
        Assert.True(g.Now < until - outside * 0.3, "mended well before the outside fitters would have");
        Assert.Null(bombe.FitterId);
    }

    [Fact]
    public void WithoutAFitterTheOutsideFittersChargeInFull()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var bombe = g.Stations.First(s => s.Type == StationType.Bombe);
        g.Funds = 5000;
        g.DebugBreakDown(bombe.Id);
        Assert.Equal(5000 - Defs.RepairCost, g.Funds);
        Assert.Equal(0, g.Stats.FitterRepairs);
    }

    [Fact]
    public void IdleFittersServiceWornMachines()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var bombe = g.Stations.First(s => s.Type == StationType.Bombe);
        TestUtil.Add(g, Role.Fitter, bombe.UseTile.x + 0.5, bombe.UseTile.y + 1.5);
        bombe.Wear = 0.9;
        for (int i = 0; i < 24 && g.Stats.FitterServices == 0; i++) { g.Advance(5); g.Events.Clear(); }
        Assert.Equal(1, g.Stats.FitterServices);
        Assert.True(bombe.Wear < 0.1);
        g.Advance(Defs.ServiceMinutes);
        Assert.False(bombe.IsDown(g.Now));
        Assert.DoesNotContain(g.Events, e => e.Text.Contains("serviced"));   // routine: no announcement
    }

    [Fact]
    public void TheRafOffersFittersWithTheBombe()
    {
        var g = GameState.NewGame(7);
        Assert.False(g.RoleOpen(Role.Fitter));
        Assert.DoesNotContain(g.Candidates, c => c.Role == Role.Fitter);
        g.Researched.Add(ResearchId.Bombe);
        g.FitterCandidates();
        Assert.True(g.RoleOpen(Role.Fitter));
        Assert.Equal(2, g.Candidates.Count(c => c.Role == Role.Fitter));
        var f = g.Candidates.First(c => c.Role == Role.Fitter);
        g.Funds = 5000;
        Assert.True(g.Hire(f.Id));
        Assert.NotNull(f.Posted);   // fitters are posted like anyone but messengers
    }
}
