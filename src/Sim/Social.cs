using System;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// The Recreational Club. The Park made its own entertainment: the Music Society's concerts, the Drama Society's
/// revues, Scottish country dancing in the Assembly Hall, chess against Hut 8, dances. Here the Staff panel can arrange
/// an evening every so often; it takes place the next evening at half past seven and lifts everyone's spirits, and
/// now and then two people who'd fallen out make it up on the dance floor.
/// </summary>
public sealed partial class GameState
{
    /// <summary>When the last evening was arranged (game minutes), or long ago.</summary>
    public double SocialArrangedAt { get; set; } = -1e9;
    /// <summary>When the next evening takes place (game minutes), or -1 if none is arranged.</summary>
    public double SocialAt { get; set; } = -1;
    /// <summary>Evenings held so far: which kind comes next.</summary>
    public int Socials { get; set; }

    public bool SocialOpen => ActIndex >= Defs.SocialFromAct;
    public bool SocialPending => SocialAt >= 0;
    public double SocialReadyIn => SocialArrangedAt + Defs.SocialEveryDays * Defs.MinutesPerDay - Now;

    /// <summary>The kind of evening the club puts on next, in turn.</summary>
    public (string name, string what) NextSocial => SocialKinds[Socials % SocialKinds.Length];

    static readonly (string name, string what)[] SocialKinds =
    {
        ("a concert", "The Music Society plays Elgar and Haydn in the Mansion's ballroom, and a Wren sings Purcell."),
        ("Scottish country dancing", "Reels in the Assembly Hall: the Hut 6 set against Hut 8, and nobody quite sure of the steps."),
        ("the revue", "The Drama Society's revue: sketches about the canteen, the Bombes, and a very good impression of the Director."),
        ("a chess evening", "A simultaneous display in the Mansion: one of Hut 8's masters against twenty boards, and he loses two."),
        ("a dance", "A dance in the Assembly Hall with a band from the RAF at Chicksands. Some of the Wrens stay till the last waltz."),
    };

    /// <summary>Why an evening can't be arranged now, or null if it can.</summary>
    public string? CantHoldSocial()
    {
        if (!SocialOpen) return "The Recreational Club starts once the war gets going.";
        if (SocialPending) return "An evening is already arranged.";
        if (SocialReadyIn > 0) return $"The club has nothing ready for another {Math.Ceiling(SocialReadyIn / Defs.MinutesPerDay):0} days.";
        if (Funds < Defs.SocialCost) return $"Needs £{Defs.SocialCost}.";
        return null;
    }

    /// <summary>Arranges the next evening for half past seven: today if there's time, else tomorrow.</summary>
    public bool HoldSocial()
    {
        if (CantHoldSocial() != null) return false;
        Funds -= Defs.SocialCost;
        SocialArrangedAt = Now;
        double at = Day * Defs.MinutesPerDay + Defs.SocialHour * 60;
        if (at < Now + 120) at += Defs.MinutesPerDay;
        SocialAt = at;
        Emit(EventKind.Info, $"The Recreational Club puts on {NextSocial.name} (£{Defs.SocialCost}), {(at < (Day + 1) * Defs.MinutesPerDay ? "this" : "tomorrow")} evening at half past seven.");
        return true;
    }

    /// <summary>The evening itself: spirits up for everyone, and a quarrel or two mended (own dice).</summary>
    void TickSocial()
    {
        if (!SocialPending || Now < SocialAt) return;
        SocialAt = -1;
        var (name, what) = NextSocial;
        Socials++;
        ChangeMorale(Defs.SocialMorale);
        var dice = Rng.Seeded(Mix(Seed, Day, 0x50C1));
        int mended = 0;
        foreach (var s in Staff.Where(s => s.Rival != null && s.Id < s.Rival).ToList())
            if (dice.Chance(Defs.SocialMend) && StaffById(s.Rival!.Value) is { } r)
            {
                s.RivalUntilDay = r.RivalUntilDay = Day;   // the daily round notices they've made it up
                mended++;
            }
        Emit(EventKind.Good, $"{char.ToUpper(name[0])}{name[1..]} tonight. {what} Spirits are up all round" +
                             (mended > 0 ? $", and {mended} {(mended == 1 ? "quarrel is" : "quarrels are")} patched up." : "."));
    }
}
