using System;

namespace StationX.Sim;

/// <summary>
/// Back traffic. A day whose key wasn't broken left its traffic unread, and the huts went back to it when they could:
/// old keys were broken for the record, for cribs and for the history of what the enemy had been doing. Here each
/// network's unbroken day leaves some unread (up to a cap), and in quiet hours cryptanalysts with nothing to do, and
/// Wrens with a Bombe standing free, work it off without being asked: breakthroughs for each message. It never takes
/// anyone from real work (the idle get up as soon as it comes). Balance: 1 a message at 90/120 minutes put Normal at 26
/// (23 without); with reputation every 8 messages, 36.
/// </summary>
public sealed partial class GameState
{
    /// <summary>Messages of old traffic still unread, and the share of the next one done.</summary>
    public int BackTraffic { get; set; }
    public double BackProgress { get; set; }
    /// <summary>Whether anyone was working it at the last look, for the view.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool BackWorking { get; private set; }

    /// <summary>At midnight: yesterday's unbroken keys leave their traffic unread.</summary>
    void DailyBackTraffic()
    {
        if (!Researched.Contains(ResearchId.Bombe)) return;
        int day = Day - 1;
        foreach (var n in CoveredNets())
        {
            var nd = Defs.NetOf(n);
            if (nd.Family == Family.Hand || n == Net.Werft) continue;
            if (KeyBroken(n, day)) continue;
            BackTraffic = Math.Min(Defs.BackMax, BackTraffic + Defs.BackPerMissedKey);
        }
    }

    /// <summary>Every few minutes: the idle work it off, a cryptanalyst at a table and a Wren with a free Bombe.</summary>
    void TickBackTraffic()
    {
        // Every BackStep minutes by the clock alone, so a loaded game keeps the same rhythm.
        if (Math.Floor(Now / Defs.BackStep) == Math.Floor((Now - Defs.Tick) / Defs.BackStep)) return;
        BackWorking = false;
        if (BackTraffic <= 0) return;
        int crypt = 0, wrens = 0, bombes = 0;
        foreach (var s in Staff)
        {
            if (s.Away || s.Leaving || !(s.Task == null || s.Task is { Kind: TaskKind.Rest, Loafing: true }) || !OnDuty(s)) continue;
            if (s.Role == Role.Cryptanalyst) crypt++;
            else if (s.Role == Role.Wren) wrens++;
        }
        if (crypt == 0 || wrens == 0) return;
        foreach (var st in Stations)
            if (st.Type is StationType.Bombe or StationType.Bombe4 && !st.PendingRemoval && st.Current == null && st.Input.Count == 0 && !st.IsDown(Now))
                bombes++;
        double rate = Math.Min(crypt / Defs.BackCryptMinutes, Math.Min(wrens, bombes) / Defs.BackBombeMinutes);
        if (rate <= 0) return;
        BackWorking = true;
        BackProgress += rate * Defs.BackStep;
        while (BackProgress >= 1 && BackTraffic > 0)
        {
            BackProgress -= 1;
            BackTraffic--;
            Points += Defs.BackPoints;
            Stats.BackRead++;
            if (Stats.BackRead % Defs.BackReportEvery == 0)
                Emit(EventKind.Info, $"Old traffic read for the record: {Stats.BackRead} messages so far, {Defs.BackPoints * Defs.BackReportEvery} more breakthroughs.");
        }
        if (BackTraffic == 0) BackProgress = 0;
    }
}
