using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>Rain and snow falling around the camera's view of the park.</summary>
public partial class WeatherFx : Node3D
{
    CpuParticles3D _rain = null!, _snow = null!;
    /// <summary>Each particle material and its full-daylight colour: they're unshaded, so they're dimmed by hand at night.</summary>
    readonly System.Collections.Generic.List<(StandardMaterial3D mat, Color day)> _mats = new();
    float _light = 1;
    readonly System.Collections.Generic.List<Node3D> _beams = new();
    float _t;

    public override void _Ready()
    {
        _rain = Make(new BoxMesh { Size = new Vector3(0.015f, 0.45f, 0.015f) }, new Color(0.75f, 0.8f, 0.85f, 0.45f),
            amount: 1600, lifetime: 0.9f, gravity: -32f, spread: 0.6f);
        _snow = Make(new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 4, Rings = 2 }, new Color(0.96f, 0.96f, 0.98f, 0.9f),
            amount: 1400, lifetime: 7f, gravity: -1.6f, spread: 0.9f);

        // Searchlights from the batteries around Bletchley, sweeping during a raid.
        var beamMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.96f, 0.82f, 0.09f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        foreach (var at in new[] { new Vector3(-4, 0, 30), new Vector3(52, 0, 31), new Vector3(24, 0, -5) })
        {
            var pivot = new Node3D { TopLevel = true, Position = at, Visible = false };
            pivot.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 1.6f, BottomRadius = 0.15f, Height = 34, RadialSegments = 12, Rings = 1 },
                Position = new Vector3(0, 17, 0),
                MaterialOverride = beamMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            AddChild(pivot);
            _beams.Add(pivot);
        }
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        for (int i = 0; i < _beams.Count; i++)
        {
            if (!_beams[i].Visible) continue;
            float yaw = _t * (0.23f + 0.07f * i) + i * 2.1f;
            float tilt = 0.45f + 0.2f * Mathf.Sin(_t * 0.31f + i);
            _beams[i].Rotation = new Vector3(tilt, yaw, 0);
        }
    }

    CpuParticles3D Make(Mesh mesh, Color c, int amount, float lifetime, float gravity, float spread)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = c,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        mesh.SurfaceSetMaterial(0, mat);
        _mats.Add((mat, c));
        var p = new CpuParticles3D
        {
            Mesh = mesh,
            Amount = amount,
            Lifetime = lifetime,
            Preprocess = lifetime,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(26, 0.5f, 26),
            Gravity = new Vector3(0.6f, gravity, 0.3f),
            Direction = Vector3.Down,
            Spread = 8,
            InitialVelocityMin = 0,
            InitialVelocityMax = spread,
            Emitting = false,
            Position = new Vector3(0, 13, 0),
            LocalCoords = false,
        };
        AddChild(p);
        return p;
    }

    /// <param name="light">How brightly the falling rain and snow catch the light: 1 by day, low at night.</param>
    public void Sync(Weather w, Vector3 target, bool searchlights = false, float light = 1)
    {
        if (Mathf.Abs(light - _light) > 0.01f)
        {
            _light = light;
            foreach (var (mat, day) in _mats)
                mat.AlbedoColor = new Color(day.R * light, day.G * light, day.B * light, day.A);
        }
        foreach (var b in _beams) if (b.Visible != searchlights) b.Visible = searchlights;
        Position = new Vector3(target.X, 0, target.Z);
        bool rain = w is Weather.Rain or Weather.Storm;
        if (_rain.Emitting != rain) _rain.Emitting = rain;
        if (_snow.Emitting != (w == Weather.Snow)) _snow.Emitting = w == Weather.Snow;
        _rain.Gravity = new Vector3(w == Weather.Storm ? 9 : 0.6f, w == Weather.Storm ? -38 : -32, 0.3f);
    }
}
