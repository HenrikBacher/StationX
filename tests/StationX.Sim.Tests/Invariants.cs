using StationX.Sim;
using Xunit;
using Xunit.Abstractions;

namespace StationX.Tests;

/// <summary>
/// Cheap consistency checks on autoplayed games, hour by hour: every message is where it says it is, nobody's energy
/// or morale is NaN or out of range, the stores are a number, nobody stands frozen mid-walk for hours, and no desk with
/// work waits half a day while someone who could work it stands idle. A short game always runs;
/// INVARIANTS=1 [INVARIANTS_SEEDS=..] [INVARIANTS_DAYS=..] [INVARIANTS_TUNNY=1] plays long games on several seeds and difficulties.
/// </summary>
public class Invariants(ITestOutputHelper output)
{
    [Fact]
    public void ShortGameKeepsItsBooks()
    {
        var problems = Play(Difficulty.Normal, 5, 20);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void LongGamesKeepTheirBooks()
    {
        if (Environment.GetEnvironmentVariable("INVARIANTS") != "1") return;
        int days = int.Parse(Environment.GetEnvironmentVariable("INVARIANTS_DAYS") ?? "320");
        var seeds = (Environment.GetEnvironmentVariable("INVARIANTS_SEEDS") ?? "3,40,77,114,151,188").Split(',').Select(int.Parse).ToArray();
        var games = Enum.GetValues<Difficulty>().SelectMany(d => seeds.Select(s => (d, s))).ToArray();
        var found = new List<string>[games.Length];
        Parallel.For(0, games.Length, i => found[i] = Play(games[i].d, games[i].s, days));
        var all = found.SelectMany(f => f).ToList();
        foreach (var p in all.Take(200)) output.WriteLine(p);
        output.WriteLine($"{all.Count} problems in {games.Length} games");
        Assert.Empty(all);
    }

    /// <summary>Hours a walker may stay on one spot, and a desk with work may wait while someone suited stands idle.</summary>
    const int FrozenHours = 6, StarvedHours = 12;

    static List<string> Play(Difficulty d, int seed, int days)
    {
        var g = GameState.NewGame(seed, d);
        // INVARIANTS_TUNNY=1 carries on past SHARK into the Tunny chapter, for longer games.
        var ai = new AutoPlayer(g) { ContinuePastEnigma = Environment.GetEnvironmentVariable("INVARIANTS_TUNNY") == "1" };
        var problems = new List<string>();
        var still = new Dictionary<int, ((double, double) at, int hours)>();
        var starved = new Dictionary<int, int>();
        void Fail(string what)
        {
            if (problems.Count < 50) problems.Add($"{d} seed {seed} day {g.Day} {g.MinuteOfDay / 60:00}h: {what}");
        }
        while ((g.Status == GameStatus.Playing || (ai.ContinuePastEnigma && g.CanContinue)) && g.Day < days)
        {
            if (g.ActCompletePending) g.ContinueAfterAct();
            ai.Think();
            g.Advance(60);
            g.Events.Clear();
            CheckBooks(g, Fail);
            if (!double.IsFinite(g.Stores)) Fail($"stores are {g.Stores}");
            foreach (var s in g.Staff)
            {
                if (!double.IsFinite(s.Energy) || s.Energy < 0 || s.Energy > 1) Fail($"{s.Name} has energy {s.Energy}");
                if (!double.IsFinite(s.Morale) || s.Morale < 0 || s.Morale > 1) Fail($"{s.Name} has morale {s.Morale}");
                if (!double.IsFinite(s.X) || !double.IsFinite(s.Y)) Fail($"{s.Name} is at {s.X},{s.Y}");
                // Frozen mid-walk: a path to follow, and not a step taken in hours.
                if (s.Away || s.Path.Count == 0) { still.Remove(s.Id); continue; }
                var at = (s.X, s.Y);
                var h = still.TryGetValue(s.Id, out var prev) && prev.at == at ? prev.hours + 1 : 0;
                still[s.Id] = (at, h);
                if (h == FrozenHours) Fail($"{s.Name} ({s.Role}, {s.Task?.Kind}) has stood at {s.TilePos} for {h} h with {s.Path.Count} tiles to go");
            }
            foreach (var st in g.Stations)
            {
                if (st.ReservedInputs < 0) Fail($"{st.Type}#{st.Id} has {st.ReservedInputs} reserved inputs");
                bool waiting = st.Def.Operator is Role role && st.Input.Count > 0 && st.Current == null && st.OperatorId == null &&
                               !st.Obsolete && !st.PendingRemoval && !st.IsDown(g.Now) &&
                               g.Staff.Any(s => s.Idle && !s.Tired && s.HeadOf == null && s.Role == role && s.Pinned == null && g.OnDuty(s) && !s.Arriving(g.Now) && g.MayWorkAt(s, st.UseTile));
                int n = waiting ? starved.GetValueOrDefault(st.Id) + 1 : 0;
                starved[st.Id] = n;
                if (n == StarvedHours) Fail($"{st.Type}#{st.Id} has had {st.Input.Count} waiting for {n} h with {st.Def.Operator} standing idle");
            }
        }
        return problems;
    }

    /// <summary>Every message is where it says it is, once, and every tray holds only messages that exist.</summary>
    static void CheckBooks(GameState g, Action<string> fail)
    {
        var seen = new Dictionary<int, string>();
        void Place(int id, string where)
        {
            if (seen.TryGetValue(id, out var other)) fail($"message {id} is both {other} and {where}");
            seen[id] = where;
            if (!g.Messages.ContainsKey(id)) fail($"{where} holds message {id}, which doesn't exist");
        }
        foreach (var st in g.Stations)
        {
            foreach (var id in st.Input) Place(id, $"in {st.Type}#{st.Id}");
            foreach (var id in st.Output) Place(id, $"out {st.Type}#{st.Id}");
            if (st.Current is int c) Place(c, $"on {st.Type}#{st.Id}");
        }
        foreach (var s in g.Staff)
            if (s.Task is { Kind: TaskKind.Carry } t && t.Phase >= 1)
                foreach (var id in t.Msgs) Place(id, $"carried by {s.Name}");
        foreach (var m in g.Messages.Values)
        {
            var want = m.Loc switch
            {
                Loc.Input => g.StationById(m.LocId) is { } st ? $"in {st.Type}#{st.Id}" : null,
                Loc.Output => g.StationById(m.LocId) is { } st ? $"out {st.Type}#{st.Id}" : null,
                Loc.Processing => g.StationById(m.LocId) is { } st ? $"on {st.Type}#{st.Id}" : null,
                Loc.Carried => g.StaffById(m.LocId) is { } s ? $"carried by {s.Name}" : null,
                _ => "",
            };
            if (want == "") continue;
            if (want == null) fail($"message {m.Id} is {m.Loc} at #{m.LocId}, which is gone");
            else if (!seen.TryGetValue(m.Id, out var where)) fail($"message {m.Id} should be {want} but is nowhere");
            else if (where != want) fail($"message {m.Id} should be {want} but is {where}");
        }
    }
}
