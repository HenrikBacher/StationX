using System.Collections.Generic;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>A little person. Faces +Z locally; the node turns to face where they're going.</summary>
public partial class PersonModel : Node3D
{
    public int StaffId;
    Node3D _legL = null!, _legR = null!, _armL = null!, _armR = null!, _body = null!;
    MeshInstance3D _carry = null!;
    float _phase, _yaw;
    Vector3 _shown;
    bool _placed;

    public static PersonModel Create(Staff s)
    {
        var p = new PersonModel { StaffId = s.Id, Name = $"Staff{s.Id}" };
        p.Build(s);
        return p;
    }

    static Node3D Limb(Node3D parent, Vector3 pivot, Vector3 size, Color c)
    {
        var joint = new Node3D { Position = pivot };
        parent.AddChild(joint);
        Mesh3.Box(joint, size, new Vector3(0, -size.Y / 2, 0), c);
        return joint;
    }

    void Build(Staff s)
    {
        var role = Gfx.RoleColor(s.Role);
        var skin = Gfx.Skin(s.Look);
        var hair = Gfx.Hair(s.Look);
        var legs = s.Role == Role.Wren ? new Color("1c2640") : new Color("2f2a26");
        var shoes = new Color("1d1a17");

        _legL = Limb(this, new Vector3(-0.055f, 0.32f, 0), new Vector3(0.075f, 0.32f, 0.08f), s.Female ? skin.Darkened(0.1f) : legs);
        _legR = Limb(this, new Vector3(0.055f, 0.32f, 0), new Vector3(0.075f, 0.32f, 0.08f), s.Female ? skin.Darkened(0.1f) : legs);
        Mesh3.Box(_legL, new Vector3(0.08f, 0.04f, 0.12f), new Vector3(0, -0.31f, 0.02f), shoes);
        Mesh3.Box(_legR, new Vector3(0.08f, 0.04f, 0.12f), new Vector3(0, -0.31f, 0.02f), shoes);

        _body = new Node3D();
        AddChild(_body);
        if (s.Female)
            Mesh3.Cyl(_body, 0.11f, 0.16f, 0.2f, new Vector3(0, 0.3f, 0), s.Role == Role.Wren ? new Color("1c2640") : role.Darkened(0.15f), 10);
        Mesh3.Box(_body, new Vector3(0.27f, 0.32f, 0.16f), new Vector3(0, 0.55f, 0), role);
        // Shirt front / collar
        Mesh3.Box(_body, new Vector3(0.08f, 0.12f, 0.01f), new Vector3(0, 0.64f, 0.081f), s.Role == Role.Linguist ? new Color("d8cfb4") : Gfx.Cream);
        if (s.Role is Role.Clerk or Role.Cryptanalyst)
            Mesh3.Box(_body, new Vector3(0.025f, 0.09f, 0.012f), new Vector3(0, 0.62f, 0.087f), s.Role == Role.Clerk ? new Color("7a2b2b") : new Color("2f4a6a"));

        _armL = Limb(_body, new Vector3(-0.17f, 0.69f, 0), new Vector3(0.065f, 0.3f, 0.075f), role.Darkened(0.08f));
        _armR = Limb(_body, new Vector3(0.17f, 0.69f, 0), new Vector3(0.065f, 0.3f, 0.075f), role.Darkened(0.08f));
        Mesh3.Box(_armL, new Vector3(0.06f, 0.05f, 0.06f), new Vector3(0, -0.31f, 0), skin);
        Mesh3.Box(_armR, new Vector3(0.06f, 0.05f, 0.06f), new Vector3(0, -0.31f, 0), skin);

        // Head
        Mesh3.Box(_body, new Vector3(0.06f, 0.05f, 0.06f), new Vector3(0, 0.73f, 0), skin);
        Mesh3.Sphere(_body, 0.11f, new Vector3(0, 0.85f, 0), skin);
        Mesh3.Box(_body, new Vector3(0.025f, 0.025f, 0.01f), new Vector3(-0.04f, 0.87f, 0.105f), Gfx.Ink);
        Mesh3.Box(_body, new Vector3(0.025f, 0.025f, 0.01f), new Vector3(0.04f, 0.87f, 0.105f), Gfx.Ink);
        if (s.Role == Role.Messenger)
        {
            // Peaked cap, and a satchel on a strap across the chest.
            Mesh3.Sphere(_body, 0.112f, new Vector3(0, 0.9f, -0.02f), hair);
            Mesh3.Cyl(_body, 0.115f, 0.115f, 0.05f, new Vector3(0, 0.96f, -0.01f), new Color("2b2a33"), 14);
            Mesh3.Box(_body, new Vector3(0.16f, 0.015f, 0.08f), new Vector3(0, 0.94f, 0.1f), new Color("1e1d22"));
            var strap = Mesh3.Box(_body, new Vector3(0.03f, 0.42f, 0.01f), new Vector3(0, 0.56f, 0.085f), new Color("4a3826"));
            strap.Rotation = new Vector3(0, 0, 0.6f);
            Mesh3.Box(_body, new Vector3(0.16f, 0.13f, 0.06f), new Vector3(0.14f, 0.38f, 0.04f), new Color("6b5236"));
        }
        else if (s.Role == Role.Wren)
        {
            // Wren's cap: navy crown with a white band
            Mesh3.Cyl(_body, 0.12f, 0.12f, 0.05f, new Vector3(0, 0.95f, -0.01f), new Color("1c2640"), 14);
            Mesh3.Cyl(_body, 0.115f, 0.115f, 0.02f, new Vector3(0, 0.98f, -0.01f), Gfx.Cream, 14);
            Mesh3.Sphere(_body, 0.09f, new Vector3(0, 0.83f, -0.07f), hair);
        }
        else
        {
            Mesh3.Sphere(_body, 0.115f, new Vector3(0, 0.89f, -0.025f), hair);
            if (s.Female) Mesh3.Sphere(_body, 0.06f, new Vector3(0, 0.84f, -0.13f), hair);
        }
        if (s.Notable != null)
            Mesh3.Sphere(_body, 0.025f, new Vector3(-0.08f, 0.66f, 0.085f), Gfx.Brass);

        _carry = Mesh3.Box(_body, new Vector3(0.22f, 0.06f, 0.16f), new Vector3(0, 0.5f, 0.2f), Gfx.Paper);
        _carry.Visible = false;
        _baseScale = s.Role == Role.Messenger ? 0.88f : 1f;
        Scale = Vector3.One * _baseScale;
    }

    /// <summary>Where a resting person sits or plays, and whether they sit down.</summary>
    public record struct RestSpot(Vector3 At, Vector3 Face, bool Sit, bool Play);

    public void Sync(Staff s, GameState g, float dt, Vector3? faceTarget, RestSpot? rest = null)
    {
        // Off watch: back at the billets, not on the map.
        Visible = !s.Away;
        if (s.Away) { _placed = false; return; }
        var target = new Vector3((float)s.X, 0, (float)s.Y);
        bool resting = rest != null && s.Path.Count == 0;
        if (resting) { target = rest!.Value.At; faceTarget = rest.Value.Face; }
        if (!_placed) { _shown = target; _placed = true; }
        // The sim moves in small steps; glide between them.
        var before = _shown;
        _shown = _shown.Lerp(target, 1 - Mathf.Exp(-dt * 18));
        if ((target - _shown).Length() > 2) _shown = target;
        Position = _shown;

        var moved = _shown - before;
        bool walking = s.Path.Count > 0 || moved.Length() > 0.002f;
        if (moved.LengthSquared() > 0.00002f) _yaw = Mathf.Atan2(moved.X, moved.Z);
        else if (faceTarget is Vector3 ft)
        {
            var d = ft - _shown;
            if (d.LengthSquared() > 0.01f) _yaw = Mathf.Atan2(d.X, d.Z);
        }
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, _yaw, 1 - Mathf.Exp(-dt * 14)), 0);

        bool carrying = s.Task is { Kind: TaskKind.Carry, Phase: >= 1 };
        bool working = s.Task is { Kind: TaskKind.Operate } && !walking;
        _carry.Visible = carrying;
        if (carrying)
        {
            var first = s.Task!.Msgs.Count > 0 ? g.Messages.GetValueOrDefault(s.Task.Msgs[0]) : null;
            _carry.MaterialOverride = Mesh3.Mat(first == null ? Gfx.Paper : Gfx.Paper.Lerp(Gfx.NetColor(first.Net), 0.25f));
            _carry.Scale = new Vector3(1, 0.5f + 0.25f * System.Math.Min(4, s.Task.Msgs.Count), 1);
        }

        bool tired = s.Energy < Defs.BreakAt;
        // Tired people slouch and trudge.
        _body.Rotation = new Vector3(tired && !resting ? 0.22f : 0, 0, 0);
        if (resting && !walking && rest!.Value.Sit)
        {
            _legL.Rotation = _legR.Rotation = new Vector3(1.45f, 0, 0);
            _body.Position = new Vector3(0, -0.2f, 0);
            _phase += dt * 2;
            _armL.Rotation = new Vector3(-0.7f + Mathf.Sin(_phase) * 0.1f, 0, 0);   // tea and a biscuit
            _armR.Rotation = new Vector3(-0.3f, 0, 0);
        }
        else if (resting && !walking && rest!.Value.Play)
        {
            _phase += dt * 8;
            _legL.Rotation = _legR.Rotation = Vector3.Zero;
            _armL.Rotation = new Vector3(-0.5f + Mathf.Sin(_phase) * 0.6f, 0, 0);
            _armR.Rotation = new Vector3(-2.5f + Mathf.Sin(_phase * 0.5f) * 0.4f, 0, 0);
            _body.Position = new Vector3(0, Mathf.Max(0, Mathf.Sin(_phase * 0.5f)) * 0.06f, 0);
        }
        else if (walking)
        {
            _phase += dt * (tired ? 8 : 11) * (s.Role == Role.Messenger ? 1.3f : 1f);
            float swing = Mathf.Sin(_phase) * 0.6f;
            _legL.Rotation = new Vector3(swing, 0, 0);
            _legR.Rotation = new Vector3(-swing, 0, 0);
            _armL.Rotation = new Vector3(carrying ? -1.2f : -swing * 0.8f, 0, 0);
            _armR.Rotation = new Vector3(carrying ? -1.2f : swing * 0.8f, 0, 0);
            _body.Position = new Vector3(0, Mathf.Abs(Mathf.Cos(_phase)) * 0.025f, 0);
        }
        else
        {
            _phase += dt * 6;
            _legL.Rotation = _legR.Rotation = Vector3.Zero;
            float work = working ? Mathf.Sin(_phase) * 0.25f : 0;
            _armL.Rotation = new Vector3(working ? -0.9f + work : carrying ? -1.2f : 0, 0, 0);
            _armR.Rotation = new Vector3(working ? -0.9f - work : carrying ? -1.2f : 0, 0, 0);
            _body.Position = Vector3.Zero;
        }
        Modulate(s.Leaving);
    }

    bool _fading;
    float _baseScale = 1;
    void Modulate(bool leaving)
    {
        if (leaving == _fading) return;
        _fading = leaving;
        Scale = Vector3.One * _baseScale * (leaving ? 0.9f : 1f);
    }

    public Vector3 HeadPoint => GlobalPosition + new Vector3(0, 1.1f, 0);
}
