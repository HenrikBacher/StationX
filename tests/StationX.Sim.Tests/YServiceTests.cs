using System;
using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class YServiceTests
{
    static GameState Started(int seed = 7)
    {
        var g = GameState.NewGame(seed);
        g.Advance(5);
        return g;
    }

    [Fact]
    public void NewNetworksTakeSpareSetsAndGiveThemBack()
    {
        var g = Started();
        Assert.Equal(Defs.SetsWanted, g.CoverOf(Net.Police));
        Assert.Equal(Defs.StartSets - Defs.SetsWanted, g.SpareSets);
        g.DebugSkipToAct(2);
        g.Advance(5);
        foreach (var n in g.CoveredNets()) Assert.True(g.CoverOf(n) > 0 || g.SpareSets == 0, $"{n} uncovered with sets spare");
        Assert.Equal(g.ReceiverSets, g.Cover.Values.Sum() + g.SpareSets);
        Assert.True(g.ReceiverSets > Defs.StartSets, "each chapter allots another set");
    }

    [Fact]
    public void ThinCoverAndRidersDelayTheTraffic()
    {
        var g = Started();
        double sent = g.Now + 7;
        double riders = g.TransitMinutes(Net.Police, sent);
        Assert.InRange(riders, Defs.RideMinutes, Defs.RiderEvery + Defs.RideMinutes);
        g.MoveSet(Net.Police, -1);
        Assert.True(g.TransitMinutes(Net.Police, sent) > riders - 1, "a thin cover catches it on a repeat");
        Assert.True(g.CoverGarble(Net.Police) > 1);
        g.MoveSet(Net.Police, -1);
        Assert.True(g.TransitMinutes(Net.Police, sent) >= Defs.NoCoverDelay);
        g.MoveSet(Net.Police, 1); g.MoveSet(Net.Police, 1); g.MoveSet(Net.Police, 1);
        Assert.True(g.CoverGarble(Net.Police) < 1, "an extra set copies cleaner");
        g.Funds = 5000;
        Assert.True(g.LayLine("denmarkhill"));
        Assert.Equal(Defs.LineMinutes, g.TransitMinutes(Net.Police, sent));
        Assert.False(g.LayLine("denmarkhill"));
    }

    [Fact]
    public void AFrequencyChangeStopsTheTrafficUntilFound()
    {
        var g = Started();
        var c = g.Contracts.First(c => c.State == ContractState.Offered && c.Net == Net.Police);
        Assert.True(g.Accept(c.Id));
        g.FreqChangeAt[Net.Police] = g.Now + 1;
        g.Advance(2);
        Assert.True(g.Searching(Net.Police));
        int spawned = c.Spawned;
        double until = g.SearchUntil[Net.Police];
        while (g.Now < until - 3) { g.Advance(10); Assert.Equal(spawned, c.Spawned); }
        g.Advance(Defs.RiderEvery + Defs.RideMinutes + 10);
        Assert.False(g.Searching(Net.Police));
        Assert.True(c.Spawned > spawned, "the missed traffic comes in once found");

        // Spare sets search faster.
        double slow = g.SearchHours(Net.Police);
        g.Funds = 5000;
        g.BuySet(); g.BuySet();
        Assert.True(g.SearchHours(Net.Police) < slow);
    }

    [Fact]
    public void DirectionFindingHalvesTheCoverStorysRisk()
    {
        var g = Started();
        g.DebugSkipToAct(4);
        g.Funds = 5000;
        int id = g.NextId;
        var plain = g.DebugIntelDilemma(Net.Red);
        double before = plain.Options[1].Suspicion;
        Assert.True(plain.Options[1].Cover);
        Assert.True(g.BuyDf(Service.Luftwaffe));
        g.NextId = id;   // the same draw again
        g.RecentScenarios.Clear();
        var d = g.DebugIntelDilemma(Net.Red);
        g.RaiseDilemma(d);
        if (before > 0) Assert.True(d.Options[1].Suspicion < before, $"{d.Options[1].Suspicion} vs {before}");
        Assert.Equal(plain.Options[0].Suspicion, d.Options[0].Suspicion);
        Assert.Contains("direction-finding", d.Context);
    }
}
