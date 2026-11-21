using System;
using System.Collections.Generic;

namespace StationX.Sim;

/// <summary>A German change of procedure late in the war, and the research that answers it.</summary>
public sealed record CounterDef(string Id, string Name, string Act, int AfterDays, Func<NetDef, bool> Hits, StationType[] Steps,
    double Mult, ResearchId Answer, string News, string Effect);

/// <summary>
/// German countermeasures. The Germans never believed Enigma broken, but they kept making it harder: the Luftwaffe's
/// rewirable reflector, Umkehrwalze D (from January 1944; Hut 6 answered with the Duenna Bombe), the Lorenz links'
/// wheel patterns changed daily (from August 1944; Colossus Mark 2 had come just in time), and the Enigma Uhr, a
/// plugboard attachment (from July 1944). Here each arrives a few days into its chapter and slows one step for the
/// networks it hits until its answer is researched.
/// </summary>
public sealed partial class GameState
{
    /// <summary>Countermeasures that have come in (they stay in force until their answer is researched).</summary>
    public HashSet<string> Counters { get; set; } = new();

    public static readonly CounterDef[] CounterDefs =
    {
        new("ukwd", "Umkehrwalze D", "D-Day", 1, n => n.Service == Service.Luftwaffe && n.Family == Family.Enigma,
            new[] { StationType.Bombe, StationType.Bombe4 }, 1.8, ResearchId.Duenna,
            "The Luftwaffe has a new reflector, Umkehrwalze D, whose wiring its operators can change. Hut 6's Bombe runs take far longer until we can work out each day's wiring (Research: Duenna).",
            "Bombe runs slower: UKW-D"),
        new("sz42", "Daily wheel patterns", "Market Garden", 1, n => n.Family == Family.Lorenz,
            new[] { StationType.Testery, StationType.HeathRobinson, StationType.Colossus }, 1.8, ResearchId.ColossusII,
            "The German high command now changes TUNNY's wheel patterns every day instead of every month. Each day's patterns have to be found before its settings (Research: Colossus Mark 2).",
            "Wheels slower: daily patterns"),
        new("uhr", "The Enigma Uhr", "The Ardennes", 1, n => n.Service == Service.Luftwaffe && n.Family == Family.Enigma,
            new[] { StationType.Crib }, 1.7, ResearchId.UhrBox,
            "Luftwaffe operators have a new box on the plugboard, the Uhr, that scrambles the steckers by a dial setting. Menus are much harder to write (Research: the Uhr box).",
            "Menus slower: the Uhr"),
    };

    /// <summary>Countermeasures in force against a network.</summary>
    public IEnumerable<CounterDef> CountersOn(Net net)
    {
        if (Counters.Count == 0) yield break;
        var nd = Defs.NetOf(net);
        foreach (var c in CounterDefs)
            if (Counters.Contains(c.Id) && !Researched.Contains(c.Answer) && c.Hits(nd)) yield return c;
    }

    /// <summary>How much longer a step takes for this network under the countermeasures in force.</summary>
    double CounterMult(Net net, StationType step)
    {
        if (Counters.Count == 0) return 1;
        double t = 1;
        var nd = Defs.NetOf(net);
        foreach (var c in CounterDefs)
            if (Counters.Contains(c.Id) && !Researched.Contains(c.Answer) && Array.IndexOf(c.Steps, step) >= 0 && c.Hits(nd)) t *= c.Mult;
        return t;
    }

    void DailyCounters()
    {
        foreach (var c in CounterDefs)
        {
            if (Counters.Contains(c.Id)) continue;
            int act = Campaign.IndexOf(c.Act);
            if (act < 0 || ActIndex < act || (ActIndex == act && Day < ActStartDay + c.AfterDays)) continue;
            BringIn(c);
        }
    }

    void BringIn(CounterDef c)
    {
        Counters.Add(c.Id);
        ResearchAvailable.Add(c.Answer);
        Stats.Countermeasures++;
        if (!Researched.Contains(c.Answer)) Emit(EventKind.Bad, c.News);
    }

    /// <summary>For screenshots and tests.</summary>
    public void DebugCounter(string id)
    {
        foreach (var c in CounterDefs) if (c.Id == id && !Counters.Contains(id)) BringIn(c);
    }
}
