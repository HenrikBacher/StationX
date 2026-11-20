using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class QuotaTests
{
    static GameState AtFinestHour()
    {
        var g = GameState.NewGame(5);
        g.DebugSkipToAct(Campaign.IndexOf("Their Finest Hour"));
        return g;
    }

    static void Days(GameState g, double a, double b)
    {
        g.History.Clear();
        g.History.Add(new DayRecord { Day = 1, Fresh = a, Delivered = (int)a });
        g.History.Add(new DayRecord { Day = 2, Fresh = b, Delivered = (int)b });
    }

    [Fact]
    public void ShortfallSetsTheDailySlip()
    {
        var g = AtFinestHour();
        Assert.True(g.QuotaActive);
        int q = g.IntelQuota;
        Assert.InRange(q, 4, 40);
        Days(g, q, q);
        Assert.Equal(Defs.QuotaFloor, g.ExpectationMult, 3);
        Days(g, q / 2.0, q / 2.0);
        Assert.Equal(Defs.QuotaSlope / 2, g.ExpectationMult, 3);
        Days(g, 0, 0);
        Assert.Equal(Defs.QuotaMaxMult, g.ExpectationMult, 3);
        // An older save's days, recorded before the quota was counted, count what they delivered.
        g.History.Clear();
        g.History.Add(new DayRecord { Day = 1, Delivered = q });
        Assert.Equal(Defs.QuotaFloor, g.ExpectationMult, 3);
    }

    [Fact]
    public void BeforeTheBattleOfBritainTheSlipIsFlat()
    {
        var g = GameState.NewGame(5);
        g.DebugSkipToAct(Defs.QuotaFromAct - 1);
        Assert.False(g.QuotaActive);
        Assert.Equal(1, g.ExpectationMult);
    }

    [Fact]
    public void StaleNewsCountsForLess()
    {
        var g = AtFinestHour();
        var fresh = new Message { Net = Net.Red, Created = g.Now };
        var stale = new Message { Net = Net.Red, Created = g.Now - Defs.StaleAfter(Net.Red) - 1 };
        var dead = new Message { Net = Net.Red, Created = g.Now - Defs.DeadAfter(Net.Red) - 1 };
        Assert.Equal(1, g.QuotaValue(fresh));
        Assert.Equal(Defs.QuotaStale, g.QuotaValue(stale));
        Assert.Equal(Defs.QuotaDead, g.QuotaValue(dead));
    }
}

public class VisitHeadcountTests
{
    [Fact]
    public void VisitorsMarkDownAWatchStandingIdle()
    {
        var g = GameState.NewGame(5);
        foreach (var s in g.Staff) { s.Away = false; s.ArrivesAt = 0; s.Task = null; }
        Assert.Equal(1, g.IdleShare());
        var idle = g.InspectionVerdict();
        Assert.Contains(idle.notes, n => n.Contains("standing about"));
        foreach (var s in g.Staff) s.Task = new StaffTask { Kind = TaskKind.Carry };
        Assert.Equal(0, g.IdleShare());
        var busy = g.InspectionVerdict();
        Assert.Contains("everyone hard at work", busy.notes);
        Assert.Equal(idle.score + 2, busy.score);
    }
}

public class MissedRepTests
{
    [Fact]
    public void ARequestNearlyDoneCostsLessThanOneBarelyStarted()
    {
        var c = new Contract { Quantity = 10, Rep = 3 };
        c.Delivered = 0;
        Assert.Equal(7, GameState.MissedRep(c), 3);
        c.Delivered = 9;
        double nearly = GameState.MissedRep(c);
        Assert.InRange(nearly, Defs.MissedRepBase, 7 / 2.0);
    }
}
