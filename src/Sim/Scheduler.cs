using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>Matches waiting work to free stations and free staff, like a foreman.</summary>
public sealed partial class GameState
{
    static int Manhattan((int x, int y) a, (int x, int y) b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

    bool _restExists;

    void Schedule()
    {
        _restExists = Stations.Any(s => s.IsRest && RestOpen(s));
        // 1. Every station with work waiting needs an operator of the right role.
        //    Someone pinned to the station goes first; pinned people don't work anywhere else.
        foreach (var st in Stations)
        {
            var role = st.Def.Operator;
            if (role == null || st.OperatorId != null || st.Obsolete) continue;
            if (st.Input.Count == 0 && st.Current == null) continue;
            if (st.IsMachine && st.Current != null) continue;
            if (st.IsDown(Now)) continue;
            var who = FindIdle(st.UseTile, s => s.Pinned == st.Id)
                      ?? FindIdle(st.UseTile, s => s.Role == role.Value && s.Pinned == null);
            if (who == null) continue;
            if (!SendTo(who, st.UseTile)) continue;
            who.Task = new StaffTask { Kind = TaskKind.Operate, Dest = st.Id };
            st.OperatorId = who.Id;
            st.OperatorPresent = who.Path.Count == 0;
        }

        // 2. Runners holding messages with nowhere to go.
        foreach (var s in Staff)
        {
            if (s.Task is not { Kind: TaskKind.Carry, Phase: 1, Dest: -1 } t) continue;
            var next = Messages[t.Msgs[0]].Next;
            Station? dest = next is StationType n ? BestDestination(n, s.TilePos, t.Msgs.Count) : null;
            if (dest != null && SendTo(s, dest.UseTile))
            {
                t.Dest = dest.Id;
                dest.ReservedInputs += t.Msgs.Count;
                if (s.Path.Count == 0) OnArrive(s);
                continue;
            }
            if (next == null || t.Msgs.Any(id => Messages[id].Next != next))
            {
                // Put them down in the nearest tray to wait.
                var tray = Stations.Where(x => x.Def.Operator != null)
                    .OrderBy(x => Manhattan(x.UseTile, s.TilePos)).FirstOrDefault();
                if (tray != null && SendTo(s, tray.UseTile))
                {
                    t.Phase = 2;
                    t.Dest = tray.Id;
                    if (s.Path.Count == 0) OnArrive(s);
                }
            }
        }

        // 3. Messages sitting in out-trays that need carrying onwards.
        var waiting = Messages.Values
            .Where(m => m.Loc == Loc.Output && m.Next != null && !m.Reserved)
            .OrderBy(m => ContractById(m.ContractId)?.Due ?? double.MaxValue)
            .ThenBy(m => m.Id)
            .ToList();
        if (waiting.Count == 0) return;
        int idle = Staff.Count(s => s.Idle);
        foreach (var m in waiting)
        {
            if (idle == 0) break;
            if (m.Reserved) continue;
            var src = StationById(m.LocId);
            if (src == null) continue;
            var dest = BestDestination(m.Next!.Value, src.UseTile, 1);
            if (dest == null) continue;
            if (src.Type != StationType.Tube && TubeVia(src, dest) is { } tube) dest = tube;

            // Messengers first, then clerks, then anyone whose own desks are quiet.
            var runner = FindIdle(src.UseTile, s => s.Role == Role.Messenger)
                         ?? FindIdle(src.UseTile, s => s.Role == Role.Clerk && s.Pinned == null)
                         ?? FindIdle(src.UseTile, s => !RoleNeeded(s.Role) && s.Pinned == null);
            if (runner == null) continue;
            if (!SendTo(runner, src.UseTile)) continue;

            var batch = src.Output
                .Select(id => Messages[id])
                .Where(x => !x.Reserved && x.Next == m.Next)
                .Take(Math.Min(Defs.CarryBatch, dest.FreeInput))
                .Select(x => x.Id)
                .ToList();
            foreach (var id in batch) Messages[id].Reserved = true;
            dest.ReservedInputs += batch.Count;
            runner.Task = new StaffTask { Kind = TaskKind.Carry, Source = src.Id, Dest = dest.Id, Msgs = batch };
            if (runner.Path.Count == 0) OnArrive(runner);
            idle--;
        }
    }

    /// <summary>A specialist shouldn't wander off to run errands while their machines have work.</summary>
    bool RoleNeeded(Role r) =>
        Stations.Any(st => st.Def.Operator == r && st.OperatorId == null && !st.Obsolete &&
                           (st.Input.Count > 0 || st.Current != null || st.ReservedInputs > 0));

    Station? BestDestination(StationType next, (int x, int y) from, int need)
    {
        Station? best = null;
        int bestScore = int.MaxValue;
        foreach (var st in Stations)
        {
            bool takes = st.Accepts(next) ||
                         (st.Type == StationType.OutstationLink && !st.PendingRemoval && OutstationTakes(next));
            if (!takes || st.FreeInput < Math.Min(need, st.Def.InputCap)) continue;
            if (st.IsDown(Now) && Stations.Any(o => o != st && o.Type == st.Type && !o.IsDown(Now))) continue;
            if (!CanBeStaffed(st)) continue;
            // Prefer the closest, but spread work across stations with shorter queues.
            int score = Manhattan(st.UseTile, from) + (st.Input.Count + st.ReservedInputs) * 6;
            if (score < bestScore) { bestScore = score; best = st; }
        }
        return best;
    }

    /// <summary>False when everyone of the right role is pinned to other stations.</summary>
    public bool CanBeStaffed(Station st)
    {
        if (st.Def.Operator is not Role role) return true;
        foreach (var s in Staff)
            if (!s.Leaving && (s.Pinned == st.Id || (s.Pinned == null && s.Role == role))) return true;
        return false;
    }

    /// <summary>A pneumatic tube station worth sending the message through, if the walk is long.</summary>
    Station? TubeVia(Station src, Station dest)
    {
        Station? a = null, b = null;
        int da = int.MaxValue, db = int.MaxValue;
        foreach (var t in Stations)
        {
            if (t.Type != StationType.Tube || t.PendingRemoval) continue;
            int x = Manhattan(t.UseTile, src.UseTile);
            if (x < da && t.FreeInput > 0) { da = x; a = t; }
            int y = Manhattan(t.UseTile, dest.UseTile);
            if (y < db) { db = y; b = t; }
        }
        if (a == null || b == null || a == b) return null;
        int direct = Manhattan(src.UseTile, dest.UseTile);
        return da + db + 3 < direct * 0.7 ? a : null;
    }

    Staff? FindIdle((int x, int y) near, Func<Staff, bool> filter)
    {
        Staff? best = null;
        int bestD = int.MaxValue;
        foreach (var s in Staff)
        {
            if (!s.Idle || !filter(s)) continue;
            if (s.Tired && _restExists) continue;
            int d = Manhattan(s.TilePos, near);
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }

    void OnDropOff(Staff s, StaffTask t)
    {
        var tray = StationById(t.Dest);
        if (tray == null) { t.Phase = 1; t.Dest = -1; return; }
        foreach (var id in t.Msgs)
        {
            var m = Messages[id];
            m.Reserved = false;
            m.Loc = Loc.Output;
            m.LocId = tray.Id;
            m.Since = Now;
            tray.Output.Add(id);
        }
        s.Task = null;
    }

    void FinishRemovals()
    {
        for (int i = Stations.Count - 1; i >= 0; i--)
        {
            var st = Stations[i];
            if (!st.PendingRemoval) continue;
            if (st.Input.Count > 0 || st.Output.Count > 0 || st.Current != null || st.ReservedInputs > 0) continue;
            if (Staff.Any(s => s.Task is { } t && (t.Dest == st.Id || t.Source == st.Id))) continue;
            ReleaseOperator(st);
            foreach (var p in Staff.Where(p => p.Pinned == st.Id)) p.Pinned = null;
            Stations.RemoveAt(i);
            Grid.RebuildOccupancy(Stations);
            Funds += (int)(st.Def.Cost * Defs.SellRefund);
            Emit(EventKind.Info, $"{st.Def.Name} removed. Refund £{(int)(st.Def.Cost * Defs.SellRefund)}.");
        }
    }
}
