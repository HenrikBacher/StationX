using System;
using System.Collections.Generic;

namespace StationX.Sim;

/// <summary>
/// Asking for more time. A request heading for a miss can be stretched once, within a day of its deadline: the ministry
/// gives half as long again (12 hours to 2 days) for a quarter off the fee and a little reputation, and whatever the decrypts
/// were for (a convoy, a raid) goes ahead without them. Each ministry grants it once a week. Not for MOST IMMEDIATE requests,
/// standing orders, or the war's own fixed dates (scripted requests).
/// </summary>
public sealed partial class GameState
{
    /// <summary>When each ministry last gave more time.</summary>
    public Dictionary<Issuer, double> LastExtension { get; set; } = new();

    public static double ExtendBy(Contract c) => Math.Clamp(c.Days * Defs.ExtendShare, Defs.ExtendMinDays, Defs.ExtendMaxDays) * Defs.MinutesPerDay;

    /// <summary>Why this request can't be given more time now, or null.</summary>
    public string? CantExtend(int contractId)
    {
        var c = Contracts.Find(x => x.Id == contractId);
        if (c is not { State: ContractState.Active }) return "Not in progress";
        if (c.Flash) return "MOST IMMEDIATE: the hour won't wait";
        if (c.Standing) return "A standing order runs day by day";
        if (c.Tag != null) return "Fixed by the war's own timetable";
        if (c.Extended) return "Already given more time once";
        if (c.Due - Now > Defs.ExtendWithinHours * 60) return $"Only in its last {Defs.ExtendWithinHours:0} hours";
        if (LastExtension.TryGetValue(c.Issuer, out var t) && Now - t < Defs.ExtendEveryDays * Defs.MinutesPerDay)
            return $"The {Defs.IssuerNames[c.Issuer]} gave more time this week already";
        return null;
    }

    public bool Extend(int contractId)
    {
        if (CantExtend(contractId) != null) return false;
        var c = Contracts.Find(x => x.Id == contractId)!;
        double by = ExtendBy(c);
        c.Due += by;
        c.Reward = (int)Math.Round(c.Reward * (1 - Defs.ExtendRewardCut));
        c.Extended = true;
        LastExtension[c.Issuer] = Now;
        Stats.Extensions++;
        AdjustRep(-Defs.ExtendRep, "More time asked");
        var op = OperationFor(c.Id);
        Emit(EventKind.Info, $"The {Defs.IssuerNames[c.Issuer]} gives {c.Title} another {(by >= Defs.MinutesPerDay ? $"{by / Defs.MinutesPerDay:0.#} day(s)" : $"{by / 60:0} hours")}" +
                             (op is { State: OpState.Pending } ? $", but {op.Name} can't wait for it." : "."));
        ResolveOperation(c, won: false);
        return true;
    }

    /// <summary>
    /// Heading for a miss: in its last hours with clearly more still to deliver than the time left would carry at an even pace.
    /// (The extend tip and the autoplayer use it.)
    /// </summary>
    public bool Behind(Contract c)
    {
        if (c.State != ContractState.Active || c.Standing) return false;
        double left = c.Due - Now, total = Math.Max(1, c.Days * Defs.MinutesPerDay);
        int rem = c.Quantity - c.Delivered;
        return rem > 0 && left <= Defs.ExtendWithinHours * 60 && rem > c.Quantity * left / total + Math.Max(1, c.Quantity * 0.15);
    }
}
