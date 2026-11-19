using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using StationX.Sim;
using StationX.View.Ui;

namespace StationX.View;

/// <summary>
/// The park: grounds, the lake, the Victorian Mansion, the wartime huts and the clutter of a requisitioned
/// country house. Walls nearest the camera are cut away; which ones depends on the camera's quarter.
/// </summary>
public partial class Park3D : Node3D
{
    public GameState? State;
    const int PxPerTile = 16;
    const float BackWall = 1.45f, FrontWall = 0.3f, WallThick = 0.18f;

    MeshInstance3D _ground = null!;
    MeshInstance3D _snow = null!;
    readonly Dictionary<string, Label3D> _headLabels = new();
    StandardMaterial3D _snowMat = null!;
    public Water Lake { get; private set; } = null!;
    public Countryside Fields { get; private set; } = null!;
    Node3D _buildings = null!;
    public readonly List<OmniLight3D> Lamps = new();
    string _sig = "";
    int _quarter;

    /// <summary>Parts belonging to one or two sides of a building (0 N, 1 E, 2 S, 3 W).</summary>
    readonly List<(Node3D node, int a, int b, bool tall)> _sided = new();

    public override void _Ready()
    {
        Fields = new Countryside { Name = "Countryside" };
        AddChild(Fields);

        _ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(Grid.Width, Grid.Height) },
            Position = new Vector3(Grid.Width / 2f, 0, Grid.Height / 2f),
        };
        AddChild(_ground);
        // A sheet of snow just above the lawn: under the lake surface and the hut floors.
        _snowMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.86f, 0.88f, 0.9f, 0),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            RenderPriority = -10,   // drawn before any other transparent thing, so labels and glass stay on top
            Roughness = 0.9f,
        };
        _snow = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(Grid.Width, Grid.Height) },
            Position = new Vector3(Grid.Width / 2f, 0.006f, Grid.Height / 2f),
            MaterialOverride = _snowMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_snow);
        Lake = new Water { Name = "Lake" };
        AddChild(Lake);
        _buildings = new Node3D { Name = "Buildings" };
        AddChild(_buildings);
    }

    /// <summary>Where snow can lie: thick on the lawns, thinner on trodden paths, none indoors or on the lake.</summary>
    static ImageTexture SnowMask(Grid g)
    {
        const int px = 4;
        var img = Image.CreateEmpty(Grid.Width * px, Grid.Height * px, false, Image.Format.Rgba8);
        for (int y = 0; y < Grid.Height; y++)
            for (int x = 0; x < Grid.Width; x++)
            {
                var tile = g.Get(x, y);
                float a = tile switch { Tile.Lawn or Tile.Tree => 1f, Tile.Path => 0.55f, _ => 0f };
                bool lakeTile = tile == Tile.Water;
                for (int j = 0; j < px; j++)
                    for (int i = 0; i < px; i++)
                    {
                        float a0 = a;
                        // Around the lake, follow the water's real outline rather than whole tiles.
                        if (lakeTile || a > 0)
                        {
                            float lx = (x + (i + 0.5f) / px - Water.CentreX) / Water.RadiusX, lz = (y + (j + 0.5f) / px - Water.CentreZ) / Water.RadiusZ;
                            float d = Mathf.Sqrt(lx * lx + lz * lz);
                            if (d < 1.3f) a0 = d < 0.95f ? 0f : 1f;
                        }
                        float n = a0 > 0 ? Gfx.Rand01(x * px + i, y * px + j, 913) * 0.18f : 0;
                        img.SetPixel(x * px + i, y * px + j, new Color(1, 1, 1, Mathf.Clamp(a0 - n, 0, 1)));
                    }
            }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>Shows each hut head's surname under the hut's name.</summary>
    public void SetHeads(GameState g)
    {
        foreach (var (id, label) in _headLabels)
        {
            var head = g.HeadOfZone(id);
            string text = head == null ? "" : "head: " + head.Name.Split(' ')[^1];
            if (label.Text != text) label.Text = text;
        }
    }

    /// <summary>0 = green, 1 = a full covering of snow.</summary>
    public void SetSnow(float cover)
    {
        _snow.Visible = cover > 0.01f;
        Fields.SetSnow(Mathf.Clamp(cover, 0, 1) * 0.85f);
        var c = _snowMat.AlbedoColor;
        c.A = Mathf.Clamp(cover, 0, 1) * 0.78f;
        _snowMat.AlbedoColor = c;
    }

    public override void _Process(double delta)
    {
        if (State == null) return;
        var sig = string.Join(",", State.Grid.Zones.Select(z => $"{z.Available}{z.Unlocked}"));
        if (sig == _sig) return;
        _sig = sig;
        Rebuild();
    }

    public void ForceRebuild() => _sig = "";

    /// <summary>Camera quarter 0..3 (yaw 45° + 90°·k). Cuts away the two sides facing the camera.</summary>
    public void SetView(int quarter)
    {
        _quarter = ((quarter % 4) + 4) % 4;
        foreach (var (node, a, b, tall) in _sided)
        {
            bool near = Near(a) || Near(b);
            node.Visible = tall ? !near : near;
        }
    }

    bool Near(int side) => _quarter switch
    {
        0 => side is 2 or 1,
        1 => side is 0 or 1,
        2 => side is 0 or 3,
        _ => side is 2 or 3,
    };

    Node3D Sided(int a, int b, bool tall)
    {
        var n = new Node3D();
        _buildings.AddChild(n);
        _sided.Add((n, a, b, tall));
        return n;
    }

    void Rebuild()
    {
        var g = State!.Grid;
        _ground.MaterialOverride = new StandardMaterial3D
        {
            AlbedoTexture = PaintGround(g),
            Roughness = 0.97f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };

        _snowMat.AlbedoTexture = SnowMask(g);
        foreach (var c in _buildings.GetChildren()) c.QueueFree();
        _headLabels.Clear();
        Lamps.Clear();
        _sided.Clear();

        var lake = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 0.02f, RadialSegments = 48, Rings = 1 },
            Position = new Vector3(9.5f, 0.01f, 23f),
            Scale = new Vector3(5.9f, 1, 4.1f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.3f, 0.32f, 0.6f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.12f, Metallic = 0.35f },
        };
        _buildings.AddChild(lake);

        for (int y = 0; y < Grid.Height; y++)
            for (int x = 0; x < Grid.Width; x++)
                if (g.Get(x, y) == Tile.Tree) Tree(x, y);

        foreach (var z in g.Zones)
        {
            if (z.Unlocked) Building(z);
            else Foundations(z);
        }

        Grounds(g);
        SetView(_quarter);
    }

    // ---------------------------------------------------------------- ground

    static float Blob(float fx, float fy, int cell, int salt)
    {
        // Smooth value noise in tile space, for worn patches and mud.
        float gx = fx / cell, gy = fy / cell;
        int ix = (int)gx, iy = (int)gy;
        float tx = gx - ix, ty = gy - iy;
        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        float a = Gfx.Rand01(ix, iy, salt), b = Gfx.Rand01(ix + 1, iy, salt), c = Gfx.Rand01(ix, iy + 1, salt), d = Gfx.Rand01(ix + 1, iy + 1, salt);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    static ImageTexture PaintGround(Grid g)
    {
        int W = Grid.Width * PxPerTile, H = Grid.Height * PxPerTile;
        var data = new byte[W * H * 3];
        void Put(int px, int py, Color c)
        {
            int i = (py * W + px) * 3;
            data[i] = (byte)(Mathf.Clamp(c.R, 0, 1) * 255);
            data[i + 1] = (byte)(Mathf.Clamp(c.G, 0, 1) * 255);
            data[i + 2] = (byte)(Mathf.Clamp(c.B, 0, 1) * 255);
        }
        var doors = g.Zones.Select(z => z.OutsideDoor).Append(Grid.Gate).ToList();

        for (int py = 0; py < H; py++)
            for (int px = 0; px < W; px++)
            {
                float fx = (px + 0.5f) / PxPerTile, fy = (py + 0.5f) / PxPerTile;
                int tx = (int)fx, ty = (int)fy;
                float noise = (Gfx.Hash(px, py, 3) & 0xFF) / 255f;
                // Tired lawn: mown stripes, worn patches, clumps.
                Color c = ((tx / 2) % 2 == 0 ? Gfx.Lawn : Gfx.LawnStripe).Lightened(noise * 0.05f);
                float wear = Blob(fx, fy, 3, 17);
                if (wear > 0.62f) c = c.Lerp(Gfx.Mud.Lightened(0.15f), (wear - 0.62f) * 1.6f);
                if (noise > 0.93f) c = c.Darkened(0.15f);

                // Trampled mud around doorways and the gate.
                foreach (var (dx0, dy0) in doors)
                {
                    float d = Mathf.Sqrt((fx - dx0 - 0.5f) * (fx - dx0 - 0.5f) + (fy - dy0 - 0.5f) * (fy - dy0 - 0.5f));
                    if (d < 2.2f) c = c.Lerp(Gfx.Mud, Mathf.Clamp((2.2f - d) / 1.6f, 0, 0.85f) * (0.6f + 0.4f * Blob(fx, fy, 1, 23)));
                }

                var tile = g.Get(tx, ty);
                if (tile is Tile.Path or Tile.Door)
                {
                    c = Gfx.Gravel.Darkened(noise * 0.12f);
                    if ((Gfx.Hash(px, py, 9) & 0x1F) == 0) c = Gfx.GravelDot;
                    // Lorry ruts along the drive, and puddles.
                    if (ty is >= 12 and <= 14 && (Math.Abs(fy - 12.9f) < 0.12f || Math.Abs(fy - 14.1f) < 0.12f)) c = c.Darkened(0.22f);
                    float puddle = Blob(fx, fy, 2, 29);
                    if (puddle > 0.8f) c = c.Lerp(new Color("3b4446"), Mathf.Clamp((puddle - 0.8f) * 8, 0, 0.85f));
                }

                // Lake
                float lx = (fx - 9.5f) / 6.0f, ly = (fy - 23f) / 4.2f;
                float ld = lx * lx + ly * ly;
                if (ld < 1.0f) c = ld > 0.86f ? Gfx.Shore.Darkened(noise * 0.1f) : Gfx.Water.Darkened(0.2f * (1 - ld));

                foreach (var z in g.Zones)
                {
                    if (fx < z.X + 0.5f || fx > z.X + z.W - 0.5f || fy < z.Y + 0.5f || fy > z.Y + z.H - 0.5f) continue;
                    if (!z.Unlocked)
                    {
                        c = (z.Available ? Gfx.Slab : Gfx.Mud.Darkened(0.1f)).Darkened(noise * 0.08f);
                        if (z.Available && Blob(fx, fy, 1, 37) > 0.75f) c = c.Darkened(0.12f);
                    }
                    else if (z.Id == "mansion")
                    {
                        bool dark = ((px / 4) + (py / 8)) % 2 == 0;
                        c = (dark ? Gfx.MansionFloor : Gfx.MansionFloorAlt).Lightened(noise * 0.04f);
                        if (px % (PxPerTile * 2) == 0 || py % (PxPerTile * 2) == 0) c = c.Darkened(0.2f);
                    }
                    else if (z.Brick)
                    {
                        c = new Color("5d4c39").Lightened(noise * 0.05f);
                        if (px % PxPerTile == 0 || py % PxPerTile == 0) c = c.Darkened(0.15f);
                    }
                    else
                    {
                        c = Gfx.HutFloor.Lightened(noise * 0.04f);
                        if (py % 4 == 0) c = Gfx.HutFloorLine;
                        if ((px + (py / 4) * 37) % 48 == 0) c = Gfx.HutFloorLine;
                        c = c.Darkened(Blob(fx, fy, 2, 41) * 0.15f);
                    }
                }
                Put(px, py, c);
            }
        var img = Image.CreateFromData(W, H, false, Image.Format.Rgb8, data);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    // ------------------------------------------------------------- buildings

    void Building(Zone z)
    {
        bool mansion = z.Id == "mansion";
        Material wall = z.Brick ? Textures.Mat(Surface.Brick, mansion ? Colors.White : new Color("c8b8a8"))
                                : Textures.Mat(Surface.Weatherboard, Colors.White);
        var cap = z.Brick ? Gfx.StoneDressing : Gfx.Creosote.Darkened(0.2f);
        float x0 = z.X + 0.5f, x1 = z.X + z.W - 0.5f, y0 = z.Y + 0.5f, y1 = z.Y + z.H - 0.5f;

        var plinth = Textures.Mat(mansion ? Surface.Stone : Surface.Brick, mansion ? Colors.White : new Color("a09080"));
        // A plinth rim just outside the walls
        float pw = 0.14f, ph = 0.1f;
        Mesh3.Box(_buildings, new Vector3(x1 - x0 + 2 * pw + WallThick, ph, pw), new Vector3((x0 + x1) / 2, ph / 2, y0 - WallThick / 2 - pw / 2), plinth);
        Mesh3.Box(_buildings, new Vector3(x1 - x0 + 2 * pw + WallThick, ph, pw), new Vector3((x0 + x1) / 2, ph / 2, y1 + WallThick / 2 + pw / 2), plinth);
        Mesh3.Box(_buildings, new Vector3(pw, ph, y1 - y0 + WallThick), new Vector3(x0 - WallThick / 2 - pw / 2, ph / 2, (y0 + y1) / 2), plinth);
        Mesh3.Box(_buildings, new Vector3(pw, ph, y1 - y0 + WallThick), new Vector3(x1 + WallThick / 2 + pw / 2, ph / 2, (y0 + y1) / 2), plinth);

        // Each side twice: full height when it's away from the camera, cut away when it's near.
        var sides = new[] { (0, x0, y0, x1, y0), (1, x1, y0, x1, y1), (2, x0, y1, x1, y1), (3, x0, y0, x0, y1) };
        foreach (var (side, ax, ay, bx, by) in sides)
        {
            WallRun(Sided(side, side, true), z, side, ax, ay, bx, by, BackWall, wall, cap, true);
            WallRun(Sided(side, side, false), z, side, ax, ay, bx, by, FrontWall, wall, cap, false);
        }
        var corners = new[] { (x0, y0, 0, 3), (x1, y0, 0, 1), (x1, y1, 2, 1), (x0, y1, 2, 3) };
        foreach (var (cx, cy, a, b) in corners)
            foreach (bool tall in new[] { true, false })
            {
                float h = tall ? BackWall : FrontWall;
                var n = Sided(a, b, tall);
                if (z.Brick)
                    for (float q = 0; q < h - 0.05f; q += 0.2f)
                        Mesh3.Box(n, new Vector3(WallThick + 0.08f, 0.1f, WallThick + 0.08f) + (((int)(q / 0.2f)) % 2 == 0 ? new Vector3(0.08f, 0, 0) : new Vector3(0, 0, 0.08f)),
                            new Vector3(cx, q + 0.05f, cy), Gfx.StoneDressing.Darkened(0.05f));
                else
                    Mesh3.Box(n, new Vector3(WallThick + 0.06f, h + 0.06f, WallThick + 0.06f), new Vector3(cx, (h + 0.06f) / 2, cy), Gfx.Creosote);
            }

        Partitions(z);
        if (mansion) MansionDetails(z);
        else if (z.Brick) BlockDetails(z);
        else HutDetails(z);
        DoorDetails(z);

        bool doorOnTop = z.DoorY == z.Y;
        var name = Mesh3.Text(_buildings, z.Name.ToUpperInvariant(), new Vector3(z.DoorX + 0.5f, 1.95f, doorOnTop ? z.Y + 0.3f : z.Y + z.H - 0.7f), 64, new Color("1d1a16"), Fonts.Heading);
        name.PixelSize = 0.0075f;
        name.OutlineSize = 16;
        name.OutlineModulate = new Color("d9cfb4");
        var headLabel = Mesh3.Text(_buildings, "", name.Position + new Vector3(0, -0.42f, 0), 30, new Color("1d1a16"), Fonts.Body);
        headLabel.PixelSize = 0.0075f;
        headLabel.OutlineSize = 10;
        headLabel.OutlineModulate = new Color("d9cfb4");
        _headLabels[z.Id] = headLabel;

        for (int y = z.Y + 2; y < z.Y + z.H - 1; y += 4)
            for (int x = z.X + 2; x < z.X + z.W - 1; x += 5)
            {
                var lamp = new OmniLight3D
                {
                    Position = new Vector3(x + 0.5f, 1.6f, y + 0.5f),
                    LightColor = new Color(1f, 0.76f, 0.45f),
                    OmniRange = 4.5f,
                    LightEnergy = 0,
                    ShadowEnabled = false,
                };
                _buildings.AddChild(lamp);
                Lamps.Add(lamp);
            }
    }

    /// <summary>
    /// Inside walls on the tile edges: lower than the outer walls so the rooms can be seen into, with a dado rail and
    /// skirting, door posts at each doorway, and the room's name painted on its floor.
    /// </summary>
    void Partitions(Zone z)
    {
        bool mansion = z.Id == "mansion";
        float h = 0.62f, th = 0.07f;
        var face = mansion ? new Color("cbbd9c") : z.Brick ? new Color("b9b39d") : new Color("a9a383");
        var dado = mansion ? Gfx.OakDark : new Color("5d6448");
        var paint = Textures.Mat(mansion ? Surface.Plaster : Surface.Matchboard, face.Lightened(0.06f));
        foreach (var p in z.Partitions)
        {
            var spans = new List<(int a, int b)>();
            int start = p.From;
            for (int i = p.From; i <= p.To + 1; i++)
                if (i > p.To || p.Gaps.Contains(i))
                {
                    if (i > start) spans.Add((start, i));
                    start = i + 1;
                }
            foreach (var (a, b) in spans)
            {
                float len = b - a, mid = (a + b) / 2f;
                var c = p.Vertical ? new Vector3(p.At, 0, mid) : new Vector3(mid, 0, p.At);
                Vector3 Size(float l, float hh, float t) => p.Vertical ? new Vector3(t, hh, l) : new Vector3(l, hh, t);
                Mesh3.Box(_buildings, Size(len, h, th), c + new Vector3(0, h / 2, 0), paint);
                Mesh3.Box(_buildings, Size(len, 0.03f, th + 0.04f), c + new Vector3(0, h + 0.015f, 0), dado);           // capping
                Mesh3.Box(_buildings, Size(len, 0.025f, th + 0.025f), c + new Vector3(0, h * 0.55f, 0), dado);          // dado rail
                Mesh3.Box(_buildings, Size(len, 0.07f, th + 0.03f), c + new Vector3(0, 0.035f, 0), Gfx.OakDark);       // skirting
                if (mansion)
                {
                    // Oak wainscot below the dado rail, in raised panels half a tile wide.
                    float wh = h * 0.55f - 0.07f;
                    Mesh3.Box(_buildings, Size(len, wh, th + 0.012f), c + new Vector3(0, 0.07f + wh / 2, 0), Gfx.Oak.Darkened(0.12f));
                    for (float t = a + 0.25f; t < b; t += 0.5f)
                    {
                        var pc = p.Vertical ? new Vector3(p.At, 0, t) : new Vector3(t, 0, p.At);
                        Mesh3.Box(_buildings, Size(0.38f, wh - 0.08f, th + 0.03f), pc + new Vector3(0, 0.07f + wh / 2, 0), Gfx.Oak);
                    }
                }
            }
            foreach (int gap in p.Gaps)
                foreach (float e in new[] { gap + 0.03f, gap + 0.97f })
                {
                    var post = p.Vertical ? new Vector3(p.At, 0, e) : new Vector3(e, 0, p.At);
                    Mesh3.Box(_buildings, new Vector3(0.09f, h + 0.06f, 0.09f), post + new Vector3(0, (h + 0.06f) / 2, 0), Gfx.OakDark);
                }
        }
        foreach (var r in z.Rooms)
        {
            if (r.Corridor && r.H == 1 && r.W > 6) continue;
            var label = Mesh3.Text(_buildings, r.Name.ToUpperInvariant(), new Vector3(r.X + r.W / 2f, 0.012f, r.Y + r.H - 0.32f), 34,
                                   new Color(0.16f, 0.12f, 0.08f, 0.55f), Fonts.Heading, billboard: false);
            label.RotationDegrees = new Vector3(-90, 0, 0);
            label.PixelSize = 0.006f;
            label.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }

    /// <summary>A straight wall with a gap at the door and, on tall walls, taped-up windows.</summary>
    void WallRun(Node3D parent, Zone z, int side, float ax, float ay, float bx, float by, float height, Material wall, Color cap, bool windows)
    {
        bool alongX = Math.Abs(ay - by) < 0.01f;
        float from = alongX ? ax : ay, to = alongX ? bx : by;
        float? gapA = null, gapB = null;
        if (alongX && Math.Abs(z.DoorY + 0.5f - ay) < 0.01f) { gapA = z.DoorX + 0.05f; gapB = z.DoorX + 0.95f; }
        if (!alongX && Math.Abs(z.DoorX + 0.5f - ax) < 0.01f) { gapA = z.DoorY + 0.05f; gapB = z.DoorY + 0.95f; }

        var spans = new List<(float, float)>();
        if (gapA is float ga && gapB is float gb) { spans.Add((from, ga)); spans.Add((gb, to)); }
        else spans.Add((from, to));
        var outward = side switch { 0 => new Vector3(0, 0, -1), 1 => new Vector3(1, 0, 0), 2 => new Vector3(0, 0, 1), _ => new Vector3(-1, 0, 0) };

        foreach (var (s, e) in spans)
        {
            if (e - s < 0.01f) continue;
            float len = e - s, mid = (s + e) / 2;
            var size = alongX ? new Vector3(len, height, WallThick) : new Vector3(WallThick, height, len);
            var center = alongX ? new Vector3(mid, height / 2, ay) : new Vector3(ax, height / 2, mid);
            Mesh3.Box(parent, size, center, wall);
            var capSize = alongX ? new Vector3(len, 0.06f, WallThick + 0.06f) : new Vector3(WallThick + 0.06f, 0.06f, len);
            Mesh3.Box(parent, capSize, center with { Y = height + 0.03f }, cap);
            if (z.Brick && height > 1)
            {
                var band = alongX ? new Vector3(len, 0.05f, WallThick + 0.03f) : new Vector3(WallThick + 0.03f, 0.05f, len);
                Mesh3.Box(parent, band, center with { Y = 0.55f }, Gfx.StoneDressing);
            }
            if (!windows) continue;
            float spacing = z.Brick ? 2.2f : 1.6f;
            for (float t = s + 0.9f; t < e - 0.6f; t += spacing)
            {
                float ww = z.Brick ? 0.6f : 0.8f, wh = z.Brick ? 0.75f : 0.5f, wy = z.Brick ? 0.95f : 0.9f;
                var wsz = alongX ? new Vector3(ww, wh, WallThick + 0.04f) : new Vector3(WallThick + 0.04f, wh, ww);
                var wc = alongX ? new Vector3(t, wy, ay) : new Vector3(ax, wy, t);
                var frame = alongX ? new Vector3(ww + 0.1f, wh + 0.1f, WallThick + 0.02f) : new Vector3(WallThick + 0.02f, wh + 0.1f, ww + 0.1f);
                Mesh3.Box(parent, frame, wc, z.Brick ? Gfx.StoneDressing : new Color("d7cfb8"));
                Mesh3.Box(parent, wsz, wc, Gfx.Window, 0.05f);
                // Anti-blast tape crosses on both faces
                foreach (float f in new[] { -1f, 1f })
                {
                    var face = wc + outward * f * (WallThick / 2 + 0.025f);
                    for (int k = 0; k < 2; k++)
                    {
                        float diag = Mathf.Atan2(wh, ww) * (k == 0 ? 1 : -1);
                        var tape = Mesh3.Box(parent, new Vector3(Mathf.Sqrt(ww * ww + wh * wh) * 0.95f, 0.035f, 0.005f), Vector3.Zero, Gfx.Cream.Darkened(0.1f));
                        tape.Position = face;
                        tape.Rotation = alongX ? new Vector3(0, 0, diag) : new Vector3(diag, Mathf.Pi / 2, 0);
                    }
                }
                if (z.Brick && height > 1)
                {
                    var ls = alongX ? new Vector3(ww + 0.2f, 0.07f, WallThick + 0.08f) : new Vector3(WallThick + 0.08f, 0.07f, ww + 0.2f);
                    Mesh3.Box(parent, ls, wc + new Vector3(0, wh / 2 + 0.09f, 0), Gfx.StoneDressing);
                    Mesh3.Box(parent, ls, wc - new Vector3(0, wh / 2 + 0.07f, 0), Gfx.StoneDressing);
                }
            }
        }
        if (gapA is float a2 && gapB is float b2 && height > 1)
        {
            float mid = (a2 + b2) / 2;
            var size = alongX ? new Vector3(b2 - a2, height - 1.05f, WallThick) : new Vector3(WallThick, height - 1.05f, b2 - a2);
            var center = alongX ? new Vector3(mid, 1.05f + size.Y / 2, ay) : new Vector3(ax, 1.05f + size.Y / 2, mid);
            Mesh3.Box(parent, size, center, wall);
        }
    }

    /// <summary>
    /// The Mansion's mock-Tudor, mock-Gothic, mock-everything roofline: gables, tall chimneys, a copper dome.
    /// Only on the far walls, so they never hide the floor.
    /// </summary>
    void MansionDetails(Zone z)
    {
        float x0 = z.X + 0.5f, x1 = z.X + z.W - 0.5f, y0 = z.Y + 0.5f, y1 = z.Y + z.H - 0.5f;
        var brick = Textures.Mat(Surface.Brick, Colors.White);
        var slate = Textures.Mat(Surface.Felt, new Color("8a8f98"), 2);
        var stone = Textures.Mat(Surface.Stone, Colors.White);

        var north = Sided(0, 0, true);
        Mesh3.Box(north, new Vector3(z.W - 0.6f, 0.14f, 0.5f), new Vector3((x0 + x1) / 2, BackWall + 0.1f, y0 - 0.12f), slate);
        foreach (float gx in new[] { x0 + 2.5f, (x0 + x1) / 2 + 0.5f, x1 - 3f })
        {
            Mesh3.Prism(north, new Vector3(2.2f, 1.1f, WallThick), new Vector3(gx, BackWall + 0.6f, y0), brick);
            Mesh3.Prism(north, new Vector3(2.45f, 1.22f, WallThick * 0.6f), new Vector3(gx, BackWall + 0.62f, y0 - 0.1f), stone);
            Mesh3.Box(north, new Vector3(0.36f, 0.5f, WallThick + 0.03f), new Vector3(gx, BackWall + 0.4f, y0), Gfx.Window, 0.05f);
            Mesh3.Box(north, new Vector3(0.12f, 0.12f, 0.12f), new Vector3(gx, BackWall + 1.2f, y0), stone);
        }
        foreach (float cx in new[] { x0 + 1.2f, (x0 + x1) / 2 - 1.4f, x1 - 1.3f })
            Chimney(north, new Vector3(cx, BackWall, y0 - 0.05f), brick);

        var west = Sided(3, 3, true);
        Mesh3.Box(west, new Vector3(0.5f, 0.14f, z.H - 0.6f), new Vector3(x0 - 0.12f, BackWall + 0.1f, (y0 + y1) / 2), slate);
        float by = (y0 + y1) / 2 + 0.5f;
        Mesh3.Box(west, new Vector3(0.6f, BackWall + 0.2f, 1.8f), new Vector3(x0 - 0.38f, (BackWall + 0.2f) / 2, by), brick);
        for (int k = -1; k <= 1; k++)
            Mesh3.Box(west, new Vector3(0.05f, 0.6f, 0.42f), new Vector3(x0 - 0.7f, 0.95f, by + k * 0.55f), Gfx.Window, 0.05f);
        Mesh3.Prism(west, new Vector3(0.7f, 0.5f, 1.9f), new Vector3(x0 - 0.38f, BackWall + 0.45f, by), slate);
        Chimney(west, new Vector3(x0 - 0.05f, BackWall, y1 - 1.6f), brick);

        var corner = Sided(0, 3, true);
        var drum = new Vector3(x0 + 0.35f, BackWall, y0 + 0.35f);
        Mesh3.Cyl(corner, 0.62f, 0.62f, 0.55f, drum + new Vector3(0, 0.27f, 0), new Color("8a4532"), 20);
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.Tau / 8;
            Mesh3.Box(corner, new Vector3(0.12f, 0.3f, 0.03f), drum + new Vector3(Mathf.Cos(a) * 0.62f, 0.3f, Mathf.Sin(a) * 0.62f), Gfx.Window, 0.05f)
                .Rotation = new Vector3(0, -a + Mathf.Pi / 2, 0);
        }
        Mesh3.Cyl(corner, 0.68f, 0.68f, 0.06f, drum + new Vector3(0, 0.57f, 0), Gfx.StoneDressing, 20);
        Mesh3.Dome(corner, 0.62f, drum + new Vector3(0, 0.6f, 0), Gfx.Verdigris);
        Mesh3.Cyl(corner, 0.02f, 0.05f, 0.35f, drum + new Vector3(0, 1.38f, 0), Gfx.Verdigris.Darkened(0.3f), 8);
        Mesh3.Sphere(corner, 0.06f, drum + new Vector3(0, 1.58f, 0), Gfx.Brass.Darkened(0.2f));

        bool doorSouth = z.DoorY == z.Y + z.H - 1;
        float pz = doorSouth ? y1 + 0.45f : y0 - 0.45f;
        foreach (float px in new[] { z.DoorX - 0.15f, z.DoorX + 1.15f })
            Mesh3.Box(_buildings, new Vector3(0.18f, 0.5f, 0.18f), new Vector3(px, 0.25f, pz), stone);
        Mesh3.Box(_buildings, new Vector3(1.6f, 0.06f, 0.6f), new Vector3(z.DoorX + 0.5f, 0.03f, pz), stone);
    }

    void Chimney(Node3D parent, Vector3 at, Material brick)
    {
        Mesh3.Box(parent, new Vector3(0.45f, 1.4f, 0.32f), at + new Vector3(0, 0.55f, 0), brick);
        Mesh3.Box(parent, new Vector3(0.52f, 0.07f, 0.4f), at + new Vector3(0, 1.27f, 0), Gfx.StoneDressing);
        foreach (float dx in new[] { -0.1f, 0.1f })
            Mesh3.Cyl(parent, 0.055f, 0.07f, 0.22f, at + new Vector3(dx, 1.41f, 0), new Color("9a5a3c"), 8);
    }

    /// <summary>The wooden huts: felt eaves, gable ends, stove pipes, bicycles, rain butts and blast walls.</summary>
    void HutDetails(Zone z)
    {
        float x0 = z.X + 0.5f, x1 = z.X + z.W - 0.5f, y0 = z.Y + 0.5f, y1 = z.Y + z.H - 0.5f;
        var felt = Textures.Mat(Surface.Felt, Colors.White, 2);
        int back = z.DoorY == z.Y ? 2 : 0;
        float by = back == 0 ? y0 : y1, sgn = back == 0 ? -1 : 1;

        var far = Sided(back, back, true);
        Mesh3.Box(far, new Vector3(z.W - 0.4f, 0.08f, 0.45f), new Vector3((x0 + x1) / 2, BackWall + 0.07f, by + sgn * 0.15f), felt);
        var pipe = new Vector3(x0 + z.W * 0.6f, 0, by + sgn * 0.2f);
        Mesh3.Cyl(far, 0.06f, 0.06f, 0.9f, pipe + new Vector3(0, BackWall + 0.45f, 0), new Color("2a2826"), 8);
        Mesh3.Cyl(far, 0.12f, 0.07f, 0.08f, pipe + new Vector3(0, BackWall + 0.92f, 0), new Color("2a2826"), 8);
        for (int k = 0; k < 3; k++) Bicycle(far, new Vector3(x0 + 1.5f + k * 0.55f, 0, by + sgn * 0.42f), back == 0 ? 0 : Mathf.Pi);

        foreach (int side in new[] { 1, 3 })
        {
            var n = Sided(side, side, true);
            float sx = side == 1 ? x1 : x0, ss = side == 1 ? 1 : -1;
            Mesh3.Box(n, new Vector3(0.45f, 0.08f, z.H - 0.4f), new Vector3(sx + ss * 0.15f, BackWall + 0.07f, (y0 + y1) / 2), felt);
            Mesh3.Prism(n, new Vector3(z.H - 0.6f, 0.55f, WallThick), new Vector3(sx, BackWall + 0.3f, (y0 + y1) / 2), Textures.Mat(Surface.Weatherboard, Colors.White))
                .Rotation = new Vector3(0, Mathf.Pi / 2, 0);
            Mesh3.Cyl(_buildings, 0.18f, 0.16f, 0.45f, new Vector3(sx + ss * 0.42f, 0.22f, by - sgn * 0.6f), new Color("3d3a33"), 10);
        }

        int front = 2 - back;
        float fy = front == 2 ? y1 + 0.55f : y0 - 0.55f;
        var brick = Textures.Mat(Surface.Brick, new Color("b0a090"));
        foreach (var (a, b) in new[] { (x0 + 0.3f, z.DoorX - 1.2f), (z.DoorX + 2.2f, x1 - 0.3f) })
            if (b - a > 0.5f)
                Mesh3.Box(_buildings, new Vector3(b - a, 0.42f, 0.22f), new Vector3((a + b) / 2, 0.21f, fy), brick);
        FireBuckets(new Vector3(x1 - 0.8f, 0, fy - (front == 2 ? 0.25f : -0.25f)));
    }

    void BlockDetails(Zone z)
    {
        float x0 = z.X + 0.5f, x1 = z.X + z.W - 0.5f, y0 = z.Y + 0.5f, y1 = z.Y + z.H - 0.5f;
        var concrete = Textures.Mat(Surface.Concrete, Colors.White, 0.5f);
        Mesh3.Box(Sided(2, 2, true), new Vector3(z.W - 0.2f, 0.16f, 0.5f), new Vector3((x0 + x1) / 2, BackWall + 0.1f, y1 + 0.1f), concrete);
        Mesh3.Box(Sided(1, 1, true), new Vector3(0.5f, 0.16f, z.H - 0.2f), new Vector3(x1 + 0.1f, BackWall + 0.1f, (y0 + y1) / 2), concrete);
        Mesh3.Box(Sided(3, 3, true), new Vector3(0.5f, 0.16f, z.H - 0.2f), new Vector3(x0 - 0.1f, BackWall + 0.1f, (y0 + y1) / 2), concrete);
        var far = Sided(2, 2, true);
        for (int k = 0; k < 3; k++)
            Mesh3.Box(far, new Vector3(0.6f, 0.4f, 0.4f), new Vector3(x0 + 4 + k * 7, BackWall + 0.38f, y1), Textures.Mat(Surface.Concrete, new Color("a0a8a0")));
    }

    void DoorDetails(Zone z)
    {
        var (ox, oy) = z.OutsideDoor;
        bool vertical = ox == z.DoorX;
        Mesh3.Box(_buildings, new Vector3(vertical ? 0.9f : 0.35f, 0.08f, vertical ? 0.35f : 0.9f),
            new Vector3(ox + 0.5f + (vertical ? 0 : (ox < z.DoorX ? 0.3f : -0.3f)), 0.04f, oy + 0.5f + (vertical ? (oy < z.DoorY ? 0.3f : -0.3f) : 0)), Gfx.OakDark);
        foreach (int s in new[] { -1, 1 })
        {
            var basePos = new Vector3(ox + 0.5f, 0, oy + 0.5f) + (vertical ? new Vector3(s * 0.85f, 0, 0) : new Vector3(0, 0, s * 0.85f));
            Sandbags(basePos, vertical);
        }
    }

    void Sandbags(Vector3 at, bool alongX)
    {
        for (int row = 0; row < 3; row++)
            for (int k = 0; k < 3 - row; k++)
            {
                float off = (k - (2 - row) / 2f) * 0.26f;
                var p = at + (alongX ? new Vector3(off, 0, 0) : new Vector3(0, 0, off)) + new Vector3(0, 0.06f + row * 0.1f, 0);
                var bag = Mesh3.Box(_buildings, new Vector3(alongX ? 0.26f : 0.18f, 0.1f, alongX ? 0.18f : 0.26f), p,
                    Gfx.Sandbag.Darkened(Gfx.Rand01((int)(p.X * 10), (int)(p.Z * 10), row) * 0.15f));
                bag.RotationDegrees = new Vector3(0, Gfx.Rand01((int)(p.X * 7), row, k) * 10 - 5, 0);
            }
    }

    void FireBuckets(Vector3 at)
    {
        Mesh3.Box(_buildings, new Vector3(0.6f, 0.35f, 0.04f), at + new Vector3(0, 0.45f, 0), Gfx.PostRed.Darkened(0.3f));
        for (int k = 0; k < 3; k++)
            Mesh3.Cyl(_buildings, 0.065f, 0.05f, 0.12f, at + new Vector3(-0.2f + k * 0.2f, 0.3f, 0.06f), Gfx.PostRed, 8);
    }

    static void Bicycle(Node3D parent, Vector3 at, float yaw)
    {
        var b = new Node3D { Position = at, Rotation = new Vector3(0.25f, yaw, 0) };
        parent.AddChild(b);
        var black = new Color("1e1d1b");
        foreach (float dx in new[] { -0.17f, 0.17f })
        {
            var w = Mesh3.Cyl(b, 0.12f, 0.12f, 0.015f, new Vector3(dx, 0.12f, 0), black, 12);
            w.RotationDegrees = new Vector3(90, 0, 0);
        }
        Mesh3.Box(b, new Vector3(0.36f, 0.025f, 0.02f), new Vector3(0, 0.2f, 0), black).RotationDegrees = new Vector3(0, 0, 12);
        Mesh3.Box(b, new Vector3(0.02f, 0.14f, 0.02f), new Vector3(-0.05f, 0.25f, 0), black);
        Mesh3.Box(b, new Vector3(0.08f, 0.02f, 0.05f), new Vector3(-0.05f, 0.32f, 0), new Color("3b2a1c"));
        Mesh3.Box(b, new Vector3(0.02f, 0.02f, 0.18f), new Vector3(0.15f, 0.33f, 0), black);
    }

    void Foundations(Zone z)
    {
        float cx = z.X + z.W / 2f, cy = z.Y + z.H / 2f;
        var concrete = Textures.Mat(Surface.Concrete, Colors.White);
        if (z.Available)
        {
            Mesh3.Box(_buildings, new Vector3(z.W - 1, 0.1f, z.H - 1), new Vector3(cx, 0.05f, cy), concrete);
            for (int k = 0; k < 4; k++)
                Mesh3.Box(_buildings, new Vector3(2.2f, 0.07f, 0.16f), new Vector3(z.X + 2.2f, 0.14f + k * 0.07f, z.Y + 1.3f + (k % 2) * 0.05f), Gfx.Oak.Lightened(0.1f));
            foreach (var (px, py) in new[] { (z.X + 0.3f, z.Y + 0.3f), (z.X + z.W - 0.3f, z.Y + 0.3f), (z.X + 0.3f, z.Y + z.H - 0.3f), (z.X + z.W - 0.3f, z.Y + z.H - 0.3f) })
                Mesh3.Box(_buildings, new Vector3(0.05f, 0.4f, 0.05f), new Vector3(px, 0.2f, py), Gfx.Oak);
        }
        Mesh3.BoxAt(_buildings, cx - 1.1f, 0, cy + 0.9f, 0.08f, 0.9f, 0.08f, Gfx.OakDark);
        Mesh3.BoxAt(_buildings, cx + 1.02f, 0, cy + 0.9f, 0.08f, 0.9f, 0.08f, Gfx.OakDark);
        Mesh3.BoxAt(_buildings, cx - 1.25f, 0.55f, cy + 0.85f, 2.5f, 0.6f, 0.06f, new Color("cfc6ad"));
        var text = z.Available ? $"{z.Name.ToUpperInvariant()}\nOpen for £{z.Cost:N0}" : $"{z.Name.ToUpperInvariant()}\nNot yet authorised";
        var l = Mesh3.Text(_buildings, text, new Vector3(cx, 0.86f, cy + 1.0f), 56, z.Available ? Gfx.Ink : Gfx.Ink.Lightened(0.3f), Fonts.Heading);
        l.PixelSize = 0.0045f;
        l.OutlineSize = 14;
        l.OutlineModulate = Gfx.Cream;
        l.NoDepthTest = true;
    }

    // ------------------------------------------------------------- grounds

    void Grounds(Grid g)
    {
        var post = new Color("4a3c2c");
        var wire = new Color("3a3632");
        void FenceRun(Vector3 a, Vector3 b)
        {
            float len = (b - a).Length();
            var dir = (b - a) / len;
            for (float t = 0; t <= len; t += 1.5f)
                Mesh3.Box(_buildings, new Vector3(0.06f, 0.6f, 0.06f), a + dir * t + new Vector3(0, 0.3f, 0), post);
            foreach (float h in new[] { 0.25f, 0.42f, 0.56f })
            {
                var w = Mesh3.Box(_buildings, new Vector3(len, 0.012f, 0.012f), (a + b) / 2 + new Vector3(0, h, 0), wire);
                w.Rotation = new Vector3(0, -Mathf.Atan2(dir.Z, dir.X), 0);
                w.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
        }
        float W = Grid.Width, H = Grid.Height;
        FenceRun(new Vector3(0.05f, 0, 0.05f), new Vector3(W - 0.05f, 0, 0.05f));
        FenceRun(new Vector3(W - 0.05f, 0, 0.05f), new Vector3(W - 0.05f, 0, H - 0.05f));
        FenceRun(new Vector3(0.05f, 0, H - 0.05f), new Vector3(W - 0.05f, 0, H - 0.05f));
        FenceRun(new Vector3(0.05f, 0, 0.05f), new Vector3(0.05f, 0, Grid.Gate.y - 1f));
        FenceRun(new Vector3(0.05f, 0, Grid.Gate.y + 2f), new Vector3(0.05f, 0, H - 0.05f));

        var brick = Textures.Mat(Surface.Brick, Colors.White);
        foreach (float gy in new[] { Grid.Gate.y - 0.8f, Grid.Gate.y + 1.8f })
        {
            Mesh3.Box(_buildings, new Vector3(0.4f, 1.0f, 0.4f), new Vector3(0.2f, 0.5f, gy), brick);
            Mesh3.Box(_buildings, new Vector3(0.48f, 0.08f, 0.48f), new Vector3(0.2f, 1.04f, gy), Gfx.StoneDressing);
        }
        for (int k = 0; k < 6; k++)
            Mesh3.Box(_buildings, new Vector3(0.06f, 0.06f, 0.42f), new Vector3(0.7f, 0.7f, Grid.Gate.y - 0.5f + k * 0.42f), k % 2 == 0 ? Gfx.Cream : Gfx.PostRed);
        var box = new Vector3(1.4f, 0, Grid.Gate.y - 1.6f);
        Mesh3.Box(_buildings, new Vector3(0.6f, 1.1f, 0.6f), box + new Vector3(0, 0.55f, 0), Textures.Mat(Surface.Weatherboard, new Color("9fa58a")));
        Mesh3.Prism(_buildings, new Vector3(0.75f, 0.3f, 0.75f), box + new Vector3(0, 1.25f, 0), Textures.Mat(Surface.Felt, Colors.White));
        Mesh3.Box(_buildings, new Vector3(0.4f, 0.5f, 0.02f), box + new Vector3(0, 0.65f, 0.31f), new Color("15130f"));

        for (int x = 4; x < Grid.Width - 2; x += 8)
        {
            if (g.Get(x, 11) != Tile.Lawn) continue;
            var p = new Vector3(x + 0.5f, 0, 11.6f);
            Mesh3.Cyl(_buildings, 0.05f, 0.07f, 2.4f, p + new Vector3(0, 1.2f, 0), new Color("4a3a2a"), 6);
            Mesh3.Box(_buildings, new Vector3(0.6f, 0.05f, 0.05f), p + new Vector3(0, 2.2f, 0), new Color("4a3a2a"));
        }

        var lorry = new Node3D { Position = new Vector3(3.2f, 0, 15.6f), RotationDegrees = new Vector3(0, 90, 0) };
        _buildings.AddChild(lorry);
        Mesh3.Box(lorry, new Vector3(0.9f, 0.55f, 0.85f), new Vector3(0, 0.45f, 0.75f), Gfx.KhakiGreen);
        Mesh3.Box(lorry, new Vector3(0.95f, 0.75f, 1.5f), new Vector3(0, 0.6f, -0.45f), Gfx.KhakiGreen.Darkened(0.15f));
        Mesh3.Box(lorry, new Vector3(0.6f, 0.2f, 0.05f), new Vector3(0, 0.62f, 1.18f), Gfx.Window.Darkened(0.4f));
        foreach (float wz in new[] { 0.8f, -0.8f })
            foreach (float wx in new[] { -0.45f, 0.45f })
                Mesh3.Cyl(lorry, 0.2f, 0.2f, 0.12f, new Vector3(wx, 0.2f, wz), new Color("1b1a18"), 12).RotationDegrees = new Vector3(0, 0, 90);
        var bike = new Node3D { Position = new Vector3(5.6f, 0, 11.4f), RotationDegrees = new Vector3(0, 20, 0) };
        _buildings.AddChild(bike);
        Bicycle(bike, Vector3.Zero, 0);
        Mesh3.Box(bike, new Vector3(0.16f, 0.12f, 0.1f), new Vector3(-0.02f, 0.2f, 0), Gfx.KhakiGreen);

        var mound = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1, Height = 1, IsHemisphere = true, RadialSegments = 16, Rings = 6 },
            Position = new Vector3(18.5f, 0, 21.5f), Scale = new Vector3(1.3f, 0.7f, 0.9f),
            MaterialOverride = Mesh3.Mat(Gfx.Lawn.Darkened(0.1f)),
        };
        _buildings.AddChild(mound);
        Mesh3.Box(_buildings, new Vector3(0.5f, 0.45f, 0.06f), new Vector3(18.5f, 0.22f, 22.42f), new Color("1d1b18"));
        Mesh3.Box(_buildings, new Vector3(1.8f, 0.5f, 1.2f), new Vector3(30.5f, 0.25f, 10.5f), Textures.Mat(Surface.Concrete, new Color("6a7470")));
        Mesh3.Box(_buildings, new Vector3(1.6f, 0.02f, 1.0f), new Vector3(30.5f, 0.48f, 10.5f), Gfx.Water.Darkened(0.3f));
        var label = Mesh3.Text(_buildings, "S.W.S.", new Vector3(30.5f, 0.3f, 11.12f), 24, Gfx.Cream, Fonts.BodyBold, billboard: false);
        label.PixelSize = 0.005f;
    }

    void Tree(int x, int y)
    {
        float ox = Gfx.Rand01(x, y, 1) * 0.4f - 0.2f, oz = Gfx.Rand01(x, y, 2) * 0.4f - 0.2f;
        float s = 0.55f + Gfx.Rand01(x, y, 5) * 0.35f;
        var root = new Node3D { Position = new Vector3(x + 0.5f + ox, 0, y + 0.5f + oz) };
        _buildings.AddChild(root);
        var bark = new Color("3e3024");
        if (Gfx.Rand01(x, y, 9) < 0.3f)
        {
            Mesh3.Cyl(root, 0.06f, 0.1f, 0.5f, new Vector3(0, 0.25f, 0), bark, 6);
            for (int k = 0; k < 3; k++)
                Mesh3.Cyl(root, 0.02f, s * (0.95f - k * 0.25f), 0.9f, new Vector3(0, 0.75f + k * 0.55f, 0), Gfx.TreeDark.Darkened(0.1f + k * 0.03f), 8);
            return;
        }
        Mesh3.Cyl(root, 0.08f, 0.11f, 0.7f, new Vector3(0, 0.35f, 0), bark, 8);
        Mesh3.Sphere(root, s, new Vector3(0, 0.75f + s * 0.8f, 0), Gfx.TreeDark);
        Mesh3.Sphere(root, s * 0.75f, new Vector3(-0.2f * s, 0.95f + s * 1.1f, 0.15f * s), Gfx.TreeMid);
        Mesh3.Sphere(root, s * 0.45f, new Vector3(-0.25f * s, 1.15f + s * 1.4f, 0.25f * s), Gfx.TreeLight);
    }
}
