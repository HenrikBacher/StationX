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

    static readonly string[] Cities = { "Liverpool", "Plymouth", "Bristol", "Hull", "Southampton", "Belfast", "Clydebank", "Portsmouth", "Birmingham", "Sheffield" };
    static readonly string[] Towns = { "Ely", "Peterborough", "Ramsey", "Banbury", "Thetford", "Huntingdon" };
    static readonly string[] Ports = { "Naples", "Taranto", "Palermo", "Brindisi" };

    Dilemma IntelDilemma(Net net)
    {
        var nd = Defs.NetOf(net);
        var name = nd.Name;
        if (nd.Family == Family.Lorenz)
            return new Dilemma
            {
                Service = Service.Heer, Title = "Orders from the high command",
                Text = "TUNNY decrypts give the German high command's plans for the next offensive in the east, " +
                       "in more detail than Moscow has ever had.",
                Options =
                {
                    new() { Label = "Pass it to Moscow disguised as a spy report", Rep = 4, Suspicion = 4,
                        Outcome = "The Soviets receive it from a 'source in Switzerland'. They act on it, and no one asks about teleprinters." },
                    new() { Label = "Tell Moscow it comes from decrypts", Rep = 2, Suspicion = 16,
                        Outcome = "Soviet ciphers are not as secure as ours. Within weeks German signals mention a leak." },
                    new() { Label = "Keep it in London", Rep = -3,
                        Outcome = "The offensive comes as a surprise to our ally. Whitehall is unimpressed." },
                },
            };
        return nd.Service switch
        {
            Service.Luftwaffe => Rng.Chance(0.6) ? RaidDilemma(name) : new Dilemma
            {
                Service = Service.Luftwaffe, Title = "Airfield strengths",
                Text = $"A {name} decrypt lists the serviceable aircraft at every Luftwaffe airfield in northern France.",
                Options =
                {
                    new() { Label = "Bomb the weakest-defended airfields tonight", Rep = 3, Suspicion = 12,
                        Outcome = "Bomber Command hits exactly the right airfields. Luftflotte 3 starts an inquiry." },
                    new() { Label = "Send a photo-reconnaissance flight to 'discover' them first", Funds = -150, Rep = 2, Suspicion = 3,
                        Outcome = "A Spitfire photographs the airfields. Nobody suspects the photographs." },
                    new() { Label = "File it for the order of battle", Points = 4,
                        Outcome = "Hut 3's index grows. The analysts are pleased." },
                },
            },
            Service.Heer => new Dilemma
            {
                Service = Service.Heer, Title = "A convoy for Rommel",
                Text = $"{name} gives the sailing time and route of a tanker convoy leaving {Rng.Pick(Ports)} for Libya.",
                Options =
                {
                    new() { Label = "Strike from Malta at once", Rep = 5, Suspicion = 18,
                        Outcome = "The tankers burn. Rome and Berlin each accuse the other of leaking the sailing orders." },
                    new() { Label = "Send a reconnaissance plane to 'find' them first", Funds = -150, Rep = 3, Suspicion = 4,
                        Outcome = "A Maryland is seen overhead before the strike. The cover story holds." },
                    new() { Label = "Let this one through", Rep = -2, Suspicion = -3,
                        Outcome = "The fuel reaches Rommel. The secret is safe." },
                },
            },
            Service.Kriegsmarine => Rng.Chance(0.5) ? new Dilemma
            {
                Service = Service.Kriegsmarine, Title = "A U-boat rendezvous",
                Text = $"{name} shows a tanker will refuel a wolf pack west of the Azores in three days.",
                Options =
                {
                    new() { Label = "Sink the tanker and the U-boats", Rep = 5, Suspicion = 20,
                        Outcome = "Escort carriers are waiting. Dönitz demands to know how they knew." },
                    new() { Label = "Quietly route the convoys around it", Rep = 2, Suspicion = 3,
                        Outcome = "The convoys sail clear. To the Germans it looks like good luck." },
                    new() { Label = "Do nothing", Suspicion = -3, Tonnage = 30000,
                        Outcome = "The wolf pack refuels and finds a convoy. Ships are lost; the secret is safe." },
                },
            } : new Dilemma
            {
                Service = Service.Kriegsmarine, Title = "The supply ships",
                Text = $"{name} gives the positions of nine supply ships waiting in the Atlantic for raiders and U-boats.",
                Options =
                {
                    new() { Label = "Sink all of them", Rep = 6, Suspicion = 28,
                        Outcome = "As in June 1941, the Navy sweeps them up. Berlin investigates, and only luck blames spies." },
                    new() { Label = "Sink two and leave the rest", Rep = 3, Suspicion = 8,
                        Outcome = "Two ships lost could be bad luck. The Germans shrug." },
                    new() { Label = "Leave them be", Rep = -1,
                        Outcome = "The Admiralty grumbles, but the secret is safe." },
                },
            },
            _ => new Dilemma
            {
                Service = Service.Security, Title = "An agent by parachute",
                Text = $"{name} traffic says the Abwehr is dropping an agent near {Rng.Pick(Towns)} on Thursday night.",
                Options =
                {
                    new() { Label = "Arrest him quietly and offer him to the Twenty Committee", Rep = 3, Points = 6, Suspicion = 3,
                        Outcome = "He takes the offer. Soon he is sending the Abwehr exactly what MI5 wants it to hear." },
                    new() { Label = "Arrest him publicly", Rep = 3, Suspicion = 12,
                        Outcome = "The newspapers love it. The Abwehr wonders how the police were waiting in the right field." },
                    new() { Label = "Let him land and watch", Rep = -1, Points = 2,
                        Outcome = "He is followed for a week, then picked up by a sharp-eyed village constable." },
                },
            },
        };
    }

    Dilemma RaidDilemma(string name)
    {
        var city = Rng.Pick(Cities);
        return new Dilemma
        {
            Service = Service.Luftwaffe, Title = $"A raid on {city}",
            Text = $"{name} decrypts give tonight's target: {city}, with about {Rng.Range(8, 30) * 10} bombers.",
            Options =
            {
                new() { Label = "Warn the city and send the night fighters", Rep = 4, Suspicion = 16,
                    Outcome = $"The raiders find the fighters waiting over {city}. Someone in Berlin asks how the RAF knew." },
                new() { Label = "Pass it on as a radar and Y-service warning", Funds = -100, Rep = 2, Suspicion = 4,
                    Outcome = "Fighter Command acts on 'radar plots'. The cover story holds." },
                new() { Label = "Keep it to ourselves", Rep = -2, Morale = -0.05, Suspicion = -3,
                    Outcome = $"The raid on {city} meets only the usual defences. The secret is safe; the watch takes it hard." },
            },
        };
    }

    Dilemma TalkerIncident(Staff s)
    {
        var pub = Rng.Pick(new[] { "the Eight Bells", "the Duke of Wellington", "the Shoulder of Mutton", "a dance at Wilton Hall" });
        var worst = Enum.GetValues<Service>().OrderByDescending(SuspicionOf).First();
        return new Dilemma
        {
            Service = worst, StaffId = s.Id, Title = "Careless talk",
            Text = $"MI5 reports that {s.Name} ({Defs.Roles[s.Role].Name.ToLowerInvariant()}) was overheard at {pub} " +
                   "talking about 'machines that read German signals'.",
            Options =
            {
                new() { Label = $"Dismiss {s.Name}", DismissStaff = true, Morale = -0.03, Suspicion = -5, RevealTalker = true,
                    Outcome = $"{s.Name} is sent away, reminded that the Official Secrets Act lasts for life." },
                new() { Label = "A reprimand, and they sign the Act again", Morale = -0.02, Suspicion = 4, RevealTalker = true,
                    Outcome = $"{s.Name} is badly shaken. Whether it sticks remains to be seen." },
                new() { Label = "Take no action", Rep = -3, Suspicion = 12,
                    Outcome = "MI5 notes our lack of interest. So, perhaps, does someone else." },
            },
        };
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
