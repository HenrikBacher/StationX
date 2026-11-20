using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// Visits from Whitehall. From the Battle of Britain on, every week or two a senior visitor is announced for the next
/// day. When they come, they walk round the huts and judge what they see: in-trays piled up, machines standing broken,
/// glum faces, requests about to be missed, an overdrawn account. A park running smoothly earns reputation; a shambles
/// costs it, and part of the next week's grant. The player gets a day's warning to tidy up.
/// </summary>
public sealed partial class GameState
{
    /// <summary>When the announced visit happens (game minutes), or -1 if none is due.</summary>
    public double InspectionAt { get; set; } = -1;
    public string InspectionBy { get; set; } = "";
    public int NextInspectionDay { get; set; }
    /// <summary>Share of the next weekly grant withheld because a visit went badly (0 if none).</summary>
    public double GrantPenalty { get; set; }

    static readonly string[] Visitors =
    {
        "\"C\", the head of the Secret Intelligence Service",
        "the Director of Naval Intelligence",
        "an Air Commodore from the Air Ministry",
        "a brigadier from the War Office",
        "the Foreign Office's man for GC&CS",
    };

    public bool InspectionDue => InspectionAt >= 0;
    /// <summary>The announced visit is the Prime Minister's (once, in the Barbarossa chapter).</summary>
    public bool PmVisit { get; set; }
    /// <summary>When the last visitor went round (game minutes): their car stays a while after.</summary>
    public double VisitedAt { get; set; } = -1e9;
    /// <summary>A visitor's car stands at the Mansion from just before the visit until two hours after.</summary>
    /// <summary>For screenshots: a visitor arriving now.</summary>
    public void DebugVisitor() { InspectionBy = Visitors[0]; InspectionAt = Now + 10; }
    public bool VisitorHere => (InspectionDue && Now >= InspectionAt - 30) || Now - VisitedAt < 120;

    void PlanInspection()
    {
        // Churchill came to see the Park for himself in 1941: here, a few days into Barbarossa, before the invasion.
        if (Mode == GameMode.Campaign && ActIndex == Campaign.IndexOf("Barbarossa") && Day >= ActStartDay + Defs.PmVisitAfterDays &&
            Stats.ScriptedDone.Add("pm1941"))
        {
            PmVisit = true;
            InspectionBy = "the Prime Minister";
            InspectionAt = (Day + 1) * Defs.MinutesPerDay + 11 * 60;
            NextInspectionDay = Math.Max(NextInspectionDay, Day + Defs.InspectionEveryDays);
            Emit(EventKind.Info, "Tomorrow at 11:00 the Prime Minister comes to see the Park for himself. Mr Churchill will go round the huts with " +
                                 "Commander Denniston and speak to the staff. Clear the in-trays, mend the machines and have everyone at work.");
            return;
        }
        if (ActIndex < Defs.InspectionsFromAct || InspectionDue || Day < NextInspectionDay) return;
        // Separate dice, so visits don't shift every other random roll in the game.
        var r = new Random(Mix(Seed, Day, 0x1A5B));
        if (NextInspectionDay == 0)
        {
            // The first one comes a few days into the chapter that starts them.
            NextInspectionDay = Day + 2 + r.Next(3);
            return;
        }
        InspectionBy = Visitors[r.Next(Visitors.Length)];
        InspectionAt = (Day + 1) * Defs.MinutesPerDay + (10 + r.Next(6)) * 60;
        NextInspectionDay = Day + Defs.InspectionEveryDays + r.Next(5);
        Emit(EventKind.Info, $"Tomorrow at {(int)(InspectionAt % Defs.MinutesPerDay / 60):00}:00, {InspectionBy} visits the Park. " +
                             "Clear the in-trays, mend the machines and keep everyone cheerful.");
    }

    /// <summary>What a visitor would make of the Park right now: a score and the things they noticed.</summary>
    public (int score, List<string> notes) InspectionVerdict()
    {
        int score = 0;
        var notes = new List<string>();
        int piled = Stations.Count(s => s.Def.InputCap > 0 && s.Input.Count >= s.Def.InputCap && s.Def.Operator != null);
        if (piled == 0) { score++; notes.Add("not an in-tray overflowing"); }
        else if (piled >= 3) { score -= 2; notes.Add($"in-trays piled high on {piled} desks"); }
        else { score--; notes.Add("an in-tray or two overflowing"); }

        int broken = Stations.Count(s => s.IsDown(Now) && !s.Servicing);
        if (broken > 0) { score -= Math.Min(2, broken); notes.Add(broken == 1 ? "a machine standing broken" : $"{broken} machines standing broken"); }

        var here = Staff.Where(s => !s.Away && s.SickUntil <= Now).ToList();
        double morale = here.Count > 0 ? here.Average(s => s.Morale) : Defs.MoraleBaseline;
        if (morale >= 0.65) { score++; notes.Add("cheerful faces"); }
        else if (morale < 0.4) { score--; notes.Add("glum faces everywhere"); }

        // The Treasury's man counts heads: a Park where most of the watch stands about is paying for people it doesn't need.
        double idle = IdleShare();
        if (idle >= Defs.InspectionIdleShare) { score--; notes.Add($"{idle:P0} of the watch standing about with nothing to do"); }
        else if (idle <= Defs.InspectionBusyShare) { score++; notes.Add("everyone hard at work"); }

        if (Contracts.Any(c => c.State == ContractState.Active && c.Due - Now < 6 * 60 && c.Quantity - c.Delivered >= 2))
        { score--; notes.Add("a request about to be missed"); }
        if (Funds < 0) { score--; notes.Add("the account overdrawn"); }
        return (score, notes);
    }

    void TickInspection()
    {
        if (!InspectionDue || Now < InspectionAt) return;
        InspectionAt = -1;
        VisitedAt = Now;
        var (score, notes) = InspectionVerdict();
        string saw = string.Join(", ", notes);
        if (PmVisit) { PmVisit = false; PrimeMinisterLeaves(score, saw); return; }
        if (score >= 1)
        {
            AdjustRep(Defs.InspectionGoodRep, "Visits");
            Stats.InspectionsPassed++;
            Emit(EventKind.Good, $"{Cap(InspectionBy)} went round the huts and left impressed: {saw}.");
        }
        else if (score <= -2)
        {
            AdjustRep(-Defs.InspectionBadRep, "Visits");
            Stats.InspectionsFailed++;
            GrantPenalty = Defs.InspectionGrantCut;
            Emit(EventKind.Bad, $"{Cap(InspectionBy)} was not impressed: {saw}. Whitehall will hear of it, and the Treasury " +
                                $"will hold back {Defs.InspectionGrantCut:P0} of next week's grant.");
        }
        else
            Emit(EventKind.Info, $"{Cap(InspectionBy)} looked round and said little: {saw}.");
    }

    /// <summary>
    /// He spoke to the staff from a tree stump on the lawn, whatever he made of the huts: everyone's spirits rise. What he
    /// saw decides what he tells Whitehall; a poor showing costs reputation but no grant (he paid for none of it).
    /// </summary>
    void PrimeMinisterLeaves(int score, string saw)
    {
        ChangeMorale(Defs.PmMorale);
        const string speech = "Standing on a tree stump outside the Mansion he told the staff they were the geese that laid the golden eggs and never cackled.";
        if (score >= 1)
        {
            AdjustRep(Defs.PmGoodRep, "The Prime Minister");
            Stats.InspectionsPassed++;
            Emit(EventKind.Good, $"Mr Churchill went round the huts and saw {saw}. {speech} He leaves delighted, and Whitehall knows it.");
        }
        else if (score <= -2)
        {
            AdjustRep(-Defs.PmBadRep, "The Prime Minister");
            Stats.InspectionsFailed++;
            Emit(EventKind.Bad, $"Mr Churchill went round the huts and saw {saw}. {speech} But on the way to his car he asked Denniston some sharp questions, and Whitehall has heard them.");
        }
        else
        {
            AdjustRep(Defs.PmFairRep, "The Prime Minister");
            Emit(EventKind.Info, $"Mr Churchill went round the huts and saw {saw}. {speech}");
        }
    }

    /// <summary>
    /// The share of the people on watch now (not on a break, sick or still on the train) with nothing to do: no task,
    /// or sitting about at a table waiting for work. What a visitor walking round sees.
    /// </summary>
    public double IdleShare()
    {
        int on = 0, idle = 0;
        foreach (var s in Staff)
        {
            if (s.Away || s.Arriving(Now) || s.IsSick(Now) || s.HeadOf != null) continue;
            if (s.Task is { Kind: TaskKind.Rest, Loafing: false }) continue;
            // A fitter waiting for a breakdown is on call, not idle.
            if (s.Role == Role.Fitter && s.Task is null or { Kind: TaskKind.Rest }) continue;
            on++;
            if (s.Task == null || s.Task is { Kind: TaskKind.Rest, Loafing: true }) idle++;
        }
        return on == 0 ? 0 : (double)idle / on;
    }

    static string Cap(string s) => s.Length == 0 ? s : s[0] == '"' ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
