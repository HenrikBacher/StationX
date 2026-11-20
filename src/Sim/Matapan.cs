using System;

namespace StationX.Sim;

/// <summary>
/// Cape Matapan, March 1941. Dilly Knox's section in the Cottage broke the Italian navy's Enigma, and on 25 March Mavis
/// Lever read "Today 25 March is X minus 3": the Italian fleet was coming out against the convoys to Greece. Admiral
/// Cunningham was told, a Sunderland was sent so the Italians would think they'd been seen from the air, and he went
/// ashore at Alexandria with his golf clubs for the Japanese consul to see before slipping out by night. Three Italian
/// heavy cruisers and two destroyers were sunk on the 28th. Here: a scripted decision on how to use it.
/// </summary>
public sealed partial class GameState
{
    public const string MatapanTitle = "X minus three";

    /// <summary>For screenshots: the decision now, whatever the date.</summary>
    public void DebugMatapan()
    {
        Stats.ScriptedDone.Remove("matapan");
        var (mode, act) = (Mode, ActDate);
        Mode = GameMode.Campaign;
        ActDate = ActDate.AddDays(Defs.MatapanFrom.DayNumber - Date.DayNumber);
        MaybeMatapan();
        (Mode, ActDate) = (mode, act);
    }

    void MaybeMatapan()
    {
        if (Mode != GameMode.Campaign || Dilemmas.Count > 0) return;
        if (Date < Defs.MatapanFrom || Date >= Defs.MatapanUntil || Stats.ScriptedDone.Contains("matapan")) return;
        Stats.ScriptedDone.Add("matapan");
        RaiseDilemma(new Dilemma
        {
            Service = Service.Kriegsmarine, Title = MatapanTitle, Tag = "matapan",
            Text = "Dilly Knox's section in the Cottage has broken the Italian navy's Enigma. Mavis Lever, nineteen, has just read a signal to " +
                   "Rhodes: 'Today 25 March is X minus 3.' The Italian fleet is putting to sea against our convoys to Greece, and the signals " +
                   "that follow give its cruisers, its course and its timings. Admiral Cunningham at Alexandria has to be told. How?",
            Context = "Source: the Italian naval Enigma, read in the Cottage by Knox's women. It is certain, and it will not come again: if " +
                      "the Italians suspect it, the Germans who lent them the machine will change it for them. Cunningham can only " +
                      "act on it if he knows; whether the enemy works out how he knew depends on what else they see.",
            Options =
            {
                new() { Label = "Signal it to Cunningham as it stands", Rep = Defs.MatapanRep + 2, Points = Defs.MatapanPoints + 1, Suspicion = 18,
                    Outcome = "Cunningham sails at once, straight for them. Three Italian heavy cruisers and two destroyers are sunk off Cape Matapan. " +
                              "In Rome they ask how he knew exactly where to look, and Berlin starts asking too." },
                new() { Label = "Send it with a cover: a Sunderland 'finds' the fleet", Rep = Defs.MatapanRep, Points = Defs.MatapanPoints, Suspicion = 6, Cover = true,
                    Outcome = "A Sunderland is sent to be seen over the Italian fleet, and Cunningham goes ashore with his golf clubs for the Japanese " +
                              "consul's benefit before slipping out at dusk. Off Cape Matapan three heavy cruisers and two destroyers go down. " +
                              "Cunningham comes to the Cottage to thank Knox's girls himself." },
                new() { Label = "Too precious: only a general warning", Rep = -2, Suspicion = -2,
                    Outcome = "Alexandria is told the Italians may be at sea. The convoys turn back and the Italian fleet goes home unharmed, " +
                              "none the wiser. In the Cottage they say nothing, which says a good deal." },
            },
        });
    }
}
