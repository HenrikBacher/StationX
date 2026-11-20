using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class JapanTests
{
    [Fact]
    public void NotBeforeTheBlackout()
    {
        var g = GameState.NewGame(7);
        g.Funds = 5000;
        Assert.False(g.JapanOpen);
        Assert.False(g.StationUnlocked(StationType.Japanese, out _));
        var linguist = TestUtil.Add(g, Role.Linguist);
        Assert.NotNull(g.CantLearnJapanese(linguist));
        Assert.False(g.LearnJapanese(linguist.Id));
        Assert.True(Campaign.JapanFrom > 0);
    }

    [Fact]
    public void TheTrafficComesInOvernight()
    {
        var g = GameState.NewGame(7);
        g.ActIndex = Campaign.JapanFrom;
        var c = g.AddScripted(Net.Jn25, 20, 6, 500, 4, "t", "t", "t");
        Assert.Equal(20, c.SpawnTimes.Count);
        foreach (double t in c.SpawnTimes)
        {
            double hour = t % Defs.MinutesPerDay / 60;
            Assert.True(hour >= Defs.JapanNightFrom || hour < Defs.JapanNightFrom + Defs.JapanNightHours - 24, $"{hour:0.0}");
            Assert.True(t > g.Now && t < c.Due);
        }
        Assert.Equal(c.SpawnTimes.OrderBy(t => t), c.SpawnTimes);
    }

    [Fact]
    public void OnlyJapaneseReadersWorkTheJapaneseDesk()
    {
        var g = GameState.NewGame(7);
        g.ActIndex = Campaign.JapanFrom;
        g.Funds = 5000;
        TestUtil.QuickIntake(g);
        g.Place(StationType.Canteen, 12, 6, 0, free: true);
        Assert.NotNull(g.Place(StationType.Registration, 4, 3, 0, free: true));
        Assert.NotNull(g.Place(StationType.HandCipher, 6, 3, 0, free: true));
        var desk = g.Place(StationType.Japanese, 9, 3, 0, free: true);
        Assert.NotNull(desk);
        Assert.NotNull(g.Place(StationType.Dispatch, 12, 3, 0, free: true));
        foreach (var s in g.Staff) s.Traits.Remove(Trait.Japanese);
        var linguist = TestUtil.Add(g, Role.Linguist);
        var c = g.AddScripted(Net.Jn25, 3, 6, 500, 4, "t", "t", "t");

        // Nobody reads Japanese: the stripped code groups wait for the Japanese desk.
        for (int i = 0; i < 5 * 48; i++)
        {
            g.Advance(30);
            g.Events.Clear();
            if (desk!.OperatorId is int op) Assert.True(g.StaffById(op)!.Has(Trait.Japanese));
        }
        Assert.Equal(0, c.Delivered);
        Assert.Contains(g.Messages.Values, m => m.Net == Net.Jn25 && m.Next == StationType.Japanese);
        Assert.Null(g.CantLearnJapanese(linguist));

        // Back from Bedford, they clear it.
        Assert.True(g.LearnJapanese(linguist.Id));
        Assert.Equal(5000 - Defs.JapaneseCourseCost, g.Funds);
        Assert.True(linguist.Has(Trait.Japanese));
        Assert.NotNull(g.CantLearnJapanese(linguist));
        TestUtil.Run(g, Defs.JapaneseCourseHours / 24 + 3);
        Assert.Equal(3, c.Delivered);
    }

    [Fact]
    public void SomeLinguistCandidatesAlreadyReadJapanese()
    {
        var g = GameState.NewGame(7);
        g.ActIndex = Campaign.JapanFrom;
        int readers = Enumerable.Range(0, 200).Count(_ => g.NewPerson(Role.Linguist, 2).Has(Trait.Japanese));
        Assert.InRange(readers, 25, 80);   // about a quarter
        Assert.DoesNotContain(Enumerable.Range(0, 50).Select(_ => g.NewPerson(Role.Clerk, 2)), p => p.Has(Trait.Japanese));
    }

    [Fact]
    public void TheBookFillsInAndANewBookSetsItBack()
    {
        var g = GameState.NewGame(7);
        g.ActIndex = Campaign.JapanFrom;
        Assert.Equal(Defs.BookStart, g.BookRecovered);
        double slow = g.BookFactor;
        for (int i = 0; i < 60; i++) g.DebugRecoverBook();
        Assert.True(g.BookRecovered > 0.7, $"{g.BookRecovered}");
        Assert.True(g.BookFactor < slow * 0.7);
        Assert.Equal("JN-25B", g.BookName);
        double before = g.BookRecovered;
        g.DebugBookChange();
        Assert.Equal("JN-25C", g.BookName);
        Assert.True(g.BookRecovered < before * 0.5);
        Assert.Contains(g.Events, e => e.Text.Contains("JN-25C"));
    }
}
