using System.Collections.Generic;

namespace StationX.Sim;

/// <summary>Which crib the crib desk builds the day's menu from.</summary>
public enum CribKind { Desk, Weather, Routine, Reencoded }

/// <summary>
/// Choosing the crib. A Bombe menu stands on a guess at some of a message's plaintext. Hut 6 and Hut 8 lived on the
/// German habits that gave them those guesses: the morning weather report (WETTERVORHERSAGE), the units with nothing to report
/// (KEINE BESONDEREN EREIGNISSE), and the same text sent again in a key already broken (a re-encodement). When a key turns
/// stubborn (a change of German habits, or a false stop today), its row on the keys card lets the player choose for the day:
/// <list type="bullet">
/// <item>Desk: the crib desk's usual mix (as before).</item>
/// <item>Weather: naval and Luftwaffe keys only. The weather goes out at fixed hours whatever else changes, so a change of
/// habits doesn't touch it; the menu waits a little for the morning report, and holds well.</item>
/// <item>Routine: "nothing to report" is everywhere and quick to set, but a short crib gives more false stops.</item>
/// <item>Re-encoded: only once another Enigma key is broken today. A long, exact crib: slow to find, the stop almost certain,
/// the Bombe run shorter, and no change of habits matters.</item>
/// </list>
/// </summary>
public sealed partial class GameState
{
    /// <summary>The day's crib for a network, as "Net:day" → kind (only when not the desk's own).</summary>
    public Dictionary<string, CribKind> CribPlan { get; set; } = new();
    /// <summary>Keys with a false Bombe stop, as "Net:day".</summary>
    public HashSet<string> FalseStops { get; set; } = new();

    public CribKind CribFor(Net net, int day) => CribPlan.TryGetValue(KeyId(net, day), out var k) ? k : CribKind.Desk;

    /// <summary>A key worth choosing a crib for today: Enigma with Bombes, not broken or given away, and gone stubborn.</summary>
    public bool CribChoiceOpen(Net net)
    {
        var nd = Defs.NetOf(net);
        if (nd.Family != Family.Enigma || !Researched.Contains(ResearchId.Bombe)) return false;
        if (KeyBroken(net, Day) || Kissed(net, Day) || Gardened(net, Day)) return false;
        return Stale(net, Day) || FalseStops.Contains(KeyId(net, Day)) || CribFor(net, Day) != CribKind.Desk;
    }

    /// <summary>Why a crib can't be used for this key today, or null.</summary>
    public string? CantUseCrib(Net net, CribKind kind)
    {
        var nd = Defs.NetOf(net);
        return kind switch
        {
            CribKind.Weather when !(nd.Naval || nd.Service == Service.Luftwaffe) => "This network sends no weather reports",
            CribKind.Reencoded when !OtherKeyBroken(net) => "No other Enigma key broken today to find it in",
            _ => null,
        };
    }

    bool OtherKeyBroken(Net net)
    {
        foreach (var n in ActiveNets)
            if (n != net && Defs.NetOf(n).Family == Family.Enigma && KeyBroken(n, Day)) return true;
        return false;
    }

    public bool SetCrib(Net net, CribKind kind)
    {
        if (CantUseCrib(net, kind) != null) return false;
        if (kind == CribKind.Desk) CribPlan.Remove(KeyId(net, Day));
        else CribPlan[KeyId(net, Day)] = kind;
        return true;
    }

    /// <summary>The next crib on from this one that can be used today (the keys card's button cycles through them).</summary>
    public CribKind NextCrib(Net net, CribKind kind)
    {
        for (int i = 1; i <= 4; i++)
        {
            var k = (CribKind)(((int)kind + i) % 4);
            if (CantUseCrib(net, k) == null) return k;
        }
        return CribKind.Desk;
    }

    /// <summary>The crib in use for a message's key: a re-encodement needs its other key still broken.</summary>
    CribKind CribOf(Message m)
    {
        var k = CribFor(m.Net, m.Day);
        return k == CribKind.Reencoded && !OtherKeyBroken(m.Net) ? CribKind.Desk : k;
    }

    /// <summary>Whether this message's crib shrugs off a change of German habits.</summary>
    bool CribIgnoresStale(Message m) => CribOf(m) is CribKind.Weather or CribKind.Reencoded;

    /// <summary>How much longer the crib desk takes over the menu, by crib.</summary>
    double CribTimeMult(Message m) => CribOf(m) switch
    {
        CribKind.Weather => Defs.CribWeatherTime,
        CribKind.Routine => Defs.CribRoutineTime,
        CribKind.Reencoded => Defs.CribReencodedTime,
        _ => 1,
    };

    /// <summary>The crib's quality (the chance a Bombe stop is the real key), by crib, from the desk's own.</summary>
    double CribQualityFor(Message m, double desk) => CribOf(m) switch
    {
        CribKind.Weather => System.Math.Min(0.97, desk + Defs.CribWeatherQuality),
        CribKind.Routine => desk - Defs.CribRoutineLoss,
        CribKind.Reencoded => Defs.CribReencodedQuality,
        _ => desk,
    };

    void NoteFalseStop(Message m) => FalseStops.Add(KeyId(m.Net, m.Day));

    public static string CribName(CribKind k) => k switch
    {
        CribKind.Weather => "the weather report",
        CribKind.Routine => "\"nothing to report\"",
        CribKind.Reencoded => "a re-encodement",
        _ => "the desk's choice",
    };

    public static string CribBlurb(CribKind k) => k switch
    {
        CribKind.Weather => $"WETTERVORHERSAGE goes out at fixed hours whatever else the Germans change. The menu takes {(Defs.CribWeatherTime - 1) * 100:0}% longer (waiting for the morning report), holds a little better, and a change of habits doesn't touch it.",
        CribKind.Routine => $"KEINE BESONDEREN EREIGNISSE: short, everywhere and quick to set. The menu takes {(1 - Defs.CribRoutineTime) * 100:0}% less time, but more Bombe stops are false.",
        CribKind.Reencoded => $"The same text found in a key already broken today: slow to find ({(Defs.CribReencodedTime - 1) * 100:0}% longer), but the stop is almost certain, the Bombe run is shorter, and a change of habits doesn't matter.",
        _ => "The crib desk's usual mix of cribs.",
    };
}
