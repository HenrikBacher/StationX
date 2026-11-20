using System;
using System.Collections.Generic;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>
/// The billets bus: a green and cream single-decker that brings the far-billeted in before their watch and takes them
/// home after it (<see cref="Commuters"/> says when it's wanted). It comes up the left of the road, stops short of the
/// gate with its door by the stop, waits while its passengers get off or on, then turns in the gateway and goes.
/// One merged mesh; it runs on game time like the commuters.
/// </summary>
public partial class Bus : Node3D
{
    enum Leg { Away, Coming, Waiting, Going }

    Leg _leg = Leg.Away;
    double _legFrom;
    Node3D _body = null!;
    List<Vector3> _in = null!, _out = null!;
    float[] _inCum = null!, _outCum = null!;

    /// <summary>The bus's middle when stopped: its door (front nearside) at <see cref="Commuters.StopPoint"/>.</summary>
    static Vector3 Stopped => Commuters.StopPoint + new Vector3(-1.1f, 0, 0.62f);

    public override void _Ready()
    {
        _body = new Node3D { Visible = false };
        AddChild(_body);
        _body.AddChild(Model());
        var stop = Stopped;
        const float far = -22;
        // In along the left-hand lane; out by turning in the mouth of the gateway and back down the other side.
        _in = new List<Vector3> { new(far, 0, stop.Z), stop };
        _out = new List<Vector3> { stop, stop + new Vector3(1.6f, 0, 0) };
        var c = stop + new Vector3(1.6f, 0, 0.8f);
        for (int k = 1; k <= 8; k++)
        {
            float a = -Mathf.Pi / 2 + k * Mathf.Pi / 8;   // a half circle to the right, round to heading back down the road
            _out.Add(c + new Vector3(Mathf.Cos(a) * 0.8f, 0, Mathf.Sin(a) * 0.8f));
        }
        _out.Add(new Vector3(far, 0, c.Z + 0.8f));
        _inCum = Cum(_in);
        _outCum = Cum(_out);
    }

    static float[] Cum(List<Vector3> pts)
    {
        var c = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++) c[i] = c[i - 1] + (pts[i] - pts[i - 1]).Length();
        return c;
    }

    public void Clear()
    {
        _leg = Leg.Away;
        if (_body != null) _body.Visible = false;
    }

    public void Sync(GameState g, bool wanted)
    {
        double now = g.Now;
        double into = now - _legFrom;
        switch (_leg)
        {
            case Leg.Away when wanted: Go(Leg.Coming, now); break;
            case Leg.Coming when into >= Commuters.BusDrive: Go(Leg.Waiting, now); break;
            case Leg.Waiting when !wanted && into > 2: Go(Leg.Going, now); break;
            case Leg.Going when into >= Commuters.BusDrive: Go(Leg.Away, now); break;
        }
        into = Math.Max(0, now - _legFrom);
        bool shown = _leg != Leg.Away;
        if (_body.Visible != shown) _body.Visible = shown;
        if (!shown) return;
        // Slowing to the stop, pulling away gently: eased along each leg.
        float t = (float)Math.Clamp(into / Commuters.BusDrive, 0, 1);
        switch (_leg)
        {
            case Leg.Coming: Place(_in, _inCum, 1 - (1 - t) * (1 - t)); break;
            case Leg.Waiting: Place(_in, _inCum, 1); break;
            case Leg.Going: Place(_out, _outCum, t * t); break;
        }
    }

    void Go(Leg leg, double now)
    {
        _leg = leg;
        _legFrom = now;
    }

    void Place(List<Vector3> pts, float[] cum, float f)
    {
        float d = f * cum[^1];
        int i = 1;
        while (i < cum.Length - 1 && cum[i] < d) i++;
        float seg = cum[i] - cum[i - 1];
        var a = pts[i - 1];
        var b = pts[i];
        _body.Position = seg > 1e-4f ? a.Lerp(b, (d - cum[i - 1]) / seg) : b;
        var dir = b - a;
        if (dir.LengthSquared() > 1e-6f) _body.Rotation = new Vector3(0, Mathf.Atan2(-dir.Z, dir.X), 0);   // the model faces +X
    }

    /// <summary>A pre-war half-cab single-decker, nose to +X, door at the front on the nearside (-Z).</summary>
    static MeshInstance3D Model()
    {
        var n = new Node3D();
        var green = new Color("2f5a3c");
        var cream = new Color("e3d9b8");
        var glass = new Color("2b3337");
        var black = new Color("181716");
        var steel = new Color("a7a59c");
        const float x0 = -1.6f, x1 = 1.12f, len = x1 - x0, w = 0.92f;
        Mesh3.Box(n, new Vector3(len + 0.4f, 0.12f, w - 0.1f), new Vector3((x0 + x1 + 0.4f) / 2, 0.24f, 0), black);              // chassis
        Mesh3.Box(n, new Vector3(len, 0.55f, w), new Vector3((x0 + x1) / 2, 0.58f, 0), green);                                    // lower panels
        Mesh3.Box(n, new Vector3(len + 0.01f, 0.06f, w + 0.01f), new Vector3((x0 + x1) / 2, 0.86f, 0), cream);                    // waist band
        Mesh3.Box(n, new Vector3(len - 0.02f, 0.42f, w - 0.02f), new Vector3((x0 + x1) / 2, 1.1f, 0), glass);                     // windows
        for (float x = x0 + 0.04f; x <= x1; x += 0.39f)                                                                          // pillars
            Mesh3.Box(n, new Vector3(0.05f, 0.42f, w + 0.01f), new Vector3(x, 1.1f, 0), cream);
        Mesh3.Box(n, new Vector3(len + 0.02f, 0.1f, w + 0.02f), new Vector3((x0 + x1) / 2, 1.36f, 0), cream);                    // cantrail
        Mesh3.Box(n, new Vector3(len - 0.1f, 0.06f, w - 0.14f), new Vector3((x0 + x1) / 2, 1.43f, 0), steel);                    // roof
        Mesh3.Box(n, new Vector3(0.02f, 0.11f, 0.5f), new Vector3(x1 + 0.01f, 1.25f, 0), black);                                 // destination blind
        // The half cab: the driver's cab on the offside (+Z), the engine's bonnet beside it.
        Mesh3.Box(n, new Vector3(0.42f, 0.62f, 0.44f), new Vector3(x1 + 0.21f, 0.62f, 0.24f), green);
        Mesh3.Box(n, new Vector3(0.36f, 0.32f, 0.4f), new Vector3(x1 + 0.2f, 1.08f, 0.24f), glass);
        Mesh3.Box(n, new Vector3(0.42f, 0.06f, 0.46f), new Vector3(x1 + 0.21f, 1.27f, 0.24f), cream);
        Mesh3.Box(n, new Vector3(0.44f, 0.34f, 0.4f), new Vector3(x1 + 0.22f, 0.5f, -0.22f), green);                             // bonnet
        Mesh3.Box(n, new Vector3(0.03f, 0.3f, 0.3f), new Vector3(x1 + 0.45f, 0.5f, -0.22f), steel);                              // radiator
        foreach (float z in new[] { -0.38f, 0.38f })
        {
            var lamp = Mesh3.Cyl(n, 0.05f, 0.05f, 0.04f, new Vector3(x1 + 0.46f, 0.66f, z), steel, 10);
            lamp.RotationDegrees = new Vector3(0, 0, 90);
            Mesh3.Box(n, new Vector3(0.01f, 0.015f, 0.05f), new Vector3(x1 + 0.485f, 0.66f, z), new Color("d9cf9a"));               // masked slit
        }
        // The open door at the front of the nearside, its step below.
        Mesh3.Box(n, new Vector3(0.3f, 0.95f, 0.02f), new Vector3(x1 - 0.12f, 0.78f, -w / 2 - 0.005f), black);
        Mesh3.Box(n, new Vector3(0.3f, 0.05f, 0.14f), new Vector3(x1 - 0.12f, 0.24f, -w / 2 - 0.04f), steel);
        foreach (float x in new[] { x1 + 0.12f, x0 + 0.55f })
            foreach (float z in new[] { -w / 2 + 0.06f, w / 2 - 0.06f })
            {
                var tyre = Mesh3.Cyl(n, 0.24f, 0.24f, 0.13f, new Vector3(x, 0.24f, z), black, 16);
                tyre.RotationDegrees = new Vector3(90, 0, 0);
                var hub = Mesh3.Cyl(n, 0.1f, 0.1f, 0.14f, new Vector3(x, 0.24f, z), steel.Darkened(0.3f), 10);
                hub.RotationDegrees = new Vector3(90, 0, 0);
            }
        return Mesh3.MergePainted(n, "Bus");
    }
}
