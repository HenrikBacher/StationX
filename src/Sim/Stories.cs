using System;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// People with stories: strain that builds into burnout if nobody notices, and rivalries between cryptanalysts
/// that sour a room until they're separated.
/// </summary>
public sealed partial class GameState
{
    /// <summary>Sends someone away from the Park for a while (leave or a breakdown), using the sick-list machinery.</summary>
    void SendOff(Staff s, double days, string reason)
    {
        s.SickUntil = Now + days * Defs.MinutesPerDay;
        s.OffReason = reason;
        if (s.Task is { Kind: TaskKind.Operate } t && StationById(t.Dest) is { } st && st.OperatorId == s.Id) ReleaseOperator(st);
    }

    void DailyStrain()
    {
        foreach (var s in Staff.ToList())
        {
            if (s.IsSick(Now) || s.Arriving(Now)) { s.LowToday = 1; continue; }
            if (s.OffReason != null) s.OffReason = null;
            double low = s.LowToday;
            s.LowToday = s.Energy;
            double d = low < 0.15 ? 0.12 : low < 0.3 ? 0.05 : low < 0.45 ? 0.0 : -0.06;
            if (d > 0 && s.Has(Trait.Tireless)) d *= 0.5;
            if (d > 0 && Overtime) d += 0.04;
            d += HeadOfLastZone(s)?.Strain ?? 0;
            s.Strain = Math.Clamp(s.Strain + d, 0, 1);
            if (s.Strain < 0.5) s.StrainWarned = false;
            if (s.Leaving) continue;
            if (s.Strain >= Defs.StrainWarnAt && !s.StrainWarned)
            {
                s.StrainWarned = true;
                RaiseDilemma(new Dilemma
                {
                    Service = Service.Heer, Title = "Close to breaking", StaffId = s.Id,
                    Text = $"{s.Name} has been working until they drop for days on end: shaking hands, not sleeping, snapping at everyone. " +
                           "Their head of section thinks they're close to a breakdown.",
                    Options =
                    {
                        new() { Label = $"Send them home on {Defs.RestLeaveDays:0} days' leave", Action = "rest",
                            Outcome = $"{s.Name} goes home to rest. They'll be back in {Defs.RestLeaveDays:0} days." },
                        new() { Label = "Keep them on: we can't spare anyone",
                            Outcome = $"{s.Name} stays at their desk. Watch them." },
                    },
                });
            }
            else if (s.Strain >= 0.9 && Rng.Chance(Defs.StrainBreakChance))
            {
                SendOff(s, Defs.BreakdownDays, "breakdown");
                s.Strain = 0.3;
                s.Morale = Math.Max(0, s.Morale - 0.2);
                foreach (var o in Staff.Where(o => o != s && !o.Away && Grid.ZoneAt((int)o.X, (int)o.Y) == Grid.ZoneAt((int)s.X, (int)s.Y)))
                    o.Morale = Math.Max(0, o.Morale - 0.05);
                Emit(EventKind.Bad, $"{s.Name} has broken down at their desk and been sent home for {Defs.BreakdownDays:0} days. It shook everyone in the hut.");
            }
        }
    }

    void DailyRivalries()
    {
        foreach (var s in Staff.Where(s => s.Rival != null && Day >= s.RivalUntilDay).ToList())
        {
            if (s.Rival is not int rid) continue;   // the other half of a pair already handled
            if (StaffById(rid) is { } r && r.Rival == s.Id)
            {
                r.Rival = null;
                Emit(EventKind.Info, $"{s.Name} and {r.Name} have made it up over a drink at the Eight Bells.");
            }
            s.Rival = null;
        }
        foreach (var s in Staff.Where(s => s.Rival is int rid && StaffById(rid) == null)) s.Rival = null;

        var minds = Staff.Where(s => s.Role == Role.Cryptanalyst && s.Rival == null && !s.Leaving && s.HeadOf == null).ToList();
        var r0 = new Random(Mix(Seed, Day, 0x5717));
        if (minds.Count < 4 || r0.NextDouble() >= Defs.RivalChance * minds.Count / 4) return;
        var a = minds[r0.Next(minds.Count)];
        var top = minds.Where(x => x != a).OrderByDescending(x => x.Skill).Take(3).ToList();
        var b = top[r0.Next(top.Count)];
        a.Rival = b.Id;
        b.Rival = a.Id;
        a.RivalUntilDay = b.RivalUntilDay = Day + r0.Next(12, 25);
        var whys = new[] { "who really broke yesterday's key", "a borrowed pencil and a ruined menu", "whose crib it was", "the last of the coffee", "the right way to set up a Bombe menu" };
        var why = whys[r0.Next(whys.Length)];
        Emit(EventKind.Bad, $"{a.Name} and {b.Name} have fallen out over {why}. In the same room they work slower and sour the place: post one of them elsewhere for a while.");
    }

    /// <summary>True when someone's rival is in the same room as them, at work.</summary>
    public bool WithRival(Staff s) =>
        s.Rival is int rid && StaffById(rid) is { Away: false } r && Grid.RoomAt(s.TilePos.x, s.TilePos.y) is { } room &&
        Grid.RoomAt(r.TilePos.x, r.TilePos.y) == room;

    double RivalryFactor(Staff op, double dt)
    {
        if (op.Rival == null || !WithRival(op)) return 1;
        op.Morale = Math.Max(0, op.Morale - Defs.RivalMoralePerMinute * dt);
        return Defs.RivalWorkRate;
    }
}
