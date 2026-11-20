using System;
using Godot;

namespace StationX.View;

/// <summary>
/// A few white farmyard geese on the lake ("the geese that laid the golden eggs and never cackled"). They paddle
/// slowly from one spot to another, bob, and now and then dip their heads; on ice they stand still. Each is two
/// meshes: the body, and the neck and head that dip.
/// </summary>
public partial class Geese : Node3D
{
    const int Count = 4;

    sealed class Goose
    {
        public Node3D Root = null!, Neck = null!;
        public Vector2 At, Target;
        public float Yaw, Phase, Dip, DipIn, Rest;
    }

    readonly Goose[] _geese = new Goose[Count];
    readonly Random _rng = new(1941);

    public override void _Ready()
    {
        for (int i = 0; i < Count; i++)
        {
            var g = new Goose { At = Spot(), Phase = i * 1.7f, DipIn = 3 + i * 2.5f };
            g.Target = Spot();
            g.Root = new Node3D { Name = $"Goose{i}" };
            AddChild(g.Root);
            g.Root.AddChild(Body());
            g.Neck = new Node3D { Position = new Vector3(0, 0.1f, 0.14f) };
            g.Root.AddChild(g.Neck);
            g.Neck.AddChild(NeckAndHead());
            _geese[i] = g;
        }
    }

    /// <summary>Somewhere out on the water, clear of the banks.</summary>
    Vector2 Spot()
    {
        double a = _rng.NextDouble() * Math.Tau, r = Math.Sqrt(_rng.NextDouble()) * 0.7;
        return new Vector2(Water.CentreX + (float)(Math.Cos(a) * r) * Water.RadiusX, Water.CentreZ + (float)(Math.Sin(a) * r) * Water.RadiusZ);
    }

    /// <param name="ice">How frozen the lake is (0..1): on ice they stand, and don't bob.</param>
    public void Sync(float dt, float ice)
    {
        bool frozen = ice > 0.5f;
        foreach (var g in _geese)
        {
            if (g == null) continue;
            g.Phase += dt;
            if (!frozen)
            {
                if (g.Rest > 0) g.Rest -= dt;
                else
                {
                    var to = g.Target - g.At;
                    if (to.Length() < 0.1f) { g.Target = Spot(); g.Rest = 4 + (float)_rng.NextDouble() * 10; }
                    else
                    {
                        g.At += to.Normalized() * Math.Min(to.Length(), 0.12f * dt);
                        float want = Mathf.Atan2(to.X, to.Y);
                        g.Yaw = Mathf.LerpAngle(g.Yaw, want, Math.Min(1, dt * 1.5f));
                    }
                }
            }
            // Head down to feed every so often, for a few seconds.
            if ((g.DipIn -= dt) <= 0) { g.Dip = 2.5f; g.DipIn = 6 + (float)_rng.NextDouble() * 14; }
            g.Dip = Math.Max(0, g.Dip - dt);
            float dip = frozen ? 0 : Mathf.Clamp(Math.Min(g.Dip, 2.5f - g.Dip) * 2, 0, 1);
            float bob = frozen ? 0 : Mathf.Sin(g.Phase * 1.3f) * 0.008f;
            g.Root.Position = new Vector3(g.At.X, (frozen ? 0.03f : 0.0f) + bob, g.At.Y);
            g.Root.Rotation = new Vector3(0, g.Yaw, 0);
            g.Neck.Rotation = new Vector3(dip * 1.9f, 0, 0);
        }
    }

    static readonly Color White = new("ebe8de"), Shade = new("d4d0c4"), Beak = new("e08a2a");

    /// <summary>The body sitting low in the water, nose to +Z, with a raised tail.</summary>
    static MeshInstance3D Body()
    {
        var n = new Node3D();
        var body = Mesh3.Sphere(n, 0.1f, new Vector3(0, 0.07f, 0), White);
        body.Scale = new Vector3(0.95f, 0.8f, 1.85f);
        Mesh3.Sphere(n, 0.06f, new Vector3(0, 0.1f, -0.16f), Shade).Scale = new Vector3(0.9f, 0.7f, 1.2f);   // tail
        foreach (float sx in new[] { -1f, 1f })   // folded wings
            Mesh3.Sphere(n, 0.06f, new Vector3(sx * 0.06f, 0.1f, -0.03f), Shade).Scale = new Vector3(0.6f, 0.6f, 2.0f);
        return Mesh3.MergePainted(n, "Body");
    }

    /// <summary>Neck up from the breast, head and orange beak; the node pivots at the base of the neck.</summary>
    static MeshInstance3D NeckAndHead()
    {
        var n = new Node3D();
        Mesh3.Cyl(n, 0.022f, 0.03f, 0.2f, new Vector3(0, 0.1f, 0.0f), White, 8).Rotation = new Vector3(-0.15f, 0, 0);
        Mesh3.Sphere(n, 0.038f, new Vector3(0, 0.205f, 0.02f), White).Scale = new Vector3(0.9f, 0.9f, 1.3f);
        Mesh3.Box(n, new Vector3(0.026f, 0.02f, 0.05f), new Vector3(0, 0.2f, 0.075f), Beak);
        return Mesh3.MergePainted(n, "Neck");
    }
}
