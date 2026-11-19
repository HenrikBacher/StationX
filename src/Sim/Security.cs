using System;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// The Park's own security. Worksheets left out on a desk abandoned mid-message overnight, and staff lodged miles away
/// chatting on the bus, are leaks the Abwehr's agents may pick up: they raise its suspicion. A security sweep (desks
/// cleared, safes checked, a word in every ear) plugs the leaks, at the cost of everyone's time and patience.
/// </summary>
public sealed partial class GameState
{
    public int LastSweepDay { get; set; } = -99;

    public string? CanSecuritySweep()
    {
        if (ActIndex < Defs.SecurityFromAct) return "Not yet: the Park is still small enough for everyone to know everyone.";
        if (SuspicionOf(Service.Security) < 5) return "No sign of any leak to plug.";
        if (Day - LastSweepDay < Defs.SweepCooldownDays) return $"The last sweep was {Day - LastSweepDay} day(s) ago; another so soon would cause a mutiny.";
        return null;
    }

    public bool SecuritySweep()
    {
        if (CanSecuritySweep() != null) return false;
        LastSweepDay = Day;
        Stats.Sweeps++;
        AddSuspicion(Service.Security, -Defs.SweepRelief);
        foreach (var s in Staff.Where(s => !s.Away))
        {
            s.Energy = Math.Max(0, s.Energy - Defs.SweepEnergy);
            s.Morale = Math.Max(0, s.Morale - Defs.SweepMorale);
        }
        Emit(EventKind.Info, "The security officer's sweep: desks cleared, safes checked, burn bags emptied and a word in every ear. " +
                             "The leaks are plugged for now; everyone on duty lost an hour and some patience.");
        return true;
    }

    /// <summary>For tests.</summary>
    public void DebugDailySecurity() => DailySecurity();

    void DailySecurity()
    {
        if (ActIndex < Defs.SecurityFromAct) return;
        var r = new Random(Mix(Seed, Day, 0x5EC));
        int found = Stations.Count(st => st.LeftAt >= 0 && st.Current != null && !st.IsMachine && r.NextDouble() < Defs.PapersOutChance);
        found = Math.Min(found, Defs.PapersOutMax);
        if (found > 0)
        {
            Stats.PapersFound += found;
            AddSuspicion(Service.Security, found * Defs.PapersOutSuspicion);
            Emit(EventKind.Bad, $"The night duty officer found worksheets left out on {(found == 1 ? "a desk" : $"{found} desks")} " +
                                "abandoned half-way through a message. Papers left about are how agents learn things.");
        }
        if (Overcrowded > 0) AddSuspicion(Service.Security, Overcrowded * Defs.FarBilletSuspicion);
    }
}
