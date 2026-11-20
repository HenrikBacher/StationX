using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// Kisses. When the same text went out twice, once in a cipher the Park could already read and once in Enigma, the
/// codebreakers had a perfect crib: a "kiss". The German dockyards and weather stations sent their routine morning
/// reports in WERFT, the dockyard hand cipher, and the same reports again in naval Enigma. Hut 8 read WERFT for no other
/// reason. Here the player can have Scarborough send the day's WERFT signals; once a hand-cipher desk has read one,
/// that day's naval keys are kissed: the crib desk writes the menu in a fraction of the time, the Bombe run is shorter,
/// and its stop is all but certain. The price is a cryptanalyst's morning at the hand-cipher desk, the desk the
/// Offizier, police and Abwehr traffic also need.
/// </summary>
public sealed partial class GameState
{
    /// <summary>The player's order: Scarborough sends the day's WERFT signals for reading.</summary>
    public bool ReadWerft { get; set; }
    /// <summary>Naval keys kissed, as "Net:day".</summary>
    public HashSet<string> KissedKeys { get; set; } = new();
    /// <summary>The day the WERFT count is for, and how many of that day's signals have been sent on.</summary>
    public int WerftDay { get; set; } = -1;
    public int WerftSent { get; set; }

    public bool Kissed(Net net, int day) => KissedKeys.Contains(KeyId(net, day));

    /// <summary>The keys a WERFT reading kisses: the naval Enigma keys on the key board.</summary>
    public IEnumerable<Net> KissTargets() => KeyBoardNets().Where(n => Defs.NetOf(n).Naval);

    /// <summary>From Operation Claymore, while a naval key is on the board.</summary>
    public bool KissesOpen => Campaign.KissesFrom >= 0 && ActIndex >= Campaign.KissesFrom && KissTargets().Any();

    /// <summary>Turns the WERFT reading on or off. Off, any WERFT still on the floor is filed unread.</summary>
    public void SetReadWerft(bool on) => ReadWerft = on;

    /// <summary>Why WERFT can't be read now, or null.</summary>
    public string? WerftProblem() =>
        !KissesOpen ? "Not on the air yet"
        : !HasStation(StationType.HandCipher) ? "Needs a hand-cipher desk"
        : null;

    /// <summary>When the day's WERFT signals go out: the dockyards' and weather stations' morning reports.</summary>
    public double[] WerftTimes(int day)
    {
        var r = new Random(Mix(Seed, day, 0x3E2F7));
        return Enumerable.Range(0, Defs.WerftPerDay)
            .Select(_ => day * Defs.MinutesPerDay + (Defs.WerftFromHour + r.NextDouble() * Defs.WerftSpreadHours) * 60)
            .OrderBy(t => t).ToArray();
    }

    /// <summary>Kisses still to be had from a WERFT message: only on its own day, for a naval key not yet kissed or broken.</summary>
    public bool KissWanted(Message m) =>
        m.Day == Day && KissTargets().Any(n => !Kissed(n, m.Day) && !KeyBroken(n, m.Day));

    /// <summary>A kiss is only any good for the day's key: WERFT is read by noon of its day, ahead of requests' paper.</summary>
    double KissDue(Message m) => m.Day * Defs.MinutesPerDay + 12 * 60;

    /// <summary>Keys kissed today, for the key board and tests.</summary>
    public int KissesToday => KissTargets().Count(n => Kissed(n, Day));

    /// <summary>WERFT comes in with Scarborough's naval traffic, by rider or over its teleprinter line.</summary>
    void TickKisses()
    {
        if (!ReadWerft) return;
        if (WerftDay != Day) { WerftDay = Day; WerftSent = 0; }
        if (WerftSent >= Defs.WerftPerDay || !KissesOpen) return;
        var times = WerftTimes(Day);
        while (WerftSent < times.Length)
        {
            double sent = times[WerftSent];
            if (sent + TransitMinutes(Net.Werft, sent) > Now || !SpawnWerft(sent)) break;
            WerftSent++;
        }
    }

    bool SpawnWerft(double sent)
    {
        bool line = LineWorking(Net.Werft);
        var desk = line ? TeleprinterFor(Net.Werft) : RiderDesk();
        if (desk == null) return false;
        var dice = new Random(Mix(Seed, (int)sent, 0x6A7B));
        double hour = sent % Defs.MinutesPerDay / 60;
        var m = new Message
        {
            Id = NextId++, Net = Net.Werft, Day = (int)Math.Floor(sent / Defs.MinutesPerDay), Created = sent, Since = Now,
            Loc = line ? Loc.Output : Loc.Input, LocId = desk.Id,
            Garbled = dice.NextDouble() < Defs.GarbledChance * (hour < 6 || hour >= 20 ? 1.6 : 1) * WeatherGarble * CoverGarble(Net.Werft),
        };
        Messages[m.Id] = m;
        if (line)
        {
            desk.Output.Add(m.Id);
            TeleprinterWear(desk);
        }
        else
        {
            desk.Input.Add(m.Id);
            RiderArrived(Net.Werft, desk);
        }
        return true;
    }

    /// <summary>For tests.</summary>
    public void DebugKiss(Message m) => Kiss(m);

    /// <summary>A hand-cipher desk has read a WERFT signal: the day's naval keys not yet broken are kissed.</summary>
    void Kiss(Message m)
    {
        var fresh = KissWanted(m) ? KissTargets().Where(n => !Kissed(n, m.Day) && !KeyBroken(n, m.Day)).ToList() : new List<Net>();
        FileWerft(m, read: true);
        if (fresh.Count == 0) return;
        foreach (var n in fresh)
        {
            KissedKeys.Add(KeyId(n, m.Day));
            ApplyKiss(n, m.Day);
        }
        Stats.Kisses++;
        string names = string.Join(" and ", fresh.Select(n => Defs.NetOf(n).Name));
        Emit(EventKind.Good, $"A kiss: this morning's WERFT signal went out again in {names}. Hut 8 has a perfect crib for today's key: " +
                             "the menu in a third of the time, a shorter Bombe run, and a stop that holds.");
    }

    /// <summary>A key attempt already under way takes the kiss at once: the rest of the menu or the run is cut short.</summary>
    void ApplyKiss(Net net, int day)
    {
        foreach (var m in Messages.Values.Where(x => x.Carrier && x.Net == net && x.Day == day))
        {
            m.CribQuality = Math.Max(m.CribQuality, Defs.KissCribQuality);
            if (m.Loc == Loc.Processing && StationById(m.LocId) is { } st && st.Current == m.Id)
            {
                double mult = st.Type switch
                {
                    StationType.Crib => Defs.KissCribMult,
                    StationType.Bombe or StationType.Bombe4 => Defs.KissBombeMult,
                    _ => 1,
                };
                st.WorkTotal = st.WorkDone + (st.WorkTotal - st.WorkDone) * mult;
            }
            foreach (var j in RemoteJobs.Where(j => j.Msg == m.Id))
                j.Done = Now + Math.Max(0, j.Done - Now) * Defs.KissBombeMult;
        }
    }

    /// <summary>A WERFT signal leaves the floor: read (kept for the intercept reader) or filed unread.</summary>
    void FileWerft(Message m, bool read)
    {
        m.Loc = Loc.Done;
        Messages.Remove(m.Id);
        if (!read) return;
        Archive.Add(new ArchivedMsg { Id = m.Id, Net = m.Net, Day = m.Day, Created = m.Created, Garbled = m.Garbled });
        if (Archive.Count > ArchiveSize) Archive.RemoveRange(0, Archive.Count - ArchiveSize);
    }

    /// <summary>WERFT waiting in a tray once it can no longer kiss anything (or nobody wants it read) is filed unread.</summary>
    void FileUnwantedWerft()
    {
        foreach (var m in Messages.Values.Where(m => m.Net == Net.Werft && !m.Reserved && m.Loc is Loc.Input or Loc.Output).ToList())
        {
            if (ReadWerft && KissWanted(m)) continue;
            var st = StationById(m.LocId);
            st?.Input.Remove(m.Id);
            st?.Output.Remove(m.Id);
            FileWerft(m, read: false);
        }
    }
}
