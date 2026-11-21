using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// Pay and conditions. The Park ran on people paid civil-service rates for work they couldn't talk about: Wrens on naval
/// ratings' pay beside civilian women earning more for the same job, temporary clerks graded below the permanent staff,
/// night watches with no allowance, billets an hour's bus ride out. Every few weeks from the Battle of Britain chapter on
/// (own dice), one group puts its case: raise their pay (a lasting cost), make a one-off allowance (the default), or refuse
/// (their morale falls, and with it their speed).
/// </summary>
public sealed partial class GameState
{
    public double LastGrievanceAt { get; set; } = -1e9;
    static readonly int GrievancesFrom = Campaign.IndexOf("Their Finest Hour");
    /// <summary>The ids of the staff a grievance is about, as "kind:id,id,…" (for the dilemma's action).</summary>
    public string GrievanceGroup { get; set; } = "";

    sealed record GrievanceKind(string Id, string Who, string Case, Func<GameState, Staff, bool> In, int Min);

    static readonly GrievanceKind[] Grievances =
    {
        new("wrens", "The Wrens",
            "are paid as naval ratings, less than the civilian women at the next machine doing the same work. Their senior Wren officer asks for parity.",
            (g, s) => s.Role == Role.Wren, 4),
        new("nights", "The night watch",
            "has worked midnight to eight without a penny more than the day watch. They ask for a night allowance, as the Post Office pays its own.",
            (g, s) => g.WatchesAvailable && s.Shift == Shift.Night, 4),
        new("billets", "The far-billeted",
            "spend two hours a day on the bus from billets out past Buckingham, and pay their landladies out of their own wages. They ask for a travel allowance.",
            (g, s) => s.FarBillet, 3),
        new("clerks", "The temporary clerks",
            "are graded below the Foreign Office's permanent staff for the same work, and nobody can tell them why. They ask to be regraded.",
            (g, s) => s.Role == Role.Clerk, 4),
        new("linguists", "The linguists",
            "point out that the services pay a language allowance, and the Park does not. They ask for one.",
            (g, s) => s.Role == Role.Linguist, 4),
    };

    void DailyGrievances()
    {
        if (Mode != GameMode.Campaign || ActIndex < GrievancesFrom) return;
        if (Dilemmas.Count > 0 || Now - LastDilemmaAt < Defs.MinutesPerDay || Now - LastGrievanceAt < Defs.GrievanceEveryDays * Defs.MinutesPerDay) return;
        var r = Rng.Seeded(Mix(Seed, Day, 0x6A1E));
        if (!r.Chance(Defs.GrievanceChance)) return;
        RaiseGrievance(r);
    }

    /// <summary>For screenshots and tests.</summary>
    public bool DebugGrievance(string? kind = null) => RaiseGrievance(Rng.Seeded(Mix(Seed, Day, 0x6A1F)), kind);

    List<Staff> GrievanceMembers(GrievanceKind k) =>
        Staff.Where(s => !s.Leaving && !s.American && s.Salary > 0 && k.In(this, s)).ToList();

    bool RaiseGrievance(Rng r, string? only = null)
    {
        var open = Grievances.Where(k => (only == null || k.Id == only) && GrievanceMembers(k).Count >= k.Min).ToList();
        if (open.Count == 0) return false;
        var k = r.Pick(open);
        var group = GrievanceMembers(k);
        LastGrievanceAt = Now;
        GrievanceGroup = $"{k.Id}:{string.Join(",", group.Select(s => s.Id))}";
        int rise = group.Sum(RiseFor);
        int allowance = group.Count * Defs.GrievanceAllowance;
        RaiseDilemma(new Dilemma
        {
            Service = Service.Security, Title = "Pay and conditions", Tag = "grievance",
            Text = $"{k.Who} ({group.Count}) {k.Case}",
            Context = $"A rise would cost £{rise} a week for as long as they stay. A one-off allowance of £{Defs.GrievanceAllowance} each " +
                      $"(£{allowance}) would quiet things for now. Refusing saves the money, but unhappy people work more slowly.",
            Options =
            {
                new() { Label = $"Raise their pay (£{rise} a week)", Action = "grievance-raise",
                    Outcome = $"{k.Who} get their rise. The Treasury will grumble at the next estimates; the huts are delighted." },
                new() { Label = $"A one-off allowance (£{allowance})", Funds = -allowance, Action = "grievance-allow",
                    Outcome = $"{k.Who} get an allowance and a promise to look at it again. It will do, for now." },
                new() { Label = "Refuse", Action = "grievance-refuse",
                    Outcome = $"Commander Travis says there is a war on. {k.Who} go back to work, slowly." },
            },
        });
        return true;
    }

    static int RiseFor(Staff s) => Math.Max(Defs.GrievanceMinRise, (int)Math.Round(s.Salary * Defs.GrievanceRiseShare));

    void GrievanceAction(string action)
    {
        int colon = GrievanceGroup.IndexOf(':');
        if (colon < 0) return;
        var ids = GrievanceGroup[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse);
        foreach (var id in ids)
        {
            if (StaffById(id) is not { } s) continue;
            switch (action)
            {
                case "grievance-raise":
                    s.Salary += RiseFor(s);
                    s.Morale = Math.Clamp(s.Morale + Defs.GrievanceRaiseMorale, 0, 1);
                    break;
                case "grievance-allow":
                    s.Morale = Math.Clamp(s.Morale + Defs.GrievanceAllowMorale, 0, 1);
                    break;
                case "grievance-refuse":
                    s.Morale = Math.Clamp(s.Morale - Defs.GrievanceRefuseMorale, 0, 1);
                    break;
            }
        }
        GrievanceGroup = "";
    }
}
