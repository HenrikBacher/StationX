using System;
using System.Collections.Generic;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>
/// Bicycle racks against the wall beside each open building's door. Most of the Park cycled in from billets in the
/// town and villages, so a rack fills as that building's watch comes on and empties as it goes home. Each rack is one
/// mesh for the rail and one per number of bicycles in it, coloured per vertex: two draws a rack whatever it holds.
/// </summary>
public partial class Bikes : Node3D
{
    const int Slots = 6;
    const float Gap = 0.34f;

    sealed class Rack
    {
        public MeshInstance3D[] Levels = Array.Empty<MeshInstance3D>();
        public int Shown;
    }

    readonly Dictionary<string, Rack> _racks = new();
    readonly Dictionary<string, int> _people = new();
    double _countIn;

    static readonly Color[] Frames = { new("1e1d1b"), new("1e1d1b"), new("2c3a2c"), new("3a2224"), new("23283a"), new("1e1d1b") };

    public void Clear()
    {
        foreach (var r in _racks.Values)
            foreach (var m in r.Levels) m.QueueFree();
        foreach (var c in GetChildren()) c.QueueFree();
        _racks.Clear();
        _countIn = 0;
    }

    public void Sync(GameState g, double dt, IReadOnlyList<Vector3> poles)
    {
        if ((_countIn -= dt) > 0) return;
        _countIn = 0.5;
        _people.Clear();
        foreach (var s in g.Staff)
        {
            if (s.Away || s.Arriving(g.Now) || s.FarBillet) continue;   // the far-billeted come by bus
            var (x, y) = s.TilePos;
            if (g.Grid.ZoneAt(x, y) is { } z) _people[z.Id] = _people.GetValueOrDefault(z.Id) + 1;
        }
        foreach (var z in g.Grid.Zones)
        {
            if (!z.Unlocked) continue;
            if (!_racks.TryGetValue(z.Id, out var rack)) _racks[z.Id] = rack = Build(z, poles);
            // Most, not all, came by bicycle: some walked, and someone always borrowed one.
            int want = Math.Min(Slots, (_people.GetValueOrDefault(z.Id) * 2 + 2) / 3);
            if (want == rack.Shown) continue;
            for (int k = 0; k < rack.Levels.Length; k++) rack.Levels[k].Visible = k + 1 == want;
            rack.Shown = want;
        }
    }

    /// <summary>The rack: along the wall to one side of the door, bicycles nose-in at right angles to it.</summary>
    Rack Build(Zone z, IReadOnlyList<Vector3> poles)
    {
        var (ox, oy) = z.OutsideDoor;
        bool alongX = oy < z.Y || oy >= z.Y + z.H;          // door in a north or south wall
        float wall = alongX ? (oy < z.Y ? z.Y : z.Y + z.H) : (ox < z.X ? z.X : z.X + z.W);
        float outward = alongX ? (oy < z.Y ? -1 : 1) : (ox < z.X ? -1 : 1);
        float door = alongX ? ox + 0.5f : oy + 0.5f;
        float lo = alongX ? z.X : z.Y, hi = alongX ? z.X + z.W : z.Y + z.H;
        float line = wall + outward * 0.42f;
        Vector3 At(float along, float off) => alongX ? new Vector3(along, 0, line + outward * off) : new Vector3(line + outward * off, 0, along);
        // Toward the middle of the wall from the door, clear of the door's swing (and the Mansion's porch and steps),
        // unless a telegraph pole stands there: then the other side, or further along.
        float near = z.Id == "mansion" ? 2.6f : 0.95f;
        float side = door - lo > hi - door ? -1 : 1, start = door + side * near;
        bool Clear(float sd, float st)
        {
            float end = st + sd * (Slots - 1) * Gap;
            if (Math.Min(st, end) - 0.3f < lo || Math.Max(st, end) + 0.3f > hi) return false;
            foreach (var p in poles)
                for (int k = 0; k < Slots; k++)
                    if ((At(st + sd * k * Gap, 0) - p).Length() < 0.55f) return false;
            return true;
        }
        foreach (var (sd, st) in new[] { (side, door + side * near), (-side, door - side * near), (side, door + side * (near + 1.2f)), (-side, door - side * (near + 1.2f)) })
            if (Clear(sd, st)) { side = sd; start = st; break; }
        float yaw = alongX ? Mathf.Pi / 2 : 0;   // the bicycle's length along the wall's normal

        var rack = new Rack { Levels = new MeshInstance3D[Slots] };
        // The rail: a low steel bar on short posts that the front wheels slot against.
        var temp = new Node3D();
        var steel = new Color("3c4038");
        float a = start, b = start + side * (Slots - 1) * Gap;
        float mid = (a + b) / 2, len = Math.Abs(b - a) + 0.3f;
        var railAt = At(mid, -0.12f) + new Vector3(0, 0.2f, 0);
        Mesh3.Box(temp, alongX ? new Vector3(len, 0.03f, 0.03f) : new Vector3(0.03f, 0.03f, len), railAt, steel);
        foreach (float p in new[] { a - side * 0.15f, b + side * 0.15f })
            Mesh3.Box(temp, new Vector3(0.035f, 0.2f, 0.035f), At(p, -0.12f) + new Vector3(0, 0.1f, 0), steel);
        AddChild(Merge(temp, $"Rack{z.Id}"));

        int seed = 0;
        foreach (char ch in z.Id) seed = seed * 31 + ch;   // a stable hash (string.GetHashCode varies per run)
        seed &= 0x7fff;
        for (int n = 1; n <= Slots; n++)
        {
            var bikes = new Node3D();
            for (int k = 0; k < n; k++)
            {
                // Filled from the door end, with the odd gap and a bicycle not quite square.
                float along = start + side * k * Gap;
                float skew = (Gfx.Rand01(seed, k, 3) - 0.5f) * 0.25f;
                Bike(bikes, At(along, 0.05f), yaw + skew, Frames[(int)(Gfx.Rand01(seed, k, 7) * Frames.Length * 0.999f)]);
            }
            var level = Merge(bikes, $"Bikes{z.Id}_{n}");
            level.Visible = false;
            AddChild(level);
            rack.Levels[n - 1] = level;
        }
        return rack;
    }

    static readonly TorusMesh Rim = new() { InnerRadius = 0.112f, OuterRadius = 0.13f, Rings = 20, RingSegments = 4 };

    /// <summary>A roadster standing upright: two wheels, frame, saddle, handlebars, a mudguard; length along local X.</summary>
    static void Bike(Node3D parent, Vector3 at, float yaw, Color frame)
    {
        // Scaled to the figures: a full-size roadster is about as long as its rider is tall.
        var b = new Node3D { Position = at, Rotation = new Vector3(0, yaw, 0), Scale = new Vector3(1.4f, 1.4f, 1.4f) };
        parent.AddChild(b);
        var tyre = new Color("161514");
        var spoke = new Color("8a8780");
        foreach (float dx in new[] { -0.19f, 0.19f })
        {
            // Tyre and rim as a ring, a hub, and a few spokes to read as a wheel, not a disc.
            var ring = new MeshInstance3D { Mesh = Rim, Position = new Vector3(dx, 0.13f, 0), RotationDegrees = new Vector3(90, 0, 0), MaterialOverride = Mesh3.Mat(tyre) };
            b.AddChild(ring);
            for (int k = 0; k < 4; k++)
                Mesh3.Box(b, new Vector3(0.006f, 0.24f, 0.006f), new Vector3(dx, 0.13f, 0), spoke).RotationDegrees = new Vector3(0, 0, k * 45);
        }
        Mesh3.Box(b, new Vector3(0.34f, 0.022f, 0.022f), new Vector3(0.01f, 0.27f, 0), frame);                         // top tube
        Mesh3.Box(b, new Vector3(0.3f, 0.022f, 0.022f), new Vector3(-0.0f, 0.2f, 0), frame).RotationDegrees = new Vector3(0, 0, 25);   // down tube
        Mesh3.Box(b, new Vector3(0.022f, 0.16f, 0.022f), new Vector3(-0.08f, 0.2f, 0), frame);                       // seat tube
        Mesh3.Box(b, new Vector3(0.022f, 0.2f, 0.022f), new Vector3(0.17f, 0.24f, 0), frame).RotationDegrees = new Vector3(0, 0, -15);  // fork
        Mesh3.Box(b, new Vector3(0.09f, 0.025f, 0.055f), new Vector3(-0.09f, 0.31f, 0), new Color("3b2a1c"));         // saddle
        Mesh3.Box(b, new Vector3(0.025f, 0.025f, 0.2f), new Vector3(0.15f, 0.35f, 0), frame);                        // handlebars
        Mesh3.Box(b, new Vector3(0.14f, 0.012f, 0.04f), new Vector3(-0.22f, 0.27f, 0), frame).RotationDegrees = new Vector3(0, 0, -20);  // rear mudguard
    }

    /// <summary>Everything built under a node, as one mesh coloured per vertex.</summary>
    static MeshInstance3D Merge(Node3D root, string name)
    {
        var pm = new Mesh3.PaintedMesh();
        void Walk(Node n, Transform3D parent)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is not Node3D c3) continue;
                var xf = parent * c3.Transform;
                if (c is MeshInstance3D mi && mi.Mesh != null)
                    for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                        if (Mesh3.Paintable(mi.MaterialOverride ?? mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s), out var col, out _))
                            pm.Add(mi.Mesh, s, xf, col);
                Walk(c, xf);
            }
        }
        Walk(root, Transform3D.Identity);
        root.Free();
        return new MeshInstance3D { Name = name, Mesh = pm.Commit(Mesh3.PaintMat(false)) };
    }
}
