using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

public enum GoalKind { Deliver, Build, Research, KeyBreaks, BombeBreaks, Contracts, StaffCount, Scripted, ZoneOpen }

/// <summary>
/// One objective of an act. A soft goal ("before D-Day") doesn't hold the act up: it counts towards a great operation's
/// readiness instead.
/// </summary>
public sealed record Goal(GoalKind Kind, int Target, string Text,
    Net? Net = null, StationType? Station = null, ResearchId? Research = null, string? Tag = null, bool Soft = false)
{
    public int Progress(GameState g)
    {
        var s = g.Stats; var b = g.ActBaseline;
        int v = Kind switch
        {
            GoalKind.Deliver => Net is Net n
                ? Stats.Get(s.Delivered, n) - Stats.Get(b.Delivered, n)
                : s.DeliveredTotal - b.DeliveredTotal,
            GoalKind.Build => g.Stations.Count(x => x.Type == Station && !x.PendingRemoval && !x.Obsolete),
            GoalKind.Research => g.Researched.Contains(Research!.Value) ? 1 : 0,
            GoalKind.KeyBreaks => Stats.Get(s.KeyBreaks, Net!.Value) - Stats.Get(b.KeyBreaks, Net!.Value),
            GoalKind.BombeBreaks => Stats.Get(s.BombeBreaks, Net!.Value) - Stats.Get(b.BombeBreaks, Net!.Value),
            GoalKind.Contracts => s.ContractsCompleted - b.ContractsCompleted,
            GoalKind.StaffCount => g.Staff.Count,
            GoalKind.Scripted => s.ScriptedDone.Contains(Tag!) ? 1 : 0,
            GoalKind.ZoneOpen => g.Grid.Zone(Tag!)?.Unlocked == true ? 1 : 0,
            _ => 0,
        };
        return Math.Min(v, Target);
    }

    public bool Done(GameState g) => Progress(g) >= Target;
}

public sealed record ContractTemplate(Net Net, int MinQty, int MaxQty, int MinDays, int MaxDays,
    double Weight = 1, ResearchId? Requires = null);

public sealed class Act
{
    public int Chapter { get; init; }
    public string ChapterTitle { get; init; } = "";
    public string Title { get; init; } = "";
    public DateOnly Date { get; init; }
    public string From { get; init; } = "";
    public string Briefing { get; init; } = "";
    public Goal[] Goals { get; init; } = Array.Empty<Goal>();
    public ContractTemplate[] Pool { get; init; } = Array.Empty<ContractTemplate>();
    public double OfferEveryHours { get; init; } = 8;
    /// <summary>The great operation this act builds up to (see <see cref="GreatOps"/>), if any.</summary>
    public string? Op { get; init; }
    public Action<GameState>? OnStart { get; init; }
}

public static class Campaign
{
    const string Denniston = "Cdr. A. G. Denniston, Head of GC&CS";
    const string Travis = "Cdr. E. W. Travis, Director, GC&CS";

    static Staff Notable(GameState g, string name, Role role, bool female, string bio, Trait? trait, params StationType[] specialties)
    {
        var p = g.NewPerson(role, 3);
        p.Name = name;
        p.Female = female;
        p.Notable = name;
        p.Bio = bio;
        p.Salary = 75;
        p.Specialties = specialties.ToList();
        p.Traits = trait is Trait t ? new List<Trait> { t } : new List<Trait>();
        return p;
    }

    public static readonly Act[] Acts =
    {
        // ------------------------------------------------------------ Chapter 1
        new()
        {
            Chapter = 1, ChapterTitle = "Station X", Title = "War Station",
            Date = new DateOnly(1939, 9, 3), From = Denniston,
            Briefing =
                "War was declared this morning. The Government Code and Cypher School is now at its war station at Bletchley Park, " +
                "and you are running the floor.\n\n" +
                "The despatch box is already in the Mansion. The Y-station at Denmark Hill is taking down German police messages " +
                "in a hand cipher that we can read, given time, and its dispatch riders will bring them up the drive to the " +
                "registration desk.\n\n" +
                "First accept a request from the War Office: Denmark Hill sends nothing until a ministry has asked for it, and " +
                "the first rider takes a while. Meanwhile set up a registration desk, a hand-cipher desk and a translation desk.",
            Goals = new[]
            {
                new Goal(GoalKind.Build, 1, "Build a registration desk", Station: StationType.Registration),
                new Goal(GoalKind.Build, 1, "Build a hand-cipher desk", Station: StationType.HandCipher),
                new Goal(GoalKind.Build, 1, "Build a translation desk", Station: StationType.Translation),
                new Goal(GoalKind.Deliver, 5, "Deliver 5 decrypts"),
            },
            Pool = new[] { new ContractTemplate(Net.Police, 3, 5, 3, 4) },
            OfferEveryHours = 6,
            OnStart = g =>
            {
                g.ResearchAvailable.Add(ResearchId.CardIndex);
                g.Candidates.Add(Notable(g, "Dilly Knox", Role.Cryptanalyst, false,
                    "Broke codes in Room 40 in the last war. Works best in the bath.", Trait.CrosswordFiend, StationType.HandCipher));
            },
        },
        new()
        {
            Chapter = 1, ChapterTitle = "Station X", Title = "The Phoney War",
            Date = new DateOnly(1939, 11, 6), From = Denniston,
            Briefing =
                "Nothing much is happening in France, but there is plenty happening on the air.\n\n" +
                "SIS has asked us to read the Abwehr, German military intelligence. Its hand cipher is harder than the police one. " +
                "Hire more people, keep every desk busy, and don't take on more than you can deliver: a missed deadline costs us " +
                "goodwill in Whitehall, and goodwill pays the wages.",
            Goals = new[]
            {
                new Goal(GoalKind.Contracts, 4, "Complete 4 requests"),
                new Goal(GoalKind.Deliver, 20, "Deliver 20 decrypts"),
                new Goal(GoalKind.StaffCount, 7, "Have 7 staff"),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Police, 4, 7, 3, 4, 2),
                new ContractTemplate(Net.Abwehr, 3, 5, 3, 5, 1),
            },
        },

        // ------------------------------------------------------------ Chapter 2
        new()
        {
            Chapter = 2, ChapterTitle = "The Polish Gift", Title = "Zygalski Sheets",
            Date = new DateOnly(1940, 1, 6), From = Denniston,
            Briefing =
                "Before the war the Polish Cipher Bureau gave us their Enigma secrets, and they are now working with the French at " +
                "PC Bruno. With their method we can break the Luftwaffe key we call RED.\n\n" +
                "Enigma is different from hand ciphers. The key changes every midnight. Each day one message has to go to the " +
                "Zygalski sheet table to find that day's key; once it's broken, that day's RED traffic needs only a few minutes " +
                "on a Typex machine.\n\n" +
                "Open Hut 6 for the work; its decoding room will hold the Typex machines. Wrens are joining us to work them.",
            Goals = new[]
            {
                new Goal(GoalKind.ZoneOpen, 1, "Open Hut 6", Tag: "hut6"),
                new Goal(GoalKind.Build, 1, "Build a Zygalski sheet table", Station: StationType.Zygalski),
                new Goal(GoalKind.Build, 1, "Build a Typex machine", Station: StationType.Typex),
                new Goal(GoalKind.KeyBreaks, 3, "Break the RED key on 3 days", Net: Net.Red),
                new Goal(GoalKind.Deliver, 12, "Deliver 12 RED decrypts", Net: Net.Red),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Red, 5, 9, 3, 4, 3),
                new ContractTemplate(Net.Police, 4, 6, 3, 4, 1),
                new ContractTemplate(Net.Abwehr, 3, 5, 3, 5, 1),
            },
            OnStart = g =>
            {
                g.Researched.Add(ResearchId.ZygalskiSheets);
                g.ResearchAvailable.Add(ResearchId.ZygalskiSheets);
                g.ResearchAvailable.Add(ResearchId.Cillies);
                g.Grid.Zone("hut6")!.Available = true;
                g.Candidates.Add(Notable(g, "Gordon Welchman", Role.Cryptanalyst, false,
                    "Cambridge mathematician. Organised Hut 6 into a production line.", Trait.Tireless, StationType.Zygalski, StationType.Crib));
            },
        },
        new()
        {
            Chapter = 2, ChapterTitle = "The Polish Gift", Title = "Victory",
            Date = new DateOnly(1940, 3, 18), From = Denniston,
            Briefing =
                "The British Tabulating Machine Company has delivered the first Bombe, which Turing designed. We call it 'Victory'.\n\n" +
                "A Bombe doesn't need repeated indicators. It needs a crib: a guess at a piece of the plaintext, such as a " +
                "weather report that goes out at the same time every day. A cryptanalyst turns the crib into a menu at the crib " +
                "desk, and a Wren runs the menu on the Bombe.\n\n" +
                "The Treasury has found money for machines. Victory is going into Hut 1, the little hut beside the Mansion. Open it.",
            Goals = new[]
            {
                new Goal(GoalKind.Research, 1, "Research the Bombe", Research: ResearchId.Bombe),
                new Goal(GoalKind.Build, 1, "Build a crib desk", Station: StationType.Crib),
                new Goal(GoalKind.Build, 1, "Build a Bombe", Station: StationType.Bombe),
                new Goal(GoalKind.Deliver, 15, "Deliver 15 RED decrypts", Net: Net.Red),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Red, 6, 10, 3, 4, 3),
                new ContractTemplate(Net.Abwehr, 3, 5, 3, 5, 1),
            },
            OnStart = g =>
            {
                g.ResearchAvailable.Add(ResearchId.Bombe);
                if (g.Grid.Zone("hut1") is { } hut1) hut1.Available = true;
                g.Funds += 1500;
                g.Emit(EventKind.Good, "Treasury grant for Bombes: +£1500.");
                g.Candidates.Add(Notable(g, "Alan Turing", Role.Cryptanalyst, false,
                    "Designed the Bombe. Chains his tea mug to the radiator.", Trait.NightOwl, StationType.Crib, StationType.Banburismus));
            },
        },
        new()
        {
            Chapter = 2, ChapterTitle = "The Polish Gift", Title = "The Indicator Change",
            Date = new DateOnly(1940, 5, 1), From = Denniston,
            Briefing =
                "At midnight the Germans changed how they send message settings. The repeated indicators are gone, and with them " +
                "every Zygalski sheet we own. Any work in progress on the sheets is lost.\n\n" +
                "From now on, RED can only be broken with cribs and Bombes. Make sure you have enough of both. " +
                "France is about to be invaded, and the Air Ministry needs to know what the Luftwaffe is doing.",
            Goals = new[]
            {
                new Goal(GoalKind.BombeBreaks, 3, "Break RED with a Bombe on 3 days", Net: Net.Red),
                new Goal(GoalKind.Deliver, 15, "Deliver 15 RED decrypts", Net: Net.Red),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Red, 6, 10, 3, 5, 3),
                new ContractTemplate(Net.Abwehr, 3, 5, 3, 5, 1),
            },
            OnStart = g => g.ChangeIndicators(),
        },

        // ------------------------------------------------------------ Chapter 3
        new()
        {
            Chapter = 3, ChapterTitle = "Battle of Britain", Title = "Their Finest Hour",
            Date = new DateOnly(1940, 7, 10), From = Denniston,
            Briefing =
                "The Luftwaffe has begun attacking the Channel convoys. A second air force key, LIGHT BLUE, is now on the air.\n\n" +
                "Decrypts are only useful once someone has turned them into intelligence. Open Hut 3 for the translators and " +
                "intelligence staff.\n\n" +
                "Welchman has had an idea for wiring the Bombes that should make each run much shorter. Look at it when you can.\n\n" +
                "From now on we work three watches: day, evening and night. Keys change at midnight, so a night watch that " +
                "can break them early gives the day watch a flying start.\n\n" +
                "One warning: the WRNS can spare us very few women. Expect about one Wren a week. Look after the ones you have.",
            Goals = new[]
            {
                new Goal(GoalKind.ZoneOpen, 1, "Open Hut 3", Tag: "hut3"),
                new Goal(GoalKind.Deliver, 40, "Deliver 40 decrypts"),
                new Goal(GoalKind.Contracts, 4, "Complete 4 requests"),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Red, 6, 10, 3, 4, 2),
                new ContractTemplate(Net.LightBlue, 6, 10, 3, 4, 2),
                new ContractTemplate(Net.Abwehr, 3, 5, 3, 5, 0.5),
            },
            OnStart = g =>
            {
                g.ResearchAvailable.Add(ResearchId.DiagonalBoard);
                g.ResearchAvailable.Add(ResearchId.Hut3Reporting);
                g.ResearchAvailable.Add(ResearchId.PneumaticTubes);
                g.Grid.Zone("hut3")!.Available = true;
                g.Grid.Zone("hut11")!.Available = true;
                g.Emit(EventKind.Good, "Hut 11 is built for the new Bombes. Hut 1 only holds a couple.");
                g.Emit(EventKind.Good, "Hut 6 goes over to three watches round the clock. Set each person's watch from the Staff panel; " +
                                       "people on long days get tired and unhappy.");
            },
        },
        new()
        {
            Chapter = 3, ChapterTitle = "Battle of Britain", Title = "The Fifteenth of September",
            Date = new DateOnly(1940, 9, 13), From = Denniston,
            Briefing =
                "Fighter Command is close to its limit. The Air Ministry believes the Luftwaffe is preparing one great daylight " +
                "attack on London, and they need RED read fast enough to tell the squadrons where and when.\n\n" +
                "This request can't be refused and can't be missed. Every Bombe and every Wren should be ready.",
            Goals = new[]
            {
                new Goal(GoalKind.Scripted, 1, "Deliver the MOST IMMEDIATE raid warnings", Tag: "bob"),
                new Goal(GoalKind.Research, 1, "Research the diagonal board", Research: ResearchId.DiagonalBoard),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.LightBlue, 4, 7, 3, 4, 1),
                new ContractTemplate(Net.Red, 4, 7, 3, 4, 1),
                new ContractTemplate(Net.Brown, 3, 6, 3, 4, 0.8),
            },
            OfferEveryHours = 14,
            OnStart = g => g.AddScripted(Net.Red, 16, 4, 1500, 10, "bob", "Luftwaffe raid orders for London",
                "Fighter Command needs to know the raid's targets, timing and strength."),
        },

        // ------------------------------------------------------------ Chapter 4
        new()
        {
            Chapter = 4, ChapterTitle = "Dolphin", Title = "Operation Claymore",
            Date = new DateOnly(1941, 3, 4), From = Denniston,
            Briefing =
                "Commandos have raided the Lofoten Islands. On the armed trawler Krebs they found spare Enigma wheels and the " +
                "naval key tables for February.\n\n" +
                "For the next few days we can read DOLPHIN, the German navy's home-waters key, without breaking it. After that, " +
                "Hut 8 must break it daily. Naval Enigma uses eight possible wheels, so a Bombe run takes far too long unless " +
                "Banburismus has ruled out most wheel orders first.\n\n" +
                "Open Hut 8. Turing's people know what to do.",
            Goals = new[]
            {
                new Goal(GoalKind.ZoneOpen, 1, "Open Hut 8", Tag: "hut8"),
                new Goal(GoalKind.Research, 1, "Research Banburismus", Research: ResearchId.Banburismus),
                new Goal(GoalKind.Build, 1, "Build a Banburismus table", Station: StationType.Banburismus),
                new Goal(GoalKind.Deliver, 15, "Deliver 15 DOLPHIN decrypts", Net: Net.Dolphin),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Dolphin, 6, 10, 3, 5, 3),
                new ContractTemplate(Net.Red, 5, 8, 3, 4, 1),
                new ContractTemplate(Net.LightBlue, 5, 8, 3, 4, 1),
                new ContractTemplate(Net.Brown, 4, 7, 3, 4, 0.8),
            },
            OnStart = g =>
            {
                g.Grid.Zone("hut8")!.Available = true;
                g.ResearchAvailable.Add(ResearchId.Banburismus);
                g.ResearchAvailable.Add(ResearchId.Hollerith);
                g.GrantFreeKeys(Net.Dolphin, 4);
                g.Candidates.Add(Notable(g, "Joan Clarke", Role.Cryptanalyst, true,
                    "Mathematician, Hut 8. Graded as a linguist so she could be paid as one.", Trait.Meticulous, StationType.Banburismus, StationType.Crib));
            },
        },
        new()
        {
            Chapter = 4, ChapterTitle = "Dolphin", Title = "U-110",
            Date = new DateOnly(1941, 5, 9), From = Denniston,
            Briefing =
                "HMS Bulldog has captured U-110 intact, with its Enigma and its key settings, before it sank. The Germans don't know.\n\n" +
                "DOLPHIN is readable for a few days again. Use them to build up cribs, then break it with the Bombes.\n\n" +
                "The War Office also needs the Afrika Korps army key, CHAFFINCH. Rommel is in Libya.",
            Goals = new[]
            {
                new Goal(GoalKind.BombeBreaks, 4, "Break DOLPHIN with a Bombe on 4 days", Net: Net.Dolphin),
                new Goal(GoalKind.Deliver, 20, "Deliver 20 DOLPHIN decrypts", Net: Net.Dolphin),
                new Goal(GoalKind.Deliver, 8, "Deliver 8 CHAFFINCH decrypts", Net: Net.Chaffinch),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Dolphin, 6, 10, 3, 5, 2),
                new ContractTemplate(Net.Chaffinch, 5, 8, 3, 5, 2),
                new ContractTemplate(Net.Red, 5, 8, 3, 4, 1),
            },
            OnStart = g => g.GrantFreeKeys(Net.Dolphin, 3),
        },
        new()
        {
            Chapter = 4, ChapterTitle = "Dolphin", Title = "Barbarossa", Op = "barbarossa",
            Date = new DateOnly(1941, 6, 14), From = Denniston,
            Briefing =
                "For weeks the Luftwaffe keys have shown squadrons, flak and signals units leaving France and the Balkans for airfields in Poland, " +
                "and the police battalions are moving east behind them. Hitler is about to turn on Russia. The Prime Minister has already warned " +
                "Stalin once, without saying how he knew.\n\n" +
                "The blow should fall in about a week. Whitehall wants every decrypt that shows where, when and with what, and wants it without the " +
                "Germans learning that we read them. The objectives marked 'before the invasion' and German suspicion on the day decide how it goes, " +
                "and how well we saw it coming will be remembered in London and in Moscow.",
            Goals = new[]
            {
                new Goal(GoalKind.Deliver, 30, "Deliver 30 RED decrypts before the invasion", Net: Net.Red, Soft: true),
                new Goal(GoalKind.Deliver, 16, "Deliver 16 Police decrypts before the invasion", Net: Net.Police, Soft: true),
                new Goal(GoalKind.Scripted, 1, "See Barbarossa through", Tag: "op:barbarossa"),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Red, 5, 8, 2, 4, 3),
                new ContractTemplate(Net.Police, 3, 6, 2, 4, 1),
                new ContractTemplate(Net.Dolphin, 5, 8, 3, 5, 1),
                new ContractTemplate(Net.Chaffinch, 5, 8, 3, 5, 1),
            },
        },
        new()
        {
            Chapter = 4, ChapterTitle = "Dolphin", Title = "Action This Day",
            Date = new DateOnly(1941, 10, 21), From = Denniston,
            Briefing =
                "Turing, Welchman, Alexander and Milner-Barry have written directly to the Prime Minister about our shortage of " +
                "typists, Wrens and Bombes. Mr Churchill's reply was: 'Action this day. Make sure they have all they want on extreme " +
                "priority.'\n\n" +
                "The money has arrived, and the Admiralty is sending Wrens at last. Use it to expand: more Bombes, more Wrens, more of everything.",
            Goals = new[]
            {
                new Goal(GoalKind.Contracts, 6, "Complete 6 requests"),
                new Goal(GoalKind.StaffCount, 40, "Have 40 staff"),
                new Goal(GoalKind.Build, 3, "Run 3 Bombes", Station: StationType.Bombe),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Dolphin, 6, 10, 3, 5, 2),
                new ContractTemplate(Net.Chaffinch, 5, 9, 3, 5, 2),
                new ContractTemplate(Net.Red, 6, 10, 3, 4, 1),
                new ContractTemplate(Net.LightBlue, 6, 10, 3, 4, 1),
            },
            OnStart = g =>
            {
                g.Funds += 4000;
                g.AdjustRep(10, "The Prime Minister");
                g.Emit(EventKind.Good, "Action this day: +£4000 and the Prime Minister's backing.");
                g.Candidates.Add(Notable(g, "Hugh Alexander", Role.Cryptanalyst, false,
                    "British chess champion. Takes over Hut 8 and runs it superbly.", Trait.ChessPlayer, StationType.Banburismus, StationType.Crib));
            },
        },

        // ------------------------------------------------------------ Chapter 5
        new()
        {
            Chapter = 5, ChapterTitle = "Shark", Title = "The Blackout",
            Date = new DateOnly(1942, 2, 1), From = Travis,
            Briefing =
                "Today the U-boats in the Atlantic switched to a new key we call SHARK, on an Enigma with four wheels. " +
                "Our Bombes can't break it. We are blind in the Atlantic.\n\n" +
                "The tonnage counter in the corner is ships sunk while we can't read SHARK. There's nothing we can do about it yet " +
                "except keep every other key flowing, so that when the chance comes, we're ready for it.",
            Goals = new[]
            {
                new Goal(GoalKind.Deliver, 35, "Deliver 35 decrypts"),
                new Goal(GoalKind.Contracts, 5, "Complete 5 requests"),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Dolphin, 6, 10, 3, 5, 2),
                new ContractTemplate(Net.Chaffinch, 6, 10, 3, 5, 2),
                new ContractTemplate(Net.Red, 6, 10, 3, 4, 1),
                new ContractTemplate(Net.LightBlue, 6, 10, 3, 4, 1),
            },
            OnStart = g => g.SharkBlackout = true,
        },
        new()
        {
            Chapter = 5, ChapterTitle = "Shark", Title = "U-559",
            Date = new DateOnly(1942, 10, 30), From = Travis,
            Briefing =
                "In the eastern Mediterranean, HMS Petard forced U-559 to the surface. Lieutenant Tony Fasson and Able Seaman " +
                "Colin Grazier swam to the sinking boat and passed out its papers. Both went down with it. Canteen assistant " +
                "Tommy Brown, who was sixteen, survived.\n\n" +
                "Among the papers is the current Short Weather Cipher book. U-boat weather reports are sent on Shark with " +
                "the fourth wheel in a fixed position, which gives us cribs at last.\n\n" +
                "Research the weather cipher and the four-rotor Bombe, build one, and break Shark.",
            Goals = new[]
            {
                new Goal(GoalKind.Research, 1, "Research the short weather cipher", Research: ResearchId.ShortWeatherCipher),
                new Goal(GoalKind.Research, 1, "Research the four-rotor Bombe", Research: ResearchId.FourRotorBombe),
                new Goal(GoalKind.Build, 1, "Build a four-rotor Bombe", Station: StationType.Bombe4),
                new Goal(GoalKind.BombeBreaks, 3, "Break SHARK on 3 days", Net: Net.Shark),
                new Goal(GoalKind.Deliver, 15, "Deliver 15 SHARK decrypts", Net: Net.Shark),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Shark, 5, 8, 4, 6, 3, ResearchId.ShortWeatherCipher),
                new ContractTemplate(Net.Dolphin, 5, 9, 3, 5, 1),
                new ContractTemplate(Net.Chaffinch, 5, 9, 3, 5, 1),
            },
            OnStart = g =>
            {
                g.ResearchAvailable.Add(ResearchId.ShortWeatherCipher);
                g.ResearchAvailable.Add(ResearchId.FourRotorBombe);
                g.Funds += 2500;
                g.Emit(EventKind.Good, "The Admiralty funds four-rotor Bombes: +£2500.");
            },
        },

        // ------------------------------------------------------------ Chapter 6
        new()
        {
            Chapter = 6, ChapterTitle = "Tunny", Title = "The Testery",
            Date = new DateOnly(1943, 1, 4), From = Travis,
            Briefing =
                "On 13 December 1942 Hut 8 broke SHARK. Within hours the Admiralty's Submarine Tracking Room had the U-boats' positions again, " +
                "and the convoys were routed around the wolf packs. The war is far from over.\n\n" +
                "There is another German cipher on the air, and it isn't Enigma. The high command talks to its army groups by " +
                "radio teleprinter, enciphered on a Lorenz machine with twelve wheels. We call the traffic Fish, and this link TUNNY.\n\n" +
                "Bill Tutte worked out how the machine is built without ever seeing one. Major Tester's section, the Testery, " +
                "breaks the wheel settings by hand with Turing's method. Once a day's settings are known, a Tunny machine prints " +
                "the plain German.\n\n" +
                "Open Block F for them.",
            Goals = new[]
            {
                new Goal(GoalKind.ZoneOpen, 1, "Open Block F", Tag: "blockf"),
                new Goal(GoalKind.Research, 1, "Research Turingery", Research: ResearchId.Turingery),
                new Goal(GoalKind.Build, 1, "Build a Testery desk", Station: StationType.Testery),
                new Goal(GoalKind.Build, 1, "Build a Tunny machine", Station: StationType.TunnyMachine),
                new Goal(GoalKind.Deliver, 12, "Deliver 12 TUNNY decrypts", Net: Net.Tunny),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Tunny, 4, 6, 4, 6, 3, ResearchId.Turingery),
                new ContractTemplate(Net.Shark, 5, 8, 3, 5, 1),
                new ContractTemplate(Net.Dolphin, 5, 8, 3, 5, 1),
                new ContractTemplate(Net.Chaffinch, 5, 8, 3, 5, 1),
            },
            OnStart = g =>
            {
                g.Grid.Zone("blockf")!.Available = true;
                g.ResearchAvailable.Add(ResearchId.Turingery);
                g.Funds += 2000;
                g.Emit(EventKind.Good, "Funds for the Fish sections: +£2000.");
                g.Candidates.Add(Notable(g, "Bill Tutte", Role.Cryptanalyst, false,
                    "Deduced the whole Lorenz machine from a single long message sent twice.", Trait.Meticulous, StationType.Testery));
            },
        },
        new()
        {
            Chapter = 6, ChapterTitle = "Tunny", Title = "Colossus",
            Date = new DateOnly(1944, 2, 5), From = Travis,
            Briefing =
                "Tommy Flowers and the Post Office engineers at Dollis Hill have built a machine with fifteen hundred valves. " +
                "It reads punched tape at five thousand characters a second and finds the chi-wheel settings far faster than any " +
                "Testery desk. We call it Colossus. Wrens will run it.\n\n" +
                "The invasion of France is coming. General Eisenhower needs to know whether Hitler believes the landings will " +
                "come at Calais. The answer will be in TUNNY.",
            Goals = new[]
            {
                new Goal(GoalKind.Research, 1, "Research Colossus", Research: ResearchId.Colossus),
                new Goal(GoalKind.Build, 1, "Build a Colossus", Station: StationType.Colossus),
                new Goal(GoalKind.BombeBreaks, 3, "Break TUNNY with Colossus on 3 days", Net: Net.Tunny),
                new Goal(GoalKind.Scripted, 1, "Deliver the high command's view of the invasion", Tag: "dday"),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Tunny, 5, 8, 4, 6, 3),
                new ContractTemplate(Net.Shark, 5, 8, 3, 5, 1),
                new ContractTemplate(Net.Chaffinch, 5, 8, 3, 5, 1),
            },
            OnStart = g =>
            {
                g.ResearchAvailable.Add(ResearchId.Colossus);
                g.Funds += 4000;
                g.Emit(EventKind.Good, "Colossus is approved: +£4000.");
                g.Candidates.Add(Notable(g, "Max Newman", Role.Cryptanalyst, false,
                    "Mathematician. His section, the Newmanry, runs the machines.", Trait.Tireless, StationType.Testery));
                g.AddScripted(Net.Tunny, 10, 6, 2500, 12, "dday", "Hitler's view of the invasion",
                    "SHAEF needs to know whether the deception about Calais is believed in Berlin.");
            },
        },
        // ------------------------------------------------------------ Chapter 7
        new()
        {
            Chapter = 7, ChapterTitle = "The Second Front", Title = "D-Day", Op = "overlord",
            Date = new DateOnly(1944, 5, 29), From = Travis,
            Briefing =
                "The invasion of France is ready. A hundred and fifty thousand men will land in Normandy, and everything depends on Hitler " +
                "believing the real blow will come at Calais. Operation FORTITUDE has invented a whole army group in Kent, and our double agents " +
                "have fed the Abwehr its order of battle for months.\n\n" +
                "Until the landings, General Eisenhower needs to know what the Germans believe and where their panzers are: TUNNY from the high " +
                "command, the Abwehr's reading of its agents, the U-boats gathering against the invasion fleet. None of it may give away that we " +
                "read them.\n\n" +
                "The objectives marked 'before D-Day' and German suspicion on the day decide how it goes. If the landings fail, so do we.",
            Goals = new[]
            {
                new Goal(GoalKind.Deliver, 45, "Deliver 45 TUNNY decrypts before D-Day", Net: Net.Tunny, Soft: true),
                new Goal(GoalKind.Deliver, 20, "Deliver 20 Abwehr decrypts before D-Day", Net: Net.Abwehr, Soft: true),
                new Goal(GoalKind.Deliver, 36, "Deliver 36 SHARK decrypts before D-Day", Net: Net.Shark, Soft: true),
                new Goal(GoalKind.Scripted, 1, "See D-Day through", Tag: "op:overlord"),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Tunny, 4, 6, 2, 4, 3),
                new ContractTemplate(Net.Abwehr, 3, 6, 2, 4, 2),
                new ContractTemplate(Net.Shark, 4, 7, 2, 4, 2),
                new ContractTemplate(Net.Red, 5, 8, 3, 4, 1),
            },
            OnStart = g =>
            {
                g.Funds += 3000;
                g.Emit(EventKind.Good, "Every resource for the invasion: +£3000.");
            },
        },
        new()
        {
            Chapter = 7, ChapterTitle = "The Second Front", Title = "Market Garden", Op = "marketgarden",
            Date = new DateOnly(1944, 9, 9), From = Travis,
            Briefing =
                "Paris is free and the Germans are streaming back across Belgium. Field Marshal Montgomery has a plan to end the war by Christmas: " +
                "three airborne divisions will seize the bridges from Eindhoven to Arnhem, and an armoured corps will drive up the road over them " +
                "into Germany.\n\n" +
                "The Germans in Holland look beaten. But Luftwaffe and TUNNY traffic speak of SS panzer divisions sent to rest and refit somewhere " +
                "in Holland. Find out where, and make sure the planners know before the drop. It goes ahead on 17 September whatever we find.",
            Goals = new[]
            {
                new Goal(GoalKind.Deliver, 50, "Deliver 50 RED decrypts before the drop", Net: Net.Red, Soft: true),
                new Goal(GoalKind.Deliver, 40, "Deliver 40 TUNNY decrypts before the drop", Net: Net.Tunny, Soft: true),
                new Goal(GoalKind.Scripted, 1, "See Market Garden through", Tag: "op:marketgarden"),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Red, 5, 8, 2, 4, 3),
                new ContractTemplate(Net.Tunny, 4, 6, 2, 4, 2),
                new ContractTemplate(Net.Shark, 4, 7, 3, 5, 1),
            },
        },
        new()
        {
            Chapter = 7, ChapterTitle = "The Second Front", Title = "The Ardennes", Op = "ardennes",
            Date = new DateOnly(1944, 12, 8), From = Travis,
            Briefing =
                "The Western Front has gone quiet. The Germans opposite the Ardennes send less and less by wireless, and the Allied commanders " +
                "take the quiet for exhaustion. But the Luftwaffe keys show fighter units moving west in numbers, fuel being stockpiled, and a " +
                "Sixth Panzer Army that has dropped out of sight.\n\n" +
                "Something is coming. The more we read before it does, the readier the front will be.",
            Goals = new[]
            {
                new Goal(GoalKind.Deliver, 55, "Deliver 55 RED decrypts before the offensive", Net: Net.Red, Soft: true),
                new Goal(GoalKind.Deliver, 40, "Deliver 40 TUNNY decrypts before the offensive", Net: Net.Tunny, Soft: true),
                new Goal(GoalKind.Scripted, 1, "See the Ardennes through", Tag: "op:ardennes"),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Red, 5, 8, 2, 4, 3),
                new ContractTemplate(Net.Tunny, 4, 6, 2, 4, 2),
                new ContractTemplate(Net.Shark, 4, 7, 3, 5, 1),
            },
        },
        new()
        {
            Chapter = 7, ChapterTitle = "The Second Front", Title = "Victory in Europe",
            Date = new DateOnly(1945, 4, 12), From = Travis,
            Briefing =
                "The Allies are across the Rhine and the Russians are on the Oder. Germany is collapsing, but its armies and U-boats fight on, and " +
                "every message still counts: where the last U-boats are bound, which units mean to surrender, whether there will be a last stand " +
                "in the Alps.\n\n" +
                "See it through to the end.",
            Goals = new[]
            {
                new Goal(GoalKind.Deliver, 30, "Deliver 30 decrypts"),
                new Goal(GoalKind.Contracts, 4, "Complete 4 requests"),
            },
            Pool = new[]
            {
                new ContractTemplate(Net.Tunny, 4, 7, 3, 5, 2),
                new ContractTemplate(Net.Shark, 4, 7, 3, 5, 2),
                new ContractTemplate(Net.Red, 5, 8, 3, 5, 1),
                new ContractTemplate(Net.Abwehr, 3, 6, 3, 5, 1),
            },
        },
    };

    public static int IndexOf(string title) => Array.FindIndex(Acts, a => a.Title == title);
    /// <summary>The act whose completion breaks SHARK (the end of the campaign before it ran to 1945).</summary>
    public static readonly int EnigmaFinale = IndexOf("U-559");
    /// <summary>From The Blackout on, SHARK is on the air.</summary>
    public static readonly int SharkFrom = IndexOf("The Blackout");
    public static readonly int ActionThisDay = Array.FindIndex(Acts, a => a.Title == "Action This Day");
    /// <summary>From U-110 on, some naval traffic is Offizier: enciphered twice.</summary>
    public static readonly int OffizierFrom = Array.FindIndex(Acts, a => a.Title == "U-110");
    /// <summary>From Operation Claymore, Hut 8 can read the dockyard cipher (WERFT) for kisses.</summary>
    public static readonly int KissesFrom = IndexOf("Operation Claymore");

    public const string TunnyEpilogue =
        "On 1 June 1944 the improved Colossus Mark 2 started work. On 5 June, as the invasion fleet waited for the weather, " +
        "a courier brought General Eisenhower a TUNNY decrypt: Hitler had told the Japanese ambassador that he expected " +
        "the main landing at Calais, and that Normandy would be a diversion. The deception had worked.\n\n" +
        "\"We go,\" said Eisenhower.\n\n" +
        "By the end of the war there were ten Colossi at Bletchley Park. Afterwards eight were broken up, the drawings were " +
        "burned, and the world's first large electronic computers were kept secret until the 1970s.";

    public const string FinalEpilogue =
        "On 7 May 1945, at Reims, Germany surrendered. At Bletchley Park the teleprinters fell quiet, and people who had worked " +
        "side by side for years went home without a word to their families about what they had done.\n\n" +
        "By the end there were nearly ten thousand of them, three-quarters of them women, and ten Colossi in Block H. Afterwards most of " +
        "the machines were broken up and the drawings burned, and the world's first large electronic computers stayed secret until the " +
        "1970s. Historians still argue over how many months ULTRA took off the war. None argue that it took off none.";

    public const string Epilogue =
        "On 13 December 1942, Hut 8 broke SHARK. Within hours the Admiralty's Submarine Tracking Room had U-boat " +
        "positions again, and convoys were routed around the wolf packs.\n\n" +
        "The Battle of the Atlantic would take months more, and the Germans would fight back with new procedures. But by " +
        "May 1943 the U-boats had been driven from the convoy routes, and Bletchley Park was reading Enigma at an industrial " +
        "scale, with thousands of people, most of them women, working shifts around the clock.\n\n" +
        "None of them could say a word about it for thirty years.";

    public static IEnumerable<Net> NetsOf(Act a) => a.Pool.Select(p => p.Net).Distinct();
}

public sealed partial class GameState
{
    public static GameState NewGame(int seed, Difficulty difficulty = Difficulty.Normal, GameMode mode = GameMode.Campaign, int startAct = 0)
    {
        var g = new GameState
        {
            Seed = seed, Rng = new Rng((ulong)seed * 2654435761UL + 1), Now = 9 * 60,
            Difficulty = difficulty, Mode = mode, Funds = Defs.Difficulties[difficulty].StartFunds, Billets = Defs.StartBillets,
        };
        Array.Fill(g.Grid.Occupant, -1);
        g.Place(StationType.Dispatch, 16, 3, 0, free: true);
        foreach (var (role, x, y) in new[] { (Role.Clerk, 7, 6), (Role.Clerk, 8, 7), (Role.Cryptanalyst, 10, 6), (Role.Linguist, 11, 7) })
        {
            var p = g.NewPerson(role, 1);
            p.Traits.Remove(Trait.CarelessTalker);
            p.X = x + 0.5; p.Y = y + 0.5;
            p.HomeX = p.X; p.HomeY = p.Y;
            g.Staff.Add(p);
        }
        g.RefreshCandidates();
        g.StartAct(0);
        if (startAct > 0) g.FastForwardTo(startAct);
        return g;
    }

    /// <summary>Free play from a later chapter: run the earlier acts' setup and hand over what they would have earned.</summary>
    void FastForwardTo(int act)
    {
        for (int k = 1; k <= act && k < Campaign.Acts.Length; k++)
        {
            foreach (var goal in Campaign.Acts[k - 1].Goals)
            {
                if (goal.Research is ResearchId r) Researched.Add(r);
                if (goal.Kind == GoalKind.ZoneOpen && Grid.Zone(goal.Tag!) is { } z) { z.Available = true; z.Unlocked = true; }
            }
            StartAct(k);
        }
        Funds += 2500 * act / 2;
        // A starting staff to match the era.
        var roles = new List<Role> { Role.Clerk, Role.Clerk, Role.Cryptanalyst, Role.Linguist };
        if (act >= 2) roles.AddRange(new[] { Role.Wren, Role.Wren, Role.Linguist });
        if (act >= 5) roles.AddRange(new[] { Role.Wren, Role.Cryptanalyst, Role.Clerk, Role.Linguist });
        foreach (var r in roles)
        {
            var p = NewPerson(r, 2);
            p.Traits.Remove(Trait.CarelessTalker);
            p.X = p.HomeX = Grid.Gate.x + 0.5;
            p.Y = p.HomeY = Grid.Gate.y + 0.5;
            Staff.Add(p);
        }
        Events.Clear();
        Emit(EventKind.ActStarted, $"Chapter {Act.Chapter}: {Act.ChapterTitle}. {Act.Title}");
    }

    public const double FreePlayActDays = 10;

    void StartAct(int index)
    {
        // Keep the calendar running: each act starts on its historical date.
        ActIndex = index;
        ActStartDay = Day;
        ActDate = Act.Date;
        ActStarts[Day] = Act.Date;
        ActBaseline = Stats.Clone();
        ActCompletePending = false;
        NextOfferAt = Now;
        foreach (var n in Campaign.NetsOf(Act)) ActiveNets.Add(n);
        if (index >= Campaign.SharkFrom) ActiveNets.Add(Net.Shark);
        NewChapterIntake(index);
        StartGreatOp();
        if (index > 0)
        {
            ReceiverSets += Defs.SetsPerAct;
            Emit(EventKind.Info, $"The Y Board allots the Park's networks {(Defs.SetsPerAct == 1 ? "another receiver set" : $"{Defs.SetsPerAct} more receiver sets")} (Y-stations, Y).");
        }
        Act.OnStart?.Invoke(this);
        Emit(EventKind.ActStarted, $"Chapter {Act.Chapter}: {Act.ChapterTitle}. {Act.Title}");
    }

    void CheckGoals()
    {
        if (ActCompletePending || Status != GameStatus.Playing) return;
        if (Mode == GameMode.FreePlay)
        {
            // No objectives: history simply moves on every few days.
            if (Day - ActStartDay >= FreePlayActDays && ActIndex + 1 < Campaign.Acts.Length) StartAct(ActIndex + 1);
            return;
        }
        if (Act.Goals.Where(g => !g.Soft).All(g => g.Done(this)))
        {
            ActCompletePending = true;
            Emit(EventKind.ActComplete, $"{Act.Title}: all objectives complete.");
        }
    }

    /// <summary>Called by the UI after the player has read the act summary.</summary>
    /// <summary>The campaign score: what was delivered, how fast, and at what cost.</summary>
    public int Score()
    {
        double s = Stats.DeliveredTotal * 10
                   + Stats.ContractsCompleted * 50
                   - Stats.ContractsFailed * 80
                   + Reputation * 20
                   + (Status == GameStatus.Won ? Math.Max(0, 200 - Day) * 25 : 0)
                   - TonnageLost / 1000;
        double mult = Difficulty switch { Difficulty.Easy => 0.75, Difficulty.Hard => 1.4, _ => 1.0 };
        return Status == GameStatus.Lost ? (int)Math.Max(0, s * mult * 0.5) : (int)Math.Max(0, s * mult);
    }

    public static readonly (int min, string honour, string citation)[] Honours =
    {
        (0, "Thanked by the Director", "Commander Denniston thanks you for your efforts."),
        (5000, "Mentioned in despatches", "Your name appears, briefly, in a report nobody outside the Park will read for thirty years."),
        (8000, "MBE", "Member of the Order of the British Empire, for services that cannot be described."),
        (11000, "OBE", "Officer of the Order of the British Empire, like Turing and Welchman."),
        (14000, "CBE", "Commander of the Order of the British Empire. The citation says only 'Foreign Office'."),
        (17000, "The geese that laid the golden eggs", "Churchill's own words for the Park, and for you: 'and never cackled'."),
    };

    public (string honour, string citation) Honour()
    {
        int sc = Score();
        var h = Honours.Last(x => sc >= x.min);
        return (h.honour, h.citation);
    }

    public void ContinueAfterAct()
    {
        if (!ActCompletePending) return;
        if (ActIndex + 1 >= Campaign.Acts.Length)
        {
            ActCompletePending = false;
            Status = GameStatus.Won;
            CanContinue = false;
            EndReason = Campaign.FinalEpilogue;
            Emit(EventKind.Won, EndReason);
            return;
        }
        StartAct(ActIndex + 1);
    }

    /// <summary>For saves from when the campaign ended with SHARK: carry on into 1943 and the Lorenz chapter.</summary>
    public bool ContinueIntoTunny()
    {
        if (Status != GameStatus.Won || !CanContinue) return false;
        Status = GameStatus.Playing;
        CanContinue = false;
        EndReason = null;
        StartAct(ActIndex + 1);
        return true;
    }

    void RecordDay()
    {
        History.Add(new DayRecord
        {
            Day = Day - 1, Delivered = DeliveredToday, KeysBroken = KeysToday, Backlog = Messages.Count,
            Funds = Funds, Reputation = Reputation, Staff = Staff.Count, Suspicion = MaxSuspicion, Morale = AverageMorale,
        });
        if (History.Count > 120) History.RemoveAt(0);
        DeliveredToday = 0;
        KeysToday = 0;
    }

    /// <summary>Debug/test helper: run every act's start effects up to the given act.</summary>
    public void DebugSkipToAct(int index)
    {
        for (int k = ActIndex + 1; k <= index && k < Campaign.Acts.Length; k++) StartAct(k);
    }

    /// <summary>
    /// Nets worth showing in the key board: machine nets in play now, meaning the act's requests draw on them, a request
    /// for them is offered or running, or their messages are still on the floor. A net whose traffic has dried up drops off.
    /// </summary>
    public IEnumerable<Net> KeyBoardNets() =>
        ActiveNets.Where(n => Defs.NetOf(n).Family != Family.Hand && InPlay(n)).OrderBy(n => NetOrder.IndexOf(n));

    static readonly List<Net> NetOrder = Defs.Nets.Keys.ToList();

    bool InPlay(Net n)
    {
        if (n == Net.Shark) return true;   // from The Blackout on, SHARK's silence is the story, requests or not
        // Loops, not LINQ: the Y service asks this for every network every few minutes.
        foreach (var p in Act.Pool) if (p.Net == n) return true;
        foreach (var g in Act.Goals) if (g.Net == n) return true;
        foreach (var c in Contracts) if (c.Net == n && (c.State == ContractState.Offered || c.State == ContractState.Active)) return true;
        foreach (var m in Messages.Values) if (m.Net == n) return true;
        return false;
    }
}
