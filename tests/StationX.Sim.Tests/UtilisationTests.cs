using System.Text.RegularExpressions;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class UtilisationTests
{
    static string Play(int seed, bool track, int days)
    {
        var g = GameState.NewGame(seed);
        g.TrackUse = track;
        var ai = new AutoPlayer(g);
        while (g.Status == GameStatus.Playing && g.Day < days)
        {
            if (g.ActCompletePending) g.ContinueAfterAct();
            ai.Think();
            g.Advance(60);
            g.Events.Clear();
            // The view asks for the report (panel, overlay, tips); asking must change nothing either.
            if (track) { g.Utilisation(); foreach (var p in g.Staff) g.PersonState(p); }
        }
        if (track) Assert.True(g.Stations.Any(s => s.Use != null) && g.Staff.Any(s => s.Use != null));
        // Everything but the wall-clock time of the save.
        return Regex.Replace(SaveGame.Save(g), "\"SavedAt\":\"[^\"]*\"", "");
    }

    [Fact]
    public void TrackingLeavesTheGameUnchanged()
    {
        var off = Play(11, false, 70);
        var on = Play(11, true, 70);
        Assert.Equal(off.Length, on.Length);
        Assert.True(off == on, "the bookkeeping changed the course of the game");
    }

    [Fact]
    public void DeskTimeIsAccountedForAndRollsOver()
    {
        var g = GameState.NewGame(7);
        TestUtil.QuickIntake(g);
        g.Place(StationType.Registration, 5, 3, 0);
        g.Place(StationType.HandCipher, 8, 3, 0);
        g.Place(StationType.Translation, 11, 3, 0);
        g.Place(StationType.Dispatch, 14, 3, 0);
        Assert.NotNull(g.Place(StationType.Canteen, 14, 8, 0));
        g.Advance(1);
        g.Accept(g.Contracts.First(c => c.State == ContractState.Offered).Id);
        TestUtil.Run(g, 0.8);
        var reg = g.Stations.First(s => s.Type == StationType.Registration);
        Assert.True(reg.Use!.In((int)DeskUse.Working) > 0 && reg.Use.DoneTotal() > 0, "registration did some work");
        var r = g.Utilisation();
        Assert.Contains(r.Steps, s => s.Type == StationType.Registration && s.Done > 0);
        TestUtil.Run(g, 2.2);

        double day = UseLog.Slots * 60;
        foreach (var st in g.Stations.Where(GameState.Tracked))
        {
            double t = st.Use!.Total();
            // The oldest hour is cleared as each new one starts, so the record holds 23 to 24 hours.
            Assert.InRange(t, day - 61, day + 0.01);
            Assert.InRange(st.Use.DoneTotal(), 0, st.Processed);
        }
        Assert.Null(g.Stations.First(s => s.Type == StationType.Canteen).Use);
        foreach (var p in g.Staff.Where(p => !p.Arriving(g.Now)))
            Assert.InRange(p.Use!.Total(), day - 61, day + 0.01);

        r = g.Utilisation();
        Assert.Equal(24, r.Hours, 3);
        foreach (var s in r.Steps) Assert.Equal(1, s.Share.Sum(), 3);
        foreach (var ro in r.Roles.Where(ro => ro.Minutes > 0)) Assert.Equal(1, ro.Share.Sum(), 3);
    }

    [Fact]
    public void PaperWithNobodyToWorkItIsCalledOut()
    {
        var g = GameState.NewGame(5);
        g.Staff.RemoveAll(p => p.Role == Role.Linguist);
        g.Candidates.Clear();
        var desk = g.Place(StationType.Translation, 11, 3, 0)!;
        g.Advance(60);
        Assert.Equal(DeskUse.Quiet, g.DeskState(desk));
        for (int i = 0; i < 3; i++)
        {
            var m = new Message { Id = g.NextId++, Net = Net.Police, Next = StationType.Translation, Loc = Loc.Input, LocId = desk.Id, Since = g.Now, Created = g.Now };
            g.Messages[m.Id] = m;
            desk.Input.Add(m.Id);
        }
        g.Advance(240);
        Assert.Equal(DeskUse.Unstaffed, g.DeskState(desk));
        Assert.InRange(desk.Use!.In((int)DeskUse.Unstaffed), 239, 241);
        Assert.InRange(desk.Use.In((int)DeskUse.Quiet), 59, 61);

        var r = g.Utilisation();
        var step = r.Steps.Single(s => s.Type == StationType.Translation);
        Assert.Equal(3, step.Waiting);
        Assert.Equal(desk.Id, step.Worst);
        Assert.Equal("unstaffed", r.Top?.Kind);
        Assert.Contains("Translation desks", r.Top!.Text);

        desk.DownUntil = g.Now + 30;
        Assert.Equal(DeskUse.Down, g.DeskState(desk));
    }
}
