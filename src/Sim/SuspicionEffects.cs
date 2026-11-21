using System;

namespace StationX.Sim;

/// <summary>
/// What rising suspicion costs before a lockdown. A German service that begins to wonder doesn't change everything at once:
/// signals officers are told to vary their stereotyped openings, to stop sending "nothing to report", to change indicators
/// more carefully. From <see cref="Defs.WaryFrom"/> its keys get steadily harder (cribs and Bombe runs, hand ciphers and the
/// Testery's work on its traffic take up to <see cref="Defs.WaryMax"/> longer at the limit). From <see cref="Defs.WorryFrom"/>
/// Whitehall hears of it too, and worries about the source every day it stays that high. At the limit: the lockdown.
/// </summary>
public sealed partial class GameState
{
    /// <summary>How much longer key-breaking on this service's traffic takes because it has grown wary (1 = not at all).</summary>
    public double WaryMult(Service sv) => WaryMultAt(SuspicionOf(sv));

    public static double WaryMultAt(double suspicion) =>
        1 + Defs.WaryMax * Math.Clamp((suspicion - Defs.WaryFrom) / (Defs.SuspicionLimit - Defs.WaryFrom), 0, 1);

    /// <summary>The steps that break a key, which a wary service's habits slow down.</summary>
    static bool KeyBreakingStep(StationType t) => t is StationType.Crib or StationType.HandCipher or StationType.Testery
        or StationType.HeathRobinson or StationType.Colossus or StationType.Zygalski or StationType.Banburismus;

    void DailySuspicionWorry()
    {
        foreach (var sv in Enum.GetValues<Service>())
            if (ServiceInPlay(sv) && SuspicionOf(sv) >= Defs.WorryFrom)
                AdjustRep(-Defs.WorryRep, "Security worries");
    }

    /// <summary>A short description of what a suspicion level means, for the panel and the decisions.</summary>
    public static string SuspicionMeans(double s)
    {
        string level = Words.SuspicionLevel(s);
        if (s >= Defs.SuspicionLimit) return "they lock us out";
        string slower = WaryMultAt(s) < 1.12 ? "a little slower to break" : WaryMultAt(s) < 1.3 ? "slower to break" : "much slower to break";
        if (s >= Defs.WorryFrom) return $"{level}: its keys {slower}, and Whitehall worries about the source every day";
        if (s >= Defs.WaryFrom) return $"{level}: its keys {slower}";
        return level;
    }
}
