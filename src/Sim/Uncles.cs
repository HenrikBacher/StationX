namespace StationX.Sim;

/// <summary>
/// The Wicked Uncles. On 21 October 1941 Turing, Welchman, Alexander and Milner-Barry wrote over everyone's heads to
/// the Prime Minister about the shortage of typists, Wrens and Bombes, and Milner-Barry took the letter to Downing Street
/// himself. Here, when Whitehall's patience is running out in the hard months before then (from Claymore), the four offer
/// to write early: the Prime Minister's backing, money and the Admiralty's Wrens come at once, and 'Action this day'
/// brings less when it comes, for its help came early. Once a game.
/// </summary>
public sealed partial class GameState
{
    public const string UnclesTitle = "A letter to the Prime Minister";

    /// <summary>The four wrote early: the Wrens come at the later rate and 'Action this day' brings less.</summary>
    public bool UnclesWrote { get; set; }

    bool UnclesDue =>
        Mode == GameMode.Campaign && Dilemmas.Count == 0 && !Stats.ScriptedDone.Contains("uncles") &&
        ActIndex >= Campaign.KissesFrom && ActIndex < Campaign.ActionThisDay && Reputation < Defs.UnclesAt;

    /// <summary>The letter has gone: from now the Wrens come at the later rate, the first of them at once (own dice).</summary>
    void LetterSent()
    {
        UnclesWrote = true;
        var shared = Rng;
        Rng = Rng.Seeded(Mix(Seed, Day, 0x0C1E));
        for (int i = 0; i < Defs.UnclesWrens; i++) AddCandidate(Role.Wren);
        Rng = shared;
        Emit(EventKind.Good, $"The Admiralty is sending Wrens at once: {Defs.UnclesWrens} are waiting to be taken on (Hire).");
    }

    /// <summary>For screenshots and tests: the offer now.</summary>
    public void DebugUncles()
    {
        Stats.ScriptedDone.Remove("uncles");
        MaybeUncles(force: true);
    }

    void MaybeUncles(bool force = false)
    {
        if (!force && !UnclesDue) return;
        Stats.ScriptedDone.Add("uncles");
        RaiseDilemma(new Dilemma
        {
            Service = Service.Security, Title = UnclesTitle, Tag = "uncles",
            Text = "Turing, Welchman, Alexander and Milner-Barry have come to your office together. Whitehall's patience is wearing thin and " +
                   "the huts are short of everything: typists, Wrens, Bombes. They want to write to the Prime Minister directly, over the heads " +
                   "of the Director and the Foreign Office, and Milner-Barry will take the letter to Downing Street himself.",
            Context = "It is unheard of, and it may work: Mr Churchill reads ULTRA every day and calls the Park his geese that lay the golden eggs. " +
                      "If he backs us, Whitehall will have to as well, and the money and the Admiralty's Wrens will come now rather than in the " +
                      "autumn. The proper channels are slower and give less, but leave the help still to come.",
            Options =
            {
                new() { Label = "Put the case through the proper channels", Rep = Defs.UnclesChannelsRep, Funds = Defs.UnclesChannelsFunds,
                    Outcome = "The Director takes our case to the Foreign Office. A little money is found, and promises are made about the rest." },
                new() { Label = "Let them write to the Prime Minister", Rep = Defs.UnclesRep, Funds = Defs.UnclesFunds, Morale = Defs.UnclesMorale, Action = "uncles",
                    Outcome = "Milner-Barry takes the train to London and hands the letter in at Number 10. Two days later the reply comes down: " +
                              "'Action this day. Make sure they have all they want on extreme priority and report to me that this has been done.'" },
            },
        });
    }
}
