using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>Watches, tea breaks, fatigue and morale.</summary>
public sealed partial class GameState
{
    public bool WatchesAvailable => ActIndex >= Defs.WatchesFromAct;

    public bool OnDuty(Staff s) => Defs.Shifts[s.Shift].OnDuty(MinuteOfDay);

    public int OnDutyCount => Staff.Count(s => !s.Away);

    void TickStaff(double dt)
    {
        double hour = MinuteOfDay / 60;
        bool night = hour >= 20 || hour < 6;
        for (int i = Staff.Count - 1; i >= 0; i--)
        {
            var s = Staff[i];
            bool duty = OnDuty(s);
            if (s.Away)
            {
                if (duty && !s.Leaving) ComeBack(s);
                continue;
            }

            var t = s.Task;
            double drain = 0;
            if (t is { Kind: TaskKind.Rest } && s.Path.Count == 0 && StationById(t.Dest) is { } rest)
            {
                s.Energy = Math.Min(1, s.Energy + Defs.EnergyRecoverRest * dt);
                s.Morale = Math.Min(1, s.Morale + dt * rest.Type switch
                {
                    StationType.Rounders => 0.008,
                    StationType.CommonRoom => 0.004,
                    _ => 0.001,
                });
                if (s.Energy >= Defs.BreakUntil) s.Task = null;
            }
            else if (t is { Kind: TaskKind.Operate } && StationById(t.Dest) is { Working: true })
                drain = Defs.EnergyDrainWorking;
            else if (t == null && s.Path.Count == 0)
                s.Energy = Math.Min(1, s.Energy + Defs.EnergyRecoverIdle * dt);
            else
                drain = Defs.EnergyDrainOther;

            if (drain > 0)
            {
                if (s.Has(Trait.Tireless)) drain *= 0.7;
                if (night && s.Has(Trait.NightOwl)) drain *= 0.67;
                s.Energy = Math.Max(0, s.Energy - drain * dt);
                if (s.Energy < Defs.Exhausted) s.Morale = Math.Max(0, s.Morale - 0.0004 * dt);
            }

            if (s.Leaving) continue;
            if (!duty)
            {
                switch (s.Task?.Kind)
                {
                    case null:
                        StartLeave(s);
                        break;
                    case TaskKind.Operate:
                        if (StationById(s.Task.Dest) is { } st && st.OperatorId == s.Id) ReleaseOperator(st);
                        s.Task = null;
                        StartLeave(s);
                        break;
                    case TaskKind.Rest:
                        s.Task = null;
                        StartLeave(s);
                        break;
                    // Runners finish their delivery first.
                }
                continue;
            }
            if (s.Task == null && s.Tired) StartBreak(s);
        }
    }

    /// <summary>Off watch: walk out of the hut and go back to the billets.</summary>
    void StartLeave(Staff s)
    {
        var zone = Grid.ZoneAt(s.TilePos.x, s.TilePos.y);
        if (zone == null) { GoAway(s); return; }
        var exit = zone.OutsideDoor;
        if (s.TilePos == exit || !SendTo(s, exit)) { GoAway(s); return; }
        s.Task = new StaffTask { Kind = TaskKind.Leave };
    }

    void GoAway(Staff s)
    {
        s.Away = true;
        s.Task = null;
        s.Path.Clear();
        s.HomeX = s.X;
        s.HomeY = s.Y;
    }

    void ComeBack(Staff s)
    {
        s.Away = false;
        int hx = (int)Math.Floor(s.HomeX), hy = (int)Math.Floor(s.HomeY);
        if (Grid.Walkable(hx, hy)) { s.X = s.HomeX; s.Y = s.HomeY; }
        else { s.X = Grid.Gate.x + 0.5; s.Y = Grid.Gate.y + 0.5; }
        s.Energy = 1;
        s.Morale = Math.Min(1, s.Morale + 0.01);
    }

    bool RestOpen(Station st) =>
        !st.PendingRemoval && st.IsRest &&
        (st.Type != StationType.Rounders || MinuteOfDay is >= 8 * 60 and < 19 * 60);

    int SeatsTaken(Station st) => Staff.Count(x => x.Task is { Kind: TaskKind.Rest } t && t.Dest == st.Id);

    /// <summary>Tea break. Unhappy people head for the common room or the rounders pitch.</summary>
    void StartBreak(Staff s)
    {
        Station? best = null;
        int bestScore = int.MaxValue;
        foreach (var st in Stations)
        {
            if (!RestOpen(st) || SeatsTaken(st) >= st.Def.Seats) continue;
            int score = Manhattan(st.UseTile, s.TilePos);
            if (s.Morale < 0.5 && st.Type != StationType.Canteen) score -= 20;
            if (score < bestScore) { bestScore = score; best = st; }
        }
        if (best == null || !SendTo(s, best.UseTile)) return;
        var used = Staff.Where(x => x.Task is { Kind: TaskKind.Rest } t && t.Dest == best.Id).Select(x => x.Task!.Seat).ToHashSet();
        int seat = 0;
        while (used.Contains(seat)) seat++;
        s.Task = new StaffTask { Kind = TaskKind.Rest, Dest = best.Id, Seat = seat };
        if (s.Path.Count == 0) OnArrive(s);
    }

    /// <summary>
    /// Men of military age could be called up. GC&amp;CS fought for each deferment, spending Whitehall's goodwill,
    /// or got the Foreign Office to rule the post a reserved occupation, which cost money and paperwork.
    /// </summary>
    void MaybeCallUp()
    {
        if (ActIndex < 1 || ActIndex > 10) return;
        if (Dilemmas.Count > 0 || Now - LastDilemmaAt < Defs.MinutesPerDay) return;
        if (!Rng.Chance(Defs.CallUpChance)) return;
        var men = Staff.Where(s => !s.Female && !s.Leaving && s.Notable == null && s.Role != Role.Messenger && s.Role != Role.Wren).ToList();
        if (men.Count == 0) return;
        // The Ministry of Labour has an unerring eye for the people you can least spare.
        var who = men.OrderByDescending(s => s.Skill * 10 + Rng.Range(0, 15)).First();
        var role = Defs.Roles[who.Role].Name.ToLowerInvariant();
        RaiseDilemma(new Dilemma
        {
            Service = Service.Security, StaffId = who.Id, Title = "Call-up papers",
            Text = $"{who.Name} ({role}, {new string('★', who.Skill)}) has received call-up papers from the Ministry of Labour. " +
                   "Without a deferment he reports to an Army training depot on Monday, and we can't tell the Army why he matters.",
            Options =
            {
                new() { Label = "Fight for a deferment", Rep = -4,
                    Outcome = $"Commander Denniston calls in a favour at the War Office. {who.Name} stays, and Whitehall remembers it." },
                new() { Label = "Ask the Foreign Office to make the post reserved", Funds = -Defs.CallUpRulingCost,
                    Outcome = $"A ruling comes through after a week of forms. {who.Name} stays." },
                new() { Label = "Let him go", DismissStaff = true, Morale = -0.02, Rep = 1,
                    Outcome = $"{who.Name} leaves for the Army. He may never say where he has been." },
            },
        });
    }

    void DailyPeople()
    {
        MaybeCallUp();
        foreach (var s in Staff.ToList())
        {
            s.Morale += (Defs.MoraleBaseline - s.Morale) * 0.15;
            if (s.Shift == Shift.Night && !s.Has(Trait.NightOwl)) s.Morale -= 0.02;
            if (s.Shift == Shift.Office && WatchesAvailable) s.Morale -= 0.01; // long days wear people down
            s.Morale = Math.Clamp(s.Morale, 0, 1);
            s.LowMoraleDays = s.Morale < 0.2 ? s.LowMoraleDays + 1 : 0;
            if (s.LowMoraleDays >= 2 && s.Notable == null && !s.Leaving && Rng.Chance(0.35))
            {
                Emit(EventKind.Bad, $"{s.Name} has asked for a transfer. Morale in the huts is too low.");
                Fire(s.Id);
            }
        }
    }

    public void ChangeMorale(double delta)
    {
        foreach (var s in Staff) s.Morale = Math.Clamp(s.Morale + delta, 0, 1);
    }

    public double AverageMorale => Staff.Count == 0 ? Defs.MoraleBaseline : Staff.Average(s => s.Morale);
    public double AverageEnergy => Staff.Count == 0 ? 1 : Staff.Where(s => !s.Away).DefaultIfEmpty().Average(s => s?.Energy ?? 1);

    // ----------------------------------------------------------- player actions

    public bool SetShift(int staffId, Shift shift)
    {
        var s = StaffById(staffId);
        if (s == null || (shift != Shift.Office && !WatchesAvailable)) return false;
        s.Shift = shift;
        return true;
    }

    /// <summary>Spreads everyone of a role (or everyone) evenly across the three watches.</summary>
    public void BalanceWatches(Role? role)
    {
        if (!WatchesAvailable) return;
        var watches = new[] { Shift.Day, Shift.Evening, Shift.Night };
        foreach (var group in Staff.Where(s => role == null || s.Role == role).GroupBy(s => s.Role))
        {
            int i = 0;
            foreach (var s in group.OrderByDescending(s => s.Has(Trait.NightOwl)).ThenBy(s => s.Id))
                s.Shift = watches[(i++ + 2) % 3];  // night owls first, onto the night watch
        }
    }

    public bool Pin(int staffId, int? stationId)
    {
        var s = StaffById(staffId);
        if (s == null) return false;
        if (stationId is int id)
        {
            var st = StationById(id);
            if (st == null || st.Def.Operator != s.Role) return false;
        }
        s.Pinned = stationId;
        return true;
    }

    public IEnumerable<Staff> PinnedTo(int stationId) => Staff.Where(s => s.Pinned == stationId);
}
