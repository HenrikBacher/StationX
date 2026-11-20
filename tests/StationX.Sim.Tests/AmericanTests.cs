using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class AmericanTests
{
    [Fact]
    public void BrusaSharesSharkAndSendsTheDetachment()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(Campaign.IndexOf("The Testery"));
        g.Researched.Add(ResearchId.FourRotorBombe);
        g.Dilemmas.Clear();
        g.DebugBrusa();
        var d = Assert.Single(g.Dilemmas, x => x.Tag == "brusa");
        Assert.Equal(3, d.Options.Count);
        Assert.Equal(0, g.DaytonBombes);
        Assert.True(g.ResolveDilemma(d.Id, 1));
        Assert.Equal(2, g.Brusa);
        Assert.Equal(Defs.DaytonStart, g.DaytonBombes);
        var us = g.Candidates.Where(c => c.American).ToList();
        Assert.Equal(2, us.Count);
        Assert.All(us, c => Assert.Equal(0, c.Salary));
        Assert.Equal("William Bundy", us[0].Name);
        // The weekly intake keeps them, and Dayton grows month by month.
        g.DebugRefreshCandidates();
        Assert.Equal(2, g.Candidates.Count(c => c.American));
        g.BrusaDay -= Defs.DaytonGrowDays * 10;
        Assert.Equal(Defs.DaytonMax, g.DaytonBombes);
    }

    [Fact]
    public void DaytonGivesSharkABombeRouteThroughTheOutstationLink()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(Campaign.IndexOf("The Testery"));
        foreach (var r in new[] { ResearchId.Bombe, ResearchId.ShortWeatherCipher, ResearchId.FourRotorBombe }) g.Researched.Add(r);
        g.Place(StationType.Crib, 14, 4, 0, free: true);
        Assert.NotNull(g.Stations.FirstOrDefault(s => s.Type == StationType.Crib));
        Assert.NotNull(g.Place(StationType.OutstationLink, 17, 7, 0, free: true));
        Assert.Null(g.KeyRoute(Net.Shark));   // no four-rotor Bombe of our own, nor an outstation
        g.Brusa = 1;
        g.BrusaDay = g.Day;
        Assert.Equal(StationType.Crib, g.KeyRoute(Net.Shark));
    }

    [Fact]
    public void KeepingSharkAtTheParkLeavesWashingtonOut()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(Campaign.IndexOf("The Testery"));
        g.Researched.Add(ResearchId.FourRotorBombe);
        g.Dilemmas.Clear();
        g.DebugBrusa();
        var d = g.Dilemmas.Single(x => x.Tag == "brusa");
        int pts = g.Points;
        Assert.True(g.ResolveDilemma(d.Id, 2));
        Assert.Equal(pts + Defs.BrusaPoints, g.Points);
        for (int i = 0; i < 3; i++) { g.Advance(Defs.MinutesPerDay); g.Reputation = 100; g.Events.Clear(); }
        Assert.Equal(-1, g.Brusa);
        Assert.Equal(0, g.DaytonBombes);
        Assert.DoesNotContain(g.Candidates, c => c.American);
        Assert.DoesNotContain(g.Dilemmas, x => x.Tag == "brusa");
    }
}

public class UnclesTests
{
    [Fact]
    public void LowReputationBeforeActionThisDayBringsTheLetterEarly()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(Campaign.IndexOf("U-110"));
        g.Dilemmas.Clear();
        g.Reputation = Defs.UnclesAt + 5;
        g.Advance(10);
        Assert.DoesNotContain(g.Dilemmas, d => d.Tag == "uncles");
        g.Reputation = Defs.UnclesAt - 5;
        g.Advance(10);
        var d = Assert.Single(g.Dilemmas, x => x.Tag == "uncles");
        int wrens = g.Candidates.Count(c => c.Role == Role.Wren);
        int funds = g.Funds;
        Assert.True(g.ResolveDilemma(d.Id, 1));
        Assert.True(g.UnclesWrote);
        Assert.Equal(funds + Defs.UnclesFunds, g.Funds);
        Assert.Equal(wrens + Defs.UnclesWrens, g.Candidates.Count(c => c.Role == Role.Wren));
        // Once a game.
        g.Reputation = 10;
        g.Advance(60);
        Assert.DoesNotContain(g.Dilemmas, x => x.Tag == "uncles");
        // 'Action this day' brings less.
        funds = g.Funds;
        g.DebugSkipToAct(Campaign.ActionThisDay);
        Assert.InRange(g.Funds - funds, Defs.UnclesLaterFunds - 200, Defs.UnclesLaterFunds + 200);
    }

    [Fact]
    public void NotBeforeClaymoreNorAfterActionThisDay()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(Campaign.IndexOf("Their Finest Hour"));
        g.Dilemmas.Clear();
        g.Reputation = 10;
        g.Advance(10);
        Assert.DoesNotContain(g.Dilemmas, d => d.Tag == "uncles");
        g.DebugSkipToAct(Campaign.ActionThisDay);
        g.Dilemmas.Clear();
        g.Reputation = 10;
        g.Advance(10);
        Assert.DoesNotContain(g.Dilemmas, d => d.Tag == "uncles");
    }
}
