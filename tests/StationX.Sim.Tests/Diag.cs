using StationX.Sim;
using Xunit;
using Xunit.Abstractions;

namespace StationX.Tests;

public class Diag(ITestOutputHelper output)
{
    [Fact]
    public void DailyLog()
    {
        // Opt-in: DIAG=1 SEED=42 DAYS=60 dotnet test --filter Diag --logger "console;verbosity=detailed"
        if (Environment.GetEnvironmentVariable("DIAG") != "1") return;
        int seed = int.Parse(Environment.GetEnvironmentVariable("SEED") ?? "1942");
        int days = int.Parse(Environment.GetEnvironmentVariable("DAYS") ?? "40");
        var g = Environment.GetEnvironmentVariable("FREE") is string fa ? GameState.NewGame(seed, mode: GameMode.FreePlay, startAct: int.Parse(fa)) : GameState.NewGame(seed);
        var ai = new AutoPlayer(g) { ContinuePastEnigma = Environment.GetEnvironmentVariable("TUNNY") == "1" };
        int lastDay = -1;
        var log = new List<string>();
        while ((g.Status == GameStatus.Playing || (ai.ContinuePastEnigma && g.CanContinue)) && g.Day < days)
        {
            ai.Think();
            foreach (var e in g.Events) if (e.Kind is EventKind.Bad or EventKind.ActStarted) log.Add($"   {g.Day}.{g.MinuteOfDay / 60:00} {e.Kind}: {e.Text[..Math.Min(110, e.Text.Length)]}");
            g.Events.Clear();
            g.Advance(60);
            if (g.Day != lastDay)
            {
                lastDay = g.Day;
                foreach (var l in log) output.WriteLine(l);
                log.Clear();
                var backlog = string.Join(" ", g.Messages.Values.GroupBy(m => m.Next?.ToString() ?? "wait").Select(x => $"{x.Key}:{x.Count()}"));
                var act = g.Contracts.Where(c => c.State == ContractState.Active).Select(c => $"{c.Net}{c.Delivered}/{c.Quantity}");
                output.WriteLine($"day {g.Day,3} act {g.ActIndex} £{g.Funds,6} rep {g.Reputation,3:0} staff {g.Staff.Count,2} E{g.AverageEnergy:0.00} M{g.AverageMorale:0.00} susp {g.MaxSuspicion,3:0} done {g.Stats.ContractsCompleted} fail {g.Stats.ContractsFailed} | {string.Join(",", act)} | {backlog}");
                if (g.Day is 4000)
                    foreach (var s in g.Staff)
                        output.WriteLine($"        {s.Name,-22} {s.Role,-12} {s.Shift,-7} away={s.Away} E{s.Energy:0.00} M{s.Morale:0.00} task={s.Task?.Kind}/{s.Task?.Phase}/{s.Task?.Dest} path={s.Path.Count} at {s.TilePos}");
                if (g.Day >= 25) output.WriteLine("        stations: " + string.Join(" ", g.Stations.GroupBy(s => s.Type).Select(x => $"{x.Key}x{x.Count()}")));
            }
        }
        output.WriteLine($"status {g.Status} act {g.ActIndex}");
    }
}

public class StuckDiag(ITestOutputHelper output)
{
    [Fact]
    public void FindStuck()
    {
        // Opt-in: STUCK=1 dotnet test --filter StuckDiag --logger "console;verbosity=detailed"
        if (Environment.GetEnvironmentVariable("STUCK") != "1") return;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var save = Environment.GetEnvironmentVariable("SAVE");
            var g = save != null ? SaveGame.Load(File.ReadAllText(save)) : GameState.NewGame(seed);
            g.Rng = new Rng((ulong)seed);
            var ai = save != null ? null : new AutoPlayer(g);
            var since = new Dictionary<int, (string sig, double t)>();
            int reported = 0;
            while (g.Status == GameStatus.Playing && g.Day < (save != null ? 14 : 50) && reported < 6)
            {
                if (g.ActCompletePending) g.ContinueAfterAct();
                if (ai != null) ai.Think();
                else foreach (var c in g.Contracts.Where(c => c.State == ContractState.Offered && g.Contracts.Count(k => k.State == ContractState.Active) < 2).ToList()) g.Accept(c.Id);
                g.Events.Clear();
                if (Environment.GetEnvironmentVariable("TRACE") is string tr)
                    File.WriteAllText(tr, $"day {g.Day} now {g.Now}\n" + string.Join("\n", g.Staff.Select(s => $"{s.Id} {s.Role} {s.X:0.00},{s.Y:0.00} {s.Task?.Kind}/{s.Task?.Phase}/{s.Task?.Dest} path {string.Join(" ", s.Path.Take(6))} away {s.Away}")));
                g.Advance(10);
                foreach (var s in g.Staff)
                    if (!s.Away && g.Grid.OccupantAt(s.TilePos.x, s.TilePos.y) >= 0 && reported < 6)
                    { output.WriteLine($"seed {seed} day {g.Day} {s.Name} ON A STATION at {s.TilePos} task {s.Task?.Kind}/{s.Task?.Phase} path {s.Path.Count}"); reported++; }
                foreach (var s in g.Staff)
                {
                    if (s.Away || s.Task == null) { since.Remove(s.Id); continue; }
                    var st = s.Task.Dest >= 0 ? g.Stations.FirstOrDefault(x => x.Id == s.Task.Dest) : null;
                    string sig = $"{s.Task.Kind}/{s.Task.Phase}/{s.Task.Dest}/{s.TilePos}/{s.Path.Count}/{st?.Current}/{st?.Input.Count}/{st?.WorkDone:0}";
                    if (!since.TryGetValue(s.Id, out var v) || v.sig != sig) { since[s.Id] = (sig, g.Now); continue; }
                    if (g.Now - v.t > 240)
                    {
                        output.WriteLine($"seed {seed} day {g.Day} {s.Role} {s.Name}: {sig} at {st?.Type} use {st?.UseTile} op {st?.OperatorId}=={s.Id} present {st?.OperatorPresent} msgs {string.Join(",", s.Task.Msgs)} energy {s.Energy:0.00}");
                        since[s.Id] = (sig, g.Now + 1e9);
                        reported++;
                    }
                }
            }
        }
    }
}

public class LoadDiag(ITestOutputHelper output)
{
    [Fact]
    public void LoadOnly()
    {
        if (Environment.GetEnvironmentVariable("SAVE") is not string save) return;
        var json = File.ReadAllText(save);
        output.WriteLine("read");
        var g = SaveGame.Load(json);
        output.WriteLine($"loaded {g.Grid.Zone("mansion")!.Rooms.Count} rooms");
        var ops = g.Stations.Where(x => x.Type == StationType.HandCipher).ToDictionary(x => x.Id, x => x.OperatorId);
        for (int i = 0; i < 3 * 1440 && g.Status == GameStatus.Playing; i++)
        {
            if (g.ActCompletePending) g.ContinueAfterAct();
            g.Advance(1);
            g.Events.Clear();
            if (g.Day == 4 && g.MinuteOfDay >= 19 * 60 + 5 && g.MinuteOfDay <= 19 * 60 + 40)
                foreach (var p in g.Staff.Where(p => !p.Away))
                    output.WriteLine($"  {g.MinuteOfDay / 60:00}:{g.MinuteOfDay % 60:00} {p.Id} {p.Role} {p.X:0.00},{p.Y:0.00} {p.Task?.Kind}/{p.Task?.Phase}/{p.Task?.Dest} path {string.Join(" ", p.Path.Take(5))} E{p.Energy:0.00}");
            foreach (var st in g.Stations.Where(x => x.Type == StationType.HandCipher))
            {
                if (ops[st.Id] != st.OperatorId)
                    output.WriteLine($"{g.Day} {g.MinuteOfDay / 60:00}:{g.MinuteOfDay % 60:00} desk {st.Id} op {ops[st.Id]} -> {st.OperatorId} in {st.Input.Count} cur {st.Current}");
                ops[st.Id] = st.OperatorId;
            }
        }
    }
}

public class BuildDiag(ITestOutputHelper output)
{
    [Fact]
    public void Placement()
    {
        if (Environment.GetEnvironmentVariable("BUILDDIAG") != "1") return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = GameState.NewGame(seed);
            var ai = new AutoPlayer(g);
            ai.PlayCampaign(130);
            var byZone = g.Stations.GroupBy(s => g.Grid.ZoneAt(s.X, s.Y)?.Id ?? "out").Select(x => $"{x.Key}:{x.Count()}");
            if (seed == 1)
            {
                g.Funds = 99999;
                foreach (var st in g.Stations.Where(x => g.Grid.ZoneAt(x.X, x.Y)?.Id == "hut6")) output.WriteLine($"  {st.Type} at {st.X},{st.Y} rot {st.Rot} use {st.UseTile}");
                for (int y = 3; y <= 7; y++)
                    output.WriteLine("  " + string.Join(" | ", Enumerable.Range(21, 10).Select(x => $"{x}:{g.CanPlace(StationType.Typex, x, y, 0) ?? "OK"}".PadRight(14)[..14])));
            }
            output.WriteLine($"seed {seed} status {g.Status} day {g.Day} act {g.ActIndex} staff {g.Staff.Count} builds {ai.Builds} fails {ai.BuildFails} | {string.Join(" ", byZone)} ");
        }
    }
}

public class PileDiag(ITestOutputHelper output)
{
    [Fact]
    public void Piles()
    {
        if (Environment.GetEnvironmentVariable("PILE") != "1") return;
        var g = GameState.NewGame(int.Parse(Environment.GetEnvironmentVariable("SEED") ?? "3"));
        var ai = new AutoPlayer(g);
        int day = int.Parse(Environment.GetEnvironmentVariable("DAYS") ?? "30");
        while (g.Status == GameStatus.Playing && g.Day < day) { ai.Think(); g.Advance(60); g.Events.Clear(); if (g.ActCompletePending) g.ContinueAfterAct(); }
        output.WriteLine($"day {g.Day} act {g.ActIndex} status {g.Status} rep {g.Reputation:0} staff {g.Staff.Count} handovers {g.Stats.HandOvers}");
        foreach (var st in g.Stations.Where(s => s.Def.Operator != null))
            output.WriteLine($"  {st.Type} #{st.Id} {g.Grid.ZoneAt(st.X, st.Y)?.Id}/{g.Grid.RoomAt(st.X, st.Y)?.Name} in {st.Input.Count} out {st.Output.Count} cur {st.Current} op {st.OperatorId}");
        foreach (var grp in g.Messages.Values.Where(m => m.Loc != Loc.Done).GroupBy(m => $"{m.Loc}/{m.Next}"))
            output.WriteLine($"  {grp.Key}: {grp.Count()} oldest {(g.Now - grp.Min(m => m.Created)) / 60:0}h");
        foreach (var r in new[] { Role.Clerk, Role.Messenger, Role.Linguist, Role.Wren, Role.Cryptanalyst })
            output.WriteLine($"  {r}: {g.Staff.Count(s => s.Role == r)} idle {g.Staff.Count(s => s.Role == r && s.Idle)} away {g.Staff.Count(s => s.Role == r && s.Away)}");
    }
}

public class MoveDiag(ITestOutputHelper output)
{
    [Fact]
    public void Moves()
    {
        if (Environment.GetEnvironmentVariable("SAVE") is not string save || Environment.GetEnvironmentVariable("MOVE") != "1") return;
        var g = SaveGame.Load(File.ReadAllText(save));
        var rooms = new Dictionary<int, HashSet<string>>();
        var stillSince = new Dictionary<int, double>();
        int delivered0 = g.Stats.DeliveredTotal;
        for (int i = 0; i < 24 * 60; i++)
        {
            if (g.ActCompletePending) g.ContinueAfterAct();
            g.Advance(1); g.Events.Clear();
            foreach (var s in g.Staff.Where(s => !s.Away))
            {
                var r = g.Grid.RoomAt(s.TilePos.x, s.TilePos.y);
                (rooms.TryGetValue(s.Id, out var set) ? set : rooms[s.Id] = new()).Add(r?.Name ?? (g.Grid.ZoneAt(s.TilePos.x, s.TilePos.y)?.Id ?? "outside"));
                if (s.Path.Count > 0 && g.Now % 60 == 0)
                    output.WriteLine($"{g.Day} {g.MinuteOfDay / 60:00}:{g.MinuteOfDay % 60:00} {s.Id} {s.Role} at {s.TilePos} {s.Task?.Kind}/{s.Task?.Phase} next {s.Path[0]} len {s.Path.Count} -> {s.Path[^1]}");
            }
        }
        foreach (var kv in rooms) output.WriteLine($"staff {kv.Key}: {string.Join(", ", kv.Value)}");
        output.WriteLine($"delivered {g.Stats.DeliveredTotal - delivered0}");
    }
}

public class StandingProbe
{
    readonly Xunit.Abstractions.ITestOutputHelper o;
    public StandingProbe(Xunit.Abstractions.ITestOutputHelper o) => this.o = o;
    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("PROBE") != "1") return;
        var g = TestUtil.BombeLine(12, checking: true);
        var c = g.MakeStanding(new ContractTemplate(Net.Red, 6, 6, 3, 3));
        g.Contracts.Add(c); g.Accept(c.Id);
        o.WriteLine($"perDay {c.PerDay} qty {c.Quantity} days {c.Days} spawns {string.Join(",", c.SpawnTimes.Select(t => ((t - c.AcceptedAt) / 60).ToString("0")))}");
        for (int h = 0; h < 80; h += 4)
        {
            TestUtil.Run(g, 4.0 / 24);
            o.WriteLine($"h{h + 4}: spawned {c.Spawned} delivered {c.Delivered} checked {c.MilestonesChecked} met {c.MilestonesMet} state {c.State}");
        }
    }
}

public class BuiltProbe
{
    readonly Xunit.Abstractions.ITestOutputHelper o;
    public BuiltProbe(Xunit.Abstractions.ITestOutputHelper o) => this.o = o;
    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("BUILT") != "1") return;
        var g = GameState.NewGame(3, Difficulty.Normal);
        var ai = new AutoPlayer(g);
        while (g.Status == GameStatus.Playing && g.Day < 40)
        {
            if (g.ActCompletePending) g.ContinueAfterAct();
            ai.Think(); g.Advance(60); g.Events.Clear();
        }
        o.WriteLine($"day {g.Day} act {g.ActIndex} builds {ai.Builds} fails {ai.BuildFails}");
        foreach (var grp in g.Stations.GroupBy(s => s.Type)) o.WriteLine($"{grp.Key}: {grp.Count()} in {string.Join(",", grp.Select(s => g.Grid.ZoneAt(s.X, s.Y)?.Id))}");
        o.WriteLine($"sixta logged {string.Join(",", g.SixtaLogged.Select(kv => kv.Key + "=" + kv.Value))}");
    }
}
