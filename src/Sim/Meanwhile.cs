using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// "Meanwhile…": the campaign jumps from one chapter's date to the next, sometimes by a year. When a chapter opens six weeks
/// or more after the last one's date, a page says what happened in between (from <see cref="Headlines"/>) and asks how the
/// Park spent the months: money put by, old problems worked on (breakthroughs; the middle choice), or a security overhaul.
/// Each scales with the months skipped, up to <see cref="Defs.MeanwhileMaxMonths"/>.
/// </summary>
public sealed partial class GameState
{
    public static readonly (DateOnly Date, string Text)[] Headlines =
    {
        (new(1939, 12, 17), "the Graf Spee is scuttled off Montevideo"),
        (new(1940, 4, 9), "Germany invades Denmark and Norway"),
        (new(1940, 5, 10), "Germany attacks in the West and Churchill becomes Prime Minister"),
        (new(1940, 6, 4), "the last boats leave Dunkirk"),
        (new(1940, 6, 22), "France signs an armistice"),
        (new(1940, 9, 7), "the Blitz on London begins"),
        (new(1940, 11, 11), "the Fleet Air Arm cripples the Italian fleet at Taranto"),
        (new(1940, 11, 14), "Coventry is bombed"),
        (new(1941, 1, 22), "Tobruk falls to the Australians"),
        (new(1941, 2, 12), "Rommel lands in Tripoli"),
        (new(1941, 3, 11), "Lend-Lease becomes law in America"),
        (new(1941, 5, 27), "the Bismarck is sunk"),
        (new(1941, 8, 14), "Churchill and Roosevelt sign the Atlantic Charter"),
        (new(1941, 11, 18), "the Eighth Army opens Operation Crusader in the desert"),
        (new(1941, 12, 7), "Japan attacks Pearl Harbor and America is in the war"),
        (new(1942, 2, 15), "Singapore surrenders"),
        (new(1942, 6, 4), "the US Navy sinks four Japanese carriers at Midway"),
        (new(1942, 6, 21), "Tobruk falls to Rommel"),
        (new(1942, 8, 19), "the raid on Dieppe fails"),
        (new(1942, 10, 23), "the battle of El Alamein begins"),
        (new(1942, 11, 8), "Allied armies land in French North Africa"),
        (new(1943, 2, 2), "the Germans surrender at Stalingrad"),
        (new(1943, 5, 13), "the Axis armies in Tunisia surrender"),
        (new(1943, 5, 24), "Dönitz withdraws his U-boats from the North Atlantic"),
        (new(1943, 7, 10), "the Allies land in Sicily"),
        (new(1943, 9, 8), "Italy surrenders"),
        (new(1943, 12, 26), "the Scharnhorst is sunk off the North Cape"),
        (new(1944, 1, 22), "the Allies land at Anzio"),
        (new(1944, 6, 4), "Rome falls"),
        (new(1944, 6, 13), "the first flying bombs fall on London"),
        (new(1944, 7, 20), "a bomb in his headquarters fails to kill Hitler"),
        (new(1944, 8, 25), "Paris is liberated"),
        (new(1944, 9, 8), "the first V-2 rocket lands in Chiswick"),
        (new(1944, 10, 21), "Aachen falls, the first German city taken"),
        (new(1945, 2, 4), "Churchill, Roosevelt and Stalin meet at Yalta"),
        (new(1945, 3, 7), "the Americans cross the Rhine at Remagen"),
    };

    /// <summary>Called as a chapter opens, with the date the last one had reached.</summary>
    void Meanwhile(DateOnly from, DateOnly to)
    {
        int days = to.DayNumber - from.DayNumber;
        if (Mode != GameMode.Campaign || days < Defs.MeanwhileMinDays) return;
        int gone = Math.Max(1, (int)Math.Round(days / 30.4)), months = Math.Min(gone, Defs.MeanwhileMaxMonths);
        Dilemmas.RemoveAll(d => d.Tag == "meanwhile");
        var news = Headlines.Where(h => h.Date > from && h.Date < to).ToList();
        if (news.Count > Defs.MeanwhileHeadlines)
        {
            // Spread over the gap: first, last and evenly between.
            var pick = new List<(DateOnly, string)>();
            for (int i = 0; i < Defs.MeanwhileHeadlines; i++)
                pick.Add(news[(int)Math.Round(i * (news.Count - 1) / (double)(Defs.MeanwhileHeadlines - 1))]);
            news = pick;
        }
        string what = news.Count == 0 ? "The war goes on, and the Park with it."
            : string.Join("\n", news.Select(h => $"{h.Date:d MMMM yyyy}: {Capital(h.Text)}."));
        int funds = Defs.MeanwhileFunds * months, points = Defs.MeanwhilePoints * months;
        int quiet = Defs.MeanwhileQuiet * months;
        RaiseDilemma(new Dilemma
        {
            Service = Service.Security, Title = $"Meanwhile, {gone} month{(gone == 1 ? "" : "s")} on", Tag = "meanwhile",
            Text = what,
            Context = $"From {from:MMMM yyyy} to {to:MMMM yyyy} the Park worked on as it does. Where did the effort go?",
            Options =
            {
                new() { Label = "Into economies", Funds = funds,
                    Outcome = "Fewer taxis, colder huts and a careful bursar: the Park has money in hand." },
                new() { Label = "Into old problems", Points = points,
                    Outcome = "The quiet months went into problems put aside: notes, methods and half a dozen ideas worth having." },
                new() { Label = $"Into security (suspicion −{quiet} everywhere)", Action = $"meanwhile-quiet:{quiet}",
                    Outcome = "New rules on papers, passes and talk in the pubs; cover stories tidied up. The Germans have less to wonder about." },
            },
        });
    }

    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    void MeanwhileAction(string action)
    {
        if (!int.TryParse(action["meanwhile-quiet:".Length..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var q)) return;
        foreach (var sv in Enum.GetValues<Service>())
            if (SuspicionOf(sv) > 0) Suspicion[sv] = Math.Max(0, SuspicionOf(sv) - q);
    }

    /// <summary>For screenshots and tests.</summary>
    public void DebugMeanwhile(DateOnly from, DateOnly to) => Meanwhile(from, to);
}
