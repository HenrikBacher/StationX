using System;
using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class CrosswordTests
{
    [Fact]
    public void NotBeforeJanuary1942()
    {
        var g = GameState.NewGame(7);
        g.Funds = 5000;
        Assert.False(g.CrosswordOpen);
        Assert.NotNull(g.CantHoldCrossword());
        Assert.False(g.HoldCrossword());
    }

    [Fact]
    public void WinnersJoinTheCandidatesAndStayUntilTheNextCompetition()
    {
        var g = GameState.NewGame(7);
        g.Funds = 5000;
        g.ActDate = Defs.CrosswordFrom.AddDays(-g.Day + g.ActStartDay);
        Assert.True(g.CrosswordOpen);
        Assert.Null(g.CantHoldCrossword());
        Assert.True(g.HoldCrossword());
        Assert.Equal(5000 - Defs.CrosswordCost, g.Funds);
        Assert.NotNull(g.CantHoldCrossword());   // under way
        Assert.Empty(g.Candidates.Where(c => c.Crossword));

        TestUtil.Run(g, Defs.CrosswordDays + 1);
        var won = g.Candidates.Where(c => c.Crossword).ToList();
        Assert.Equal(Defs.CrosswordRecruits, won.Count);
        Assert.All(won, c =>
        {
            Assert.Equal(Role.Cryptanalyst, c.Role);
            Assert.True(c.Skill >= 2);
            Assert.True(c.Has(Trait.CrosswordFiend));
            Assert.NotNull(c.Bio);
        });
        g.DebugRefreshCandidates();   // the weekly intake doesn't sweep them away
        Assert.Equal(Defs.CrosswordRecruits, g.Candidates.Count(c => c.Crossword));
        Assert.NotNull(g.CantHoldCrossword());   // not again for a few weeks
        Assert.True(g.Hire(won[0].Id));
    }
}
