using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

public sealed class Station
{
    public int Id { get; set; }
    public StationType Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Rot { get; set; }

    public List<int> Input { get; set; } = new();
    public List<int> Output { get; set; } = new();
    public int ReservedInputs { get; set; }
    public int? OperatorId { get; set; }
    public bool OperatorPresent { get; set; }
    /// <summary>When the desk was left with a message half done (game minutes), or -1.</summary>
    public double LeftAt { get; set; } = -1;
    public int? Current { get; set; }
    public double WorkDone { get; set; }
    public double WorkTotal { get; set; }
    public double IdleMinutes { get; set; }
    public bool PendingRemoval { get; set; }
    public bool Obsolete { get; set; }
    public double LastWorked { get; set; } = -1e9;
    /// <summary>Messages this station has finished, for the statistics panel.</summary>
    public int Processed { get; set; }

    // Def and UseTile are asked for constantly; both are remembered until the type or placement changes.
    StationDef? _def;
    StationType _defType;
    public StationDef Def
    {
        get
        {
            if (_def == null || _defType != Type) { _def = Defs.Station(Type); _defType = Type; }
            return _def;
        }
    }
    public (int w, int h) Dims => Defs.Dims(Type, Rot);
    public double Progress => WorkTotal > 0 ? Math.Clamp(WorkDone / WorkTotal, 0, 1) : 0;
    public bool Working => Current != null && (OperatorPresent || IsMachine);

    /// <summary>Bombes and Colossus run by themselves once a Wren has set them going.</summary>
    public bool IsMachine => Type is StationType.Bombe or StationType.Bombe4 or StationType.Colossus or StationType.HeathRobinson;

    /// <summary>0 when freshly serviced; breakdowns get likely past about 0.7.</summary>
    public double Wear { get; set; }
    /// <summary>While Now is before this, the machine is out of action (broken down or being serviced).</summary>
    public double DownUntil { get; set; }
    /// <summary>True when the downtime is a planned service rather than a breakdown.</summary>
    public bool Servicing { get; set; }
    public bool Wears => Defs.WearLife(Type) > 0;
    public bool IsDown(double now) => DownUntil > now;

    public IEnumerable<(int x, int y)> Footprint()
    {
        var (w, h) = Dims;
        for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                yield return (X + dx, Y + dy);
    }

    (StationType t, int x, int y, int rot)? _useKey;
    (int x, int y) _use;
    public (int x, int y) UseTile
    {
        get
        {
            var key = (Type, X, Y, Rot);
            if (_useKey != key) { _use = UseTileFor(Type, X, Y, Rot); _useKey = key; }
            return _use;
        }
    }

    public static (int x, int y) UseTileFor(StationType t, int x, int y, int rot)
    {
        var (w, h) = Defs.Dims(t, rot);
        return (rot & 3) switch
        {
            0 => (x + (w - 1) / 2, y + h),
            1 => (x + w, y + (h - 1) / 2),
            2 => (x + (w - 1) / 2, y - 1),
            _ => (x - 1, y + (h - 1) / 2),
        };
    }

    /// <summary>Whether this station takes messages that need the given step next.</summary>
    /// <summary>For a teleprinter: the networks routed to it, or null for all of them.</summary>
    public List<Net>? Receives { get; set; }
    public bool ReceivesNet(Net n) => Receives == null || Receives.Contains(n);

    public bool Accepts(StationType next) =>
        !Obsolete && !PendingRemoval &&
        (Type == next || (Type == StationType.Bombe4 && next == StationType.Bombe));

    public bool IsRest => Def.Seats > 0;

    public int FreeInput => Def.InputCap - Input.Count - ReservedInputs;
}

public enum TaskKind { Carry, Operate, Rest, Leave }

public sealed class StaffTask
{
    public TaskKind Kind { get; set; }
    public int Phase { get; set; }
    public int Source { get; set; } = -1;
    public int Dest { get; set; } = -1;
    public List<int> Msgs { get; set; } = new();
    public int Seat { get; set; }
    /// <summary>A break taken for want of work rather than from tiredness: they'll get up when work comes in.</summary>
    public bool Loafing { get; set; }
}

public sealed class Staff
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Role Role { get; set; }
    public int Skill { get; set; } = 1;
    public int Salary { get; set; }
    public bool Female { get; set; }
    public int Look { get; set; }
    public string? Notable { get; set; }
    public string? Bio { get; set; }
    public List<StationType> Specialties { get; set; } = new();
    public List<Trait> Traits { get; set; } = new();
    /// <summary>Careless talkers aren't known to be one until MI5 notices.</summary>
    public bool CarelessKnown { get; set; }
    public Shift Shift { get; set; } = Shift.Office;
    /// <summary>Nights worked in a row on the night watch.</summary>
    public int NightsRunning { get; set; }
    public int? Pinned { get; set; }
    /// <summary>The building this person is posted to (zone id), or null to work wherever they're needed.</summary>
    public string? Posted { get; set; }
    public double Energy { get; set; } = 1;
    public double Morale { get; set; } = Defs.MoraleBaseline;
    public bool Away { get; set; }
    public double HomeX { get; set; }
    public double HomeY { get; set; }
    public double LowMoraleDays { get; set; }

    /// <summary>Minutes on duty with nothing to do.</summary>
    public double IdleMinutes { get; set; }
    /// <summary>Minutes spent getting up from a shared table at the end of a watch (they leave one by one).</summary>
    public double LeaveWait { get; set; }
    /// <summary>Minutes spent waiting for someone standing in the way to move.</summary>
    public double WaitedFor { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public List<(int x, int y)> Path { get; set; } = new();
    public StaffTask? Task { get; set; }
    public bool Leaving { get; set; }
    public int Decrypts { get; set; }

    public (int x, int y) TilePos => ((int)Math.Floor(X), (int)Math.Floor(Y));
    public bool Idle => Task == null && !Leaving && !Away;
    /// <summary>Free to be given work: idle, or sitting over a cup of tea for want of anything to do.</summary>
    public bool Available => Idle || (Task is { Kind: TaskKind.Rest, Loafing: true } && !Leaving && !Away && !Tired);
    public bool Has(Trait t) => Traits.Contains(t);
    public bool Tired => Energy < Defs.BreakAt;

    public double SpeedFor(StationType t)
    {
        double v = Defs.SkillMultiplier(Skill) * (Specialties.Contains(t) ? 1.5 : 1.0);
        if (Has(Trait.NativeGerman) && t == StationType.Translation) v *= 1.4;
        if (Has(Trait.ChessPlayer) && t is StationType.Crib or StationType.Banburismus) v *= 1.2;
        if (Has(Trait.CrosswordFiend) && t == StationType.Crib) v *= 1.25;
        return v;
    }

    /// <summary>Minutes of desk or machine work done; skill rises with it.</summary>
    public double Experience { get; set; }
    /// <summary>Lodged miles away and bussed in: slower and grumpier.</summary>
    public bool FarBillet { get; set; }
    /// <summary>The zone this person heads: they manage it rather than work a desk.</summary>
    public string? HeadOf { get; set; }
    public HeadStyle HeadStyle { get; set; }
    /// <summary>The building they last finished a job in: their hut, for the head's daily effects.</summary>
    public string? LastZone { get; set; }
    /// <summary>Off sick until this time; they don't come back on watch before it.</summary>
    public double SickUntil { get; set; }
    /// <summary>Worked in a freezing hut today (reset each midnight).</summary>
    public bool Chilled { get; set; }
    public bool IsSick(double now) => SickUntil > now;
    /// <summary>Why they're away from the Park while SickUntil runs: null for illness, or "leave" / "breakdown".</summary>
    public string? OffReason { get; set; }
    /// <summary>Strain built up over days of exhaustion (0 to 1). Near the top they risk breaking down.</summary>
    public double Strain { get; set; }
    /// <summary>The lowest energy reached today; feeds strain at midnight.</summary>
    public double LowToday { get; set; } = 1;
    public bool StrainWarned { get; set; }
    /// <summary>Someone they've fallen out with; both work worse in the same room. Ends on RivalUntilDay.</summary>
    public int? Rival { get; set; }
    public int RivalUntilDay { get; set; }
    /// <summary>A new recruit still being vetted and on the train to Bletchley; 0 once they've arrived.</summary>
    public double ArrivesAt { get; set; }
    public bool Arriving(double now) => ArrivesAt > now;

    /// <summary>How fast they're working right now: tired or unhappy people are slower.</summary>
    public double WorkRate =>
        (Energy >= 0.5 ? 1.0 : 0.6 + 0.8 * Energy) * (0.8 + 0.35 * Morale) * (FarBillet ? Defs.FarBilletRate : 1);

    /// <summary>Traits shown to the player (a careless talker is hidden until found out).</summary>
    public IEnumerable<Trait> KnownTraits() =>
        Traits.Where(t => t != Trait.CarelessTalker || CarelessKnown);
}

public enum Loc { Output, Carried, Input, Processing, Remote, Done }

public sealed class Message
{
    public int Id { get; set; }
    public Net Net { get; set; }
    public int ContractId { get; set; }
    public int Day { get; set; }
    public double Created { get; set; }
    public StationType? Next { get; set; } = StationType.Registration;
    public Loc Loc { get; set; } = Loc.Output;
    public int LocId { get; set; }
    public bool Reserved { get; set; }
    /// <summary>The player placed this key attempt's crib by hand.</summary>
    public bool Dragged { get; set; }
    /// <summary>The player slid this key attempt's Banbury sheets by hand.</summary>
    public bool Slid { get; set; }
    /// <summary>An Offizier message: under the day's naval key, a second layer on an officers-only setting.</summary>
    public bool Offizier { get; set; }
    public bool Carrier { get; set; }
    public bool Banbury { get; set; }
    public double CribQuality { get; set; } = 0.8;
    /// <summary>When it arrived where it is now, for the bottleneck overlay.</summary>
    public double Since { get; set; }
    public bool Garbled { get; set; }
    /// <summary>Garbled: a clean copy has been asked for from the Y-station.</summary>
    public double HoldUntil { get; set; } = -1;
    /// <summary>A Bombe stop that failed checking; the next run continues where it left off.</summary>
    public bool Rerun { get; set; }
}

public sealed class RemoteJob
{
    public int Msg { get; set; }
    public int Link { get; set; }
    public double Done { get; set; }
    public bool FourRotor { get; set; }
}

public enum ContractState { Offered, Active, Completed, Failed, Expired }

public sealed class Contract
{
    public int Id { get; set; }
    public Issuer Issuer { get; set; }
    public Net Net { get; set; }
    public string Title { get; set; } = "";
    public string Flavour { get; set; } = "";
    public int Quantity { get; set; }
    public int Spawned { get; set; }
    public int Delivered { get; set; }
    public double OfferedAt { get; set; }
    public double OfferExpires { get; set; }
    public double Days { get; set; }
    public double AcceptedAt { get; set; }
    public double Due { get; set; }
    public int Reward { get; set; }
    public int Rep { get; set; }
    public string? Tag { get; set; }
    /// <summary>A MOST IMMEDIATE request: hours, not days, and ignoring it is noticed.</summary>
    public bool Flash { get; set; }
    /// <summary>Put first by the player: its paper is worked and carried before anything else's.</summary>
    public bool Priority { get; set; }
    /// <summary>A standing order: PerDay decrypts every day, checked against a running total each 24 hours.</summary>
    public int PerDay { get; set; }
    public bool Standing => PerDay > 0;
    public int MilestonesChecked { get; set; }
    public int MilestonesMet { get; set; }
    /// <summary>Paid for each milestone met (the completion Reward is the bonus on top).</summary>
    public int MilestonePay { get; set; }
    public int TotalDays => (int)Math.Floor(Days);
    public ContractState State { get; set; }
    public List<double> SpawnTimes { get; set; } = new();
}

public enum KeyStatus { None, Attempting, Broken }

public sealed class Stats
{
    public Dictionary<Net, int> Delivered { get; set; } = new();
    public Dictionary<Net, int> KeyBreaks { get; set; } = new();
    public Dictionary<Net, int> BombeBreaks { get; set; } = new();
    public int MilestonesMet { get; set; }
    public int MilestonesMissed { get; set; }
    public int OpsWithheld { get; set; }
    public int CribsDragged { get; set; }
    public int BanburiesSlid { get; set; }
    public int OffizierRead { get; set; }
    /// <summary>Decrypts delivered more than a day after they were intercepted.</summary>
    public int LateDecrypts { get; set; }
    /// <summary>Typex runs that came out as gibberish because the wheels were set wrong.</summary>
    public int TypexSlips { get; set; }
    public int FreqChanges { get; set; }
    /// <summary>Half-done messages picked up after sitting cold, and the desk-minutes of work lost re-reading them.</summary>
    public int ColdHandovers { get; set; }
    public int PapersFound { get; set; }
    public int Sweeps { get; set; }
    public double HandoverMinutesLost { get; set; }
    public int InspectionsPassed { get; set; }
    public int InspectionsFailed { get; set; }
    public int StandingAccepted { get; set; }
    public int DilemmasResolved { get; set; }
    public HashSet<string> ScriptedDone { get; set; } = new();
    /// <summary>Reputation gained and lost over the whole campaign, by cause.</summary>
    public Dictionary<string, double> RepByCause { get; set; } = new();
    public int DeliveredTotal { get; set; }
    public int ContractsCompleted { get; set; }
    public int ContractsFailed { get; set; }
    public int Breakdowns { get; set; }
    /// <summary>Messages passed straight across between neighbouring desks, no runner needed.</summary>
    public int HandOvers { get; set; }

    public Stats Clone() => new()
    {
        Delivered = new(Delivered),
        KeyBreaks = new(KeyBreaks),
        BombeBreaks = new(BombeBreaks),
        DilemmasResolved = DilemmasResolved,
        ScriptedDone = new(ScriptedDone),
        RepByCause = new(RepByCause),
        DeliveredTotal = DeliveredTotal,
        ContractsCompleted = ContractsCompleted,
        ContractsFailed = ContractsFailed,
        Breakdowns = Breakdowns,
    };

    public static int Get(Dictionary<Net, int> d, Net n) => d.TryGetValue(n, out var v) ? v : 0;
    public static void Inc(Dictionary<Net, int> d, Net n) => d[n] = Get(d, n) + 1;
}

public enum EventKind { Info, Good, Bad, ActStarted, ActComplete, Won, Lost, Dilemma, Sound }

public sealed record GameEvent(EventKind Kind, string Text);

public sealed class RepEntry
{
    public double At { get; set; }
    public double Delta { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>One day's numbers, for the statistics panel.</summary>
public sealed class DayRecord
{
    public int Day { get; set; }
    public int Delivered { get; set; }
    public int KeysBroken { get; set; }
    public int Backlog { get; set; }
    public int Funds { get; set; }
    public double Reputation { get; set; }
    public int Staff { get; set; }
    public double Suspicion { get; set; }
    public double Morale { get; set; }
}

/// <summary>A decision put to the player: what to do with a piece of intelligence, or a security scare.</summary>
public sealed class Dilemma
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>What we know and how we know it: the source, how sure it is, and what's at stake. Shown before the options.</summary>
    public string Context { get; set; } = "";
    public Service Service { get; set; }
    public int? StaffId { get; set; }
    public double Created { get; set; }
    /// <summary>What raised it, when the sim needs to find it again ("op" for a great operation's decision).</summary>
    public string? Tag { get; set; }
    public List<DilemmaOption> Options { get; set; } = new();
}

public sealed class DilemmaOption
{
    public string Label { get; set; } = "";
    public string Outcome { get; set; } = "";
    public int Funds { get; set; }
    public double Rep { get; set; }
    public double Suspicion { get; set; }
    public double Morale { get; set; }
    public double Tonnage { get; set; }
    public int Points { get; set; }
    public bool DismissStaff { get; set; }
    public bool RevealTalker { get; set; }
    /// <summary>Acting under a cover story: direction-finding makes it more believable.</summary>
    public bool Cover { get; set; }
    /// <summary>A special effect handled by the sim (a crisis response), or null.</summary>
    public string? Action { get; set; }

    /// <summary>One-line summary of the effects, for buttons.</summary>
    public string Effects()
    {
        var parts = new List<string>();
        if (Funds != 0) parts.Add(Funds > 0 ? $"+£{Funds}" : $"−£{-Funds}");
        if (Rep != 0) parts.Add($"{(Rep > 0 ? "+" : "−")}{Math.Abs(Rep):0} reputation");
        if (Points != 0) parts.Add($"+{Points} breakthroughs");
        if (Suspicion > 0) parts.Add($"+{Suspicion:0} suspicion");
        if (Suspicion < 0) parts.Add($"−{-Suspicion:0} suspicion");
        if (Morale != 0) parts.Add(Morale > 0 ? "morale up" : "morale down");
        if (Tonnage > 0) parts.Add($"{Tonnage / 1000:0}k tons lost");
        if (DismissStaff) parts.Add("dismissal");
        return string.Join(" · ", parts);
    }
}
