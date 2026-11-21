using System;
using System.Collections.Generic;
using Godot;
using StationX.Sim;
using StationX.View.Ui;

namespace StationX.View;

/// <summary>
/// One desk or machine. Built in local space: footprint W×H centred on the origin, front (where the
/// operator works) facing +Z. The node is rotated in 90° steps to match the station.
/// </summary>
public partial class StationModel : Node3D
{
    public StationType Type;
    public int StationId = -1;
    const float DeskTop = 0.44f;

    readonly List<MeshInstance3D> _spinners = new();   // turn about their own axis while working
    readonly List<MeshInstance3D> _sliders = new();    // slide back and forth while working
    MeshInstance3D? _glow;
    readonly List<MeshInstance3D> _inPile = new(), _outPile = new();
    Vector3 _inPos, _outPos;
    float _time;

    public static StationModel Create(StationType t)
    {
        var m = new StationModel { Type = t, Name = t.ToString() };
        m.Build();
        return m;
    }

    public void PlaceAt(int x, int y, int rot) => Transform = Placement(Type, x, y, rot);

    /// <summary>Where a station of this type stands, footprint at (x, y), turned rot quarters.</summary>
    public static Transform3D Placement(StationType t, int x, int y, int rot)
    {
        var (w, h) = Defs.Dims(t, rot);
        return new Transform3D(Basis.FromEuler(new Vector3(0, Mathf.DegToRad(rot * 90), 0)), new Vector3(x + w / 2f, 0, y + h / 2f));
    }

    /// <summary>A still, opaque piece of the model, in the model's own space, for Furniture to merge.</summary>
    public readonly record struct Part(Mesh Mesh, int Surface, Material Mat, Transform3D Local, GeometryInstance3D.ShadowCastingSetting Shadow);

    public readonly List<Part> StaticParts = new();
    /// <summary>The still parts have been handed to Furniture (only for real stations, not the build ghost).</summary>
    public bool Detached { get; private set; }

    /// <summary>
    /// Takes out every piece that never moves or changes (left to Furniture to draw, merged with the rest of the
    /// building), keeping the drums, tapes, glows, trays and anything see-through.
    /// </summary>
    public void DetachStatic()
    {
        if (Detached) return;
        Detached = true;
        var live = new HashSet<Node>();
        foreach (var n in _spinners) live.Add(n);
        foreach (var n in _sliders) live.Add(n);
        foreach (var n in _inPile) live.Add(n);
        foreach (var n in _outPile) live.Add(n);
        foreach (var n in _valves) live.Add(n);
        if (_glow != null) live.Add(_glow);
        if (_canister != null) live.Add(_canister);
        if (_fanHub != null) live.Add(_fanHub);
        var taken = new List<MeshInstance3D>();
        void Collect(Node n, Transform3D parent)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is not Node3D c3 || live.Contains(c)) continue;
                var xf = parent * c3.Transform;
                if (c is MeshInstance3D mi && mi.Mesh != null && mi.GetChildCount() == 0 && mi.Visible)
                {
                    var mats = new Material[mi.Mesh.GetSurfaceCount()];
                    bool ok = true;
                    for (int s = 0; s < mats.Length && ok; s++)
                    {
                        mats[s] = (mi.MaterialOverride ?? mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s))!;
                        ok = mats[s] is BaseMaterial3D { Transparency: BaseMaterial3D.TransparencyEnum.Disabled };
                    }
                    if (!ok) continue;
                    for (int s = 0; s < mats.Length; s++) StaticParts.Add(new Part(mi.Mesh, s, mats[s], xf, mi.CastShadow));
                    taken.Add(mi);
                    continue;
                }
                Collect(c, xf);
            }
        }
        Collect(this, Transform3D.Identity);
        foreach (var mi in taken) { mi.GetParent().RemoveChild(mi); mi.QueueFree(); }
    }

    float W => Defs.Station(Type).W;
    float H => Defs.Station(Type).H;

    // --------------------------------------------------------------- models

    void Build()
    {
        switch (Type)
        {
            case StationType.Inbox: Teleprinter(); break;
            case StationType.Registration: Desk(); Registration(); break;
            case StationType.HandCipher: Desk(); HandCipher(); break;
            case StationType.Crib: Desk(); Crib(); break;
            case StationType.Translation: Desk(); Translation(); break;
            case StationType.Japanese: Desk(); Japanese(); break;
            case StationType.Zygalski: Zygalski(); break;
            case StationType.Banburismus: Banburismus(); break;
            case StationType.Bombe: Bombe(3, Gfx.Black, 1.0f); break;
            case StationType.Bombe4: Bombe(4, new Color("2c3442"), 1.15f); break;
            case StationType.Typex: Typex(); break;
            case StationType.Dispatch: Despatch(); break;
            case StationType.RiderTray: RiderTray(); break;
            case StationType.Canteen: Canteen(); break;
            case StationType.CommonRoom: CommonRoom(); break;
            case StationType.Rounders: Rounders(); break;
            case StationType.Checking: Checking(); break;
            case StationType.Tube: Tube(); break;
            case StationType.Stove: Stove(); break;
            case StationType.Fan: Fan(); break;
            case StationType.SickBay: SickBay(); break;
            case StationType.OutstationLink: Teleprinter(); OutstationSign(); break;
            case StationType.Testery: Desk(); Testery(); break;
            case StationType.Colossus: Colossus(); break;
            case StationType.HeathRobinson: HeathRobinson(); break;
            case StationType.TunnyMachine: TunnyMachine(); break;
            case StationType.Index: CardIndex(); break;
            case StationType.TrafficAnalysis: Desk(); TrafficAnalysis(); break;
        }
        if (!Defs.IsRest(Type)) BuildPiles();
    }

    // ------------------------------------------------------------- rest areas

    /// <summary>Where resting staff sit, in local space, and the point they face.</summary>
    public (Vector3 at, Vector3 face) Seat(int i) => Type switch
    {
        StationType.Canteen => (new Vector3((i % 3 - 1) * 0.4f, 0, i < 3 ? 0.62f : -0.62f), Vector3.Zero),
        StationType.CommonRoom => (i switch
        {
            0 => new Vector3(-0.62f, 0, 0.35f), 1 => new Vector3(0.62f, 0, 0.35f), 2 => new Vector3(-0.35f, 0, -0.62f), _ => new Vector3(0.35f, 0, -0.62f),
        }, Vector3.Zero),
        StationType.Rounders => (i switch
        {
            0 => new Vector3(0, 0, 1.15f), 1 => new Vector3(1.1f, 0, 0), 2 => new Vector3(0, 0, -1.1f),
            3 => new Vector3(-1.1f, 0, 0), 4 => new Vector3(0, 0, 0.1f), _ => new Vector3(0.7f, 0, 0.8f),
        }, new Vector3(0, 0, 0.1f)),
        _ => (new Vector3(0, 0, H / 2 + 0.3f), Vector3.Zero),
    };

    public bool Seated => Type is StationType.Canteen or StationType.CommonRoom;

    void Canteen()
    {
        // A trestle table with a tea urn, mugs and benches.
        Mesh3.BoxAt(this, -0.65f, 0.4f, -0.38f, 1.3f, 0.05f, 0.76f, new Color("8a7a5c"));
        foreach (var lx in new[] { -0.55f, 0.5f })
            Mesh3.BoxAt(this, lx, 0, -0.32f, 0.05f, 0.4f, 0.64f, Gfx.OakDark);
        foreach (float bz in new[] { -0.7f, 0.62f })
            Mesh3.BoxAt(this, -0.6f, 0.22f, bz, 1.2f, 0.04f, 0.08f, Gfx.Oak);
        Mesh3.Cyl(this, 0.11f, 0.12f, 0.3f, new Vector3(0.38f, 0.6f, -0.15f), new Color("9a9a92"), 12);
        Mesh3.Cyl(this, 0.02f, 0.02f, 0.05f, new Vector3(0.38f, 0.78f, -0.15f), Gfx.Black, 6);
        _glow = Mesh3.Sphere(this, 0.02f, new Vector3(0.38f, 0.52f, -0.03f), new Color("554a30"));
        for (int k = 0; k < 5; k++)
            Mesh3.Cyl(this, 0.035f, 0.03f, 0.07f, new Vector3(-0.45f + k * 0.17f, 0.46f, k % 2 == 0 ? 0.18f : -0.2f), Gfx.Cream, 8);
        Mesh3.BoxAt(this, -0.2f, 0.425f, -0.05f, 0.22f, 0.03f, 0.16f, new Color("c8a060"));   // a plate of toast
    }

    void CommonRoom()
    {
        var rug = Mesh3.Box(this, new Vector3(1.7f, 0.01f, 1.6f), new Vector3(0, 0.005f, 0), new Color("6a3a32"));
        rug.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        Mesh3.Box(this, new Vector3(1.4f, 0.012f, 1.3f), new Vector3(0, 0.012f, 0), new Color("7d4a3a"));
        // Low table with a chess game in progress
        Mesh3.Cyl(this, 0.3f, 0.3f, 0.04f, new Vector3(0, 0.3f, 0), Gfx.OakDark, 16);
        Mesh3.Cyl(this, 0.04f, 0.06f, 0.28f, new Vector3(0, 0.14f, 0), Gfx.OakDark, 8);
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                Mesh3.Box(this, new Vector3(0.06f, 0.005f, 0.06f), new Vector3(-0.09f + i * 0.06f, 0.325f, -0.09f + j * 0.06f), (i + j) % 2 == 0 ? Gfx.Cream : Gfx.Black);
        Mesh3.Cyl(this, 0.012f, 0.015f, 0.04f, new Vector3(-0.04f, 0.345f, 0.03f), Gfx.Cream, 6);
        Mesh3.Cyl(this, 0.012f, 0.015f, 0.04f, new Vector3(0.05f, 0.345f, -0.05f), Gfx.Black, 6);
        // Armchairs
        for (int i = 0; i < 4; i++)
        {
            var (at, _) = Seat(i);
            var chair = new Node3D { Position = at, Rotation = new Vector3(0, Mathf.Atan2(-at.X, -at.Z), 0) };
            AddChild(chair);
            var fabric = new Color("5a4a3a").Lerp(new Color("5a2e25"), i * 0.4f);
            Mesh3.BoxAt(chair, -0.2f, 0, -0.18f, 0.4f, 0.24f, 0.38f, fabric);
            Mesh3.BoxAt(chair, -0.2f, 0.24f, -0.22f, 0.4f, 0.32f, 0.08f, fabric.Darkened(0.1f));
            Mesh3.BoxAt(chair, -0.24f, 0.16f, -0.2f, 0.06f, 0.18f, 0.36f, fabric.Darkened(0.15f));
            Mesh3.BoxAt(chair, 0.18f, 0.16f, -0.2f, 0.06f, 0.18f, 0.36f, fabric.Darkened(0.15f));
        }
        // A wireless set and the day's paper
        Mesh3.BoxAt(this, 0.55f, 0, -0.75f, 0.3f, 0.38f, 0.22f, Gfx.OakDark);
        Mesh3.BoxAt(this, 0.58f, 0.18f, -0.53f, 0.24f, 0.14f, 0.01f, new Color("8f7a50"));
        Paper(-0.15f, 0.12f, 0.18f, 0.26f, 25, 0.32f);
    }

    void Rounders()
    {
        // Four white posts round a diamond, a chalked square, a bat and a ball.
        foreach (var (x, z) in new[] { (0f, 1.15f), (1.1f, 0f), (0f, -1.1f), (-1.1f, 0f) })
        {
            Mesh3.Cyl(this, 0.025f, 0.03f, 0.5f, new Vector3(x, 0.25f, z), Gfx.Cream, 6);
            Mesh3.Box(this, new Vector3(0.18f, 0.005f, 0.18f), new Vector3(x, 0.004f, z), Gfx.Cream.Darkened(0.1f)).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        for (int k = 0; k < 4; k++)
        {
            float a = Mathf.Pi / 4 + k * Mathf.Pi / 2;
            var line = Mesh3.Box(this, new Vector3(1.55f, 0.004f, 0.03f), new Vector3(Mathf.Cos(a) * 0.55f, 0.003f, Mathf.Sin(a) * 0.55f), Gfx.Cream.Darkened(0.15f));
            line.Rotation = new Vector3(0, -a + Mathf.Pi / 2, 0);
            line.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        Mesh3.Box(this, new Vector3(0.05f, 0.04f, 0.4f), new Vector3(0.25f, 0.02f, 1.3f), Gfx.Oak).RotationDegrees = new Vector3(0, 30, 0);
        _glow = Mesh3.Sphere(this, 0.04f, new Vector3(-0.3f, 0.04f, 0.6f), new Color("e8e0c8"));
    }

    void Checking()
    {
        // A grey steel cabinet with a small bank of drums and a lamp board.
        Mesh3.BoxAt(this, -0.35f, 0, -0.38f, 0.7f, 0.85f, 0.5f, new Color("5c5f5a"));
        Mesh3.BoxAt(this, -0.37f, 0.85f, -0.4f, 0.74f, 0.04f, 0.54f, new Color("6e716b"));
        for (int i = 0; i < 3; i++)
        {
            var drum = Mesh3.Cyl(this, 0.07f, 0.07f, 0.04f, new Vector3(-0.18f + i * 0.18f, 0.62f, 0.13f), new[] { new Color("b8483e"), new Color("d2b04a"), new Color("5a8f55") }[i], 12);
            drum.RotationDegrees = new Vector3(90, 0, 0);
            Mesh3.Box(drum, new Vector3(0.02f, 0.01f, 0.06f), new Vector3(0, 0.025f, -0.03f), Gfx.Cream);
            drum.SetMeta("speed", 3f + i);
            _spinners.Add(drum);
        }
        for (int i = 0; i < 6; i++)
            Mesh3.Sphere(this, 0.018f, new Vector3(-0.2f + i * 0.08f, 0.35f, 0.125f), new Color("3a3528"));
        _glow = Mesh3.Sphere(this, 0.03f, new Vector3(0.25f, 0.78f, 0.13f), new Color("554a30"));
        Chair(new Vector3(0, 0, 0.5f + 0.2f));
    }

    MeshInstance3D? _canister;

    void Tube()
    {
        // A Lamson tube terminal: a brass-banded receiver with its pipe running up to the ceiling.
        Mesh3.BoxAt(this, -0.3f, 0, -0.35f, 0.6f, 0.75f, 0.45f, Gfx.OakDark);
        Mesh3.BoxAt(this, -0.32f, 0.75f, -0.37f, 0.64f, 0.04f, 0.49f, Gfx.Oak);
        Mesh3.Cyl(this, 0.09f, 0.09f, 0.55f, new Vector3(0, 1.05f, -0.15f), Gfx.Brass.Darkened(0.25f), 12);
        Mesh3.Cyl(this, 0.06f, 0.06f, 0.6f, new Vector3(0, 1.6f, -0.15f), new Color("5a5650"), 10);
        Mesh3.Cyl(this, 0.11f, 0.11f, 0.05f, new Vector3(0, 0.8f, -0.15f), Gfx.Brass, 12);
        Mesh3.Cyl(this, 0.11f, 0.11f, 0.05f, new Vector3(0, 1.3f, -0.15f), Gfx.Brass, 12);
        _canister = Mesh3.Cyl(this, 0.06f, 0.06f, 0.18f, new Vector3(0, 0.88f, 0.05f), Gfx.Brass.Lightened(0.1f), 10);
        _canister.RotationDegrees = new Vector3(90, 0, 0);
    }

    void Stove()
    {
        // A tortoise stove on an iron plate, with its flue up through the roof and a coke scuttle beside it.
        var iron = new Color("23211f");
        Mesh3.BoxAt(this, -0.32f, 0, -0.32f, 0.64f, 0.03f, 0.64f, new Color("4a4440"));
        Mesh3.Cyl(this, 0.2f, 0.24f, 0.5f, new Vector3(0, 0.28f, 0), iron, 14);
        Mesh3.Cyl(this, 0.12f, 0.2f, 0.12f, new Vector3(0, 0.59f, 0), iron, 14);
        Mesh3.Cyl(this, 0.05f, 0.05f, 1.6f, new Vector3(0, 1.45f, 0), new Color("2e2b28"), 10);
        _glow = Mesh3.Box(this, new Vector3(0.14f, 0.08f, 0.02f), new Vector3(0, 0.22f, 0.235f), new Color("ff6a2a"), 2f);
        Mesh3.Cyl(this, 0.1f, 0.08f, 0.14f, new Vector3(0.32f, 0.07f, 0.22f), new Color("3a342c"), 10);
        Mesh3.Sphere(this, 0.07f, new Vector3(0.32f, 0.15f, 0.22f), new Color("1a1816"));
    }

    /// <summary>The blades of an extractor fan, turned in code while the summer heat is on.</summary>
    Node3D? _fanHub;

    void Fan()
    {
        // A standing extractor: an iron cabinet trunked up to the roof, a round cowl with a guard, three blades.
        var iron = new Color("3c4038");
        Mesh3.BoxAt(this, -0.3f, 0, -0.18f, 0.6f, 0.75f, 0.32f, iron);
        Mesh3.Box(this, new Vector3(0.26f, 0.9f, 0.22f), new Vector3(0, 1.2f, -0.04f), iron.Darkened(0.15f));
        var cowl = Mesh3.Cyl(this, 0.26f, 0.26f, 0.08f, new Vector3(0, 0.42f, 0.17f), iron.Lightened(0.1f), 18);
        cowl.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
        var hole = Mesh3.Cyl(this, 0.22f, 0.22f, 0.02f, new Vector3(0, 0.42f, 0.205f), new Color("121210"), 18);
        hole.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
        _fanHub = new Node3D { Position = new Vector3(0, 0.42f, 0.215f) };
        AddChild(_fanHub);
        for (int k = 0; k < 3; k++)
        {
            var blade = Mesh3.Box(_fanHub, new Vector3(0.07f, 0.19f, 0.01f), new Vector3(0, 0.1f, 0), new Color("8a8478"));
            var arm = new Node3D { Rotation = new Vector3(0, 0, k * Mathf.Tau / 3) };
            _fanHub.AddChild(arm);
            blade.Reparent(arm, false);
            blade.Rotation = new Vector3(0, 0.5f, 0);
        }
        Mesh3.Sphere(_fanHub, 0.035f, Vector3.Zero, new Color("2a2a26"));
        foreach (float r in new[] { 0.08f, 0.16f, 0.24f })
        {
            var ring = Mesh3.Cyl(this, r, r, 0.012f, new Vector3(0, 0.42f, 0.23f), new Color("2a2a26"), 18);
            ring.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
        }
        Mesh3.Box(this, new Vector3(0.5f, 0.012f, 0.012f), new Vector3(0, 0.42f, 0.235f), new Color("2a2a26"));
        Mesh3.Box(this, new Vector3(0.012f, 0.5f, 0.012f), new Vector3(0, 0.42f, 0.235f), new Color("2a2a26"));
    }

    void SickBay()
    {
        // Two iron bedsteads with white counterpanes, a folding screen and a medicine cabinet.
        var iron = new Color("2b2d2e");
        var sheet = new Color("e6e2d6");
        foreach (float x in new[] { -0.55f, 0.45f })
        {
            Mesh3.BoxAt(this, x - 0.22f, 0.22f, -0.42f, 0.44f, 0.12f, 0.84f, sheet);
            Mesh3.BoxAt(this, x - 0.18f, 0.34f, -0.38f, 0.36f, 0.06f, 0.18f, Colors.White);
            Mesh3.BoxAt(this, x - 0.26f, 0, -0.46f, 0.04f, 0.55f, 0.04f, iron);
            Mesh3.BoxAt(this, x + 0.22f, 0, -0.46f, 0.04f, 0.55f, 0.04f, iron);
            Mesh3.BoxAt(this, x - 0.26f, 0, 0.42f, 0.04f, 0.38f, 0.04f, iron);
            Mesh3.BoxAt(this, x + 0.22f, 0, 0.42f, 0.04f, 0.38f, 0.04f, iron);
            Mesh3.BoxAt(this, x - 0.26f, 0.45f, -0.46f, 0.52f, 0.04f, 0.04f, iron);
            Mesh3.BoxAt(this, x - 0.26f, 0.3f, 0.42f, 0.52f, 0.04f, 0.04f, iron);
            Mesh3.BoxAt(this, x - 0.22f, 0.16f, -0.42f, 0.44f, 0.06f, 0.84f, new Color("7a6a55"));
        }
        // Screen between the beds.
        Mesh3.BoxAt(this, -0.06f, 0, -0.35f, 0.03f, 0.75f, 0.6f, new Color("cfc8b4"));
        // Cabinet with a red cross.
        Mesh3.BoxAt(this, 0.8f, 0, -0.45f, 0.18f, 0.6f, 0.22f, new Color("e2ddd0"));
        Mesh3.BoxAt(this, 0.86f, 0.4f, -0.235f, 0.06f, 0.02f, 0.01f, Gfx.Bad);
        Mesh3.BoxAt(this, 0.88f, 0.38f, -0.235f, 0.02f, 0.06f, 0.01f, Gfx.Bad);
    }

    void OutstationSign()
    {
        Mesh3.BoxAt(this, -0.4f, 0, -0.42f, 0.04f, 1.2f, 0.04f, Gfx.OakDark);
        Mesh3.Cyl(this, 0.01f, 0.01f, 1.2f, new Vector3(-0.38f, 1.2f, -0.4f), Gfx.Black, 4).RotationDegrees = new Vector3(0, 0, 70);
        var l = Mesh3.Text(this, "OUTSTATIONS", new Vector3(0, 0.4f, 0.26f), 18, Gfx.Cream, Fonts.BodyBold, billboard: false);
        l.PixelSize = 0.0035f;
    }

    void Testery()
    {
        // Lengths of teleprinter tape, squared paper covered in pencil, and a slide rule.
        for (int k = 0; k < 3; k++)
        {
            var tape = Mesh3.Box(this, new Vector3(1.4f, 0.004f, 0.05f), new Vector3(0, DeskTop + 0.006f + k * 0.003f, -0.2f + k * 0.1f), Gfx.Paper.Darkened(0.05f));
            tape.RotationDegrees = new Vector3(0, -3 + k * 3, 0);
            _sliders.Add(tape);
        }
        Paper(0.45f, 0.1f, 0.32f, 0.3f, 8);
        for (int i = 0; i < 5; i++)
            Mesh3.Box(this, new Vector3(0.28f, 0.003f, 0.004f), new Vector3(0.45f, DeskTop + 0.012f, 0.0f + i * 0.05f), new Color(0.4f, 0.4f, 0.45f)).RotationDegrees = new Vector3(0, 8, 0);
        Mesh3.Box(this, new Vector3(0.35f, 0.012f, 0.04f), new Vector3(-0.45f, DeskTop + 0.008f, 0.12f), new Color("d8cfb4"));
        Lamp(-W / 2 + 0.25f, -H / 2 + 0.25f);
    }

    readonly List<MeshInstance3D> _valves = new();

    /// <summary>Every desk lamp's light in the world; Main turns them up after dark.</summary>
    public static readonly List<(OmniLight3D light, StationModel owner)> DeskLamps = new();

    /// <summary>
    /// The first Newmanry machine: an angle-iron "bedstead" carrying two loops of tape over pulleys, beside a cabinet of
    /// relays and counters. Smaller and rougher than Colossus.
    /// </summary>
    void HeathRobinson()
    {
        var steel = Gfx.SteelLight;
        var cab = new Color("4a4a42");
        // Relay and counter cabinet on the right
        Mesh3.BoxAt(this, 0.25f, 0, -0.42f, 0.7f, 1.25f, 0.4f, cab);
        Mesh3.BoxAt(this, 0.23f, 1.25f, -0.44f, 0.74f, 0.04f, 0.44f, cab.Lightened(0.1f));
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 5; col++)
            {
                var v = Mesh3.Cyl(this, 0.022f, 0.022f, 0.07f, new Vector3(0.33f + col * 0.12f, 0.55f + row * 0.17f, -0.0f), new Color("6a4a28"), 6);
                _valves.Add(v);
            }
        // Counter display: a strip of cream dials
        for (int i = 0; i < 5; i++)
            Mesh3.Cyl(this, 0.035f, 0.035f, 0.01f, new Vector3(0.33f + i * 0.12f, 0.36f, 0.0f), Gfx.Cream, 10).RotationDegrees = new Vector3(90, 0, 0);
        // The bedstead on the left: a tall angle-iron frame
        float x0 = -0.95f, x1 = 0.1f, z0 = -0.35f, z1 = 0.25f, top = 1.35f;
        foreach (var px in new[] { x0, x1 - 0.04f })
            foreach (var pz in new[] { z0, z1 })
                Mesh3.BoxAt(this, px, 0, pz, 0.04f, top, 0.04f, steel);
        // Open angle-iron rails top and bottom, so the tapes show
        foreach (var y in new[] { top, 0.05f })
        {
            Mesh3.BoxAt(this, x0, y, z0, x1 - x0, 0.04f, 0.04f, steel);
            Mesh3.BoxAt(this, x0, y, z1, x1 - x0, 0.04f, 0.04f, steel);
            Mesh3.BoxAt(this, x0, y, z0, 0.04f, 0.04f, z1 - z0 + 0.04f, steel);
            Mesh3.BoxAt(this, x1 - 0.04f, y, z0, 0.04f, 0.04f, z1 - z0 + 0.04f, steel);
        }
        // Spindle bars carrying the pulleys
        Mesh3.BoxAt(this, x0, top - 0.14f, -0.07f, x1 - x0, 0.03f, 0.03f, steel);
        Mesh3.BoxAt(this, x0, 0.33f, -0.07f, x1 - x0, 0.03f, 0.03f, steel);
        // Two tape loops, each over a top pulley and a bottom pulley, running in step
        for (int loop = 0; loop < 2; loop++)
        {
            float lx = -0.7f + loop * 0.45f;
            foreach (var py in new[] { top - 0.12f, 0.35f })
            {
                var wheel = Mesh3.Cyl(this, 0.08f, 0.08f, 0.04f, new Vector3(lx, py, -0.05f), new Color("2a2a28"), 12);
                wheel.RotationDegrees = new Vector3(90, 0, 0);
                Mesh3.Box(wheel, new Vector3(0.02f, 0.045f, 0.14f), Vector3.Zero, Gfx.SteelLight);
                wheel.SetMeta("speed", 10f + loop * 2);
                _spinners.Add(wheel);
            }
            float len = top - 0.12f - 0.35f;
            foreach (var side in new[] { -0.08f, 0.08f })
                Mesh3.Box(this, new Vector3(0.006f, len, 0.05f), new Vector3(lx + side, 0.35f + len / 2, -0.05f), Gfx.Paper);
        }
        // The photocell reading head between the loops
        Mesh3.BoxAt(this, -0.55f, 0.62f, -0.12f, 0.16f, 0.12f, 0.14f, new Color("2c2c2a"));
        Mesh3.Box(this, new Vector3(0.04f, 0.04f, 0.02f), new Vector3(-0.47f, 0.68f, 0.03f), new Color("e8c870"), 1.2f);
    }

    void Colossus()
    {
        // Racks of valves along the back, and the "bedstead" tape reader in front.
        var rack = new Color("3a3e3a");
        for (int r = 0; r < 3; r++)
        {
            float x = -1.0f + r * 1.0f;
            Mesh3.BoxAt(this, x - 0.45f, 0, -0.95f, 0.9f, 1.55f, 0.35f, rack);
            for (int row = 0; row < 6; row++)
                for (int col = 0; col < 7; col++)
                {
                    var v = Mesh3.Cyl(this, 0.025f, 0.025f, 0.08f, new Vector3(x - 0.36f + col * 0.12f, 0.35f + row * 0.2f, -0.58f), new Color("6a4a28"), 6);
                    _valves.Add(v);
                }
            Mesh3.BoxAt(this, x - 0.45f, 1.55f, -0.97f, 0.9f, 0.05f, 0.39f, rack.Lightened(0.1f));
        }
        // Control panel with switches and lamps
        Mesh3.BoxAt(this, 0.45f, 0, -0.1f, 0.9f, 0.75f, 0.45f, rack.Lightened(0.05f));
        for (int i = 0; i < 8; i++)
            Mesh3.Box(this, new Vector3(0.02f, 0.05f, 0.02f), new Vector3(0.55f + i * 0.09f, 0.62f, 0.36f), Gfx.Cream);
        // The bedstead: a steel frame with pulleys and a loop of punched tape
        var steel = Gfx.SteelLight;
        Mesh3.BoxAt(this, -1.3f, 0, -0.15f, 1.3f, 0.05f, 0.5f, steel);
        foreach (var px in new[] { -1.28f, -0.05f })
        {
            Mesh3.BoxAt(this, px, 0, -0.12f, 0.04f, 1.0f, 0.04f, steel);
            Mesh3.BoxAt(this, px, 0, 0.28f, 0.04f, 1.0f, 0.04f, steel);
        }
        Mesh3.BoxAt(this, -1.3f, 1.0f, -0.15f, 1.3f, 0.04f, 0.5f, steel);
        for (int k = 0; k < 4; k++)
        {
            var wheel = Mesh3.Cyl(this, 0.09f, 0.09f, 0.04f, new Vector3(-1.1f + k * 0.33f, k % 2 == 0 ? 0.85f : 0.3f, 0.08f), new Color("2a2a28"), 12);
            wheel.RotationDegrees = new Vector3(90, 0, 0);
            Mesh3.Box(wheel, new Vector3(0.02f, 0.045f, 0.16f), Vector3.Zero, Gfx.SteelLight);
            wheel.SetMeta("speed", 14f);
            _spinners.Add(wheel);
        }
        for (int k = 0; k < 3; k++)
        {
            var tape = Mesh3.Box(this, new Vector3(0.36f, 0.006f, 0.05f), new Vector3(-0.95f + k * 0.33f, 0.58f, 0.08f), Gfx.Paper);
            tape.RotationDegrees = new Vector3(0, 0, k % 2 == 0 ? -58 : 58);
        }
        _glow = Mesh3.Box(this, new Vector3(0.25f, 0.06f, 0.02f), new Vector3(-0.65f, 0.15f, 0.35f), new Color("554a30"));
    }

    void TunnyMachine()
    {
        // A grey cabinet with the twelve Lorenz wheels on top.
        Mesh3.BoxAt(this, -0.4f, 0, -0.38f, 0.8f, DeskTop, 0.66f, Gfx.Oak);
        Mesh3.BoxAt(this, -0.34f, DeskTop, -0.32f, 0.68f, 0.18f, 0.42f, new Color("4d5250"));
        for (int i = 0; i < 12; i++)
        {
            var wheel = Mesh3.Cyl(this, 0.035f, 0.035f, 0.03f, new Vector3(-0.28f + i * 0.051f, DeskTop + 0.22f, -0.15f), Gfx.SteelLight, 10);
            wheel.RotationDegrees = new Vector3(0, 0, 90);
            Mesh3.Box(wheel, new Vector3(0.032f, 0.008f, 0.012f), new Vector3(0, 0.032f, 0), Gfx.Black);
            wheel.SetMeta("speed", 2f + (i % 5) * 1.3f);
            wheel.SetMeta("axis", 1);
            _spinners.Add(wheel);
        }
        var tape = Mesh3.Box(this, new Vector3(0.04f, 0.01f, 0.22f), new Vector3(0.32f, DeskTop + 0.12f, 0.12f), Gfx.Paper);
        _sliders.Add(tape);
        Chair(new Vector3(0, 0, 0.5f + 0.22f));
    }

    /// <summary>Hut 3's Index: banks of oak card drawers with brass pulls, a ledge with trays of cards, a chair.</summary>
    void CardIndex()
    {
        float back = -H / 2 + 0.06f, depth = 0.34f, height = 0.86f;
        foreach (float cx in new[] { -0.5f, 0.5f })
        {
            Mesh3.BoxAt(this, cx - 0.44f, 0, back, 0.88f, height, depth, Gfx.Oak);
            Mesh3.BoxAt(this, cx - 0.46f, height, back - 0.01f, 0.92f, 0.03f, depth + 0.02f, Gfx.OakDark);
            for (int row = 0; row < 6; row++)
                for (int col = 0; col < 4; col++)
                {
                    float x = cx - 0.4f + col * 0.205f, y = 0.08f + row * 0.13f, z = back + depth + 0.005f;
                    Mesh3.Box(this, new Vector3(0.18f, 0.11f, 0.012f), new Vector3(x + 0.09f, y + 0.055f, z), Gfx.OakLight);
                    Mesh3.Box(this, new Vector3(0.06f, 0.025f, 0.008f), new Vector3(x + 0.09f, y + 0.075f, z + 0.01f), Gfx.Paper);   // label
                    Mesh3.Box(this, new Vector3(0.05f, 0.012f, 0.02f), new Vector3(x + 0.09f, y + 0.035f, z + 0.015f), Gfx.Brass); // pull
                }
        }
        // One drawer pulled out, full of cards
        Mesh3.BoxAt(this, 0.1f, 0.47f, back + depth, 0.18f, 0.1f, 0.22f, Gfx.OakLight);
        for (int i = 0; i < 9; i++)
            Mesh3.Box(this, new Vector3(0.16f, 0.09f, 0.004f), new Vector3(0.19f, 0.535f, back + depth + 0.03f + i * 0.02f), i % 4 == 0 ? Gfx.PaperShade : Gfx.Paper);
        // Working ledge in front with card trays
        Mesh3.BoxAt(this, -0.9f, DeskTop - 0.04f, back + depth + 0.02f, 1.8f, 0.04f, 0.3f, Gfx.Oak);
        foreach (float lx in new[] { -0.86f, 0.8f })
            Mesh3.BoxAt(this, lx, 0, back + depth + 0.24f, 0.05f, DeskTop - 0.04f, 0.05f, Gfx.OakDark);
        for (int i = 0; i < 3; i++)
        {
            float tx = -0.6f + i * 0.32f;
            Mesh3.BoxAt(this, tx, DeskTop, back + depth + 0.07f, 0.22f, 0.05f, 0.16f, Gfx.OakDark);
            for (int k = 0; k < 6; k++)
                Mesh3.Box(this, new Vector3(0.18f, 0.06f, 0.003f), new Vector3(tx + 0.11f, DeskTop + 0.05f, back + depth + 0.09f + k * 0.022f), Gfx.Paper);
        }
        Chair(new Vector3(-0.3f, 0, H / 2 + 0.22f));
    }

    void Desk(float inset = 0.08f)
    {
        float w = W - inset * 2, d = H - 0.3f, z0 = -H / 2 + 0.08f;
        var oak = Gfx.Oak; var dark = Gfx.OakDark;
        // Top with a slight overhang and a darker moulded edge under it
        Mesh3.BoxAt(this, -w / 2, DeskTop - 0.035f, z0, w, 0.035f, d, oak);
        Mesh3.BoxAt(this, -w / 2 + 0.03f, DeskTop - 0.05f, z0 + 0.03f, w - 0.06f, 0.015f, d - 0.06f, dark);
        float apronY = DeskTop - 0.12f, apronH = 0.07f;
        bool pedestal = w > 1.2f;
        float pw = 0.36f, px = w / 2 - 0.05f - pw;      // pedestal on the right
        // Apron rails front and back, with a pencil drawer in the knee-hole front
        Mesh3.BoxAt(this, -w / 2 + 0.05f, apronY, z0 + 0.04f, w - 0.1f, apronH, 0.025f, dark);
        Mesh3.BoxAt(this, -w / 2 + 0.05f, apronY, z0 + d - 0.065f, w - 0.1f, apronH, 0.025f, dark);
        Mesh3.Box(this, new Vector3(0.05f, 0.012f, 0.012f), new Vector3(pedestal ? (-w / 2 + px) / 2 : 0, apronY + apronH / 2, z0 + d - 0.035f), Gfx.Brass);
        // Turned legs (both on the left if there's a pedestal, all four otherwise)
        foreach (var lx in pedestal ? new[] { -w / 2 + 0.07f } : new[] { -w / 2 + 0.07f, w / 2 - 0.07f })
            foreach (var lz in new[] { z0 + 0.06f, z0 + d - 0.06f })
            {
                Mesh3.Cyl(this, 0.028f, 0.02f, apronY, new Vector3(lx, apronY / 2, lz), dark, 10);
                Mesh3.Box(this, new Vector3(0.06f, apronH, 0.06f), new Vector3(lx, apronY + apronH / 2, lz), dark);
            }
        if (pedestal)
        {
            // A pedestal of three drawers with brass cup handles
            Mesh3.BoxAt(this, px, 0.02f, z0 + 0.04f, pw, DeskTop - 0.07f, d - 0.08f, dark);
            float dh = (DeskTop - 0.13f) / 3f;
            for (int k = 0; k < 3; k++)
            {
                float y = 0.05f + k * dh;
                Mesh3.BoxAt(this, px + 0.025f, y, z0 + d - 0.045f, pw - 0.05f, dh - 0.02f, 0.012f, oak);
                Mesh3.Box(this, new Vector3(0.07f, 0.014f, 0.014f), new Vector3(px + pw / 2, y + dh / 2 - 0.01f, z0 + d - 0.03f), Gfx.Brass);
            }
        }
        Chair(new Vector3(0, 0, H / 2 + 0.22f));
    }

    /// <summary>The operator's chair, in local space, if this station has one.</summary>
    public Vector3? ChairLocal { get; private set; }

    void Chair(Vector3 at)
    {
        ChairLocal ??= at;
        // A plain wooden office chair: four turned legs with stretchers, a dished seat, a slatted back under a top rail.
        var leg = Gfx.OakDark;
        foreach (var lx in new[] { -0.13f, 0.13f })
            foreach (var lz in new[] { -0.11f, 0.12f })
                Mesh3.Cyl(this, 0.016f, 0.013f, 0.24f, new Vector3(at.X + lx, 0.12f, at.Z + lz), leg, 8);
        foreach (var lx in new[] { -0.13f, 0.13f })
            Mesh3.Box(this, new Vector3(0.018f, 0.018f, 0.22f), new Vector3(at.X + lx, 0.08f, at.Z + 0.005f), leg);
        Mesh3.Box(this, new Vector3(0.26f, 0.018f, 0.018f), new Vector3(at.X, 0.1f, at.Z - 0.11f), leg);
        Mesh3.BoxAt(this, at.X - 0.16f, 0.24f, at.Z - 0.14f, 0.32f, 0.035f, 0.29f, Gfx.Oak);
        // Back posts rise from the rear legs
        foreach (var lx in new[] { -0.13f, 0.13f })
            Mesh3.Cyl(this, 0.015f, 0.016f, 0.32f, new Vector3(at.X + lx, 0.43f, at.Z + 0.13f), leg, 8);
        for (int k = -1; k <= 1; k++)
            Mesh3.Box(this, new Vector3(0.035f, 0.17f, 0.012f), new Vector3(at.X + k * 0.07f, 0.4f, at.Z + 0.13f), Gfx.Oak);
        Mesh3.Box(this, new Vector3(0.3f, 0.05f, 0.025f), new Vector3(at.X, 0.53f, at.Z + 0.13f), Gfx.Oak);
    }

    void Paper(float x, float z, float w, float d, float rotY = 0, float y = DeskTop)
    {
        var p = Mesh3.Box(this, new Vector3(w, 0.01f, d), new Vector3(x, y + 0.005f, z), Gfx.Paper);
        p.RotationDegrees = new Vector3(0, rotY, 0);
    }

    void Lamp(float x, float z)
    {
        Mesh3.Cyl(this, 0.07f, 0.08f, 0.03f, new Vector3(x, DeskTop + 0.015f, z), Gfx.Brass.Darkened(0.2f));
        Mesh3.Cyl(this, 0.012f, 0.012f, 0.28f, new Vector3(x, DeskTop + 0.17f, z), Gfx.Brass.Darkened(0.3f));
        Mesh3.Cyl(this, 0.04f, 0.12f, 0.09f, new Vector3(x, DeskTop + 0.31f, z + 0.03f), new Color("2f5a3a"));
        Mesh3.Sphere(this, 0.03f, new Vector3(x, DeskTop + 0.27f, z + 0.03f), Gfx.LampGlow with { A = 1 });
        // A warm pool on the desk after dark, under the green shade
        var light = new OmniLight3D
        {
            Position = new Vector3(x, DeskTop + 0.22f, z + 0.05f),
            LightColor = new Color("ffcf88"),
            LightEnergy = 0,
            OmniRange = 1.3f,
            OmniAttenuation = 1.6f,
            ShadowEnabled = false,
            Visible = false,
        };
        AddChild(light);
        DeskLamps.Add((light, this));
        TreeExiting += () => DeskLamps.Remove((light, this));
    }

    /// <summary>Log sheets, a callsign chart pinned on a board, and coloured pencils for drawing the network diagrams.</summary>
    void TrafficAnalysis()
    {
        // Upright board at the back with a network diagram: callsign cards joined by coloured threads
        float back = -H / 2 + 0.1f;
        Mesh3.BoxAt(this, -0.8f, DeskTop, back - 0.03f, 1.6f, 0.62f, 0.03f, new Color("8a7a5c"));
        Mesh3.BoxAt(this, -0.76f, DeskTop + 0.04f, back, 1.52f, 0.54f, 0.01f, new Color("d9cfb4"));
        var nodes = new[] { (-0.55f, 0.42f), (-0.15f, 0.5f), (0.3f, 0.4f), (0.6f, 0.48f), (-0.35f, 0.18f), (0.1f, 0.14f), (0.5f, 0.2f) };
        var colors = new[] { new Color("a3392b"), new Color("3d6b9a"), new Color("4f7a3a") };
        int ci = 0;
        foreach (var (a, b) in new[] { (0, 1), (1, 2), (2, 3), (1, 5), (4, 5), (5, 6), (2, 6), (0, 4) })
        {
            var p = new Vector3(nodes[a].Item1, DeskTop + nodes[a].Item2, back + 0.015f);
            var q = new Vector3(nodes[b].Item1, DeskTop + nodes[b].Item2, back + 0.015f);
            var mid = (p + q) / 2;
            var thread = Mesh3.Box(this, new Vector3(p.DistanceTo(q), 0.008f, 0.004f), mid, colors[ci++ % 3]);
            thread.RotationDegrees = new Vector3(0, 0, Mathf.RadToDeg(Mathf.Atan2(q.Y - p.Y, q.X - p.X)));
        }
        foreach (var (x, y) in nodes)
            Mesh3.Box(this, new Vector3(0.12f, 0.07f, 0.006f), new Vector3(x, DeskTop + y, back + 0.02f), Gfx.Paper);
        // Log sheets and pencils on the desk
        Paper(-0.35f, 0.08f, 0.32f, 0.4f, 3);
        Paper(0.1f, 0.1f, 0.32f, 0.4f, -5);
        for (int i = 0; i < 3; i++)
            Mesh3.Box(this, new Vector3(0.14f, 0.012f, 0.012f), new Vector3(0.5f + i * 0.04f, DeskTop + 0.01f, 0.12f + i * 0.03f), colors[i]).RotationDegrees = new Vector3(0, 25 + i * 10, 0);
        Lamp(W / 2 - 0.3f, -H / 2 + 0.3f);
    }

    void Registration()
    {
        // Card index drawers and an open ledger
        for (int i = 0; i < 2; i++)
        {
            Mesh3.BoxAt(this, -W / 2 + 0.18f + i * 0.24f, DeskTop, -H / 2 + 0.15f, 0.2f, 0.16f, 0.32f, Gfx.OakDark);
            Mesh3.BoxAt(this, -W / 2 + 0.2f + i * 0.24f, DeskTop + 0.16f, -H / 2 + 0.18f, 0.16f, 0.03f, 0.26f, Gfx.Paper);
        }
        Paper(0.15f, 0.0f, 0.26f, 0.34f, 4);
        Paper(0.43f, 0.0f, 0.26f, 0.34f, -4);
        Mesh3.Cyl(this, 0.03f, 0.03f, 0.05f, new Vector3(0.75f, DeskTop + 0.025f, -0.15f), Gfx.Ink);
    }

    void HandCipher()
    {
        Paper(0.0f, -0.02f, 0.5f, 0.36f, -6);
        for (int i = -3; i <= 3; i++)
            Mesh3.Box(this, new Vector3(0.44f, 0.004f, 0.006f), new Vector3(0, DeskTop + 0.012f, -0.02f + i * 0.045f), new Color(0.45f, 0.55f, 0.75f)).RotationDegrees = new Vector3(0, -6, 0);
        Mesh3.Box(this, new Vector3(0.18f, 0.015f, 0.015f), new Vector3(0.4f, DeskTop + 0.01f, 0.05f), new Color("d8b03a")).RotationDegrees = new Vector3(0, 35, 0);
        Lamp(-W / 2 + 0.3f, -H / 2 + 0.25f);
    }

    void Crib()
    {
        Paper(0.1f, 0.0f, 0.55f, 0.36f, 3);
        // A blackboard behind the desk with a Bombe menu chalked on it.
        Mesh3.BoxAt(this, -0.75f, 0.0f, -H / 2 - 0.02f, 0.05f, 1.15f, 0.05f, Gfx.OakDark);
        Mesh3.BoxAt(this, 0.7f, 0.0f, -H / 2 - 0.02f, 0.05f, 1.15f, 0.05f, Gfx.OakDark);
        Mesh3.BoxAt(this, -0.8f, 0.6f, -H / 2, 1.6f, 0.55f, 0.04f, new Color("2c3a32"));
        var nodes = new[] { new Vector2(-0.5f, 0.98f), new Vector2(-0.2f, 1.03f), new Vector2(0.15f, 0.92f), new Vector2(-0.35f, 0.72f), new Vector2(0.05f, 0.7f), new Vector2(0.45f, 0.8f) };
        int[,] links = { { 0, 1 }, { 1, 2 }, { 0, 3 }, { 3, 4 }, { 2, 4 }, { 2, 5 } };
        for (int i = 0; i < links.GetLength(0); i++)
        {
            var a = nodes[links[i, 0]]; var b = nodes[links[i, 1]];
            var mid = (a + b) / 2; var len = (b - a).Length();
            var s = Mesh3.Box(this, new Vector3(len, 0.012f, 0.01f), new Vector3(mid.X, mid.Y, -H / 2 + 0.045f), Gfx.Cream);
            s.Rotation = new Vector3(0, 0, (b - a).Angle());
        }
        foreach (var n in nodes) Mesh3.Box(this, new Vector3(0.05f, 0.05f, 0.012f), new Vector3(n.X, n.Y, -H / 2 + 0.048f), Gfx.PostRed);
    }

    void Translation()
    {
        Mesh3.BoxAt(this, -0.75f, DeskTop, -0.2f, 0.5f, 0.05f, 0.34f, new Color("5a2e25"));
        Paper(-0.62f, -0.03f, 0.22f, 0.3f);
        Paper(-0.38f, -0.03f, 0.22f, 0.3f);
        // Typewriter
        Mesh3.BoxAt(this, 0.15f, DeskTop, -0.2f, 0.42f, 0.1f, 0.32f, Gfx.Steel);
        Mesh3.BoxAt(this, 0.17f, DeskTop + 0.1f, -0.22f, 0.38f, 0.05f, 0.08f, Gfx.Black);
        Mesh3.BoxAt(this, 0.22f, DeskTop + 0.06f, -0.06f, 0.28f, 0.05f, 0.14f, Gfx.Black);
        Mesh3.Box(this, new Vector3(0.2f, 0.18f, 0.01f), new Vector3(0.36f, DeskTop + 0.22f, -0.2f), Gfx.Paper);
    }

    /// <summary>The Japanese desk: the recovered code book in bound volumes, a sheet of five-figure groups, a dictionary open.</summary>
    void Japanese()
    {
        var bindings = new[] { new Color("7a2b26"), new Color("3e4a5c"), new Color("6b5634"), new Color("7a2b26") };
        for (int i = 0; i < bindings.Length; i++)
            Mesh3.BoxAt(this, -0.82f + i * 0.075f, DeskTop, -0.32f, 0.065f, 0.26f, 0.2f, bindings[i]);
        Paper(-0.25f, -0.02f, 0.3f, 0.36f, 4);
        for (int i = -3; i <= 3; i++)
            Mesh3.Box(this, new Vector3(0.24f, 0.004f, 0.006f), new Vector3(-0.25f, DeskTop + 0.012f, -0.02f + i * 0.045f), new Color(0.3f, 0.3f, 0.32f)).RotationDegrees = new Vector3(0, 4, 0);
        // The dictionary, open, its two halves tilted up a little.
        Mesh3.Box(this, new Vector3(0.2f, 0.03f, 0.26f), new Vector3(0.22f, DeskTop + 0.02f, -0.05f), Gfx.Paper).RotationDegrees = new Vector3(0, 0, 6);
        Mesh3.Box(this, new Vector3(0.2f, 0.03f, 0.26f), new Vector3(0.43f, DeskTop + 0.02f, -0.05f), Gfx.Paper).RotationDegrees = new Vector3(0, 0, -6);
        Mesh3.Box(this, new Vector3(0.43f, 0.02f, 0.28f), new Vector3(0.325f, DeskTop + 0.005f, -0.05f), new Color("4a2e22"));
        Lamp(W / 2 - 0.3f, -H / 2 + 0.25f);
    }

    void Teleprinter()
    {
        Mesh3.BoxAt(this, -0.34f, 0, -0.3f, 0.68f, 0.5f, 0.55f, new Color("6d6a5c"));
        Mesh3.BoxAt(this, -0.3f, 0.5f, -0.28f, 0.6f, 0.22f, 0.45f, new Color("bdb79f"));
        Mesh3.BoxAt(this, -0.25f, 0.55f, 0.12f, 0.5f, 0.08f, 0.14f, Gfx.Black);
        Mesh3.Cyl(this, 0.09f, 0.09f, 0.5f, new Vector3(0, 0.82f, -0.18f), Gfx.Paper).RotationDegrees = new Vector3(0, 0, 90);
        var ribbon = Mesh3.Box(this, new Vector3(0.16f, 0.01f, 0.3f), new Vector3(0.0f, 0.73f, 0.0f), Gfx.Paper);
        _sliders.Add(ribbon);
    }

    void Zygalski()
    {
        float t = 0.75f;
        Mesh3.BoxAt(this, -W / 2 + 0.1f, t - 0.07f, -H / 2 + 0.1f, W - 0.2f, 0.07f, H - 0.2f, Gfx.Oak);
        foreach (var lx in new[] { -W / 2 + 0.15f, W / 2 - 0.23f })
            foreach (var lz in new[] { -H / 2 + 0.15f, H / 2 - 0.23f })
                Mesh3.BoxAt(this, lx, 0, lz, 0.08f, t - 0.07f, 0.08f, Gfx.OakDark);
        // Light box with the sheets stacked on it
        Mesh3.BoxAt(this, -0.55f, t, -0.55f, 1.1f, 0.1f, 1.1f, new Color("3a3528"));
        _glow = Mesh3.Box(this, new Vector3(1.0f, 0.02f, 1.0f), new Vector3(0, t + 0.105f, 0), new Color(1f, 0.9f, 0.6f), 0.6f);
        for (int k = 0; k < 3; k++)
        {
            var s = Mesh3.Box(this, new Vector3(0.9f, 0.01f, 0.9f), new Vector3(0, t + 0.12f + k * 0.012f, 0), new Color(0.96f, 0.93f, 0.85f, 0.55f));
            s.RotationDegrees = new Vector3(0, -6 + k * 5, 0);
            _sliders.Add(s);
        }
        Paper(0.75f, 0.7f, 0.25f, 0.3f, 20, t);
    }

    void Banburismus()
    {
        Desk(0.05f);
        for (int k = 0; k < 2; k++)
        {
            var strip = Mesh3.Box(this, new Vector3(2.1f, 0.01f, 0.14f), new Vector3(-0.1f + k * 0.2f, DeskTop + 0.008f + k * 0.006f, -0.12f + k * 0.2f), Gfx.Paper);
            _sliders.Add(strip);
        }
        Mesh3.Box(this, new Vector3(0.2f, 0.015f, 0.015f), new Vector3(1.1f, DeskTop + 0.01f, 0.12f), new Color("d8b03a")).RotationDegrees = new Vector3(0, 40, 0);
    }

    void Bombe(int rows, Color cabinet, float height)
    {
        float w = 1.8f, d = 0.6f, z0 = -H / 2 + 0.12f;
        Mesh3.BoxAt(this, -w / 2, 0, z0, w, height, d, cabinet);
        Mesh3.BoxAt(this, -w / 2 - 0.02f, height, z0 - 0.02f, w + 0.04f, 0.05f, d + 0.04f, cabinet.Lightened(0.15f));
        Color[] drumColors = { new("b8483e"), new("d2b04a"), new("5a8f55"), new("e6dcc0") };
        int cols = 9;
        float sx = (w - 0.2f) / cols, sy = (height - 0.25f) / rows;
        float r = Math.Min(sx, sy) * 0.4f;
        // The drums, each with a white index mark so the turning shows: drawn as instances, two draws for the lot
        // (bodies coloured by row, and the marks) rather than two for every drum.
        int n = rows * cols;
        _drumBase = new Transform3D[n];
        _drumSpeed = new float[n];
        _drumAngle = new float[n];
        _markLocal = new Transform3D(Basis.Identity, new Vector3(0, 0.03f, -r * 0.45f));
        var bodies = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = n,
            Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.05f, RadialSegments = 12, Rings = 1, Material = Mesh3.PaintMat(false) },
        };
        var marks = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = n,
            Mesh = new BoxMesh { Size = new Vector3(r * 0.25f, 0.01f, r * 0.9f), Material = Mesh3.Mat(Gfx.Cream) },
        };
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                int i = row * cols + col;
                var c = new Vector3(-w / 2 + 0.1f + sx * (col + 0.5f), height - 0.12f - sy * (row + 0.5f), z0 + d + 0.025f);
                _drumBase[i] = new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2), c);
                _drumSpeed[i] = (rows - row) * 2.2f;
                bodies.SetInstanceColor(i, drumColors[row % drumColors.Length]);
            }
        _drums = new MultiMeshInstance3D { Multimesh = bodies };
        _drumMarks = new MultiMeshInstance3D { Multimesh = marks };
        AddChild(_drums);
        AddChild(_drumMarks);
        TurnDrums(0);
        _glow = Mesh3.Sphere(this, 0.04f, new Vector3(w / 2 - 0.1f, height + 0.08f, z0 + 0.1f), new Color("554a30"));
        // A small table for the menus in front
        Mesh3.BoxAt(this, -w / 2 + 0.05f, 0, H / 2 - 0.05f, 0.35f, 0.42f, 0.25f, Gfx.Oak);
    }

    void Typex()
    {
        Mesh3.BoxAt(this, -0.4f, 0.0f, -0.38f, 0.8f, DeskTop, 0.66f, Gfx.Oak);
        Mesh3.BoxAt(this, -0.27f, DeskTop, -0.3f, 0.54f, 0.16f, 0.46f, Gfx.Black);
        Mesh3.BoxAt(this, -0.22f, DeskTop + 0.06f, 0.06f, 0.44f, 0.04f, 0.1f, Gfx.Cream);
        for (int i = 0; i < 5; i++)
        {
            var rotor = Mesh3.Cyl(this, 0.045f, 0.045f, 0.06f, new Vector3(-0.16f + i * 0.08f, DeskTop + 0.2f, -0.18f), Gfx.SteelLight, 10);
            rotor.RotationDegrees = new Vector3(0, 0, 90);
            Mesh3.Box(rotor, new Vector3(0.062f, 0.01f, 0.02f), new Vector3(0, 0.04f, 0), Gfx.Black);
            rotor.SetMeta("speed", (5 - i) * 2.5f);
            rotor.SetMeta("axis", 1);
            _spinners.Add(rotor);
        }
        var tape = Mesh3.Box(this, new Vector3(0.05f, 0.01f, 0.18f), new Vector3(0.3f, DeskTop + 0.1f, 0.1f), Gfx.Paper);
        _sliders.Add(tape);
        Chair(new Vector3(0, 0, 0.5f + 0.22f));
    }

    void Despatch()
    {
        Mesh3.BoxAt(this, -0.3f, 0, -0.3f, 0.6f, 0.62f, 0.5f, Gfx.PostRed);
        Mesh3.BoxAt(this, -0.32f, 0.62f, -0.32f, 0.64f, 0.05f, 0.54f, Gfx.PostRed.Darkened(0.2f));
        Mesh3.BoxAt(this, -0.18f, 0.45f, 0.2f, 0.36f, 0.04f, 0.02f, Gfx.Black);
        Mesh3.Sphere(this, 0.035f, new Vector3(0, 0.3f, 0.21f), Gfx.Brass);
        var l = Mesh3.Text(this, "OHMS", new Vector3(0, 0.55f, 0.205f), 22, Gfx.Cream, Fonts.BodyBold, billboard: false);
        l.PixelSize = 0.004f;
    }

    /// <summary>A small hall table with a wire tray, and a painted board: where the dispatch riders hand in.</summary>
    void RiderTray()
    {
        var wood = new Color("5a3d26");
        Mesh3.BoxAt(this, -0.38f, 0.66f, -0.3f, 0.76f, 0.05f, 0.6f, wood);
        foreach (var (lx, lz) in new[] { (-0.34f, -0.26f), (0.3f, -0.26f), (-0.34f, 0.22f), (0.3f, 0.22f) })
            Mesh3.BoxAt(this, lx, 0, lz, 0.04f, 0.66f, 0.04f, wood.Darkened(0.15f));
        // The wire tray.
        var wire = new Color("8a8478");
        Mesh3.BoxAt(this, -0.2f, 0.71f, -0.16f, 0.4f, 0.01f, 0.3f, wire);
        Mesh3.BoxAt(this, -0.2f, 0.71f, -0.16f, 0.4f, 0.06f, 0.01f, wire);
        Mesh3.BoxAt(this, -0.2f, 0.71f, 0.13f, 0.4f, 0.06f, 0.01f, wire);
        Mesh3.BoxAt(this, -0.2f, 0.71f, -0.16f, 0.01f, 0.06f, 0.3f, wire);
        Mesh3.BoxAt(this, 0.19f, 0.71f, -0.16f, 0.01f, 0.06f, 0.3f, wire);
        // A board on a post at the back: DESPATCH RIDERS.
        Mesh3.BoxAt(this, -0.02f, 0.71f, -0.28f, 0.04f, 0.5f, 0.03f, wood);
        Mesh3.BoxAt(this, -0.3f, 1.05f, -0.29f, 0.6f, 0.2f, 0.02f, Gfx.Cream);
        // Lettered on both faces, so it reads from either side of the hall.
        foreach (var (z, yaw) in new[] { (-0.265f, 0f), (-0.295f, 180f) })
        {
            var l = Mesh3.Text(this, "DESPATCH\nRIDERS", new Vector3(0, 1.15f, z), 22, Gfx.Black, Fonts.BodyBold, billboard: false);
            l.PixelSize = 0.0035f;
            l.RotationDegrees = new Vector3(0, yaw, 0);
        }
    }

    // ----------------------------------------------------------- message piles

    void BuildPiles()
    {
        (_inPos, _outPos) = Type switch
        {
            StationType.Inbox => (new Vector3(-0.2f, 0.72f, 0.1f), new Vector3(0.18f, 0.72f, 0.12f)),
            StationType.Dispatch => (new Vector3(0, 0.67f, -0.05f), new Vector3(0, 0.67f, -0.05f)),
            StationType.RiderTray => (new Vector3(0, 0.72f, 0), new Vector3(0, 0.72f, 0)),
            StationType.Typex => (new Vector3(-0.32f, DeskTop, 0.15f), new Vector3(0.32f, DeskTop, 0.15f) with { X = 0.3f, Z = -0.25f }),
            StationType.Bombe or StationType.Bombe4 => (new Vector3(-0.75f, 0.42f, H / 2 + 0.08f), new Vector3(-0.58f, 0.42f, H / 2 + 0.08f)),
            StationType.Zygalski => (new Vector3(-0.75f, 0.75f, 0.7f), new Vector3(0.75f, 0.75f, -0.7f)),
            _ => (new Vector3(-W / 2 + 0.22f, DeskTop, H / 2 - 0.32f), new Vector3(W / 2 - 0.22f, DeskTop, H / 2 - 0.32f)),
        };
        for (int i = 0; i < 7; i++)
        {
            _inPile.Add(Slip(_inPos, i));
            _outPile.Add(Slip(_outPos, i));
        }
    }

    MeshInstance3D Slip(Vector3 at, int i)
    {
        var s = Mesh3.Box(this, new Vector3(0.2f, 0.018f, 0.14f), at + new Vector3(0.005f * i, 0.012f + i * 0.02f, 0), Gfx.Paper);
        s.RotationDegrees = new Vector3(0, Gfx.Rand01(i, (int)(at.X * 10), 4) * 14 - 7, 0);
        s.Visible = false;
        s.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return s;
    }

    static void ShowPile(List<MeshInstance3D> pile, IReadOnlyList<int> ids, GameState g, Material?[] shown)
    {
        for (int i = 0; i < pile.Count; i++)
        {
            bool vis = i < ids.Count;
            pile[i].Visible = vis;
            if (!vis) continue;
            var m = g.Messages.GetValueOrDefault(ids[i]);
            var col = m == null ? Gfx.Paper
                : m.Next == null && m.Loc == Loc.Output ? Gfx.PaperWaiting
                : Gfx.Paper.Lerp(Gfx.NetColor(m.Net), 0.22f);
            var mat = Mesh3.Mat(col);
            // Set only when it changes: each set is a call into the renderer.
            if (shown[i] != mat) pile[i].MaterialOverride = shown[i] = mat;
        }
    }

    // ---------------------------------------------------------------- update

    // Breakdown and servicing: a lamp on top, smoke while broken, a fitter's toolbox while down.
    Node3D? _fault;
    MeshInstance3D? _faultLamp;
    readonly List<MeshInstance3D> _smoke = new();

    void SyncFault(Station st, GameState g)
    {
        bool down = st.IsDown(g.Now);
        bool worn = st.Wear > 0.7;
        if (!down && !worn) { if (_fault != null) _fault.Visible = false; return; }
        if (_fault == null)
        {
            _fault = new Node3D();
            AddChild(_fault);
            float top = ToLocal(TopPoint).Y;
            Mesh3.Cyl(_fault, 0.05f, 0.06f, 0.06f, new Vector3(0.25f, top + 0.03f, 0), new Color("2a2622"), 10);
            _faultLamp = Mesh3.Sphere(_fault, 0.07f, new Vector3(0.25f, top + 0.1f, 0), new Color("aa2a1e"));
            // The fitters' toolbox, on the floor at the front.
            Mesh3.Box(_fault, new Vector3(0.34f, 0.14f, 0.16f), new Vector3(-0.35f, 0.07f, 0.75f), new Color("7a2e22"));
            Mesh3.Box(_fault, new Vector3(0.22f, 0.03f, 0.03f), new Vector3(-0.35f, 0.17f, 0.75f), new Color("2a2622"));
            for (int i = 0; i < 4; i++)
                _smoke.Add(Mesh3.Sphere(_fault, 0.17f, new Vector3(0, top, 0), new Color(0.62f, 0.6f, 0.57f)));
        }
        _fault.Visible = true;
        bool broken = down && !st.Servicing;
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 7);
        _faultLamp!.MaterialOverride = broken ? Mesh3.Mat(new Color("ff3a22"), 0.5f + 2.5f * pulse)
                                     : st.Servicing ? Mesh3.Mat(new Color("ffb43a"), 1.5f)
                                     : Mesh3.Mat(new Color("ff8a2a"), 0.4f + 1.2f * pulse * pulse);
        foreach (var child in _fault.GetChildren())
            if (child is MeshInstance3D mi && mi != _faultLamp && !_smoke.Contains(mi) && mi.Position.Y < 0.3f) mi.Visible = down;
        float top2 = ToLocal(TopPoint).Y;
        for (int i = 0; i < _smoke.Count; i++)
        {
            var puff = _smoke[i];
            puff.Visible = broken;
            if (!broken) continue;
            float t = (_time * 0.5f + i / (float)_smoke.Count) % 1f;
            puff.Position = new Vector3(-0.1f + Mathf.Sin(t * 5 + i) * 0.15f, top2 + t * 1.4f, 0.1f * i - 0.15f);
            puff.Scale = Vector3.One * (0.6f + t * 2.2f);
            puff.Transparency = 0.15f + 0.85f * t * t;
        }
    }

    float[]? _spinSpeed;
    bool[]? _spinAxis;
    float?[]? _slideFrom;
    readonly List<int> _pileIds = new();
    Material?[] _inShown = System.Array.Empty<Material?>(), _outShown = System.Array.Empty<Material?>();

    /// <summary>A Bombe's drums (see <see cref="Bombe"/>): where each rests, how fast it turns, and how far it has.</summary>
    MultiMeshInstance3D? _drums, _drumMarks;
    Transform3D[] _drumBase = System.Array.Empty<Transform3D>();
    float[] _drumSpeed = System.Array.Empty<float>(), _drumAngle = System.Array.Empty<float>();
    Transform3D _markLocal;

    void TurnDrums(float dt)
    {
        if (_drums == null || _drumMarks == null) return;
        var bodies = _drums.Multimesh;
        var marks = _drumMarks.Multimesh;
        for (int i = 0; i < _drumBase.Length; i++)
        {
            _drumAngle[i] = (_drumAngle[i] + _drumSpeed[i] * dt) % Mathf.Tau;
            var xf = _drumBase[i] * new Transform3D(new Basis(Vector3.Up, _drumAngle[i]), Vector3.Zero);
            bodies.SetInstanceTransform(i, xf);
            marks.SetInstanceTransform(i, xf * _markLocal);
        }
    }

    public void Sync(Station st, GameState g, float dt)
    {
        _time += dt;
        if (st.Wears) SyncFault(st, g);
        bool working = st.Working && !st.IsDown(g.Now);
        if (working)
        {
            // Each part's speed and axis, read from its metadata once rather than through the engine every frame.
            if (_spinSpeed == null || _spinSpeed.Length != _spinners.Count)
            {
                _spinSpeed = new float[_spinners.Count];
                _spinAxis = new bool[_spinners.Count];
                for (int i = 0; i < _spinners.Count; i++)
                {
                    _spinSpeed[i] = (float)_spinners[i].GetMeta("speed");
                    _spinAxis[i] = _spinners[i].HasMeta("axis");
                }
            }
            TurnDrums(dt);
            for (int i = 0; i < _spinners.Count; i++)
            {
                var s = _spinners[i];
                float speed = _spinSpeed[i];
                if (_spinAxis![i]) s.RotateX(speed * dt);
                else s.RotateObjectLocal(Vector3.Up, speed * dt);
            }
            if (_slideFrom == null || _slideFrom.Length != _sliders.Count) _slideFrom = new float?[_sliders.Count];
            for (int i = 0; i < _sliders.Count; i++)
            {
                var sl = _sliders[i];
                var p = sl.Position;
                _slideFrom[i] ??= p.X;   // where it rests, taken the first time it moves
                float bx = Type == StationType.Banburismus ? -0.1f + i * 0.2f : _slideFrom[i]!.Value;
                p.X = Mathf.Sin(_time * (0.9f + i * 0.3f) + i * 2) * (Type == StationType.Banburismus ? 0.25f : 0.03f) + bx;
                sl.Position = p;
            }
        }
        if (_canister != null)
            _canister.Position = new Vector3(0, working ? 0.88f + Mathf.PingPong(_time * 1.6f, 1f) * 0.9f : 0.88f, working ? -0.15f : 0.05f);
        if (_valves.Count > 0)
        {
            float flicker = 1.6f + 0.4f * Mathf.Sin(_time * 23);
            var mat = Mesh3.Mat(working ? new Color("ff9a3a") : new Color("6a4a28"), working ? flicker : 0);
            foreach (var v in _valves) v.MaterialOverride = mat;
        }
        if (_fanHub != null && g.Summer) _fanHub.RotateObjectLocal(Vector3.Back, 14 * dt);
        if (_glow != null && Type is StationType.Rounders)
        {
            bool playing = g.Staff.Exists(p => p.Task is { Kind: TaskKind.Rest } t && t.Dest == st.Id && p.Path.Count == 0);
            _glow.Position = playing
                ? new Vector3(Mathf.Sin(_time * 2.1f) * 0.9f, 0.3f + Mathf.Abs(Mathf.Sin(_time * 4.2f)) * 0.8f, Mathf.Cos(_time * 2.1f) * 0.9f)
                : new Vector3(-0.3f, 0.04f, 0.6f);
        }
        else if (_glow != null && Type is StationType.Stove)
        {
            float f = g.Winter ? 1.6f + 0.6f * Mathf.Sin(_time * 3.1f) * Mathf.Sin(_time * 1.7f) : 0;
            _glow.MaterialOverride = Mesh3.Mat(g.Winter ? new Color("ff6a2a") : new Color("3a2a20"), f);
        }
        else if (_glow != null && Type is StationType.Canteen)
        {
            _glow.MaterialOverride = Mesh3.Mat(new Color("ff5a3a"), 1.5f);
        }
        else if (_glow != null)
        {
            if (Type == StationType.Zygalski)
                _glow.MaterialOverride = Mesh3.Mat(new Color(1f, 0.9f, 0.6f), working ? 1.2f + 0.4f * Mathf.Sin(_time * 3) : 0.25f);
            else
                _glow.MaterialOverride = Mesh3.Mat(working ? new Color("ffd36a") : new Color("554a30"), working ? 2f : 0);
        }

        if (Defs.IsRest(Type)) return;
        var input = _pileIds;
        input.Clear();
        input.AddRange(st.Input);
        if (st.Current is int c) input.Add(c);
        if (_inShown.Length != _inPile.Count) _inShown = new Material?[_inPile.Count];
        if (_outShown.Length != _outPile.Count) _outShown = new Material?[_outPile.Count];
        if (Type != StationType.Inbox) ShowPile(_inPile, input, g, _inShown);
        if (Type != StationType.Dispatch) ShowPile(_outPile, st.Output, g, _outShown);
    }

    /// <summary>World-space point just above the station, for overlays.</summary>
    public Vector3 TopPoint => GlobalPosition + new Vector3(0, Type switch
    {
        StationType.Bombe or StationType.Bombe4 or StationType.Colossus => 1.7f,
        StationType.Tube => 2.0f,
        StationType.Rounders => 0.8f,
        _ => 1.0f,
    }, 0);
}
