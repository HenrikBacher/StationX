using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>Rain and snow falling around the camera's view of the park.</summary>
public partial class WeatherFx : Node3D
{
    CpuParticles3D _rain = null!, _snow = null!;

    public override void _Ready()
    {
        _rain = Make(new BoxMesh { Size = new Vector3(0.015f, 0.45f, 0.015f) }, new Color(0.75f, 0.8f, 0.85f, 0.45f),
            amount: 1600, lifetime: 0.9f, gravity: -32f, spread: 0.6f);
        _snow = Make(new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 4, Rings = 2 }, new Color(0.96f, 0.96f, 0.98f, 0.9f),
            amount: 1400, lifetime: 7f, gravity: -1.6f, spread: 0.9f);
    }

    CpuParticles3D Make(Mesh mesh, Color c, int amount, float lifetime, float gravity, float spread)
    {
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            AlbedoColor = c,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        });
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

    public void Sync(Weather w, Vector3 target)
    {
        Position = new Vector3(target.X, 0, target.Z);
        bool rain = w is Weather.Rain or Weather.Storm;
        if (_rain.Emitting != rain) _rain.Emitting = rain;
        if (_snow.Emitting != (w == Weather.Snow)) _snow.Emitting = w == Weather.Snow;
        _rain.Gravity = new Vector3(w == Weather.Storm ? 9 : 0.6f, w == Weather.Storm ? -38 : -32, 0.3f);
    }
}
