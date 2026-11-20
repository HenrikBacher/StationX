using System.Linq;
using StationX.Sim;
using Xunit;

namespace StationX.Tests;

public class CourseTests
{
    [Fact]
    public void OnlySensibleCoursesExist()
    {
        Assert.Equal((12.0, 30), Defs.Course(Role.Linguist, Role.Clerk));
        Assert.Equal((12.0, 30), Defs.Course(Role.Clerk, Role.Wren));
        Assert.Equal((72.0, 80), Defs.Course(Role.Linguist, Role.Cryptanalyst));
        Assert.Null(Defs.Course(Role.Clerk, Role.Cryptanalyst));
        Assert.Null(Defs.Course(Role.Wren, Role.Linguist));
        Assert.Null(Defs.Course(Role.Clerk, Role.Clerk));
    }

    [Fact]
    public void ASecondRoleIsWorkedMoreSlowly()
    {
        var g = GameState.NewGame(7);
        var p = TestUtil.Add(g, Role.Linguist);
        p.Specialties.Clear();
        Assert.Equal(Defs.SecondRoleSpeed, p.SpeedFor(StationType.Registration) / p.SpeedFor(StationType.Translation), 6);
    }

    [Fact]
    public void ALinguistBackFromACourseRegistersWhenNoClerkIsLeft()
    {
        var g = GameState.NewGame(7);
        g.Funds = 5000;
        g.Place(StationType.Registration, 5, 3, 0);
        g.Place(StationType.HandCipher, 8, 3, 0);
        g.Place(StationType.Translation, 11, 3, 0);
        Assert.NotNull(g.Place(StationType.Canteen, 14, 8, 0));
        g.Staff.RemoveAll(s => s.Role is Role.Clerk or Role.Messenger);
        var lin = g.Staff.First(s => s.Role == Role.Linguist);
        g.Advance(1);
        var c = g.Contracts.Where(k => k.State == ContractState.Offered && Defs.NetOf(k.Net).Family == Family.Hand).OrderBy(k => k.Quantity).First();
        Assert.True(g.Accept(c.Id));
        TestUtil.Run(g, 1);
        Assert.Equal(0, g.Stats.DeliveredTotal);   // nobody to register anything

        int funds = g.Funds;
        Assert.Null(g.CantTrain(lin, Role.Clerk));
        Assert.True(g.Train(lin.Id, Role.Clerk));
        Assert.Equal(funds - 30, g.Funds);
        Assert.True(lin.IsSick(g.Now) && lin.OffReason == "course");
        Assert.NotNull(g.CantTrain(lin, Role.Wren));   // away on the course
        TestUtil.Run(g, 3);
        Assert.Equal(Role.Clerk, lin.Second);
        Assert.True(g.Stats.DeliveredTotal > 0, "the linguist registered the traffic, then translated it");
    }
}
