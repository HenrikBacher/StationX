using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>How many fire-watchers stand by each building on a raid night.</summary>
public enum FireRota { None, Light, Full }

/// <summary>
/// Fire-watching. The Park's timber huts were tinder, and in a raid the danger was the incendiary: a small bomb that burned
/// through a roof unless someone was there with sand and a stirrup pump. Staff took turns on a fire-watching rota on top of
/// their work. Here: in the Blitz (August 1940 to May 1941) and the "Baby Blitz" (January to May 1944), each raid may drop an
/// incendiary on a building (own dice; timber huts far likelier than brick). With fire-watchers on the rota it is usually
/// caught early: an hour's disruption. Without, the building burns: its desks are out of action for most of a day while it
/// is made good, a machine in it may be damaged, and the people in it are shaken. Fire-watchers come from those off watch and
/// lose a night's sleep on raid nights: it wears on them (strain) and their spirits.
/// </summary>
public sealed partial class GameState
{
    public FireRota FireRota { get; set; } = FireRota.None;
    /// <summary>Buildings on fire now, and until when (zone id → minute).</summary>
    public Dictionary<string, double> BurningUntil { get; set; } = new();
    /// <summary>Buildings smouldering after a fire, and until when.</summary>
    public Dictionary<string, double> ScorchedUntil { get; set; } = new();

    public static readonly DateOnly SteinbockStart = new(1944, 1, 21), SteinbockEnd = new(1944, 5, 29);
    public static bool InRaidSeason(DateOnly d) => InBlitz(d) || (d >= SteinbockStart && d <= SteinbockEnd);

    /// <summary>The rota is worth setting from the first raid season.</summary>
    public bool FireWatchOpen => Mode == GameMode.Campaign && Date >= BlitzStart;

    public void SetFireRota(FireRota r) => FireRota = r;

    public int FireWatchersPer => FireRota switch { FireRota.Light => 1, FireRota.Full => 2, _ => 0 };

    public bool Burning(string zoneId) => BurningUntil.TryGetValue(zoneId, out var t) && t > Now;
    public bool Scorched(string zoneId) => ScorchedUntil.TryGetValue(zoneId, out var t) && t > Now;
    public IEnumerable<Zone> BurningZones() => Grid.Zones.Where(z => Burning(z.Id));

    /// <summary>A raid has begun: the fire-watchers turn out, and incendiaries may fall.</summary>
    void IncendiaryRaid()
    {
        var zones = Grid.Zones.Where(z => z.Unlocked).ToList();
        TurnOutFireWatchers(zones.Count * FireWatchersPer);
        var dice = Rng.Seeded(Mix(Seed, Day, (int)(Now % Defs.MinutesPerDay), 0xF14E));
        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            if (!dice.Chance(z.Brick || z.Id == "mansion" ? Defs.FireChanceBrick : Defs.FireChanceTimber)) continue;
            double caught = FireRota switch { FireRota.Light => Defs.FireCaughtLight, FireRota.Full => Defs.FireCaughtFull, _ => 0 };
            if (dice.Chance(caught)) FireCaught(z);
            else HutBurns(z, dice);
        }
    }

    void TurnOutFireWatchers(int n)
    {
        if (n <= 0) return;
        // Those off watch, the least tired first (a rota shares it out).
        var off = Staff.Where(s => !s.Leaving && s.Away && !s.IsSick(Now) && !s.Arriving(Now))
                       .OrderBy(s => s.FireWatchedAt).ThenBy(s => s.Id).Take(n).ToList();
        foreach (var s in off)
        {
            s.FireWatchedAt = Now;
            s.Morale = Math.Max(0, s.Morale - Defs.FireWatchMorale);
            s.Strain = Math.Min(1, s.Strain + Defs.FireWatchStrain);
        }
        Stats.FireWatchNights += off.Count;
    }

    void FireCaught(Zone z)
    {
        Stats.FiresCaught++;
        foreach (var st in StationsIn(z)) st.DownUntil = Math.Max(st.DownUntil, Now + Defs.FireCaughtMinutes);
        ScorchedUntil[z.Id] = Now + Defs.FireScorchHours * 60;
        Emit(EventKind.Info, $"An incendiary through the roof of {z.Name}: the fire-watchers have it out with sand and a stirrup pump before it takes hold. A scorched floor and an hour's mess.");
    }

    void HutBurns(Zone z, Rng dice)
    {
        Stats.FiresLost++;
        double hours = z.Brick || z.Id == "mansion" ? Defs.FireBurnHoursBrick : Defs.FireBurnHours;
        BurningUntil[z.Id] = Now + Defs.FireBlazeMinutes;
        ScorchedUntil[z.Id] = Now + (hours + Defs.FireScorchHours) * 60;
        var inZone = StationsIn(z).ToList();
        foreach (var st in inZone)
        {
            st.DownUntil = Math.Max(st.DownUntil, Now + hours * 60);
            if (st.OperatorId is int oid && StaffById(oid) is { } op && op.Task is { Kind: TaskKind.Operate }) ReleaseOperator(st);
        }
        string machine = "";
        var wears = inZone.Where(s => s.Wears).ToList();
        if (wears.Count > 0)
        {
            var m = wears[(int)(dice.NextDouble() * wears.Count)];
            m.Wear = Math.Min(1, m.Wear + 0.4);
            Funds -= Defs.RepairCost * 2;
            machine = $" The {m.Def.Name.ToLowerInvariant()} is scorched and needs the fitters (£{Defs.RepairCost * 2}).";
        }
        foreach (var s in Staff.Where(s => !s.Away && Grid.ZoneAt((int)s.X, (int)s.Y) == z))
            s.Morale = Math.Max(0, s.Morale - Defs.FireMorale);
        Funds -= Defs.FireRepairCost;
        Emit(EventKind.Bad, $"FIRE in {z.Name}! An incendiary has caught with nobody on the roof to deal with it. The work is carried out into the dark; " +
                            $"the building is out of action for about {hours:0} hours while it is made good (£{Defs.FireRepairCost}).{machine}");
    }

    IEnumerable<Station> StationsIn(Zone z)
    {
        foreach (var st in Stations)
            if (Grid.ZoneAt(st.X, st.Y) == z) yield return st;
    }


    /// <summary>For screenshots and tests: an incendiary on this building now.</summary>
    public void DebugFire(string zoneId, bool caught)
    {
        var z = Grid.Zone(zoneId);
        if (z == null) return;
        if (caught) FireCaught(z);
        else HutBurns(z, Rng.Seeded(Mix(Seed, Day, 0xF14F)));
    }
}
