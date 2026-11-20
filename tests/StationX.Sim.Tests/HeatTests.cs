using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class HeatTests
{
    /// <summary>A Park in high summer with Hut 11 open and three Bombes in it, each given a key to run.</summary>
    static GameState BombeBay(int bombes)
    {
        var g = GameState.NewGame(4);
        g.DebugSkipToAct(Campaign.IndexOf("Their Finest Hour"));
        while (!g.Summer) g.Advance(Defs.MinutesPerDay);
        g.Funds = 50000;
        g.Researched.Add(ResearchId.Bombe);
        g.Grid.Zone("hut11")!.Unlocked = true;
        for (int i = 0; i < bombes; i++)
        {
            var st = g.Place(StationType.Bombe, 35 + i * 3, 3, 0, free: true);
            Assert.NotNull(st);
            // A very long run, as the breakdown tests give one, so they stay busy while the test watches.
            var m = new Message { Id = g.NextId++, Net = Net.Red, Day = g.Day, Created = g.Now, Since = g.Now, Loc = Loc.Processing, LocId = st!.Id };
            g.Messages[m.Id] = m;
            st.Current = m.Id;
            st.WorkTotal = 100000;
        }
        return g;
    }

    [Fact]
    public void ABombeBayIsAFurnaceInSummerUntilAFanGoesIn()
    {
        var g = BombeBay(Defs.HotBombes);
        Assert.Contains("hut11", g.HotZones()!);
        Assert.NotNull(g.Place(StationType.Fan, 44, 6, 0));
        Assert.Null(g.HotZones());
    }

    [Fact]
    public void FewerBombesStayBearable()
    {
        Assert.Null(BombeBay(Defs.HotBombes - 1).HotZones());
    }

    [Fact]
    public void TheHeatWearsPeopleDown()
    {
        double Energy(bool fan)
        {
            var g = BombeBay(Defs.HotBombes);
            if (fan) Assert.NotNull(g.Place(StationType.Fan, 44, 6, 0));
            var w = TestUtil.Add(g, Role.Wren, 40.5, 6.5);
            w.Task = new StaffTask { Kind = TaskKind.Operate, Dest = g.Stations.First(s => s.Type == StationType.Bombe).Id };
            double before = w.Energy;
            g.Advance(30);
            return before - w.Energy;
        }
        Assert.True(Energy(fan: false) > Energy(fan: true) * 1.2);
    }

    [Fact]
    public void FansGoWhereBombesGo()
    {
        Assert.True(Defs.AllowedIn("hut11", StationType.Fan));
        Assert.False(Defs.AllowedIn("mansion", StationType.Fan));
    }
}
