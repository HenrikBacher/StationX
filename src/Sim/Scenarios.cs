using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>A medal for a scenario: none, then bronze, silver and gold.</summary>
public enum Medal { None, Bronze, Silver, Gold }

/// <summary>
/// A short scenario: the campaign from one chapter's start for a fixed number of days, with one measure to beat (decrypts of
/// a service or network delivered in that time). Bronze, silver and gold by how far past the marks the Park gets; a Park
/// closed down for lack of reputation gets nothing.
/// </summary>
public sealed record ScenarioDef(string Id, string Name, string Act, int Days, string Blurb, string Measure, Func<Net, bool> Counts,
                                 int Bronze, int Silver, int Gold);

public static class Scenarios
{
    public static readonly ScenarioDef[] All =
    {
        new("britain", "The Battle of Britain", "Their Finest Hour", 21,
            "July 1940. The Luftwaffe is setting out to destroy Fighter Command before an invasion. Read its traffic for three weeks: every decrypt " +
            "tells the RAF where and when.", "Luftwaffe decrypts",
            n => Defs.NetOf(n).Service == Service.Luftwaffe, 55, 75, 100),
        new("dolphin", "DOLPHIN", "Operation Claymore", 21,
            "March 1941. The raid on the Lofoten Islands has brought back the naval key tables. Get the U-boats' home-waters traffic read and keep it read.",
            "naval decrypts", n => Defs.NetOf(n).Service == Service.Kriegsmarine, 70, 90, 105),
        new("shark", "The SHARK blackout ends", "U-559", 28,
            "December 1942. Two sailors have drowned bringing out the weather short-signal book from U-559. SHARK can be read again: the Atlantic convoys depend on it.",
            "naval decrypts", n => Defs.NetOf(n).Service == Service.Kriegsmarine, 160, 185, 200),
        new("tunny", "Tunny", "The Testery", 28,
            "Summer 1943. Hitler's generals talk to Berlin on the Lorenz teleprinter cipher. Read the High Command's own traffic by hand, then with Colossus.",
            "decrypts", _ => true, 470, 530, 600),
        new("overlord", "Before Overlord", "D-Day", 28,
            "Spring 1944. Every German order in the West matters now: where the panzers are, what the Abwehr believes, whether FORTITUDE is holding.",
            "decrypts", _ => true, 560, 610, 640),
    };

    public static ScenarioDef? ById(string? id) => All.FirstOrDefault(s => s.Id == id);

    public static Medal MedalFor(ScenarioDef s, int score) =>
        score >= s.Gold ? Medal.Gold : score >= s.Silver ? Medal.Silver : score >= s.Bronze ? Medal.Bronze : Medal.None;

    public static string Name(Medal m) => m switch { Medal.Gold => "Gold", Medal.Silver => "Silver", Medal.Bronze => "Bronze", _ => "No medal" };
}

public sealed partial class GameState
{
    /// <summary>The scenario being played, or null for a campaign or free play.</summary>
    public string? ScenarioId { get; set; }
    public int ScenarioStartDay { get; set; }
    /// <summary>Deliveries of every network at the scenario's start, so only its own days count.</summary>
    public Dictionary<Net, int> ScenarioBaseline { get; set; } = new();

    public ScenarioDef? ScenarioPlayed => Scenarios.ById(ScenarioId);

    public int ScenarioDaysLeft => ScenarioPlayed is { } s ? Math.Max(0, ScenarioStartDay + s.Days - Day) : 0;

    /// <summary>The scenario's measure so far: decrypts of its kind delivered since it began.</summary>
    public int ScenarioScore()
    {
        if (ScenarioPlayed is not { } s) return 0;
        int n = 0;
        foreach (var (net, count) in Stats.Delivered)
            if (s.Counts(net)) n += count - (ScenarioBaseline.TryGetValue(net, out int b) ? b : 0);
        return n;
    }

    public Medal ScenarioMedal() => Status == GameStatus.Lost || ScenarioPlayed is not { } s ? Medal.None : Scenarios.MedalFor(s, ScenarioScore());

    /// <summary>
    /// A new scenario. The Park is the one the war would have built by then: the autoplayer plays the campaign up to the
    /// scenario's chapter (on Easy, so it gets there; another seed if it doesn't). Then the player takes over on their own
    /// difficulty with Whitehall's confidence restored, and only what happens from then on counts.
    /// </summary>
    public static GameState NewScenario(int seed, Difficulty difficulty, string id)
    {
        var s = Scenarios.ById(id) ?? throw new ArgumentException($"No scenario {id}");
        int act = Math.Max(0, Campaign.IndexOf(s.Act));
        GameState? g = null;
        for (int attempt = 0; attempt < 6 && g == null; attempt++)
        {
            var t = NewGame(seed + attempt * 7919, Difficulty.Easy);
            var ai = new AutoPlayer(t);
            while (t.Status == GameStatus.Playing && t.ActIndex < act && t.Day < 700)
            {
                if (t.ActCompletePending) t.ContinueAfterAct();
                ai.Think();
                t.Advance(60);
                t.Events.Clear();
            }
            if (t.Status == GameStatus.Playing && t.ActIndex >= act) g = t;
        }
        g ??= NewGame(seed, difficulty, GameMode.Campaign, act);
        g.Difficulty = difficulty;
        g.Reputation = Math.Max(g.Reputation, Defs.ScenarioRep);
        g.Funds = Math.Max(g.Funds, Defs.ScenarioFunds);
        g.Dilemmas.Clear();
        g.Events.Clear();
        g.ScenarioId = id;
        g.ScenarioStartDay = g.Day;
        g.ScenarioBaseline = new Dictionary<Net, int>(g.Stats.Delivered);
        g.Emit(EventKind.Info, $"{s.Name}: {s.Days} days. Deliver as many {s.Measure} as you can: bronze at {s.Bronze}, silver at {s.Silver}, gold at {s.Gold}.");
        return g;
    }

    /// <summary>At midnight: the scenario's days are up.</summary>
    void CheckScenario()
    {
        if (ScenarioPlayed is not { } s || Status != GameStatus.Playing || Day < ScenarioStartDay + s.Days) return;
        Status = GameStatus.Won;
        CanContinue = false;
        ActCompletePending = false;
        var medal = ScenarioMedal();
        EndReason = $"{s.Name}: {s.Days} days are up. {ScenarioScore()} {s.Measure} delivered: {(medal == Medal.None ? "no medal" : Scenarios.Name(medal).ToLowerInvariant())}.";
        Emit(EventKind.Won, EndReason);
    }
}
