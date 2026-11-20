using System;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// The Daily Telegraph crossword. In January 1942 the paper ran a competition at its Fleet Street offices; those who
/// finished the puzzle inside twelve minutes later had a letter asking whether they'd take up "a particular type of
/// work as a contribution to the war effort". Here it can be run again every few weeks: for a fee it sends skilled
/// cryptanalysts, crossword fiends all, who stay on the candidates list until hired or the next competition.
/// </summary>
public sealed partial class GameState
{
    /// <summary>When the last competition was run (game minutes), or long ago.</summary>
    public double CrosswordAt { get; set; } = -1e9;
    /// <summary>The day the winners' answers come back, or -1 when no competition is under way.</summary>
    public int CrosswordDueDay { get; set; } = -1;

    public bool CrosswordOpen => Date >= Defs.CrosswordFrom;
    public bool CrosswordPending => CrosswordDueDay >= 0;
    public double CrosswordReadyIn => CrosswordAt + Defs.CrosswordEveryDays * Defs.MinutesPerDay - Now;

    /// <summary>Why a competition can't be run now, or null if it can.</summary>
    public string? CantHoldCrossword()
    {
        if (!CrosswordOpen) return "The Telegraph's competition isn't until January 1942.";
        if (CrosswordPending) return "The competition is under way: the winners' letters come in a few days.";
        if (CrosswordReadyIn > 0) return $"The paper won't run another for {Math.Ceiling(CrosswordReadyIn / Defs.MinutesPerDay):0} days.";
        if (Funds < Defs.CrosswordCost) return $"Needs £{Defs.CrosswordCost}.";
        return null;
    }

    public bool HoldCrossword()
    {
        if (CantHoldCrossword() != null) return false;
        Funds -= Defs.CrosswordCost;
        CrosswordAt = Now;
        CrosswordDueDay = Day + Defs.CrosswordDays;
        Emit(EventKind.Info, $"The Daily Telegraph runs a crossword competition (£{Defs.CrosswordCost}): anyone who finishes it inside twelve minutes " +
                             $"at its Fleet Street offices will have a discreet letter from the War Office. The names come in {Defs.CrosswordDays} days.");
        return true;
    }

    /// <summary>At midnight: the winners of a competition join the candidates, on their own dice.</summary>
    void DailyCrossword()
    {
        if (!CrosswordPending || Day < CrosswordDueDay) return;
        CrosswordDueDay = -1;
        Candidates.RemoveAll(c => c.Crossword);
        var shared = Rng;
        Rng = Rng.Seeded(Mix(Seed, Day, 0xC205));
        for (int i = 0; i < Defs.CrosswordRecruits; i++)
        {
            var p = NewPerson(Role.Cryptanalyst, Rng.Chance(0.4) ? 3 : 2);
            p.Traits = new() { Trait.CrosswordFiend };
            p.Crossword = true;
            p.Bio = Rng.Pick(SolverBios);
            Candidates.Add(p);
        }
        Rng = shared;
        Emit(EventKind.Good, $"{Defs.CrosswordRecruits} of the Telegraph's crossword winners have answered the War Office's letter: they're on the candidates list (Staff, H) until the next competition.");
    }

    static readonly string[] SolverBios =
    {
        "Finished the Telegraph puzzle in under eight minutes. A clerk at a Dagenham insurance office.",
        "Solved it in eleven minutes. Teaches classics at a grammar school in Leeds.",
        "Second fastest at the Fleet Street test. Plays chess by post with half of Scotland.",
        "Finished with a minute to spare. Was a bank cashier in Bristol until last week.",
        "Did the puzzle in ten minutes and asked for another. Grew up in a Norfolk vicarage.",
        "Under twelve minutes, in pencil, without a crossing out. Works on the stock exchange.",
        "Beat the clock at Fleet Street. Sets the parish magazine's crossword.",
    };
}
