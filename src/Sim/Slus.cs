using System;
using System.Collections.Generic;

namespace StationX.Sim;

/// <summary>A theatre a Special Liaison Unit can be sent to: when, which services' decrypts it carries, what it costs.</summary>
public sealed record SluDef(string Id, string Name, string Where, string From, string? Until, Service[] Serves, int Cost, int Upkeep, string Blurb);

/// <summary>
/// Special Liaison Units. From 1941 Hut 3's reports reached commanders in the field through small units of RAF and army
/// officers with their own cipher staff (Winterbotham's SLUs), who handed over each signal, waited while it was read and
/// burned it. Here a unit sent to a theatre gets its services' decrypts to the commander while they still matter (they stay
/// fresh a fifth longer), and more people act on ULTRA (a fifth more suspicion for each). Balance: neutral for the autoplayer
/// (80 Normal seeds 50 with or without at 1.2/1.2; 1.3/1.2 made 59, 1.5/1.15 was 28 of 40). Each costs money to
/// send and a weekly upkeep; it comes home when its theatre's war is over.
/// </summary>
public sealed partial class GameState
{
    /// <summary>Theatres with a unit sent (ids of <see cref="SluDefs"/>).</summary>
    public HashSet<string> Slus { get; set; } = new();

    public static readonly SluDef[] SluDefs =
    {
        new("cairo", "Middle East", "Cairo", "Operation Claymore", "Colossus", new[] { Service.Heer, Service.Luftwaffe }, 300, 30,
            "Wavell and then Auchinleck and Montgomery against Rommel: the Afrika Korps' army and air keys."),
        new("malta", "Malta", "Valletta", "U-110", "Colossus", new[] { Service.Kriegsmarine }, 250, 24,
            "The island's aircraft and submarines against the convoys to Tripoli: naval keys."),
        new("normandy", "21st Army Group", "Normandy", "D-Day", null, new[] { Service.Heer, Service.Luftwaffe }, 400, 40,
            "Montgomery's and Bradley's armies from the beaches to the Elbe: army and air keys."),
    };

    /// <summary>Whether a theatre's war is on (a unit can go, or one there is working).</summary>
    public bool SluTheatreOpen(SluDef d)
    {
        if (Mode != GameMode.Campaign) return false;
        int from = Campaign.IndexOf(d.From), until = d.Until == null ? int.MaxValue : Campaign.IndexOf(d.Until);
        return from >= 0 && ActIndex >= from && ActIndex < until;
    }

    bool SluWorking(SluDef d) => Slus.Contains(d.Id) && SluTheatreOpen(d);

    /// <summary>Whether a unit in the field carries this service's decrypts.</summary>
    public bool SluServes(Service sv)
    {
        if (Slus.Count == 0) return false;
        foreach (var d in SluDefs)
            if (SluWorking(d) && Array.IndexOf(d.Serves, sv) >= 0) return true;
        return false;
    }

    /// <summary>Hours a decrypt of this network stays fresh, with a liaison unit to carry it.</summary>
    public double StaleAfter(Net net)
    {
        double h = Defs.StaleAfter(net);
        return SluServes(Defs.NetOf(net).Service) ? h * Defs.SluFresh : h;
    }

    public double DeadAfter(Net net) => 2 * StaleAfter(net);

    public int SluUpkeep
    {
        get
        {
            int u = 0;
            foreach (var d in SluDefs) if (SluWorking(d)) u += d.Upkeep;
            return u;
        }
    }

    /// <summary>Theatres open now, for the Intelligence panel.</summary>
    public IEnumerable<SluDef> SluTheatres()
    {
        foreach (var d in SluDefs) if (SluTheatreOpen(d)) yield return d;
    }

    /// <summary>Why a unit can't be sent to this theatre now, or null.</summary>
    public string? CantSendSlu(string id)
    {
        var d = Array.Find(SluDefs, x => x.Id == id);
        if (d == null || !SluTheatreOpen(d)) return "No war there now";
        if (Slus.Contains(id)) return "A unit is there";
        if (Funds < d.Cost) return $"Needs £{d.Cost}";
        return null;
    }

    public bool SendSlu(string id)
    {
        if (CantSendSlu(id) != null) return false;
        var d = Array.Find(SluDefs, x => x.Id == id)!;
        Funds -= d.Cost;
        Slus.Add(id);
        Emit(EventKind.Good, $"A Special Liaison Unit flies to {d.Where}: commanders there will have ULTRA while it still matters.");
        return true;
    }

    /// <summary>At midnight: units whose theatre's war is over come home.</summary>
    void DailySlus()
    {
        if (Slus.Count == 0) return;
        foreach (var d in SluDefs)
            if (Slus.Contains(d.Id) && !SluTheatreOpen(d) && ActIndex >= Campaign.IndexOf(d.From))
            {
                Slus.Remove(d.Id);
                Emit(EventKind.Info, $"The Special Liaison Unit in {d.Where} comes home: the fighting it served is over.");
            }
    }
}
