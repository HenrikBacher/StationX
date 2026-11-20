using System;
using System.Linq;

namespace StationX.Sim;

/// <summary>The Blitz: night air-raid warnings slow the desks, and now and then a stray bomb falls on the Park.</summary>
public partial class GameState
{
    public double RaidUntil { get; set; }
    public double NextRaidAt { get; set; } = -1;
    public bool RaidBomb { get; set; }
    public double BombAt { get; set; } = -1;

    public bool RaidOn => Now < RaidUntil;

    public static readonly DateOnly BlitzStart = new(1940, 8, 24), BlitzEnd = new(1941, 5, 16);
    static readonly DateOnly HistoricBombs = new(1940, 11, 20);
    public static bool InBlitz(DateOnly d) => d >= BlitzStart && d <= BlitzEnd;

    /// <summary>Desk work by blackout lamps with half the watch in the shelter trenches.</summary>
    public double RaidWorkFactor => RaidOn ? Defs.RaidWorkRate : 1;

    void PlanRaid()
    {
        NextRaidAt = -1;
        RaidBomb = false;
        if (!InBlitz(Date)) return;
        // The night of 20 November 1940: a stick of bombs meant for Bletchley's railway junction lands on the Park.
        bool historic = Date >= HistoricBombs && !Stats.ScriptedDone.Contains("bombs1940");
        if (!historic && !Rng.Chance(Defs.RaidChance * Rules.WearMult)) return;
        NextRaidAt = Day * Defs.MinutesPerDay + Rng.Range(20.5, 26.0) * 60;
        RaidBomb = historic || Rng.Chance(Defs.RaidBombChance);
    }

    void TickRaids()
    {
        if (NextRaidAt >= 0 && Now >= NextRaidAt)
        {
            NextRaidAt = -1;
            double length = Rng.Range(60.0, 150.0);
            RaidUntil = Now + length;
            if (RaidBomb) BombAt = Now + Rng.Range(10.0, length - 10);
            RaidBomb = false;
            Emit(EventKind.Bad, "Air-raid warning! The sirens are going over Bletchley. Desk work slows to half pace in the blackout until the all-clear; the Bombes run on.");
        }
        if (BombAt >= 0 && Now >= BombAt)
        {
            BombAt = -1;
            StrayBomb();
        }
        if (RaidUntil > 0 && !RaidOn)
        {
            RaidUntil = 0;
            Emit(EventKind.Info, "The all-clear sounds.");
        }
    }

    void StrayBomb()
    {
        Stats.ScriptedDone.Add("bombs1940");
        var huts = Grid.Zones.Where(z => z.Unlocked && z.Id != "mansion").ToList();
        if (huts.Count == 0) huts = Grid.Zones.Where(z => z.Unlocked).ToList();
        if (huts.Count == 0) return;
        var hut = Rng.Pick(huts);
        string damage = "";
        var machines = Stations.Where(s => s.Wears && !s.IsDown(Now) && Grid.ZoneAt(s.X, s.Y) == hut).ToList();
        if (machines.Count > 0)
        {
            var m = Rng.Pick(machines);
            m.DownUntil = Now + Defs.RepairMinutesFor(m.Type) * 1.5;
            m.Servicing = false;
            Funds -= Defs.RepairCost * 2;
            Stats.Breakdowns++;
            damage = $" {m.Def.Name} #{m.Id} is shaken off its mountings: the fitters need about {Defs.RepairMinutesFor(m.Type) * 1.5 / 60:0} hours (£{Defs.RepairCost * 2}).";
        }
        foreach (var s in Staff.Where(s => !s.Away))
        {
            bool inside = Grid.ZoneAt((int)s.X, (int)s.Y) == hut;
            s.Morale = Math.Max(0, s.Morale - (inside ? 0.15 : 0.03));
            if (inside) s.Energy = Math.Max(0, s.Energy - 0.2);
        }
        Emit(EventKind.Bad, $"A stray stick of German bombs falls on the Park. {hut.Name} is blown a foot off its foundations and the windows are in; by some miracle nobody is hurt.{damage}");
    }
}
