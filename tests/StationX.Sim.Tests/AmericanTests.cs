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

public class FluTests
{
    [Fact]
    public void AWaveIsWarnedOfAndPrecautionsHalveIt()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(2);
        g.Dilemmas.Clear();
        g.DebugFlu();
        var d = g.Dilemmas.Single(x => x.Tag == "flu");
        Assert.True(g.FluComing);
        Assert.Equal(1, g.FluMult);
        int funds = g.Funds;
        Assert.True(g.ResolveDilemma(d.Id, 1));
        Assert.Equal(funds - Defs.FluPrecautionCost, g.Funds);
        g.Now = g.FluFrom + 60;
        Assert.True(g.FluWave);
        Assert.Equal(Defs.FluWaveMult * Defs.FluPrecautionShare, g.FluMult, 6);
        g.Now = g.FluUntil + 60;
        Assert.False(g.FluWave);
        Assert.Equal(1, g.FluMult);
    }
}

public class FirstsTests
{
    [Fact]
    public void TheFirstKeyBreakIsRecordedForTheEndSheet()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        g.AddScripted(Net.Red, 4, 4, 0, 0, "t", "t", "t");
        TestUtil.Run(g, 1.5);
        Assert.True(g.Stats.Firsts.ContainsKey("Enigma"));
        int day = g.Stats.Firsts["Enigma"];
        Assert.Contains(g.Stats.Firsts, kv => kv.Key != "Enigma" && kv.Value == day);
        Assert.All(Defs.HistoryFirsts, h => Assert.True(h.key == "Enigma" || h.key == "Colossus" || System.Enum.TryParse<Net>(h.key, out _), h.key));
    }
}

public class CustomRulesTests
{
    [Fact]
    public void CustomRulesLayOverTheDifficulty()
    {
        var plain = GameState.NewGame(3, Difficulty.Normal, custom: new CustomRules());
        Assert.Null(plain.Custom);
        var g = GameState.NewGame(3, Difficulty.Normal, custom: new CustomRules { Funds = 2, Patience = 0.5, Deadlines = 1 });
        var d = Defs.Difficulties[Difficulty.Normal];
        Assert.Equal(d.StartFunds * 2, g.Funds);
        Assert.Equal(d.RepLossMult * 0.5, g.Rules.RepLossMult, 6);
        Assert.Equal(d.ExtraDays + 1, g.Rules.ExtraDays, 6);
        double rep = g.Reputation;
        g.AdjustRep(-10);
        Assert.Equal(rep - 10 * d.RepLossMult * 0.5, g.Reputation, 6);
        var back = SaveGame.Load(SaveGame.Save(g));
        Assert.NotNull(back.Custom);
        Assert.Equal(g.Rules.RepLossMult, back.Rules.RepLossMult, 6);
        // A later start always counts as custom.
        Assert.NotNull(GameState.NewGame(3, Difficulty.Normal, GameMode.Campaign, 5, new CustomRules()).Custom);
    }
}

public class NotableStoryTests
{
    static Staff Notable(GameState g, string name)
    {
        var p = TestUtil.Add(g, Role.Cryptanalyst);
        p.Name = p.Notable = name;
        return p;
    }

    [Fact]
    public void WelchmansSystemSlowsThenSpeedsHisBuilding()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var w = Notable(g, "Gordon Welchman");
        w.Posted = "hut6";
        var crib = g.Stations.First(s => s.Type == StationType.Crib);
        var op = g.Staff.First(s => s.Role == Role.Cryptanalyst && s != w);
        var m = new Message { Net = Net.Red, Day = g.Day };
        double before = g.Duration(crib, op, m);
        g.Dilemmas.Clear();
        Assert.True(g.DebugStory("welchman"));
        var d = g.Dilemmas.Single(x => x.Tag == "welchman");
        Assert.True(g.ResolveDilemma(d.Id, 1));
        Assert.Equal(before * Defs.WelchmanUpheaval, g.Duration(crib, op, m), 6);
        g.Now += (Defs.WelchmanUpheavalDays + 0.1) * Defs.MinutesPerDay;
        Assert.Equal(before * Defs.WelchmanFaster, g.Duration(crib, op, m), 6);
    }

    [Fact]
    public void KnoxFromCournsWoodAndTuringInAmerica()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(Campaign.EnigmaFinale);
        var k = Notable(g, "Dilly Knox");
        g.Dilemmas.Clear();
        Assert.True(g.DebugStory("knox"));
        Assert.True(g.ResolveDilemma(g.Dilemmas.Single(x => x.Tag == "knox").Id, 1));
        Assert.True(g.KnoxAtHome);
        Assert.True(k.IsSick(g.Now));
        var t = Notable(g, "Alan Turing");
        Assert.True(g.DebugStory("turing-us"));
        Assert.True(g.ResolveDilemma(g.Dilemmas.Single(x => x.Tag == "turing-us").Id, 1));
        Assert.True(t.IsSick(g.Now));
        g.Brusa = 1;
        g.BrusaDay = g.Day;
        g.Researched.Add(ResearchId.FourRotorBombe);
        Assert.Equal(Defs.DaytonStart + 1, g.DaytonBombes);
        // Knox dies in 1943.
        g.DebugSkipToAct(Campaign.IndexOf("Colossus"));
        g.Advance(Defs.MinutesPerDay);
        Assert.DoesNotContain(g.Staff, s => s.Notable == "Dilly Knox" && !s.Leaving);
    }
}

public class CountermeasureTests
{
    [Fact]
    public void ACountermeasureSlowsItsStepUntilAnswered()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var crib = g.Stations.First(s => s.Type == StationType.Crib);
        var bombe = g.Stations.First(s => s.Type == StationType.Bombe);
        var op = g.Staff.First(s => s.Role == Role.Cryptanalyst);
        var wren = g.Staff.First(s => s.Role == Role.Wren);
        var red = new Message { Net = Net.Red, Day = g.Day };
        double c0 = g.Duration(crib, op, red), b0 = g.Duration(bombe, wren, red);
        g.DebugCounter("uhr");
        Assert.Contains(ResearchId.UhrBox, g.ResearchAvailable);
        Assert.Equal(c0 * 1.7, g.Duration(crib, op, red), 6);
        Assert.Equal(b0, g.Duration(bombe, wren, red), 6);
        Assert.Single(g.CountersOn(Net.Red));
        Assert.Empty(g.CountersOn(Net.Dolphin));   // naval: untouched
        g.DebugCounter("ukwd");
        Assert.Equal(b0 * 1.8, g.Duration(bombe, wren, red), 6);
        g.Points = 1000;
        Assert.True(g.DoResearch(ResearchId.UhrBox));
        Assert.Equal(c0, g.Duration(crib, op, red), 6);
        Assert.Single(g.CountersOn(Net.Red));
    }

    [Fact]
    public void TheyComeWithTheirChapters()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(Campaign.IndexOf("D-Day"));
        Assert.Empty(g.Counters);
        for (int i = 0; i < 3; i++) { g.Advance(Defs.MinutesPerDay); g.Reputation = 100; g.Dilemmas.Clear(); g.Events.Clear(); }
        Assert.Contains("ukwd", g.Counters);
        Assert.DoesNotContain("sz42", g.Counters);
    }
}

public class BackTrafficTests
{
    [Fact]
    public void TheIdleWorkOffOldTraffic()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        g.BackTraffic = 20;
        int points = g.Points;
        TestUtil.Run(g, 0.5);
        Assert.True(g.BackTraffic < 20, $"still {g.BackTraffic}");
        Assert.True(g.Points > points);
        Assert.True(g.Stats.BackRead > 0);
    }

    [Fact]
    public void UnbrokenKeysLeaveTrafficUnread()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        int before = g.BackTraffic;
        g.Advance(Defs.MinutesPerDay * 2);
        Assert.True(g.BackTraffic + g.Stats.BackRead > before);
    }
}

public class SluTests
{
    [Fact]
    public void AUnitKeepsItsTheatresNewsFreshAndComesHome()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(Campaign.KissesFrom);
        g.Funds = 5000;
        double red = g.StaleAfter(Net.Red), dolphin = g.StaleAfter(Net.Dolphin), susp = g.SuspicionPerDecrypt(Net.Red);
        int upkeep = g.WeeklyUpkeep;
        Assert.Null(g.CantSendSlu("cairo"));
        Assert.NotNull(g.CantSendSlu("normandy"));
        Assert.True(g.SendSlu("cairo"));
        Assert.Equal(5000 - 300, g.Funds);
        Assert.Equal(red * Defs.SluFresh, g.StaleAfter(Net.Red), 6);
        Assert.Equal(dolphin, g.StaleAfter(Net.Dolphin), 6);
        Assert.Equal(susp * Defs.SluSuspicion, g.SuspicionPerDecrypt(Net.Red), 6);
        Assert.Equal(upkeep + 30, g.WeeklyUpkeep);
        Assert.False(g.SendSlu("cairo"));
        g.DebugSkipToAct(Campaign.IndexOf("Colossus"));
        g.Advance(Defs.MinutesPerDay);
        Assert.Empty(g.Slus);
        Assert.Equal(red, g.StaleAfter(Net.Red), 6);
    }
}

public class MeanwhileTests
{
    [Fact]
    public void ALongGapAsksWhereTheMonthsWent()
    {
        var g = GameState.NewGame(3);
        g.Dilemmas.Clear();
        g.DebugMeanwhile(new DateOnly(1941, 1, 1), new DateOnly(1941, 2, 1));
        Assert.Empty(g.Dilemmas);
        g.AddSuspicion(Service.Heer, 30);
        g.DebugMeanwhile(new DateOnly(1942, 2, 20), new DateOnly(1942, 10, 30));
        var d = g.Dilemmas.Single(x => x.Tag == "meanwhile");
        Assert.Contains("Midway", d.Text);
        Assert.Equal(3, d.Options.Count);
        double before = g.SuspicionOf(Service.Heer);
        Assert.True(g.ResolveDilemma(d.Id, 2));
        Assert.Equal(before - Defs.MeanwhileQuiet * Defs.MeanwhileMaxMonths, g.SuspicionOf(Service.Heer), 6);
    }
}

public class CribChoiceTests
{
    [Fact]
    public void AStubbornKeyOffersCribs()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        g.ActiveNets.Add(Net.Red);
        g.ActiveNets.Add(Net.LightBlue);
        Assert.False(g.CribChoiceOpen(Net.Red));
        g.StaleUntil[Net.Red] = g.Day;
        Assert.True(g.CribChoiceOpen(Net.Red));
        Assert.NotNull(g.CantUseCrib(Net.Red, CribKind.Reencoded));   // no other key broken yet
        Assert.Null(g.CantUseCrib(Net.Red, CribKind.Weather));         // the Luftwaffe sends weather
        Assert.NotNull(g.CantUseCrib(Net.Police, CribKind.Weather));

        var crib = g.Stations.First(s => s.Type == StationType.Crib);
        var bombe = g.Stations.First(s => s.Type == StationType.Bombe);
        var op = g.Staff.First(s => s.Role == Role.Cryptanalyst);
        var wren = g.Staff.First(s => s.Role == Role.Wren);
        var m = new Message { Net = Net.Red, Day = g.Day };
        double cStale = g.Duration(crib, op, m), bStale = g.Duration(bombe, wren, m);

        // The weather report shrugs off the change of habits, waiting a little for the morning.
        Assert.True(g.SetCrib(Net.Red, CribKind.Weather));
        Assert.Equal(cStale / Defs.StaleCribMult * Defs.CribWeatherTime, g.Duration(crib, op, m), 6);
        Assert.Equal(bStale / Defs.StaleBombeMult, g.Duration(bombe, wren, m), 6);

        // "Nothing to report" is quick but keeps the stale penalty.
        Assert.True(g.SetCrib(Net.Red, CribKind.Routine));
        Assert.Equal(cStale * Defs.CribRoutineTime, g.Duration(crib, op, m), 6);

        // A re-encodement once another key is broken today.
        g.Keys[GameState.KeyId(Net.LightBlue, g.Day)] = KeyStatus.Broken;
        Assert.True(g.SetCrib(Net.Red, CribKind.Reencoded));
        Assert.Equal(bStale / Defs.StaleBombeMult * Defs.CribReencodedBombe, g.Duration(bombe, wren, m), 6);
        Assert.Equal(CribKind.Desk, g.NextCrib(Net.Red, CribKind.Reencoded));

        // Broken: no more choosing.
        g.Keys[GameState.KeyId(Net.Red, g.Day)] = KeyStatus.Broken;
        Assert.False(g.CribChoiceOpen(Net.Red));
    }
}
