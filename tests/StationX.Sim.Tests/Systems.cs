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
        Assert.NotNull(g.Place(StationType.Index, 14, 8, 0, free: true));   // act 5: decrypts are indexed
        g.Place(StationType.Typex, 22, 3, 0, free: true);
        g.Place(StationType.Crib, 21, 5, 0, free: true);   // Hut 6 Machine Room
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
        Assert.True(night.Energy > 0.9, $"rested: {night.Energy}");   // fresh, less the walk in
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
        g.Place(StationType.Tube, 4, 5, 0, free: true);
        g.Place(StationType.Tube, 22, 5, 0, free: true);
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
        Assert.NotNull(g.Place(StationType.Testery, 21, 27, 0, free: true));      // the Testery room, off the corridor
        Assert.NotNull(g.Place(StationType.TunnyMachine, 25, 27, 0, free: true));
        Assert.NotNull(g.Place(StationType.Index, 14, 8, 0, free: true));
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
        // Easy: Normal is tuned so the autoplayer loses half its campaigns.
        var g = GameState.NewGame(77, Difficulty.Easy);
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

public class WinterTests
{
    [Fact]
    public void TimberHutsAreColdInWinterUntilAStoveGoesIn()
    {
        var g = TestUtil.BombeLine(9, false);   // act 5: July 1940
        Assert.False(g.Winter);
        Assert.Null(g.ColdZones());
        g.ActDate = new DateOnly(1940, 12, 10);
        Assert.True(g.Winter);
        var cold = g.ColdZones()!;
        Assert.Contains("hut6", cold);
        Assert.DoesNotContain("mansion", cold);
        Assert.NotNull(g.CanPlace(StationType.Stove, 5, 6, 0));     // no stoves in the Mansion
        Assert.Null(g.CanPlace(StationType.Stove, 30, 3, 0));
        g.Place(StationType.Stove, 30, 3, 0, free: true);
        Assert.DoesNotContain("hut6", g.ColdZones()!);
    }

    [Fact]
    public void PeopleInAColdHutFeelIt()
    {
        var g = TestUtil.BombeLine(9, false);
        g.ActDate = new DateOnly(1940, 12, 10);
        var inHut = TestUtil.Add(g, Role.Cryptanalyst, 24.5, 5.5);
        var inMansion = TestUtil.Add(g, Role.Cryptanalyst, 9.5, 6.5);
        Assert.True(g.IsCold(inHut, g.ColdZones()));
        Assert.False(g.IsCold(inMansion, g.ColdZones()));
        g.Place(StationType.Stove, 30, 3, 0, free: true);
        Assert.False(g.IsCold(inHut, g.ColdZones()));
        g.ActDate = new DateOnly(1941, 6, 10);
        Assert.Null(g.ColdZones());
    }
}

public class TreasuryTests
{
    [Fact]
    public void HoardingCostsPartOfTheGrant()
    {
        var g = GameState.NewGame(12);
        g.Funds = 1000;
        Assert.Equal(0, g.TreasuryCut(g.WeeklyGrant));
        g.Funds = Defs.TreasuryComfortable + 1000;
        Assert.Equal((int)(1000 * Defs.TreasuryClawback), g.TreasuryCut(10000));
        g.Funds = 100000;
        Assert.Equal(g.WeeklyGrant, g.TreasuryCut(g.WeeklyGrant));
    }
}

public class FlashTests
{
    [Fact]
    public void FlashRequestsArriveAndPunishBeingIgnored()
    {
        var g = TestUtil.BombeLine(14, false);
        Contract? f = null;
        for (int i = 0; i < 24 * 8 && f == null; i++)
        {
            g.Advance(60); g.Events.Clear();
            f = g.Contracts.Find(c => c.Flash && c.State == ContractState.Offered);
        }
        Assert.NotNull(f);
        Assert.True(f!.Days < 1);
        Assert.Equal(1, g.Contracts.Count(c => c.Flash && c.State == ContractState.Offered));
        double rep = g.Reputation;
        g.Advance(Defs.FlashOfferHours * 60 + 2);
        Assert.Equal(ContractState.Expired, f.State);
        Assert.True(g.Reputation < rep);
    }

    [Fact]
    public void NoFlashRequestsEarlyOn()
    {
        var g = GameState.NewGame(14);
        for (int i = 0; i < 24 * 5; i++) { g.Advance(60); g.Events.Clear(); }
        Assert.DoesNotContain(g.Contracts, c => c.Flash);
    }
}

public class ExperienceTests
{
    [Fact]
    public void PractiseMakesPerfectAndCostsMore()
    {
        var g = TestUtil.BombeLine(15, false);
        var s = g.Staff.First(x => x.Role == Role.Linguist);
        s.Skill = 1; s.Experience = 0;
        int pay = s.Salary;
        for (int i = 0; i < 100 && s.Skill == 1; i++) g.DebugGainExperience(s, 60);
        Assert.Equal(2, s.Skill);
        Assert.True(s.Salary > pay);
        for (int i = 0; i < 400; i++) g.DebugGainExperience(s, 60);
        Assert.Equal(3, s.Skill);
    }
}

public class ScoreTests
{
    [Fact]
    public void ScoreRewardsDeliveryAndSpeedAndHonoursAreOrdered()
    {
        var g = GameState.NewGame(16);
        int baseline = g.Score();
        g.Stats.DeliveredTotal += 100;
        Assert.Equal(baseline + 1000, g.Score());
        g.Stats.ContractsFailed += 1;
        Assert.Equal(baseline + 920, g.Score());
        var hard = GameState.NewGame(16, Difficulty.Hard);
        hard.Stats.DeliveredTotal = 100;
        var easy = GameState.NewGame(16, Difficulty.Easy);
        easy.Stats.DeliveredTotal = 100;
        Assert.True(hard.Score() > easy.Score());
        Assert.Equal(GameState.Honours.OrderBy(h => h.min).Select(h => h.min), GameState.Honours.Select(h => h.min));
    }
}

public class WeatherTests
{
    [Fact]
    public void WeatherVariesWithTheSeasonAndHasEffects()
    {
        var g = GameState.NewGame(17);
        g.ActDate = new DateOnly(1940, 1, 1);
        var seen = new System.Collections.Generic.HashSet<Weather>();
        for (int i = 0; i < 40; i++) { g.Advance(Defs.MinutesPerDay); g.Events.Clear(); seen.Add(g.Weather); }
        Assert.Contains(Weather.Snow, seen);
        Assert.True(seen.Count >= 3);

        g.Weather = Weather.Snow;
        Assert.True(g.OutdoorPace < 1);
        g.Weather = Weather.Storm;
        Assert.True(g.WeatherGarble > 1);
        g.Weather = Weather.Fine;
        Assert.Equal(1, g.OutdoorPace);
    }
}

public class BilletTests
{
    [Fact]
    public void StaffBeyondTheBilletsLodgeFarAway()
    {
        var g = GameState.NewGame(18);
        Assert.Equal(Defs.StartBillets, g.Billets);
        while (g.Staff.Count < Defs.StartBillets + 3) TestUtil.Add(g, Role.Clerk);
        g.AssignBillets();
        Assert.Equal(3, g.Overcrowded);
        var newest = g.Staff.OrderByDescending(s => s.Id).Take(3).ToList();
        Assert.All(newest, s => Assert.True(s.FarBillet));
        Assert.True(newest[0].WorkRate < new Staff { Energy = newest[0].Energy, Morale = newest[0].Morale }.WorkRate);

        g.Funds = 5000;
        Assert.True(g.RequisitionBillets());
        Assert.Equal(0, g.Overcrowded);
        Assert.All(g.Staff, s => Assert.False(s.FarBillet));
    }

    [Fact]
    public void OldSavesGetEnoughBillets()
    {
        var g = GameState.NewGame(18);
        while (g.Staff.Count < 27) TestUtil.Add(g, Role.Clerk);
        g.Billets = 0;
        var copy = SaveGame.Load(SaveGame.Save(g));
        Assert.True(copy.Billets >= 27);
    }
}

public class RaidTests
{
    [Fact]
    public void TheNovember1940BombsFallAndRaidsSlowTheDesks()
    {
        var g = GameState.NewGame(19);
        g.ActDate = new DateOnly(1940, 11, 19);
        bool sawRaid = false;
        for (int i = 0; i < 2 * 24 * 4; i++)
        {
            g.Advance(15);
            if (g.RaidOn) { sawRaid = true; Assert.Equal(Defs.RaidWorkRate, g.RaidWorkFactor); }
        }
        Assert.True(sawRaid);
        Assert.Contains("bombs1940", g.Stats.ScriptedDone);
        Assert.Contains(g.Events, e => e.Text.Contains("stray stick"));
        Assert.False(g.RaidOn);
        Assert.Equal(1, g.RaidWorkFactor);
    }

    [Fact]
    public void NoRaidsOutsideTheBlitz()
    {
        var g = GameState.NewGame(19);
        g.ActDate = new DateOnly(1942, 6, 1);
        for (int i = 0; i < 20; i++) { g.Advance(Defs.MinutesPerDay); Assert.False(g.RaidOn); Assert.True(g.NextRaidAt < 0); }
    }
}

public class IllnessTests
{
    [Fact]
    public void FluSpreadsInWinter()
    {
        var g = GameState.NewGame(23);
        g.ActDate = new DateOnly(1940, 12, 1);
        while (g.Staff.Count < 18) TestUtil.Add(g, Role.Clerk);
        int maxSick = 0;
        for (int i = 0; i < 40; i++) { g.Advance(Defs.MinutesPerDay); g.Events.Clear(); maxSick = Math.Max(maxSick, g.SickCount); }
        Assert.True(maxSick > 0);
    }

    [Fact]
    public void TheSickStayHomeUntilWell()
    {
        var g = GameState.NewGame(23);
        var s = g.Staff[0];
        // Wait until they go off watch, then make them ill for two days.
        for (int i = 0; i < 96 && !s.Away; i++) g.Advance(30);
        Assert.True(s.Away);
        s.SickUntil = g.Now + 2 * Defs.MinutesPerDay;
        for (int i = 0; i < 2 * 48 - 2; i++) { g.Advance(30); Assert.True(s.Away); }
        bool back = false;
        for (int i = 0; i < 96 && !back; i++) { g.Advance(30); back = !s.Away; }
        Assert.True(back);
    }
}

public class SickBayTests
{
    [Fact]
    public void SickBayOnlyInTheMansionAndShortensIllness()
    {
        Assert.True(Defs.AllowedIn("mansion", StationType.SickBay));
        Assert.False(Defs.AllowedIn("hut6", StationType.SickBay));

        double Longest(bool bay)
        {
            var g = GameState.NewGame(31);
            g.ActDate = new DateOnly(1940, 12, 1);
            while (g.Staff.Count < 25) TestUtil.Add(g, Role.Clerk);
            if (bay) g.Stations.Add(new Station { Id = 999, Type = StationType.SickBay, X = 2, Y = 2 });
            double longest = 0;
            for (int i = 0; i < 60; i++)
            {
                double before = g.Now;
                g.Advance(Defs.MinutesPerDay);
                g.Events.Clear();
                foreach (var s in g.Staff.Where(s => s.SickUntil > before)) longest = Math.Max(longest, s.SickUntil - g.Now);
            }
            return longest;
        }
        Assert.True(Longest(true) <= 3 * Defs.MinutesPerDay + 1);
        Assert.True(Longest(false) > 3 * Defs.MinutesPerDay);
    }
}

public class VettingTests
{
    [Fact]
    public void RecruitsArriveAfterVettingButNotablesAtOnce()
    {
        var g = GameState.NewGame(37);
        g.Funds = 5000;
        var cand = g.Candidates.First(c => c.Notable == null && c.Role != Role.Messenger);
        Assert.True(g.Hire(cand.Id));
        var s = g.StaffById(cand.Id)!;
        Assert.True(s.Away);
        Assert.True(s.Arriving(g.Now));
        g.Advance(Defs.VettingHours(s.Role, g.Difficulty) * 60 - 30);
        Assert.True(s.Away);
        for (int i = 0; i < 48 && s.Away; i++) g.Advance(30);
        Assert.False(s.Away);
        Assert.Equal(0, s.ArrivesAt);
    }
}

public class AllHandsTests
{
    [Fact]
    public void AllHandsCallsEveryoneInForAFewHoursWithACooldown()
    {
        var g = GameState.NewGame(41);
        g.ActIndex = 2;
        var night = TestUtil.Add(g, Role.Clerk, shift: Shift.Night);
        // Late evening: the night watch isn't on yet.
        while (g.MinuteOfDay < 20 * 60) g.Advance(30);
        for (int i = 0; i < 4; i++) g.Advance(30);
        Assert.False(g.OnDuty(night));
        double morale = night.Morale;
        Assert.True(g.CallAllHands());
        Assert.True(g.OnDuty(night));
        Assert.True(night.Morale < morale);
        Assert.NotNull(g.CanCallAllHands());
        Assert.False(g.CallAllHands());
        g.Advance(Defs.AllHandsHours * 60 + 30);
        Assert.False(g.AllHands);
        g.Advance(Defs.AllHandsCooldownDays * Defs.MinutesPerDay);
        Assert.Null(g.CanCallAllHands());
        var copy = SaveGame.Load(SaveGame.Save(g));
        Assert.Equal(g.LastAllHandsAt, copy.LastAllHandsAt);
    }
}

public class StaleCribTests
{
    [Fact]
    public void StaleCribsSlowBombeRunsUnlessGardened()
    {
        var g = GameState.NewGame(43);
        var m = new Message { Net = Net.Dolphin, Day = g.Day };
        var bombe = new Station { Id = 900, Type = StationType.Bombe };
        var op = TestUtil.Add(g, Role.Wren);
        double normal = g.Duration(bombe, op, m);
        g.StaleUntil[Net.Dolphin] = g.Day + 2;
        Assert.True(g.Duration(bombe, op, m) > normal * 1.2);
        g.GardenUntil[Net.Dolphin] = g.Day + 2;
        Assert.Equal(normal, g.Duration(bombe, op, m), 3);
    }

    [Fact]
    public void StaleCribsTurnUpLaterInTheWar()
    {
        var g = GameState.NewGame(43);
        g.ActIndex = Defs.StaleFromAct;
        g.ActiveNets.Add(Net.Red); g.ActiveNets.Add(Net.Dolphin);
        for (int i = 0; i < 60 && g.StaleUntil.Count == 0; i++) { g.Advance(Defs.MinutesPerDay); g.Events.Clear(); }
        Assert.NotEmpty(g.StaleUntil);
    }
}

public class MinistryTests
{
    [Fact]
    public void AMinistryLeftWaitingComplainsAndCostsReputation()
    {
        var g = GameState.NewGame(47);
        g.ActIndex = 3;
        g.Contracts.Clear();
        g.Contracts.Add(new Contract { Id = 5000, Issuer = Issuer.AirMinistry, Net = Net.Red, OfferedAt = g.Now, State = ContractState.Expired });
        g.Now += 7 * Defs.MinutesPerDay;
        Assert.Contains(g.NeglectedMinistries(), x => x.issuer == Issuer.AirMinistry);
        Assert.DoesNotContain(g.NeglectedMinistries(), x => x.issuer == Issuer.Admiralty);

        // Served recently: no complaint.
        g.LastServed[Issuer.AirMinistry] = g.Now - Defs.MinutesPerDay;
        Assert.DoesNotContain(g.NeglectedMinistries(), x => x.issuer == Issuer.AirMinistry);

        // Ignored past the limit: the weekly accounts bring a complaint.
        g.LastServed.Clear();
        g.Now += 4 * Defs.MinutesPerDay;
        double rep = g.Reputation;
        g.DebugWeeklyAccounts();
        Assert.Contains(g.Events, e => e.Text.Contains("complains to the Foreign Office"));
        Assert.True(g.Reputation <= rep - Defs.NeglectRep + 0.01);
    }
}

public class WrenShortageTests
{
    [Fact]
    public void FewWrensUntilActionThisDay()
    {
        int Wrens(int act)
        {
            var g = GameState.NewGame(53);
            g.ActIndex = act;
            g.DebugRefreshCandidates();
            return g.Candidates.Count(c => c.Role == Role.Wren && c.Notable == null);
        }
        Assert.Equal(2, Wrens(3));
        Assert.Equal(1, Wrens(Defs.WrenShortageFrom));
        Assert.Equal(1, Wrens(Campaign.ActionThisDay - 1));
        Assert.Equal(3, Wrens(Campaign.ActionThisDay));
    }
}

public class RepLogTests
{
    [Fact]
    public void ReputationChangesAreLoggedByCauseAndPruned()
    {
        var g = GameState.NewGame(59);
        g.AdjustRep(-2, "Missed deadlines");
        g.AdjustRep(-1, "Missed deadlines");
        g.AdjustRep(3, "Requests completed");
        Assert.Equal(2, g.RepLog.Count);
        Assert.Equal(-3 * Defs.Difficulties[g.Difficulty].RepLossMult, g.RepLog[0].Delta, 3);
        Assert.Equal(g.RepLog[0].Delta, g.Stats.RepByCause["Missed deadlines"], 3);
        g.ActIndex = 2;
        g.Advance(20 * Defs.MinutesPerDay);
        g.Events.Clear();
        Assert.All(g.RepLog, e => Assert.True(g.Now - e.At <= 15 * Defs.MinutesPerDay));
        Assert.Contains(g.RepLog, e => e.Reason == "Whitehall's expectations");
        var copy = SaveGame.Load(SaveGame.Save(g));
        Assert.Equal(g.RepLog.Count, copy.RepLog.Count);
    }
}

public class TypexWearTests
{
    [Fact]
    public void TypexWearsAndRepairsFasterThanABombe()
    {
        Assert.True(new Station { Type = StationType.Typex }.Wears);
        Assert.True(Defs.RepairMinutesFor(StationType.Typex) < Defs.RepairMinutesFor(StationType.Bombe));
    }
}

public class HeadOfHutTests
{
    [Fact]
    public void AHeadSpeedsUpTheirHutAndWorksNoDesk()
    {
        var g = TestUtil.BombeLine(61, true);
        var crypt = TestUtil.Add(g, Role.Cryptanalyst);
        crypt.Skill = 3;
        var clerk = TestUtil.Add(g, Role.Clerk);
        clerk.Skill = 1;
        Assert.False(g.AppointHead(clerk.Id, "hut6"));
        Assert.True(g.AppointHead(crypt.Id, "hut6"));
        Assert.Equal(crypt, g.HeadOfZone("hut6"));
        Assert.Contains(g.Grid.Zone("hut6")!, g.HeadedZones());
        Assert.Single(g.HeadedZones());

        // A second appointment to the same hut replaces the first.
        var crypt2 = TestUtil.Add(g, Role.Cryptanalyst);
        crypt2.Skill = 2;
        Assert.True(g.AppointHead(crypt2.Id, "hut6"));
        Assert.Null(crypt.HeadOf);

        // Heads never take a desk job.
        for (int i = 0; i < 200; i++) { g.Advance(5); Assert.NotEqual(TaskKind.Operate, crypt2.Task?.Kind); }
    }
}

public class PriorityTests
{
    [Fact]
    public void WorkForAMissedRequestWaitsBehindLiveOnes()
    {
        var g = GameState.NewGame(67);
        var dead = new Contract { Id = 7001, Net = Net.Police, State = ContractState.Failed, Due = g.Now - 60 };
        var live = new Contract { Id = 7002, Net = Net.Police, State = ContractState.Active, Due = g.Now + 600 };
        g.Contracts.Add(dead); g.Contracts.Add(live);
        g.Messages[9001] = new Message { Id = 9001, Net = Net.Police, ContractId = dead.Id };
        g.Messages[9002] = new Message { Id = 9002, Net = Net.Police, ContractId = live.Id };
        Assert.Equal(9002, g.DebugMostUrgent(new() { 9001, 9002 }));
    }
}

public class RuthlessTests
{
    [Fact]
    public void OperationRuthlessIsOfferedOnceAndIsAGamble()
    {
        int successes = 0, failures = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            var g = GameState.NewGame(seed);
            g.DebugSkipToAct(Defs.RuthlessAct);
            g.Dilemmas.Clear();
            for (int i = 0; i < 5 && !g.Dilemmas.Any(d => d.Title == "Operation Ruthless"); i++)
            {
                g.Dilemmas.RemoveAll(d => d.Title != "Operation Ruthless");
                g.Advance(Defs.MinutesPerDay);
            }
            var r = g.Dilemmas.Single(d => d.Title == "Operation Ruthless");
            int points = g.Points;
            double sus = g.SuspicionOf(Service.Kriegsmarine);
            Assert.True(g.ResolveDilemma(r.Id, 0));
            if (g.Points >= points + Defs.RuthlessPoints) successes++;
            else { Assert.True(g.SuspicionOf(Service.Kriegsmarine) > sus); failures++; }
            g.Advance(3 * Defs.MinutesPerDay);
            Assert.DoesNotContain(g.Dilemmas, d => d.Title == "Operation Ruthless");
        }
        Assert.True(successes > 0 && failures > 0);
    }
}

public class InterceptTests
{
    [Fact]
    public void EnigmaMatchesTheKnownTestVector()
    {
        var m = new Enigma(new[] { 0, 1, 2 }, new[] { 0, 0, 0 }, System.Array.Empty<(char, char)>());
        m.SetPositions("AAA");
        Assert.Equal("BDZGO", m.Encipher("AAAAA"));
    }

    [Fact]
    public void EnigmaDoubleStepsTheMiddleRotor()
    {
        // Rotors I II III at ADU: the middle rotor steps twice in a row (ADU -> ADV -> AEW -> BFX).
        var m = new Enigma(new[] { 0, 1, 2 }, new[] { 0, 0, 0 }, System.Array.Empty<(char, char)>());
        m.SetPositions("ADU");
        Assert.Equal("EQIBM", m.Encipher("AAAAA"));
    }

    [Theory]
    [InlineData(Net.Red)]
    [InlineData(Net.Shark)]
    public void TheSameSettingsDecipherWhatTheyEncipher(Net net)
    {
        const string text = "ANBDUXGELEITZUGQUADRATAJEINSZWODREIVIER";
        var a = Enigma.DailyKey(5, net, 12); a.SetPositions("QWE");
        var c = a.Encipher(text);
        Assert.NotEqual(text, c);
        Assert.DoesNotContain(c.Where((ch, i) => ch == text[i]), _ => true);   // Enigma never enciphers a letter as itself
        var b = Enigma.DailyKey(5, net, 12); b.SetPositions("QWE");
        Assert.Equal(text, b.Encipher(c));
    }

    [Fact]
    public void EveryNetworkReadsTheSameEachTimeAndHasAllThreeTexts()
    {
        var g = GameState.NewGame(3);
        foreach (var net in Enum.GetValues<Net>())
        {
            var m = new Message { Id = 4242, Net = net, Day = 3, Created = 3 * Defs.MinutesPerDay + 600 };
            var x = g.Intercept(m);
            var y = g.Intercept(m);
            Assert.Equal(x, y);
            Assert.False(string.IsNullOrWhiteSpace(x.Cipher));
            Assert.False(string.IsNullOrWhiteSpace(x.German));
            Assert.False(string.IsNullOrWhiteSpace(x.English));
            Assert.Contains("1000", x.Log);   // time of intercept
        }
    }

    [Fact]
    public void DeliveredMessagesAreArchivedAndStagesAdvance()
    {
        var m = new Message { Next = StationType.Typex };
        Assert.Equal(ReadStage.Intercept, Intercepts.StageOf(m));
        m.Next = StationType.Translation;
        Assert.Equal(ReadStage.Decrypted, Intercepts.StageOf(m));
        m.Next = StationType.Dispatch;
        Assert.Equal(ReadStage.Translated, Intercepts.StageOf(m));

        var g = GameState.NewGame(9);
        var ai = new AutoPlayer(g);
        for (int i = 0; i < 6 * 24; i++) { if (g.ActCompletePending) g.ContinueAfterAct(); ai.Think(); g.Advance(60); }
        Assert.InRange(g.Archive.Count, 1, GameState.ArchiveSize);
        Assert.Contains("\n", g.Intercept(g.Archive[^1]).English);
    }
}

public class WartimeInterceptTests
{
    static System.Collections.Generic.List<InterceptText> Many(Net net, DateOnly date) =>
        Enumerable.Range(0, 300).Select(i => Intercepts.For(11, i, net, 5, date, 600, false)).ToList();

    [Fact]
    public void TheTrafficFitsTheDate()
    {
        var phoney = Many(Net.Red, new DateOnly(1939, 10, 15));
        Assert.DoesNotContain(phoney, x => x.German.Contains("LONDON") || x.German.Contains("CHARTRES") || x.German.Contains("DUENKIRCHEN"));
        Assert.Contains(phoney, x => x.German.Contains("SCAPA FLOW"));

        var coventry = Many(Net.Red, new DateOnly(1940, 11, 12));
        Assert.Contains(coventry, x => x.German.Contains("MONDSCHEINSONATE") && x.English.Contains("Moonlight Sonata"));

        var eagle = Many(Net.Red, new DateOnly(1940, 8, 18));
        Assert.Contains(eagle, x => x.German.Contains("BIGGIN HILL") || x.German.Contains("KENLEY") || x.German.Contains("MANSTON"));

        Assert.Contains(Many(Net.Dolphin, new DateOnly(1941, 5, 25)), x => x.German.Contains("BISMARCK"));
        Assert.DoesNotContain(Many(Net.Dolphin, new DateOnly(1941, 9, 1)), x => x.German.Contains("BISMARCK"));
        Assert.Contains(Many(Net.Chaffinch, new DateOnly(1942, 11, 10)), x => x.English.Contains("Algeria"));
        Assert.DoesNotContain(Many(Net.Chaffinch, new DateOnly(1941, 6, 1)), x => x.English.Contains("Algeria"));
    }
}

public class LayoutTests
{
    static GameState Run(int[] xs, out Contract c, int y = 3)
    {
        var g = GameState.NewGame(7);
        Assert.NotNull(g.Place(StationType.Registration, xs[0], y, 0));
        Assert.NotNull(g.Place(StationType.HandCipher, xs[1], y, 0));
        Assert.NotNull(g.Place(StationType.Translation, xs[2], y, 0));
        g.Advance(1);
        c = g.Contracts.First(k => k.State == ContractState.Offered);
        Assert.True(g.Accept(c.Id));
        for (int t = 0; t < 3 * Defs.MinutesPerDay; t += 30) g.Advance(30);
        return g;
    }

    [Fact]
    public void NeighbouringDesksHandWorkAcross()
    {
        var tight = Run(new[] { 11, 13, 15 }, out var c, y: 6);   // side by side in the Ballroom
        Assert.True(tight.Stats.HandOvers > 0, "desks side by side should pass paper across");
        Assert.Equal(ContractState.Completed, c.State);

        // A new game comes with its own starting desks, so compare rather than expect none.
        var spread = Run(new[] { 4, 9, 14 }, out _);
        Assert.True(tight.Stats.HandOvers > spread.Stats.HandOvers, $"tight {tight.Stats.HandOvers} vs spread {spread.Stats.HandOvers}");
    }

    [Fact]
    public void CrowdedTilesSlowWalking()
    {
        var g = GameState.NewGame(7);
        var tile = (9, 8);
        Assert.Equal(1.0, g.CrowdingAt(tile), 3);
        foreach (var r in new[] { Role.Clerk, Role.Clerk, Role.Clerk })
        {
            var p = g.NewPerson(r, 1); p.X = 9.5; p.Y = 8.5; g.Staff.Add(p);
        }
        Assert.True(g.CrowdingAt(tile) < 0.8);
    }
}

public class InteriorTests
{
    [Fact]
    public void BuildingsHaveRoomsAndWalls()
    {
        var g = GameState.NewGame(7);
        Assert.Equal("Library", g.Grid.RoomAt(4, 4)!.Name);
        Assert.Equal("Decoding Room", g.Grid.RoomAt(28, 4)!.Name);
        // Library and Drawing Room share a wall except at the doorway on row 5.
        Assert.True(g.Grid.Blocked(7, 3, 8, 3));
        Assert.False(g.Grid.Blocked(7, 5, 8, 5));
        // Desks can't straddle a partition or sit in the hall.
        Assert.Equal("Can't stand across a partition wall", g.CanPlace(StationType.Registration, 7, 3, 0));
        Assert.Contains("clear", g.CanPlace(StationType.Registration, 8, 8, 0));
        // Walking from Library to Ballroom goes through the doorways, never through a wall.
        var path = Pathfinder.Find(g.Grid, (4, 3), (14, 3))!;
        var prev = (4, 3);
        foreach (var p in path) { Assert.False(g.Grid.Blocked(prev.Item1, prev.Item2, p.x, p.y)); prev = p; }
        Assert.True(path.Count > 10);
    }

    [Fact]
    public void OldSavesGetRoomsWithoutWallingInDesks()
    {
        var g = GameState.NewGame(7);
        foreach (var z in g.Grid.Zones) { z.Rooms.Clear(); z.Partitions.Clear(); }
        g.Grid.FitInteriors(g.Stations);   // reset walls
        foreach (var z in g.Grid.Zones) { z.Rooms.Clear(); z.Partitions.Clear(); }
        var st = g.Place(StationType.Registration, 7, 3, 0, free: true);   // across the future Library wall
        Assert.NotNull(st);
        var loaded = SaveGame.Load(SaveGame.Save(g));
        Assert.NotEmpty(loaded.Grid.Zone("mansion")!.Rooms);
        Assert.False(loaded.Grid.Blocked(7, 3, 8, 3));
    }
}

public class TiredOperatorTests
{
    [Fact]
    public void ATiredCryptanalystWithNoCanteenStillGetsBackToWork()
    {
        // A player's early Mansion: no canteen or common room yet.
        var g = GameState.NewGame(7);
        g.Place(StationType.Registration, 4, 3, 0, free: true);
        var hand = g.Place(StationType.HandCipher, 6, 3, 0, free: true)!;
        g.Place(StationType.Translation, 11, 3, 0, free: true);
        Assert.DoesNotContain(g.Stations, s => s.IsRest);
        var c = g.AddScripted(Net.Police, 4, 4, 0, 0, "t", "t", "t");
        // Work arrives at the desk; then the cryptanalyst flags just as a message is finished.
        for (int t = 0; t < 8 * 60 && hand.Input.Count == 0; t += 5) g.Advance(5);
        Assert.NotEmpty(hand.Input);
        foreach (var s in g.Staff.Where(s => s.Role == Role.Cryptanalyst)) s.Energy = Defs.BreakAt - 0.02;
        hand.Current = null;
        for (int t = 0; t < 3 * 60; t += 5) g.Advance(5);
        Assert.True(hand.Processed > 0 || hand.WorkDone > 0, $"the hand-cipher desk never got going again: now {g.Now} in {hand.Input.Count} op {hand.OperatorId} " +
            string.Join("; ", g.Staff.Select(s => $"{s.Role} E{s.Energy:0.00} {s.Task?.Kind} away {s.Away} at {s.TilePos}")) + " msgs " + string.Join(",", g.Messages.Values.Select(m => $"{m.Loc}/{m.Next}")) + $" contract {c.State}");
    }
}

public class IndexTests
{
    [Fact]
    public void MachineDecryptsAreIndexedFromTheBattleOfBritain()
    {
        var g = GameState.NewGame(3);
        g.DebugSkipToAct(Defs.IndexFromAct - 1);
        Assert.False(g.NeedsIndex(new Message { Net = Net.Red }));
        g.DebugSkipToAct(Defs.IndexFromAct);
        Assert.True(g.NeedsIndex(new Message { Net = Net.Red }));
        Assert.True(g.NeedsIndex(new Message { Net = Net.Tunny }));
        Assert.False(g.NeedsIndex(new Message { Net = Net.Police }));
    }

    [Fact]
    public void IndexedRedTrafficStillGetsThrough()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        g.AddScripted(Net.Red, 8, 4, 0, 0, "t", "t", "t");
        TestUtil.Run(g, 3);
        var index = g.Stations.Single(s => s.Type == StationType.Index);
        Assert.True(index.Processed > 0, "nothing was indexed");
        Assert.True(Stats.Get(g.Stats.Delivered, Net.Red) > 0);
    }
}

public class PostingTests
{
    [Fact]
    public void PostedStaffStayInTheirBuilding()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var clerk = g.Staff.First(s => s.Role == Role.Clerk);
        Assert.True(g.Post(clerk.Id, "hut6"));
        Assert.False(g.Post(clerk.Id, "blockf"));   // not open
        TestUtil.Add(g, Role.Messenger);
        g.AddScripted(Net.Red, 8, 4, 0, 0, "t", "t", "t");
        int outside = 0;
        for (int t = 0; t < 3 * Defs.MinutesPerDay; t += 5)
        {
            g.Advance(5);
            g.Events.Clear();
            if (!clerk.Away && clerk.Task != null && clerk.Path.Count == 0 && g.Grid.ZoneAt(clerk.TilePos.x, clerk.TilePos.y)?.Id is string z && z != "hut6") outside++;
        }
        Assert.Equal(0, outside);
        Assert.True(Stats.Get(g.Stats.Delivered, Net.Red) > 0, "the rest of the Park still gets RED through");
        Assert.True(g.Post(clerk.Id, null));
        Assert.Null(clerk.Posted);
    }

    [Fact]
    public void TeleprintersTakeTheNetworksRoutedToThem()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var hut6Tp = g.Place(StationType.Inbox, 28, 3, 0, free: true)!;
        var mansionTp = g.Stations.First(s => s.Type == StationType.Inbox && s != hut6Tp);
        foreach (var n in Enum.GetValues<Net>()) g.SetReceives(mansionTp.Id, n, n != Net.Red);
        foreach (var n in Enum.GetValues<Net>()) g.SetReceives(hut6Tp.Id, n, n == Net.Red);
        Assert.True(hut6Tp.ReceivesNet(Net.Red) && !hut6Tp.ReceivesNet(Net.Police));
        g.AddScripted(Net.Red, 6, 4, 0, 0, "t", "t", "t");
        g.AddScripted(Net.Police, 4, 4, 0, 0, "t", "t", "t");
        TestUtil.Run(g, 0.5);
        var red = g.Messages.Values.Where(m => m.Net == Net.Red).ToList();
        var police = g.Messages.Values.Where(m => m.Net == Net.Police).ToList();
        Assert.NotEmpty(red);
        Assert.NotEmpty(police);
        // Where each first came in: messages carry no history, so check none of the RED is in the Mansion teleprinter's tray.
        Assert.DoesNotContain(mansionTp.Output, id => g.Messages[id].Net == Net.Red);
        Assert.DoesNotContain(hut6Tp.Output, id => g.Messages[id].Net == Net.Police);
        // Ticking every network back on means "everything" again.
        foreach (var n in Enum.GetValues<Net>()) g.SetReceives(hut6Tp.Id, n, true);
        Assert.Null(hut6Tp.Receives);
    }
}

public class WarMapTests
{
    [Fact]
    public void RequestsBecomeOperationsThatAreWonOrLost()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        int grant0 = g.WeeklyGrant;
        var c = g.AddScripted(Net.Red, 2, 4, 0, 0, "t", "t", "t");
        var op = g.OperationFor(c.Id);
        Assert.NotNull(op);
        Assert.Equal(OpState.Pending, op!.State);
        Assert.False(string.IsNullOrEmpty(op.Won) || string.IsNullOrEmpty(op.Lost));
        TestUtil.Run(g, 4);
        Assert.NotEqual(OpState.Pending, op.State);
        if (op.State == OpState.Won)
        {
            // Left undecided, the policy picks after the decision window.
            Assert.True(op.Use != OpUse.Undecided || op.DecideBy > g.Now);
            TestUtil.Run(g, Defs.OpDecideHours / 24 + 0.05);
            Assert.NotEqual(OpUse.Undecided, op.Use);
            Assert.True(g.WarMood > 1, "a win acted on lifts the grant");
        }
        // An impossible request is lost, and the loss is counted.
        var lost = g.AddScripted(Net.Shark, 50, 0.2, 0, 0, "t2", "t", "t");
        TestUtil.Run(g, 1);
        Assert.Equal(OpState.Lost, g.OperationFor(lost.Id)!.State);
        Assert.True(g.War.ShipsSunk + g.War.RaidsUnopposed + g.War.BattlesSurprised > 0);
    }

    [Fact]
    public void OperationsSurviveSaving()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var c = g.AddScripted(Net.Red, 2, 4, 0, 0, "t", "t", "t");
        var back = SaveGame.Load(SaveGame.Save(g));
        Assert.Equal(g.OperationFor(c.Id)!.Name, back.OperationFor(c.Id)!.Name);
    }
}

public class KeyOrderTests
{
    [Fact]
    public void HeldKeysAreNotAttempted()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        g.SetKeyOrder(Net.Red, KeyOrder.Hold);
        g.AddScripted(Net.Red, 4, 4, 0, 0, "t", "t", "t");
        TestUtil.Run(g, 0.6);
        Assert.Equal(KeyStatus.None, g.KeyState(Net.Red, g.Day));
        Assert.Equal(0, Stats.Get(g.Stats.KeyBreaks, Net.Red));
        g.SetKeyOrder(Net.Red, KeyOrder.Normal);
        Assert.Empty(g.KeyOrders);
        TestUtil.Run(g, 1.5);
        Assert.True(Stats.Get(g.Stats.KeyBreaks, Net.Red) > 0);
    }

    [Fact]
    public void FirstJumpsTheBombeQueue()
    {
        // Two networks' key attempts sit in one Bombe's tray: the one put First is run first,
        // even though the other's request is due sooner.
        var g = TestUtil.BombeLine(12, checking: true);
        var bombe = g.Stations.First(s => s.Type == StationType.Bombe);
        var a = new Message { Id = g.NextId++, Net = Net.Red, Day = g.Day, Carrier = true, Loc = Loc.Input, LocId = bombe.Id, Next = StationType.Bombe };
        var b = new Message { Id = g.NextId++, Net = Net.LightBlue, Day = g.Day, Carrier = true, Loc = Loc.Input, LocId = bombe.Id, Next = StationType.Bombe };
        g.Messages[a.Id] = a; g.Messages[b.Id] = b;
        bombe.Input.Add(a.Id); bombe.Input.Add(b.Id);
        g.SetKeyOrder(Net.LightBlue, KeyOrder.First);
        for (int i = 0; i < 200 && bombe.Current == null; i++) g.Advance(1);
        Assert.Equal(b.Id, bombe.Current);
    }
}

public class CrisisTests
{
    [Fact]
    public void PowerCutStopsMachinesUnlessTheGeneratorRunsTheBombes()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        g.PowerCutAt = g.Now + 1;
        g.Advance(3);
        Assert.True(g.PowerCut);
        var d = g.Dilemmas.Single(x => x.Title == "Power cut");
        var bombe = g.Stations.First(s => s.Type == StationType.Bombe);
        var typex = g.Stations.First(s => s.Type == StationType.Typex);
        Assert.True(g.Unpowered(bombe) && g.Unpowered(typex));
        int funds = g.Funds;
        g.ResolveDilemma(d.Id, 0);
        Assert.Equal(funds - Defs.GeneratorCost, g.Funds);
        Assert.False(g.Unpowered(bombe));
        Assert.True(g.Unpowered(typex));
        TestUtil.Run(g, 0.4);
        Assert.False(g.PowerCut);
        Assert.False(g.Generator);
        Assert.False(g.Unpowered(typex));
    }

    [Fact]
    public void SurgeBringsUrgentRequestsAndOvertimeSpeedsWork()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        g.SurgeAt = g.Now + 1;
        g.Advance(3);
        var d = g.Dilemmas.Single(x => x.Options.Any(o => o.Action == "overtime"));
        Assert.Contains(g.Contracts, c => c.Flash && c.State == ContractState.Offered);
        g.ResolveDilemma(d.Id, d.Options.FindIndex(o => o.Action == "overtime"));
        Assert.True(g.Overtime);
        Assert.Equal(Defs.OvertimeRate, g.OvertimeFactor);
        TestUtil.Run(g, Defs.OvertimeDays + 0.1);
        Assert.False(g.Overtime);
    }
}

public class StoryTests
{
    [Fact]
    public void StrainWarnsThenRestHelps()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var s = g.Staff.First(x => x.Role == Role.Cryptanalyst);
        s.Strain = 0.7;
        s.LowToday = 0.05;
        g.Advance(Defs.MinutesPerDay - g.MinuteOfDay + 1);
        var d = g.Dilemmas.Single(x => x.StaffId == s.Id);
        Assert.True(s.StrainWarned);
        g.ResolveDilemma(d.Id, 0);
        Assert.True(s.IsSick(g.Now));
        Assert.Equal("leave", s.OffReason);
        Assert.True(s.Strain < 0.5);
    }

    [Fact]
    public void KeptOnTheyBreakDown()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var s = g.Staff.First(x => x.Role == Role.Cryptanalyst);
        s.StrainWarned = true;
        for (int day = 0; day < 20 && s.OffReason != "breakdown"; day++)
        {
            s.Strain = 0.95; s.LowToday = 0.05;
            g.Advance(Defs.MinutesPerDay);
            g.Dilemmas.Clear();
        }
        Assert.Equal("breakdown", s.OffReason);
        Assert.True(s.SickUntil - g.Now > (Defs.BreakdownDays - 1.1) * Defs.MinutesPerDay);
    }

    [Fact]
    public void RivalsInOneRoomWorkSlower()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var a = TestUtil.Add(g, Role.Cryptanalyst, 21.5, 4.5);
        var b = TestUtil.Add(g, Role.Cryptanalyst, 22.5, 4.5);
        a.Rival = b.Id; b.Rival = a.Id; a.RivalUntilDay = b.RivalUntilDay = g.Day + 30;
        Assert.True(g.WithRival(a));
        b.X = 4.5; b.Y = 4.5;   // across the Park in the Mansion
        Assert.False(g.WithRival(a));
    }
}

public class HeadStyleTests
{
    [Fact]
    public void StylesChangeTheirHut()
    {
        var g = TestUtil.BombeLine(61, true);
        var crypt = TestUtil.Add(g, Role.Cryptanalyst, 22.5, 4.5);
        crypt.Skill = 3;
        Assert.True(g.AppointHead(crypt.Id, "hut6"));
        Assert.Equal(HeadStyle.Teacher, crypt.HeadStyle);   // skill 3, no stronger hint
        Assert.True(g.SetHeadStyle(crypt.Id, HeadStyle.Driver));
        Assert.Equal(1.18, g.HeadIn(g.Grid.Zone("hut6"))!.Work);
        Assert.Null(g.HeadIn(g.Grid.Zone("mansion")));
    }

    [Fact]
    public void AShepherdLiftsTheirHutsMorale()
    {
        var g = TestUtil.BombeLine(61, true);
        var crypt = TestUtil.Add(g, Role.Cryptanalyst, 22.5, 4.5);
        crypt.Skill = 3;
        g.AppointHead(crypt.Id, "hut6");
        g.SetHeadStyle(crypt.Id, HeadStyle.Shepherd);
        var a = TestUtil.Add(g, Role.Linguist);
        var b = TestUtil.Add(g, Role.Linguist);
        a.LastZone = "hut6";
        b.LastZone = "mansion";
        g.Advance(Defs.MinutesPerDay - g.MinuteOfDay - 1);
        a.Morale = b.Morale = 0.5;
        a.LastZone = "hut6"; b.LastZone = "mansion";
        g.Advance(3);
        Assert.True(a.Morale > b.Morale + 0.03, $"{a.Morale} vs {b.Morale}");
    }
}

public class StandingOrderTests
{
    [Fact]
    public void MilestonesPayDailyAndMissesCostReputation()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var c = g.MakeStanding(new ContractTemplate(Net.Red, 6, 6, 3, 3));
        Assert.True(c.Standing);
        Assert.Equal(c.PerDay * Defs.StandingDays, c.Quantity);
        g.Contracts.Add(c);
        Assert.True(g.Accept(c.Id));
        // Spread over the days: no more than a day's quota (plus one) comes in on the first day.
        Assert.True(c.SpawnTimes.Count(t => t < c.AcceptedAt + Defs.MinutesPerDay) <= c.PerDay);
        TestUtil.Run(g, 1.0 + Defs.MilestoneGraceDays + 0.02);
        Assert.Equal(1, c.MilestonesChecked);
        // Force a miss on day 2.
        c.Delivered = 0;
        int before = c.MilestonesMet;
        TestUtil.Run(g, 1.0);
        Assert.Equal(2, c.MilestonesChecked);
        Assert.Equal(before, c.MilestonesMet);
    }

    [Fact]
    public void FinishingEarlyCountsTheRemainingDays()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var c = g.MakeStanding(new ContractTemplate(Net.Red, 6, 6, 3, 3));
        g.Contracts.Add(c);
        g.Accept(c.Id);
        int funds = g.Funds;
        c.Delivered = c.Quantity;
        g.Advance(3);
        Assert.Equal(ContractState.Completed, c.State);
        Assert.Equal(c.TotalDays, c.MilestonesMet);
        Assert.True(g.Funds >= funds + c.MilestonePay * c.TotalDays);
    }
}

public class UltraUseTests
{
    static (GameState g, Operation op) WonOp()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        var c = g.AddScripted(Net.Red, 1, 4, 0, 0, "t", "t", "t");
        var op = g.OperationFor(c.Id)!;
        c.Delivered = c.Quantity;
        g.Advance(3);
        Assert.True(op.AwaitingUse);
        return (g, op);
    }

    [Fact]
    public void ActingDrawsSuspicionCoverLittleHoldingBackNone()
    {
        var (g, op) = WonOp();
        double s0 = g.SuspicionOf(Service.Luftwaffe);
        int ships = g.War.ShipsSaved, downed = g.War.BombersDowned, warned = g.War.BattlesWarned;
        Assert.True(g.UseOperation(op.Id, OpUse.Acted));
        Assert.True(g.SuspicionOf(Service.Luftwaffe) > s0 + Defs.OpActSuspicion * 0.5);
        Assert.True(g.War.ShipsSaved + g.War.BombersDowned + g.War.BattlesWarned > ships + downed + warned);
        Assert.False(g.UseOperation(op.Id, OpUse.Withheld));   // decided once

        var (g2, op2) = WonOp();
        int cover = g2.CoverLeft;
        Assert.True(g2.UseOperation(op2.Id, OpUse.Covered));
        Assert.Equal(cover - 1, g2.CoverLeft);
        Assert.True(g2.SuspicionOf(Service.Luftwaffe) < Defs.OpActSuspicion);

        var (g3, op3) = WonOp();
        double s3 = g3.SuspicionOf(Service.Luftwaffe);
        Assert.True(g3.UseOperation(op3.Id, OpUse.Withheld));
        Assert.Equal(s3, g3.SuspicionOf(Service.Luftwaffe));
        Assert.Equal(1, g3.Stats.OpsWithheld);
        Assert.Equal(1.0, g3.WarMood);
    }

    [Fact]
    public void CoverRunsOut()
    {
        var (g, op) = WonOp();
        g.CoverLeft = 0;
        Assert.False(g.UseOperation(op.Id, OpUse.Covered));
        Assert.Equal(OpUse.Acted, g.PolicyUse);
        g.SetPolicy(Policy.Cautious);
        Assert.Equal(OpUse.Withheld, g.PolicyUse);
    }
}

public class CalendarTests
{
    [Fact]
    public void DaysFromEarlierActsKeepTheirDates()
    {
        var g = GameState.NewGame(5);
        g.Advance(Defs.MinutesPerDay * 2 + 60);
        int early = g.Day;
        var dateThen = g.DateOf(early);
        g.Advance(Defs.MinutesPerDay);
        g.DebugSkipToAct(5);
        Assert.True(g.Date > dateThen.AddDays(30), "the calendar jumped");
        Assert.Equal(dateThen, g.DateOf(early));
        Assert.Equal(g.Date, g.DateOf(g.Day));
    }
}

public class SixtaTests
{
    [Fact]
    public void TrafficAnalysisSpeedsTheDaysCribs()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        Station? ta = null;
        foreach (var (x, y) in new[] { (26, 3), (28, 3), (26, 5), (21, 3), (4, 3), (12, 3) })
            if ((ta = g.Place(StationType.TrafficAnalysis, x, y, 0, free: true)) != null) break;
        Assert.NotNull(ta);
        g.AddScripted(Net.Red, 6, 4, 0, 0, "t", "t", "t");
        TestUtil.Run(g, 1.0);
        Assert.True(ta!.Processed >= Defs.SixtaNeeded, $"logged {ta.Processed}");
        Assert.Contains(g.SixtaLogged, kv => kv.Value >= Defs.SixtaNeeded);
        TestUtil.Run(g, 1.5);
        Assert.True(Stats.Get(g.Stats.Delivered, Net.Red) > 0, "traffic still gets through");
        // No more than needed go to the analysts each day.
        Assert.All(g.SixtaRouted.Values, v => Assert.True(v <= Defs.SixtaNeeded));
    }
}

public class HeathRobinsonTests
{
    [Fact]
    public void HeathRobinsonBreaksTunnyBeforeColossus()
    {
        var g = GameState.NewGame(13);
        g.DebugSkipToAct(12);
        g.Researched.Add(ResearchId.Turingery);
        g.Grid.Zone("blockf")!.Unlocked = true;
        foreach (var r in new[] { Role.Wren, Role.Wren, Role.Cryptanalyst, Role.Linguist, Role.Clerk }) TestUtil.Add(g, r);
        g.Place(StationType.Registration, 5, 3, 0, free: true);
        g.Place(StationType.Translation, 11, 3, 0, free: true);
        g.Place(StationType.Testery, 21, 27, 0, free: true);
        g.Place(StationType.TunnyMachine, 25, 27, 0, free: true);
        g.Place(StationType.Index, 14, 8, 0, free: true);
        Assert.False(g.StationUnlocked(StationType.HeathRobinson, out _));   // needs the research
        g.Researched.Add(ResearchId.HeathRobinson);
        var hr = g.Place(StationType.HeathRobinson, 32, 27, 0, free: true);
        Assert.NotNull(hr);
        Assert.Equal(StationType.HeathRobinson, g.KeyRoute(Net.Tunny));
        Assert.True(hr!.IsMachine && hr.Wears);
        g.AddScripted(Net.Tunny, 5, 5, 0, 0, "t", "t", "t");
        TestUtil.Run(g, 3);
        Assert.True(Stats.Get(g.Stats.BombeBreaks, Net.Tunny) > 0, "broken by machine");
    }
}

public class CribPuzzleTests
{
    [Fact]
    public void EveryPuzzleHasExactlyOneClearPosition()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            var p = CribPuzzle.Make(seed, seed % 2 == 0 ? Net.Red : Net.Dolphin);
            var clear = Enumerable.Range(0, p.Positions).Where(p.Possible).ToList();
            Assert.Single(clear);
            Assert.Equal(p.Answer, clear[0]);
            Assert.True(p.Cipher.Length > p.Crib.Length + 5);
        }
    }

    [Fact]
    public void DraggingTheCribFinishesTheMenu()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        g.AddScripted(Net.Red, 4, 4, 0, 0, "t", "t", "t");
        var crib = g.Stations.First(s => s.Type == StationType.Crib);
        for (int i = 0; i < 2000 && g.PuzzleFor(crib.Id) == null; i++) g.Advance(1);
        var p = g.PuzzleFor(crib.Id);
        Assert.NotNull(p);
        double total = crib.WorkTotal;
        int wrong = p!.Answer == 0 ? 1 : 0;
        Assert.False(g.DragCrib(crib.Id, wrong));
        Assert.Equal(total + Defs.WrongDragMinutes, crib.WorkTotal);
        Assert.True(g.DragCrib(crib.Id, p.Answer));
        Assert.Equal(crib.WorkTotal, crib.WorkDone);
        Assert.Null(g.PuzzleFor(crib.Id));
        Assert.Equal(1, g.Stats.CribsDragged);
    }
}

public class InspectionTests
{
    [Fact]
    public void VisitsAreAnnouncedThenJudged()
    {
        var g = GameState.NewGame(21);
        g.DebugSkipToAct(Defs.InspectionsFromAct);
        int announced = 0, judged = 0;
        for (int i = 0; i < 24 * 30 && judged == 0; i++)
        {
            bool before = g.InspectionDue;
            g.Advance(60);
            if (!before && g.InspectionDue) announced++;
            if (g.Stats.InspectionsPassed + g.Stats.InspectionsFailed > 0 || (before && !g.InspectionDue)) judged++;
        }
        Assert.True(announced >= 1, "a visit was announced");
        Assert.True(judged >= 1, "and then happened");
    }

    [Fact]
    public void AShamblesScoresBadlyAndATidyParkWell()
    {
        var g = TestUtil.BombeLine(12, checking: true);
        foreach (var s in g.Staff) s.Morale = 0.8;
        g.Funds = 500;
        Assert.True(g.InspectionVerdict().score >= 1);
        g.Funds = -200;
        foreach (var s in g.Staff) s.Morale = 0.2;
        foreach (var st in g.Stations.Where(s => s.Wears)) st.DownUntil = g.Now + 600;
        Assert.True(g.InspectionVerdict().score <= -2);
    }

    [Fact]
    public void InspectionScheduleIsReproducible()
    {
        double At(int seed)
        {
            var g = GameState.NewGame(seed);
            g.DebugSkipToAct(Defs.InspectionsFromAct);
            for (int i = 0; i < 24 * 20 && !g.InspectionDue; i++) g.Advance(60);
            return g.InspectionAt;
        }
        Assert.Equal(At(5), At(5));
    }
}

public class BanburyPuzzleTests
{
    [Fact]
    public void TheDepthStandsOut()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            var p = BanburyPuzzle.Make(seed);
            int best = Enumerable.Range(0, BanburyPuzzle.MaxOffset + 1).OrderByDescending(o => p.Repeats(o).Count()).First();
            Assert.Equal(p.Answer, best);
            Assert.True(p.Repeats(p.Answer).Count() >= 7);
            Assert.All(Enumerable.Range(0, BanburyPuzzle.MaxOffset + 1).Where(o => o != p.Answer), o => Assert.True(p.Repeats(o).Count() <= 3));
        }
    }
}

public class OffizierTests(ITestOutputHelper output)
{
    [Fact]
    public void SomeNavalTrafficGoesBackForItsInnerLayer()
    {
        var g = GameState.NewGame(31);
        g.DebugSkipToAct(Campaign.OffizierFrom);
        int offizier = 0, naval = 0;
        for (int id = 1; id <= 400; id++)
        {
            var m = new Message { Id = id, Net = Net.Dolphin };
            naval++;
            if (new System.Random(GameState.Mix(g.Seed, m.Id, 0x0FF1)).NextDouble() < Defs.OffizierShare) offizier++;
        }
        // About a fifth, decided by a stable per-message dice
        Assert.InRange(offizier / (double)naval, 0.14, 0.26);
    }

    [Fact]
    public void OffizierMessagesAreReadAndDelivered()
    {
        var g = GameState.NewGame(31);
        var ai = new AutoPlayer(g);
        for (int h = 0; h < 24 * 200 && g.Status == GameStatus.Playing && (g.ActIndex < Campaign.OffizierFrom || g.Stats.OffizierRead == 0); h++)
        {
            ai.Think();
            g.Advance(60);
            if (g.ActCompletePending) g.ContinueAfterAct();
        }
        output.WriteLine($"act {g.ActIndex}, day {g.Day}, offizier read {g.Stats.OffizierRead}");
        Assert.True(g.Stats.OffizierRead > 0);
    }
}

public class HandbookTests
{
    [Fact]
    public void EveryStepIsARealStationAndEveryNetIsCovered()
    {
        foreach (var p in Handbook.Paths)
            foreach (var route in p.Routes)
                foreach (var step in route)
                    Assert.True(Defs.Stations.ContainsKey(step.Type), $"{p.Title}: {step.Type}");
        foreach (Net n in Enum.GetValues<Net>())
            Assert.Contains(Handbook.Paths, p => p.Nets.Contains(n));
    }
}

public class StaleIntelTests
{
    [Fact]
    public void LateDecryptsEarnLess()
    {
        var g = GameState.NewGame(5);
        var m = new Message { Id = 1, Net = Net.Police, Created = g.Now };
        int full = Defs.NetOf(Net.Police).Points;
        Assert.Equal(full, g.PointsFor(m));
        m.Created = g.Now - Defs.StaleIntelAfter - 1;
        Assert.Equal(full / 2, g.PointsFor(m));
        m.Created = g.Now - Defs.DeadIntelAfter - 1;
        Assert.Equal(0, g.PointsFor(m));
    }
}

public class TypexSlipTests
{
    [Fact]
    public void GreenAndTiredWrensSlipMore()
    {
        var expert = new Staff { Role = Role.Wren, Skill = 3, Energy = 1 };
        var green = new Staff { Role = Role.Wren, Skill = 1, Energy = 1 };
        var tired = new Staff { Role = Role.Wren, Skill = 1, Energy = 0.1 };
        Assert.True(GameState.TypexSlipChance(expert) < GameState.TypexSlipChance(green));
        Assert.True(GameState.TypexSlipChance(green) < GameState.TypexSlipChance(tired));
        green.Specialties.Add(StationType.Typex);
        Assert.Equal(0.035, GameState.TypexSlipChance(green), 6);
    }
}

public class KeyBoardTests
{
    [Fact]
    public void NetsWithoutTrafficDropOffButSharkStays()
    {
        var g = GameState.NewGame(3);
        int claymore = Array.FindIndex(Campaign.Acts, a => a.Title == "Operation Claymore");
        g.DebugSkipToAct(claymore);
        Assert.Contains(Net.Brown, g.KeyBoardNets());
        int blackout = Array.FindIndex(Campaign.Acts, a => a.Title == "The Blackout");
        g.DebugSkipToAct(blackout);
        g.Contracts.Clear();
        g.Messages.Clear();
        var nets = g.KeyBoardNets().ToList();
        Assert.DoesNotContain(Net.Brown, nets);
        Assert.Contains(Net.Shark, nets);
        // Listed in the definitions' order, so BROWN would sit among the Luftwaffe keys
        Assert.Equal(nets.OrderBy(n => Defs.Nets.Keys.ToList().IndexOf(n)), nets);
    }
}
