using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

public enum OpOutcome { Success, Partial, Failure }

/// <summary>
/// A great operation of the war that a whole chapter builds up to: Barbarossa, D-Day, Market Garden, the Ardennes.
/// D-Day comes a fixed number of days into the act whatever the Park does. How it goes depends on readiness: the
/// decrypts delivered against the act's "before D-Day" objectives, how unsuspecting the watched German services still
/// are, and one decision a few days before. A failed operation costs reputation; a failed invasion ends the campaign.
/// </summary>
public sealed record GreatOpDef(
    string Id, string Name, int PrepDays, Service[] Watch, bool Fatal,
    string Success, string Partial, string Failure,
    int RepSuccess, int RepPartial, int RepFailure,
    Func<GameState, Dilemma> Decision, int DecisionDaysBefore = 3);

public sealed partial class GameState
{
    /// <summary>The operation's decision has been put to the player.</summary>
    public bool OpDecisionRaised { get; set; }
    /// <summary>What the operation's decision added to (or took from) readiness.</summary>
    public double OpBonus { get; set; }
    /// <summary>How each great operation went.</summary>
    public Dictionary<string, OpOutcome> OpOutcomes { get; set; } = new();

    public GreatOpDef? CurrentOp => Act.Op is { } id && !OpOutcomes.ContainsKey(id) ? GreatOps.ById(id) : null;

    /// <summary>When the operation begins: dawn, the act's prep days after it opened.</summary>
    public double DDayAt => (ActStartDay + (GreatOps.ById(Act.Op!)?.PrepDays ?? 0)) * Defs.MinutesPerDay + 6 * 60;

    public bool LockedDown(Service sv) => LockdownUntil.TryGetValue(sv, out var until) && Day <= until;

    /// <summary>The intelligence part of readiness: how far the act's before-D-Day objectives have got.</summary>
    public double OpIntel()
    {
        var soft = Act.Goals.Where(g => g.Soft).ToList();
        return soft.Count == 0 ? 1 : soft.Average(g => (double)g.Progress(this) / g.Target);
    }

    /// <summary>The secrecy part: the least unsuspecting of the watched services (a lockdown is the worst).</summary>
    public double OpSecrecy(GreatOpDef op) =>
        op.Watch.Length == 0 ? 1 : op.Watch.Min(sv => LockedDown(sv) ? 0 : 1 - Math.Max(0, SuspicionOf(sv) - Defs.OpSuspicionFree) / (Defs.SuspicionLimit - Defs.OpSuspicionFree));

    /// <summary>How ready the operation is, 0 to 1.</summary>
    public double Readiness()
    {
        if (CurrentOp is not { } op) return 0;
        return Math.Clamp(Defs.OpIntelWeight * OpIntel() + Defs.OpSecrecyWeight * OpSecrecy(op) + Defs.OpBase + OpBonus + OshimaBonus, 0, 1);
    }

    public static OpOutcome OutcomeFor(double readiness) =>
        readiness >= Defs.OpSuccessAt ? OpOutcome.Success : readiness >= Defs.OpPartialAt ? OpOutcome.Partial : OpOutcome.Failure;

    void TickGreatOp()
    {
        if (CurrentOp is not { } op || Status != GameStatus.Playing) return;
        if (!OpDecisionRaised && Now >= DDayAt - op.DecisionDaysBefore * Defs.MinutesPerDay)
        {
            OpDecisionRaised = true;
            var d = op.Decision(this);
            d.Tag = "op";
            RaiseDilemma(d);
        }
        if (Now < DDayAt) return;

        // A decision left unanswered on the day counts for nothing.
        Dilemmas.RemoveAll(d => d.Tag == "op");
        double r = Readiness();
        var outcome = OutcomeFor(r);
        OpOutcomes[op.Id] = outcome;
        Stats.ScriptedDone.Add("op:" + op.Id);
        string head = $"{op.Name} (readiness {r * 100:0}%): ";
        switch (outcome)
        {
            case OpOutcome.Success:
                AdjustRep(op.RepSuccess, "Great operations");
                ChangeMorale(0.06);
                Points += Defs.OpSuccessPoints;
                Emit(EventKind.Good, head + op.Success);
                break;
            case OpOutcome.Partial:
                AdjustRep(op.RepPartial, "Great operations");
                Emit(EventKind.Info, head + op.Partial);
                break;
            default:
                ChangeMorale(-0.08);
                if (op.Fatal && Mode == GameMode.Campaign)
                {
                    Status = GameStatus.Lost;
                    EndReason = op.Failure;
                    Emit(EventKind.Lost, op.Failure);
                    return;
                }
                AdjustRep(op.RepFailure, "Great operations");
                Emit(EventKind.Bad, head + op.Failure);
                break;
        }
    }

    void StartGreatOp()
    {
        OpDecisionRaised = false;
        OpBonus = 0;
    }
}

public static class GreatOps
{
    public static GreatOpDef? ById(string id) => All.FirstOrDefault(o => o.Id == id);

    static DilemmaOption O(string label, string outcome, int bonus, double rep = 0, double suspicion = 0, int points = 0) =>
        new() { Label = label, Outcome = outcome, Action = $"op{bonus:+0;-0}", Rep = rep, Suspicion = suspicion, Points = points };

    public static readonly GreatOpDef[] All =
    {
        new("barbarossa", "Barbarossa", 8, new[] { Service.Luftwaffe, Service.Security }, false,
            "When the panzers crossed on 22 June, Moscow had been warned in time to disperse some of its aircraft, and Whitehall knew the German " +
            "order of battle in detail. That night the Prime Minister broadcast that any man or state who fights against Nazidom will have our aid.",
            "Stalin ignored the warnings, as he ignored his own spies'. The Soviet air force lost over a thousand aircraft on the ground on the first day. " +
            "Whitehall at least knew what was happening.",
            "Whitehall heard of the invasion from the BBC. The Park's picture of the eastern front was patchy and late, and the Chiefs of Staff want to know why.",
            8, -2, -10,
            g => new Dilemma
            {
                Service = Service.Luftwaffe, Title = "A last warning to Moscow",
                Text = "The Luftwaffe's move east is complete, and the attack can only be days away. Moscow has had one warning already, without a source.",
                Context = "Stalin distrusts anything British and suspects us of trying to drag him into the war. A full order of battle from the British " +
                          "military mission might convince his generals, and might reach Berlin through a Soviet cipher. A warning through the ambassador " +
                          "keeps the source, and may be ignored like the last. What Moscow knows on the day decides how much we are thanked for afterwards.",
                Options =
                {
                    O("Give the Soviet military mission the full order of battle", "The mission is given everything. Its generals are shaken; Stalin calls it a provocation, but some airfields are dispersed.", 15, rep: 1, suspicion: 12),
                    O("Warn again through the ambassador, without a source", "Sir Stafford Cripps delivers a second warning. Moscow acknowledges it coolly.", 6),
                    O("Say nothing more: Stalin won't listen", "We keep our counsel and our source. The Luftwaffe's signals carry on unchanged.", 0, suspicion: -4),
                },
            }),
        new("overlord", "D-Day", 8, new[] { Service.Heer, Service.Security, Service.Kriegsmarine }, true,
            "On 6 June the landings succeeded. The panzers waited at Calais for a second invasion that never came, held there for weeks by FORTITUDE " +
            "and by GARBO's reports, and the decrypts showed it day by day.",
            "The beachheads held, at a heavy price. Rommel's reserves came sooner than hoped and Omaha was nearly lost. Whitehall is grateful, but not delighted.",
            "The Germans were waiting. With the panzers moved to Normandy in time, the landings were thrown back into the sea. There will be no second " +
            "front this year, and the Prime Minister has asked what Bletchley Park was for.",
            12, -4, -30,
            g => new Dilemma
            {
                Service = Service.Security, Title = "GARBO's message",
                Text = "GARBO, our best double agent, is the Abwehr's most trusted source in Britain. MI5 wants to spend some of that trust on the invasion.",
                Context = "The decrypts show the Abwehr passing GARBO's reports straight to Hitler's headquarters. MI5's plan: GARBO warns of Normandy " +
                          "hours before the landings, too late to matter but proving his worth, and days later reports that Normandy is a feint before " +
                          "the real blow at Calais. If the Abwehr doubts him, FORTITUDE collapses with him. Routine reports keep him safe but spend nothing.",
                Options =
                {
                    O("Let GARBO warn of Normandy too late, then call it a feint", "GARBO's warning arrives as planned, too late. Three days later Hitler reads that Normandy is a diversion, and holds the panzers at Calais.", 15, suspicion: 6),
                    O("Keep the double agents to routine reports", "The agents report troop movements in Kent, as they have for months. FORTITUDE holds, unspectacularly.", 6),
                    O("Stand the agents down until it's over", "The agents fall silent. The Abwehr frets about them, and about what it can't see.", -5, suspicion: -4),
                },
            }),
        new("marketgarden", "Market Garden", 8, new[] { Service.Luftwaffe, Service.Heer }, false,
            "Warned of the panzers, the planners dropped the 1st Airborne Division on the bridge itself, with anti-tank guns. Arnhem bridge held " +
            "until XXX Corps came up the road, and the Allies are across the Rhine.",
            "Arnhem was a bridge too far. The warnings about the SS panzers were heard and dismissed. Of ten thousand men dropped at Arnhem, " +
            "fewer than a quarter came back across the river.",
            "Nobody knew the panzers were there. The drop at Arnhem was cut to pieces in two days, the road north was blocked at Nijmegen, " +
            "and the whole operation collapsed.",
            8, -3, -10,
            g => new Dilemma
            {
                Service = Service.Heer, Title = "Panzers at Arnhem",
                Text = "Decrypts place II SS Panzer Corps, two divisions refitting after Normandy, in the woods around Arnhem: exactly where the 1st Airborne will drop.",
                Context = "The divisions are worn down but not finished, and they have tanks. Montgomery's headquarters is sure the Germans in Holland are beaten " +
                          "and does not want to hear otherwise. Taking the decrypts there in person will make enemies; passing them through the usual " +
                          "channels is what happened in 1944, and they were dismissed. Holding them back for a second source risks losing the time to act.",
                Options =
                {
                    O("Take the decrypts to Montgomery's headquarters in person", "The staff are furious at the interference, but the plan is changed: anti-tank guns go in with the first lift.", 20, rep: -2),
                    O("Pass them through the usual channels", "The report goes up the chain, and is noted.", 5),
                    O("Hold them until a second source confirms it", "Hut 3 waits for confirmation. It comes on the day of the drop.", -5, points: 3),
                },
            }),
        new("ardennes", "The Ardennes", 8, new[] { Service.Luftwaffe, Service.Heer }, false,
            "When the panzers came out of the fog on 16 December, the Americans had been warned. Bastogne and the Elsenborn ridge were held in " +
            "strength, and the offensive ran out of fuel within a week.",
            "The attack came as a surprise. The Americans were caught thin on the ground but held at Bastogne and St Vith long enough. It cost them dearly.",
            "The panzers broke through and reached the Meuse. Antwerp was threatened for weeks before the line was restored, and the Chiefs of Staff " +
            "want to know why the Park had nothing to say.",
            8, -4, -12,
            g => new Dilemma
            {
                Service = Service.Luftwaffe, Title = "The quiet front",
                Text = "The Ardennes have gone quiet on the wireless. Luftwaffe decrypts show fighter units moving west in numbers and fuel being stockpiled behind the line.",
                Context = "The army commanders read the quiet as exhaustion. Hut 3 reads it as a secret being kept: the German army is using landlines, but " +
                          "the Luftwaffe still talks. Saying plainly that an offensive is coming stakes the Park's name on a guess; reporting the facts and " +
                          "letting the commanders judge is what happened in 1944.",
                Options =
                {
                    O("Tell SHAEF plainly: an offensive is coming in the Ardennes", "SHAEF is told, in so many words. Some think the Park has lost its nerve; others move reserves closer.", 20, rep: -1, suspicion: 4),
                    O("Report the build-up and let the commanders judge", "The facts go to SHAEF. The commanders are sure the Germans have nothing left.", 5),
                    O("Wait until there's more to go on", "Hut 3 waits for the army to start talking. It doesn't.", -5, suspicion: -3),
                },
            }),
    };
}
