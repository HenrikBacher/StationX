using System.Collections.Generic;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>
/// A person, roughly to scale (about six and a half heads tall), jointed at hip, knee, shoulder and elbow so they
/// can stride, sit at a desk and write, carry a sheaf of paper, or drink tea. Faces +Z locally; the node turns to
/// face where they're going. Joint rotations about X: negative swings a limb forward.
/// </summary>
public partial class PersonModel : Node3D
{
    public int StaffId;

    // Standing measurements (the desk top is 0.44, a chair seat 0.28).
    const float HipY = 0.5f, Thigh = 0.24f, Shin = 0.23f, Upper = 0.17f, Fore = 0.15f, ShoulderY = 0.31f;
    const float SeatY = 0.31f;

    Node3D _hips = null!, _torso = null!, _head = null!;
    Node3D _thighL = null!, _thighR = null!, _shinL = null!, _shinR = null!;
    Node3D _armL = null!, _armR = null!, _elbowL = null!, _elbowR = null!;
    Node3D? _skirt, _lap;
    MeshInstance3D _carry = null!;
    float _phase, _yaw, _idleT, _lookYaw, _lookGoal;
    Vector3 _shown;
    bool _placed;
    float _baseScale = 1;

    public static PersonModel Create(Staff s)
    {
        var p = new PersonModel { StaffId = s.Id, Name = $"Staff{s.Id}" };
        p.Build(s);
        return p;
    }

    // ------------------------------------------------------------------ build

    static MeshInstance3D Capsule(Node3D parent, float r, float len, Vector3 centre, Color c, float squashZ = 1)
    {
        var m = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = r, Height = len + 2 * r, RadialSegments = 10, Rings = 3 },
            Position = centre,
            Scale = new Vector3(1, 1, squashZ),
            MaterialOverride = Mesh3.Mat(c),
        };
        parent.AddChild(m);
        return m;
    }

    static Node3D Joint(Node3D parent, Vector3 at)
    {
        var j = new Node3D { Position = at };
        parent.AddChild(j);
        return j;
    }

    /// <summary>Muted period cloth that still tells the roles apart at a glance.</summary>
    static (Color jacket, Color lower, Color shirt, Color? tie) Clothes(Staff s)
    {
        var jacket = Gfx.RoleColor(s.Role);
        return s.Role switch
        {
            Role.Wren => (jacket, jacket.Darkened(0.05f), new Color("e9e4d6"), new Color("15161a")),
            Role.Clerk => (jacket, s.Female ? new Color("4a4746") : jacket.Darkened(0.12f), new Color("e4ddca"), s.Female ? null : new Color("6e2a2a")),
            Role.Cryptanalyst => (jacket, s.Female ? new Color("5b4a3a") : new Color("5d5a55"), new Color("ded6c2"), s.Female ? null : new Color("2f4361")),
            Role.Linguist => (jacket, s.Female ? new Color("4c4a3e") : new Color("57544d"), new Color("d8cfb4"), s.Female ? null : new Color("5a3a26")),
            Role.Messenger => (jacket, new Color("2a2a30"), new Color("cfc8b8"), null),
            _ => (jacket, jacket.Darkened(0.2f), Gfx.Cream, null),
        };
    }

    void Build(Staff s)
    {
        var skin = Gfx.Skin(s.Look);
        var hair = Gfx.Hair(s.Look);
        var (jacket, lower, shirt, tie) = Clothes(s);
        var shoes = new Color("1d1a17");
        var stockings = skin.Darkened(0.18f).Lerp(new Color("6b5a4a"), 0.25f);

        _hips = Joint(this, new Vector3(0, HipY, 0));

        // Legs: trousers for the men, skirt and stockings for the women.
        Color thighCol = s.Female ? stockings : lower, shinCol = s.Female ? stockings : lower;
        float legR = s.Female ? 0.034f : 0.042f;
        (_thighL, _shinL) = Leg(-0.06f);
        (_thighR, _shinR) = Leg(0.06f);
        (Node3D, Node3D) Leg(float x)
        {
            var thigh = Joint(_hips, new Vector3(x, 0, 0));
            Capsule(thigh, legR + 0.006f, Thigh - 0.04f, new Vector3(0, -Thigh / 2, 0), thighCol);
            var shin = Joint(thigh, new Vector3(0, -Thigh, 0));
            Capsule(shin, legR, Shin - 0.04f, new Vector3(0, -Shin / 2, 0), shinCol);
            Mesh3.Box(shin, new Vector3(0.07f, 0.04f, 0.13f), new Vector3(0, -Shin - 0.005f, 0.025f), shoes);
            return (thigh, shin);
        }

        _torso = Joint(_hips, Vector3.Zero);
        if (s.Female)
        {
            // Knee-length A-line skirt; a separate lap piece when seated.
            _skirt = Joint(_hips, Vector3.Zero);
            var sk = Mesh3.Cyl(_skirt, 0.1f, 0.135f, 0.27f, new Vector3(0, -0.12f, 0), lower, 12);
            sk.Scale = new Vector3(1, 1, 0.78f);
            _lap = Joint(_hips, Vector3.Zero);
            Mesh3.Box(_lap, new Vector3(0.2f, 0.08f, 0.26f), new Vector3(0, 0.0f, 0.11f), lower);
            _lap.Visible = false;
        }
        else
        {
            // Seat of the trousers, so the hip joints don't show.
            var seat = Capsule(_torso, 0.075f, 0.06f, new Vector3(0, 0.0f, 0), lower, 0.75f);
            seat.Rotation = new Vector3(0, 0, Mathf.Pi / 2);
        }

        // Torso: a tapered jacket, flattened front to back, with shoulders.
        var chest = Mesh3.Cyl(_torso, s.Female ? 0.115f : 0.13f, s.Female ? 0.095f : 0.11f, 0.3f, new Vector3(0, 0.16f, 0), jacket, 14);
        chest.Scale = new Vector3(1, 1, 0.66f);
        var shoulders = Capsule(_torso, 0.05f, s.Female ? 0.15f : 0.18f, new Vector3(0, ShoulderY - 0.02f, 0), jacket, 0.9f);
        shoulders.Rotation = new Vector3(0, 0, Mathf.Pi / 2);
        // Shirt front in the V of the jacket, collar, tie.
        var v = Mesh3.Prism(_torso, new Vector3(0.08f, 0.12f, 0.01f), new Vector3(0, ShoulderY - 0.07f, 0.083f), Mesh3.Mat(shirt));
        v.Rotation = new Vector3(0, 0, Mathf.Pi);
        if (tie is Color t)
            Mesh3.Box(_torso, new Vector3(0.022f, 0.1f, 0.01f), new Vector3(0, ShoulderY - 0.09f, 0.089f), t);
        if (s.Role != Role.Messenger && s.Role != Role.Wren)
        {
            // Jacket buttons
            for (int b = 0; b < 2; b++)
                Mesh3.Sphere(_torso, 0.009f, new Vector3(0, 0.13f - b * 0.07f, 0.09f), jacket.Darkened(0.45f));
        }
        if (s.Role == Role.Messenger)
        {
            // Satchel on a strap across the chest
            var strap = Mesh3.Box(_torso, new Vector3(0.025f, 0.38f, 0.01f), new Vector3(0, 0.16f, 0.09f), new Color("4a3826"));
            strap.Rotation = new Vector3(0, 0, 0.6f);
            Mesh3.Box(_torso, new Vector3(0.14f, 0.12f, 0.055f), new Vector3(0.14f, -0.04f, 0.04f), new Color("6b5236"));
        }
        if (s.Notable != null)
            Mesh3.Sphere(_torso, 0.016f, new Vector3(-0.07f, 0.23f, 0.085f), Gfx.Brass);

        // Arms: sleeve, forearm, hand.
        (_armL, _elbowL) = Arm(-1);
        (_armR, _elbowR) = Arm(1);
        (Node3D, Node3D) Arm(int side)
        {
            var sh = Joint(_torso, new Vector3(side * (s.Female ? 0.125f : 0.145f), ShoulderY - 0.02f, 0));
            Capsule(sh, 0.034f, Upper - 0.03f, new Vector3(0, -Upper / 2, 0), jacket.Darkened(0.04f));
            var el = Joint(sh, new Vector3(0, -Upper, 0));
            Capsule(el, 0.03f, Fore - 0.04f, new Vector3(0, -Fore / 2 + 0.01f, 0), jacket.Darkened(0.06f));
            Mesh3.Sphere(el, 0.028f, new Vector3(0, -Fore - 0.005f, 0), skin);
            return (sh, el);
        }

        // Neck and head: slightly long, no cartoon features at this scale.
        Capsule(_torso, 0.03f, 0.03f, new Vector3(0, ShoulderY + 0.035f, 0), skin);
        _head = Joint(_torso, new Vector3(0, ShoulderY + 0.115f, 0));
        var skull = Mesh3.Sphere(_head, 0.072f, Vector3.Zero, skin);
        skull.Scale = new Vector3(0.92f, 1.12f, 1f);
        Mesh3.Box(_head, new Vector3(0.016f, 0.026f, 0.02f), new Vector3(0, -0.01f, 0.07f), skin.Darkened(0.06f));   // nose
        Hair(s, hair);

        _carry = Mesh3.Box(_torso, new Vector3(0.2f, 0.05f, 0.15f), new Vector3(0, 0.12f, 0.17f), Gfx.Paper);
        _carry.Visible = false;
        _baseScale = s.Role == Role.Messenger ? 0.9f : s.Female ? 0.95f : 1f;
        Scale = Vector3.One * _baseScale;
        _phase = (s.Id * 1.37f) % Mathf.Tau;
    }

    void Hair(Staff s, Color hair)
    {
        if (s.Role == Role.Wren)
        {
            // Hair rolled up off the collar, under a navy cap with a tally band.
            var bun = Mesh3.Sphere(_head, 0.06f, new Vector3(0, -0.02f, -0.055f), hair);
            bun.Scale = new Vector3(1.2f, 0.8f, 0.8f);
            Mesh3.Cyl(_head, 0.082f, 0.078f, 0.045f, new Vector3(0, 0.07f, -0.008f), new Color("1a2238"), 16);
            Mesh3.Cyl(_head, 0.079f, 0.079f, 0.015f, new Vector3(0, 0.05f, -0.008f), new Color("101318"), 16);
            Mesh3.Box(_head, new Vector3(0.025f, 0.014f, 0.004f), new Vector3(0, 0.05f, 0.072f), Gfx.Brass);
            return;
        }
        if (s.Role == Role.Messenger)
        {
            var crop = Mesh3.Sphere(_head, 0.075f, new Vector3(0, 0.018f, -0.012f), hair);
            crop.Scale = new Vector3(0.95f, 1f, 1f);
            Mesh3.Cyl(_head, 0.078f, 0.078f, 0.04f, new Vector3(0, 0.07f, -0.005f), new Color("2b2a33"), 16);
            var peak = Mesh3.Box(_head, new Vector3(0.12f, 0.01f, 0.06f), new Vector3(0, 0.055f, 0.075f), new Color("16151a"));
            peak.Rotation = new Vector3(0.25f, 0, 0);
            return;
        }
        if (s.Female)
        {
            // 1940s: hair rolled at the temples and at the nape.
            var cap = Mesh3.Sphere(_head, 0.076f, new Vector3(0, 0.02f, -0.012f), hair);
            cap.Scale = new Vector3(0.98f, 1.05f, 1f);
            foreach (int side in new[] { -1, 1 })
            {
                var roll = Capsule(_head, 0.025f, 0.03f, new Vector3(side * 0.035f, 0.075f, 0.01f), hair);
                roll.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
            }
            var nape = Capsule(_head, 0.03f, 0.08f, new Vector3(0, -0.045f, -0.05f), hair);
            nape.Rotation = new Vector3(0, 0, Mathf.Pi / 2);
            return;
        }
        // Short back and sides, parted; some older men going grey or thin on top.
        bool thinning = (s.Look % 5) == 0 && s.Role == Role.Cryptanalyst;
        var top = Mesh3.Sphere(_head, 0.075f, new Vector3(0, thinning ? 0.005f : 0.022f, -0.016f), thinning ? hair.Lerp(new Color("8a8680"), 0.5f) : hair);
        top.Scale = new Vector3(0.96f, thinning ? 0.85f : 0.95f, 1f);
    }

    // ------------------------------------------------------------- animation

    /// <summary>Where a resting person sits or plays, and whether they sit down.</summary>
    public record struct RestSpot(Vector3 At, Vector3 Face, bool Sit, bool Play);

    /// <param name="seat">For someone operating a desk: the chair to sit on (global) while they work.</param>
    public void Sync(Staff s, GameState g, float dt, Vector3? faceTarget, RestSpot? rest = null, Vector3? seat = null)
    {
        // Off watch: back at the billets, not on the map.
        Visible = !s.Away;
        if (s.Away) { _placed = false; return; }
        var target = new Vector3((float)s.X, 0, (float)s.Y);
        bool atRest = rest != null && s.Path.Count == 0;
        bool operating = s.Task is { Kind: TaskKind.Operate } && s.Path.Count == 0;
        bool deskWork = operating && seat != null && (target - seat.Value).Length() < 1.2f;
        if (atRest) { target = rest!.Value.At; faceTarget = rest.Value.Face; }
        else if (deskWork) target = seat!.Value;
        if (!_placed) { _shown = target; _placed = true; }
        // The sim moves in small steps; glide between them.
        var before = _shown;
        _shown = _shown.Lerp(target, 1 - Mathf.Exp(-dt * 18));
        if ((target - _shown).Length() > 2) _shown = target;
        Position = _shown;

        var moved = _shown - before;
        float speed = dt > 0 ? moved.Length() / dt : 0;
        bool walking = s.Path.Count > 0 || speed > 0.15f;
        if (moved.LengthSquared() > 0.00002f && !deskWork) _yaw = Mathf.Atan2(moved.X, moved.Z);
        else if (faceTarget is Vector3 ft)
        {
            var d = ft - _shown;
            if (d.LengthSquared() > 0.01f) _yaw = Mathf.Atan2(d.X, d.Z);
        }
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, _yaw, 1 - Mathf.Exp(-dt * 12)), 0);

        bool carrying = s.Task is { Kind: TaskKind.Carry, Phase: >= 1 };
        _carry.Visible = carrying;
        if (carrying)
        {
            var first = s.Task!.Msgs.Count > 0 ? g.Messages.GetValueOrDefault(s.Task.Msgs[0]) : null;
            _carry.MaterialOverride = Mesh3.Mat(first == null ? Gfx.Paper : Gfx.Paper.Lerp(Gfx.NetColor(first.Net), 0.25f));
            _carry.Scale = new Vector3(1, 0.5f + 0.25f * System.Math.Min(4, s.Task.Msgs.Count), 1);
        }

        bool tired = s.Energy < Defs.BreakAt;
        bool sitting = !walking && ((atRest && rest!.Value.Sit) || deskWork);
        if (_skirt != null) { _skirt.Visible = !sitting; _lap!.Visible = sitting; }

        Pose p = default;
        if (sitting) p = deskWork ? Writing(dt) : TeaBreak(dt);
        else if (atRest && !walking && rest!.Value.Play) p = Rounders(dt);
        else if (walking) p = Walk(dt, tired, s.Role == Role.Messenger, carrying);
        else if (operating) p = MachineWork(dt);
        else p = Idle(dt, carrying);
        if (tired && !sitting) p.Lean += 0.14f;
        Apply(p, dt);
        Modulate(s.Leaving);
    }

    /// <summary>Joint angles (radians, about X unless named) and hip height for one frame.</summary>
    struct Pose
    {
        public float HipY, Lean, Twist, HeadYaw, HeadPitch;
        public float ThighL, ThighR, ShinL, ShinR;
        public float ArmL, ArmR, ElbowL, ElbowR, ArmOutL, ArmOutR;
        public bool Snap;
    }

    Pose Walk(float dt, bool tired, bool brisk, bool carrying)
    {
        _phase += dt * (tired ? 7.5f : 10f) * (brisk ? 1.25f : 1f);
        float sn = Mathf.Sin(_phase), cs = Mathf.Cos(_phase);
        float stride = brisk ? 0.5f : 0.42f;
        var p = new Pose
        {
            HipY = HipY - 0.012f + Mathf.Abs(cs) * 0.018f,
            Lean = brisk ? 0.08f : 0.04f,
            Twist = sn * 0.07f,
            ThighL = -sn * stride, ThighR = sn * stride,
            // The knee folds as the leg swings through.
            ShinL = Mathf.Max(0, cs) * 0.75f + 0.05f, ShinR = Mathf.Max(0, -cs) * 0.75f + 0.05f,
        };
        if (carrying)
        {
            p.ArmL = p.ArmR = -0.45f;
            p.ElbowL = p.ElbowR = -1.15f;
            p.ArmOutL = p.ArmOutR = 0.12f;
        }
        else
        {
            p.ArmL = sn * 0.38f; p.ArmR = -sn * 0.38f;
            p.ElbowL = -0.2f - Mathf.Max(0, -sn) * 0.35f;
            p.ElbowR = -0.2f - Mathf.Max(0, sn) * 0.35f;
            p.ArmOutL = p.ArmOutR = 0.06f;
        }
        return p;
    }

    Pose Writing(float dt)
    {
        _phase += dt * 9f;
        _idleT += dt;
        // Long stretches of writing, the odd pause to read back or look up.
        bool pause = Mathf.Sin(_idleT * 0.37f + StaffId) > 0.75f;
        float scribble = pause ? 0 : Mathf.Sin(_phase) * 0.06f;
        return new Pose
        {
            HipY = SeatY, Lean = 0.22f, Snap = true,
            HeadPitch = pause ? -0.05f : 0.35f,
            HeadYaw = pause ? Mathf.Sin(_idleT * 0.5f) * 0.3f : 0,
            ThighL = -1.45f, ThighR = -1.5f, ShinL = 1.4f, ShinR = 1.55f,
            ArmL = -0.85f, ElbowL = -0.75f, ArmOutL = 0.12f,
            ArmR = -0.95f + scribble, ElbowR = -0.6f - scribble * 0.8f, ArmOutR = 0.05f + scribble * 0.5f,
        };
    }

    Pose TeaBreak(float dt)
    {
        _phase += dt * 0.9f;
        // Cup to the lips every few seconds.
        float sip = Mathf.Max(0, Mathf.Sin(_phase)) ;
        sip = sip * sip * sip;
        return new Pose
        {
            HipY = SeatY, Lean = -0.05f, Snap = true,
            HeadPitch = -0.1f * sip, HeadYaw = Mathf.Sin(_phase * 0.31f + StaffId) * 0.4f,
            ThighL = -1.45f, ThighR = -1.35f, ShinL = 1.45f, ShinR = 1.2f,
            ArmL = -0.35f, ElbowL = -1.1f, ArmOutL = 0.1f,
            ArmR = -0.45f - sip * 0.6f, ElbowR = -1.3f - sip * 0.9f, ArmOutR = 0.15f,
        };
    }

    Pose Rounders(float dt)
    {
        _phase += dt * 8;
        float sn = Mathf.Sin(_phase);
        return new Pose
        {
            HipY = HipY - 0.03f + Mathf.Max(0, Mathf.Sin(_phase * 0.5f)) * 0.06f,
            Lean = 0.12f, Twist = Mathf.Sin(_phase * 0.5f) * 0.3f,
            ThighL = -0.25f, ThighR = 0.15f, ShinL = 0.4f, ShinR = 0.3f,
            ArmL = -0.5f + sn * 0.5f, ElbowL = -0.6f,
            ArmR = -2.4f + Mathf.Sin(_phase * 0.5f) * 0.4f, ElbowR = -0.3f, ArmOutR = 0.2f,
        };
    }

    Pose MachineWork(float dt)
    {
        _phase += dt * 5f;
        float w = Mathf.Sin(_phase) * 0.18f;
        return new Pose
        {
            HipY = HipY, Lean = 0.1f, HeadPitch = 0.15f,
            ThighL = -0.04f, ThighR = 0.04f, ShinL = 0.06f, ShinR = 0.06f,
            ArmL = -0.75f + w, ElbowL = -0.7f, ArmOutL = 0.1f,
            ArmR = -0.75f - w, ElbowR = -0.7f, ArmOutR = 0.1f,
        };
    }

    Pose Idle(float dt, bool carrying)
    {
        _idleT += dt;
        // Shift weight now and then and glance about.
        if (Mathf.Abs(_lookYaw - _lookGoal) < 0.02f && GD.Randf() < dt * 0.4f) _lookGoal = (GD.Randf() - 0.5f) * 1.1f;
        _lookYaw = Mathf.Lerp(_lookYaw, _lookGoal, 1 - Mathf.Exp(-dt * 3));
        float sway = Mathf.Sin(_idleT * 0.8f + StaffId) * 0.025f;
        return new Pose
        {
            HipY = HipY - 0.004f, HeadYaw = _lookYaw, Twist = sway,
            ThighL = -0.03f + sway, ThighR = 0.05f + sway, ShinL = 0.05f, ShinR = 0.08f,
            ArmL = carrying ? -0.45f : 0.03f, ElbowL = carrying ? -1.15f : -0.12f, ArmOutL = carrying ? 0.12f : 0.05f,
            ArmR = carrying ? -0.45f : 0.03f, ElbowR = carrying ? -1.15f : -0.12f, ArmOutR = carrying ? 0.12f : 0.05f,
        };
    }

    void Apply(Pose p, float dt)
    {
        // Ease into the pose so switching between sitting, walking and standing doesn't pop.
        float k = 1 - Mathf.Exp(-dt * (p.Snap ? 10 : 22));
        static Vector3 L(Vector3 a, Vector3 b, float t) => a.Lerp(b, t);
        _hips.Position = L(_hips.Position, new Vector3(0, p.HipY, 0), k);
        _hips.Rotation = L(_hips.Rotation, new Vector3(0, -p.Twist * 0.5f, 0), k);
        _torso.Rotation = L(_torso.Rotation, new Vector3(p.Lean, p.Twist, 0), k);
        _head.Rotation = L(_head.Rotation, new Vector3(p.HeadPitch - p.Lean * 0.5f, p.HeadYaw - p.Twist, 0), k);
        _thighL.Rotation = L(_thighL.Rotation, new Vector3(p.ThighL, 0, 0), k);
        _thighR.Rotation = L(_thighR.Rotation, new Vector3(p.ThighR, 0, 0), k);
        _shinL.Rotation = L(_shinL.Rotation, new Vector3(p.ShinL, 0, 0), k);
        _shinR.Rotation = L(_shinR.Rotation, new Vector3(p.ShinR, 0, 0), k);
        _armL.Rotation = L(_armL.Rotation, new Vector3(p.ArmL, 0, -p.ArmOutL), k);
        _armR.Rotation = L(_armR.Rotation, new Vector3(p.ArmR, 0, p.ArmOutR), k);
        _elbowL.Rotation = L(_elbowL.Rotation, new Vector3(p.ElbowL, 0, 0), k);
        _elbowR.Rotation = L(_elbowR.Rotation, new Vector3(p.ElbowR, 0, 0), k);
    }

    bool _fading;
    void Modulate(bool leaving)
    {
        if (leaving == _fading) return;
        _fading = leaving;
        Scale = Vector3.One * _baseScale * (leaving ? 0.9f : 1f);
    }

    public Vector3 HeadPoint => GlobalPosition + new Vector3(0, 1.1f, 0);
}
