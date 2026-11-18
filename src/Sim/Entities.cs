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
    public int? Current { get; set; }
    public double WorkDone { get; set; }
    public double WorkTotal { get; set; }
    public double IdleMinutes { get; set; }
    public bool PendingRemoval { get; set; }
    public bool Obsolete { get; set; }
    public double LastWorked { get; set; } = -1e9;
    /// <summary>Messages this station has finished, for the statistics panel.</summary>
    public int Processed { get; set; }

    public StationDef Def => Defs.Station(Type);
    public (int w, int h) Dims => Defs.Dims(Type, Rot);
    public double Progress => WorkTotal > 0 ? Math.Clamp(WorkDone / WorkTotal, 0, 1) : 0;
    public bool Working => Current != null && (OperatorPresent || IsMachine);

    /// <summary>Bombes and Colossus run by themselves once a Wren has set them going.</summary>
    public bool IsMachine => Type is StationType.Bombe or StationType.Bombe4 or StationType.Colossus;

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

    public (int x, int y) UseTile => UseTileFor(Type, X, Y, Rot);

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
    public int? Pinned { get; set; }
    public double Energy { get; set; } = 1;
    public double Morale { get; set; } = Defs.MoraleBaseline;
    public bool Away { get; set; }
    public double HomeX { get; set; }
    public double HomeY { get; set; }
    public double LowMoraleDays { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public List<(int x, int y)> Path { get; set; } = new();
    public StaffTask? Task { get; set; }
    public bool Leaving { get; set; }
    public int Decrypts { get; set; }

    public (int x, int y) TilePos => ((int)Math.Floor(X), (int)Math.Floor(Y));
    public bool Idle => Task == null && !Leaving && !Away;
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
    /// <summary>Off sick until this time; they don't come back on watch before it.</summary>
    public double SickUntil { get; set; }
    /// <summary>Worked in a freezing hut today (reset each midnight).</summary>
    public bool Chilled { get; set; }
    public bool IsSick(double now) => SickUntil > now;
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
    public ContractState State { get; set; }
    public List<double> SpawnTimes { get; set; } = new();
}

public enum KeyStatus { None, Attempting, Broken }

public sealed class Stats
{
    public Dictionary<Net, int> Delivered { get; set; } = new();
    public Dictionary<Net, int> KeyBreaks { get; set; } = new();
    public Dictionary<Net, int> BombeBreaks { get; set; } = new();
    public int DilemmasResolved { get; set; }
    public HashSet<string> ScriptedDone { get; set; } = new();
    public int DeliveredTotal { get; set; }
    public int ContractsCompleted { get; set; }
    public int ContractsFailed { get; set; }
    public int Breakdowns { get; set; }

    public Stats Clone() => new()
    {
        Delivered = new(Delivered),
        KeyBreaks = new(KeyBreaks),
        BombeBreaks = new(BombeBreaks),
        DilemmasResolved = DilemmasResolved,
        ScriptedDone = new(ScriptedDone),
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
    public Service Service { get; set; }
    public int? StaffId { get; set; }
    public double Created { get; set; }
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
