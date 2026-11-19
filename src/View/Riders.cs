using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>
/// Dispatch riders: when a Y-station's bundle comes by road (no teleprinter line), a motorcycle rides in at the gate,
/// up to the door of the building whose registration desk takes it, waits while the bundle is signed for, and rides
/// out again. Purely for the eye: the sim has already put the messages on the desk.
/// </summary>
public partial class Riders : Node3D
{
    sealed class Ride
    {
        public Node3D Bike = null!;
        public List<Vector3> Route = new();
        public int Leg;
        public float Wait = -1;
        public bool Returning;
    }

    const float Speed = 3.2f;        // tiles per real second at 1x
    const float HandOver = 1.6f;     // seconds at the door
    readonly List<Ride> _rides = new();
    readonly HashSet<int> _seen = new();

    public void Sync(GameState s, float dt, int gameSpeed)
    {
        foreach (var d in s.RiderDrops)
        {
            if (!_seen.Add(d.Id)) continue;
            if (d.At < s.Now - 30 || _rides.Count >= 4) continue;   // stale (a loaded game) or the drive is full
            var route = RouteTo(s, d.StationId);
            if (route == null) continue;
            var bike = Motorcycle();
            bike.Position = route[0];
            AddChild(bike);
            _rides.Add(new Ride { Bike = bike, Route = route });
            Sound.Play("motorbike", 0.55f, 0.08f);
        }
        if (_seen.Count > 500) _seen.IntersectWith(s.RiderDrops.Select(d => d.Id));

        float step = dt * Speed * Mathf.Sqrt(Math.Max(1, gameSpeed));
        for (int i = _rides.Count - 1; i >= 0; i--)
            if (!Advance(_rides[i], step, dt))
            {
                _rides[i].Bike.QueueFree();
                _rides.RemoveAt(i);
            }
    }

    /// <summary>Moves a ride along its route; false once it has left the Park.</summary>
    bool Advance(Ride r, float step, float dt)
    {
        if (dt <= 0) return true;
        if (r.Wait >= 0)
        {
            r.Wait -= dt;
            if (r.Wait >= 0) return true;
            r.Route.Reverse();
            r.Leg = 0;
            r.Returning = true;
            Sound.Play("motorbike", 0.4f, 0.08f);
        }
        while (step > 0 && r.Leg < r.Route.Count - 1)
        {
            var to = r.Route[r.Leg + 1];
            var pos = r.Bike.Position;
            var d = to - pos;
            float len = d.Length();
            if (len < 1e-3f) { r.Leg++; continue; }
            float yaw = Mathf.Atan2(d.X, d.Z) - Mathf.Pi / 2;
            r.Bike.Rotation = new Vector3(0, Mathf.LerpAngle(r.Bike.Rotation.Y, yaw, Mathf.Min(1, dt * 10)), 0);
            if (step >= len) { r.Bike.Position = to; step -= len; r.Leg++; }
            else { r.Bike.Position = pos + d / len * step; step = 0; }
        }
        if (r.Leg < r.Route.Count - 1) return true;
        if (r.Returning) return false;
        r.Wait = HandOver;
        return true;
    }

    /// <summary>From off the map, in at the gate and along the ground to the outside door of the desk's building.</summary>
    static List<Vector3>? RouteTo(GameState s, int stationId)
    {
        var st = s.StationById(stationId);
        if (st == null) return null;
        var zone = s.Grid.ZoneAt(st.UseTile.x, st.UseTile.y);
        if (zone == null) return null;
        var door = zone.OutsideDoor;
        var path = Pathfinder.Find(s.Grid, Grid.Gate, door);
        if (path == null) return null;
        var pts = new List<Vector3> { new(-4f, 0, Grid.Gate.y + 0.5f), new(Grid.Gate.x + 0.5f, 0, Grid.Gate.y + 0.5f) };
        // Cut the grid's corners: keep only where the path turns, and ride diagonally between them.
        (int x, int y) prev = Grid.Gate, dir = (0, 0);
        foreach (var p in path)
        {
            var nd = (p.x - prev.x, p.y - prev.y);
            if (nd != dir && prev != Grid.Gate) pts.Add(new Vector3(prev.x + 0.5f, 0, prev.y + 0.5f));
            dir = nd;
            prev = p;
        }
        pts.Add(new Vector3(door.x + 0.5f, 0, door.y + 0.5f));
        return pts;
    }

    /// <summary>A khaki army motorcycle with a dispatch rider aboard; faces +X.</summary>
    static Node3D Motorcycle()
    {
        var root = new Node3D { Name = "Rider" };
        var bike = new Node3D { Scale = Vector3.One * 1.15f };
        root.AddChild(bike);
        var black = new Color("1b1a18");
        var khaki = Gfx.KhakiGreen;
        var steel = new Color("8a8a84");
        foreach (float dx in new[] { -0.24f, 0.24f })
        {
            var tyre = Mesh3.Cyl(bike, 0.14f, 0.14f, 0.06f, new Vector3(dx, 0.14f, 0), black, 14);
            tyre.RotationDegrees = new Vector3(90, 0, 0);
            var hub = Mesh3.Cyl(bike, 0.05f, 0.05f, 0.07f, new Vector3(dx, 0.14f, 0), steel, 8);
            hub.RotationDegrees = new Vector3(90, 0, 0);
            // Mudguards
            Mesh3.Box(bike, new Vector3(0.24f, 0.02f, 0.08f), new Vector3(dx, 0.3f, 0), khaki);
        }
        Mesh3.Box(bike, new Vector3(0.22f, 0.11f, 0.12f), new Vector3(0.03f, 0.37f, 0), khaki);            // tank
        Mesh3.Box(bike, new Vector3(0.18f, 0.14f, 0.1f), new Vector3(-0.02f, 0.21f, 0), new Color("4a4a46")); // engine
        Mesh3.Box(bike, new Vector3(0.42f, 0.03f, 0.03f), new Vector3(0.0f, 0.27f, 0), black).RotationDegrees = new Vector3(0, 0, 8); // frame
        Mesh3.Box(bike, new Vector3(0.03f, 0.22f, 0.03f), new Vector3(0.21f, 0.3f, 0), black).RotationDegrees = new Vector3(0, 0, -20); // forks
        Mesh3.Box(bike, new Vector3(0.03f, 0.03f, 0.26f), new Vector3(0.19f, 0.43f, 0), black);              // handlebars
        Mesh3.Cyl(bike, 0.04f, 0.045f, 0.04f, new Vector3(0.26f, 0.36f, 0), black, 10).RotationDegrees = new Vector3(0, 0, 90);
        Mesh3.Box(bike, new Vector3(0.012f, 0.03f, 0.05f), new Vector3(0.285f, 0.36f, 0), new Color("e8dfae"), 0.6f); // masked headlamp slit
        Mesh3.Box(bike, new Vector3(0.14f, 0.03f, 0.1f), new Vector3(-0.1f, 0.4f, 0), new Color("3b2a1c"));  // saddle
        Mesh3.Cyl(bike, 0.025f, 0.025f, 0.3f, new Vector3(-0.08f, 0.13f, 0.07f), steel, 6).RotationDegrees = new Vector3(0, 0, 90); // exhaust
        // Pannier with the despatch bags
        Mesh3.Box(bike, new Vector3(0.14f, 0.12f, 0.06f), new Vector3(-0.24f, 0.33f, 0.09f), new Color("5a4630"));
        Mesh3.Box(bike, new Vector3(0.14f, 0.12f, 0.06f), new Vector3(-0.24f, 0.33f, -0.09f), new Color("5a4630"));

        // The rider: greatcoat, breeches, boots, steel helmet and goggles, despatch case slung across.
        var coat = new Color("6b6345");
        var skin = new Color("d9b08c");
        Mesh3.Box(bike, new Vector3(0.12f, 0.26f, 0.18f), new Vector3(-0.07f, 0.56f, 0), coat).RotationDegrees = new Vector3(0, 0, -12);
        Mesh3.Sphere(bike, 0.065f, new Vector3(-0.04f, 0.74f, 0), skin);
        var helmet = Mesh3.Dome(bike, 0.08f, new Vector3(-0.045f, 0.76f, 0), new Color("4b4a36"));
        Mesh3.Box(bike, new Vector3(0.02f, 0.025f, 0.09f), new Vector3(0.02f, 0.75f, 0), new Color("2a2f33"));   // goggles
        foreach (float z in new[] { -0.07f, 0.07f })
        {
            Mesh3.Box(bike, new Vector3(0.22f, 0.04f, 0.04f), new Vector3(0.07f, 0.6f, z * 1.2f), coat).RotationDegrees = new Vector3(0, 0, -18); // arms
            Mesh3.Box(bike, new Vector3(0.2f, 0.06f, 0.06f), new Vector3(-0.0f, 0.43f, z * 1.3f), new Color("575038"));                        // thighs
            Mesh3.Box(bike, new Vector3(0.06f, 0.2f, 0.06f), new Vector3(0.08f, 0.3f, z * 1.4f), black);                                        // boots
        }
        Mesh3.Box(bike, new Vector3(0.1f, 0.12f, 0.04f), new Vector3(-0.15f, 0.55f, 0.1f), new Color("4d3a26"));   // despatch case
        return root;
    }
}
