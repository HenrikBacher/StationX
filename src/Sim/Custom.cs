namespace StationX.Sim;

/// <summary>
/// A custom game's rules, laid over the chosen difficulty: the money at the start and each week, Whitehall's patience,
/// the time requests allow, German suspicion and how hard the machines wear. A custom game keeps no best score.
/// </summary>
public sealed class CustomRules
{
    public double Funds { get; set; } = 1;
    public double Grant { get; set; } = 1;
    /// <summary>Multiplies reputation lost.</summary>
    public double Patience { get; set; } = 1;
    /// <summary>Days added to (or taken from) every request.</summary>
    public double Deadlines { get; set; }
    public double Suspicion { get; set; } = 1;
    public double Wear { get; set; } = 1;

    public bool Plain => Funds == 1 && Grant == 1 && Patience == 1 && Deadlines == 0 && Suspicion == 1 && Wear == 1;

    public DifficultyDef Apply(DifficultyDef d) => d with
    {
        StartFunds = (int)(d.StartFunds * Funds),
        GrantMult = d.GrantMult * Grant,
        RepLossMult = d.RepLossMult * Patience,
        ExtraDays = d.ExtraDays + Deadlines,
        SuspicionMult = d.SuspicionMult * Suspicion,
        WearMult = d.WearMult * Wear,
    };
}

public sealed partial class GameState
{
    /// <summary>A custom game's rules over the difficulty, or null.</summary>
    public CustomRules? Custom { get => _custom; set { _custom = value; _rules = null; } }
    CustomRules? _custom;

    DifficultyDef? _rules;

    /// <summary>The difficulty's numbers, with a custom game's rules laid over them.</summary>
    public DifficultyDef Rules => _rules ??= Custom is { } c ? c.Apply(Defs.Difficulties[Difficulty]) : Defs.Difficulties[Difficulty];
}
