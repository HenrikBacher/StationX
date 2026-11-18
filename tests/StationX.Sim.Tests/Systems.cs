using StationX.Sim;
using Xunit;
using Xunit.Abstractions;

namespace StationX.Tests;

static class TestUtil
{
    public static void Run(GameState g, double days)
    {
        for (double t = 0; t < days * Defs.MinutesPerDay; t += 30)
        {
            g.Advance(30);
            if (g.ActCompletePending) g.ContinueAfterAct();
            g.Events.Clear();
        }
    }

    public static Staff Add(GameState g, Role r, double x = 9.5, double y = 6.5, Shift shift = Shift.Office)
    {
        var p = g.NewPerson(r, 2);
        p.Traits.Clear();
        p.X = x; p.Y = y; p.HomeX = x; p.HomeY = y;
        p.Shift = shift;
        g.Staff.Add(p);
        return p;
    }

    /// <summary>A park in act 6 with Hut 6 and Hut 11 open and a working Bombe line for RED.</summary>
    public static GameState BombeLine(int seed, bool checking)
    {
        var g = GameState.NewGame(seed);
        g.DebugSkipToAct(5);
        g.Funds = 50000;
        g.Researched.Add(ResearchId.Bombe);
        foreach (var z in new[] { "hut6", "hut11" }) g.Grid.Zone(z)!.Unlocked = true;
        foreach (var r in new[] { Role.Wren, Role.Wren, Role.Wren, Role.Cryptanalyst, Role.Linguist, Role.Clerk, Role.Clerk }) Add(g, r);
        g.Place(StationType.Registration, 5, 3, 0, free: true);
        g.Place(StationType.Translation, 11, 3, 0, free: true);
        g.Place(StationType.Typex, 22, 3, 0, free: true);
        g.Place(StationType.Crib, 25, 3, 0, free: true);
        g.Place(StationType.Bombe, 36, 3, 0, free: true);
        if (checking) g.Place(StationType.Checking, 40, 3, 0, free: true);
        return g;
    }
}

public class SaveTests
{
    [Fact]
    public void SaveLoadRoundTripIsDeterministic()
    {
        var g = GameState.NewGame(11);
        var ai = new AutoPlayer(g);
        for (int i = 0; i < 24 * 12; i++) { ai.Think(); g.Advance(60); g.Events.Clear(); }
        Assert.True(g.Staff.Count > 4 && g.Messages.Count + g.Stats.DeliveredTotal > 0);

        var json = SaveGame.Save(g);
        var copy = SaveGame.Load(json);
        Assert.Equal(StateOnly(json), StateOnly(SaveGame.Save(copy)));
        Assert.Equal(g.Now, copy.Now);
        Assert.Equal(g.Staff.Count, copy.Staff.Count);
        Assert.Equal(g.Stations.Select(s => s.UseTile), copy.Stations.Select(s => s.UseTile));
        Assert.Equal(g.Grid.Occupant, copy.Grid.Occupant);

        // Both copies must play out identically.
        for (int i = 0; i < 48; i++)
        {
            g.Advance(30); copy.Advance(30);
            g.Events.Clear(); copy.Events.Clear();
        }
        Assert.Equal(StateOnly(SaveGame.Save(g)), StateOnly(SaveGame.Save(copy)));
        Assert.Equal(g.Rng.State, copy.Rng.State);
    }

    static string StateOnly(string json) => json[json.IndexOf("\"State\"", StringComparison.Ordinal)..];

    [Fact]
    public void InfoIsReadable()
    {
        var g = GameState.NewGame(2, Difficulty.Hard);
        var info = SaveGame.ReadInfo(SaveGame.Save(g));
        Assert.NotNull(info);
        Assert.Contains("War Station", info!.Title);
        Assert.Equal(Defs.Difficulties[Difficulty.Hard].StartFunds, info.Funds);
    }
}

public class PeopleTests
{
    [Fact]
    public void TiredStaffTakeATeaBreak()
    {
        var g = GameState.NewGame(5);
        g.Place(StationType.Canteen, 12, 6, 0, free: true);
        var p = TestUtil.Add(g, Role.Clerk, 5.5, 8.5);
        p.Energy = 0.2;
        TestUtil.Run(g, 0.01);
        Assert.Equal(TaskKind.Rest, p.Task?.Kind);
        TestUtil.Run(g, 0.1);
        Assert.True(p.Energy > 0.8, $"energy {p.Energy}");
    }

    [Fact]
    public void OffWatchStaffGoHomeAndComeBack()
    {
        var g = GameState.NewGame(5);
        g.DebugSkipToAct(Defs.WatchesFromAct);
        var night = TestUtil.Add(g, Role.Wren, 9.5, 6.5, Shift.Night);
        TestUtil.Run(g, 0.1);   // 09:00 → off watch
        Assert.True(night.Away);
        double until = (g.Day + 1) * Defs.MinutesPerDay + 60;
        while (g.Now < until) { g.Advance(30); g.Events.Clear(); }
        Assert.False(night.Away);
        Assert.Equal(1.0, night.Energy, 2);
    }

    [Fact]
    public void WatchesNeedTheWatchSystem()
    {
        var g = GameState.NewGame(5);
        var p = g.Staff[0];
        Assert.False(g.SetShift(p.Id, Shift.Night));
        g.DebugSkipToAct(Defs.WatchesFromAct);
        Assert.True(g.SetShift(p.Id, Shift.Night));
        g.BalanceWatches(null);
        Assert.DoesNotContain(g.Staff, s => s.Shift == Shift.Office);
    }

    [Fact]
    public void PinnedStaffOnlyWorkTheirStation()
    {
        var g = GameState.NewGame(9);
        var a = g.Place(StationType.Registration, 5, 3, 0, free: true)!;
        var b = g.Place(StationType.Registration, 12, 3, 0, free: true)!;
        var clerk = g.Staff.First(s => s.Role == Role.Clerk);
        Assert.True(g.Pin(clerk.Id, b.Id));
        Assert.False(g.Pin(clerk.Id, g.Stations.First(s => s.Type == StationType.Inbox).Id));
        g.Staff.RemoveAll(s => s.Role == Role.Clerk && s != clerk);
        g.Place(StationType.HandCipher, 8, 6, 0, free: true);
        g.AddScripted(Net.Police, 4, 4, 0, 0, "t", "t", "t");
        TestUtil.Run(g, 3);
        Assert.Equal(0, a.Processed);
        Assert.True(b.Processed > 0);
    }

    [Fact]
    public void MovingAStationKeepsItsTrays()
    {
        var g = GameState.NewGame(4);
        var reg = g.Place(StationType.Registration, 5, 3, 0, free: true)!;
        g.Messages[999] = new Message { Id = 999, Net = Net.Police, Loc = Loc.Input, LocId = reg.Id };
        reg.Input.Add(999);
        Assert.Equal("Someone is standing there", g.CanMove(reg.Id, 10, 6, 1));
        Assert.Null(g.CanMove(reg.Id, 12, 8, 1));
        int funds = g.Funds;
        Assert.True(g.Move(reg.Id, 12, 8, 1));
        Assert.Equal((12, 8, 1), (reg.X, reg.Y, reg.Rot));
        Assert.Equal(funds - Defs.MoveCost, g.Funds);
        Assert.Contains(999, reg.Input);
        Assert.Equal(reg.Id, g.Grid.OccupantAt(12, 9));
        Assert.Equal(-1, g.Grid.OccupantAt(5, 3));
        Assert.NotNull(g.CanMove(reg.Id, 25, 13, 0));
    }

    [Fact]
    public void RoundersGoesOnTheLawn()
    {
        var g = GameState.NewGame(4);
        g.DebugSkipToAct(2);
        Assert.Equal("Needs open lawn", g.CanPlace(StationType.Rounders, 5, 3, 0));
        Assert.Equal("Needs open lawn", g.CanPlace(StationType.Rounders, 20, 12, 0));
        Assert.Null(g.CanPlace(StationType.Rounders, 22, 9, 0));
    }

    [Fact]
    public void TraitsChangeTheWork()
    {
        var g = GameState.NewGame(4);
        var p = TestUtil.Add(g, Role.Linguist);
        double plain = p.SpeedFor(StationType.Translation);
        p.Traits.Add(Trait.NativeGerman);
        Assert.Equal(plain * 1.4, p.SpeedFor(StationType.Translation), 5);
        p.Traits.Add(Trait.CarelessTalker);
        Assert.DoesNotContain(Trait.CarelessTalker, p.KnownTraits());
    }
}

public class IntelligenceTests
{
    [Fact]
    public void TooMuchSuspicionLocksAKeyDown()
    {
        var g = TestUtil.BombeLine(3, checking: true);
        Assert.NotNull(g.KeyRoute(Net.Red));
        g.AddSuspicion(Service.Luftwaffe, 200);
        Assert.True(g.LockedDown(Net.Red));
        Assert.Null(g.KeyRoute(Net.Red));
        Assert.StartsWith("Procedures tightened", g.KeyRouteProblem(Net.Red));
        TestUtil.Run(g, Defs.LockdownDays + 1.1);
        Assert.False(g.LockedDown(Net.Red));
    }

    [Fact]
    public void PolicyScalesSuspicion()
    {
        var g = GameState.NewGame(8);
        g.SetPolicy(Policy.Cautious);
        double cautious = g.SuspicionPerDecrypt(Net.Red);
        g.SetPolicy(Policy.Bold);
        Assert.True(g.SuspicionPerDecrypt(Net.Red) > cautious * 4);
        Assert.True(g.SuspicionPerDecrypt(Net.Police) < g.SuspicionPerDecrypt(Net.Red));
    }

    [Fact]
    public void DilemmaOptionsApply()
    {
        var g = GameState.NewGame(4);
        var who = g.Staff[0];
        who.Traits.Add(Trait.CarelessTalker);
        var d = new Dilemma
        {
            Service = Service.Security, StaffId = who.Id, Title = "t",
            Options = { new DilemmaOption { Funds = 100, Suspicion = 10, DismissStaff = true, RevealTalker = true, Outcome = "o" } },
        };
        g.RaiseDilemma(d);
        int funds = g.Funds;
        Assert.True(g.ResolveDilemma(d.Id, 0));
        Assert.Equal(funds + 100, g.Funds);
        Assert.Equal(10, g.SuspicionOf(Service.Security), 3);
        Assert.True(who.Leaving || g.StaffById(who.Id) == null);
        Assert.Empty(g.Dilemmas);
    }

    [Fact]
    public void GardeningGivesCribs()
    {
        var g = GameState.NewGame(4);
        g.DebugSkipToAct(7);
        g.Funds = 5000;
        Assert.Equal("Only for naval keys", g.CanGarden(Net.Red));
        Assert.True(g.Garden(Net.Dolphin));
        Assert.True(g.Gardened(Net.Dolphin, g.Day + 1));
        Assert.False(g.Gardened(Net.Dolphin, g.Day + 2));
        Assert.Equal("Mines already laid", g.CanGarden(Net.Dolphin));
    }
}

public class MachineTests(ITestOutputHelper output)
{
    [Fact]
    public void BombeStopsGoThroughTheCheckingMachine()
    {
        var g = TestUtil.BombeLine(21, checking: true);
        g.AddScripted(Net.Red, 8, 4, 0, 0, "t", "t", "t");
        TestUtil.Run(g, 3);
        var check = g.Stations.Single(s => s.Type == StationType.Checking);
        Assert.True(check.Processed > 0);
        Assert.True(Stats.Get(g.Stats.BombeBreaks, Net.Red) > 0);
        Assert.True(Stats.Get(g.Stats.Delivered, Net.Red) > 0);
    }

    [Fact]
    public void WithoutCheckingBombesRunLonger()
    {
        var with = TestUtil.BombeLine(1, checking: true);
        var without = TestUtil.BombeLine(1, checking: false);
        var m = new Message { Net = Net.Red };
        var bw = with.Stations.Single(s => s.Type == StationType.Bombe);
        var bo = without.Stations.Single(s => s.Type == StationType.Bombe);
        var op = with.Staff.First(s => s.Role == Role.Wren);
        Assert.True(without.Duration(bo, op, m) > with.Duration(bw, op, m) * 1.2);
    }

    [Fact]
    public void OutstationBombesBreakKeys()
    {
        var g = TestUtil.BombeLine(6, checking: true);
        g.Stations.RemoveAll(s => s.Type == StationType.Bombe);
        g.Grid.RebuildOccupancy(g.Stations);
        g.DebugSkipToAct(9);
        Assert.Null(g.KeyRoute(Net.Red));
        Assert.True(g.BuyOutstation());
        g.Place(StationType.OutstationLink, 38, 3, 0, free: true);
        Assert.NotNull(g.KeyRoute(Net.Red));
        g.AddScripted(Net.Red, 6, 4, 0, 0, "t", "t", "t");
        TestUtil.Run(g, 3);
        Assert.True(Stats.Get(g.Stats.BombeBreaks, Net.Red) > 0);
    }

    [Fact]
    public void TubesCarryMessagesBetweenHuts()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        g.Researched.Add(ResearchId.PneumaticTubes);
        g.Place(StationType.Tube, 4, 6, 0, free: true);
        g.Place(StationType.Tube, 22, 6, 0, free: true);
        g.Place(StationType.Tube, 37, 6, 0, free: true);
        g.AddScripted(Net.Red, 10, 4, 0, 0, "t", "t", "t");
        TestUtil.Run(g, 3);
        Assert.True(g.Stations.Where(s => s.Type == StationType.Tube).Sum(s => s.Processed) > 0);
        Assert.True(Stats.Get(g.Stats.Delivered, Net.Red) > 0);
    }

    [Fact]
    public void GarbledInterceptsAreRequested()
    {
        var g = GameState.NewGame(7);
        g.DebugSkipToAct(1);
        g.Place(StationType.Registration, 5, 3, 0, free: true);
        g.Place(StationType.HandCipher, 8, 3, 0, free: true);
        g.Place(StationType.Translation, 11, 3, 0, free: true);
        var c = g.AddScripted(Net.Police, 4, 4, 0, 0, "t", "t", "t");
        g.Advance(1);
        TestUtil.Run(g, 0.3);
        var m = g.Messages.Values.First();
        m.Garbled = true;
        TestUtil.Run(g, 3);
        Assert.Equal(ContractState.Completed, c.State);
    }

    [Fact]
    public void TunnyGoesThroughTheTestery()
    {
        var g = GameState.NewGame(13);
        g.DebugSkipToAct(12);
        g.Researched.Add(ResearchId.Turingery);
        g.Grid.Zone("blockf")!.Unlocked = true;
        foreach (var r in new[] { Role.Wren, Role.Cryptanalyst, Role.Cryptanalyst, Role.Linguist, Role.Clerk }) TestUtil.Add(g, r);
        g.Place(StationType.Registration, 5, 3, 0, free: true);
        g.Place(StationType.Translation, 11, 3, 0, free: true);
        Assert.Equal("Build a Testery desk", g.KeyRouteProblem(Net.Tunny));
        g.Place(StationType.Testery, 21, 26, 0, free: true);
        g.Place(StationType.TunnyMachine, 25, 27, 0, free: true);
        Assert.Equal(StationType.Testery, g.KeyRoute(Net.Tunny));
        g.AddScripted(Net.Tunny, 5, 5, 0, 0, "t", "t", "t");
        TestUtil.Run(g, 4);
        output.WriteLine($"delivered {Stats.Get(g.Stats.Delivered, Net.Tunny)}");
        Assert.True(Stats.Get(g.Stats.KeyBreaks, Net.Tunny) > 0);
        Assert.True(Stats.Get(g.Stats.Delivered, Net.Tunny) > 0);
    }
}

public class ModeTests(ITestOutputHelper output)
{
    [Fact]
    public void DifficultySetsStartingFunds()
    {
        Assert.True(GameState.NewGame(1, Difficulty.Easy).Funds > GameState.NewGame(1, Difficulty.Hard).Funds);
    }

    [Fact]
    public void FreePlayMovesThroughHistory()
    {
        var g = GameState.NewGame(3, mode: GameMode.FreePlay, startAct: 5);
        Assert.Equal(5, g.ActIndex);
        Assert.Contains(ResearchId.Bombe, g.Researched);
        Assert.True(g.Grid.Zone("hut6")!.Unlocked);
        var ai = new AutoPlayer(g);
        ai.PlayCampaign(maxDays: g.Day + 25, log: output.WriteLine);
        Assert.Equal(GameStatus.Playing, g.Status);
        Assert.True(g.ActIndex >= 7, $"act {g.ActIndex}");
        Assert.True(g.History.Count >= 20);
    }

    [Fact]
    public void TheTunnyChapterCanBeFinished()
    {
        var g = GameState.NewGame(77);
        var ai = new AutoPlayer(g) { ContinuePastEnigma = true };
        ai.PlayCampaign(maxDays: 400, log: output.WriteLine);
        Assert.True(g.Status == GameStatus.Won && g.ActIndex == Campaign.Acts.Length - 1,
            $"act {g.ActIndex} '{g.Act.Title}' status {g.Status} day {g.Day}: " +
            string.Join("; ", g.Act.Goals.Select(x => $"{x.Text} {x.Progress(g)}/{x.Target}")) + $" funds {g.Funds}");
        Assert.Equal(Campaign.TunnyEpilogue, g.EndReason);
    }
}

public class HutRoleTests
{
    [Fact]
    public void StationsOnlyGoWhereTheyBelong()
    {
        var g = TestUtil.BombeLine(5, checking: false);
        g.Grid.Zone("hut8")!.Unlocked = true;
        g.Researched.Add(ResearchId.Banburismus);
        g.DebugSkipToAct(7);
        g.Funds = 50000;

        // Machines live in the huts, not the Mansion.
        Assert.StartsWith("Belongs in Hut 1 or Hut 11", g.CanPlace(StationType.Bombe, 5, 6, 0));
        Assert.StartsWith("Belongs in Hut 6 or Hut 8", g.CanPlace(StationType.Typex, 5, 6, 0));
        Assert.Null(g.CanPlace(StationType.Bombe, 36, 5, 0));
        // Hand ciphers stay in the Mansion; Banburismus is naval work.
        Assert.StartsWith("Belongs in The Mansion", g.CanPlace(StationType.HandCipher, 22, 6, 0));
        Assert.StartsWith("Belongs in", g.CanPlace(StationType.Banburismus, 22, 6, 0));
        Assert.Null(g.CanPlace(StationType.Banburismus, 36, 20, 0));
        // Tea goes anywhere.
        Assert.Null(g.CanPlace(StationType.Canteen, 42, 5, 0));

        // A move is held to the same rules.
        var bombe = g.Stations.First(s => s.Type == StationType.Bombe);
        Assert.StartsWith("Belongs in", g.CanMove(bombe.Id, 22, 6, 0));
    }

    [Fact]
    public void EveryStationHasSomewhereToGo()
    {
        foreach (var t in Enum.GetValues<StationType>())
            if (!Defs.Station(t).Outdoor) Assert.NotEmpty(Defs.ZonesFor(t));
    }
}

public class MessengerTests
{
    [Fact]
    public void MessengersTakeTheErrandsAndWalkFaster()
    {
        var g = GameState.NewGame(8);
        g.DebugSkipToAct(1);
        g.Funds = 50000;
        var ai = new AutoPlayer(g);
        var boy = TestUtil.Add(g, Role.Messenger);
        int messengerCarrying = 0, clerkCarrying = 0;
        for (int i = 0; i < 24 * 6; i++)
        {
            ai.Think();
            g.Advance(10);
            g.Events.Clear();
            if (boy.Task?.Kind == TaskKind.Carry) messengerCarrying++;
            clerkCarrying += g.Staff.Count(s => s.Role == Role.Clerk && s.Task?.Kind == TaskKind.Carry);
        }
        Assert.True(messengerCarrying > 0, "the messenger never carried anything");
        Assert.True(messengerCarrying * 2 > clerkCarrying / Math.Max(1, g.Staff.Count(s => s.Role == Role.Clerk)),
            $"messenger carried {messengerCarrying} ticks, clerks {clerkCarrying}");

        // Walking: the messenger covers more ground in the same time.
        var clerk = TestUtil.Add(g, Role.Clerk);
        foreach (var s in new[] { boy, clerk })
        {
            s.Task = null; s.Path.Clear();
            s.X = 2.5; s.Y = 13.5;
        }
        foreach (var s in new[] { boy, clerk })
        {
            for (int x = 3; x <= 40; x++) s.Path.Add((x, 13));
            s.Task = new StaffTask { Kind = TaskKind.Leave };
        }
        g.Advance(30);
        Assert.True(boy.X > clerk.X + 1, $"messenger at {boy.X:0.0}, clerk at {clerk.X:0.0}");
    }

    [Fact]
    public void MessengersCannotWorkDesks()
    {
        Assert.DoesNotContain(Defs.Stations.Values, d => d.Operator == Role.Messenger);
    }
}

public class BreakdownTests
{
    /// <summary>The line's Bombe, already running a very long job so it wears while the test watches.</summary>
    static Station Bombe(GameState g)
    {
        var b = g.Stations.First(s => s.Type == StationType.Bombe);
        var m = new Message { Id = g.NextId++, Net = Net.Red, Day = g.Day, Created = g.Now, Since = g.Now, Loc = Loc.Processing, LocId = b.Id };
        g.Messages[m.Id] = m;
        b.Current = m.Id;
        b.WorkTotal = 100000;
        return b;
    }

    [Fact]
    public void WornMachinesBreakDownAndGetRepaired()
    {
        var g = TestUtil.BombeLine(5, false);
        var b = Bombe(g);
        b.Wear = 1.4;
        long funds = g.Funds;
        for (int i = 0; i < 48 * 4 && b.DownUntil <= 0; i++) { g.Advance(15); g.Events.Clear(); }
        Assert.True(b.DownUntil > 0, "a badly worn Bombe never broke down");
        Assert.False(b.Servicing);
        Assert.True(g.Stats.Breakdowns >= 1);
        Assert.True(b.Wear < 0.5);
        double work = b.WorkDone;
        g.Advance(60);
        Assert.Equal(work, b.WorkDone);   // nothing happens while it's down
        g.Advance(Defs.RepairMinutes);
        g.Advance(1);
        Assert.Equal(0, b.DownUntil);
    }

    [Fact]
    public void FreshMachinesRarelyBreak()
    {
        var g = TestUtil.BombeLine(6, false);
        var b = Bombe(g);
        b.Wear = 0;
        g.Advance(240);
        Assert.Equal(0, g.Stats.Breakdowns);
    }

    [Fact]
    public void ServicingResetsWearAndTakesAnHour()
    {
        var g = TestUtil.BombeLine(7, false);
        var b = Bombe(g);
        b.Wear = 0.8;
        Assert.Null(g.CanServiceMachine(b.Id));
        long funds = g.Funds;
        Assert.True(g.ServiceMachine(b.Id));
        Assert.Equal(funds - Defs.ServiceCost, g.Funds);
        Assert.Equal(0, b.Wear);
        Assert.True(b.IsDown(g.Now) && b.Servicing);
        Assert.NotNull(g.CanServiceMachine(b.Id));
        g.Advance(Defs.ServiceMinutes + 1);
        Assert.False(b.IsDown(g.Now));
        Assert.NotNull(g.CanServiceMachine(g.Stations.First(s => s.Type == StationType.Registration).Id));
    }
}

public class CallUpTests
{
    [Fact]
    public void MenGetCalledUpAndCanBeKeptOrLost()
    {
        var g = GameState.NewGame(4);
        g.DebugSkipToAct(2);
        var man = TestUtil.Add(g, Role.Linguist);
        man.Female = false; man.Notable = null; man.Skill = 3;
        Dilemma? d = null;
        for (int day = 0; day < 80 && d == null; day++)
        {
            g.Advance(Defs.MinutesPerDay);
            g.Events.Clear();
            d = g.Dilemmas.Find(x => x.Title == "Call-up papers");
            if (d == null) g.Dilemmas.Clear();
        }
        Assert.NotNull(d);
        var who = g.StaffById(d!.StaffId!.Value)!;
        Assert.False(who.Female);
        Assert.NotEqual(Role.Messenger, who.Role);

        long funds = g.Funds;
        Assert.True(g.ResolveDilemma(d.Id, 1));
        Assert.Equal(funds - Defs.CallUpRulingCost, g.Funds);
        Assert.False(who.Leaving);
    }

    [Fact]
    public void NoCallUpsBeforeThePhoneyWarOrForWomen()
    {
        var g = GameState.NewGame(4);
        foreach (var s in g.Staff) s.Female = true;
        for (int day = 0; day < 6; day++) { g.Advance(Defs.MinutesPerDay); g.Events.Clear(); }
        Assert.DoesNotContain(g.Dilemmas, x => x.Title == "Call-up papers");
    }
}
