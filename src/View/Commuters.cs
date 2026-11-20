using System;
using System.Collections.Generic;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>
/// The walk (or ride) between the billets and the Park, drawn with each person's own figure. The sim has people
/// appear at their building's door when their watch begins and vanish there when it ends; this shows the journey on
/// either side of that: going off watch they walk or cycle out to the gate and off down the road, and before a watch
/// they come up the road, stop at the barrier for the sentry to check their pass, and go on to their door, timed to
/// get there just before the watch begins. It is only a picture: the sim never sees it, so it changes no balance.
///
/// Trips run on game time (they pause with the game and hurry at 6x). Arrivals for a watch are planned together,
/// one pass check after another, worked back from the start of the watch so nobody queues and nobody is late.
/// </summary>
public sealed class Commuters
{
    /// <summary>Tiles per game minute: a brisk walk (sim walkers do 0.28 inside the Park), and a bicycle.</summary>
    const float WalkSpeed = 0.42f, BikeSpeed = 1.05f;
    /// <summary>Game minutes at the barrier while the sentry looks at a pass.</summary>
    const double CheckMinutes = 3.5;
    /// <summary>How long before a watch its arrivals are planned (the furthest walker needs about two hours).</summary>
    const double PlanAhead = 300;
    const float RoadX = -18f;   // where the road leaves the edge of the map
    /// <summary>Game minutes: going off watch, some come out of the door at once and some up to this much later.</summary>
    const double Linger = 20;
    /// <summary>Game minutes: arrivals reach their door between Early.min and Early.max before the watch begins.</summary>
    static readonly (double min, double max) Early = (3, 30);

    /// <summary>Bus passengers file past the sentry one after another: this far apart, each showing a pass this long.</summary>
    const double BusGap = 1.6, BusCheck = 1.2;
    /// <summary>Game minutes the bus takes to come up the road or to turn and go.</summary>
    public const double BusDrive = 10;

    sealed class Trip
    {
        public bool In, Bike, Bus;
        public double Check = CheckMinutes;
        public List<Vector3> Pts = null!;
        public float[] Cum = null!;
        public float Total, GateDist;
        public double Start, CheckFrom;
        public (int x, int y) Home;
        public float Speed => Bike ? BikeSpeed : WalkSpeed;
    }

    readonly Dictionary<int, Trip> _trips = new();
    readonly HashSet<int> _present = new();
    readonly HashSet<double> _planned = new();
    readonly Dictionary<(int, int), List<Vector3>?> _routesIn = new();
    int _routeDay = -1;
    bool _primed;

    /// <summary>Where the arrivals stop for the pass check: on the road side of the barrier, toward the sentry.</summary>
    static Vector3 CheckPoint => new(0.32f, 0, Grid.Gate.y - 0.42f);
    static Vector3 GatePoint => new(Grid.Gate.x + 0.5f, 0, Grid.Gate.y + 0.5f);
    static Vector3 RoadPoint => new(RoadX, 0, Grid.Gate.y + 0.5f);
    /// <summary>Where the bus's door opens at the stop short of the gate (see <see cref="Bus"/>).</summary>
    public static Vector3 StopPoint => new(-3.9f, 0, Grid.Gate.y - 0.62f);

    /// <summary>Most of the Park cycled in from billets in the town and villages; the far-billeted came by bus.</summary>
    public static bool Cycles(Staff s) => !s.FarBillet && Gfx.Rand01(s.Id, 11, 5) < 0.6f;

    public void Clear()
    {
        _trips.Clear();
        _present.Clear();
        _planned.Clear();
        _routesIn.Clear();
        _primed = false;
    }

    /// <summary>Starts trips for those who just went off watch, plans the next watch's arrivals, drops finished trips.</summary>
    public void Update(GameState g)
    {
        if (g.Day != _routeDay) { _routesIn.Clear(); _routeDay = g.Day; }
        foreach (var s in g.Staff)
        {
            if (!s.Away)
            {
                _present.Add(s.Id);
                _trips.Remove(s.Id);   // back in the sim: the figure is theirs again
                continue;
            }
            // Just gone off: out of the door (or from wherever they were) to the gate.
            if (_present.Remove(s.Id) && _primed && !_trips.ContainsKey(s.Id)) StartOut(g, s);
        }
        _primed = true;
        PlanArrivals(g);
        List<int>? done = null;
        foreach (var (id, t) in _trips)
        {
            double end = t.In ? t.CheckFrom + t.Check + (t.Total - t.GateDist) / t.Speed : t.Start + t.Total / t.Speed;
            // Arrivals stay (inside, unseen) until the sim brings them on watch; give up on them an hour after.
            if (g.Now > end + (t.In ? 60 : 0) || g.StaffById(id) == null) (done ??= new()).Add(id);
        }
        if (done != null) foreach (var id in done) _trips.Remove(id);
    }

    void StartOut(GameState g, Staff s)
    {
        var from = ((int)Math.Floor(s.X), (int)Math.Floor(s.Y));
        if (from == Grid.Gate || !g.Grid.Walkable(from.Item1, from.Item2)) return;
        var path = Pathfinder.Find(g.Grid, from, Grid.Gate);
        if (path == null) return;
        var pts = new List<Vector3> { new((float)s.X, 0, (float)s.Y) };
        AddCorners(pts, from, path);
        pts.Add(GatePoint);
        pts.Add(s.FarBillet ? StopPoint : RoadPoint);   // the far-billeted to the bus, the rest off down the road
        // Not all out of the door at once: some gather their things, finish a chat, or wait for a friend.
        double linger = Gfx.Rand01(s.Id, g.Day, 13) * Linger;
        var t = new Trip { In = false, Bike = Cycles(s), Bus = s.FarBillet, Pts = pts, Start = g.Now + linger, Home = from };
        Measure(t);
        _trips[s.Id] = t;
    }

    /// <summary>The next start of this person's watch, as an absolute game minute.</summary>
    static double NextStart(GameState g, Staff s)
    {
        double t = g.Day * Defs.MinutesPerDay + Defs.Shifts[s.Shift].StartHour * 60;
        return t <= g.Now ? t + Defs.MinutesPerDay : t;
    }

    void PlanArrivals(GameState g)
    {
        // Each distinct watch start within reach gets planned once, for everyone then due.
        var due = new Dictionary<double, List<(Staff s, List<Vector3> route, double latest)>>();
        var bus = new Dictionary<double, List<(Staff s, List<Vector3> route, double latest)>>();
        foreach (var s in g.Staff)
        {
            if (!s.Away || s.Leaving || _trips.ContainsKey(s.Id)) continue;
            double start = NextStart(g, s);
            if (start - g.Now > PlanAhead || _planned.Contains(start)) continue;
            if (s.IsSick(start) || s.Arriving(start)) continue;
            var home = ((int)Math.Floor(s.HomeX), (int)Math.Floor(s.HomeY));
            if (home == Grid.Gate || !g.Grid.Walkable(home.Item1, home.Item2)) continue;   // the sim brings them in at the gate itself
            if (RouteIn(g, home, s.FarBillet) is not { } route) continue;
            var lists = s.FarBillet ? bus : due;
            if (!lists.TryGetValue(start, out var list)) lists[start] = list = new();
            if (!due.ContainsKey(start)) due[start] = new();
            // On the doorstep a little before the watch: a few minutes, or up to half an hour for the keen.
            double margin = Early.min + Gfx.Rand01(s.Id, g.Day, 9) * (Early.max - Early.min);
            double after = (Length(route) - Length(route, 2)) / (Cycles(s) ? BikeSpeed : WalkSpeed);
            list.Add((s, route, start - margin - after));
        }
        foreach (var (start, list) in due)
        {
            _planned.Add(start);
            // The bus's passengers file past the sentry together, through by the time the earliest of them must be.
            double blockFrom = double.MaxValue, blockTo = double.MinValue;
            if (bus.TryGetValue(start, out var riders) && riders.Count > 0)
            {
                riders.Sort((a, b) => a.s.Id.CompareTo(b.s.Id));
                blockTo = double.MaxValue;
                foreach (var r in riders) blockTo = Math.Min(blockTo, r.latest);
                blockFrom = blockTo - riders.Count * BusGap;
                for (int k = 0; k < riders.Count; k++)
                {
                    var (s, route, _) = riders[k];
                    var t = new Trip { In = true, Bus = true, Check = BusCheck, Pts = route, CheckFrom = blockFrom + k * BusGap, Home = ((int)Math.Floor(s.HomeX), (int)Math.Floor(s.HomeY)) };
                    Measure(t);
                    t.Start = t.CheckFrom - t.GateDist / t.Speed;   // stepping off the bus
                    _trips[s.Id] = t;
                }
            }
            // Work back from the latest: each check ends by when that person must be through, and before the next begins.
            list.Sort((a, b) => b.latest.CompareTo(a.latest));
            double next = double.MaxValue;
            foreach (var (s, route, latest) in list)
            {
                double checkEnd = Math.Min(latest, next);
                if (checkEnd > blockFrom && checkEnd - CheckMinutes < blockTo) checkEnd = blockFrom;   // not while the bus empties
                double checkFrom = checkEnd - CheckMinutes;
                next = checkFrom;
                var t = new Trip { In = true, Bike = Cycles(s), Pts = route, CheckFrom = checkFrom, Home = ((int)Math.Floor(s.HomeX), (int)Math.Floor(s.HomeY)) };
                Measure(t);
                t.Start = checkFrom - t.GateDist / t.Speed;
                _trips[s.Id] = t;
            }
        }
        if (_planned.Count > 16) _planned.RemoveWhere(x => x < g.Now - Defs.MinutesPerDay);
    }

    /// <summary>Road, the barrier, through the gate and along the paths to a door. Shared by everyone going there today.</summary>
    List<Vector3>? RouteIn(GameState g, (int x, int y) home, bool byBus)
    {
        if (!_routesIn.TryGetValue(home, out var r))
        {
            var path = Pathfinder.Find(g.Grid, Grid.Gate, home);
            if (path != null)
            {
                r = new List<Vector3> { RoadPoint, CheckPoint, GatePoint };
                AddCorners(r, Grid.Gate, path);
                r.Add(new Vector3(home.x + 0.5f, 0, home.y + 0.5f));
            }
            _routesIn[home] = r;
        }
        if (r == null || !byBus) return r;
        var fromBus = new List<Vector3>(r);
        fromBus[0] = StopPoint;
        return fromBus;
    }

    /// <summary>Only the turns of a tile path, so the figure walks diagonally between them rather than in steps.</summary>
    static void AddCorners(List<Vector3> pts, (int x, int y) from, List<(int x, int y)> path)
    {
        (int x, int y) prev = from, dir = (0, 0);
        foreach (var p in path)
        {
            var nd = (p.x - prev.x, p.y - prev.y);
            if (nd != dir && prev != from) pts.Add(new Vector3(prev.x + 0.5f, 0, prev.y + 0.5f));
            dir = nd;
            prev = p;
        }
    }

    static float Length(List<Vector3> pts, int upTo = int.MaxValue)
    {
        float d = 0;
        for (int i = 1; i < pts.Count && i < upTo; i++) d += (pts[i] - pts[i - 1]).Length();
        return d;
    }

    static void Measure(Trip t)
    {
        t.Cum = new float[t.Pts.Count];
        for (int i = 1; i < t.Pts.Count; i++) t.Cum[i] = t.Cum[i - 1] + (t.Pts[i] - t.Pts[i - 1]).Length();
        t.Total = t.Cum[^1];
        t.GateDist = t.In ? t.Cum[1] : 0;
    }

    /// <summary>Where someone off the map's figure is now, if they're on the way in or out.</summary>
    public bool Where(int id, double now, out Vector3 at, out Vector3 heading, out bool riding, out bool checking)
    {
        at = heading = default;
        riding = checking = false;
        if (!_trips.TryGetValue(id, out var t) || now < t.Start) return false;
        float d;
        if (!t.In) d = (float)((now - t.Start) * t.Speed);
        else if (now < t.CheckFrom) d = Math.Min(t.GateDist, (float)((now - t.Start) * t.Speed));
        else if (now < t.CheckFrom + t.Check) { d = t.GateDist; checking = true; }
        else d = t.GateDist + (float)((now - t.CheckFrom - t.Check) * t.Speed);
        if (d >= t.Total) return false;   // in at the door, or off down the road
        int i = 1;
        while (i < t.Cum.Length - 1 && t.Cum[i] < d) i++;
        var a = t.Pts[i - 1];
        var b = t.Pts[i];
        float seg = t.Cum[i] - t.Cum[i - 1];
        at = seg > 1e-4f ? a.Lerp(b, (d - t.Cum[i - 1]) / seg) : b;
        // Walkers keep to the left verge and cyclists ride nearer the crown, so a bicycle passes a walker rather than
        // through them; both close in to the line at the barrier and at either end.
        if (seg > 1e-4f)
        {
            var dir = (b - a) / seg;
            var left = new Vector3(dir.Z, 0, -dir.X);
            float ease = Mathf.Clamp(Math.Min(d, Math.Min(t.Total - d, t.In ? Math.Abs(d - t.GateDist) : float.MaxValue)) / 1.2f, 0, 1);
            at += left * ((t.Bike ? -0.28f : 0.3f) * ease);
        }
        heading = checking ? Sentry - at : b - a;
        // On the bicycle out of doors; wheeled through a building's door and out again.
        riding = t.Bike && (at.X < 0 || (t.In ? d > t.GateDist + 0.6f && t.Total - d > 0.7f : d > 0.7f));
        return true;
    }

    /// <summary>The sentry, just inside the gate by the barrier's pivot (see Park3D.GateParts).</summary>
    public static Vector3 Sentry => new(1.3f, 0, Grid.Gate.y - 1.02f);   // on the lawn at the drive's edge, clear of the way in

    /// <summary>Someone being checked at the barrier right now (the sentry turns to them).</summary>
    public Vector3? Checking(double now)
    {
        foreach (var t in _trips.Values)
            if (t.In && now >= t.CheckFrom && now < t.CheckFrom + t.Check) return CheckPoint;
        return null;
    }

    /// <summary>Anyone just through the barrier or about to go out by it: the pole goes up for them.</summary>
    public bool AtBarrier(double now)
    {
        foreach (var (id, t) in _trips)
        {
            if (!Where(id, now, out var at, out _, out _, out bool checking) || checking) continue;
            if (at.X > -1.2f && at.X < 2.2f && Mathf.Abs(at.Z - (Grid.Gate.y + 0.5f)) < 1.6f) return true;
        }
        return false;
    }

    /// <summary>
    /// The bus is wanted at the stop: passengers are about to step off (it comes up the road ahead of them), someone is
    /// walking out to it, or a far-billeted watch is about to end.
    /// </summary>
    public bool BusWanted(GameState g)
    {
        double now = g.Now;
        foreach (var t in _trips.Values)
        {
            if (!t.Bus) continue;
            if (t.In ? now >= t.Start - BusDrive - 1 && now < t.Start + 0.5 : now < t.Start + t.Total / t.Speed) return true;
        }
        foreach (var s in g.Staff)
        {
            if (!s.FarBillet || s.Away || s.Leaving) continue;
            if (!g.OnDuty(s)) return true;   // off watch and on their way out: the bus waits for them
            double end = g.Day * Defs.MinutesPerDay + Defs.Shifts[s.Shift].EndHour * 60;
            if (end <= now) end += Defs.MinutesPerDay;
            if (end - now <= BusDrive + 1) return true;
        }
        return false;
    }

    /// <summary>Cyclists who've arrived and gone in, but aren't on watch in the sim yet: their bicycles are in the rack.</summary>
    public int Parked(GameState g, Zone z)
    {
        int n = 0;
        foreach (var (id, t) in _trips)
            if (t.In && t.Bike && g.Now >= t.CheckFrom + t.Check + (t.Total - t.GateDist) / t.Speed && g.Grid.ZoneAt(t.Home.x, t.Home.y) == z) n++;
        return n;
    }
}
