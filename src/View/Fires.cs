using System.Collections.Generic;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>
/// Fire-watching in the Park (view only): on raid nights a fire-watcher in a tin hat stands by each building with a bucket and
/// a stirrup pump (one per building on the rota); a building on fire smokes heavily with an orange glow under it, and smoulders
/// for a while after.
/// </summary>
public partial class Fires : Node3D
{
    readonly Dictionary<string, (CpuParticles3D smoke, OmniLight3D glow)> _fires = new();
    readonly Dictionary<string, Node3D> _watchers = new();
    static Mesh? _watcherMesh;
    float _t;

    public void Sync(GameState g, double dt, float wind)
    {
        _t += (float)dt;
        bool raid = g.RaidOn && g.FireWatchersPer > 0;
        foreach (var z in g.Grid.Zones)
        {
            if (!z.Unlocked) continue;
            // The watcher, just outside the building's corner nearest the camera.
            bool watch = raid;
            if (watch && !_watchers.ContainsKey(z.Id)) _watchers[z.Id] = Watcher(new Vector3(z.X - 0.45f, 0, z.Y + z.H + 0.35f));
            if (_watchers.TryGetValue(z.Id, out var w) && w.Visible != watch) w.Visible = watch;

            bool burning = g.Burning(z.Id), scorched = g.Scorched(z.Id);
            if ((burning || scorched) && !_fires.ContainsKey(z.Id)) _fires[z.Id] = Fire(new Vector3(z.X + z.W / 2f, 1.6f, z.Y + z.H / 2f));
            if (_fires.TryGetValue(z.Id, out var f))
            {
                bool on = burning || scorched;
                Smoke.Set(f.smoke, on, wind);
                // Setting the amount restarts the particles, so only when it changes.
                int amount = burning ? 48 : 14;
                if (f.smoke.Amount != amount) f.smoke.Amount = amount;
                float scale = burning ? 5f : 1.8f;
                if (f.smoke.ScaleAmountMin != scale) f.smoke.ScaleAmountMin = f.smoke.ScaleAmountMax = scale;
                f.glow.Visible = burning;
                if (burning) f.glow.LightEnergy = 2.2f + 0.8f * Mathf.Sin(_t * 9f) * Mathf.Sin(_t * 3.7f + 1f);
            }
        }
    }

    (CpuParticles3D, OmniLight3D) Fire(Vector3 at)
    {
        var smoke = Smoke.At(this, at);
        // Darker than coal smoke from a chimney: tarred felt and timber.
        var ramp = new Gradient();
        // Lit from below by the flames at first, then grey.
        ramp.SetColor(0, new Color(0.9f, 0.45f, 0.15f, 0f));
        ramp.SetColor(1, new Color(0.5f, 0.49f, 0.48f, 0f));
        ramp.AddPoint(0.06f, new Color(0.85f, 0.42f, 0.16f, 0.9f));
        ramp.AddPoint(0.25f, new Color(0.38f, 0.33f, 0.3f, 0.8f));
        ramp.AddPoint(0.65f, new Color(0.45f, 0.44f, 0.43f, 0.5f));
        smoke.ColorRamp = ramp;
        smoke.Lifetime = 10;
        smoke.EmissionSphereRadius = 0.8f;
        smoke.InitialVelocityMin = 0.35f;
        smoke.InitialVelocityMax = 0.55f;
        var glow = new OmniLight3D
        {
            Position = at + new Vector3(0, -0.6f, 0),
            LightColor = new Color(1f, 0.5f, 0.18f),
            OmniRange = 5.5f,
            LightEnergy = 2.2f,
            ShadowEnabled = false,
            Visible = false,
        };
        AddChild(glow);
        return (smoke, glow);
    }

    Node3D Watcher(Vector3 at)
    {
        var node = new Node3D { Position = at, RotationDegrees = new Vector3(0, 35, 0) };
        AddChild(node);
        node.AddChild(new MeshInstance3D { Mesh = _watcherMesh ??= WatcherMesh(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return node;
    }

    /// <summary>A civilian fire-watcher: dark overcoat, armband, steel helmet, with a bucket and a stirrup pump at their feet.</summary>
    static Mesh WatcherMesh()
    {
        var root = new Node3D();
        var coat = new Color("2f3033");
        var helmet = new Color("3a3b30");
        var skin = new Color("d9b08c");
        foreach (float z in new[] { -0.055f, 0.055f })
            Mesh3.Box(root, new Vector3(0.09f, 0.36f, 0.09f), new Vector3(0, 0.2f, z), new Color("26231f"));
        Mesh3.Box(root, new Vector3(0.18f, 0.44f, 0.27f), new Vector3(0, 0.58f, 0), coat);
        Mesh3.Box(root, new Vector3(0.08f, 0.05f, 0.09f), new Vector3(0, 0.68f, 0.15f), new Color("d8d2c0"));   // armband
        foreach (float z in new[] { -0.16f, 0.16f })
            Mesh3.Box(root, new Vector3(0.07f, 0.32f, 0.07f), new Vector3(0, 0.6f, z), coat);
        Mesh3.Sphere(root, 0.07f, new Vector3(0, 0.86f, 0), skin);
        Mesh3.Dome(root, 0.1f, new Vector3(0, 0.89f, 0), helmet);
        Mesh3.Cyl(root, 0.115f, 0.115f, 0.012f, new Vector3(0, 0.895f, 0), helmet, 14);
        // The bucket and the stirrup pump standing in it.
        Mesh3.Cyl(root, 0.09f, 0.07f, 0.16f, new Vector3(0.28f, 0.08f, 0.05f), new Color("6b6f72"), 12);
        Mesh3.Box(root, new Vector3(0.025f, 0.42f, 0.025f), new Vector3(0.28f, 0.3f, 0.05f), new Color("8a6a3a"));
        Mesh3.Box(root, new Vector3(0.12f, 0.02f, 0.02f), new Vector3(0.28f, 0.5f, 0.05f), new Color("8a6a3a"));
        var mi = Mesh3.MergePainted(root, "FireWatcher");   // frees root
        var mesh = mi.Mesh;
        mi.Free();
        return mesh;
    }
}
