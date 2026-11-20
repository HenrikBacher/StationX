using System;
using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

/// <summary>Kisses: WERFT read on a hand-cipher desk gives the day's naval keys a perfect crib.</summary>
public class KissTests
{
    /// <summary>Operation Claymore, past the pinched keys, with a Mansion that can register and read hand ciphers.</summary>
    static GameState Claymore(int seed = 21)
    {
        var g = GameState.NewGame(seed);
        g.DebugSkipToAct(Campaign.KissesFrom);
        g.FreeKeysUntil.Clear();
        TestUtil.QuickIntake(g);
        g.Place(StationType.Registration, 5, 3, 0, free: true);
        g.Place(StationType.HandCipher, 8, 3, 0, free: true);
        g.Place(StationType.Canteen, 12, 6, 0, free: true);
        foreach (var r in new[] { Role.Clerk, Role.Clerk, Role.Cryptanalyst }) TestUtil.Add(g, r);
        g.Contracts.Clear();
        g.Messages.Clear();
        return g;
    }

    /// <summary>Runs on to the given hour of the given day.</summary>
    static void Until(GameState g, int day, double hour)
    {
        double target = day * Defs.MinutesPerDay + hour * 60;
        while (g.Now < target) { g.Advance(30); g.Events.Clear(); }
    }

    [Fact]
    public void OpensWithTheNavalKeys()
    {
        var g = GameState.NewGame(3);
        Assert.False(g.KissesOpen);
        g.DebugSkipToAct(Campaign.KissesFrom - 1);
        Assert.False(g.KissesOpen);
        g.DebugSkipToAct(Campaign.KissesFrom);
        Assert.True(g.KissesOpen);
        Assert.Contains(Net.Dolphin, g.KissTargets());
        Assert.DoesNotContain(Net.Werft, g.KeyBoardNets());   // a hand cipher: never on the key board itself
        Assert.Equal("Needs a hand-cipher desk", g.WerftProblem());
    }

    [Fact]
    public void NothingComesUnlessThePlayerAsksForIt()
    {
        var g = Claymore();
        Until(g, g.Day + 1, 14);
        Assert.DoesNotContain(g.Messages.Values, m => m.Net == Net.Werft);
        Assert.Empty(g.KissedKeys);
    }

    [Fact]
    public void ReadingWerftKissesTheDaysNavalKey()
    {
        var g = Claymore();
        g.SetReadWerft(true);
        int day = g.Day + 1;
        Until(g, day, 1);
        Assert.False(g.Kissed(Net.Dolphin, day));
        Until(g, day, 16);   // sent at 05:00-09:00, in by teleprinter, registered and read
        Assert.True(g.Kissed(Net.Dolphin, day), "today's DOLPHIN key is kissed");
        Assert.True(g.Stats.Kisses >= 1);
        Assert.Equal(1, g.KissesToday);
        // Read once is enough: the day's other signal isn't worth a cryptanalyst's time, so nothing is left lying about.
        Assert.DoesNotContain(g.Messages.Values, m => m.Net == Net.Werft && m.Loc != Loc.Processing);
        Assert.Contains(g.Archive, a => a.Net == Net.Werft);
        // A kiss is for its own day only.
        Assert.False(g.Kissed(Net.Dolphin, day + 1));
    }

    [Fact]
    public void WerftForAKeyAlreadyBrokenIsFiledUnread()
    {
        var g = Claymore();
        g.SetReadWerft(true);
        int day = g.Day + 1;
        Until(g, day, 1);
        g.Keys[GameState.KeyId(Net.Dolphin, day)] = KeyStatus.Broken;
        var hand = g.Stations.First(s => s.Type == StationType.HandCipher);
        int before = hand.Processed, kisses = g.Stats.Kisses;
        Until(g, day, 16);
        Assert.Equal(before, hand.Processed);
        Assert.False(g.Kissed(Net.Dolphin, g.Day));
        Assert.Equal(kisses, g.Stats.Kisses);
        Assert.DoesNotContain(g.Messages.Values, m => m.Net == Net.Werft);
    }

    [Fact]
    public void TurningItOffFilesWhatIsWaiting()
    {
        var g = Claymore();
        g.SetReadWerft(true);
        // Stop the reading: nobody at the hand-cipher desk.
        g.Staff.RemoveAll(p => p.Role == Role.Cryptanalyst);
        Until(g, g.Day + 1, 12);
        Assert.Contains(g.Messages.Values, m => m.Net == Net.Werft);
        g.SetReadWerft(false);
        g.Advance(5);
        Assert.DoesNotContain(g.Messages.Values, m => m.Net == Net.Werft);
    }

    [Fact]
    public void AKissedKeyIsQuickerToCribAndToRun()
    {
        var g = Claymore();
        g.Researched.Add(ResearchId.Bombe);
        g.Grid.Zone("hut6")!.Unlocked = true;
        g.Grid.Zone("hut11")!.Unlocked = true;
        var crib = g.Place(StationType.Crib, 21, 5, 0, free: true)!;
        var bombe = g.Place(StationType.Bombe, 36, 3, 0, free: true)!;
        Assert.NotNull(crib);
        Assert.NotNull(bombe);
        var crypt = TestUtil.Add(g, Role.Cryptanalyst);
        var wren = TestUtil.Add(g, Role.Wren);
        var m = new Message { Id = 999, Net = Net.Dolphin, Day = g.Day, Carrier = true };
        double plainCrib = g.Duration(crib, crypt, m), plainBombe = g.Duration(bombe, wren, m);
        g.KissedKeys.Add(GameState.KeyId(Net.Dolphin, g.Day));
        Assert.Equal(plainCrib * Defs.KissCribMult, g.Duration(crib, crypt, m), 3);
        Assert.Equal(plainBombe * Defs.KissBombeMult, g.Duration(bombe, wren, m), 3);
        // Not the next day's key.
        var tomorrow = new Message { Id = 1000, Net = Net.Dolphin, Day = g.Day + 1, Carrier = true };
        Assert.Equal(plainCrib, g.Duration(crib, crypt, tomorrow), 3);
        // A kiss beats a stale crib.
        g.StaleUntil[Net.Dolphin] = g.Day + 2;
        Assert.Equal(plainCrib * Defs.KissCribMult, g.Duration(crib, crypt, m), 3);
    }

    [Fact]
    public void AKissCutsShortAnAttemptAlreadyUnderWay()
    {
        var g = Claymore();
        g.Researched.Add(ResearchId.Bombe);
        g.Grid.Zone("hut11")!.Unlocked = true;
        var bombe = g.Place(StationType.Bombe, 36, 3, 0, free: true)!;
        var carrier = new Message { Id = g.NextId++, Net = Net.Dolphin, Day = g.Day, Carrier = true, Loc = Loc.Processing, LocId = bombe.Id, Next = null, CribQuality = 0.7 };
        g.Messages[carrier.Id] = carrier;
        bombe.Current = carrier.Id;
        bombe.WorkDone = 100;
        bombe.WorkTotal = 300;
        var werft = new Message { Id = g.NextId++, Net = Net.Werft, Day = g.Day, Created = g.Now };
        g.Messages[werft.Id] = werft;
        g.DebugKiss(werft);
        Assert.True(g.Kissed(Net.Dolphin, g.Day));
        Assert.Equal(100 + 200 * Defs.KissBombeMult, bombe.WorkTotal, 3);
        Assert.Equal(Defs.KissCribQuality, carrier.CribQuality);
        Assert.False(g.Messages.ContainsKey(werft.Id), "the WERFT signal leaves the floor once read");
        // A second reading the same day adds nothing.
        var again = new Message { Id = g.NextId++, Net = Net.Werft, Day = g.Day, Created = g.Now };
        g.Messages[again.Id] = again;
        g.DebugKiss(again);
        Assert.Equal(1, g.Stats.Kisses);
        Assert.Equal(100 + 200 * Defs.KissBombeMult, bombe.WorkTotal, 3);
    }

    [Fact]
    public void TheWerftSignalsGoOutInTheMorningOnTheirOwnDice()
    {
        var g = Claymore();
        var a = g.WerftTimes(40);
        Assert.Equal(Defs.WerftPerDay, a.Length);
        Assert.Equal(a, g.WerftTimes(40));
        foreach (var t in a)
            Assert.InRange(t - 40 * Defs.MinutesPerDay, Defs.WerftFromHour * 60, (Defs.WerftFromHour + Defs.WerftSpreadHours) * 60);
        // WERFT shares the naval sets' cover at Scarborough.
        Assert.Equal(GameState.YStationOf(Net.Dolphin), GameState.YStationOf(Net.Werft));
        Assert.Equal(g.CoverOf(Net.Dolphin), g.CoverOf(Net.Werft));
    }

    [Fact]
    public void KissesSurviveASave()
    {
        var g = Claymore();
        g.SetReadWerft(true);
        g.KissedKeys.Add(GameState.KeyId(Net.Dolphin, g.Day));
        var h = SaveGame.Load(SaveGame.Save(g));
        Assert.True(h.ReadWerft);
        Assert.True(h.Kissed(Net.Dolphin, g.Day));
    }

    [Fact]
    public void WerftInterceptsReadCleanly()
    {
        var g = GameState.NewGame(4);
        for (int i = 0; i < 200; i++)
        {
            var x = Intercepts.For(i, 100 + i, Net.Werft, 3, new DateOnly(1941, 3, 1).AddDays(i * 5), 6 * 60 + i, false);
            foreach (var s in new[] { x.German, x.English, x.Cipher })
            {
                Assert.False(string.IsNullOrWhiteSpace(s));
                Assert.DoesNotContain("{", s);
                Assert.DoesNotContain("}", s);
            }
            Assert.Contains("SCARBOROUGH", x.Station);
        }
    }
}
