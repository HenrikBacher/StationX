using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>A named room inside a building. Corridors are kept clear for walking.</summary>
public sealed class Room
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public bool Corridor { get; set; }
    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + W && y < Y + H;
}

/// <summary>
/// A thin partition on the line between tiles. Vertical: between columns At-1 and At, rows From..To.
/// Horizontal: between rows At-1 and At, columns From..To. Gaps are doorways.
/// </summary>
public sealed class Partition
{
    public bool Vertical { get; set; }
    public int At { get; set; }
    public int From { get; set; }
    public int To { get; set; }
    public List<int> Gaps { get; set; } = new();
}

/// <summary>
/// How the buildings were divided inside. No wartime plans or photographs of the hut interiors survive, so this
/// follows the accounts: the Mansion's ground-floor rooms (Denniston in the Morning Room, the Naval Section in the
/// Library, the Ballroom and Dining Room), Hut 6's Registration, Machine and Decoding Rooms off a corridor, Hut 3's
/// Watch and Index Rooms, Hut 8's Banburismus room and the Prof's room, the Testery and Newmanry in Block F, and
/// Hut 11's single Bombe room (its partitions came after 1943).
/// </summary>
public static class Interiors
{
    static Partition V(int at, int from, int to, params int[] gaps) => new() { Vertical = true, At = at, From = from, To = to, Gaps = gaps.ToList() };
    static Partition Hz(int at, int from, int to, params int[] gaps) => new() { Vertical = false, At = at, From = from, To = to, Gaps = gaps.ToList() };
    static Room R(string name, int x, int y, int w, int h, bool corridor = false) => new() { Name = name, X = x, Y = y, W = w, H = h, Corridor = corridor };

    public static (List<Partition> walls, List<Room> rooms) For(string zoneId) => zoneId switch
    {
        "mansion" => (new()
        {
            V(8, 3, 10, 5, 9), V(11, 3, 10, 5, 9),
            Hz(7, 3, 7, 5), Hz(7, 8, 10, 9), Hz(8, 11, 16, 13),
        }, new()
        {
            R("Library", 3, 3, 5, 4), R("Morning Room", 3, 7, 5, 4),
            R("Drawing Room", 8, 3, 3, 4), R("Entrance Hall", 8, 7, 3, 4, corridor: true),
            R("Ballroom", 11, 3, 6, 5), R("Dining Room", 11, 8, 6, 3),
        }),
        // The Decoding Room, full of Typex operators, was the big room.
        "hut6" => (new() { Hz(7, 21, 30, 22, 24, 28), V(24, 3, 6), V(26, 3, 6) },
                   new() { R("Machine Room", 21, 3, 3, 4), R("Registration Room", 24, 3, 2, 4), R("Decoding Room", 26, 3, 5, 4), R("Corridor", 21, 7, 10, 1, corridor: true) }),
        "hut3" => (new() { Hz(19, 21, 30, 23, 27, 29), V(26, 19, 22), V(29, 19, 22) },
                   new() { R("Corridor", 21, 18, 10, 1, corridor: true), R("Watch Room", 21, 19, 5, 4), R("Index Room", 26, 19, 3, 4), R("Advisers", 29, 19, 2, 4) }),
        "hut8" => (new() { Hz(19, 35, 44, 36, 39, 43), V(39, 19, 22), V(41, 19, 22) },
                   new() { R("Corridor", 35, 18, 10, 1, corridor: true), R("Banburismus Room", 35, 19, 4, 4), R("The Prof's Room", 39, 19, 2, 4), R("Decoding Room", 41, 19, 4, 4) }),
        "blockf" => (new() { Hz(27, 20, 44, 24, 27, 36), V(30, 27, 29) },
                     new() { R("Corridor", 20, 26, 25, 1, corridor: true), R("Testery", 20, 27, 10, 3), R("Newmanry", 30, 27, 15, 3) }),
        "hut11" => (new(), new() { R("Bombe Room", 35, 3, 10, 5) }),
        "hut1" => (new(), new() { R("Bombe Room", 14, 16, 5, 3) }),
        "hut7" => (new(), new() { R("Japanese Naval Section", 3, 16, 8, 3) }),
        _ => (new(), new()),
    };
}

public sealed partial class Grid
{
    [System.Text.Json.Serialization.JsonIgnore] bool[]? _vWall, _hWall, _nearDoor;

    void InvalidateWalls() => _vWall = _hWall = _nearDoor = null;

    void BuildWalls()
    {
        _vWall = new bool[Width * Height];
        _hWall = new bool[Width * Height];
        _nearDoor = new bool[Width * Height];
        foreach (var z in Zones)
            foreach (var p in z.Partitions)
                for (int i = p.From; i <= p.To; i++)
                {
                    int x = p.Vertical ? p.At : i, y = p.Vertical ? i : p.At;
                    if (!InBounds(x, y)) continue;
                    if (p.Gaps.Contains(i))
                    {
                        _nearDoor[y * Width + x] = true;
                        int bx = p.Vertical ? x - 1 : x, by = p.Vertical ? y : y - 1;
                        if (InBounds(bx, by)) _nearDoor[by * Width + bx] = true;
                    }
                    else (p.Vertical ? _vWall : _hWall)[y * Width + x] = true;
                }
    }

    /// <summary>True when a partition stands between two neighbouring tiles.</summary>
    public bool Blocked(int ax, int ay, int bx, int by)
    {
        if (_vWall == null) BuildWalls();
        if (ay == by) { int x = Math.Max(ax, bx); return InBounds(x, ay) && _vWall![ay * Width + x]; }
        int y = Math.Max(ay, by);
        return InBounds(ax, y) && _hWall![y * Width + ax];
    }

    /// <summary>A tile either side of an interior doorway, which is kept clear.</summary>
    public bool NearDoorway(int x, int y)
    {
        if (_nearDoor == null) BuildWalls();
        return InBounds(x, y) && _nearDoor![y * Width + x];
    }

    public Room? RoomAt(int x, int y)
    {
        var z = ZoneAt(x, y);
        if (z == null) return null;
        foreach (var r in z.Rooms) if (r.Contains(x, y)) return r;
        return null;
    }

    /// <summary>Gives a building its rooms. For a save from before interiors, opens a doorway wherever a desk already stands across a new wall.</summary>
    public void FitInteriors(IEnumerable<Station> stations)
    {
        bool changed = false;
        foreach (var z in Zones)
        {
            if (z.Rooms.Count > 0) continue;
            (z.Partitions, z.Rooms) = Interiors.For(z.Id);
            changed = true;
        }
        if (!changed) return;
        InvalidateWalls();
        foreach (var s in stations)
        {
            var tiles = s.Footprint().ToList();
            if (!s.Def.Outdoor) tiles.Add(s.UseTile);
            foreach (var a in tiles)
                foreach (var b in tiles)
                    if (Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y) == 1 && Blocked(a.x, a.y, b.x, b.y)) OpenGap(a, b);
        }
        InvalidateWalls();
    }

    void OpenGap((int x, int y) a, (int x, int y) b)
    {
        foreach (var z in Zones)
            foreach (var p in z.Partitions)
            {
                if (p.Vertical && a.y == b.y && p.At == Math.Max(a.x, b.x) && a.y >= p.From && a.y <= p.To) p.Gaps.Add(a.y);
                if (!p.Vertical && a.x == b.x && p.At == Math.Max(a.y, b.y) && a.x >= p.From && a.x <= p.To) p.Gaps.Add(a.x);
            }
    }
}
