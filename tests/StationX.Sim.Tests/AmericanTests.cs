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

public class DoubleCrossTests
{
    [Fact]
    public void AbwehrDecryptsKeepTheAgentsBelievedAndAddToDDay()
    {
        var g = GameState.NewGame(3);
        Assert.False(g.DoubleCrossOn);
        g.DebugSkipToAct(Campaign.IndexOf("Colossus"));
        Assert.Equal(5, g.Agents.Count);
        Assert.All(g.Agents, a => Assert.Equal(Defs.XxStartTrust, a.Trust, 6));
        // Each decrypt goes to the least sure.
        g.Agents[2].Trust = 0.3;
        g.DebugReadAbwehr();
        Assert.Equal(0.3 + Defs.XxPerDecrypt, g.Agents[2].Trust, 6);
        // Unread, they drift and in the end may be blown.
        for (int i = 0; i < 30; i++) { g.Advance(Defs.MinutesPerDay); g.Reputation = 100; g.Dilemmas.Clear(); g.Events.Clear(); if (g.ActCompletePending) break; }
        Assert.Contains(g.Agents, a => a.Blown || a.Trust < Defs.XxStartTrust - 0.2);
        // On D-Day's chapter what's believed counts.
        g.DebugSkipToAct(Campaign.IndexOf("D-Day"));
        foreach (var a in g.Agents) { a.Blown = false; a.Trust = 1; }
        Assert.Equal(5 * Defs.XxReadiness, g.FortitudeBonus, 6);
    }

    [Fact]
    public void ArtistAsksAboutTricycle()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(Campaign.IndexOf("D-Day"));
        g.Dilemmas.Clear();
        g.DebugArtist();
        var d = Assert.Single(g.Dilemmas, x => x.Tag == "artist");
        Assert.True(g.ResolveDilemma(d.Id, 0));
        var t = g.Agents.Single(a => a.Name == "TRICYCLE");
        Assert.True(t.Dropped);
        Assert.False(t.Active);
    }
}

public class MoleTests
{
    static GameState Blackout()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(Campaign.SharkFrom);
        g.Dilemmas.Clear();
        for (int i = 0; i < 4; i++) TestUtil.Add(g, Role.Linguist);
        return g;
    }

    [Fact]
    public void ALeakRaisesSuspicionUntilMI5BringsNames()
    {
        var g = Blackout();
        double air = g.SuspicionOf(Service.Luftwaffe);
        Dilemma? d = null;
        for (int i = 0; i < 20 && d == null; i++)
        {
            g.Advance(Defs.MinutesPerDay);
            g.Reputation = 100;
            g.Events.Clear();
            g.Dilemmas.RemoveAll(x => x.Tag != "mole");
            d = g.Dilemmas.Find(x => x.Tag == "mole");
        }
        Assert.NotNull(d);
        Assert.Equal(1, g.MoleState);
        Assert.True(g.Stats.MoleLeakDays >= Defs.MoleNoticeDays);
        Assert.Equal(4, d!.Options.Count);
        Assert.StartsWith("mole-move:", d.Options[1].Action);
        Assert.Contains(d.Options, o => o.Action == $"mole:{g.MoleId}");
    }

    [Fact]
    public void TheRightNameStopsItAndTheWrongOneDoesNot()
    {
        var g = Blackout();
        Assert.True(g.DebugMole());
        var d = g.Dilemmas.Single(x => x.Tag == "mole");
        int wrong = d.Options.FindIndex(o => o.Action!.StartsWith("mole:") && o.Action != $"mole:{g.MoleId}");
        double rep = g.Reputation;
        Assert.True(g.ResolveDilemma(d.Id, wrong));
        Assert.Equal(1, g.MoleState);
        Assert.True(g.Reputation < rep);
        Assert.True(g.DebugMole());
        d = g.Dilemmas.Single(x => x.Tag == "mole");
        Assert.True(g.ResolveDilemma(d.Id, d.Options.FindIndex(o => o.Action == $"mole:{g.MoleId}")));
        Assert.Equal(2, g.MoleState);
        Assert.True(g.Stats.MoleCaught);
    }

    [Fact]
    public void MovingAllThreeContainsIt()
    {
        var g = Blackout();
        Assert.True(g.DebugMole());
        var d = g.Dilemmas.Single(x => x.Tag == "mole");
        Assert.True(g.ResolveDilemma(d.Id, 1));
        Assert.Equal(3, g.MoleState);
    }
}
