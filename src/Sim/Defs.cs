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
    Stove, SickBay, Index, TrafficAnalysis, HeathRobinson,
}

public enum Family { Hand, Enigma, Lorenz }

public enum Net { Police, Abwehr, Red, LightBlue, Chaffinch, Dolphin, Shark, Tunny, Brown }

public enum Issuer { WarOffice, AirMinistry, Admiralty, SIS }

/// <summary>The German organisation whose signals security reacts to suspiciously good Allied luck.</summary>
public enum Service { Luftwaffe, Heer, Kriegsmarine, Security }

/// <summary>How freely commanders may act on ULTRA intelligence.</summary>
public enum Policy { Cautious, Standard, Bold }

public enum Difficulty { Easy, Normal, Hard }

public enum GameMode { Campaign, FreePlay }

public enum Weather { Fine, Overcast, Rain, Fog, Snow, Storm }

/// <summary>Working hours. Office hours until the watch system starts, then three watches round the clock.</summary>
public enum Shift { Office, Day, Evening, Night }

public enum Trait { CrosswordFiend, NativeGerman, NightOwl, ChessPlayer, Tireless, Meticulous, CarelessTalker }

public enum ResearchId
{
    CardIndex, ZygalskiSheets, Cillies, Bombe, DiagonalBoard, Hut3Reporting,
    Banburismus, Hollerith, ShortWeatherCipher, FourRotorBombe,
    PneumaticTubes, Turingery, Colossus, HeathRobinson,
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
public static partial class Defs
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
    /// <summary>Working minutes needed to reach skill 2 and skill 3. Each step up brings a pay rise.</summary>
    public static readonly double[] ExperienceFor = { 0, 0, 4000, 12000 };
    /// <summary>Act index from which the three-watch system is available.</summary>
    public const int WatchesFromAct = 5;

    // ---- Economy ----
    public const double StartReputation = 50;
    public const int GrantBase = 200;
    public const double GrantPerRep = 7;
    /// <summary>Whitehall gets used to miracles: reputation slips this much a day, plus a bit more each act.</summary>
    public const double ExpectationBase = 0.5;
    /// <summary>Above this balance at the weekly accounts, the Treasury claws back part of the grant.</summary>
    public const int TreasuryComfortable = 2500;
    // ---- Operation Ruthless (autumn 1940) ----
    public const int RuthlessAct = 6;
    public const int RuthlessCost = 300;
    public const double RuthlessSuccess = 0.4;
    public const int RuthlessPoints = 80;
    public const double RuthlessSuspicion = 18;
    // ---- Hut 3 ----
    /// <summary>From "Their Finest Hour": machine-cipher decrypts are indexed after translation, before despatch.</summary>
    public const int IndexFromAct = 5;
    /// <summary>Traffic analysis (later SIXTA) from the Victory act, when the Bombes arrive.</summary>
    public const int SixtaFromAct = 3;
    /// <summary>Messages of a network's day to log before its externals give a crib hint; and the hint's effect.</summary>
    public const int SixtaNeeded = 2;
    public const double SixtaCribMult = 0.6, SixtaBombeMult = 0.85;

    // ---- Layout ----
    /// <summary>Paper passes straight across to a next-step desk this close (Manhattan, between working tiles, same hut).</summary>
    public const int HandOverReach = 2;
    /// <summary>Walking slows by this much for each other person on the tile ahead or the one you're on.</summary>
    // Crises (from act 2): a power cut stops the machines; a surge brings a run of MOST IMMEDIATE requests.
    public const int CrisesFromAct = 2;
    /// <summary>Visits from Whitehall: from the Battle of Britain, every 9-13 days.</summary>
    public const int InspectionsFromAct = 3;
    /// <summary>Share of naval decrypts that turn out to be Offizier messages, and their time at the hand-cipher desk.</summary>
    public const double OffizierShare = 0.2, OffizierMult = 0.8;
    /// <summary>Intelligence goes off: decrypts delivered this many minutes after interception earn half the points, then none.</summary>
    /// <summary>Chance a Wren sets a Typex wrongly, by skill 1..3 (doubled when worn out; halved for the meticulous or a Typex specialist).</summary>
    public static readonly double[] TypexSlip = { 0, 0.07, 0.04, 0.02 };
    public const double StaleIntelAfter = 24 * 60, DeadIntelAfter = 48 * 60;
    public const int InspectionEveryDays = 9;
    public const double InspectionGoodRep = 2;
    public const double InspectionBadRep = 2;
    /// <summary>Share of the next weekly grant the Treasury withholds after a visit that went badly.</summary>
    public const double InspectionGrantCut = 0.25;
    public const double CrisisChance = 0.07;            // per day, scaled by difficulty's wear multiplier
    public const int GeneratorCost = 250, OvertimeCost = 200;
    public const double OvertimeRate = 1.25, OvertimeDrain = 1.35, OvertimeMorale = -0.06, OvertimeDays = 2;
    // Burnout: strain rises on days someone ends exhausted, falls on easy days.
    public const double StrainWarnAt = 0.75, StrainBreakChance = 0.3, RestLeaveDays = 4, BreakdownDays = 10;
    // Rivalries between cryptanalysts working in the same room.
    public const double RivalChance = 0.025, RivalWorkRate = 0.85, RivalMoralePerMinute = 0.00006;
    // Standing orders: from the Fifteenth of September (act 6), some requests are N a day for a few days with daily milestones.
    public const int StandingFromAct = 6, StandingDays = 3;
    public const double StandingChance = 0.15, MilestoneMissedRep = 0.75, MilestoneRep = 0.5, MilestoneGraceDays = 1.25;
    // ULTRA and the war map: deciding how a won operation's decrypts are used.
    public const double OpDecideHours = 12, OpActSuspicion = 4, OpCoverSuspicion = 0.8;
    public const int CoverPerWeek = 2;
    public const double WrongDragMinutes = 20;
    public const double CrowdSlow = 0.15;
    public const double CrowdFloor = 0.6;

    /// <summary>The steps that feed a station, and the steps it feeds: where it wants to sit.</summary>
    public static StationType[] FlowNeighbours(StationType t) => t switch
    {
        StationType.Inbox => new[] { StationType.Registration },
        StationType.Registration => new[] { StationType.Inbox, StationType.HandCipher, StationType.Crib, StationType.Typex, StationType.TrafficAnalysis },
        StationType.TrafficAnalysis => new[] { StationType.Registration, StationType.Crib },
        StationType.HandCipher => new[] { StationType.Registration, StationType.Translation },
        StationType.Crib => new[] { StationType.Registration, StationType.Zygalski, StationType.Banburismus, StationType.Bombe, StationType.Bombe4 },
        StationType.Zygalski or StationType.Banburismus => new[] { StationType.Crib, StationType.Bombe, StationType.Bombe4 },
        StationType.Bombe or StationType.Bombe4 => new[] { StationType.Crib, StationType.Checking },
        StationType.Checking => new[] { StationType.Bombe, StationType.Bombe4 },
        StationType.Typex => new[] { StationType.Registration, StationType.Translation },
        StationType.Translation => new[] { StationType.Typex, StationType.HandCipher, StationType.Index, StationType.Dispatch },
        StationType.Index => new[] { StationType.Translation, StationType.Dispatch },
        StationType.Dispatch => new[] { StationType.Translation, StationType.Index },
        _ => Array.Empty<StationType>(),
    };
    // ---- Traffic growth ----
    /// <summary>German wireless traffic grew year on year; requests grow with each chapter, so the Park must grow too.</summary>
    public const double TrafficPerAct = 0.06;
    public static double TrafficScale(int act) => 1 + TrafficPerAct * Math.Max(0, act);
    // ---- Heads of huts ----
    // ---- Wrens ----
    /// <summary>From the Bombe build-up of 1940 until 'Action this day', only one Wren a week comes forward.</summary>
    public const int WrenShortageFrom = 5;
    // ---- Ministries ----
    public const double NeglectDays = 10;
    public const double NeglectWarnDays = 6;
    public const double NeglectRep = 3;
    // ---- Stale cribs ----
    public const int StaleFromAct = 5;
    public const double StaleChance = 0.08;
    public const double StaleCribMult = 1.6;
    public const double StaleBombeMult = 1.3;
    // ---- All hands ----
    public const double AllHandsCooldownDays = 7;
    public const double AllHandsHours = 4;
    public const double AllHandsMorale = 0.12;
    public const int AllHandsPay = 6;   // overtime, per person called in
    public const double AllHandsDrain = 1.5;
    // ---- Recruitment ----
    /// <summary>Hours from accepting a post to reporting for duty: interview, vetting, the Official Secrets Act and the train.</summary>
    public static double VettingHours(Role r, Difficulty d) =>
        (r == Role.Messenger ? 6 : 16) * d switch { Difficulty.Easy => 0.5, _ => 1.0 };
    // ---- Illness ----
    public const double SickChanceSummer = 0.002;
    public const double SickChanceWinter = 0.008;
    public const double SickContagion = 0.0025;   // per colleague already off sick
    public const double SickChilledMult = 2.0;
    public const double SickFarBilletMult = 1.5;
    public const double SickTiredMult = 1.5;
    public const double SickBayDuration = 0.6;
    public const double SickBayContagion = 0.5;
    // ---- The Blitz ----
    public const double RaidChance = 0.22;
    public const double RaidBombChance = 0.06;
    public const double RaidWorkRate = 0.5;
    // ---- Billets ----
    public const int StartBillets = 20;
    public const int BilletBlock = 10;
    public const int BilletCost = 400;
    public const double FarBilletRate = 0.9;
    public const double FarBilletMoralePerDay = 0.035;
    // ---- Flash requests ----
    public const int FlashFromAct = 2;
    public const double FlashOfferHours = 3;
    public const double FlashIgnoredRep = 2;
    public const double TreasuryClawback = 0.4;
    public const double ExpectationPerAct = 0.17;
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
        StationType.HeathRobinson => 700, // two tapes kept in step by sprockets: they stretched, slipped and snapped
        StationType.Typex => 3000,        // rotors, contacts and a printer, all worked hard
        StationType.Inbox => 1,           // teleprinters wear per message received, not per minute: see TeleprinterWearPerMsg
        _ => 0,
    };

    /// <summary>Teleprinters: wear per message received (fully worn after ~150) and breakdown chance per message, times wear squared.</summary>
    public const double TeleprinterWearPerMsg = 1 / 150.0, TeleprinterHazard = 0.025;

    /// <summary>Who mends it: the Post Office engineers looked after the teleprinter lines, the RAF fitters the machines.</summary>
    public static string Fitters(StationType t) => t == StationType.Inbox ? "the GPO engineers" : "the RAF fitters";

    public static int RepairCostFor(StationType t) => t == StationType.Inbox ? 30 : RepairCost;

    /// <summary>Repair time: a Typex is a far simpler machine than a Bombe.</summary>
    public static double RepairMinutesFor(StationType t) => t switch
    {
        StationType.Typex => RepairMinutes * 0.5,
        StationType.HeathRobinson => RepairMinutes * 0.35,   // splice the tape and start again
        StationType.Inbox => RepairMinutes * 0.4,            // a GPO engineer and a new print head
        _ => RepairMinutes,
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
        [Trait.Meticulous] = new(Trait.Meticulous, "Meticulous", "Fewer mistakes on hand ciphers, sheets, checking and Typex settings.", true),
        [Trait.CarelessTalker] = new(Trait.CarelessTalker, "Careless talker", "Says too much in the pub. Raises German suspicion and draws MI5's attention.", false),
    };

    public static readonly Dictionary<Difficulty, DifficultyDef> Difficulties = new()
    {
        [Difficulty.Easy] = new(Difficulty.Easy, "Easy", "More money, longer deadlines, slower German suspicion.", 2200, 1.2, 1, 0.6, 0.7, 0.5),
        [Difficulty.Normal] = new(Difficulty.Normal, "Normal", "As intended.", 1500, 0.9, 0, 1.0, 1.0),
        [Difficulty.Hard] = new(Difficulty.Hard, "Hard", "Less money, tighter deadlines, and the Germans notice everything.", 1100, 0.85, -0.25, 1.4, 1.0, 1.2),
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
        [StationType.Index] = new(StationType.Index, "Card index",
            "Hut 3's Index: every unit, officer, place and callsign in the decrypts on a card, so each new message is read against everything already known. From the Battle of Britain, Enigma and Tunny decrypts are indexed before they go out.",
            120, 2, 1, Role.Clerk, 7, 8, null, Defs.IndexFromAct),
        [StationType.TrafficAnalysis] = new(StationType.TrafficAnalysis, "Traffic analysis",
            "Hut 6's traffic analysts (later SIXTA) log each message's externals: callsigns, frequencies, routing. Two of a network's messages logged in a day tell the crib desk who is sending what, so that key's cribs come faster and its Bombe runs shorter.",
            140, 2, 1, Role.Clerk, 12, 6, null, Defs.SixtaFromAct),
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
        [StationType.SickBay] = new(StationType.SickBay, "Sick bay",
            "Two iron beds, a screen and a nursing sister in the Mansion. The sick get well sooner and pass less on.",
            350, 2, 1, null, 0, 0, null, 2, Upkeep: 15),
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
        [StationType.HeathRobinson] = new(StationType.HeathRobinson, "Heath Robinson",
            "Max Newman's first machine for Tunny: two loops of punched tape run in step past photocells, counting to find the chi-wheel settings. Much faster than the Testery, but the tapes stretch and snap.",
            1800, 2, 1, Role.Wren, 220, 2, ResearchId.HeathRobinson, 12, Upkeep: 30),
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
        [Net.Brown] = new(Net.Brown, "BROWN", "Luftwaffe beam-navigation key (KG 100)", Family.Enigma, false, false, 1.25, false, Issuer.AirMinistry, 2, 42, Service.Luftwaffe),
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
        [ResearchId.HeathRobinson] = new(ResearchId.HeathRobinson, "Heath Robinson",
            "Newman's tape-comparing machine for the Newmanry, named after the cartoonist's contraptions. Build Heath Robinsons.", 30),
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
                    StationType.Translation, StationType.Dispatch, StationType.CommonRoom, StationType.Testery, StationType.Index, StationType.TrafficAnalysis }),
        ["hut1"] = new("A small hut beside the Mansion. The first Bombe, 'Victory', was installed here in March 1940.",
            new[] { StationType.Bombe, StationType.Checking }),
        ["hut6"] = new("Army and Luftwaffe Enigma: cribs, Zygalski sheets, and the Typex machines of the decoding room.",
            new[] { StationType.Inbox, StationType.Registration, StationType.Crib, StationType.Zygalski, StationType.Typex, StationType.TrafficAnalysis }),
        ["hut11"] = new("The Bombe hut, built for the machines that followed Victory and run by the Wrens. Hot, loud and smelling of oil.",
            new[] { StationType.Bombe, StationType.Bombe4, StationType.Checking, StationType.OutstationLink }),
        ["hut3"] = new("Intelligence from Hut 6: translation, emendation and despatch to the commands. No machines.",
            new[] { StationType.Registration, StationType.Translation, StationType.Index, StationType.Dispatch, StationType.CommonRoom }),
        ["hut8"] = new("Naval Enigma: cribs, Banburismus, the naval decoding room's Typex machines, and naval translation.",
            new[] { StationType.Inbox, StationType.Registration, StationType.Crib, StationType.Banburismus, StationType.Typex,
                    StationType.Translation, StationType.Index, StationType.Dispatch, StationType.TrafficAnalysis }),
        ["blockf"] = new("The Testery and the Newmanry: Tunny by hand and by Colossus.",
            new[] { StationType.Inbox, StationType.Registration, StationType.Testery, StationType.Colossus, StationType.HeathRobinson, StationType.TunnyMachine,
                    StationType.Translation, StationType.Index, StationType.Dispatch }),
    };

    /// <summary>Buildings with central heating: no stove needed, and none allowed.</summary>
    public static bool Heated(string zoneId) => zoneId is "mansion" or "blockf";
    public const double ColdDrainMult = 1.4;
    public const double ColdMoralePerMinute = 0.00006;

    public static bool AllowedIn(string zoneId, StationType t) =>
        t == StationType.Stove ? !Heated(zoneId) :
        t == StationType.SickBay ? zoneId == "mansion" :
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
