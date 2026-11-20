using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// JN-25, the Imperial Japanese Navy's fleet code, from the Blackout (February 1942). Five-figure code groups under an
/// additive: a hand-cipher desk strips the additive, then the Japanese desk looks the groups up in the book recovered so
/// far and puts them into English. Only linguists who've been to the Bedford Japanese course can work that desk, so
/// the Park has to send people away to learn it, or hire the few who already have. Tokyo works while Britain sleeps:
/// the traffic comes in overnight, which gives the night watch a job of its own.
/// </summary>
public sealed partial class GameState
{
    public bool JapanOpen => Campaign.JapanFrom >= 0 && ActIndex >= Campaign.JapanFrom;

    /// <summary>Who can work a desk: the Japanese desk wants a Japanese reader, every other desk its role.</summary>
    public static bool CanOperate(Staff s, Station st) => st.Type != StationType.Japanese || s.Has(Trait.Japanese);

    /// <summary>People on the staff who read Japanese (including those still at Bedford).</summary>
    public int JapaneseReaders => Staff.Count(s => s.Has(Trait.Japanese) && !s.Leaving);

    /// <summary>JN-25 is on the go: a request for it offered or under way.</summary>
    public bool JapanWanted => Contracts.Any(c => c.Net == Net.Jn25 && c.State is ContractState.Offered or ContractState.Active);

    /// <summary>Why this person can't go on the Bedford course now, or null if they can.</summary>
    public string? CantLearnJapanese(Staff s)
    {
        if (!JapanOpen) return "Not needed yet.";
        if (s.Role != Role.Linguist) return "The Bedford course takes linguists.";
        if (s.Has(Trait.Japanese)) return "They already read Japanese.";
        if (s.Leaving || s.HeadOf != null) return "Not while they're leaving or heading a hut.";
        if (s.Arriving(Now) || s.IsSick(Now)) return "Not while they're away from the Park.";
        if (Funds < Defs.JapaneseCourseCost) return $"The course costs £{Defs.JapaneseCourseCost}.";
        return null;
    }

    /// <summary>Sends a linguist to Bedford to learn naval Japanese: away five days, then they can work the Japanese desk.</summary>
    public bool LearnJapanese(int staffId)
    {
        var s = StaffById(staffId);
        if (s == null || CantLearnJapanese(s) != null) return false;
        Funds -= Defs.JapaneseCourseCost;
        s.Traits.Add(Trait.Japanese);
        s.Pinned = null;
        SendOff(s, Defs.JapaneseCourseHours / 24.0, "course");
        Emit(EventKind.Info, $"{s.Name} goes to Bedford for Captain Tuck's Japanese course (£{Defs.JapaneseCourseCost}), back in about " +
                             $"{Defs.JapaneseCourseHours / 24:0} days reading naval Japanese.");
        return true;
    }

    /// <summary>How much of the current JN-25 code book is recovered (0..1): the Japanese desk works faster the more is known.</summary>
    public double BookRecovered { get; set; } = Defs.BookStart;
    /// <summary>New books brought in since JN-25B (an index into <see cref="Defs.JapaneseBooks"/>, plus one).</summary>
    public int BookChanges { get; set; }
    public string BookName => BookChanges == 0 ? "JN-25B" : Defs.JapaneseBooks[BookChanges - 1].name;

    /// <summary>The Japanese desk's time for a message, by how much of the book is known.</summary>
    public double BookFactor => Defs.BookSlowest - (Defs.BookSlowest - Defs.BookFastest) * BookRecovered;

    /// <summary>Every group read fills in the book a little: each decrypt recovers a share of what's still unknown.</summary>
    void RecoverBook() => BookRecovered = Math.Min(1, BookRecovered + Defs.BookPerDecrypt * (1 - BookRecovered));

    public void DebugRecoverBook() => RecoverBook();
    /// <summary>For tests: Tokyo's next book comes in now.</summary>
    public void DebugBookChange()
    {
        if (BookChanges >= Defs.JapaneseBooks.Length) return;
        var (from, _) = Defs.JapaneseBooks[BookChanges];
        ActDate = ActDate.AddDays(Math.Max(0, from.DayNumber - Date.DayNumber));
        DailyBook();
    }

    /// <summary>At midnight: on the historical dates Tokyo brings in a new book, and the recovery starts over (a little carries).</summary>
    void DailyBook()
    {
        while (BookChanges < Defs.JapaneseBooks.Length && Date >= Defs.JapaneseBooks[BookChanges].from)
        {
            var (_, name) = Defs.JapaneseBooks[BookChanges++];
            double before = BookRecovered;
            BookRecovered = Defs.BookStart * Defs.BookKept + before * Defs.BookKept;
            if (JapanOpen)
                Emit(EventKind.Bad, $"Tokyo has brought in a new code book, {name}. Of the {before * 100:0}% of the old book we had recovered, only {BookRecovered * 100:0}% " +
                                    "carries over: the Japanese desk is slow again until the new groups are read out of the traffic.");
        }
    }

    /// <summary>A linguist candidate may already have been through the Bedford course (own dice: the main game's rolls don't move).</summary>
    void MaybeJapanese(Staff p)
    {
        if (p.Role != Role.Linguist || !JapanOpen || p.Has(Trait.Japanese)) return;
        if (Rng.Seeded(Mix(Seed, p.Id, 0x7A9A)).Chance(Defs.JapaneseCandidateChance)) p.Traits.Add(Trait.Japanese);
    }

    /// <summary>
    /// Moves a JN-25 request's traffic into the Japanese working day: a time anywhere in a day maps, in order, onto
    /// 22:00 to 08:00 by British clocks. Never before the request opens, nor in its last half day.
    /// </summary>
    void NightTraffic(Contract c)
    {
        if (c.Net != Net.Jn25 || c.Flash) return;
        double day = Defs.MinutesPerDay;
        for (int i = 0; i < c.SpawnTimes.Count; i++)
        {
            double t = c.SpawnTimes[i];
            double start = Math.Floor(t / day) * day;
            double night = start + Defs.JapanNightFrom * 60 + (t - start) / day * Defs.JapanNightHours * 60;
            c.SpawnTimes[i] = Math.Clamp(night, Now + 20, Math.Max(Now + 20, c.Due - day / 2));
        }
        c.SpawnTimes.Sort();
    }
}
