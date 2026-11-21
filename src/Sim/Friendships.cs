using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// Friendships and marriages. Thousands of young people were thrown together at the Park, sworn to tell nobody what they
/// did, so they mostly had only each other; dozens of couples met there and married. Here: two people who share a building
/// and a watch can become friends (own dice, one friend each). Friends on the same watch in the same building are a little
/// happier every day. Some friendships between a man and a woman turn into courtships, and some courtships into a wedding:
/// a decision on how much leave to give. A married couple on different watches hardly see each other, and it shows.
/// </summary>
public sealed partial class GameState
{
    void DailyFriendships()
    {
        // Anyone gone takes their friendship with them.
        foreach (var s in Staff)
        {
            if (s.Friend is int f && StaffById(f) == null) { s.Friend = null; s.Courting = false; }
            if (s.Spouse is int sp && StaffById(sp) == null) s.Spouse = null;
        }
        foreach (var s in Staff)
        {
            if (s.Friend is not int fid || StaffById(fid) is not { } f) continue;
            if (Together(s, f)) s.Morale = Math.Min(1, s.Morale + Defs.FriendMorale);
            if (s.Spouse == f.Id && WatchesAvailable && s.Shift != f.Shift) s.Morale = Math.Max(0, s.Morale - Defs.MarriedApartMorale);
        }
        var dice = Rng.Seeded(Mix(Seed, Day, 0xF71E));
        NewFriendship(dice);
        Courtships(dice);
        Weddings(dice);
    }

    /// <summary>On the same watch and working in the same building (posted there, or last worked there).</summary>
    public bool Together(Staff a, Staff b) =>
        a.Shift == b.Shift && (a.Posted ?? a.LastZone) is { } za && za == (b.Posted ?? b.LastZone);

    static bool Befriendable(Staff s, double now) => !s.Leaving && !s.Arriving(now) && s.Friend == null && s.HeadOf == null;

    void NewFriendship(Rng dice)
    {
        var free = Staff.Where(s => Befriendable(s, Now)).ToList();
        if (free.Count < 2 || !dice.Chance(Defs.FriendChance * Math.Min(free.Count, 40) / 20.0)) return;
        var a = free[(int)(dice.NextDouble() * free.Count)];
        var mates = free.Where(b => b != a && Together(a, b)).ToList();
        if (mates.Count == 0) return;
        var b = mates[(int)(dice.NextDouble() * mates.Count)];
        a.Friend = b.Id;
        b.Friend = a.Id;
        a.FriendSince = b.FriendSince = Day;
        Stats.Friendships++;
        string over = Over[(int)(dice.NextDouble() * Over.Length)];
        Emit(EventKind.Info, $"{a.Name} and {b.Name} have become friends over {over}. On the same watch in the same building, both are a little happier.");
    }

    static readonly string[] Over =
    {
        "the night watch's cocoa", "a game of rounders on the lawn", "the Scottish country dancing club", "a shared billet's cold bathwater",
        "the chess club", "the madrigal society", "the crossword in the canteen", "a bicycle with a puncture on the Buckingham road",
    };

    void Courtships(Rng dice)
    {
        foreach (var a in Staff.Where(s => s.Friend is int f && s.Id < f && !s.Courting && s.Spouse == null).ToList())
        {
            if (StaffById(a.Friend!.Value) is not { } b || b.Female == a.Female || b.Spouse != null || a.Leaving || b.Leaving) continue;
            if (a.Notable != null || b.Notable != null) continue;   // the notables keep their real lives
            if (Day - a.FriendSince < Defs.CourtAfterDays || !dice.Chance(Defs.CourtChance)) continue;
            a.Courting = b.Courting = true;
            a.CourtingSince = b.CourtingSince = Day;
            Emit(EventKind.Info, $"{a.Name} and {b.Name} have been seen walking by the lake after their watch. The Park has noticed, even if they think it hasn't.");
        }
    }

    void Weddings(Rng dice)
    {
        if (Dilemmas.Count > 0) return;
        foreach (var a in Staff.Where(s => s.Courting && s.Friend is int f && s.Id < f).ToList())
        {
            if (StaffById(a.Friend!.Value) is not { } b) continue;
            if (Day - a.CourtingSince < Defs.WedAfterDays || !dice.Chance(Defs.WedChance)) continue;
            AskWedding(a, b);
            return;
        }
    }

    void AskWedding(Staff a, Staff b)
    {
        var (bride, groom) = a.Female ? (a, b) : (b, a);
        string church = Churches[(Mix(Seed, a.Id, b.Id) & 0x7fffffff) % Churches.Length];
        RaiseDilemma(new Dilemma
        {
            Service = Service.Security, StaffId = a.Id, Title = "A wedding", Tag = "wedding",
            Text = $"{groom.Name} and {bride.Name} are to be married at {church} on Saturday, and the whole Park knows it. " +
                   $"They ask for leave.",
            Context = "A wedding lifts the whole Park for a day or two. A honeymoon takes them both away for a week; " +
                      "refusing leave keeps them at their desks and sours the day for them. Once married they will want to be on the same watch.",
            Options =
            {
                new() { Label = $"Two days off and a toast in the canteen (£{Defs.WeddingToastCost})", Funds = -Defs.WeddingToastCost, Morale = Defs.WeddingMorale,
                    Action = $"wedding:{a.Id},{b.Id}:{Defs.WeddingShortDays}", Note = $"both away {Defs.WeddingShortDays} days, very happy",
                    Outcome = $"{groom.Name} and {bride.Name} are married at {church}. The canteen raises a glass of something that was nearly sherry." },
                new() { Label = $"A week's leave for a honeymoon", Morale = Defs.WeddingMorale * 1.5, Action = $"wedding:{a.Id},{b.Id}:{Defs.WeddingLongDays}",
                    Note = $"both away {Defs.WeddingLongDays} days, happier still",
                    Outcome = $"{groom.Name} and {bride.Name} are married at {church} and go off to the coast for a week." },
                new() { Label = "No leave: the war won't wait", Action = $"wedding:{a.Id},{b.Id}:0", Note = "both at work, and unhappy about it",
                    Outcome = $"{groom.Name} and {bride.Name} marry on Saturday afternoon and are back on watch on Sunday. They say nothing, pointedly." },
            },
        });
    }

    static readonly string[] Churches =
    {
        "St Mary's, Bletchley", "St Martin's, Fenny Stratford", "the Methodist chapel in Bletchley", "St Giles', Stony Stratford",
        "the register office in Newport Pagnell", "St Mary's, Shenley Church End",
    };

    void WeddingAction(string action)
    {
        // "wedding:a,b:days"
        var parts = action.Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[2], out int days)) return;
        var ids = parts[1].Split(',').Select(x => int.TryParse(x, out int i) ? i : -1).ToList();
        if (ids.Count != 2 || StaffById(ids[0]) is not { } a || StaffById(ids[1]) is not { } b) return;
        a.Spouse = b.Id;
        b.Spouse = a.Id;
        a.Courting = b.Courting = false;
        Stats.Weddings++;
        foreach (var s in new[] { a, b })
        {
            s.Morale = Math.Clamp(s.Morale + (days == 0 ? -Defs.WeddingNoLeaveMorale : days >= Defs.WeddingLongDays ? Defs.WeddingCoupleMorale * 1.5 : Defs.WeddingCoupleMorale), 0, 1);
            if (days > 0)
            {
                s.Pinned = null;
                SendOff(s, days, "wedding");
            }
        }
        // They'll want the same watch; the one with fewer on theirs moves, if the watches are on.
        if (WatchesAvailable && a.Shift != b.Shift) Emit(EventKind.Info, $"{a.Name} and {b.Name} are on different watches. Married couples who never see each other are unhappy: put them on the same watch.");
    }

    /// <summary>
    /// Someone's friendship, marriage and quarrel, each with what it is doing to them now: +1 lifting their spirits, −1 hurting
    /// them, 0 neither. For the person's panel, the Staff list and the rings in the Park.
    /// </summary>
    public List<(string text, int sign, Staff other)> Ties(Staff s)
    {
        var list = new List<(string, int, Staff)>();
        if (s.Friend is int fid && StaffById(fid) is { } f)
        {
            bool together = Together(s, f);
            if (s.Spouse == fid)
                list.Add(WatchesAvailable && s.Shift != f.Shift
                    ? ($"Married to {f.Name}, who is on another watch: they hardly see each other, and both grow unhappier every day. Put them on the same watch.", -1, f)
                    : together ? ($"Married to {f.Name}, on the same watch in the same building: both a little happier every day.", 1, f)
                    : ($"Married to {f.Name}. On the same watch, but in different buildings: post them together and both are happier.", 0, f));
            else
            {
                string who = s.Courting ? $"Walking out with {f.Name}" : $"Friends with {f.Name}";
                list.Add(together ? ($"{who}: on the same watch in the same building, both a little happier every day.", 1, f)
                                  : ($"{who}, but they work apart: on the same watch in the same building both would be happier.", 0, f));
            }
        }
        if (s.Rival is int rid && StaffById(rid) is { } r)
            list.Add(WithRival(s) ? ($"Has fallen out with {r.Name}, who is in the same room: both work slower and grow unhappier. Post one of them elsewhere.", -1, r)
                                  : ($"Has fallen out with {r.Name}. Apart, it does no harm; in the same room both work slower.", 0, r));
        return list;
    }

    /// <summary>The Park's friendships, couples and quarrels, and how many are doing good or harm right now.</summary>
    public (int friends, int friendsTogether, int couples, int couplesApart, int feuds, int feudsTogether) TieCounts()
    {
        int fr = 0, frT = 0, co = 0, coA = 0, fe = 0, feT = 0;
        foreach (var s in Staff)
        {
            if (s.Friend is int f && s.Id < f && StaffById(f) is { } o)
            {
                if (s.Spouse == f) { co++; if (WatchesAvailable && s.Shift != o.Shift) coA++; }
                else { fr++; if (Together(s, o)) frT++; }
            }
            if (s.Rival is int r && s.Id < r && StaffById(r) != null) { fe++; if (WithRival(s)) feT++; }
        }
        return (fr, frT, co, coA, fe, feT);
    }

    /// <summary>A married couple on different watches.</summary>
    public IEnumerable<(Staff a, Staff b)> CouplesApart() =>
        WatchesAvailable
            ? Staff.Where(s => s.Spouse is int sp && s.Id < sp).Select(s => (s, StaffById(s.Spouse!.Value)))
                   .Where(p => p.Item2 != null && p.s.Shift != p.Item2.Shift).Select(p => (p.s, p.Item2!))
            : Enumerable.Empty<(Staff, Staff)>();

    /// <summary>For screenshots and tests: the first man and woman who could be, made friends and courting, and the wedding asked.</summary>
    public bool DebugWedding()
    {
        var a = Staff.FirstOrDefault(s => !s.Female && !s.Leaving && s.HeadOf == null && s.Spouse == null && s.Notable == null);
        var b = Staff.FirstOrDefault(s => s.Female && !s.Leaving && s.HeadOf == null && s.Spouse == null && s.Notable == null);
        if (a == null || b == null) return false;
        a.Friend = b.Id; b.Friend = a.Id;
        a.Courting = b.Courting = true;
        AskWedding(a, b);
        return true;
    }
}
