using Godot;

namespace StationX.View;

/// <summary>
/// Coal smoke from a chimney pot or a stove pipe: a thin stream of soft grey puffs that swell, drift downwind and
/// thin out. One particle system (one draw) each; off in the warm months.
/// </summary>
public static class Smoke
{
    static StandardMaterial3D? _mat;
    static GradientTexture2D? _puff;

    static StandardMaterial3D Mat()
    {
        if (_mat != null) return _mat;
        // A round puff, solid in the middle and fading to nothing at the edge.
        var g = new Gradient();
        g.SetColor(0, new Color(1, 1, 1, 0.9f));
        g.SetColor(1, new Color(1, 1, 1, 0));
        _puff = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f), Width = 32, Height = 32 };
        return _mat = new StandardMaterial3D
        {
            AlbedoTexture = _puff,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            Roughness = 1,
            DisableReceiveShadows = true,
        };
    }

    /// <summary>An emitter at the top of a flue (local to its parent), not yet smoking.</summary>
    public static CpuParticles3D At(Node3D parent, Vector3 top)
    {
        var quad = new QuadMesh { Size = new Vector2(0.5f, 0.5f), Material = Mat() };
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(0.34f, 0.33f, 0.32f, 0.0f));
        ramp.SetColor(1, new Color(0.6f, 0.59f, 0.58f, 0.0f));
        ramp.AddPoint(0.08f, new Color(0.36f, 0.35f, 0.34f, 0.8f));
        ramp.AddPoint(0.55f, new Color(0.5f, 0.49f, 0.48f, 0.45f));
        var grow = new Curve();
        grow.AddPoint(new Vector2(0, 0.4f));
        grow.AddPoint(new Vector2(1, 2.2f));
        var p = new CpuParticles3D
        {
            Mesh = quad,
            Amount = 22,
            Lifetime = 8,
            Preprocess = 8,
            Position = top,
            LocalCoords = false,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.04f,
            Direction = Vector3.Up,
            Spread = 12,
            InitialVelocityMin = 0.18f,
            InitialVelocityMax = 0.28f,
            Gravity = new Vector3(0.12f, 0.02f, 0.06f),
            DampingMin = 0.02f,
            DampingMax = 0.05f,
            ScaleAmountCurve = grow,
            ColorRamp = ramp,
            AngleMin = 0,
            AngleMax = 360,
            Emitting = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(p);
        return p;
    }

    /// <summary>Starts or stops a stream, with the wind (0 calm to 1 a gale) bending it over.</summary>
    public static void Set(CpuParticles3D p, bool on, float wind)
    {
        if (p.Emitting != on) p.Emitting = on;
        if (!on) return;
        var g = new Vector3(0.1f + 0.9f * wind, 0.02f - 0.04f * wind, 0.05f + 0.45f * wind);
        if (!p.Gravity.IsEqualApprox(g)) p.Gravity = g;
    }
}
