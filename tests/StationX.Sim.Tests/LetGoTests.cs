using System;
using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class LetGoTests
{
    [Fact]
    public void LettingGoTheSpareLeavesEveryWatchWhatItNeeded()
    {
        var g = GameState.NewGame(7);
        var ai = new AutoPlayer(g);
        while (g.Status == GameStatus.Playing && g.Day < 60)
        {
            if (g.ActCompletePending) g.ContinueAfterAct();
            ai.Think();
            g.Reputation = Math.Max(g.Reputation, 30);
            g.Advance(60);
            g.Events.Clear();
        }
        Assert.True(g.WatchesAvailable);
        int let = 0;
        foreach (var role in Enum.GetValues<Role>())
        {
            var go = g.Expendable(role);
            Assert.All(go, p => { Assert.Equal(role, p.Role); Assert.Null(p.HeadOf); Assert.Null(p.Notable); Assert.True(g.SpareHand(p)); });
            var use = g.Utilisation();
            foreach (var w in use.Staffing.Where(w => w.Role == role))
                Assert.True(w.On - go.Count(p => p.Shift == w.Shift) >= Math.Min(w.On, Math.Max(w.Needed, 1) + 1), $"{role} on {w.Shift} keeps what it needed");
            int before = g.Staff.Count(p => p.Role == role && !p.Leaving);
            Assert.Equal(go.Count, g.LetGo(role));
            Assert.Equal(before - go.Count, g.Staff.Count(p => p.Role == role && !p.Leaving));
            Assert.Equal(0, g.LetGo(role));   // asked again at once (paused): nobody more goes
            let += go.Count;
        }
        Assert.True(let > 0, "the autoplayer's Park has hands to spare by now");
    }
}
