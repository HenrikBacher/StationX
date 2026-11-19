using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>A listening station and the German networks it takes down.</summary>
public sealed record YStationDef(string Id, string Name, Net[] Nets, Service? Df, int DfFromAct);

/// <summary>A dispatch rider who has just brought a Y-station's bundle to a registration desk.</summary>
public sealed record RiderDrop(int Id, double At, string YStation, int StationId);

/// <summary>
/// The Y service: the listening stations that take down German traffic for the Park.
/// Receiver sets are shared out across the networks (the Y Board's "cover"): a network with fewer sets than it needs
/// comes in late and garbled, and one with extra sets comes in cleaner. Traffic reaches the Park by dispatch rider,
/// in bundles every two hours, unless a teleprinter line has been laid from that station. Direction-finding stations
/// make cover stories believable. And now and then a network changes its frequencies and goes quiet until the
/// stations' spare sets find it again.
/// </summary>
public sealed partial class GameState
{
    /// <summary>Receiver sets the Y Board has given the Park's networks.</summary>
    public int ReceiverSets { get; set; } = Defs.StartSets;
    /// <summary>Sets assigned to each network.</summary>
    public Dictionary<Net, int> Cover { get; set; } = new();
    /// <summary>Networks the cover has been set up for (new ones take spare sets when they first come on the air).</summary>
    public HashSet<Net> Covered { get; set; } = new();
    /// <summary>Y-stations with a teleprinter line to the Park.</summary>
    public HashSet<string> Lines { get; set; } = new();
    /// <summary>Services whose transmitters our direction-finding stations can fix.</summary>
    public HashSet<Service> DirectionFinding { get; set; } = new();
    /// <summary>Networks lost after a change of frequencies, and when the search will find them.</summary>
    public Dictionary<Net, double> SearchUntil { get; set; } = new();
    /// <summary>A change of frequencies due today, per network.</summary>
    public Dictionary<Net, double> FreqChangeAt { get; set; } = new();

    public static YStationDef YStationOf(Net net) => Defs.YStations.First(y => y.Nets.Contains(net));

    public int CoverOf(Net n) => Cover.GetValueOrDefault(n);
    public int SpareSets => Math.Max(0, ReceiverSets - Cover.Values.Sum());

    /// <summary>Networks the Y service is taking down now: the ones in play.</summary>
    public IEnumerable<Net> CoveredNets() =>
        ActiveNets.Where(InPlay).OrderBy(n => NetOrder.IndexOf(n));

    /// <summary>How well a network is covered: 1 when it has the sets it needs, more with extra.</summary>
    public double CoverShare(Net n) => (double)CoverOf(n) / Defs.SetsWanted;

    /// <summary>Garbles are likelier on a thinly covered network, and rarer with a spare set on the same frequency.</summary>
    public double CoverGarble(Net n) => CoverShare(n) switch
    {
        >= 1.5 => 0.6,
        >= 1 => 1,
        >= 0.5 => 1.8,
        _ => 3,
    };

    /// <summary>
    /// Minutes between a message going out on the air and its reaching the Park: a thin cover catches it only on a
    /// repeat, and dispatch riders leave the station every two hours.
    /// </summary>
    public double TransitMinutes(Net n, double sentAt)
    {
        double share = CoverShare(n);
        double late = share >= 1 ? 0 : share >= 0.5 ? Defs.ThinCoverDelay : Defs.NoCoverDelay;
        double caught = sentAt + late;
        if (LineWorking(n)) return late + Defs.LineMinutes;
        double leaves = Math.Ceiling(caught / Defs.RiderEvery) * Defs.RiderEvery;
        return leaves + Defs.RideMinutes - sentAt;
    }

    /// <summary>A working teleprinter in the Park that takes this network (or, failing that, any working one).</summary>
    public Station? TeleprinterFor(Net n)
    {
        var up = Stations.Where(s => s.Type == StationType.Inbox && !s.PendingRemoval && !s.IsDown(Now)).ToList();
        return up.Where(s => s.ReceivesNet(n)).OrderBy(s => s.Output.Count).FirstOrDefault() ?? up.OrderBy(s => s.Output.Count).FirstOrDefault();
    }

    /// <summary>
    /// The network's Y-station has a teleprinter line and a teleprinter in the Park can print it. A line with no
    /// teleprinter at the far end (none built, or all jammed) is no use: the station sends by rider as before.
    /// </summary>
    public bool LineWorking(Net n) => Lines.Contains(YStationOf(n).Id) && TeleprinterFor(n) != null;

    /// <summary>The registration desk a dispatch rider hands a bundle to: the least loaded one.</summary>
    public Station? RiderDesk() =>
        Stations.Where(s => s.Type == StationType.Registration && !s.PendingRemoval && !s.Obsolete && !s.IsDown(Now))
                .OrderBy(s => s.Input.Count).FirstOrDefault();

    /// <summary>Riders seen arriving lately, for the view to draw (not saved).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<RiderDrop> RiderDrops { get; } = new();

    /// <summary>Sends a rider to a desk, for screenshots.</summary>
    public void DebugRider(int stationId) => RiderDrops.Add(new RiderDrop(NextId++, Now, "denmarkhill", stationId));

    void RiderArrived(Net n, Station desk)
    {
        string y = YStationOf(n).Id;
        RiderDrops.RemoveAll(d => d.At < Now - 3 * 60);
        // One rider per station and desk per bundle.
        if (RiderDrops.Any(d => d.YStation == y && d.StationId == desk.Id && d.At > Now - 30)) return;
        RiderDrops.Add(new RiderDrop(NextId++, Now, y, desk.Id));
    }

    public bool Searching(Net n) => SearchUntil.TryGetValue(n, out var t) && Now < t;

    public string? CanMoveSet(Net n, int delta)
    {
        if (delta > 0 && SpareSets <= 0) return "No spare sets";
        if (delta < 0 && CoverOf(n) <= 0) return "None to take";
        return null;
    }

    public bool MoveSet(Net n, int delta)
    {
        if (CanMoveSet(n, delta) != null) return false;
        Cover[n] = CoverOf(n) + delta;
        Covered.Add(n);
        return true;
    }

    public string? CanBuySet() => Funds < Defs.SetCost ? $"Needs £{Defs.SetCost}" : null;

    public bool BuySet()
    {
        if (CanBuySet() != null) return false;
        Funds -= Defs.SetCost;
        ReceiverSets++;
        Emit(EventKind.Info, "A new receiver set is installed at a Y-station, and its operators trained.");
        return true;
    }

    public string? CanLayLine(string station)
    {
        if (Lines.Contains(station)) return "Line laid";
        if (Funds < Defs.LineCost) return $"Needs £{Defs.LineCost}";
        return null;
    }

    public bool LayLine(string station)
    {
        if (CanLayLine(station) != null) return false;
        Funds -= Defs.LineCost;
        Lines.Add(station);
        var y = Defs.YStations.First(s => s.Id == station);
        Emit(EventKind.Good, Stations.Any(s => s.Type == StationType.Inbox)
            ? $"The Post Office has laid a teleprinter line from {y.Name}. Its traffic now prints on the Park's teleprinters within minutes, not in the riders' bundles."
            : $"The Post Office has laid a teleprinter line from {y.Name}. Build a teleprinter (B) to receive it: until then the riders still come.");
        return true;
    }

    public string? CanBuyDf(Service sv)
    {
        if (DirectionFinding.Contains(sv)) return "Working";
        var y = Defs.YStations.First(s => s.Df == sv);
        if (ActIndex < y.DfFromAct) return "Not available yet";
        if (Funds < Defs.DfCost) return $"Needs £{Defs.DfCost}";
        return null;
    }

    public bool BuyDf(Service sv)
    {
        if (CanBuyDf(sv) != null) return false;
        Funds -= Defs.DfCost;
        DirectionFinding.Add(sv);
        Emit(EventKind.Good, $"Direction-finding stations now fix the {Defs.ServiceNames[sv]}'s transmitters. A cover story can point to their bearings.");
        return true;
    }

    /// <summary>Direction-finding makes cover stories believable: a fix explains what we knew.</summary>
    public double CoverSuspicionFactor(Service sv) => DirectionFinding.Contains(sv) ? Defs.DfCoverFactor : 1;

    /// <summary>
    /// New networks on the air take spare sets, up to what they need; a network that goes off the air gives its sets
    /// back.
    /// </summary>
    void AllotCover()
    {
        var onAir = CoveredNets().ToList();
        foreach (var n in Cover.Keys.Where(n => !onAir.Contains(n)).ToList())
        {
            Cover.Remove(n);
            Covered.Remove(n);
        }
        foreach (var n in onAir)
        {
            if (!Covered.Add(n)) continue;
            int give = Math.Min(Defs.SetsWanted - CoverOf(n), SpareSets);
            if (give > 0) Cover[n] = CoverOf(n) + give;
            if (CoverOf(n) < Defs.SetsWanted)
                Emit(EventKind.Bad, $"{Defs.NetOf(n).Name} is on the air, but {YStationOf(n).Name} has no spare sets for it. Its traffic will come in late and garbled (Y-stations, Y).");
        }
    }

    /// <summary>Each day a network may change its frequencies; the change comes at some hour of that day.</summary>
    void PlanFrequencyChanges()
    {
        if (ActIndex < Defs.FreqChangesFromAct) return;
        FreqChangeAt.Clear();
        foreach (var n in CoveredNets())
        {
            var r = new Random(Mix(Seed, Day, (int)n, 0xF4E9));
            if (r.NextDouble() < Defs.FreqChangeChance)
                FreqChangeAt[n] = Day * Defs.MinutesPerDay + r.NextDouble() * Defs.MinutesPerDay;
        }
    }

    /// <summary>Hours the search for a lost network takes: spare sets search the band, the network's own sets help a little.</summary>
    public double SearchHours(Net n) => Defs.SearchHours / (1 + Defs.SparePerSearch * SpareSets + 0.25 * Math.Min(CoverOf(n), Defs.SetsWanted));

    void TickYService()
    {
        foreach (var (n, at) in FreqChangeAt.ToList())
        {
            if (Now < at) continue;
            FreqChangeAt.Remove(n);
            if (Searching(n) || !Covered.Contains(n)) continue;
            double h = SearchHours(n);
            SearchUntil[n] = Now + h * 60;
            Stats.FreqChanges++;
            Emit(EventKind.Bad, $"{Defs.NetOf(n).Name} has changed its frequencies and gone quiet. {YStationOf(n).Name} is searching the band: " +
                                $"with {SpareSets} spare set{(SpareSets == 1 ? "" : "s")}, about {Math.Max(1, (int)Math.Round(h))} hours.");
        }
        foreach (var (n, until) in SearchUntil.ToList())
            if (Now >= until)
            {
                SearchUntil.Remove(n);
                Emit(EventKind.Good, $"{YStationOf(n).Name} has found {Defs.NetOf(n).Name} on its new frequencies. The traffic it missed was caught on repeats.");
            }
    }
}
