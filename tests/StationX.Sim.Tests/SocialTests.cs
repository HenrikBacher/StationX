using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class SocialTests
{
    static GameState Open()
    {
        var g = GameState.NewGame(7);
        g.Funds = 5000;
        g.ActIndex = Defs.SocialFromAct;
        return g;
    }

    [Fact]
    public void NotBeforeTheWarGetsGoing()
    {
        var g = GameState.NewGame(7);
        g.Funds = 5000;
        Assert.False(g.SocialOpen);
        Assert.False(g.HoldSocial());
    }

    [Fact]
    public void TheEveningLiftsSpiritsAtHalfPastSeven()
    {
        var g = Open();
        foreach (var s in g.Staff) s.Morale = 0.3;
        Assert.True(g.HoldSocial());
        Assert.Equal(5000 - Defs.SocialCost, g.Funds);
        Assert.True(g.SocialPending);
        Assert.Equal(Defs.SocialHour * 60, g.SocialAt % Defs.MinutesPerDay, 6);
        Assert.True(g.SocialAt >= g.Now + 120);
        Assert.NotNull(g.CantHoldSocial());   // already arranged
        string first = g.NextSocial.name;

        g.Advance(g.SocialAt - g.Now - 1);
        Assert.True(g.SocialPending);
        double before = g.Staff.Average(s => s.Morale);
        g.Advance(2);
        Assert.False(g.SocialPending);
        Assert.True(g.Staff.Average(s => s.Morale) > before + Defs.SocialMorale * 0.9);
        Assert.Equal(1, g.Socials);
        Assert.NotEqual(first, g.NextSocial.name);   // the club puts on something different next time
        Assert.Contains(g.Events, e => e.Kind == EventKind.Good && e.Text.Contains("Spirits are up"));
    }

    [Fact]
    public void OnlyOnceEveryFewDays()
    {
        var g = Open();
        Assert.True(g.HoldSocial());
        TestUtil.Run(g, 2);
        Assert.False(g.SocialPending);
        Assert.Contains("days", g.CantHoldSocial());
        TestUtil.Run(g, Defs.SocialEveryDays);
        Assert.Null(g.CantHoldSocial());
    }

    [Fact]
    public void QuarrelsCanBeMadeUp()
    {
        int mended = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            var g = GameState.NewGame(seed);
            g.Funds = 5000;
            g.ActIndex = Defs.SocialFromAct;
            var (a, b) = (g.Staff[0], g.Staff[1]);
            a.Rival = b.Id; b.Rival = a.Id;
            a.RivalUntilDay = b.RivalUntilDay = g.Day + 30;
            g.HoldSocial();
            g.Advance(g.SocialAt - g.Now + 1);
            if (a.RivalUntilDay <= g.Day) { mended++; Assert.Equal(a.RivalUntilDay, b.RivalUntilDay); }
        }
        Assert.InRange(mended, 2, 10);   // about half
    }
}
