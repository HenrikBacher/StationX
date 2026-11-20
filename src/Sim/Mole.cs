using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// A mole. John Cairncross, a Cambridge man in Hut 3's air section in 1942–43, passed Luftwaffe decrypts to the Russians
/// (they were used at Kursk); had the Germans got into the Soviet channels, ULTRA would have gone with them. Here: from
/// the Blackout one of the linguists (own dice) starts passing what they see on, and German air and army suspicion creeps
/// up with nothing we did to explain it. After a week MI5 brings three names, the mole among them, each with what is
/// known about them: dismiss one (the wrong one costs morale and standing, and the leak goes on, to be looked at again),
/// or move all three out of their building, which stops it without ever knowing who.
/// </summary>
public sealed partial class GameState
{
    /// <summary>0 none yet; 1 leaking; 2 caught; 3 contained (the suspects moved); −1 the time for it has passed.</summary>
    public int MoleState { get; set; }
    public int MoleId { get; set; }
    public int MoleSince { get; set; }
    /// <summary>Innocent people already dismissed as the mole, so they aren't named again.</summary>
    public List<int> MoleCleared { get; set; } = new();

    static readonly string[] MoleClues =
    {
        "read modern languages at Trinity, Cambridge, and was a communist there, as half of Trinity was",
        "has asked twice to see Luftwaffe reports that have nothing to do with their own desk",
        "takes the London train on days off with a briefcase, and comes back without it",
        "was seen in Regent's Park with a man the Special Branch knows from the Soviet trade delegation",
        "copies out decrypts in longhand 'to practise the German', and takes the notes home",
    };

    static readonly string[] HerringClues =
    {
        "grumbles about the pay to anyone who'll listen in the Eight Bells",
        "keeps a diary, which is against the rules",
        "has a German mother, interned in 1940 and released",
        "was reprimanded last month for leaving a hut door unlocked",
        "writes every week to a cousin in neutral Lisbon",
        "plays chess by post with a man in Dublin",
        "was a pacifist before the war and signed the Peace Pledge",
        "borrows money from everyone in the hut and seldom pays it back",
    };

    bool MoleWindow => Mode == GameMode.Campaign && ActIndex >= Campaign.SharkFrom && ActIndex < Campaign.IndexOf("D-Day");

    void DailyMole()
    {
        if (MoleState == 0)
        {
            if (!MoleWindow) return;
            var pool = MoleCandidates(null).ToList();
            if (pool.Count < 3) return;
            var r = Rng.Seeded(Mix(Seed, Day, 0x30AE));
            MoleId = pool[r.Range(0, pool.Count)].Id;
            MoleState = 1;
            MoleSince = Day;
            return;
        }
        if (MoleState != 1) return;
        if (StaffById(MoleId) is not { Leaving: false } mole) { MoleState = 2; return; }   // gone of their own accord
        if (!MoleWindow) { MoleState = -1; return; }
        if (mole.Away && mole.OffReason != null) return;   // on leave or a course: sees nothing
        AddSuspicion(Service.Luftwaffe, Defs.MoleLeakAir);
        AddSuspicion(Service.Heer, Defs.MoleLeakArmy);
        Stats.MoleLeakDays++;
        if (Day - MoleSince >= Defs.MoleNoticeDays && Dilemmas.Count == 0 && !Dilemmas.Any(d => d.Tag == "mole")) RaiseMole(mole);
    }

    /// <summary>Linguists who might be (or be suspected of being) the mole: not heads, not notables, not cleared.</summary>
    IEnumerable<Staff> MoleCandidates(string? zone) =>
        Staff.Where(s => s.Role == Role.Linguist && !s.Leaving && s.Notable == null && s.HeadOf == null && !s.American &&
                         !MoleCleared.Contains(s.Id) && (zone == null || s.Posted == zone));

    /// <summary>For screenshots and tests: start the leak now and bring MI5's names at once.</summary>
    public bool DebugMole()
    {
        var pool = MoleCandidates(null).ToList();
        if (pool.Count < 3) return false;
        MoleId = pool[0].Id;
        MoleState = 1;
        MoleSince = Day - Defs.MoleNoticeDays;
        RaiseMole(pool[0]);
        return true;
    }

    void RaiseMole(Staff mole)
    {
        var r = Rng.Seeded(Mix(Seed, Day, 0x30AF));
        // Two others from the same building if there are, else anywhere.
        var others = MoleCandidates(mole.Posted).Where(s => s.Id != mole.Id).ToList();
        if (others.Count < 2) others = MoleCandidates(null).Where(s => s.Id != mole.Id).ToList();
        if (others.Count < 2)
        {
            // Too few left to choose between: MI5 finds them itself.
            Fire(mole.Id);
            MoleState = 2;
            Emit(EventKind.Info, $"MI5 has found the leak itself: {mole.Name} is quietly dismissed. Air and army suspicion will settle.");
            return;
        }
        var suspects = new List<Staff> { mole };
        while (suspects.Count < 3)
        {
            var o = others[r.Range(0, others.Count)];
            others.Remove(o);
            suspects.Add(o);
        }
        // Shuffled, so the mole isn't always first.
        for (int i = suspects.Count - 1; i > 0; i--)
        {
            int j = r.Range(0, i + 1);
            (suspects[i], suspects[j]) = (suspects[j], suspects[i]);
        }
        string where = mole.Posted is { } z && Grid.Zone(z) is { } zz ? zz.Name : "the Park";
        var herrings = HerringClues.OrderBy(_ => r.Next()).ToList();
        int h = 0;
        string About(Staff s)
        {
            if (s.Id == mole.Id)
            {
                int a = r.Range(0, MoleClues.Length), b = (a + 1 + r.Range(0, MoleClues.Length - 1)) % MoleClues.Length;
                return $"{s.Name} {MoleClues[a]}, and {MoleClues[b]}.";
            }
            return r.Chance(0.5) ? $"{s.Name} {herrings[h++]}, and {herrings[h++]}." : $"{s.Name} {herrings[h++]}.";
        }
        var notes = string.Join("\n", suspects.Select(s => "· " + About(s)));
        var d = new Dilemma
        {
            Service = Service.Luftwaffe, Title = "A leak", Tag = "mole",
            Text = $"German air and army suspicion has been rising for {Day - MoleSince} days with nothing we have done to explain it. MI5 believes " +
                   $"someone at the Park is passing ULTRA on, most likely to the Russians, and that the Germans may be reading the Russians. " +
                   $"Three linguists who see the material in {where} are on their list.",
            Context = notes + "\n\nDismissing the wrong one would be an injustice and the leak would go on. Moving all three out of their building stops it " +
                      "for certain, but costs the desks their hands and we would never know.",
        };
        foreach (var s in suspects)
            d.Options.Add(new DilemmaOption { Label = $"Dismiss {s.Name}", Action = $"mole:{s.Id}",
                Outcome = $"{s.Name} is called to the Director's office and does not come back to the hut." });
        d.Options.Insert(1, new DilemmaOption { Label = "Move all three out of their building", Morale = -0.01, Action = $"mole-move:{string.Join(",", suspects.Select(s => s.Id))}",
            Outcome = "All three are posted elsewhere at a day's notice, with no reason given. The hut is short-handed and puzzled." });
        RaiseDilemma(d);
    }

    void MoleAction(string action)
    {
        if (action.StartsWith("mole-move:"))
        {
            var ids = action["mole-move:".Length..].Split(',').Select(int.Parse).ToList();
            foreach (var id in ids)
                if (StaffById(id) is { } s)
                {
                    var from = s.Posted;
                    s.Posted = PostsFor(s.Role).Select(z => z.Id).FirstOrDefault(z => z != from) ?? from;
                    s.Pinned = null;
                    s.Morale = Math.Max(0, s.Morale - Defs.MoleMovedMorale);
                }
            if (ids.Contains(MoleId)) MoleState = 3;
            Emit(EventKind.Info, "The leak stops, as far as anyone can tell. Nobody at the Park will ever know who it was.");
            return;
        }
        if (!int.TryParse(action["mole:".Length..], out int who)) return;
        Fire(who);
        if (who == MoleId)
        {
            MoleState = 2;
            Stats.MoleCaught = true;
            AdjustRep(Defs.MoleCaughtRep, "Security");
            AddSuspicion(Service.Luftwaffe, -Defs.MoleRelief);
            AddSuspicion(Service.Heer, -Defs.MoleRelief);
            Emit(EventKind.Good, "MI5 is satisfied: the leak has stopped. Moscow will have to make do with what Churchill chooses to tell Stalin.");
        }
        else
        {
            MoleCleared.Add(who);
            MoleSince = Day;
            ChangeMorale(-Defs.MoleWrongMorale);
            AdjustRep(-Defs.MoleWrongRep, "Security");
            Emit(EventKind.Bad, "The suspicion goes on rising after the dismissal. The wrong person was sent away, and the whole Park knows it.");
        }
    }
}
