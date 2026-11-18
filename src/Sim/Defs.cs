using System.Linq;
using System;
using System.Collections.Generic;

namespace StationX.Sim;

public enum Role { Clerk, Cryptanalyst, Wren, Linguist, Messenger }

public enum StationType
{
    Inbox, Registration, HandCipher, Crib, Zygalski, Banburismus,
    Bombe, Bombe4, Typex, Translation, Dispatch,
    Canteen, CommonRoom, Rounders, Checking, Tube, OutstationLink,
    Testery, Colossus, TunnyMachine,
    Stove,
}

public enum Family { Hand, Enigma, Lorenz }

public enum Net { Police, Abwehr, Red, LightBlue, Chaffinch, Dolphin, Shark, Tunny }

public enum Issuer { WarOffice, AirMinistry, Admiralty, SIS }

/// <summary>The German organisation whose signals security reacts to suspiciously good Allied luck.</summary>
public enum Service { Luftwaffe, Heer, Kriegsmarine, Security }

/// <summary>How freely commanders may act on ULTRA intelligence.</summary>
public enum Policy { Cautious, Standard, Bold }

public enum Difficulty { Easy, Normal, Hard }

public enum GameMode { Campaign, FreePlay }

/// <summary>Working hours. Office hours until the watch system starts, then three watches round the clock.</summary>
public enum Shift { Office, Day, Evening, Night }

public enum Trait { CrosswordFiend, NativeGerman, NightOwl, ChessPlayer, Tireless, Meticulous, CarelessTalker }

public enum ResearchId
{
    CardIndex, ZygalskiSheets, Cillies, Bombe, DiagonalBoard, Hut3Reporting,
    Banburismus, Hollerith, ShortWeatherCipher, FourRotorBombe,
    PneumaticTubes, Turingery, Colossus,
}

public sealed record StationDef(
    StationType Type, string Name, string Blurb, int Cost, int W, int H,
    Role? Operator, double Minutes, int InputCap, ResearchId? Requires, int MinAct,
    int Seats = 0, int Upkeep = 0, bool Outdoor = false);

public sealed record NetDef(
    Net Id, string Name, string Owner, Family Family, bool Naval, bool FourRotor,
    double Difficulty, bool ZygalskiOk, Issuer Issuer, int Points, int PayPerMsg, Service Service);

public sealed record TraitDef(Trait Trait, string Name, string Blurb, bool Good);

public sealed record ShiftDef(Shift Id, string Name, int StartHour, int EndHour)
{
    public bool OnDuty(double minuteOfDay)
    {
        double h = minuteOfDay / 60.0;
        return StartHour < EndHour ? h >= StartHour && h < EndHour : h >= StartHour || h < EndHour;
    }
}

public sealed record DifficultyDef(Difficulty Id, string Name, string Blurb, int StartFunds, double GrantMult,
    double ExtraDays, double SuspicionMult, double RepLossMult, double WearMult = 1);

public sealed record PolicyDef(Policy Id, string Name, string Blurb, double Suspicion, double Reward, double Rep);

public sealed record ResearchDef(ResearchId Id, string Name, string Blurb, int Cost);

public sealed record RoleDef(Role Role, string Name, string Plural, int Salary, string Blurb);

/// <summary>All tunable numbers live here.</summary>
public static class Defs
{
    // ---- Time ----
    public const double MinutesPerDay = 1440;
    /// <summary>Game minutes that pass per real second at 1x speed (a day is 3 real minutes).</summary>
    public const double GameMinutesPerRealSecond = 6;
    public const double Tick = 0.5;
    public const double WalkTilesPerMinute = 0.28;   // ~1.7 tiles per real second at 1×
    /// <summary>Messengers are young and in a hurry.</summary>
    public const double MessengerSpeed = 1.3;
    public const int CarryBatch = 4;
    public const double OperatorLingerMinutes = 30;

    // ---- People ----
    public const double EnergyDrainWorking = 1.0 / 700;
    public const double EnergyDrainOther = 1.0 / 1300;
    public const double EnergyRecoverIdle = 1.0 / 500;
    public const double EnergyRecoverRest = 1.0 / 45;
    public const double BreakAt = 0.35;
    public const double BreakUntil = 0.92;
    public const double Exhausted = 0.08;
    public const double MoraleBaseline = 0.6;
    /// <summary>Act index from which the three-watch system is available.</summary>
    public const int WatchesFromAct = 5;

    // ---- Economy ----
    public const double StartReputation = 50;
    public const int GrantBase = 200;
    public const double GrantPerRep = 7;
    /// <summary>Whitehall gets used to miracles: reputation slips this much a day, plus a bit more each act.</summary>
    public const double ExpectationBase = 0.5;
    public const double ExpectationPerAct = 0.2;
    public const double SellRefund = 0.5;
    public const int MaxOffers = 3;
    public const double OfferLifetimeDays = 1.5;
    public const int MoveCost = 25;
    public const int OutstationBombeCost = 1500;
    public const int OutstationUpkeep = 40;
    public const int GardeningCost = 300;
    public const double GarbledChance = 0.06;
    /// <summary>Daily chance that a man on the staff is called up (1940-42).</summary>
    public const double CallUpChance = 0.1;
    public const int CallUpRulingCost = 150;

    // ---- Machine wear (RAF fitters kept the Bombes running) ----
    /// <summary>Working minutes for a machine to go from freshly serviced to fully worn.</summary>
    public static double WearLife(StationType t) => t switch
    {
        StationType.Bombe => 2400,
        StationType.Bombe4 => 1800,       // the four-rotor Bombes were notoriously temperamental
        StationType.Colossus => 3000,
        StationType.TunnyMachine => 1500, // tapes stretched and snapped
        _ => 0,
    };
    /// <summary>Chance per working minute of a breakdown, times wear squared.</summary>
    public const double BreakdownHazard = 0.006;
    public const double RepairMinutes = 300;
    public const int RepairCost = 80;
    public const double ServiceMinutes = 60;
    public const int ServiceCost = 20;

    // ---- Suspicion ----
    public const double SuspicionPerDecrypt = 0.8;
    public const double SuspicionDecayPerDay = 5;
    public const double SuspicionWarn = 70;
    public const double SuspicionLimit = 100;
    public const int LockdownDays = 2;

    public static readonly string[] OutstationSites =
        { "Wavendon", "Adstock", "Gayhurst", "Drayton Parslow", "Eastcote", "Stanmore" };

    public static readonly Dictionary<Shift, ShiftDef> Shifts = new()
    {
        [Shift.Office] = new(Shift.Office, "Long days", 7, 22),
        [Shift.Day] = new(Shift.Day, "Day watch", 9, 16),
        [Shift.Evening] = new(Shift.Evening, "Evening watch", 16, 24),
        [Shift.Night] = new(Shift.Night, "Night watch", 0, 9),
    };

    public static readonly Dictionary<Trait, TraitDef> Traits = new()
    {
        [Trait.CrosswordFiend] = new(Trait.CrosswordFiend, "Crossword fiend", "Recruited through the Telegraph crossword. Better, faster cribs.", true),
        [Trait.NativeGerman] = new(Trait.NativeGerman, "Native German speaker", "Translates 40% faster.", true),
        [Trait.NightOwl] = new(Trait.NightOwl, "Night owl", "Tires a third more slowly between 20:00 and 06:00.", true),
        [Trait.ChessPlayer] = new(Trait.ChessPlayer, "Chess player", "20% faster at crib desks and Banburismus.", true),
        [Trait.Tireless] = new(Trait.Tireless, "Tireless", "Tires 30% more slowly.", true),
        [Trait.Meticulous] = new(Trait.Meticulous, "Meticulous", "Fewer mistakes on hand ciphers, sheets and checking.", true),
        [Trait.CarelessTalker] = new(Trait.CarelessTalker, "Careless talker", "Says too much in the pub. Raises German suspicion and draws MI5's attention.", false),
    };

    public static readonly Dictionary<Difficulty, DifficultyDef> Difficulties = new()
    {
        [Difficulty.Easy] = new(Difficulty.Easy, "Easy", "More money, longer deadlines, slower German suspicion.", 2200, 1.2, 1, 0.6, 0.7, 0.5),
        [Difficulty.Normal] = new(Difficulty.Normal, "Normal", "As intended.", 1500, 1.0, 0, 1.0, 1.0),
        [Difficulty.Hard] = new(Difficulty.Hard, "Hard", "Less money, tighter deadlines, and the Germans notice everything.", 1100, 0.85, -0.25, 1.4, 1.2, 1.2),
    };

    public static readonly Dictionary<Policy, PolicyDef> Policies = new()
    {
        [Policy.Cautious] = new(Policy.Cautious, "Cautious", "Every use of ULTRA gets a cover story. Smaller rewards, little suspicion.", 0.35, 0.8, 0.8),
        [Policy.Standard] = new(Policy.Standard, "Standard", "Cover stories where they're cheap.", 1.0, 1.0, 1.0),
        [Policy.Bold] = new(Policy.Bold, "Bold", "Commanders act on everything at once. Bigger rewards, and the Germans will notice.", 1.9, 1.25, 1.3),
    };

    public static readonly Dictionary<Service, string> ServiceNames = new()
    {
        [Service.Luftwaffe] = "Luftwaffe",
        [Service.Heer] = "German Army",
        [Service.Kriegsmarine] = "Kriegsmarine",
        [Service.Security] = "Abwehr and SD",
    };

    public static readonly Dictionary<Role, RoleDef> Roles = new()
    {
        [Role.Clerk] = new(Role.Clerk, "Clerk", "Clerks", 22,
            "Registers traffic and seals the despatch box. Carries paper when no messenger is free."),
        [Role.Cryptanalyst] = new(Role.Cryptanalyst, "Cryptanalyst", "Cryptanalysts", 48,
            "Breaks hand ciphers, writes cribs and menus, and works the sheets."),
        [Role.Wren] = new(Role.Wren, "Wren", "Wrens", 25,
            "Women's Royal Naval Service. Operates the Bombes, Typex, checking machines and Colossus."),
        [Role.Linguist] = new(Role.Linguist, "Linguist", "Linguists", 34,
            "Translates decrypts and writes them up as intelligence reports."),
        [Role.Messenger] = new(Role.Messenger, "Messenger", "Messengers", 14,
            "Mostly local teenagers. Cheap, quick on their feet, and can't work a desk: they carry paper between desks and buildings so the clerks can stay at theirs."),
    };

    public static readonly Dictionary<StationType, StationDef> Stations = new()
    {
        [StationType.Inbox] = new(StationType.Inbox, "Teleprinter",
            "Intercepts arrive from the Y-stations at Chatham, Cheadle and Beaumanor.",
            250, 1, 1, null, 0, 0, null, 0),
        [StationType.Registration] = new(StationType.Registration, "Registration desk",
            "Logs callsigns and frequencies, works out which key net each message belongs to, and spots garbled intercepts.",
            150, 2, 1, Role.Clerk, 25, 6, null, 0),
        [StationType.HandCipher] = new(StationType.HandCipher, "Hand-cipher desk",
            "Pencil, squared paper and patience. Breaks police and Abwehr hand ciphers.",
            200, 2, 1, Role.Cryptanalyst, 180, 4, null, 0),
        [StationType.Crib] = new(StationType.Crib, "Crib desk",
            "Guesses a stretch of plaintext, such as WETTERVORHERSAGE, and turns it into a menu for the Bombe.",
            200, 2, 1, Role.Cryptanalyst, 120, 2, null, 3),
        [StationType.Zygalski] = new(StationType.Zygalski, "Zygalski sheet table",
            "Perforated sheets stacked over a lamp. Finds the day's key from repeated indicators.",
            500, 2, 2, Role.Cryptanalyst, 360, 1, ResearchId.ZygalskiSheets, 2),
        [StationType.Banburismus] = new(StationType.Banburismus, "Banburismus table",
            "Long Banbury sheets slid against each other to rule out wheel orders. Saves hours of Bombe time on naval keys.",
            400, 3, 1, Role.Cryptanalyst, 240, 1, ResearchId.Banburismus, 7),
        [StationType.Bombe] = new(StationType.Bombe, "Bombe",
            "Runs a menu through every wheel position until it stops on a possible key.",
            1800, 2, 1, Role.Wren, 480, 1, ResearchId.Bombe, 3, Upkeep: 30),
        [StationType.Bombe4] = new(StationType.Bombe4, "Four-rotor Bombe",
            "Taller, faster and temperamental. The only machine that can find the four-wheel Shark key.",
            3000, 2, 1, Role.Wren, 420, 1, ResearchId.FourRotorBombe, 11, Upkeep: 45),
        [StationType.Typex] = new(StationType.Typex, "Typex machine",
            "A British cipher machine modified to emulate Enigma. With the day's key, decrypts a message in minutes.",
            350, 1, 1, Role.Wren, 15, 8, null, 2, Upkeep: 5),
        [StationType.Translation] = new(StationType.Translation, "Translation desk",
            "Turns German service jargon into intelligence that Whitehall can act on.",
            150, 2, 1, Role.Linguist, 50, 6, null, 0),
        [StationType.Dispatch] = new(StationType.Dispatch, "Despatch box",
            "Sealed reports leave by teleprinter and motorcycle courier.",
            100, 1, 1, Role.Clerk, 10, 10, null, 0),
        [StationType.Canteen] = new(StationType.Canteen, "Canteen table",
            "Tea, toast and a sit-down. Tired staff get their energy back here.",
            250, 2, 2, null, 0, 0, null, 0, Seats: 4, Upkeep: 12),
        [StationType.CommonRoom] = new(StationType.CommonRoom, "Common room",
            "Armchairs, a chess set and yesterday's Times crossword. A break here lifts spirits as well as energy.",
            300, 2, 2, null, 0, 0, null, 1, Seats: 3, Upkeep: 5),
        [StationType.Rounders] = new(StationType.Rounders, "Rounders pitch",
            "A game on the lawn between watches. Daytime only. The best thing there is for morale.",
            150, 3, 3, null, 0, 0, null, 2, Seats: 6, Outdoor: true),
        [StationType.Checking] = new(StationType.Checking, "Checking machine",
            "Tests each Bombe stop to see if it really is the day's key. Without one, Bombes run longer to be sure, and a false stop sends the crib back to the start.",
            400, 1, 1, Role.Wren, 15, 3, ResearchId.Bombe, 3, Upkeep: 5),
        [StationType.Stove] = new(StationType.Stove, "Coke stove",
            "The timber huts were bitterly cold. From November to March, people in a hut without a stove tire faster and grumble. Brick buildings have central heating.",
            60, 1, 1, null, 0, 0, null, 0, Upkeep: 4),
        [StationType.Tube] = new(StationType.Tube, "Pneumatic tube",
            "Shoots messages to the tube station nearest their destination. Build one in each hut to save long walks.",
            300, 1, 1, null, 4, 12, ResearchId.PneumaticTubes, 4, Upkeep: 8),
        [StationType.OutstationLink] = new(StationType.OutstationLink, "Outstation teleprinter",
            "Sends Bombe menus to the outstation Bombes and receives their stops. Buy outstation Bombes from the Huts panel.",
            350, 1, 1, Role.Wren, 20, 3, ResearchId.Bombe, 9),
        [StationType.Testery] = new(StationType.Testery, "Testery desk",
            "Major Tester's section breaks Tunny wheel settings by hand with Turingery. Slow, brilliant and exhausting.",
            250, 2, 1, Role.Cryptanalyst, 540, 2, ResearchId.Turingery, 12),
        [StationType.Colossus] = new(StationType.Colossus, "Colossus",
            "Flowers' valve computer reads punched tape at 5,000 characters a second to find the chi-wheel settings.",
            6000, 3, 2, Role.Wren, 100, 2, ResearchId.Colossus, 13, Upkeep: 80),
        [StationType.TunnyMachine] = new(StationType.TunnyMachine, "Tunny machine",
            "A British copy of the Lorenz SZ42. With the day's wheel settings, prints German high command teleprinter traffic.",
            600, 1, 1, Role.Wren, 30, 6, ResearchId.Turingery, 12, Upkeep: 10),
    };

    public static readonly Dictionary<Net, NetDef> Nets = new()
    {
        [Net.Police] = new(Net.Police, "Police", "Ordnungspolizei hand cipher", Family.Hand, false, false, 1.0, false, Issuer.WarOffice, 1, 45, Service.Security),
        [Net.Abwehr] = new(Net.Abwehr, "Abwehr", "German military intelligence hand cipher", Family.Hand, false, false, 1.4, false, Issuer.SIS, 2, 60, Service.Security),
        [Net.Red] = new(Net.Red, "RED", "Luftwaffe general key", Family.Enigma, false, false, 1.0, true, Issuer.AirMinistry, 1, 35, Service.Luftwaffe),
        [Net.LightBlue] = new(Net.LightBlue, "LIGHT BLUE", "Luftwaffe operational key", Family.Enigma, false, false, 1.1, false, Issuer.AirMinistry, 1, 38, Service.Luftwaffe),
        [Net.Chaffinch] = new(Net.Chaffinch, "CHAFFINCH", "Afrika Korps army key", Family.Enigma, false, false, 1.3, false, Issuer.WarOffice, 2, 45, Service.Heer),
        [Net.Dolphin] = new(Net.Dolphin, "DOLPHIN", "Kriegsmarine home waters key", Family.Enigma, true, false, 3.0, false, Issuer.Admiralty, 2, 55, Service.Kriegsmarine),
        [Net.Shark] = new(Net.Shark, "SHARK", "U-boat Atlantic key, four wheels", Family.Enigma, true, true, 1.6, false, Issuer.Admiralty, 3, 80, Service.Kriegsmarine),
        [Net.Tunny] = new(Net.Tunny, "TUNNY", "German high command teleprinter cipher, Lorenz SZ42", Family.Lorenz, false, false, 1.0, false, Issuer.WarOffice, 4, 100, Service.Heer),
    };

    public static readonly Dictionary<ResearchId, ResearchDef> Research = new()
    {
        [ResearchId.CardIndex] = new(ResearchId.CardIndex, "Card index",
            "Every callsign, unit and name on a card. Registration 40% faster.", 8),
        [ResearchId.ZygalskiSheets] = new(ResearchId.ZygalskiSheets, "Zygalski sheets",
            "The Polish method. Build Zygalski sheet tables to find Enigma keys without a crib.", 0),
        [ResearchId.Cillies] = new(ResearchId.Cillies, "Cillies",
            "Lazy operators choose guessable message settings. Zygalski work 40% faster.", 10),
        [ResearchId.Bombe] = new(ResearchId.Bombe, "The Bombe",
            "Turing's machine. Build Bombes and checking machines to find the day's key from a crib.", 18),
        [ResearchId.DiagonalBoard] = new(ResearchId.DiagonalBoard, "Diagonal board",
            "Welchman's wiring exploits Enigma's reciprocity. Bombe runs 50% shorter.", 25),
        [ResearchId.Hut3Reporting] = new(ResearchId.Hut3Reporting, "Hut 3 reporting",
            "Standard formats for intelligence summaries. Translation 40% faster.", 15),
        [ResearchId.Banburismus] = new(ResearchId.Banburismus, "Banburismus",
            "Sequential statistics on long paper strips. Build Banburismus tables for naval keys.", 25),
        [ResearchId.Hollerith] = new(ResearchId.Hollerith, "Hollerith tabulators",
            "British Tabulating Machine Company punched cards. Registration and Typex 30% faster.", 20),
        [ResearchId.ShortWeatherCipher] = new(ResearchId.ShortWeatherCipher, "Short weather cipher",
            "The weather book from U-559 gives cribs for Shark.", 30),
        [ResearchId.FourRotorBombe] = new(ResearchId.FourRotorBombe, "Four-rotor Bombe",
            "A Bombe with a fourth fast wheel. Build them to attack Shark. Outstation Bombes are upgraded too.", 40),
        [ResearchId.PneumaticTubes] = new(ResearchId.PneumaticTubes, "Pneumatic tubes",
            "Lamson tubes between the huts. Build tube stations so messages skip the long walks.", 12),
        [ResearchId.Turingery] = new(ResearchId.Turingery, "Turingery",
            "Turing's hand method for Tunny wheel patterns. Build Testery desks and Tunny machines.", 30),
        [ResearchId.Colossus] = new(ResearchId.Colossus, "Colossus",
            "Tommy Flowers' valve computer, built at Dollis Hill in eleven months. Build Colossi.", 50),
    };

    public static readonly Dictionary<Issuer, string> IssuerNames = new()
    {
        [Issuer.WarOffice] = "War Office",
        [Issuer.AirMinistry] = "Air Ministry",
        [Issuer.Admiralty] = "Admiralty",
        [Issuer.SIS] = "SIS (MI6)",
    };

    // ------------------------------------------------------------- hut roles

    public sealed record ZoneRole(string Purpose, StationType[] Allows);

    /// <summary>Allowed in every hut: everyone needs tea, and the tubes have to start somewhere.</summary>
    public static readonly StationType[] Everywhere = { StationType.Canteen, StationType.Tube };

    /// <summary>What each building is for, after its real wartime job. Stations can only be built where they belong.</summary>
    public static readonly Dictionary<string, ZoneRole> ZoneRoles = new()
    {
        ["mansion"] = new("Headquarters. Any desk work can go here, but no machines: those went into the huts.",
            new[] { StationType.Inbox, StationType.Registration, StationType.HandCipher, StationType.Crib, StationType.Zygalski, StationType.Banburismus,
                    StationType.Translation, StationType.Dispatch, StationType.CommonRoom, StationType.Testery }),
        ["hut1"] = new("A small hut beside the Mansion. The first Bombe, 'Victory', was installed here in March 1940.",
            new[] { StationType.Bombe, StationType.Checking }),
        ["hut6"] = new("Army and Luftwaffe Enigma: cribs, Zygalski sheets, and the Typex machines of the decoding room.",
            new[] { StationType.Inbox, StationType.Registration, StationType.Crib, StationType.Zygalski, StationType.Typex }),
        ["hut11"] = new("The Bombe hut, built for the machines that followed Victory and run by the Wrens. Hot, loud and smelling of oil.",
            new[] { StationType.Bombe, StationType.Bombe4, StationType.Checking, StationType.OutstationLink }),
        ["hut3"] = new("Intelligence from Hut 6: translation, emendation and despatch to the commands. No machines.",
            new[] { StationType.Registration, StationType.Translation, StationType.Dispatch, StationType.CommonRoom }),
        ["hut8"] = new("Naval Enigma: cribs, Banburismus, the naval decoding room's Typex machines, and naval translation.",
            new[] { StationType.Inbox, StationType.Registration, StationType.Crib, StationType.Banburismus, StationType.Typex,
                    StationType.Translation, StationType.Dispatch }),
        ["blockf"] = new("The Testery and the Newmanry: Tunny by hand and by Colossus.",
            new[] { StationType.Inbox, StationType.Registration, StationType.Testery, StationType.Colossus, StationType.TunnyMachine,
                    StationType.Translation, StationType.Dispatch }),
    };

    /// <summary>Buildings with central heating: no stove needed, and none allowed.</summary>
    public static bool Heated(string zoneId) => zoneId is "mansion" or "blockf";
    public const double ColdDrainMult = 1.4;
    public const double ColdMoralePerMinute = 0.00006;

    public static bool AllowedIn(string zoneId, StationType t) =>
        t == StationType.Stove ? !Heated(zoneId) :
        Everywhere.Contains(t) || !ZoneRoles.TryGetValue(zoneId, out var r) || r.Allows.Contains(t);

    /// <summary>The zone ids a station may go in, in map order.</summary>
    public static IEnumerable<string> ZonesFor(StationType t) => ZoneRoles.Keys.Where(z => AllowedIn(z, t));

    public static StationDef Station(StationType t) => Stations[t];
    public static NetDef NetOf(Net n) => Nets[n];

    public static (int w, int h) Dims(StationType t, int rot)
    {
        var d = Stations[t];
        return (rot & 1) == 0 ? (d.W, d.H) : (d.H, d.W);
    }

    public static bool IsRest(StationType t) => Stations[t].Seats > 0;

    /// <summary>The machine that decrypts a net's traffic once the day's key is known.</summary>
    public static StationType DecryptStep(Net n) => Nets[n].Family == Family.Lorenz ? StationType.TunnyMachine : StationType.Typex;

    public static double SkillMultiplier(int skill) => 0.75 + 0.25 * skill;

    public static readonly string[] FemaleFirst =
    {
        "Joan", "Mavis", "Margaret", "Dorothy", "Phyllis", "Barbara", "Jean", "Betty", "Audrey",
        "Eileen", "Joyce", "Doreen", "Winifred", "Pamela", "Sheila", "Hilda", "Ruth", "Iris",
        "Olive", "Violet", "Diana", "Rosemary", "Evelyn", "Marjorie", "Gwen", "Nancy", "Peggy",
    };

    public static readonly string[] MaleFirst =
    {
        "Arthur", "Leonard", "Frank", "Harold", "Stanley", "Geoffrey", "Dennis", "Ronald",
        "Peter", "John", "Hugh", "Rex", "Oliver", "Bernard", "Cyril", "Edward", "Frederick",
        "Henry", "Kenneth", "Leslie", "Nigel", "Reginald", "Walter", "Philip", "Gerald",
    };

    public static readonly string[] Surnames =
    {
        "Ashworth", "Bellamy", "Brookes", "Carrington", "Dawson", "Ellis", "Fairfax", "Gibbs",
        "Hartley", "Irving", "Jennings", "Kemp", "Lacey", "Marsh", "Norris", "Oakley",
        "Pembroke", "Quinn", "Radcliffe", "Sutton", "Thorne", "Underwood", "Vickers", "Whitlock",
        "Yardley", "Abbott", "Barnes", "Cole", "Drummond", "Frost", "Graves", "Hale", "Lowe",
        "Mercer", "Pryce", "Rowe", "Sinclair", "Tate", "Ainsworth", "Blackwood",
    };
}

/// <summary>Small, serialisable xorshift RNG so saves are reproducible.</summary>
public sealed class Rng
{
    public ulong State { get; set; }

    public Rng() : this(0x9E3779B97F4A7C15UL) { }

    public Rng(ulong seed) { State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }

    public ulong Next()
    {
        var x = State;
        x ^= x << 13;
        x ^= x >> 7;
        x ^= x << 17;
        State = x;
        return x;
    }

    public double NextDouble() => (Next() >> 11) * (1.0 / 9007199254740992.0);
    public int Range(int min, int maxExclusive) => min + (int)(NextDouble() * (maxExclusive - min));
    public double Range(double min, double max) => min + NextDouble() * (max - min);
    public bool Chance(double p) => NextDouble() < p;
    public T Pick<T>(IReadOnlyList<T> list) => list[Range(0, list.Count)];
}
