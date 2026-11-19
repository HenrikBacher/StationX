using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// The ULTRA dilemma: every decrypt that is acted on makes the Germans a little more suspicious.
/// Too much and they tighten procedures, and a key goes dark.
/// </summary>
public sealed partial class GameState
{
    public double SuspicionOf(Service s) => Suspicion.TryGetValue(s, out var v) ? v : 0;

    public double MaxSuspicion => Enum.GetValues<Service>().Max(SuspicionOf);

    public void AddSuspicion(Service sv, double amount)
    {
        if (amount > 0) amount *= Defs.Difficulties[Difficulty].SuspicionMult;
        double v = Math.Clamp(SuspicionOf(sv) + amount, 0, Defs.SuspicionLimit);
        Suspicion[sv] = v;
        if (v >= Defs.SuspicionWarn && SuspicionWarned.Add(sv))
            Emit(EventKind.Bad, $"Decrypts show the {Defs.ServiceNames[sv]} asking whether its ciphers are secure. " +
                                "Ease off: switch to a cautious policy or choose cover stories.");
        if (v < Defs.SuspicionWarn - 20) SuspicionWarned.Remove(sv);
        if (v >= Defs.SuspicionLimit) Lockdown(sv);
    }

    void Lockdown(Service sv)
    {
        LockdownUntil[sv] = Day + Defs.LockdownDays;
        Suspicion[sv] = 40;
        SuspicionWarned.Remove(sv);
        Emit(EventKind.Bad, $"The {Defs.ServiceNames[sv]} suspects its ciphers are compromised and has tightened procedures. " +
                            $"Its keys can't be broken for {Defs.LockdownDays + 1} days.");
    }

    void OnDecryptUsed(Message m)
    {
        AddSuspicion(Defs.NetOf(m.Net).Service, SuspicionPerDecrypt(m.Net));
        if (Dilemmas.Count == 0 && ActIndex >= 1 && Now - LastDilemmaAt > 1.5 * Defs.MinutesPerDay && Rng.Chance(0.05))
            RaiseDilemma(IntelDilemma(m.Net));
    }

    /// <summary>Suspicion added each time a decrypt from this net is used, under the current policy.</summary>
    public double SuspicionPerDecrypt(Net net) =>
        Defs.SuspicionPerDecrypt * Defs.Policies[Policy].Suspicion * (Defs.NetOf(net).Family == Family.Hand ? 0.6 : 1);

    void DailyIntelligence()
    {
        foreach (var sv in Enum.GetValues<Service>())
            if (SuspicionOf(sv) > 0) Suspicion[sv] = Math.Max(0, SuspicionOf(sv) - Defs.SuspicionDecayPerDay);
        foreach (var s in Staff.Where(s => s.Has(Trait.CarelessTalker) && !s.Leaving).ToList())
        {
            AddSuspicion(Service.Security, 1.5);
            if (Dilemmas.Count == 0 && Rng.Chance(0.07)) RaiseDilemma(TalkerIncident(s));
        }
    }

    public void SetPolicy(Policy p)
    {
        if (Policy == p) return;
        Policy = p;
        Emit(EventKind.Info, $"ULTRA policy is now {Defs.Policies[p].Name.ToLowerInvariant()}. {Defs.Policies[p].Blurb}");
    }

    // ---------------------------------------------------------------- dilemmas

    public void RaiseDilemma(Dilemma d)
    {
        d.Id = NextId++;
        d.Created = Now;
        Dilemmas.Add(d);
        LastDilemmaAt = Now;
        Emit(EventKind.Dilemma, d.Title);
    }

    /// <summary>A sample decision for screenshots and tests.</summary>
    public Dilemma DebugDilemma() => IntelDilemma(ActiveNets.Count > 0 ? ActiveNets.First() : Net.Red);
    public Dilemma DebugIntelDilemma(Net net) => IntelDilemma(net);

    const string RuthlessTitle = "Operation Ruthless";

    /// <summary>Autumn 1940: Ian Fleming's plan to pinch naval Enigma material with a captured bomber. Never mounted; here, a gamble.</summary>
    void MaybeRuthless()
    {
        if (ActIndex != Defs.RuthlessAct || Mode == GameMode.FreePlay || Dilemmas.Count > 0) return;
        if (!Stats.ScriptedDone.Add("ruthless")) return;
        RaiseDilemma(new Dilemma
        {
            Service = Service.Kriegsmarine, Title = RuthlessTitle,
            Text = "Lieutenant Commander Ian Fleming of Naval Intelligence has a plan. A captured Heinkel, crewed by German-speaking airmen in " +
                   "Luftwaffe uniform, ditches in the Channel; when a German rescue boat comes, they overpower the crew and bring back its " +
                   "Enigma settings. Hut 8 is desperate for naval material. It might work. It might also tell the Kriegsmarine exactly what we want.",
            Options =
            {
                new() { Label = "Mount the operation", Funds = -Defs.RuthlessCost,
                    Outcome = "A Heinkel is found and a crew trained. Everyone waits for a German rescue boat to take the bait." },
                new() { Label = "Shelve it",
                    Outcome = "Operation Ruthless is quietly shelved. Frank Birch writes that Turing and Twinn came to him 'like undertakers cheated of a nice corpse'." },
            },
        });
    }

    void RuthlessOutcome()
    {
        if (Rng.Chance(Defs.RuthlessSuccess))
        {
            Points += Defs.RuthlessPoints;
            AdjustRep(3, "Decisions");
            Emit(EventKind.Good, $"Operation Ruthless works. A rescue boat's key tables and a bigram table are in Hut 8 by morning. +{Defs.RuthlessPoints} breakthroughs.");
        }
        else
        {
            AddSuspicion(Service.Kriegsmarine, Defs.RuthlessSuspicion);
            AdjustRep(-2, "Decisions");
            Emit(EventKind.Bad, "Operation Ruthless goes wrong: the 'downed' Heinkel is spotted being towed out. The Kriegsmarine tightens up, suspicious.");
        }
    }

    public bool ResolveDilemma(int id, int option)
    {
        var d = Dilemmas.Find(x => x.Id == id);
        if (d == null || option < 0 || option >= d.Options.Count) return false;
        var o = d.Options[option];
        Dilemmas.Remove(d);
        Funds += o.Funds;
        if (o.Rep != 0) AdjustRep(o.Rep, "Decisions");
        if (o.Suspicion != 0) AddSuspicion(d.Service, o.Suspicion);
        if (o.Morale != 0) ChangeMorale(o.Morale);
        Points += o.Points;
        if (o.Tonnage > 0) TonnageLost += o.Tonnage;
        if (d.StaffId is int sid && StaffById(sid) is { } who)
        {
            if (o.RevealTalker) who.CarelessKnown = true;
            if (o.DismissStaff) Fire(sid);
        }
        Stats.DilemmasResolved++;
        Emit(o.Rep < 0 || o.Suspicion > 10 ? EventKind.Bad : EventKind.Info, o.Outcome);
        if (d.Title == RuthlessTitle && option == 0) RuthlessOutcome();
        if (o.Action != null) CrisisAction(o.Action, d);
        return true;
    }

    Dilemma TalkerIncident(Staff s)
    {
        var t = NewTale(0x7A1C);
        t["who"] = s.Name;
        t["role"] = Defs.Roles[s.Role].Name.ToLowerInvariant();
        t["where"] = t.Pick("the Eight Bells", "the Duke of Wellington", "the Shoulder of Mutton", "a dance at Wilton Hall", "the station buffet",
                            "a crowded train to Euston", "their billet's kitchen table", "a cinema queue in Bedford");
        t["said"] = t.Pick("'machines that read German signals'", "'how we knew where the bombers were going'", "'the Wrens and their clever machines'",
                           "'breaking the Germans' codes'", "'what the U-boats said yesterday'");
        t["heard"] = t.Pick("MI5 reports that", "The Park's security officer hears that", "A landlady writes to the Park to say that",
                            "A fellow billetee reports that", "The local police pass on that");
        var worst = Enum.GetValues<Service>().OrderByDescending(SuspicionOf).First();
        var d = Dil(worst, t, "Careless talk",
            "{heard} {who} ({role}) was overheard at {where} talking about {said}.",
            "The talk has happened; what matters is whether it happens again. Careless talkers keep raising the " +
            "Abwehr's suspicion a little every day they stay. Dismissing them stops it but costs us their work and shakes the huts; " +
            "a reprimand marks them, so you know who to watch. Ignoring it costs us standing in Whitehall.",
            new DilemmaOption { Label = "Dismiss {who}", DismissStaff = true, Morale = -0.03, Suspicion = -5, RevealTalker = true,
                Outcome = "{who} is sent away, reminded that the Official Secrets Act lasts for life." },
            new DilemmaOption { Label = "A reprimand, and they sign the Act again", Morale = -0.02, RevealTalker = true,
                Outcome = "{who} is {badly shaken|white as a sheet|close to tears}. Whether it sticks remains to be seen." },
            new DilemmaOption { Label = "Take no action", Rep = -3, Suspicion = 12,
                Outcome = "{MI5 notes our lack of interest.|The security officer writes a sharp minute.} So, perhaps, does someone else." });
        d.StaffId = s.Id;
        return d;
    }

    // ---------------------------------------------------------------- gardening

    /// <summary>
    /// Gardening: the RAF lays mines in a chosen spot, the Germans broadcast a warning with predictable text,
    /// and Hut 8 gets a ready-made crib for that key today and tomorrow.
    /// </summary>
    public string? CanGarden(Net net)
    {
        var nd = Defs.NetOf(net);
        if (!nd.Naval) return "Only for naval keys";
        if (ActIndex < 7) return "Not available yet";
        if (!ActiveNets.Contains(net)) return "Not on the air yet";
        if (Gardened(net, Day)) return "Mines already laid";
        if (LockedDown(net)) return "Procedures tightened";
        if (Funds < Defs.GardeningCost) return $"Needs £{Defs.GardeningCost}";
        return null;
    }

    public bool Garden(Net net)
    {
        if (CanGarden(net) != null) return false;
        Funds -= Defs.GardeningCost;
        GardenUntil[net] = Day + 1;
        AddSuspicion(Service.Kriegsmarine, 6);
        Emit(EventKind.Good, $"Coastal Command lays mines off {Rng.Pick(new[] { "Lorient", "Brest", "St Nazaire", "the Frisian Islands" })}. " +
                             $"The minefield warnings will give {Defs.NetOf(net).Name} cribs today and tomorrow.");
        return true;
    }

    // ---------------------------------------------------------------- outstations

    public string? CanBuyOutstation()
    {
        if (ActIndex < Defs.Station(StationType.OutstationLink).MinAct) return "Not needed yet";
        if (!Researched.Contains(ResearchId.Bombe)) return "Research the Bombe";
        if (OutstationBombes >= Defs.OutstationSites.Length) return "Every site is in use";
        if (Funds < Defs.OutstationBombeCost) return $"Needs £{Defs.OutstationBombeCost}";
        return null;
    }

    public bool BuyOutstation()
    {
        if (CanBuyOutstation() != null) return false;
        Funds -= Defs.OutstationBombeCost;
        var site = Defs.OutstationSites[OutstationBombes];
        OutstationBombes++;
        Emit(EventKind.Good, $"A Bombe is installed at {site}, run by Wrens billeted nearby. " +
                             (HasStation(StationType.OutstationLink) ? "" : "Build an outstation teleprinter to send it menus."));
        return true;
    }

    public IEnumerable<string> OutstationNames => Defs.OutstationSites.Take(OutstationBombes);

    int OutstationQueued =>
        RemoteJobs.Count + Stations.Where(s => s.Type == StationType.OutstationLink)
            .Sum(s => s.Input.Count + s.ReservedInputs + (s.Current != null ? 1 : 0));

    bool OutstationTakes(StationType next) =>
        next is StationType.Bombe or StationType.Bombe4 &&
        (next == StationType.Bombe || Researched.Contains(ResearchId.FourRotorBombe)) &&
        OutstationQueued < OutstationBombes;
}
