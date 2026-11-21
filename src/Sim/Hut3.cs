namespace StationX.Sim;

/// <summary>How hard Hut 3 checks its translations before they go out.</summary>
public enum Checking { Quick, Standard, Careful }

/// <summary>
/// Hut 3's accuracy against its speed. A decrypt was only as good as its translation: a German abbreviation guessed wrong,
/// a grid reference misread, and a convoy was routed the wrong way or a raid met over the wrong town. The duty officer could
/// pass a translation quickly, or have it read again. Quick: the translation desks are faster, but more go out wrong.
/// Careful: slower, and hardly any. Each mistake that goes out costs a little reputation (own dice per message, so the
/// setting never touches the game's main dice). A skilled linguist makes fewer.
/// </summary>
public sealed partial class GameState
{
    public Checking Checking { get; set; } = Checking.Standard;

    public void SetChecking(Checking c) => Checking = c;

    public double CheckingTime => Checking switch { Checking.Quick => Defs.CheckQuickTime, Checking.Careful => Defs.CheckCarefulTime, _ => 1 };

    /// <summary>The chance a translation goes out wrong, for a linguist of this skill.</summary>
    public double MistakeChance(int skill) =>
        (Checking switch { Checking.Quick => Defs.CheckQuickMistakes, Checking.Careful => Defs.CheckCarefulMistakes, _ => Defs.CheckStandardMistakes })
        * (1.3 - 0.15 * skill);

    /// <summary>A translation has been finished: perhaps it was wrong.</summary>
    void CheckTranslation(Message m, Staff? op)
    {
        var dice = Rng.Seeded(Mix(Seed, m.Id, 0x4A73));
        if (!dice.Chance(MistakeChance(op?.Skill ?? 2))) return;
        Stats.Mistranslations++;
        AdjustRep(-Defs.MistakeRep, "Hut 3's mistakes");
        Emit(EventKind.Bad, $"A {Defs.NetOf(m.Net).Name} translation went out wrong: {Mistakes[(int)(dice.NextDouble() * Mistakes.Length)]}");
    }

    static readonly string[] Mistakes =
    {
        "a grid square misread sent the escorts to the wrong side of the convoy.",
        "an abbreviation guessed wrong had the fighters waiting over the wrong town.",
        "a time read as 21:00 for 12:00 warned the Army a day late.",
        "a unit's number transposed had Intelligence chasing a division that wasn't there.",
        "\"not\" was dropped from a sentence. The commanders noticed before we did.",
    };

    public static string CheckingBlurb(Checking c) => c switch
    {
        Checking.Quick => $"The duty officer passes translations at a glance: translation desks are {(1 - Defs.CheckQuickTime) * 100:0}% faster, but about {Defs.CheckQuickMistakes * 100:0} in 100 go out wrong.",
        Checking.Careful => $"Every translation is read again: translation desks take {(Defs.CheckCarefulTime - 1) * 100:0}% longer, and hardly any go out wrong.",
        _ => $"A second look at anything doubtful: about {Defs.CheckStandardMistakes * 100:0.#} in 100 go out wrong.",
    };
}
