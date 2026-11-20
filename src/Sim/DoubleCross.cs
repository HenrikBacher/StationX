using System;
using System.Collections.Generic;

namespace StationX.Sim;

/// <summary>A double agent of the XX Committee's, as the Abwehr sees them.</summary>
public sealed class XxAgent
{
    public string Name { get; set; } = "";
    public string Who { get; set; } = "";
    /// <summary>How far, as far as we can tell, the Abwehr believes them (0..1).</summary>
    public double Trust { get; set; }
    public bool Blown { get; set; }
    /// <summary>Taken out of FORTITUDE (after ARTIST's arrest): safe, but no use to it.</summary>
    public bool Dropped { get; set; }
    public bool Active => !Blown && !Dropped;
}

/// <summary>
/// Double Cross. MI5's XX Committee ran every German agent in Britain as a double, and ISOS, the Abwehr's hand cipher
/// read at the Park, showed whether their reports were believed: without it the committee would have been feeding
/// stories into the dark. For FORTITUDE, GARBO, BRUTUS, TRICYCLE, TATE and TREASURE built the Abwehr a phantom army
/// group in Kent. Here: from the Colossus chapter each agent's standing drifts down a little each day the Abwehr isn't
/// read, and each Abwehr decrypt shows how one of them is received, the least certain first. An agent left in doubt for
/// long may be blown. On D-Day what the Abwehr still believes adds to the invasion's readiness. ARTIST's arrest by the
/// Gestapo in the spring of 1944 asks what to do about TRICYCLE.
/// </summary>
public sealed partial class GameState
{
    public List<XxAgent> Agents { get; set; } = new();

    public bool DoubleCrossOn => Agents.Count > 0;

    static readonly (string name, string who)[] XxCast =
    {
        ("GARBO", "Juan Pujol, with a network of twenty-seven agents who never existed"),
        ("BRUTUS", "Roman Czerniawski, a Polish air force officer, 'on the staff' of the phantom army group"),
        ("TRICYCLE", "Dušan Popov, a Yugoslav lawyer with friends in the Abwehr in Lisbon"),
        ("TATE", "Wulf Schmidt, parachuted in 1940, now a 'farm worker' in Hertfordshire"),
        ("TREASURE", "Lily Sergueiew, a French-Russian journalist who travels with her dog"),
    };

    /// <summary>The Colossus chapter brings the Double Cross agents into the Park's view.</summary>
    public void StartDoubleCross()
    {
        if (Agents.Count > 0 || Mode != GameMode.Campaign) return;
        foreach (var (name, who) in XxCast) Agents.Add(new XxAgent { Name = name, Who = who, Trust = Defs.XxStartTrust });
        Emit(EventKind.Info, "MI5's Double Cross agents are building FORTITUDE's phantom army in Kent. Abwehr decrypts show whether Berlin believes them (Intelligence).");
    }

    /// <summary>What the agents still believed add to D-Day's readiness.</summary>
    public double FortitudeBonus
    {
        get
        {
            if (Act.Op != "overlord") return 0;
            double t = 0;
            foreach (var a in Agents) if (a.Active) t += a.Trust;
            return t * Defs.XxReadiness;
        }
    }

    /// <summary>The agent we're least sure of, for the next Abwehr decrypt and for the tip.</summary>
    public XxAgent? LeastSure()
    {
        XxAgent? best = null;
        foreach (var a in Agents) if (a.Active && (best == null || a.Trust < best.Trust)) best = a;
        return best;
    }

    bool XxRunning => DoubleCrossOn && (Act.Op != "overlord" || CurrentOp != null) && ActIndex <= Campaign.IndexOf("D-Day");

    /// <summary>For tests: an Abwehr decrypt delivered.</summary>
    public void DebugReadAbwehr() => ReadAbwehr();

    void ReadAbwehr()
    {
        if (!XxRunning || LeastSure() is not { } a) return;
        a.Trust = Math.Min(1, a.Trust + Defs.XxPerDecrypt);
        Stats.XxChecks++;
    }

    void DailyDoubleCross()
    {
        if (!XxRunning) return;
        var r = Rng.Seeded(Mix(Seed, Day, 0x0DC5));
        foreach (var a in Agents)
        {
            if (!a.Active) continue;
            a.Trust = Math.Max(0, a.Trust - Defs.XxDrift);
            if (a.Trust >= Defs.XxBlownBelow || !r.Chance(Defs.XxBlownChance)) continue;
            a.Blown = true;
            AdjustRep(-Defs.XxBlownRep, "Double Cross");
            Emit(EventKind.Bad, $"{a.Name} is blown: the Abwehr no longer believes a word of it, and what it was told is being read the other way. " +
                                "Read more Abwehr traffic to keep the others in view.");
        }
        if (Act.Op == "overlord" && Day >= ActStartDay + Defs.XxArtistDay && !Stats.ScriptedDone.Contains("artist") && Dilemmas.Count == 0) Artist();
    }

    /// <summary>For screenshots and tests.</summary>
    public void DebugArtist()
    {
        StartDoubleCross();
        Stats.ScriptedDone.Remove("artist");
        Artist();
    }

    void Artist()
    {
        Stats.ScriptedDone.Add("artist");
        if (Agents.Find(a => a.Name == "TRICYCLE") is not { Active: true }) return;
        RaiseDilemma(new Dilemma
        {
            Service = Service.Security, Title = "ARTIST", Tag = "artist",
            Text = "Johnny Jebsen, the Abwehr officer in Lisbon we call ARTIST, has been drugged and driven over the border into France by the " +
                   "Gestapo. He knows TRICYCLE is ours, and may guess about others. If he talks, FORTITUDE's whole story could be read " +
                   "backwards in Berlin.",
            Context = "ISOS shows nothing yet: the Abwehr itself may not know why he was taken (he was suspected of currency dealing). Dropping " +
                      "TRICYCLE from FORTITUDE keeps his story from contaminating the rest. Keeping him on keeps his voice in the chorus, and " +
                      "only the Abwehr's own traffic will tell us whether it still believes him.",
            Options =
            {
                new() { Label = "Drop TRICYCLE from FORTITUDE", Suspicion = -3, Action = "artist-drop",
                    Outcome = "TRICYCLE is told his work is done. If Jebsen talks, there is little left for him to give away." },
                new() { Label = "Keep him on, and watch the Abwehr's traffic", Suspicion = 4, Action = "artist-keep",
                    Outcome = "TRICYCLE keeps reporting. Hut 3 is told to send every Abwehr decrypt that mentions Lisbon straight to MI5." },
            },
        });
    }

    void ArtistAction(bool drop)
    {
        if (Agents.Find(a => a.Name == "TRICYCLE") is not { Active: true } t) return;
        if (drop) t.Dropped = true;
        else t.Trust = Math.Max(0, t.Trust - Defs.XxArtistDoubt);
    }
}
