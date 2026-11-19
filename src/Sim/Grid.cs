using System;
using System.Collections.Generic;

namespace StationX.Sim;

public enum Tile : byte { Lawn, Path, Floor, Wall, Door, Water, Tree }

public sealed class Zone
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public int DoorX { get; set; }
    public int DoorY { get; set; }
    public int Cost { get; set; }
    public bool Brick { get; set; }
    public bool Available { get; set; }
    public bool Unlocked { get; set; }
    public List<Room> Rooms { get; set; } = new();
    public List<Partition> Partitions { get; set; } = new();

    public bool InInterior(int x, int y) => x > X && x < X + W - 1 && y > Y && y < Y + H - 1;
    public bool InOuter(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;

    /// <summary>The path tile just outside the door.</summary>
    public (int x, int y) OutsideDoor =>
        DoorY == Y ? (DoorX, DoorY - 1) :
        DoorY == Y + H - 1 ? (DoorX, DoorY + 1) :
        DoorX == X ? (DoorX - 1, DoorY) : (DoorX + 1, DoorY);

    /// <summary>The floor tile just inside the door, which is kept clear.</summary>
    public (int x, int y) InsideDoor =>
        DoorY == Y ? (DoorX, DoorY + 1) :
        DoorY == Y + H - 1 ? (DoorX, DoorY - 1) :
        DoorX == X ? (DoorX + 1, DoorY) : (DoorX - 1, DoorY);
}

/// <summary>The park: a tile map with walled zones (Mansion and huts).</summary>
public sealed partial class Grid
{
    public const int Width = 48;
    public const int Height = 32;
    public static readonly (int x, int y) Gate = (0, 13);

    public Tile[] Tiles { get; set; } = new Tile[Width * Height];
    public List<Zone> Zones { get; set; } = new();

    /// <summary>Station id occupying each tile, or -1. Rebuilt from stations, not saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int[] Occupant { get; } = new int[Width * Height];

    public static bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
    public Tile Get(int x, int y) => Tiles[y * Width + x];
    public void Set(int x, int y, Tile t) => Tiles[y * Width + x] = t;
    public int OccupantAt(int x, int y) => Occupant[y * Width + x];

    public bool Walkable(int x, int y)
    {
        if (!InBounds(x, y)) return false;
        var t = Get(x, y);
        if (t is Tile.Wall or Tile.Water or Tile.Tree) return false;
        return Occupant[y * Width + x] < 0;
    }

    public Zone? ZoneAt(int x, int y)
    {
        foreach (var z in Zones)
            if (z.InOuter(x, y)) return z;
        return null;
    }

    public Zone? Zone(string id) => Zones.Find(z => z.Id == id);

    public static Grid CreateBletchley()
    {
        var g = new Grid();
        Array.Fill(g.Occupant, -1);

        // Gravel drive across the park, from the gate on the left.
        for (int x = 0; x < Width; x++)
            for (int y = 12; y <= 14; y++)
                g.Set(x, y, Tile.Path);
        for (int y = 0; y < 12; y++) g.Set(18, y, Tile.Path);
        for (int y = 15; y < 25; y++) g.Set(32, y, Tile.Path);
        for (int x = 25; x < 33; x++) g.Set(x, 24, Tile.Path);

        g.AddZone(new Zone { Id = "mansion", Name = "The Mansion", X = 2, Y = 2, W = 16, H = 10, DoorX = 9, DoorY = 11, Brick = true, Available = true, Unlocked = true });
        // Hut 1, beside the Mansion, where the first Bombe went in March 1940.
        g.AddZone(new Zone { Id = "hut1", Name = "Hut 1", X = 13, Y = 15, W = 7, H = 5, DoorX = 16, DoorY = 15, Cost = 400 });
        g.AddZone(new Zone { Id = "hut6", Name = "Hut 6", X = 20, Y = 2, W = 12, H = 7, DoorX = 25, DoorY = 8, Cost = 800 });
        g.AddZone(new Zone { Id = "hut11", Name = "Hut 11", X = 34, Y = 2, W = 12, H = 7, DoorX = 39, DoorY = 8, Cost = 1200 });
        g.AddZone(new Zone { Id = "hut3", Name = "Hut 3", X = 20, Y = 17, W = 12, H = 7, DoorX = 25, DoorY = 17, Cost = 1000 });
        g.AddZone(new Zone { Id = "hut8", Name = "Hut 8", X = 34, Y = 17, W = 12, H = 7, DoorX = 39, DoorY = 17, Cost = 1500 });
        g.AddZone(new Zone { Id = "blockf", Name = "Block F", X = 19, Y = 25, W = 27, H = 6, DoorX = 25, DoorY = 25, Cost = 2500, Brick = true });

        // The lake in front of the Mansion.
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                double dx = (x + 0.5 - 9.5) / 6.0, dy = (y + 0.5 - 23.0) / 4.2;
                if (dx * dx + dy * dy < 1.0) g.Set(x, y, Tile.Water);
            }

        // Trees along the edges, placed deterministically.
        var rng = new Rng(1939);
        for (int i = 0; i < 70; i++)
        {
            int x = rng.Range(0, Width), y = rng.Range(0, Height);
            bool edge = x < 2 || y < 2 || x > Width - 3 || y > Height - 4 || (y > 24 && x > 14);
            if (edge && g.Get(x, y) == Tile.Lawn && (x, y) != Gate) g.Set(x, y, Tile.Tree);
        }
        return g;
    }

    /// <summary>Adds buildings that a newer version of the game has and an older save lacks.</summary>
    public void AddMissingZones()
    {
        foreach (var z in CreateBletchley().Zones)
            if (Zone(z.Id) == null)
            {
                bool clear = true;
                for (int y = z.Y; y < z.Y + z.H && clear; y++)
                    for (int x = z.X; x < z.X + z.W && clear; x++)
                        if (Get(x, y) is not (Tile.Lawn or Tile.Path or Tile.Tree)) clear = false;
                if (clear) AddZone(new Zone { Id = z.Id, Name = z.Name, X = z.X, Y = z.Y, W = z.W, H = z.H, DoorX = z.DoorX, DoorY = z.DoorY, Cost = z.Cost, Brick = z.Brick });
            }
    }

    void AddZone(Zone z)
    {
        if (z.Rooms.Count == 0) (z.Partitions, z.Rooms) = Interiors.For(z.Id);
        Zones.Add(z);
        InvalidateWalls();
        for (int y = z.Y; y < z.Y + z.H; y++)
            for (int x = z.X; x < z.X + z.W; x++)
            {
                bool edge = x == z.X || y == z.Y || x == z.X + z.W - 1 || y == z.Y + z.H - 1;
                Set(x, y, edge ? Tile.Wall : Tile.Floor);
            }
        Set(z.DoorX, z.DoorY, Tile.Door);
        // Make sure the door opens onto walkable ground.
        int ox = z.DoorX, oy = z.DoorY;
        if (z.DoorY == z.Y) oy--; else if (z.DoorY == z.Y + z.H - 1) oy++;
        else if (z.DoorX == z.X) ox--; else ox++;
        if (InBounds(ox, oy) && Get(ox, oy) == Tile.Lawn) Set(ox, oy, Tile.Path);
    }

    public void RebuildOccupancy(IEnumerable<Station> stations)
    {
        Array.Fill(Occupant, -1);
        foreach (var s in stations)
            foreach (var (x, y) in s.Footprint())
                Occupant[y * Width + x] = s.Id;
    }
}

/// <summary>A* over the tile grid, 4-connected.</summary>
public static class Pathfinder
{
    static readonly int[] Dx = { 1, -1, 0, 0 };
    static readonly int[] Dy = { 0, 0, 1, -1 };

    [ThreadStatic] static int[]? _came;
    [ThreadStatic] static int[]? _cost;
    [ThreadStatic] static int[]? _stamp;
    [ThreadStatic] static int _gen;

    /// <param name="avoid">Tiles to go round if possible (people standing there); the goal itself is never avoided.</param>
    public static List<(int x, int y)>? Find(Grid g, (int x, int y) from, (int x, int y) to, HashSet<(int, int)>? avoid = null)
    {
        if (!Grid.InBounds(from.x, from.y) || !Grid.InBounds(to.x, to.y)) return null;
        if (from == to) return new List<(int, int)>();
        if (!g.Walkable(to.x, to.y)) return null;

        int n = Grid.Width * Grid.Height;
        _came ??= new int[n];
        _cost ??= new int[n];
        _stamp ??= new int[n];
        _gen++;
        if (_gen == int.MaxValue) { Array.Clear(_stamp); _gen = 1; }

        int start = from.y * Grid.Width + from.x, goal = to.y * Grid.Width + to.x;
        var open = new PriorityQueue<int, int>();
        _stamp[start] = _gen;
        _cost[start] = 0;
        _came[start] = -1;
        open.Enqueue(start, H(from, to));

        while (open.TryDequeue(out int cur, out _))
        {
            if (cur == goal) break;
            int cx = cur % Grid.Width, cy = cur / Grid.Width;
            for (int i = 0; i < 4; i++)
            {
                int nx = cx + Dx[i], ny = cy + Dy[i];
                if (!g.Walkable(nx, ny) || g.Blocked(cx, cy, nx, ny)) continue;
                if (avoid != null && (nx, ny) != to && avoid.Contains((nx, ny))) continue;
                int ni = ny * Grid.Width + nx;
                int nc = _cost[cur] + 1;
                if (_stamp[ni] == _gen && _cost[ni] <= nc) continue;
                _stamp[ni] = _gen;
                _cost[ni] = nc;
                _came[ni] = cur;
                open.Enqueue(ni, nc + H((nx, ny), to));
            }
        }

        if (_stamp[goal] != _gen) return null;
        var path = new List<(int, int)>();
        for (int c = goal; c != start; c = _came[c])
            path.Add((c % Grid.Width, c / Grid.Width));
        path.Reverse();
        return path;
    }

    /// <summary>Tiles reachable from a start tile (flood fill), for placement checks.</summary>
    public static bool[] Reachable(Grid g, (int x, int y) from)
    {
        var seen = new bool[Grid.Width * Grid.Height];
        var q = new Queue<(int, int)>();
        q.Enqueue(from);
        seen[from.y * Grid.Width + from.x] = true;
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = x + Dx[i], ny = y + Dy[i];
                if (!g.Walkable(nx, ny) || g.Blocked(x, y, nx, ny)) continue;
                int ni = ny * Grid.Width + nx;
                if (seen[ni]) continue;
                seen[ni] = true;
                q.Enqueue((nx, ny));
            }
        }
        return seen;
    }

    static int H((int x, int y) a, (int x, int y) b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
}
