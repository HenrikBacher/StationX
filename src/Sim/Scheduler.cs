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
        // Heads of huts walk back to their hut when they've nothing else on.
        foreach (var h in Staff)
            if (h.HeadOf != null && h.Idle && h.Path.Count == 0 && Grid.Zone(h.HeadOf) is { } hz && Grid.ZoneAt(h.TilePos.x, h.TilePos.y) != hz)
                SendTo(h, hz.InsideDoor);
        // 1. Every station with work waiting needs an operator of the right role.
        //    Someone pinned to the station goes first; pinned people don't work anywhere else.
        foreach (var st in Stations)
        {
            var role = st.Def.Operator;
            if (role == null || st.OperatorId != null || st.Obsolete) continue;
            if (st.Input.Count == 0 && st.Current == null) continue;
            if (st.IsMachine && st.Current != null) continue;
            if (st.IsDown(Now)) continue;
            // The nearest who can actually get there: someone cut off from it mustn't block everyone else.
            Staff? who = null;
            var cantReach = new HashSet<int>();
            for (int tries = 0; tries < 4 && who == null; tries++)
            {
                var cand = FindIdle(st.UseTile, s => s.Pinned == st.Id && !cantReach.Contains(s.Id))
                           ?? FindIdle(st.UseTile, s => s.Role == role.Value && s.Pinned == null && MayWorkAt(s, st.UseTile) && !cantReach.Contains(s.Id));
                if (cand == null) break;
                if (SendTo(cand, st.UseTile)) who = cand;
                else cantReach.Add(cand.Id);
            }
            if (who == null) continue;
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
            if (dest != null && !MayWorkAt(s, dest.UseTile)) dest = null;
            if (dest != null && SendTo(s, dest.UseTile))
            {
                t.Dest = dest.Id;
                dest.ReservedInputs += t.Msgs.Count;
                if (s.Path.Count == 0) OnArrive(s);
                continue;
            }
            // Someone posted to a building puts paper bound elsewhere down in a tray there, for a runner to take on.
            if (next == null || t.Msgs.Any(id => Messages[id].Next != next) || s.Posted != null)
            {
                // Put them down in the nearest tray to wait.
                var tray = Stations.Where(x => x.Def.Operator != null && MayWorkAt(s, x.UseTile))
                    .OrderBy(x => Manhattan(x.UseTile, s.TilePos)).FirstOrDefault();
                if (tray != null && SendTo(s, tray.UseTile))
                {
                    t.Phase = 2;
                    t.Dest = tray.Id;
                    if (s.Path.Count == 0) OnArrive(s);
                }
            }
        }

        SettleIdle();

        // 3. Messages sitting in out-trays that need carrying onwards.
        var waiting = Messages.Values
            .Where(m => m.Loc == Loc.Output && m.Next != null && !m.Reserved)
            .OrderBy(Rank)
            .ThenBy(m => ContractById(m.ContractId) is { } c ? WorkDue(c) : double.MaxValue)
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
            if (src.Type != StationType.Tube && HandOver(src, m)) continue;
            var dest = BestDestination(m.Next!.Value, src.UseTile, 1);
            if (dest == null) continue;
            if (src.Type != StationType.Tube && TubeVia(src, dest) is { } tube) dest = tube;

            // Messengers first, then clerks, then anyone whose own desks are quiet.
            // People posted to a building only carry within it.
            bool may(Staff s) => MayWorkAt(s, src.UseTile) && MayWorkAt(s, dest.UseTile);
            var runner = FindIdle(src.UseTile, s => s.Role == Role.Messenger && may(s))
                         ?? FindIdle(src.UseTile, s => s.Role == Role.Clerk && s.Pinned == null && may(s))
                         ?? FindIdle(src.UseTile, s => !RoleNeeded(s.Role) && s.Pinned == null && may(s));
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

    /// <summary>
    /// People with nothing to do don't loiter on someone's working spot, outside a door or at the gate: they go and
    /// wait by a wall in a room where their kind of work happens, so they're close when it comes.
    /// </summary>
    void SettleIdle()
    {
        foreach (var s in Staff)
        {
            if (!s.Idle || s.Path.Count > 0 || s.HeadOf != null || !OnDuty(s) || s.Arriving(Now)) continue;
            var here = s.TilePos;
            var zone = Grid.ZoneAt(here.x, here.y);
            bool inTheWay = zone == null || (s.Posted != null && zone.Id != s.Posted) || Stations.Any(st => st.UseTile == here) || Grid.NearDoorway(here.x, here.y) ||
                            zone.InsideDoor == here || Grid.RoomAt(here.x, here.y) is { Corridor: true };
            if (!inTheWay || !Rng.Chance(0.15)) continue;
            if (WaitingSpot(s) is { } spot) SendTo(s, spot);
        }
    }

    (int x, int y)? WaitingSpot(Staff s)
    {
        // Near the desks they work at; anyone else (and messengers) near the desks that make the most paper.
        var work = Stations.Where(st => st.Id == s.Pinned).ToList();
        if (work.Count == 0 && s.Posted != null)
            work = Stations.Where(st => st.Def.Operator == s.Role && !st.Obsolete && MayWorkAt(s, st.UseTile)).ToList();
        if (work.Count == 0) work = Stations.Where(st => st.Def.Operator == s.Role && !st.Obsolete).ToList();
        if (work.Count == 0) work = Stations.Where(st => st.Def.Operator != null).ToList();
        var anchor = work.OrderBy(st => Manhattan(st.UseTile, s.TilePos)).FirstOrDefault();
        var zone = s.Posted != null ? Grid.Zone(s.Posted)
                 : anchor != null ? Grid.ZoneAt(anchor.UseTile.x, anchor.UseTile.y) : Grid.Zone("mansion");
        if (anchor != null && zone != null && Grid.ZoneAt(anchor.UseTile.x, anchor.UseTile.y) != zone) anchor = null;
        if (zone == null) return null;
        var from = anchor?.UseTile ?? zone.InsideDoor;
        var standing = new HashSet<(int, int)>(Staff.Where(o => o != s && !o.Away).Select(o => o.Path.Count > 0 ? o.Path[^1] : o.TilePos));
        var uses = new HashSet<(int, int)>(Stations.Select(st => st.UseTile));
        (int x, int y)? best = null;
        int bestScore = int.MaxValue;
        for (int y = zone.Y + 1; y < zone.Y + zone.H - 1; y++)
            for (int x = zone.X + 1; x < zone.X + zone.W - 1; x++)
            {
                if (!Grid.Walkable(x, y) || uses.Contains((x, y)) || standing.Contains((x, y)) || Grid.NearDoorway(x, y) || zone.InsideDoor == (x, y)) continue;
                if (Grid.RoomAt(x, y) is { Corridor: true }) continue;
                // By a wall or partition, out of the way.
                bool byWall = x == zone.X + 1 || y == zone.Y + 1 || x == zone.X + zone.W - 2 || y == zone.Y + zone.H - 2 ||
                              Grid.Blocked(x, y, x + 1, y) || Grid.Blocked(x, y, x - 1, y) || Grid.Blocked(x, y, x, y + 1) || Grid.Blocked(x, y, x, y - 1);
                int score = Manhattan((x, y), from) + (byWall ? 0 : 4) + (Grid.RoomAt(x, y) == Grid.RoomAt(from.x, from.y) ? 0 : 6);
                if (score < bestScore) { bestScore = score; best = (x, y); }
            }
        return best;
    }

    /// <summary>
    /// Next-step desk within arm's reach in the same hut, with someone sitting at one of the two: the paper is passed
    /// across, no runner. This is what makes a well laid-out hut quicker than a scattered one.
    /// </summary>
    bool HandOver(Station src, Message m)
    {
        var zone = Grid.ZoneAt(src.UseTile.x, src.UseTile.y);
        if (zone == null || m.Next is not StationType next) return false;
        bool srcManned = src.OperatorId != null && src.OperatorPresent;
        Station? best = null;
        foreach (var st in Stations)
        {
            if (st == src || !st.Accepts(next) || st.FreeInput < 1 || st.IsDown(Now) || !CanBeStaffed(st)) continue;
            if (Manhattan(st.UseTile, src.UseTile) > Defs.HandOverReach) continue;
            if (Grid.ZoneAt(st.UseTile.x, st.UseTile.y) != zone) continue;
            if (Grid.RoomAt(st.UseTile.x, st.UseTile.y) != Grid.RoomAt(src.UseTile.x, src.UseTile.y)) continue;
            if (!srcManned && !(st.OperatorId != null && st.OperatorPresent)) continue;
            if (best == null || st.Input.Count < best.Input.Count) best = st;
        }
        if (best == null) return false;
        var batch = src.Output.Where(id => !Messages[id].Reserved && Messages[id].Next == next)
                              .Take(Math.Min(Defs.CarryBatch, best.FreeInput)).ToList();
        foreach (var id in batch)
        {
            var x = Messages[id];
            src.Output.Remove(id);
            x.Loc = Loc.Input;
            x.LocId = best.Id;
            x.Since = Now;
            best.Input.Add(id);
        }
        Stats.HandOvers += batch.Count;
        return batch.Count > 0;
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
            if (!s.Leaving && s.HeadOf == null && (s.Pinned == st.Id || (s.Pinned == null && s.Role == role && MayWorkAt(s, st.UseTile)))) return true;
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
            if (!s.Idle || s.HeadOf != null || !filter(s)) continue;
            // Tired people aren't sent back to work: with nowhere to rest they catch their breath where they stand
            // (a few minutes), rather than being sat down and stood up again every tick.
            if (s.Tired) continue;
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
