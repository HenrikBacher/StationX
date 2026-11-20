using System;
using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class PmVisitTests
{
    [Fact]
    public void ThePrimeMinisterComesOnceInBarbarossaAndLiftsEveryone()
    {
        var g = GameState.NewGame(9);
        g.DebugSkipToAct(Campaign.IndexOf("Barbarossa"));
        g.Funds = 20000;
        bool announced = false, visited = false;
        double moraleBefore = 0;
        while (g.Now < (g.ActStartDay + 6) * Defs.MinutesPerDay && g.Status == GameStatus.Playing && !g.ActCompletePending)
        {
            g.Dilemmas.RemoveAll(d => d.Tag != "op");
            g.Reputation = Math.Max(g.Reputation, 50);
            if (g.PmVisit && !announced)
            {
                announced = true;
                Assert.Equal("the Prime Minister", g.InspectionBy);
                Assert.True(g.InspectionAt - g.Now < Defs.MinutesPerDay * 1.5);
            }
            if (announced && !visited && g.Now + 30 >= g.InspectionAt)
            {
                foreach (var s in g.Staff) s.Morale = 0.5;
                moraleBefore = 0.5;
            }
            bool due = g.InspectionDue;
            g.Advance(30);
            if (announced && due && !g.InspectionDue && !visited)
            {
                visited = true;
                Assert.False(g.PmVisit);
                Assert.True(g.Staff.Average(s => s.Morale) > moraleBefore + Defs.PmMorale / 2, "his speech lifts everyone");
                Assert.True(g.Stats.RepByCause.ContainsKey("The Prime Minister"));
            }
        }
        Assert.True(announced && visited);
        Assert.Contains("pm1941", g.Stats.ScriptedDone);
    }

    [Fact]
    public void NotInFreePlayOrOtherChapters()
    {
        var g = GameState.NewGame(9);
        g.DebugSkipToAct(Campaign.IndexOf("U-110"));
        g.Funds = 20000;
        for (int i = 0; i < 6 * 48; i++) { g.Reputation = Math.Max(g.Reputation, 50); g.Dilemmas.Clear(); g.Advance(30); Assert.False(g.PmVisit); }
    }
}
