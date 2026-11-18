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

    public void PlaceAt(int x, int y, int rot)
    {
        var (w, h) = Defs.Dims(Type, rot);
        Position = new Vector3(x + w / 2f, 0, y + h / 2f);
        RotationDegrees = new Vector3(0, rot * 90, 0);
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
            case StationType.Zygalski: Zygalski(); break;
            case StationType.Banburismus: Banburismus(); break;
            case StationType.Bombe: Bombe(3, Gfx.Black, 1.0f); break;
            case StationType.Bombe4: Bombe(4, new Color("2c3442"), 1.15f); break;
            case StationType.Typex: Typex(); break;
            case StationType.Dispatch: Despatch(); break;
            case StationType.Canteen: Canteen(); break;
            case StationType.CommonRoom: CommonRoom(); break;
            case StationType.Rounders: Rounders(); break;
            case StationType.Checking: Checking(); break;
            case StationType.Tube: Tube(); break;
            case StationType.OutstationLink: Teleprinter(); OutstationSign(); break;
            case StationType.Testery: Desk(); Testery(); break;
            case StationType.Colossus: Colossus(); break;
            case StationType.TunnyMachine: TunnyMachine(); break;
        }
        if (!Defs.IsRest(Type)) BuildPiles();
    }

    // ------------------------------------------------------------- rest areas

    /// <summary>Where resting staff sit, in local space, and the point they face.</summary>
    public (Vector3 at, Vector3 face) Seat(int i) => Type switch
    {
        StationType.Canteen => (new Vector3(i % 2 == 0 ? -0.32f : 0.32f, 0, i < 2 ? 0.62f : -0.62f), Vector3.Zero),
        StationType.CommonRoom => (i switch { 0 => new Vector3(-0.62f, 0, 0.35f), 1 => new Vector3(0.62f, 0, 0.35f), _ => new Vector3(0, 0, -0.62f) }, Vector3.Zero),
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
        for (int i = 0; i < 3; i++)
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

    void Desk(float inset = 0.08f)
    {
        float w = W - inset * 2, d = H - 0.3f, z0 = -H / 2 + 0.08f;
        Mesh3.BoxAt(this, -w / 2, DeskTop - 0.06f, z0, w, 0.06f, d, Gfx.Oak);
        foreach (var lx in new[] { -w / 2 + 0.03f, w / 2 - 0.09f })
            foreach (var lz in new[] { z0 + 0.03f, z0 + d - 0.09f })
                Mesh3.BoxAt(this, lx, 0, lz, 0.06f, DeskTop - 0.06f, 0.06f, Gfx.OakDark);
        // Modesty panel at the back
        Mesh3.BoxAt(this, -w / 2 + 0.05f, 0.12f, z0 + 0.02f, w - 0.1f, DeskTop - 0.2f, 0.03f, Gfx.OakDark);
        Chair(new Vector3(0, 0, H / 2 + 0.22f));
    }

    void Chair(Vector3 at)
    {
        Mesh3.BoxAt(this, at.X - 0.16f, 0, at.Z - 0.14f, 0.32f, 0.24f, 0.28f, Gfx.OakDark);
        Mesh3.BoxAt(this, at.X - 0.17f, 0.24f, at.Z - 0.15f, 0.34f, 0.04f, 0.3f, Gfx.Oak);
        Mesh3.BoxAt(this, at.X - 0.17f, 0.24f, at.Z + 0.12f, 0.34f, 0.32f, 0.04f, Gfx.Oak);
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
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                var c = new Vector3(-w / 2 + 0.1f + sx * (col + 0.5f), height - 0.12f - sy * (row + 0.5f), z0 + d + 0.025f);
                var drum = Mesh3.Cyl(this, r, r, 0.05f, c, drumColors[row % drumColors.Length], 12);
                drum.RotationDegrees = new Vector3(90, 0, 0);
                // A white index mark so the turning is visible
                var mark = Mesh3.Box(drum, new Vector3(r * 0.25f, 0.01f, r * 0.9f), new Vector3(0, 0.03f, -r * 0.45f), Gfx.Cream);
                drum.SetMeta("speed", (rows - row) * 2.2f);
                _spinners.Add(drum);
            }
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

    // ----------------------------------------------------------- message piles

    void BuildPiles()
    {
        (_inPos, _outPos) = Type switch
        {
            StationType.Inbox => (new Vector3(-0.2f, 0.72f, 0.1f), new Vector3(0.18f, 0.72f, 0.12f)),
            StationType.Dispatch => (new Vector3(0, 0.67f, -0.05f), new Vector3(0, 0.67f, -0.05f)),
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

    static void ShowPile(List<MeshInstance3D> pile, IReadOnlyList<int> ids, GameState g)
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
            pile[i].MaterialOverride = Mesh3.Mat(col);
        }
    }

    // ---------------------------------------------------------------- update

    public void Sync(Station st, GameState g, float dt)
    {
        _time += dt;
        bool working = st.Working;
        if (working)
        {
            foreach (var s in _spinners)
            {
                float speed = (float)s.GetMeta("speed");
                if (s.HasMeta("axis")) s.RotateX(speed * dt);
                else s.RotateObjectLocal(Vector3.Up, speed * dt);
            }
            for (int i = 0; i < _sliders.Count; i++)
            {
                var sl = _sliders[i];
                var p = sl.Position;
                if (!sl.HasMeta("bx")) sl.SetMeta("bx", p.X);
                float bx = Type == StationType.Banburismus ? -0.1f + i * 0.2f : (float)sl.GetMeta("bx");
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
        if (_glow != null && Type is StationType.Rounders)
        {
            bool playing = g.Staff.Exists(p => p.Task is { Kind: TaskKind.Rest } t && t.Dest == st.Id && p.Path.Count == 0);
            _glow.Position = playing
                ? new Vector3(Mathf.Sin(_time * 2.1f) * 0.9f, 0.3f + Mathf.Abs(Mathf.Sin(_time * 4.2f)) * 0.8f, Mathf.Cos(_time * 2.1f) * 0.9f)
                : new Vector3(-0.3f, 0.04f, 0.6f);
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
        var input = new List<int>(st.Input);
        if (st.Current is int c) input.Add(c);
        if (Type != StationType.Inbox) ShowPile(_inPile, input, g);
        if (Type != StationType.Dispatch) ShowPile(_outPile, st.Output, g);
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
