using System;

namespace StationX.Sim;

/// <summary>
/// Reporting to the Chiefs of Staff: once the new methods are paid for, breakthroughs have nowhere to go. Writing up
/// what the Park has learned for the Chiefs turns them into standing in Whitehall: each report costs more than the
/// last (the Chiefs expect more each time), and there's one a fortnight at most. Only once Whitehall judges the Park
/// by its results (the quota).
/// </summary>
public sealed partial class GameState
{
    public int ChiefsReports { get; set; }
    public double LastReportAt { get; set; } = -1e9;

    public int ReportCost => Defs.ReportBaseCost + Defs.ReportCostStep * ChiefsReports;
    public double ReportReadyIn => LastReportAt + Defs.ReportEveryDays * Defs.MinutesPerDay - Now;

    /// <summary>Why a report can't go to the Chiefs now, or null if it can.</summary>
    public string? CantReport()
    {
        if (!QuotaActive) return "The Chiefs take no interest until Whitehall judges the Park by its results.";
        if (ReportReadyIn > 0) return $"The Chiefs had one lately: the next in {Math.Ceiling(ReportReadyIn / Defs.MinutesPerDay):0} days.";
        if (Points < ReportCost) return $"Needs {ReportCost} breakthroughs.";
        return null;
    }

    public bool ReportToChiefs()
    {
        if (CantReport() != null) return false;
        Points -= ReportCost;
        ChiefsReports++;
        LastReportAt = Now;
        AdjustRep(Defs.ReportRep, "Reports to the Chiefs");
        Emit(EventKind.Good, $"A report on the Park's methods and results goes to the Chiefs of Staff. Whitehall is impressed (+{Defs.ReportRep:0} reputation); the next will have to show more.");
        return true;
    }

    /// <summary>Nothing left to research that's on offer now.</summary>
    public bool ResearchDone()
    {
        foreach (var r in ResearchAvailable) if (!Researched.Contains(r)) return false;
        return true;
    }
}
