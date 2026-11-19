using System;
using System.Collections.Generic;

namespace StationX.Sim;

public sealed partial class GameState
{
    /// <summary>
    /// A standing order: N decrypts a day for several days. Each 24 hours from acceptance the running total is checked
    /// against N per day so far. A milestone met pays its share at once; one missed costs a little reputation, but the
    /// total can still catch up.
    /// </summary>
    public Contract MakeStanding(ContractTemplate tpl) => ToStanding(MakeContract(tpl), tpl);

    /// <summary>Turns a freshly made request into a standing order (its own dice, so the game's other rolls don't shift).</summary>
    Contract ToStanding(Contract c, ContractTemplate tpl)
    {
        var r = new Random(Mix(Seed, c.Id, 0x57B));
        var net = Defs.NetOf(tpl.Net);
        int baseQty = r.Next(tpl.MinQty, tpl.MaxQty + 1);
        int days = Defs.StandingDays;
        int perDay = Math.Max(1, (int)Math.Round(baseQty * Defs.TrafficScale(ActIndex) * 1.25 / days));
        var fl = StandingFlavours[net.Issuer];
        c.PerDay = perDay;
        c.Quantity = perDay * days;
        c.Days = days + Defs.MilestoneGraceDays;
        // Steady work pays: each day met is worth a little more than the same messages in a one-off request.
        c.MilestonePay = 30 + (int)(perDay * net.PayPerMsg * 1.2);
        c.Reward = 120 + perDay * net.PayPerMsg;
        c.Rep = 3 + perDay / 2;
        c.Title = $"Standing order: {perDay} {net.Name} a day for {days} days";
        c.Flavour = fl[r.Next(fl.Length)];
        return c;
    }

    static readonly Dictionary<Issuer, string[]> StandingFlavours = new()
    {
        [Issuer.WarOffice] = new[] { "MI14 wants a daily picture of the order of battle while the offensive lasts.", "The War Office asks for a daily summary from the front." },
        [Issuer.AirMinistry] = new[] { "Air Intelligence wants the Luftwaffe's daily returns every morning this week.", "Fighter Command asks for tomorrow's raids, every day, all week." },
        [Issuer.Admiralty] = new[] { "The Submarine Tracking Room needs U-boat positions every day while the convoys are at sea.", "The OIC wants the daily naval picture until the convoy is in." },
        [Issuer.SIS] = new[] { "Broadway wants the Abwehr's traffic read daily while the agents are in play." },
    };

    /// <summary>When day k's running total is checked: k days after acceptance, plus grace for the time a day's traffic takes to get through.</summary>
    public static double MilestoneAt(Contract c, int k) => c.AcceptedAt + (k + Defs.MilestoneGraceDays) * Defs.MinutesPerDay;

    /// <summary>The deadline that sets a request's place in the queues: a standing order's next milestone.</summary>
    public static double WorkDue(Contract c) =>
        c.Standing && c.MilestonesChecked < c.TotalDays ? MilestoneAt(c, c.MilestonesChecked + 1) : c.Due;

    void CheckMilestones(Contract c)
    {
        while (c.MilestonesChecked < c.TotalDays && Now >= MilestoneAt(c, c.MilestonesChecked + 1))
        {
            c.MilestonesChecked++;
            int needed = c.PerDay * c.MilestonesChecked;
            if (c.Delivered >= needed)
            {
                c.MilestonesMet++;
                Stats.MilestonesMet++;
                int pay = (int)(c.MilestonePay * Defs.Policies[Policy].Reward);
                Funds += pay;
                AdjustRep(Defs.MilestoneRep * Defs.Policies[Policy].Rep, "Standing orders");
                LastServed[c.Issuer] = Now;
                Emit(EventKind.Good, $"{Defs.IssuerNames[c.Issuer]}: day {c.MilestonesChecked} of the standing order met ({c.Delivered}/{needed}). +£{pay}");
            }
            else
            {
                Stats.MilestonesMissed++;
                AdjustRep(-Defs.MilestoneMissedRep, "Standing orders");
                Emit(EventKind.Bad, $"{Defs.IssuerNames[c.Issuer]}: day {c.MilestonesChecked} of the standing order missed ({c.Delivered}/{needed}). Catch up tomorrow.");
            }
        }
    }

    /// <summary>Delivered in full: any milestones not yet reached count as met, and the bonus depends on them all.</summary>
    int FinishStanding(Contract c, int reward)
    {
        while (c.MilestonesChecked < c.TotalDays)
        {
            c.MilestonesChecked++;
            c.MilestonesMet++;
            Funds += (int)(c.MilestonePay * Defs.Policies[Policy].Reward);
        }
        return c.MilestonesMet == c.TotalDays ? reward : reward / 2;
    }
}
