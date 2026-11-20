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

    // Built from jointed parts (below), then baked into one skinned mesh: a bone per joint, every part rigidly on its
    // joint's bone, coloured per vertex. One draw per figure (and per shadow pass) instead of one per part.
    Node3D _hips = null!, _torso = null!, _head = null!;
    Node3D _thighL = null!, _thighR = null!, _shinL = null!, _shinR = null!;
    Node3D _armL = null!, _armR = null!, _elbowL = null!, _elbowR = null!;
    Node3D? _skirt, _lap;
    MeshInstance3D _carry = null!;

    // Bones, in this order; each one's parent.
    const int BHips = 0, BTorso = 1, BHead = 2, BThighL = 3, BShinL = 4, BThighR = 5, BShinR = 6,
              BArmL = 7, BElbowL = 8, BArmR = 9, BElbowR = 10, BSkirt = 11, BLap = 12, Bones = 13;
    static readonly int[] BoneParent = { -1, BHips, BTorso, BHips, BThighL, BHips, BThighR, BTorso, BArmL, BTorso, BArmR, BHips, BHips };
    Skeleton3D _skel = null!;
    /// <summary>Each joint's place on its parent (fixed, but for the hips), and its current rotation (Euler, YXZ).</summary>
    readonly Vector3[] _jointAt = new Vector3[Bones], _rot = new Vector3[Bones];
    Vector3 _hipAt;
    bool _hasSkirt, _seatedSkirt;
    /// <summary>Where the carried sheaf sits on the torso.</summary>
    Transform3D _carryOnTorso;
    float _phase, _yaw, _idleT, _lookYaw, _lookGoal;
    Vector3 _shown;
    bool _placed;

    /// <summary>Sitting at a desk or a table, or playing rounders: others step round them, not the other way.</summary>
    public bool Settled { get; private set; }
    public Vector3 Shown => _shown;

    /// <summary>Sidestep: shifts the figure; the next frames glide it back on course.</summary>
    public void Nudge(Vector3 by)
    {
        _shown += by;
        Position = _shown;
    }
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
            Mesh = new CapsuleMesh { Radius = r, Height = len + 2 * r, RadialSegments = 16, Rings = 5 },
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

    /// <summary>A stable random number in 0..1 for this person and this trait.</summary>
    static float Trait(Staff s, int salt) => Gfx.Rand01(s.Id * 31 + 7, s.Look, salt);
    static T PickFor<T>(Staff s, int salt, T[] options) => options[(int)(Trait(s, salt) * options.Length * 0.9999f)];

    static readonly TorusMesh GlassRing = new() { InnerRadius = 0.013f, OuterRadius = 0.018f, Rings = 12, RingSegments = 4 };
    static readonly Color[] Ties = { new("6e2a2a"), new("2f4361"), new("5a3a26"), new("2c4a33"), new("4a2c4a"), new("7a5a2a"), new("1f1f24") };
    static readonly Color[] Tweeds = { new("8a6a43"), new("7a6545"), new("6f5a3c"), new("8f7550"), new("6a5f45"), new("7d5e3e") };
    /// <summary>College scarf colours: (main, stripe).</summary>
    static readonly (Color, Color)[] Scarves =
    {
        (new("2b3a6b"), new("c9b06a")), (new("7a1f2a"), new("d8cfb4")), (new("1f4a33"), new("d8cfb4")),
        (new("3a2a4a"), new("c9a46a")), (new("8a5a1f"), new("1f1f24")), (new("2a5a7a"), new("e8e2d0")),
    };

    /// <summary>
    /// Period cloth. Each role has a signature that reads from the game camera, and each person their own shade:
    /// clerks in shirtsleeves under a waistcoat or a grey cardigan, cryptanalysts in tweed with a college scarf,
    /// linguists in green with spectacles, Wrens in navy uniform, messengers in dark red.
    /// </summary>
    static (Color jacket, Color lower, Color shirt, Color? tie) Clothes(Staff s)
    {
        float shade = (Trait(s, 1) - 0.5f) * 0.16f;
        Color Vary(Color c) => shade >= 0 ? c.Lightened(shade) : c.Darkened(-shade);
        var tie = PickFor(s, 2, Ties);
        var shirt = PickFor(s, 3, new[] { new Color("e9e4d6"), new Color("e4ddca"), new Color("dfe3e6"), new Color("e8dccf") });
        return s.Role switch
        {
            Role.Wren => (Gfx.RoleColor(Role.Wren), Gfx.RoleColor(Role.Wren).Darkened(0.05f), new Color("e9e4d6"), new Color("15161a")),
            Role.Clerk => (Vary(s.Female ? PickFor(s, 4, new[] { new Color("6b6a70"), new Color("7a6f78"), new Color("5f6670") }) : Gfx.RoleColor(Role.Clerk)),
                           Vary(s.Female ? new Color("4a4746") : new Color("4f4e52")), shirt, s.Female ? null : tie),
            Role.Cryptanalyst => (Vary(PickFor(s, 4, Tweeds)), Vary(s.Female ? new Color("5b4a3a") : new Color("5d5a55")), shirt, s.Female ? null : tie),
            Role.Linguist => (Vary(Gfx.RoleColor(Role.Linguist)), Vary(s.Female ? new Color("4c4a3e") : new Color("57544d")), shirt, null),
            Role.Messenger => (Vary(Gfx.RoleColor(Role.Messenger)), new Color("2a2a30"), new Color("cfc8b8"), null),
            _ => (Gfx.RoleColor(s.Role), Gfx.RoleColor(s.Role).Darkened(0.2f), Gfx.Cream, null),
        };
    }

    void Build(Staff s)
    {
        var skin = Gfx.Skin(s.Look);
        var hair = Gfx.Hair(s.Look);
        var (jacket, lower, shirt, tie) = Clothes(s);
        if (Trait(s, 20) < 0.16f && s.Role != Role.Messenger) hair = hair.Lerp(new Color("a8a49c"), 0.75f);   // going grey
        float build = 0.9f + Trait(s, 21) * 0.22f;     // slim to stout
        bool shirtSleeves = s.Role == Role.Clerk && !s.Female;
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
        chest.Scale = new Vector3(build, 1, 0.66f * (0.9f + 0.1f * build));
        var shoulders = Capsule(_torso, 0.05f, (s.Female ? 0.15f : 0.18f) * build, new Vector3(0, ShoulderY - 0.02f, 0), shirtSleeves ? shirt : jacket, 0.9f);
        shoulders.Rotation = new Vector3(0, 0, Mathf.Pi / 2);
        // Shirt front in the V of the jacket, collar, tie.
        var v = Mesh3.Prism(_torso, new Vector3(0.08f, 0.12f, 0.01f), new Vector3(0, ShoulderY - 0.07f, 0.083f), Mesh3.Mat(shirt));
        v.Rotation = new Vector3(0, 0, Mathf.Pi);
        if (tie is Color t)
            Mesh3.Box(_torso, new Vector3(0.022f, 0.1f, 0.01f), new Vector3(0, ShoulderY - 0.09f, 0.089f), t);
        if (shirtSleeves)   // a watch chain across the waistcoat
            Mesh3.Box(_torso, new Vector3(0.09f, 0.006f, 0.006f), new Vector3(0, 0.1f, 0.093f), Gfx.Brass);
        if (s.Role == Role.Cryptanalyst)
        {
            // The long striped college scarf: round the neck, both ends hanging down the front
            var (main, stripe) = PickFor(s, 5, Scarves);
            Mesh3.Cyl(_torso, 0.068f, 0.075f, 0.05f, new Vector3(0, ShoulderY + 0.02f, 0), main, 14);
            float len = 0.26f + Trait(s, 6) * 0.08f;
            const int bands = 6;
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * 0.04f;
                for (int k = 0; k < bands; k++)
                    Mesh3.Box(_torso, new Vector3(0.045f, len / bands, 0.014f),
                              new Vector3(x, ShoulderY - 0.01f - (k + 0.5f) * len / bands, 0.094f + side * 0.003f), k % 2 == 0 ? main : stripe);
                Mesh3.Box(_torso, new Vector3(0.045f, 0.02f, 0.008f), new Vector3(x, ShoulderY - 0.02f - len, 0.094f), stripe);   // fringe
            }
        }
        if (s.Role == Role.Linguist && !s.Female)
        {
            var bow = Mesh3.Mat(PickFor(s, 7, Ties));
            foreach (int side in new[] { -1, 1 })
            {
                var wing = Mesh3.Prism(_torso, new Vector3(0.03f, 0.022f, 0.01f), new Vector3(side * 0.016f, ShoulderY - 0.035f, 0.088f), bow);
                wing.Rotation = new Vector3(0, 0, side * Mathf.Pi / 2);
            }
        }
        if (s.Role == Role.Linguist && s.Female)   // a string of pearls
            for (int k = -3; k <= 3; k++)
                Mesh3.Sphere(_torso, 0.008f, new Vector3(k * 0.012f, ShoulderY - 0.035f - 0.003f * (3 - Mathf.Abs(k)), 0.082f), new Color("efe9dc"));
        if (s.Role != Role.Messenger && s.Role != Role.Wren && !shirtSleeves)
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
        bool leadingWren = s.Role == Role.Wren && s.Skill >= 3;

        // Arms: sleeve, forearm, hand.
        (_armL, _elbowL) = Arm(-1);
        (_armR, _elbowR) = Arm(1);
        (Node3D, Node3D) Arm(int side)
        {
            var sh = Joint(_torso, new Vector3(side * (s.Female ? 0.125f : 0.145f) * build, ShoulderY - 0.02f, 0));
            var sleeve = shirtSleeves ? shirt : jacket;
            Capsule(sh, 0.034f, Upper - 0.03f, new Vector3(0, -Upper / 2, 0), sleeve.Darkened(0.04f));
            var el = Joint(sh, new Vector3(0, -Upper, 0));
            Capsule(el, 0.03f, Fore - 0.04f, new Vector3(0, -Fore / 2 + 0.01f, 0), sleeve.Darkened(0.06f));
            if (leadingWren && side < 0)   // a Leading Wren's red anchor badge on the left sleeve
                Mesh3.Box(sh, new Vector3(0.012f, 0.03f, 0.03f), new Vector3(-0.033f, -Upper * 0.35f, 0.0f), new Color("a3392b"));
            if (shirtSleeves)   // black sleeve garters above the elbow
                Mesh3.Cyl(sh, 0.037f, 0.037f, 0.018f, new Vector3(0, -Upper * 0.7f, 0), new Color("1d1c1f"), 10);
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
        if (s.Role == Role.Linguist || Trait(s, 30) < 0.22f)
        {
            // Round spectacles
            var rim = Mesh3.Mat(PickFor(s, 31, new[] { new Color("1d1a17"), new Color("5a3a22"), Gfx.Brass.Darkened(0.2f) }));
            foreach (int side in new[] { -1, 1 })
            {
                var ring = new MeshInstance3D { Mesh = GlassRing, Position = new Vector3(side * 0.027f, 0.012f, 0.066f), MaterialOverride = rim };
                ring.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
                _head.AddChild(ring);
            }
            Mesh3.Box(_head, new Vector3(0.02f, 0.004f, 0.004f), new Vector3(0, 0.014f, 0.07f), rim);
        }
        if (!s.Female && s.Role != Role.Messenger)
        {
            float f = Trait(s, 32);
            if (f < 0.2f)        // moustache
                Mesh3.Box(_head, new Vector3(0.042f, 0.011f, 0.014f), new Vector3(0, -0.03f, 0.068f), hair.Darkened(0.1f));
            else if (f < 0.27f)  // a full beard
            {
                var beard = Mesh3.Sphere(_head, 0.05f, new Vector3(0, -0.05f, 0.03f), hair.Darkened(0.1f));
                beard.Scale = new Vector3(1.15f, 0.9f, 0.9f);
            }
        }

        _carry = Mesh3.Box(_torso, new Vector3(0.2f, 0.05f, 0.15f), new Vector3(0, 0.12f, 0.17f), Gfx.Paper);
        _carry.Visible = false;
        _baseScale = (s.Role == Role.Messenger ? 0.9f : s.Female ? 0.95f : 1f) * (0.94f + Trait(s, 22) * 0.12f);
        Scale = Vector3.One * _baseScale;
        _phase = (s.Id * 1.37f) % Mathf.Tau;
        Bake();
    }

    // ------------------------------------------------------------------ bake

    static readonly Dictionary<float, SphereMesh> SmallSpheres = new();
    static StandardMaterial3D? _clothMat, _glossyMat;

    /// <summary>The vertex-coloured materials every figure shares: cloth and skin, and brilliantined hair.</summary>
    static StandardMaterial3D FigureMat(bool glossy)
    {
        if (glossy) return _glossyMat ??= NewFigureMat(true);
        return _clothMat ??= NewFigureMat(false);
    }

    static StandardMaterial3D NewFigureMat(bool glossy) => new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        AlbedoColor = Colors.White,
        Roughness = glossy ? 0.25f : 0.85f,
        Metallic = glossy ? 0.2f : 0f,
    };

    /// <summary>
    /// Turns the jointed parts into one skinned mesh on a skeleton with a bone per joint (two surfaces if the hair is
    /// glossy), and drops the parts. The carried sheaf stays its own mesh, since its colour changes.
    /// </summary>
    void Bake()
    {
        var joints = new Node3D?[] { _hips, _torso, _head, _thighL, _shinL, _thighR, _shinR, _armL, _elbowL, _armR, _elbowR, _skirt, _lap };
        _hasSkirt = _skirt != null;
        var restAt = new Transform3D[Bones];
        _skel = new Skeleton3D { Name = "Skeleton" };
        for (int b = 0; b < Bones; b++)
        {
            _skel.AddBone(b.ToString());
            if (BoneParent[b] >= 0) _skel.SetBoneParent(b, BoneParent[b]);
            _jointAt[b] = joints[b]?.Position ?? Vector3.Zero;
            var rest = new Transform3D(Basis.Identity, _jointAt[b]);
            _skel.SetBoneRest(b, rest);
            _skel.SetBonePosePosition(b, _jointAt[b]);
            restAt[b] = BoneParent[b] >= 0 ? restAt[BoneParent[b]] * rest : rest;
        }
        _hipAt = _jointAt[BHips];
        _carryOnTorso = _carry.Transform;

        var verts = new List<Vector3>[] { new(), new() };
        var norms = new List<Vector3>[] { new(), new() };
        var cols = new List<Color>[] { new(), new() };
        var bones = new List<int>[] { new(), new() };
        var idx = new List<int>[] { new(), new() };
        for (int b = 0; b < Bones; b++)
        {
            if (joints[b] is not { } j) continue;
            foreach (var child in j.GetChildren())
            {
                if (child is not MeshInstance3D mi || mi == _carry || mi.Mesh == null) continue;
                var mat = mi.MaterialOverride as StandardMaterial3D;
                var col = mat?.AlbedoColor ?? Colors.White;
                int surf = mat != null && mat.Roughness < 0.5f ? 1 : 0;
                var mesh = mi.Mesh;
                // Beads, buttons and hands are a few pixels across: a coarser sphere looks the same.
                if (mesh is SphereMesh { Radius: < 0.03f } sm)
                {
                    if (!SmallSpheres.TryGetValue(sm.Radius, out var small))
                        SmallSpheres[sm.Radius] = small = new SphereMesh { Radius = sm.Radius, Height = sm.Radius * 2, RadialSegments = 10, Rings = 5 };
                    mesh = small;
                }
                var t = Mesh3.TrianglesOf(mesh);
                var xf = restAt[b] * mi.Transform;
                var nb = xf.Basis.Inverse().Transposed();
                int baseV = verts[surf].Count;
                for (int k = 0; k < t.V.Length; k++)
                {
                    verts[surf].Add(xf * t.V[k]);
                    norms[surf].Add((nb * t.N[k]).Normalized());
                    cols[surf].Add(col);
                    bones[surf].Add(b);
                }
                foreach (int i in t.I) idx[surf].Add(baseV + i);
            }
        }

        var skin = new Skin();
        for (int b = 0; b < Bones; b++) skin.AddBind(b, restAt[b].AffineInverse());
        var arrays = new ArrayMesh();
        for (int surf = 0; surf < 2; surf++)
        {
            if (verts[surf].Count == 0) continue;
            int n = verts[surf].Count;
            var bi = new int[n * 4];
            var bw = new float[n * 4];
            for (int k = 0; k < n; k++) { bi[k * 4] = bones[surf][k]; bw[k * 4] = 1; }
            var a = new Godot.Collections.Array();
            a.Resize((int)Mesh.ArrayType.Max);
            a[(int)Mesh.ArrayType.Vertex] = verts[surf].ToArray();
            a[(int)Mesh.ArrayType.Normal] = norms[surf].ToArray();
            a[(int)Mesh.ArrayType.Color] = cols[surf].ToArray();
            a[(int)Mesh.ArrayType.Bones] = bi;
            a[(int)Mesh.ArrayType.Weights] = bw;
            a[(int)Mesh.ArrayType.Index] = idx[surf].ToArray();
            arrays.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, a);
            arrays.SurfaceSetMaterial(arrays.GetSurfaceCount() - 1, FigureMat(surf == 1));
        }
        var body = new MeshInstance3D { Name = "Body", Mesh = arrays, Skin = skin, Skeleton = ".." };
        // Generous bounds for culling: the skinned figure reaches past its rest pose (an arm raised at rounders).
        body.CustomAabb = new Aabb(new Vector3(-0.7f, -0.1f, -0.7f), new Vector3(1.4f, 1.6f, 1.4f));
        _skel.AddChild(body);

        // The jointed parts have served their turn; the sheaf moves to the figure itself.
        _carry.GetParent().RemoveChild(_carry);
        _hips.GetParent().RemoveChild(_hips);
        _hips.Free();
        AddChild(_skel);
        AddChild(_carry);
        if (_hasSkirt) SetSkirt(false);
    }

    /// <summary>The skirt hangs when standing; seated, the lap piece shows instead. The hidden one shrinks to nothing.</summary>
    void SetSkirt(bool seated)
    {
        _seatedSkirt = seated;
        _skel.SetBonePoseScale(BSkirt, Vector3.One * (seated ? 0.001f : 1f));
        _skel.SetBonePoseScale(BLap, Vector3.One * (seated ? 1f : 0.001f));
    }

    void Hair(Staff s, Color hair)
    {
        // About half the Wrens work bareheaded indoors; the others keep their caps on.
        if (s.Role == Role.Wren && Trait(s, 43) < 0.5f)
        {
            // Hair rolled up off the collar, under a navy cap with a tally band.
            var bun = Mesh3.Sphere(_head, 0.06f, new Vector3(0, -0.02f, -0.055f), hair);
            bun.Scale = new Vector3(1.2f, 0.8f, 0.8f);
            Mesh3.Cyl(_head, 0.082f, 0.078f, 0.045f, new Vector3(0, 0.07f, -0.008f), new Color("1a2238"), 16);
            Mesh3.Cyl(_head, 0.079f, 0.079f, 0.015f, new Vector3(0, 0.05f, -0.008f), new Color("101318"), 16);
            Mesh3.Box(_head, new Vector3(0.025f, 0.014f, 0.004f), new Vector3(0, 0.05f, 0.072f), Gfx.Brass);
            return;
        }
        if (s.Role == Role.Messenger && Trait(s, 44) < 0.6f)
        {
            var crop = Mesh3.Sphere(_head, 0.075f, new Vector3(0, 0.018f, -0.012f), hair);
            crop.Scale = new Vector3(0.95f, 1f, 1f);
            Mesh3.Cyl(_head, 0.078f, 0.078f, 0.04f, new Vector3(0, 0.07f, -0.005f), new Color("2b2a33"), 16);
            var peak = Mesh3.Box(_head, new Vector3(0.12f, 0.01f, 0.06f), new Vector3(0, 0.055f, 0.075f), new Color("16151a"));
            peak.Rotation = new Vector3(0.25f, 0, 0);
            return;
        }
        if (s.Role == Role.Messenger)
        {
            // Capless: a schoolboy crop, some of them tousled
            var mop = Mesh3.Sphere(_head, 0.077f, new Vector3(0, 0.022f, -0.01f), hair);
            mop.Scale = new Vector3(1f, 0.95f, 1.02f);
            if (Trait(s, 45) < 0.5f)
                for (int k = 0; k < 4; k++)
                    Mesh3.Sphere(_head, 0.022f, new Vector3(-0.03f + k * 0.02f, 0.075f, 0.03f), hair);
            return;
        }
        if (s.Female)
        {
            int style = (int)(Trait(s, 40) * 4 * 0.9999f);
            var cap = Mesh3.Sphere(_head, 0.076f, new Vector3(0, 0.02f, -0.012f), hair);
            cap.Scale = new Vector3(0.98f, 1.05f, 1f);
            switch (style)
            {
                case 0:
                    // Victory rolls at the temples, rolled at the nape
                    foreach (int side in new[] { -1, 1 })
                    {
                        var roll = Capsule(_head, 0.025f, 0.03f, new Vector3(side * 0.035f, 0.075f, 0.01f), hair);
                        roll.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
                    }
                    Capsule(_head, 0.03f, 0.08f, new Vector3(0, -0.045f, -0.05f), hair).Rotation = new Vector3(0, 0, Mathf.Pi / 2);
                    break;
                case 1:
                    // A bob to the jaw, waved at the sides
                    var bob = Mesh3.Sphere(_head, 0.082f, new Vector3(0, -0.01f, -0.018f), hair);
                    bob.Scale = new Vector3(1.05f, 0.95f, 0.98f);
                    foreach (int side in new[] { -1, 1 })
                        Mesh3.Sphere(_head, 0.035f, new Vector3(side * 0.06f, -0.045f, -0.005f), hair);
                    break;
                case 2:
                    // Hair pinned up in a bun on the crown
                    Mesh3.Sphere(_head, 0.04f, new Vector3(0, 0.07f, -0.05f), hair);
                    break;
                default:
                    // Shoulder-length pageboy, curled under
                    var page = Capsule(_head, 0.038f, 0.11f, new Vector3(0, -0.06f, -0.045f), hair);
                    page.Rotation = new Vector3(0, 0, Mathf.Pi / 2);
                    foreach (int side in new[] { -1, 1 })
                        Capsule(_head, 0.03f, 0.05f, new Vector3(side * 0.06f, -0.04f, -0.01f), hair);
                    break;
            }
            return;
        }
        // Men: short back and sides in several cuts; some older men thin on top.
        int cut = (int)(Trait(s, 41) * 4 * 0.9999f);
        bool thinning = (cut == 3 && Trait(s, 42) < 0.5f) || ((s.Look % 5) == 0 && s.Role == Role.Cryptanalyst);
        if (cut == 3 && !thinning) cut = 0;
        if (thinning)
        {
            // Bald crown with a fringe round the back and sides
            var fringe = Mesh3.Sphere(_head, 0.074f, new Vector3(0, -0.005f, -0.02f), hair);
            fringe.Scale = new Vector3(1f, 0.7f, 0.98f);
            return;
        }
        var top = Mesh3.Sphere(_head, 0.075f, new Vector3(0, 0.022f, -0.016f), hair);
        top.Scale = new Vector3(0.96f, 0.95f, 1f);
        if (cut == 1)
        {
            // Brilliantined and swept back: flat and glossy
            top.Scale = new Vector3(0.95f, 0.88f, 1.04f);
            top.MaterialOverride = Mesh3.Mat(hair, 0, glossy: true);
        }
        else if (cut == 2)
        {
            // Unruly curls on top
            for (int k = 0; k < 5; k++)
                Mesh3.Sphere(_head, 0.026f, new Vector3(-0.04f + k * 0.02f, 0.07f, -0.01f + (k % 2) * 0.02f), hair);
        }
        else
        {
            // A neat side parting
            Mesh3.Box(_head, new Vector3(0.004f, 0.006f, 0.06f), new Vector3(-0.03f, 0.08f, 0.0f), hair.Darkened(0.35f));
        }
    }

    // ------------------------------------------------------------- animation

    /// <summary>Where a resting person sits or plays, and whether they sit down.</summary>
    public record struct RestSpot(Vector3 At, Vector3 Face, bool Sit, bool Play);

    /// <param name="seat">For someone operating a desk: the chair to sit on (global) while they work.</param>
    Material? _carryMat;

    /// <param name="hidden">Indoors under solid roofs: kept in place but not drawn or posed.</param>
    public void Sync(Staff s, GameState g, float dt, Vector3? faceTarget, RestSpot? rest = null, Vector3? seat = null, bool hidden = false)
    {
        if (Commuting) EndCommute();
        // Off watch: back at the billets, not on the map.
        bool show = !s.Away && !hidden;
        if (Visible != show) Visible = show;
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
        if (hidden) { Settled = atRest || deskWork; return; }
        if (moved.LengthSquared() > 0.00002f && !deskWork) _yaw = Mathf.Atan2(moved.X, moved.Z);
        else if (faceTarget is Vector3 ft)
        {
            var d = ft - _shown;
            if (d.LengthSquared() > 0.01f) _yaw = Mathf.Atan2(d.X, d.Z);
        }
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, _yaw, 1 - Mathf.Exp(-dt * 12)), 0);

        bool carrying = s.Task is { Kind: TaskKind.Carry, Phase: >= 1 };
        if (_carry.Visible != carrying) _carry.Visible = carrying;
        if (carrying)
        {
            var first = s.Task!.Msgs.Count > 0 ? g.Messages.GetValueOrDefault(s.Task.Msgs[0]) : null;
            var mat = Mesh3.Mat(first == null ? Gfx.Paper : Gfx.Paper.Lerp(Gfx.NetColor(first.Net), 0.25f));
            if (mat != _carryMat) _carry.MaterialOverride = _carryMat = mat;   // only on a change: each set calls the renderer
            _carryScale = 0.5f + 0.25f * System.Math.Min(4, s.Task.Msgs.Count);
        }

        bool tired = s.Energy < Defs.BreakAt;
        bool sitting = !walking && ((atRest && rest!.Value.Sit) || deskWork);
        Settled = atRest || deskWork;
        if (_hasSkirt && sitting != _seatedSkirt) SetSkirt(sitting);

        Pose p = default;
        if (sitting) p = deskWork ? Writing(dt) : TeaBreak(dt);
        else if (atRest && !walking && rest!.Value.Play) p = Rounders(dt);
        else if (walking) p = Walk(dt, tired, s.Role == Role.Messenger, carrying);
        else if (operating) p = MachineWork(dt);
        else p = Idle(dt, carrying);
        if (tired && !sitting) p.Lean += 0.14f;
        Apply(p, dt);
        if (carrying) PlaceCarry();
        Modulate(s.Leaving);
    }

    float _carryScale = 1;

    MeshInstance3D? _bike;
    /// <summary>On the way in or out (<see cref="Commuters"/>): not where the sim has them, so others don't step round them.</summary>
    public bool Commuting { get; private set; }

    /// <summary>
    /// Off the map in the sim, but walking or cycling between the gate and their door: placed and posed from the
    /// trip, riding a bicycle out of doors, or stopped at the barrier holding out a pass.
    /// </summary>
    public void SyncCommute(Vector3 at, Vector3 heading, bool riding, bool checking, float dt)
    {
        Commuting = true;
        Settled = false;
        if (!Visible) Visible = true;
        if (!_placed) { _shown = at; _placed = true; }
        _shown = _shown.Lerp(at, 1 - Mathf.Exp(-dt * 18));
        if ((at - _shown).Length() > 2) _shown = at;
        Position = _shown;
        if (heading.LengthSquared() > 1e-4f) _yaw = Mathf.Atan2(heading.X, heading.Z);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, _yaw, 1 - Mathf.Exp(-dt * 12)), 0);
        if (riding && _bike == null)
        {
            // Nose to +Z, the saddle under the hips.
            _bike = new MeshInstance3D { Mesh = Bikes.Single(Bikes.FrameOf(StaffId)), RotationDegrees = new Vector3(0, -90, 0), Position = new Vector3(0, 0, 0.1f) };
            AddChild(_bike);
        }
        if (_bike != null && _bike.Visible != riding) _bike.Visible = riding;
        if (_carry.Visible) _carry.Visible = false;
        if (_hasSkirt && _seatedSkirt) SetSkirt(false);
        Pose p = riding ? Ride(dt, !checking) : checking ? ShowPass(dt) : Walk(dt, false, false, false);
        if (riding && checking) { p.ArmR = -1.25f; p.ElbowR = -0.25f; p.ArmOutR = 0.2f; }   // astride, pass held out
        Apply(p, dt);
    }

    /// <summary>Back in the sim's hands: no bicycle.</summary>
    void EndCommute()
    {
        Commuting = false;
        if (_bike != null && _bike.Visible) _bike.Visible = false;
    }

    Pose Ride(float dt, bool pedalling)
    {
        if (pedalling) _phase += dt * 7f;
        float sn = Mathf.Sin(_phase), cs = Mathf.Cos(_phase);
        return new Pose
        {
            HipY = 0.47f, Lean = 0.3f, HeadPitch = -0.15f,
            ThighL = -0.95f + sn * 0.3f, ThighR = -0.95f - sn * 0.3f,
            ShinL = 1.05f + cs * 0.35f, ShinR = 1.05f - cs * 0.35f,
            ArmL = -0.95f, ElbowL = -0.25f, ArmOutL = 0.12f,
            ArmR = -0.95f, ElbowR = -0.25f, ArmOutR = 0.12f,
        };
    }

    /// <summary>Stopped at the barrier, pass held out to the sentry.</summary>
    Pose ShowPass(float dt)
    {
        _idleT += dt;
        return new Pose
        {
            HipY = HipY - 0.004f, HeadPitch = 0.05f,
            ThighL = -0.03f, ThighR = 0.05f, ShinL = 0.05f, ShinR = 0.08f,
            ArmL = 0.03f, ElbowL = -0.12f, ArmOutL = 0.05f,
            ArmR = -1.2f, ElbowR = -0.3f, ArmOutR = 0.15f,
        };
    }

    /// <summary>The sheaf held in front of the chest: the torso bone's pose, worked out here rather than asked of the engine.</summary>
    void PlaceCarry()
    {
        var hips = new Transform3D(Basis.FromEuler(_rot[BHips]), _hipAt);
        var torso = hips * new Transform3D(Basis.FromEuler(_rot[BTorso]), _jointAt[BTorso]);
        var t = torso * _carryOnTorso;
        _carry.Transform = new Transform3D(t.Basis.ScaledLocal(new Vector3(1, _carryScale, 1)), t.Origin);
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
        _hipAt = _hipAt.Lerp(new Vector3(0, p.HipY, 0), k);
        Ease(BHips, new Vector3(0, -p.Twist * 0.5f, 0), k);
        Ease(BTorso, new Vector3(p.Lean, p.Twist, 0), k);
        Ease(BHead, new Vector3(p.HeadPitch - p.Lean * 0.5f, p.HeadYaw - p.Twist, 0), k);
        Ease(BThighL, new Vector3(p.ThighL, 0, 0), k);
        Ease(BThighR, new Vector3(p.ThighR, 0, 0), k);
        Ease(BShinL, new Vector3(p.ShinL, 0, 0), k);
        Ease(BShinR, new Vector3(p.ShinR, 0, 0), k);
        Ease(BArmL, new Vector3(p.ArmL, 0, -p.ArmOutL), k);
        Ease(BArmR, new Vector3(p.ArmR, 0, p.ArmOutR), k);
        Ease(BElbowL, new Vector3(p.ElbowL, 0, 0), k);
        Ease(BElbowR, new Vector3(p.ElbowR, 0, 0), k);
        _skel.SetBonePosePosition(BHips, _hipAt);
    }

    void Ease(int bone, Vector3 goal, float k)
    {
        var r = _rot[bone].Lerp(goal, k);
        _rot[bone] = r;
        _skel.SetBonePoseRotation(bone, Quaternion.FromEuler(r));
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
