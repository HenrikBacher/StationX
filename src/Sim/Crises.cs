using System;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// Crises that test the layout and the staff: a power cut that stops the machines, and a surge of MOST IMMEDIATE
/// requests when a battle opens. Each one asks for a decision: pay for a generator or wait; cancel leave and work
/// overtime, or carry on.
/// </summary>
public sealed partial class GameState
{
    public double PowerCutAt { get; set; } = -1;
    public double PowerCutUntil { get; set; }
    /// <summary>A hired generator keeps the Bombes running through the current power cut.</summary>
    public bool Generator { get; set; }
    public double OvertimeUntil { get; set; }
    public double SurgeAt { get; set; } = -1;
    public int SurgeLeft { get; set; }
    public double NextSurgeFlashAt { get; set; }

    public bool PowerCut => Now < PowerCutUntil;
    public bool Overtime => Now < OvertimeUntil;
    public double OvertimeFactor => Overtime ? Defs.OvertimeRate : 1;

    /// <summary>True for a machine stopped by the power cut. The generator runs the Bombes only.</summary>
    public bool Unpowered(Station st) =>
        PowerCut && st.Wears && !(Generator && st.Type is StationType.Bombe or StationType.Bombe4);

    /// <summary>A stable hash of a few ints (HashCode.Combine is randomised per process, so it isn't reproducible).</summary>
    public static int Mix(params int[] xs)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var x in xs) { h ^= (uint)x; h *= 16777619; h ^= h >> 15; }
            return (int)(h & 0x7fffffff);
        }
    }

    void PlanCrisis()
    {
        if (ActIndex < Defs.CrisesFromAct || PowerCutAt >= 0 || SurgeAt >= 0 || PowerCut || SurgeLeft > 0) return;
        // A separate dice for crises, so they don't shift every other random roll in the game.
        var r = new Random(Mix(Seed, Day, 0xC415));
        if (r.NextDouble() >= Defs.CrisisChance * Math.Min(1.0, Defs.Difficulties[Difficulty].WearMult)) return;
        double at = Day * Defs.MinutesPerDay + (7 + r.NextDouble() * 14) * 60;
        if (r.NextDouble() < 0.5 && Stations.Any(s => s.Wears)) PowerCutAt = at;
        else SurgeAt = at;
    }

    void TickCrises()
    {
        if (PowerCutAt >= 0 && Now >= PowerCutAt)
        {
            PowerCutAt = -1;
            PowerCutUntil = Now + Rng.Range(3.0, 7.0) * 60;
            Generator = false;
            int hours = (int)Math.Round((PowerCutUntil - Now) / 60);
            Emit(EventKind.Bad, $"Power cut! The supply to the Park has failed. The Bombes and Typexes have stopped; the Post Office engineers say about {hours} hours.");
            RaiseDilemma(new Dilemma
            {
                Service = Service.Heer, Title = "Power cut",
                Text = $"The Park's supply has failed and the engineers say about {hours} hours. Every machine has stopped. " +
                       "A mobile generator can be had from the RAF at Chicksands, but only enough for the Bombe rooms.",
                Options =
                {
                    new() { Label = "Hire the generator for the Bombes", Funds = -Defs.GeneratorCost, Action = "generator",
                        Outcome = "The generator lorry backs up to the Bombe hut and the drums turn again. The Typexes wait for the mains." },
                    new() { Label = "Wait for the engineers",
                        Outcome = "Candles in the huts. The machines are silent until the power comes back." },
                },
            });
        }
        if (PowerCutUntil > 0 && !PowerCut)
        {
            PowerCutUntil = 0;
            Generator = false;
            Emit(EventKind.Good, "The power is back. The machines start up again.");
        }

        if (SurgeAt >= 0 && Now >= SurgeAt)
        {
            SurgeAt = -1;
            SurgeLeft = Rng.Range(2, 4);
            NextSurgeFlashAt = Now;
            var (battle, who) = SurgeBattle();
            Emit(EventKind.Bad, $"{battle} has begun. The {who} will be asking for everything we have.");
            RaiseDilemma(new Dilemma
            {
                Service = Service.Heer, Title = battle,
                Text = $"{battle} has begun, and the {who} will want everything we can read for the next two days. " +
                       "Cancelling leave and working double shifts would get more done, at a cost in overtime and in tiredness.",
                Options =
                {
                    new() { Label = "Cancel leave: everyone on overtime for two days", Funds = -Defs.OvertimeCost, Morale = Defs.OvertimeMorale, Action = "overtime",
                        Outcome = "Leave is cancelled. The huts work flat out; people grumble, and tire faster." },
                    new() { Label = "Carry on as normal",
                        Outcome = "The watches carry on as rostered. We'll do what we can." },
                },
            });
        }
        if (SurgeLeft > 0 && Now >= NextSurgeFlashAt && !Contracts.Any(c => c.Flash && c.State == ContractState.Offered))
        {
            if (PostFlash()) SurgeLeft--;
            else SurgeLeft = 0;
            NextSurgeFlashAt = Now + Rng.Range(5.0, 10.0) * 60;
        }
    }

    void CrisisAction(string action, Dilemma d)
    {
        var who = d.StaffId is int sid ? StaffById(sid) : null;
        switch (action)
        {
            case "rest" when who != null:
                SendOff(who, Defs.RestLeaveDays, "leave");
                who.Strain = 0.2;
                break;
            case "generator" when PowerCut:
                Generator = true;
                break;
            case "overtime":
                OvertimeUntil = Now + Defs.OvertimeDays * Defs.MinutesPerDay;
                break;
        }
    }

    /// <summary>The battle behind a surge, by date.</summary>
    (string battle, string who) SurgeBattle()
    {
        var d = Date;
        if (d < new DateOnly(1940, 6, 5)) return ("The battle for France", "War Office");
        if (d < new DateOnly(1940, 11, 1)) return ("A great air battle", "Air Ministry");
        if (d < new DateOnly(1941, 5, 17)) return (Rng.Chance(0.5) ? "A heavy night of the Blitz" : "A battle in the Western Desert", Rng.Chance(0.5) ? "Air Ministry" : "War Office");
        if (d < new DateOnly(1941, 12, 31)) return (Rng.Chance(0.5) ? "A convoy battle in the Atlantic" : "Operation CRUSADER", Rng.Chance(0.5) ? "Admiralty" : "War Office");
        if (d < new DateOnly(1942, 11, 8)) return (Rng.Chance(0.5) ? "A wolf-pack attack on a convoy" : "A battle in the desert", Rng.Chance(0.5) ? "Admiralty" : "War Office");
        return ("A great battle", "Chiefs of Staff");
    }
}
