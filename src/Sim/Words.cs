using System;

namespace StationX.Sim;

/// <summary>
/// Reputation and suspicion are never shown as numbers (user request): the player reads them as standings, levels and sizes
/// of change. These words are shared by the sim's own texts (dilemma effects, events) and the interface.
/// </summary>
public static class Words
{
    /// <summary>How a German service stands towards its ciphers.</summary>
    public static string SuspicionLevel(double s) =>
        s >= Defs.SuspicionLimit ? "locked out" : s >= Defs.WorryFrom ? "alarmed" : s >= Defs.SuspicionWarn - 10 ? "suspicious"
        : s >= Defs.WaryFrom ? "wary" : s >= 20 ? "untroubled" : "unsuspecting";

    /// <summary>How Whitehall regards the Park.</summary>
    public static string RepStanding(double rep) =>
        rep < 15 ? "on the brink" : rep < 25 ? "in disfavour" : rep < 45 ? "doubted" : rep < 65 ? "trusted" : rep < 85 ? "well regarded" : "held in high esteem";

    /// <summary>A change in reputation: "reputation up a little", "reputation down sharply".</summary>
    public static string RepChange(double d)
    {
        double a = Math.Abs(d);
        string size = a < 0.75 ? " a little" : a < 2.5 ? "" : a < 5 ? " well" : " sharply";
        return d >= 0 ? (size == " well" ? "reputation well up" : $"reputation up{size}") : (size == " well" ? "reputation well down" : $"reputation down{size}");
    }

    /// <summary>How much reputation, as a noun phrase: "a little reputation", "a great deal of reputation".</summary>
    public static string RepAmount(double d)
    {
        double a = Math.Abs(d);
        return a < 0.75 ? "a little reputation" : a < 2.5 ? "some reputation" : a < 5 ? "a good deal of reputation" : "a great deal of reputation";
    }

    /// <summary>A change in suspicion: "a little more suspicion", "much less suspicion".</summary>
    public static string SuspicionChange(double d)
    {
        double a = Math.Abs(d);
        string size = a < 3 ? "a little " : a < 8 ? "" : a < 15 ? "much " : "far ";
        return $"{size}{(d >= 0 ? "more" : "less")} suspicion";
    }

    /// <summary>How fast reputation slips each day: "slowly", "steadily", "quickly", "fast".</summary>
    public static string RepRate(double perDay)
    {
        double a = Math.Abs(perDay);
        return a < 0.2 ? "slowly" : a < 0.6 ? "steadily" : a < 1.2 ? "quickly" : "fast";
    }

    /// <summary>A multiplier as words: "much less", "less", "about the same", "more", "much more".</summary>
    public static string Relative(double mult) =>
        mult < 0.5 ? "much less" : mult < 0.95 ? "less" : mult <= 1.05 ? "the usual" : mult < 1.5 ? "more" : "much more";
}
