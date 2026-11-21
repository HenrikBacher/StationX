using System;
using System.Collections.Generic;

namespace StationX.Sim;

/// <summary>
/// The notables' own stories, each once a game and only with them on the staff.
/// Welchman: the organisation of Hut 6, registration through to Hut 3, was his; here he offers to reorganise his building,
/// two days of upheaval for desks that work faster for good. Knox: ill with cancer from 1941, he worked on from his house
/// at Courns Wood (breaking the Abwehr's Enigma there) and died on 27 February 1943. Turing: in November 1942 he sailed to
/// Washington to help the US Navy with its Bombes; letting him go makes Dayton's start the stronger. And his bicycle.
/// </summary>
public sealed partial class GameState
{
    /// <summary>Buildings reorganised on Welchman's system, and those still in the upheaval (until when).</summary>
    public HashSet<string> Reorganised { get; set; } = new();
    public Dictionary<string, double> DisruptedUntil { get; set; } = new();
    /// <summary>When a declined story may be offered again (game day), by tag.</summary>
    public Dictionary<string, int> StoryAgain { get; set; } = new();
    public bool KnoxAtHome { get; set; }
    public bool TuringWent { get; set; }

    Staff? NotableHere(string name)
    {
        foreach (var s in Staff) if (s.Notable == name && !s.Leaving) return s;
        return null;
    }

    bool StoryDue(string tag) => !Stats.ScriptedDone.Contains(tag) && (!StoryAgain.TryGetValue(tag, out int d) || Day >= d);

    /// <summary>How a building's desks are working: faster once reorganised, slower in the upheaval.</summary>
    double ZoneFactor(Station st)
    {
        double builders = 1;
        if (Grid.ZoneAt(st.X, st.Y) is { Rebuilding: true } bz) builders = BuildersFactor(bz);
        return builders * ReorganisedFactor(st);
    }

    double ReorganisedFactor(Station st)
    {
        if (Reorganised.Count == 0) return 1;
        var z = Grid.ZoneAt(st.X, st.Y)?.Id;
        if (z == null || !Reorganised.Contains(z)) return 1;
        return DisruptedUntil.TryGetValue(z, out var until) && Now < until ? Defs.WelchmanUpheaval : Defs.WelchmanFaster;
    }

    void DailyNotables()
    {
        if (Mode != GameMode.Campaign) return;
        KnoxDies();
        if (KnoxAtHome && Day % 7 == 0 && NotableHere("Dilly Knox") != null)
        {
            Points += Defs.KnoxPoints;
            Emit(EventKind.Good, $"A parcel of worksheets from Courns Wood: Knox has broken more of the Abwehr's Enigma. +{Defs.KnoxPoints} breakthroughs.");
        }
        if (Dilemmas.Count > 0) return;
        if (StoryDue("welchman") && ActIndex >= Campaign.IndexOf("Their Finest Hour") && NotableHere("Gordon Welchman") is { } w && !w.IsSick(Now)) Welchman(w);
        else if (StoryDue("knox") && ActIndex >= Campaign.KissesFrom && NotableHere("Dilly Knox") is { } k && !k.IsSick(Now)) KnoxIll(k);
        else if (StoryDue("turing-us") && ActIndex >= Campaign.EnigmaFinale && NotableHere("Alan Turing") is { } t && !t.IsSick(Now)) TuringToAmerica(t);
        if (!Stats.ScriptedDone.Contains("bicycle") && Date.Month is 5 or 6 && Weather == Weather.Fine && NotableHere("Alan Turing") is { } tb && !tb.IsSick(Now))
        {
            Stats.ScriptedDone.Add("bicycle");
            Emit(EventKind.Info, $"{tb.Name} cycles in wearing his gas mask against the hay fever. His chain slips every so many turns; rather than mend it he " +
                                 "counts the pedal strokes and gets off to set it right just in time. His tea mug is chained to the radiator.");
        }
    }

    /// <summary>For screenshots and tests: offer a notable's story now (welchman, knox, turing-us).</summary>
    public bool DebugStory(string tag)
    {
        Stats.ScriptedDone.Remove(tag);
        StoryAgain.Remove(tag);
        switch (tag)
        {
            case "welchman" when NotableHere("Gordon Welchman") is { } w: Welchman(w); return true;
            case "knox" when NotableHere("Dilly Knox") is { } k: KnoxIll(k); return true;
            case "turing-us" when NotableHere("Alan Turing") is { } t: TuringToAmerica(t); return true;
        }
        return false;
    }

    void Welchman(Staff w)
    {
        string zone = w.HeadOf ?? w.Posted ?? "hut6";
        string name = Grid.Zone(zone)?.Name ?? "Hut 6";
        if (name.StartsWith("The ")) name = "the " + name[4..];   // mid-sentence
        Stats.ScriptedDone.Add("welchman");
        RaiseDilemma(new Dilemma
        {
            Service = Service.Security, Title = "Welchman's system", Tag = "welchman", StaffId = w.Id,
            Text = $"{w.Name} has drawn up how {name} ought to run: every message logged once at registration and followed by its docket, the " +
                   "machine room, the decoding room and the hand-over to Hut 3 each with its own place, its own people and its own forms. He wants " +
                   "to put it in now.",
            Context = $"Two days of upheaval while desks are moved, forms printed and everyone learns the new way: {name}'s desks would work " +
                      $"{(Defs.WelchmanUpheaval - 1) * 100:0}% slower. After that, {(1 - Defs.WelchmanFaster) * 100:0}% faster, for good.",
            Options =
            {
                new() { Label = "Not now: we can't afford the upheaval", Action = "story-later:welchman",
                    Outcome = $"{w.Name} puts his plan back in the drawer, for now." },
                new() { Label = $"Let him reorganise {name}", Action = $"welchman:{zone}",
                    Outcome = $"For two days {name} is chaos. Then it hums." },
            },
        });
    }

    void KnoxIll(Staff k)
    {
        Stats.ScriptedDone.Add("knox");
        RaiseDilemma(new Dilemma
        {
            Service = Service.Security, Title = "Dilly is ill", Tag = "knox", StaffId = k.Id,
            Text = $"{k.Name} has cancer. He will not stop working. He could go on at the Cottage, or from his house at Courns Wood, where the " +
                   "Abwehr's own Enigma, which nobody else has broken, could have all his attention.",
            Context = "At home he'd be no use at a desk here, but he'd send in breakthroughs every week for as long as he can. At the Cottage he " +
                      "works as before, at a cost to his health that everyone can see.",
            Options =
            {
                new() { Label = "Keep him at the Cottage", Action = "knox-stay",
                    Outcome = $"{k.Name} stays, grey and thin, folding himself into the Cottage every morning." },
                new() { Label = "Let him work from Courns Wood", Action = "knox-home",
                    Outcome = "A car takes the worksheets to Courns Wood each week and brings his back. The Abwehr's machine is in good hands." },
            },
        });
    }

    void KnoxDies()
    {
        if (Stats.ScriptedDone.Contains("knoxdied") || NotableHere("Dilly Knox") is not { } k) return;
        if (Date < Defs.KnoxDied && ActIndex <= Campaign.IndexOf("The Testery")) return;
        Stats.ScriptedDone.Add("knoxdied");
        Fire(k.Id);
        ChangeMorale(-Defs.KnoxDiedMorale);
        Emit(EventKind.Bad, $"{k.Name} has died at Courns Wood. He was made a Companion of St Michael and St George a few weeks before; the Park mourns him.");
    }

    void TuringToAmerica(Staff t)
    {
        Stats.ScriptedDone.Add("turing-us");
        RaiseDilemma(new Dilemma
        {
            Service = Service.Security, Title = "Washington asks for Turing", Tag = "turing-us", StaffId = t.Id,
            Text = $"The US Navy is building four-rotor Bombes of its own and wants {t.Name} to look at the design. He would sail on the Queen " +
                   $"Elizabeth and be away about {Defs.TuringAwayDays} days.",
            Context = "Hut 8 would manage without him for a month; it has learned to. What he tells the Americans would make their Bombes, and " +
                      "any agreement to share the work with them, the better from the start.",
            Options =
            {
                new() { Label = "Keep him in Hut 8",
                    Outcome = $"Travis tells Washington that {t.Name} cannot be spared. The Americans press on alone." },
                new() { Label = "Send him to America", Rep = 2, Action = "turing-us",
                    Outcome = $"{t.Name} sails for New York. He finds the Americans' design over-elaborate, and says so." },
            },
        });
    }

    void StoryAction(string action, Dilemma d)
    {
        var who = d.StaffId is int sid ? StaffById(sid) : null;
        switch (action)
        {
            case { } a when a.StartsWith("story-later:"):
                string tag = a["story-later:".Length..];
                Stats.ScriptedDone.Remove(tag);
                StoryAgain[tag] = Day + Defs.StoryAgainDays;
                break;
            case { } a when a.StartsWith("welchman:"):
                string zone = a["welchman:".Length..];
                Reorganised.Add(zone);
                DisruptedUntil[zone] = Now + Defs.WelchmanUpheavalDays * Defs.MinutesPerDay;
                break;
            case "knox-home" when who != null:
                KnoxAtHome = true;
                SendOff(who, 10000, "Courns Wood");
                break;
            case "knox-stay" when who != null:
                who.Strain = Math.Min(1, who.Strain + Defs.KnoxStrain);
                break;
            case "turing-us" when who != null:
                TuringWent = true;
                SendOff(who, Defs.TuringAwayDays, "America");
                break;
        }
    }
}
