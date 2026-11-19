using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// The deck of ULTRA decisions. Each scenario belongs to a German service (or to TUNNY), may be tied to the weeks it
/// happened in, and is filled by a <see cref="Tale"/>, so the same decision reads differently each time: other cities,
/// ships, units, sources and stakes. Options keep one shape throughout: act openly, act under a cover story (the
/// middle choice), or hold back; the stakes scale rewards and risks together.
/// </summary>
public sealed partial class GameState
{
    /// <summary>The scenarios raised lately, so the same one doesn't come round again too soon.</summary>
    public List<string> RecentScenarios { get; set; } = new();

    sealed record Scenario(string Id, Func<Tale, bool> When, Func<Tale, Dilemma> Make, double Weight = 1);

    /// <summary>A tale with its own dice, so drawing words never shifts any other roll in the game.</summary>
    Tale NewTale(int salt) => new(new Random(Mix(Seed, NextId, (int)Now, salt)), Date);

    Dilemma IntelDilemma(Net net)
    {
        var nd = Defs.NetOf(net);
        var t = NewTale(0x1D7E);
        t.Stakes = Math.Round(t.Range(0.75, 1.3), 2);
        t["net"] = nd.Name;
        var deck = UltraDeck(nd).Where(s => s.When(t)).ToList();
        var fresh = deck.Where(s => !RecentScenarios.Contains(s.Id)).ToList();
        if (fresh.Count == 0) fresh = deck;
        double x = t.Range(0.0, fresh.Sum(s => s.Weight));
        var pick = fresh[^1];
        foreach (var s in fresh)
            if ((x -= s.Weight) < 0) { pick = s; break; }
        RecentScenarios.Add(pick.Id);
        if (RecentScenarios.Count > 8) RecentScenarios.RemoveAt(0);
        return pick.Make(t);
    }

    /// <summary>Every scenario id, for the tests.</summary>
    public IEnumerable<string> ScenarioIds(Net net) => UltraDeck(Defs.NetOf(net)).Select(s => s.Id);

    /// <summary>A scenario by id, for the tests and screenshots.</summary>
    public Dilemma ScenarioById(Net net, string id, DateOnly date, int seed)
    {
        var t = new Tale(new Random(seed), date) { Stakes = 1 };
        t["net"] = Defs.NetOf(net).Name;
        return UltraDeck(Defs.NetOf(net)).First(s => s.Id == id).Make(t);
    }

    /// <summary>How many scenarios could come up for this network on this date, for the tests.</summary>
    public int ScenarioCount(Net net, DateOnly date)
    {
        var t = new Tale(new Random(1), date);
        return UltraDeck(Defs.NetOf(net)).Count(s => s.When(t));
    }

    static DilemmaOption Opt(string label, string outcome, double rep = 0, double suspicion = 0, int funds = 0,
                             int points = 0, double morale = 0, double tonnage = 0) =>
        new() { Label = label, Outcome = outcome, Rep = rep, Suspicion = suspicion, Funds = funds, Points = points, Morale = morale, Tonnage = tonnage };

    static Dilemma Dil(Service sv, Tale t, string title, string text, string context, params DilemmaOption[] options)
    {
        var d = new Dilemma { Service = sv, Title = t.Fill(title), Text = t.Fill(text), Context = t.Fill(context) };
        foreach (var o in options)
        {
            o.Label = t.Fill(o.Label);
            o.Outcome = t.Fill(o.Outcome);
            d.Options.Add(o);
        }
        return d;
    }

    /// <summary>How the decrypt reached us, in the words of the hut that read it.</summary>
    static void HowRead(Tale t, Service sv, bool police = false)
    {
        t["read"] = police ? t.Fill("The section that reads the German police's hand ciphers {had it out overnight|broke it this morning|read it within hours}.") : sv switch
        {
            Service.Kriegsmarine => t.Fill("Hut 8 broke the day's key {this morning|before lunch|in the small hours|after a long night}, and Hut 4 had it translated {within the hour|by teatime|before the watch changed}."),
            Service.Security => t.Fill("{The section that reads the Abwehr's ciphers|Oliver Strachey's section} {broke it this morning|had it out overnight|read it within hours}."),
            _ => t.Fill("Hut 6 broke the key {this afternoon|before lunch|in the small hours|after a long night}, and Hut 3 had it translated {within the hour|by teatime|before the watch changed}."),
        };
        t["sure"] = t.Pick("The text is clean and complete.", "Two groups are corrupt, but the meaning is plain.",
                           "A second message an hour later confirms it.", "It fits everything the index already holds.");
    }

    static readonly string[] BlitzCities = { "Liverpool", "Plymouth", "Bristol", "Hull", "Southampton", "Belfast", "Clydebank", "Portsmouth", "Birmingham", "Sheffield", "Coventry", "Cardiff", "Swansea", "Manchester" };
    static readonly string[] BaedekerCities = { "Exeter", "Bath", "Norwich", "York", "Canterbury" };
    static readonly string[] CoastTowns = { "Hull", "Great Yarmouth", "Lowestoft", "Plymouth", "Falmouth", "Hastings", "Eastbourne", "Bournemouth", "Brighton" };
    static readonly string[] FighterFields = { "Biggin Hill", "Kenley", "Manston", "Hornchurch", "North Weald", "Detling", "Tangmere", "Debden" };
    static readonly string[] DropTowns = { "Ely", "Peterborough", "Ramsey", "Banbury", "Thetford", "Huntingdon", "Luton", "Dumfries", "Aylesbury" };
    static readonly string[] ItalianPorts = { "Naples", "Taranto", "Palermo", "Brindisi" };
    static readonly string[] ItalianShips = { "Proserpina", "Tergestea", "San Andrea", "Pozarica", "Panuco", "Ankara", "Monviso", "Rosolino Pilo" };

    IEnumerable<Scenario> UltraDeck(NetDef nd)
    {
        if (nd.Family == Family.Lorenz) return TunnyDeck();
        return nd.Service switch
        {
            Service.Luftwaffe => LuftwaffeDeck(nd),
            Service.Heer => HeerDeck(),
            Service.Kriegsmarine => NavalDeck(),
            _ => SecurityDeck(nd),
        };
    }

    // ------------------------------------------------------------------ Luftwaffe

    IEnumerable<Scenario> LuftwaffeDeck(NetDef nd)
    {
        bool med = nd.Id == Net.LightBlue;

        yield return new Scenario("raid", t => !med && t.From(1940, 9, 7), t =>
        {
            HowRead(t, Service.Luftwaffe);
            bool baedeker = t.Between(1942, 4, 23, 1942, 6, 30);
            t["city"] = baedeker ? t.Pick(BaedekerCities) : t.Before(1941, 5, 17) ? t.Pick(BlitzCities) : t.Pick(CoastTowns);
            t["bombers"] = (t.S(t.Range(8, 30)) * 10).ToString();
            t["when"] = t.Pick("tonight", "tomorrow night", "at dusk");
            return Dil(Service.Luftwaffe, t, "A raid on {city}",
                "{net} decrypts give the target for {when}: {city}, with about {bombers} bombers.",
                "A Fliegerkorps order to its bomber groups: target, numbers and time over target. {read} {sure} " +
                (baedeker ? "These are the 'Baedeker' raids, on old cities chosen from a tourist guide. " : "") +
                "Only Enigma could have told us this early. Night fighters waiting over a city before any radar plot " +
                "point straight at it; passed on as a radar and Y-service warning, the defences are readier but not suspiciously so.",
                Opt("Warn {city} and send the night fighters", "The raiders find the fighters waiting over {city}. {Someone in Berlin asks how the RAF knew.|Luftflotte 3 orders an inquiry into the losses.}", rep: t.S(4), suspicion: t.S(16)),
                Opt("Pass it on as a radar and Y-service warning", "Fighter Command acts on 'radar plots'. {The cover story holds.|Nobody in Berlin looks further than the radar towers.}", rep: t.S(2), suspicion: t.S(4), funds: -100),
                Opt("Keep it to ourselves", "The raid on {city} meets only the usual defences. The secret is safe; the watch takes it hard.", rep: -t.S(2), suspicion: -3, morale: -0.05));
        }, 2);

        yield return new Scenario("adler", t => !med && t.Between(1940, 7, 10, 1940, 9, 15), t =>
        {
            HowRead(t, Service.Luftwaffe);
            t["field"] = t.Pick(FighterFields);
            t["hour"] = t.Pick("noon", "one o'clock", "teatime", "first light");
            return Dil(Service.Luftwaffe, t, "An attack on {field}",
                "{net} gives tomorrow's targets: the fighter airfield at {field}, about {hour}, with a strong fighter escort.",
                "A Luftflotte's operation order for the next day. {read} {sure} Squadrons already airborne and waiting over " +
                "{field} before the radar sees anything would be hard to explain; ordinary readiness and an early radar warning would not. " +
                "Keep it back and the field takes the raid as it comes.",
                Opt("Have squadrons in the air over {field} before they come", "The raiders run into a wall of Hurricanes and Spitfires. Göring wants to know who talked.", rep: t.S(4), suspicion: t.S(14)),
                Opt("Bring {field} to readiness and trust the radar", "The squadrons are at readiness when the plots appear, and scramble in good time. It looks like radar.", rep: t.S(2), suspicion: t.S(3), funds: -100),
                Opt("Leave {field} to the usual warning", "The raid catches some aircraft on the ground. The hangars burn; the secret holds.", rep: -t.S(2), suspicion: -3, morale: -0.03));
        }, 3);

        yield return new Scenario("mines", t => !med && t.Before(1940, 5, 10), t =>
        {
            HowRead(t, Service.Luftwaffe);
            t["water"] = t.Pick("the Thames estuary", "the approaches to Harwich", "the Humber", "the Firth of Forth");
            return Dil(Service.Luftwaffe, t, "Mines by night",
                "{net} traffic says seaplanes will drop mines in {water} {tonight|tomorrow night}: the new magnetic kind.",
                "A minelaying order to a coastal Gruppe: the area, the night, the number of aircraft. {read} {sure} " +
                "Fighters waiting over the water at night would be remarkable; closing the channel 'for routine sweeping' is not. " +
                "A mine recovered intact would tell the Admiralty how to beat it.",
                Opt("Have fighters and guns waiting over {water}", "Two seaplanes are shot down. The rest turn back, and someone in Germany wonders why.", rep: t.S(3), suspicion: t.S(12)),
                Opt("Close the channel 'for sweeping' and watch where they fall", "Shipping waits a day. Observers mark where the mines fall, and one lands on a mudflat at low tide.", rep: t.S(2), suspicion: t.S(3), points: 1),
                Opt("Let the ships sail as usual", "The mines are laid. A collier is lost off the estuary next morning; the secret is safe.", rep: -t.S(2), suspicion: -3, tonnage: t.S(4000)));
        }, 2);

        yield return new Scenario("dunkirk", t => !med && t.Between(1940, 5, 24, 1940, 6, 4), t =>
        {
            HowRead(t, Service.Luftwaffe);
            return Dil(Service.Luftwaffe, t, "Stukas for the beaches",
                "{net} gives the times of tomorrow's dive-bomber attacks on the ships off Dunkirk.",
                "Fliegerkorps VIII's orders to its Stuka groups: waves, times and targets. {read} {sure} " +
                "Fighter Command can put squadrons over the beaches exactly when the waves arrive, which would save ships and men " +
                "and look a great deal like foreknowledge; or thicken its patrols across the day, which saves fewer and looks like effort.",
                Opt("Put the squadrons over the beaches at the times given", "The Stukas meet Spitfires at every wave. More ships get away; a Luftwaffe staff officer starts asking questions.", rep: t.S(5), suspicion: t.S(14)),
                Opt("Thicken the patrols over the beaches all day", "The patrols are thinner but always there. Some ships are saved; it looks like a stubborn RAF.", rep: t.S(2), suspicion: t.S(3), funds: -100),
                Opt("Leave the patrols as planned", "The waves come in unopposed. Ships are lost off the beaches; the secret is safe.", rep: -t.S(3), suspicion: -3, morale: -0.04));
        }, 4);

        yield return new Scenario("strengths", t => true, t =>
        {
            HowRead(t, Service.Luftwaffe);
            t["where"] = med ? t.Pick("Sicily", "Libya", "Crete") : t.From(1940, 6, 25) ? t.Pick("northern France", "Belgium", "Brittany") : t.Pick("the Rhineland", "north-west Germany");
            return Dil(Service.Luftwaffe, t, "Airfield strengths",
                "A {net} decrypt lists the serviceable aircraft at every Luftwaffe airfield in {where}.",
                "A Luftflotte's daily strength return, read in full: which airfields are crowded and which are thin. {read} " +
                "The figures are a day old and good for perhaps two more. Bombing exactly the weak ones points at Enigma; " +
                "a reconnaissance flight first gives the RAF a plausible reason to know. Filed, it builds Hut 3's order of battle.",
                Opt("Bomb the crowded airfields tonight", "Bomber Command hits exactly the right airfields. The Luftflotte starts an inquiry.", rep: t.S(3), suspicion: t.S(12)),
                Opt("Send a photo-reconnaissance flight to 'discover' them first", "A Spitfire photographs the airfields. Nobody suspects the photographs.", rep: t.S(2), suspicion: t.S(3), funds: -150),
                Opt("File it for the order of battle", "Hut 3's index grows. The analysts are pleased.", points: t.S(4)));
        });

        yield return new Scenario("beams", t => !med && t.Between(1940, 9, 1, 1941, 5, 16), t =>
        {
            HowRead(t, Service.Luftwaffe);
            t["city"] = t.Pick(BlitzCities);
            return Dil(Service.Luftwaffe, t, "The beams are on {city}",
                "{net} gives the settings of tonight's navigation beams: they cross over {city}.",
                "The beam transmitters' orders, with bearings to the minute. {read} {sure} The scientists can jam or bend the beams, " +
                "and decoy fires can be lit where the bombers will think the target is. Jamming exactly tonight's beams, and nothing else, " +
                "is a giveaway; the decoys are lit on many nights and prove nothing.",
                Opt("Jam the beams and put the night fighters along them", "The beams go mushy and the night fighters fly up them. The raid scatters; the beam engineers suspect a leak.", rep: t.S(4), suspicion: t.S(14)),
                Opt("Light STARFISH decoy fires near {city}", "Much of the raid falls on burning fields outside {city}. Decoys are lit most nights; nobody reads anything into it.", rep: t.S(2), suspicion: t.S(3), funds: -100),
                Opt("Leave the beams alone tonight", "The bombers find {city}. The secret holds; the watch hears the casualty figures in the morning.", rep: -t.S(2), suspicion: -3, morale: -0.05));
        }, 2);

        yield return new Scenario("eastward", t => !med && t.Between(1941, 3, 20, 1941, 6, 21), t =>
        {
            HowRead(t, Service.Luftwaffe);
            return Dil(Service.Luftwaffe, t, "Moving east",
                "{net} shows bomber and fighter Gruppen leaving France for airfields in Poland, and railway flak following them.",
                "A run of transfer orders over weeks, now unmistakable: the Luftwaffe is moving against Russia. {read} " +
                "Stalin trusts nothing British. Telling him the source would make him listen, perhaps, and Soviet ciphers are not safe. " +
                "In April 1941 Churchill sent him a short warning through the ambassador without saying how he knew.",
                Opt("Tell Moscow everything, source and all", "Moscow is told plainly. Stalin calls it a British provocation, and the Germans hear of the warning.", rep: t.S(3), suspicion: t.S(14)),
                Opt("Send a short warning through the ambassador", "The warning goes to Moscow without a source. Stalin dismisses it, but the record shows we warned him.", rep: t.S(2), suspicion: t.S(2)),
                Opt("File it and watch", "Hut 3 keeps the transfers on its maps. When the blow falls, Whitehall asks why we said nothing.", rep: -t.S(2), points: t.S(3)));
        }, 4);

        yield return new Scenario("crete", t => med && t.Between(1941, 5, 1, 1941, 5, 31), t =>
        {
            HowRead(t, Service.Luftwaffe);
            return Dil(Service.Luftwaffe, t, "Paratroops for Crete",
                "{net} gives Operation MERKUR: paratroops will drop on Maleme airfield and the towns along the north coast of Crete.",
                "Fliegerkorps XI's plan, almost entire: drop zones, days and numbers. {read} General Freyberg commands on Crete. " +
                "If he is told the source he can ring Maleme with every gun he has, and the Germans will see that he knew. " +
                "In May 1941 he was told it came from a spy in Athens, and forbidden to move in ways that would give it away.",
                Opt("Tell Freyberg the source and let him fortify Maleme", "Maleme is ringed with guns and the first wave is cut to pieces. Berlin wants to know how the drop zones leaked.", rep: t.S(6), suspicion: t.S(22)),
                Opt("Pass it as a report from an agent in Athens", "Freyberg gets the plan from 'a source in Athens' and spreads his forces. The secret holds.", rep: t.S(2), suspicion: t.S(4)),
                Opt("Let the island's command rely on its own reconnaissance", "The drop comes as a surprise. Crete falls; the secret is safe.", rep: -t.S(3), suspicion: -3));
        }, 5);

        yield return new Scenario("airlift", t => med, t =>
        {
            HowRead(t, Service.Luftwaffe);
            t["route"] = t.From(1942, 11, 8) ? t.Pick("Sicily to Tunis", "Trapani to Bizerta") : t.Pick("Crete to Tobruk", "Sicily to Tripoli", "Brindisi to Derna");
            t["n"] = t.S(t.Range(30, 70)).ToString();
            return Dil(Service.Luftwaffe, t, "Transports over the sea",
                "{net} gives the times of tomorrow's Ju 52 transport flights from {route}: about {n} aircraft, with a light escort.",
                "The air transport command's schedule. {read} {sure} Fighters from Malta waiting on the exact track would make a slaughter " +
                "and a mystery; a radar-carrying Wellington 'happening' to be on patrol first gives a reason.",
                Opt("Send the Malta fighters to the exact track", "The transports fall into the sea one after another. A Luftwaffe signals officer is sent to investigate.", rep: t.S(4), suspicion: t.S(14)),
                Opt("Let a patrol 'find' them first, then strike", "A Wellington reports the formation, and the fighters arrive soon after. It looks like a lucky patrol.", rep: t.S(2), suspicion: t.S(4), funds: -150),
                Opt("File it for the order of battle", "The schedule goes into Hut 3's index. The transports land.", points: t.S(4)));
        }, 2);
    }

    // ------------------------------------------------------------------ Army

    IEnumerable<Scenario> HeerDeck()
    {
        yield return new Scenario("convoy", t => true, t =>
        {
            HowRead(t, Service.Heer);
            t["port"] = t.Pick(ItalianPorts);
            t["ship"] = t.Pick(ItalianShips);
            t["cargo"] = t.Pick("fuel", "fuel and ammunition", "tanks and lorries", "aviation spirit");
            t["dest"] = t.From(1942, 11, 8) ? "Tunisia" : t.Pick("Tripoli", "Benghazi", "Tobruk");
            return Dil(Service.Heer, t, "A convoy for Rommel",
                "{net} gives the sailing time and route of the {ship}, carrying {cargo} from {port} to {dest}.",
                "The sailing signal to the escort, read within hours: ships, times and route. {read} The Afrika Korps runs on this {cargo}. " +
                "The rule for Malta is that no strike goes in unless a reconnaissance aircraft has been seen to 'find' the ships first. " +
                "Every convoy that arrives untouched also reassures Rome that its signals are safe.",
                Opt("Strike from Malta at once", "The {ship} burns. Rome and Berlin each accuse the other of leaking the sailing orders.", rep: t.S(5), suspicion: t.S(18)),
                Opt("Send a reconnaissance plane to 'find' them first", "A Maryland is seen overhead before the strike. The {ship} is hit, and the cover story holds.", rep: t.S(3), suspicion: t.S(4), funds: -150),
                Opt("Let this one through", "The {cargo} reaches {dest}. The secret is safe.", rep: -t.S(2), suspicion: -3));
        }, 2);

        yield return new Scenario("fuel", t => t.From(1941, 3, 1), t =>
        {
            HowRead(t, Service.Heer);
            t["days"] = t.Range(2, 6).ToString();
            return Dil(Service.Heer, t, "Running on empty",
                "{net} gives the Panzerarmee's own fuel return: stocks for {days} days of movement, and nothing on the way.",
                "The chief supply officer's report to Berlin, read the same day. {read} {sure} An army that knows its enemy is dry can attack now; " +
                "but a general who moves the hour Rommel runs short is a general who is being told. Patrols and captured documents " +
                "could have shown some of it.",
                Opt("Tell the Eighth Army to attack while he is dry", "The Eighth Army strikes and the Panzers cannot manoeuvre. Rommel's staff wonders aloud how the British always know.", rep: t.S(5), suspicion: t.S(14)),
                Opt("Let it reach the army as 'patrol reports and prisoners' talk'", "The army presses a little harder, as if on a hunch. Nobody suspects.", rep: t.S(3), suspicion: t.S(4), funds: -100),
                Opt("File it for the order of battle", "Hut 3's supply index grows. The desert stays quiet.", points: t.S(4)));
        });

        yield return new Scenario("offensive", t => t.From(1941, 3, 1), t =>
        {
            HowRead(t, Service.Heer);
            t["plan"] = t.Between(1942, 8, 1, 1942, 9, 5) ? "attack at Alam Halfa, swinging round the southern flank"
                      : t.Between(1942, 5, 1, 1942, 6, 15) ? "go round the south end of the Gazala line, past Bir Hacheim"
                      : t.Pick("attack at dawn on the coast road", "hook south through the desert at night", "pull back to a shorter line");
            return Dil(Service.Heer, t, "Rommel's intentions",
                "{net} decrypts give Rommel's plan for his next move: to {plan}.",
                "The Panzerarmee's report of intention to Berlin. {read} {sure} Knowing where the blow will fall, our army can be waiting in exactly " +
                "the right place, which is how ULTRA gets noticed. Moving most of the way there, as a careful general might, is safer.",
                Opt("Put our armour exactly where he will come", "The Panzers run into dug-in tanks and guns. The attack fails, and Rommel suspects treachery in Rome.", rep: t.S(5), suspicion: t.S(16)),
                Opt("Strengthen that flank as any careful general would", "Our line is thicker where he comes. The attack is held at a price. It looks like good generalship.", rep: t.S(3), suspicion: t.S(4), funds: -100),
                Opt("Leave the dispositions as they are", "The attack comes as a surprise and gains ground. The secret holds.", rep: -t.S(3), suspicion: -3));
        }, 2);

        yield return new Scenario("orbat", t => true, t =>
        {
            HowRead(t, Service.Heer);
            t["div"] = t.Pick("a Panzer division", "a motorised division", "two infantry divisions", "a parachute brigade");
            t["where"] = t.From(1941, 2, 1) ? t.Pick("Sicily", "Tripolitania", "the Balkans", "southern Italy") : t.Pick("the Low Countries", "Norway", "the Rhineland");
            return Dil(Service.Heer, t, "A division on the move",
                "{net} shows {div} being moved to {where}, with its trains timed to the hour.",
                "A run of movement orders and railway timetables. {read} Bombing the trains at the exact stations would hurt it badly; " +
                "telling the commanders where it is going helps them plan without anything being seen. Either way, Hut 3's picture improves.",
                Opt("Bomb the trains at the stations given", "The railway yards are hit as the trains come in. A German staff officer remarks on the RAF's timing.", rep: t.S(4), suspicion: t.S(13)),
                Opt("Warn the commanders, and let them plan around it", "The theatre's planners redraw their maps. Nothing visible happens at all.", rep: t.S(2), suspicion: t.S(2)),
                Opt("File it for the order of battle", "Hut 3 adds the division to its index.", points: t.S(4)));
        });
    }

    // ------------------------------------------------------------------ Navy

    IEnumerable<Scenario> NavalDeck()
    {
        yield return new Scenario("rendezvous", t => true, t =>
        {
            HowRead(t, Service.Kriegsmarine);
            t["tanker"] = t.From(1942, 3, 1) ? "a supply U-boat, one of the 'milch cows'," : "a tanker";
            t["where"] = t.Pick("west of the Azores", "south of Greenland", "off the Cape Verde Islands", "north of the Azores");
            t["days"] = t.Pick("two", "three", "four");
            return Dil(Service.Kriegsmarine, t, "A U-boat rendezvous",
                "{net} shows {tanker} will refuel a wolf pack {where} in {days} days.",
                "U-boat Command's orders to the boats and their supply ship: a position and a time. {read} {sure} " +
                "Sinking them at a secret rendezvous is hard to explain any other way; moving the convoys is invisible but leaves the pack to hunt. " +
                "Do nothing, and the refuelled pack will find someone.",
                Opt("Sink the supply ship and the U-boats", "Escorts are waiting {where}. Dönitz demands to know how they knew.", rep: t.S(5), suspicion: t.S(20)),
                Opt("Quietly route the convoys around it", "The convoys sail clear. To the Germans it looks like good luck.", rep: t.S(2), suspicion: t.S(3)),
                Opt("Do nothing", "The wolf pack refuels and finds a convoy. Ships are lost and the Admiralty is bitter; the secret is safe.", rep: -t.S(2), suspicion: -3, tonnage: t.S(30000)));
        }, 2);

        yield return new Scenario("supplyships", t => t.Before(1942, 1, 1), t =>
        {
            HowRead(t, Service.Kriegsmarine);
            t["n"] = t.Range(6, 10).ToString();
            return Dil(Service.Kriegsmarine, t, "The supply ships",
                "{net} gives the positions of {n} supply ships waiting in the Atlantic for raiders and U-boats.",
                "A whole season's supply plan, read from the naval key: where each ship waits and for whom. {read} " +
                "Take them all and the raiders and U-boats starve, but {n} sinkings in a week can't all be luck. Two might be.",
                Opt("Sink all of them", "The Navy sweeps them up, as in June 1941. Berlin investigates, and only luck blames spies.", rep: t.S(6), suspicion: t.S(28)),
                Opt("Sink two and leave the rest", "Two ships lost could be bad luck. The Germans shrug.", rep: t.S(3), suspicion: t.S(8)),
                Opt("Leave them be", "The Admiralty grumbles, but the secret is safe.", rep: -t.S(1)));
        });

        yield return new Scenario("weathership", t => t.Before(1942, 2, 1), t =>
        {
            HowRead(t, Service.Kriegsmarine);
            t["ship"] = t.Pick("München", "Lauenburg", "Sachsenwald", "Coburg");
            t["where"] = t.Pick("north-east of Iceland", "off Jan Mayen", "in the Denmark Strait");
            return Dil(Service.Kriegsmarine, t, "A weather ship",
                "{net} gives the position of the weather trawler {ship}, alone on station {where}. She carries next month's key tables.",
                "Weather ships sit at sea for weeks, so they carry the naval key for months ahead. {read} " +
                "A boarding party could take the tables before the crew throws them over the side, and a month of the naval key would " +
                "be read as it is sent. But a lone trawler boarded and not sunk, followed by a month of perfect convoy routing, would be noticed. " +
                "The boarders can make a show of having sunk her.",
                Opt("Board her openly and bring her in", "The {ship} is brought into Scapa Flow with her tables intact. The Kriegsmarine notes that she was taken, not sunk.", rep: t.S(2), suspicion: t.S(14), points: t.S(6)),
                Opt("Board her, take the tables, and sink her for show", "The boarders take the tables and sink the {ship} with guns firing for the record. The Germans think her papers went down with her.", suspicion: t.S(5), funds: -150, points: t.S(4)),
                Opt("Leave her: one trawler isn't worth the risk", "The {ship} keeps sending her weather reports. The secret is safe.", rep: -t.S(1)));
        });

        yield return new Scenario("raider", t => true, t =>
        {
            HowRead(t, Service.Kriegsmarine);
            t["what"] = t.Pick("a blockade runner from Bordeaux, outward bound for Japan with machine tools", "a disguised merchant raider",
                               "a blockade runner inbound from Japan with rubber", "a raider's supply tanker");
            t["where"] = t.Pick("off Cape Finisterre", "in the South Atlantic", "west of the Canaries", "in the Bay of Biscay");
            return Dil(Service.Kriegsmarine, t, "A ship that should not get through",
                "{net} gives the route and timings of {what}, now {where}.",
                "Escort and rendezvous orders for a lone ship. {read} {sure} A cruiser sent straight to her points to Enigma; " +
                "a patrolling aircraft 'happening' on her first gives a reason. Let her go and her cargo arrives.",
                Opt("Send a cruiser straight to her", "She is intercepted and scuttles herself. The Kriegsmarine asks how a lone ship was found.", rep: t.S(4), suspicion: t.S(14)),
                Opt("Let a patrolling aircraft find her first", "A Sunderland 'sights' her and the cruiser follows. Bad luck, as far as the Germans can see.", rep: t.S(2), suspicion: t.S(4), funds: -150),
                Opt("Let her through", "She makes port with her cargo. The secret is safe.", rep: -t.S(2), suspicion: -3));
        });

        yield return new Scenario("bismarck", t => t.Between(1941, 5, 22, 1941, 5, 27), t =>
        {
            HowRead(t, Service.Kriegsmarine);
            return Dil(Service.Kriegsmarine, t, "Where is Bismarck going?",
                "The Home Fleet has lost Bismarck. {net} and a Luftwaffe decrypt say she is making for Brest.",
                "A Luftwaffe chief of staff has asked after a relative aboard, and been told the ship is bound for Brest. {read} " +
                "The Home Fleet is searching towards Norway. Turning it south on the strength of one signal, at once, will be noticed in Berlin " +
                "if she is caught. A Catalina 'patrol' over the Bay would find her almost as surely.",
                Opt("Tell the Home Fleet: she is making for Brest", "The fleet turns south and runs her down. After the sinking, Berlin orders an inquiry into how she was found.", rep: t.S(6), suspicion: t.S(12)),
                Opt("Steer a Catalina patrol over her likely course", "A Catalina 'stumbles' on her, and the fleet closes in. The Germans blame their ship's own wireless.", rep: t.S(4), suspicion: t.S(3), funds: -150),
                Opt("Leave the fleet to its own plots", "The fleet searches the wrong sea for a day. The Admiralty wants to know what the Park was for.", rep: -t.S(3)));
        }, 6);

        yield return new Scenario("pq17", t => t.Between(1942, 6, 27, 1942, 7, 10), t =>
        {
            HowRead(t, Service.Kriegsmarine);
            return Dil(Service.Kriegsmarine, t, "Is Tirpitz at sea?",
                "Convoy PQ 17 is on its way to Russia. The First Sea Lord fears Tirpitz has sailed to attack it, and will scatter the convoy if she has.",
                "{net} decrypts up to this morning show Tirpitz still in Altenfjord, with no order to sail. {read} But the next key isn't broken yet, " +
                "and silence proves nothing. Saying firmly that she is in harbour stakes the Park's word on what we can't see; saying nothing leaves " +
                "the Admiralty to its fears. A scattered convoy is a feast for U-boats and bombers.",
                Opt("Tell the Admiralty plainly she has not sailed", "The convoy keeps together and fights its way through. The Kriegsmarine wonders why the British were so calm.", rep: t.S(5), suspicion: t.S(8)),
                Opt("Advise holding the convoy together, with the cruisers close", "The convoy holds together under a strong escort. The losses are heavy but bearable.", rep: t.S(2), suspicion: t.S(2), tonnage: t.S(30000)),
                Opt("Leave it to the First Sea Lord", "The order to scatter goes out. U-boats and bombers pick off the ships one by one.", rep: -t.S(3), tonnage: t.S(90000)));
        }, 6);
    }

    // ------------------------------------------------------------------ Abwehr and police

    IEnumerable<Scenario> SecurityDeck(NetDef nd)
    {
        bool police = nd.Id == Net.Police;

        yield return new Scenario("agent", t => !police, t =>
        {
            HowRead(t, Service.Security);
            t["town"] = t.Pick(DropTowns);
            t["day"] = t.Pick("Thursday", "Friday", "Saturday", "Tuesday");
            t["who"] = t.Pick("a Dutchman", "a Dane who speaks perfect English", "a German who lived in London before the war", "a Swiss national with a wireless set");
            return Dil(Service.Security, t, "An agent by parachute",
                "{net} traffic says the Abwehr is dropping an agent near {town} on {day} night: {who}.",
                "The Abwehr's own signals give the night, the field and his cover name. {read} MI5 can be waiting. " +
                "A captured agent can be turned (MI5 runs them, feeding the Abwehr what we choose), " +
                "and a turned agent who keeps reporting reassures the Abwehr that its network is sound. " +
                "A public arrest wins headlines and makes the Abwehr ask how we knew; a free agent may lead to others.",
                Opt("Arrest him publicly", "The newspapers love it, and so does the War Cabinet. The Abwehr wonders how the police were waiting in the right field.", rep: t.S(5), suspicion: t.S(12)),
                Opt("Arrest him quietly and offer to turn him", "He takes the offer. Soon he is sending the Abwehr exactly what MI5 wants it to hear, and the Abwehr is pleased with him.", rep: t.S(2), suspicion: -2, points: t.S(4)),
                Opt("Let him land and follow him to his contacts", "He leads MI5 to two more agents before he is picked up. The Abwehr loses a network and starts asking why.", rep: t.S(1), suspicion: t.S(6), points: t.S(6)));
        }, 2);

        yield return new Scenario("questionnaire", t => !police, t =>
        {
            HowRead(t, Service.Security);
            t["agent"] = t.Pick("TATE", "SNOW", "GW", "RAINBOW", "TRICYCLE");
            t["asks"] = t.Pick("the airfields in Kent and how many fighters each holds", "the new radar towers on the east coast",
                               "the anti-aircraft guns around Portsmouth", "what is being built at the shipyards on the Clyde");
            return Dil(Service.Security, t, "The Abwehr asks a question",
                "{net} shows the Abwehr sending its agent {agent} a questionnaire about {asks}. {agent} is ours: MI5 turned him.",
                "Reading the Abwehr's traffic shows what it believes and how it rates each agent. {read} " +
                "Whatever {agent} answers, the decrypts will show how it is received. A bold lie can mislead a whole planning staff; if it is " +
                "found out, the Abwehr looks hard at all its agents. Harmless truths, approved by the committee that runs the double agents, " +
                "keep him trusted. Silence makes him look caught.",
                Opt("Feed them a bold lie", "{agent} reports strength where there is none. The decrypts show the Abwehr passing it up the line as gospel, for now.", rep: t.S(4), suspicion: t.S(8), points: t.S(2)),
                Opt("Answer with true but harmless 'chickenfeed'", "{agent} sends a careful mix of truth and trivia. The decrypts show the Abwehr rating him reliable.", rep: t.S(2), suspicion: -2),
                Opt("Have {agent} stall, and ask the Abwehr for more money", "The Abwehr grumbles but pays. MI5 banks the German money; the questions can wait.", suspicion: t.S(3), funds: 120));
        });

        yield return new Scenario("sabotage", t => !police && t.From(1940, 6, 1), t =>
        {
            HowRead(t, Service.Security);
            t["target"] = t.Pick("an aircraft factory at Hatfield", "a power station at Battersea", "a munitions works in Wales", "the docks at Southampton");
            return Dil(Service.Security, t, "A sabotage plan",
                "{net} reveals an Abwehr plan to blow up {target}, with the date and the man who is to do it.",
                "The plan comes from the Abwehr's instructions to its agent. {read} Arresting him at the gate saves the target and tips off the Abwehr. " +
                "If he is one of MI5's men, a staged explosion (tarpaulins, paint and rubble, photographed from the air) can make the Abwehr believe he did it.",
                Opt("Arrest him at the gate", "The saboteur is taken with his explosives. The Abwehr wonders who betrayed him.", rep: t.S(4), suspicion: t.S(12)),
                Opt("Stage a fake explosion for the German cameras", "Camouflage experts paint a ruin on the roof. The decrypts show the Abwehr delighted with its saboteur.", rep: t.S(2), suspicion: -3, funds: -150, points: t.S(2)),
                Opt("Double the guards and wait", "The guards are doubled. Nothing happens, and the Abwehr is left guessing.", rep: -t.S(1), suspicion: t.S(2)));
        });

        yield return new Scenario("crime", t => police && t.Between(1941, 7, 15, 1941, 12, 31), t =>
        {
            HowRead(t, Service.Security, true);
            return Dil(Service.Security, t, "What the police report",
                "{net} decrypts carry the daily reports of police battalions behind the front in Russia. They report mass shootings of Jews, in numbers.",
                "The Order Police report to Berlin every day, in a cipher the Park reads in full. The reports are precise and they do not stop. " +
                "Saying so in public tells the world, and tells the police that their cipher is read: in August 1941 Churchill spoke of 'a crime " +
                "without a name', and within weeks Berlin ordered such reports to go by courier. Passing them quietly to the Foreign Office " +
                "keeps the source.",
                Opt("Let the Prime Minister speak of it publicly", "Churchill speaks of a crime without a name. Within weeks the police are ordered to send such reports by courier.", rep: t.S(3), suspicion: t.S(18)),
                Opt("Pass the reports quietly to the Foreign Office", "The reports go into Foreign Office files. The source is kept, and the reports keep coming.", rep: t.S(1), suspicion: t.S(2)),
                Opt("Keep them within the Park", "The reports are filed. Those who read them carry it home with them.", morale: -0.05));
        }, 6);

        yield return new Scenario("reprisals", t => police, t =>
        {
            HowRead(t, Service.Security, true);
            t["place"] = t.Before(1941, 6, 22) ? t.Pick("Kraków", "Lublin", "Radom", "Warsaw") : t.Pick("Minsk", "Riga", "Białystok", "Zhytomyr");
            return Dil(Service.Security, t, "Arrests at {place}",
                "{net} traffic gives a list of people the police will arrest at {place} on the night of the {7th|12th|19th|23rd}.",
                "A police regiment's orders, read from its hand cipher. {read} A warning through the underground might save some of them; " +
                "if too many are gone when the police come, the police will look at their own signals. A broadcast hint on the BBC's foreign service " +
                "warns many without naming anyone.",
                Opt("Warn those named through the underground", "Most of those named are gone when the police come. The police commander suspects a leak and asks about his cipher.", rep: t.S(2), suspicion: t.S(14)),
                Opt("Have the BBC broadcast a general warning", "The BBC warns of arrests in the region. Some take the hint; nothing points at the cipher.", rep: t.S(1), suspicion: t.S(3)),
                Opt("File it", "The arrests go ahead. The list goes into the index.", points: t.S(2), morale: -0.03));
        });

        yield return new Scenario("lisbon", t => !police, t =>
        {
            HowRead(t, Service.Security);
            t["who"] = t.Pick("a clerk at the British embassy", "a shipping agent who handles our convoys", "a journalist with friends in the Admiralty");
            t["city"] = t.Pick("Lisbon", "Madrid", "Tangier");
            return Dil(Service.Security, t, "A leak in {city}",
                "{net} shows the Abwehr in {city} paying {who} for gossip about British shipping.",
                "The Abwehr station's own accounts and reports. {read} Arresting him plugs the leak, and the Abwehr will wonder how he was found. " +
                "Left in place and fed the right gossip, he becomes a channel for whatever we want the Abwehr to believe.",
                Opt("Have him arrested", "He is picked up and the leak stops. The Abwehr in {city} wonders how.", rep: t.S(3), suspicion: t.S(10)),
                Opt("Leave him in place and feed him", "MI6 sees that he hears the right things. The decrypts show the Abwehr paying for them.", rep: t.S(1), suspicion: -2, points: t.S(3)),
                Opt("Have him sent home on some pretext", "He is recalled to London for a dull job. The Abwehr's gossip dries up without explanation.", rep: t.S(2), suspicion: t.S(5)));
        });
    }

    // ------------------------------------------------------------------ TUNNY

    IEnumerable<Scenario> TunnyDeck()
    {
        yield return new Scenario("east", t => true, t =>
        {
            HowRead(t, Service.Heer);
            t["what"] = t.Between(1943, 3, 1, 1943, 7, 12) ? "Operation ZITADELLE: the attack on the Kursk salient, from Orel and Belgorod"
                      : t.Pick("the next offensive in the east", "a withdrawal to a new line in the Ukraine", "the reinforcements for Army Group Centre");
            return Dil(Service.Heer, t, "Orders from the high command",
                "TUNNY decrypts give the German high command's plans for {what}, in more detail than Moscow has ever had.",
                "The Testery has read a run of teleprinter messages between the high command and an army group: dates, " +
                "objectives, divisions. It is as good as intelligence gets. But Stalin distrusts anything British, " +
                "and the Soviets' own ciphers are known to be read by the Germans: whatever we tell Moscow, Berlin may overhear.",
                Opt("Tell Moscow it comes from decrypts", "Moscow believes it and acts in full. But Soviet ciphers are not as secure as ours, and within weeks German signals mention a leak.", rep: t.S(6), suspicion: t.S(16)),
                Opt("Pass it to Moscow disguised as a spy report", "The Soviets receive it from a 'source in Switzerland'. Stalin half-believes it; no one asks about teleprinters.", rep: t.S(3), suspicion: t.S(4)),
                Opt("Keep it in London", "The blow falls as a surprise to our ally. Whitehall is unimpressed.", rep: -t.S(3)));
        });

        yield return new Scenario("west", t => t.From(1943, 11, 1), t =>
        {
            HowRead(t, Service.Heer);
            t["sender"] = t.Pick("C-in-C West", "the Japanese ambassador's German hosts", "Army Group B");
            return Dil(Service.Heer, t, "Do they believe it?",
                "TUNNY gives {sender}'s appreciation of where the invasion will land: in the Pas de Calais, with a feint in Normandy.",
                "A full appreciation, read at the Testery within a day. It shows the deception plan is working: phantom armies in Kent are " +
                "believed. The planners want everything; but if the deception staff are seen to react to German thinking too neatly, the Germans " +
                "might start to doubt what they think they know.",
                Opt("Give SHAEF the full text at once", "The planners have it all, and use it. A German signals officer remarks on how well the Allies seem to read the mood.", rep: t.S(6), suspicion: t.S(12)),
                Opt("Pass a summary to the deception staff only", "The deception staff adjust their phantom armies gently. The Germans believe them more than ever.", rep: t.S(3), suspicion: t.S(3)),
                Opt("Keep it at the Park until it can be confirmed", "The appreciation waits for a second source. The planners grumble.", rep: -t.S(2), points: t.S(3)));
        }, 2);

        yield return new Scenario("south", t => t.Between(1943, 7, 10, 1944, 6, 4), t =>
        {
            HowRead(t, Service.Heer);
            t["front"] = t.Before(1943, 9, 1) ? "Sicily" : t.Pick("Salerno", "the Gustav Line", "Anzio");
            return Dil(Service.Heer, t, "Kesselring's plans",
                "TUNNY gives Field Marshal Kesselring's orders for {front}: where he will counter-attack, and with what.",
                "Teleprinter traffic between Kesselring and Berlin. {sure} Our generals in Italy can be ready at the exact spot, which " +
                "looks like foreknowledge, or be warned in general terms of a counter-attack 'expected soon'.",
                Opt("Give our generals the exact plan", "The counter-attack runs into a prepared defence. Kesselring's staff checks its own signals.", rep: t.S(5), suspicion: t.S(14)),
                Opt("Warn of a counter-attack 'expected soon'", "Our line is readier along its length. The attack is held.", rep: t.S(3), suspicion: t.S(4)),
                Opt("Keep it in London", "The counter-attack comes as a surprise and costs us ground.", rep: -t.S(3)));
        });
    }
}
