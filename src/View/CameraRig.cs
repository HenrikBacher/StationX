using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>
/// Fixed isometric camera: orthographic, looking down from the south-east at the classic angle,
/// so the south and east walls are the ones nearest the viewer (and are cut away).
/// </summary>
public partial class CameraRig : Node3D
{
    public Camera3D Cam = null!;
    public const float Pitch = 35.264f; // true isometric
    public float MinSize = 7, MaxSize = 42;

    /// <summary>Which quarter the camera looks from: yaw 45° + 90°·Quarter. 0 is from the south-east.</summary>
    public int Quarter { get; private set; }
    float _yaw = 45, _targetYaw = 45;

    public override void _Ready()
    {
        RotationDegrees = new Vector3(-Pitch, _yaw, 0);
        Cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 18,
            Near = 1,
            Far = 300,
            Position = new Vector3(0, 0, 120),
        };
        AddChild(Cam);
        Cam.MakeCurrent();
    }

    /// <summary>The ground point (y = 0) the camera looks at, in tiles.</summary>
    public Vector3 Target
    {
        get => Position;
        set => Position = new Vector3(Mathf.Clamp(value.X, -4, Grid.Width + 4), 0, Mathf.Clamp(value.Z, -4, Grid.Height + 4));
    }

    public void LookAtTile(float x, float y) => Target = new Vector3(x, 0, y);

    /// <summary>Turn the view a quarter round, keeping the same spot in the middle of the screen.</summary>
    public void Rotate(int steps)
    {
        Quarter = ((Quarter + steps) % 4 + 4) % 4;
        _targetYaw += steps * 90;
    }

    public void SetQuarter(int q)
    {
        Quarter = ((q % 4) + 4) % 4;
        _yaw = _targetYaw = 45 + 90 * Quarter;
        RotationDegrees = new Vector3(-Pitch, _yaw, 0);
    }

    public bool Turning => Mathf.Abs(_yaw - _targetYaw) > 0.01f;

    public override void _Process(double delta)
    {
        if (!Turning) return;
        _yaw = Mathf.Lerp(_yaw, _targetYaw, 1 - Mathf.Exp(-(float)delta * 10));
        if (Mathf.Abs(_yaw - _targetYaw) < 0.25f) _yaw = _targetYaw;
        RotationDegrees = new Vector3(-Pitch, _yaw, 0);
    }

    /// <summary>Screen-right and screen-up, flattened onto the ground.</summary>
    public Vector3 GroundRight => new Vector3(GlobalBasis.X.X, 0, GlobalBasis.X.Z).Normalized();
    public Vector3 GroundUp => -new Vector3(GlobalBasis.Z.X, 0, GlobalBasis.Z.Z).Normalized();

    public void PanPixels(Vector2 delta)
    {
        float unitsPerPixel = Cam.Size / GetViewport().GetVisibleRect().Size.Y;
        float upScale = 1f / Mathf.Sin(Mathf.DegToRad(Pitch));
        Target = Target - GroundRight * delta.X * unitsPerPixel + GroundUp * delta.Y * unitsPerPixel * upScale;
    }

    public void PanScreen(Vector2 dir, float unitsPerSecond, float dt)
    {
        float upScale = 1f / Mathf.Sin(Mathf.DegToRad(Pitch));
        Target = Target + (GroundRight * dir.X - GroundUp * dir.Y * upScale) * unitsPerSecond * dt * Cam.Size / 18f;
    }

    /// <summary>Where the mouse ray meets the horizontal plane at height y.</summary>
    public Vector3? GroundUnder(Vector2 screen, float y = 0)
    {
        var from = Cam.ProjectRayOrigin(screen);
        var dir = Cam.ProjectRayNormal(screen);
        var hit = new Plane(Vector3.Up, y).IntersectsRay(from, dir);
        return hit;
    }

    public void ZoomAt(float factor, Vector2 screen)
    {
        var before = GroundUnder(screen);
        Cam.Size = Mathf.Clamp(Cam.Size / factor, MinSize, MaxSize);
        var after = GroundUnder(screen);
        if (before is Vector3 b && after is Vector3 a) Target = Target + (b - a);
    }
}
