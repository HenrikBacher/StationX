using System;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// RAF fitters, from the Bombe research. The Wrens ran the Bombes; RAF technicians kept them running, in Hut 11 and at
/// the outstations. A fitter on watch in a machine's building walks over to a breakdown and mends it in a fraction of the
/// time the outside fitters take, for the price of the parts; with nothing broken, they service worn machines whenever
/// one stands idle. Teleprinters, Colossus and the Tunny machines stay the Post Office's.
/// </summary>
public sealed partial class GameState
{
    public bool FittersOpen => Researched.Contains(ResearchId.Bombe);

    /// <summary>Whether a fitter of ours can come to this machine: one on watch, in the Park, posted to its building.</summary>
    bool FitterCanCome(Station st)
    {
        if (!Defs.FitterServes(st.Type)) return false;
        string? zone = Grid.ZoneAt(st.X, st.Y)?.Id;
        foreach (var s in Staff)
            if (s.Role == Role.Fitter && !s.Away && !s.Leaving && OnDuty(s) && (s.Posted == null || s.Posted == zone)) return true;
        return false;
    }

    bool _anyFitter;

    /// <summary>For tests and screenshots: break a machine down now.</summary>
    public void DebugBreakDown(int stationId)
    {
        if (StationById(stationId) is { } st) BreakDown(st);
    }

    void TickFitters()
    {
        _anyFitter = false;
        foreach (var s in Staff) if (s.Role == Role.Fitter) { _anyFitter = true; break; }
        if (!_anyFitter) return;
        foreach (var s in Staff)
        {
            if (s.Role != Role.Fitter || s.Away || s.Leaving || !OnDuty(s)) continue;
            if (s.Task is { Kind: TaskKind.Repair } t) { CarryOnMending(s, t); continue; }
            // Free: nothing on, or only sitting about for want of work.
            if (s.Task != null && s.Task is not { Kind: TaskKind.Rest, Loafing: true }) continue;
            if (FitterJob(s) is not { } job) continue;
            var (st, repair) = job;
            if (!SendTo(s, st.UseTile)) continue;
            s.Task = new StaffTask { Kind = TaskKind.Repair, Dest = st.Id, Phase = repair ? 0 : 2 };
            st.FitterId = s.Id;
            if (s.Path.Count == 0) CarryOnMending(s, s.Task);
        }
    }

    /// <summary>The next job in their building: a breakdown first, the nearest; else the most worn idle machine.</summary>
    (Station st, bool repair)? FitterJob(Staff s)
    {
        Station? broken = null, worn = null;
        int bestD = int.MaxValue;
        double bestWear = Defs.FitterServiceAt;
        foreach (var st in Stations)
        {
            if (!Defs.FitterServes(st.Type) || st.PendingRemoval || !MayWorkAt(s, st.UseTile)) continue;
            // A fitter who went off or left without finishing frees the machine for the next.
            if (st.FitterId is int f && !(StaffById(f) is { Task: { Kind: TaskKind.Repair } ft } && ft.Dest == st.Id)) st.FitterId = null;
            if (st.FitterId != null) continue;
            if (st.IsDown(Now))
            {
                if (st.Servicing) continue;
                int d = Manhattan(st.UseTile, s.TilePos);
                if (d < bestD) { bestD = d; broken = st; }
            }
            else if (broken == null && st.Wear >= bestWear && st.Current == null && st.Input.Count == 0)
            {
                bestWear = st.Wear;
                worn = st;
            }
        }
        return broken != null ? (broken, true) : worn != null ? (worn, false) : null;
    }

    /// <summary>Phase 0 walking to a breakdown, 1 mending it; 2 walking to service a machine, 3 servicing it.</summary>
    void CarryOnMending(Staff s, StaffTask t)
    {
        var st = StationById(t.Dest);
        if (st == null) { s.Task = null; return; }
        if (s.Path.Count > 0) return;
        switch (t.Phase)
        {
            case 0:
                t.Phase = 1;
                if (st.IsDown(Now)) st.DownUntil = Math.Min(st.DownUntil, Now + Defs.RepairMinutesFor(st.Type) * Defs.FitterRepairShare);
                break;
            case 2:
                // Someone set it going again while they walked over: leave it for later.
                if (st.Current != null || st.Input.Count > 0 || st.IsDown(Now)) { EndMending(s, st); return; }
                t.Phase = 3;
                st.Servicing = true;
                st.QuietService = true;
                st.Wear = 0;
                st.DownUntil = Now + Defs.ServiceMinutes * Defs.FitterServiceShare;
                Stats.FitterServices++;
                break;
            default:
                if (!st.IsDown(Now)) EndMending(s, st);
                break;
        }
    }

    void EndMending(Staff s, Station st)
    {
        if (st.FitterId == s.Id) st.FitterId = null;
        s.Task = null;
    }

    /// <summary>A fitter going off watch leaves the job to the next watch (a repair already begun runs on to its end).</summary>
    void FitterOffWatch(Staff s)
    {
        if (s.Task is { Kind: TaskKind.Repair } t && StationById(t.Dest) is { } st) EndMending(s, st);
        else s.Task = null;
    }
}
