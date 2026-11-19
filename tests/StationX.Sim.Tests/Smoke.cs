using StationX.Sim;
using Xunit;
using Xunit.Abstractions;

namespace StationX.Tests;

public class GridTests
{
    [Fact]
    public void GateReachesMansionDoor()
    {
        var g = GameState.NewGame(1);
        var mansion = g.Grid.Zone("mansion")!;
        var path = Pathfinder.Find(g.Grid, Grid.Gate, mansion.InsideDoor);
        Assert.NotNull(path);
        Assert.Contains((mansion.DoorX, mansion.DoorY), path!);
    }

    [Fact]
    public void PlacementRules()
    {
        var g = GameState.NewGame(1);
        Assert.Equal("Must be built inside a hut", g.CanPlace(StationType.Registration, 25, 13, 0));
        Assert.Contains("isn't open", g.CanPlace(StationType.Registration, 22, 4, 0));
        var door = g.Grid.Zone("mansion")!.InsideDoor;
        Assert.Equal("Keep the doorway clear", g.CanPlace(StationType.Dispatch, door.x, door.y, 0));
        Assert.Null(g.CanPlace(StationType.Registration, 5, 3, 0));
        Assert.NotNull(g.Place(StationType.Registration, 5, 3, 0));
        Assert.Equal("Something is already there", g.CanPlace(StationType.HandCipher, 5, 3, 0));
    }

    [Fact]
    public void UseTileFacesRotation()
    {
        Assert.Equal((5, 4), Station.UseTileFor(StationType.Registration, 5, 3, 0));
        Assert.Equal((5, 2), Station.UseTileFor(StationType.Registration, 5, 3, 2));
        Assert.Equal((6, 3), Station.UseTileFor(StationType.Registration, 5, 3, 1));
        Assert.Equal((4, 3), Station.UseTileFor(StationType.Registration, 5, 3, 3));
    }
}

public class PipelineTests
{
    static void Run(GameState g, double days)
    {
        for (double t = 0; t < days * Defs.MinutesPerDay; t += 30)
        {
            g.Advance(30);
            if (g.ActCompletePending) g.ContinueAfterAct();
        }
    }

    [Fact]
    public void HandCipherTrafficIsDelivered()
    {
        var g = GameState.NewGame(7);
        g.Place(StationType.Registration, 5, 3, 0);
        g.Place(StationType.HandCipher, 8, 3, 0);
        g.Place(StationType.Translation, 11, 3, 0);
        Assert.NotNull(g.Place(StationType.Canteen, 14, 8, 0));   // nobody lasts long without a cup of tea
        g.Advance(1);
        var c = g.Contracts.First(c => c.State == ContractState.Offered);
        Assert.True(g.Accept(c.Id));
        Run(g, 3);
        Assert.True(g.Stats.DeliveredTotal >= c.Quantity, $"delivered {g.Stats.DeliveredTotal} of {c.Quantity}");
        Assert.Equal(ContractState.Completed, c.State);
    }

    [Fact]
    public void EnigmaWaitsForTheDaysKey()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(2);
        g.Funds = 10000;
        g.Grid.Zone("hut6")!.Unlocked = true;
        foreach (var r in new[] { Role.Wren, Role.Cryptanalyst, Role.Linguist, Role.Clerk })
        {
            var p = g.NewPerson(r, 1); p.X = 9.5; p.Y = 6.5; g.Staff.Add(p);
        }
        g.Place(StationType.Registration, 5, 3, 0);
        g.Place(StationType.Translation, 11, 3, 0);
        g.Place(StationType.Typex, 22 + 1, 3, 0);
        // No way to break RED yet: messages pile up waiting.
        var c = g.AddScripted(Net.Red, 6, 4, 0, 0, "test", "test", "test");
        Run(g, 1);
        Assert.Equal(0, Stats.Get(g.Stats.Delivered, Net.Red));
        Assert.Contains(g.Messages.Values, m => m.Net == Net.Red && m.Next == null);
        Assert.Equal("Build a Zygalski sheet table", g.KeyRouteProblem(Net.Red));

        Assert.NotNull(g.Place(StationType.Zygalski, 21, 3, 0));   // Hut 6 Machine Room
        Run(g, 3);
        Assert.True(Stats.Get(g.Stats.KeyBreaks, Net.Red) >= 1);
        Assert.True(Stats.Get(g.Stats.Delivered, Net.Red) >= 1);
    }

    [Fact]
    public void IndicatorChangeObsoletesSheets()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(2);
        g.Grid.Zone("hut6")!.Unlocked = true;
        g.Place(StationType.Zygalski, 25, 3, 0, free: true);
        Assert.NotNull(g.KeyRoute(Net.Red));
        g.DebugSkipToAct(4);
        Assert.True(g.IndicatorChanged);
        Assert.Null(g.KeyRoute(Net.Red));
        Assert.All(g.Stations.Where(s => s.Type == StationType.Zygalski), s => Assert.True(s.Obsolete));
    }

    [Fact]
    public void PinchGivesFreeKeys()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(7);
        Assert.True(g.KeyBroken(Net.Dolphin, g.Day));
        Assert.True(g.KeyBroken(Net.Dolphin, g.Day + 3));
        Assert.False(g.KeyBroken(Net.Dolphin, g.Day + 4));
    }
}

public class CampaignTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(1942)]
    public void AutoplayerFinishesTheCampaign(int seed)
    {
        // Proves every act can be finished. Easy, because Normal is tuned so the autoplayer loses about one
        // campaign in six: a fixed Normal seed would fail after any legitimate tuning. Win rates per difficulty
        // come from the 20-seed balance report (BALANCE=1).
        var g = GameState.NewGame(seed, Difficulty.Easy);
        var ai = new AutoPlayer(g);
        var acts = ai.PlayCampaign(maxDays: 400, log: output.WriteLine);
        output.WriteLine($"status {g.Status} day {g.Day} delivered {g.Stats.DeliveredTotal} failed {g.Stats.ContractsFailed} completed {g.Stats.ContractsCompleted}");
        Assert.True(g.Status == GameStatus.Won,
            $"Stuck in act {g.ActIndex} '{g.Act.Title}' on day {g.Day}: " +
            string.Join("; ", g.Act.Goals.Select(x => $"{x.Text} {x.Progress(g)}/{x.Target}")) +
            $" | funds {g.Funds} rep {g.Reputation:0} staff {g.Staff.Count} msgs {g.Messages.Count}");
        Assert.Equal(Campaign.Acts.Length, acts.Count);
    }
}
