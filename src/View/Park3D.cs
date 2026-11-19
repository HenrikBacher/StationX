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
    /// <summary>The gate's moving parts, kept out of the merged static meshes: the barrier pole and the sentry.</summary>
    Node3D? _gate;
    Node3D? _boom, _sentryArm;
    float _boomOpen;
    public readonly List<OmniLight3D> Lamps = new();
    string _sig = "";
    int _quarter;

    // Roofs: on when zoomed out (the Park from outside, all walls standing), fading away as the camera comes in to
    // look inside. Their materials are their own, so fading them leaves the walls and floors alone.
    readonly List<MeshInstance3D> _roofs = new();
    readonly List<StandardMaterial3D> _roofMats = new();
    StandardMaterial3D? _felt, _feltTrim, _slate, _flatRoof, _gable;
    /// <summary>The roof materials with moss in their detail layer, bare while snow lies on them.</summary>
    readonly List<StandardMaterial3D> _mossy = new();
    /// <summary>The porch roof's slate: the roofs' look, but it stays when they fade (the porch is part of the walls).</summary>
    StandardMaterial3D? _porchSlate;
    /// <summary>Each roof material's tone, and how far its photo must be brightened to look snow-covered.</summary>
    readonly Dictionary<StandardMaterial3D, (Color tone, float snow)> _roofTones = new();
    float _roofAlpha, _roofSnow;
    bool _roofed;
    /// <summary>Camera sizes (tiles of view height) over which the roofs fade in.</summary>
    public const float RoofsFrom = 22, RoofsFull = 28;

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
        float roofSnow = Mathf.Clamp(cover, 0, 1);
        if (Mathf.Abs(roofSnow - _roofSnow) > 0.01f) { _roofSnow = roofSnow; ApplyRoofs(); }
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
            node.Visible = _roofed ? tall : tall ? !near : near;
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
        var (paint, mask) = PaintGround(g);
        _ground.MaterialOverride = (Material?)Ground.Make(paint, mask) ?? new StandardMaterial3D
        {
            AlbedoTexture = paint,
            Roughness = 0.97f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            // Fine world-space grit over the painted ground, a few repeats per tile
            DetailEnabled = true,
            DetailAlbedo = Textures.Get(Surface.Grit),
            DetailBlendMode = BaseMaterial3D.BlendModeEnum.Mul,
            DetailUVLayer = BaseMaterial3D.DetailUV.UV2,
            UV2Triplanar = true,
            UV2WorldTriplanar = true,
            UV2Scale = Vector3.One * 2.5f,
        };

        _snowMat.AlbedoTexture = SnowMask(g);
        foreach (var c in _buildings.GetChildren()) c.QueueFree();
        _headLabels.Clear();
        Lamps.Clear();
        _sided.Clear();
        _roofs.Clear();

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
        MergeStatic();
        ApplyRoofs();
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

    /// <summary>
    /// The ground's colour, and a mask of what lies where for the photographed ground (r gravel, g earth, b wet,
    /// a grass; the rest, under buildings and the lake, stays plain).
    /// </summary>
    static bool IsPath(Grid g, int x, int y) =>
        x >= 0 && y >= 0 && x < Grid.Width && y < Grid.Height && g.Get(x, y) is Tile.Path or Tile.Door;

    /// <summary>
    /// Gravel or not, with ragged edges: near the boundary between a path tile and grass, noise decides, so grass creeps
    /// over the gravel in places and the gravel spills into the lawn in others.
    /// </summary>
    static bool PathAt(Grid g, float fx, float fy)
    {
        int tx = (int)fx, ty = (int)fy;
        bool here = IsPath(g, tx, ty);
        float lx = fx - tx, ly = fy - ty, edge = 1;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx == 0 && dy == 0) || IsPath(g, tx + dx, ty + dy) == here) continue;
                float ex = dx < 0 ? lx : dx > 0 ? 1 - lx : 0, ey = dy < 0 ? ly : dy > 0 ? 1 - ly : 0;
                edge = Mathf.Min(edge, dx != 0 && dy != 0 ? Mathf.Max(ex, ey) : ex + ey);
            }
        if (edge >= 0.3f) return here;
        float n = Blob(fx * 4, fy * 4, 1, here ? 51 : 53) * 0.75f + Blob(fx * 13, fy * 13, 1, 57) * 0.25f;
        return here ? edge > n * 0.22f : edge < n * 0.3f - 0.06f;
    }

    static (ImageTexture paint, ImageTexture mask) PaintGround(Grid g)
    {
        int W = Grid.Width * PxPerTile, H = Grid.Height * PxPerTile;
        var data = new byte[W * H * 3];
        var mask = new byte[W * H * 4];
        void Put(int px, int py, Color c)
        {
            int i = (py * W + px) * 3;
            data[i] = (byte)(Mathf.Clamp(c.R, 0, 1) * 255);
            data[i + 1] = (byte)(Mathf.Clamp(c.G, 0, 1) * 255);
            data[i + 2] = (byte)(Mathf.Clamp(c.B, 0, 1) * 255);
        }
        void Mask(int px, int py, float gravel, float earth, float wet, float grass)
        {
            int i = (py * W + px) * 4;
            mask[i] = (byte)(Mathf.Clamp(gravel, 0, 1) * 255);
            mask[i + 1] = (byte)(Mathf.Clamp(earth, 0, 1) * 255);
            mask[i + 2] = (byte)(Mathf.Clamp(wet, 0, 1) * 255);
            mask[i + 3] = (byte)(Mathf.Clamp(grass, 0, 1) * 255);
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
                float gravel = 0, earth = 0, wet = 0;
                float wear = Blob(fx, fy, 3, 17);
                if (wear > 0.62f)
                {
                    c = c.Lerp(Gfx.Mud.Lightened(0.15f), (wear - 0.62f) * 1.6f);
                    earth = (wear - 0.62f) * 2.2f;
                }
                if (noise > 0.93f) c = c.Darkened(0.15f);

                // Trampled mud around doorways and the gate.
                foreach (var (dx0, dy0) in doors)
                {
                    float d = Mathf.Sqrt((fx - dx0 - 0.5f) * (fx - dx0 - 0.5f) + (fy - dy0 - 0.5f) * (fy - dy0 - 0.5f));
                    if (d >= 2.2f) continue;
                    float t = Mathf.Clamp((2.2f - d) / 1.6f, 0, 0.85f) * (0.6f + 0.4f * Blob(fx, fy, 1, 23));
                    c = c.Lerp(Gfx.Mud, t);
                    earth = Mathf.Max(earth, t * 1.3f);
                }

                // Drip line: under the eaves rain off the roof keeps the grass down to bare, dark earth.
                foreach (var z in g.Zones)
                {
                    if (!z.Unlocked) continue;
                    float ox = Mathf.Max(z.X + 0.5f - fx, fx - (z.X + z.W - 0.5f)), oy = Mathf.Max(z.Y + 0.5f - fy, fy - (z.Y + z.H - 0.5f));
                    float outside = Mathf.Max(ox, oy);
                    if (outside < 0.15f || outside > 0.75f) continue;
                    float band = 0.38f + 0.2f * Blob(fx * 3, fy * 3, 1, 67);
                    if (outside > band) continue;
                    float t = Mathf.Clamp((band - outside) / 0.15f, 0, 1) * 0.75f;
                    c = c.Lerp(Gfx.Mud.Darkened(0.15f), t);
                    earth = Mathf.Max(earth, t);
                }

                // Damp shade: the strip north of a building (and less, west of it) that the sun never reaches, where
                // moss takes over the grass and creeps over the gravel.
                float damp = 0;
                foreach (var z in g.Zones)
                {
                    if (!z.Unlocked) continue;
                    float n = z.Y + 0.5f - fy, w = z.X + 0.5f - fx;
                    bool alongN = fx > z.X + 0.2f && fx < z.X + z.W - 0.2f, alongW = fy > z.Y + 0.2f && fy < z.Y + z.H - 0.2f;
                    if (alongN && n > 0 && n < 1.6f) damp = Mathf.Max(damp, 1 - n / 1.6f);
                    if (alongW && w > 0 && w < 1.1f) damp = Mathf.Max(damp, (1 - w / 1.1f) * 0.6f);
                }
                if (damp > 0)
                {
                    damp *= Mathf.Clamp(Blob(fx * 2.5f, fy * 2.5f, 1, 81) * 1.6f - 0.25f, 0, 1);
                    c = c.Lerp(Gfx.Moss.Darkened(noise * 0.1f), damp * 0.6f);
                }

                if (PathAt(g, fx, fy))
                {
                    c = Gfx.Gravel.Darkened(noise * 0.12f);
                    if ((Gfx.Hash(px, py, 9) & 0x1F) == 0) c = Gfx.GravelDot;
                    // Lorry ruts along the drive, and puddles.
                    if (ty is >= 12 and <= 14 && (Math.Abs(fy - 12.9f) < 0.12f || Math.Abs(fy - 14.1f) < 0.12f)) c = c.Darkened(0.22f);
                    float puddle = Blob(fx, fy, 2, 29);
                    if (puddle > 0.8f) c = c.Lerp(new Color("3b4446"), Mathf.Clamp((puddle - 0.8f) * 8, 0, 0.85f));
                    gravel = 1; earth = 0;
                    wet = Mathf.Clamp((puddle - 0.8f) * 8, 0, 1);
                    // Green in the damp shade, most of all at the path's edge, where the gravel is thin and seldom trodden.
                    if (damp > 0)
                    {
                        float edge = PathAt(g, fx + 0.25f, fy) && PathAt(g, fx - 0.25f, fy) && PathAt(g, fx, fy + 0.25f) && PathAt(g, fx, fy - 0.25f) ? 0.45f : 1f;
                        c = c.Lerp(Gfx.Moss.Lightened(0.1f), damp * 0.5f * edge);
                    }
                }
                earth = Mathf.Clamp(earth, 0, 1 - gravel);
                float grass = 1 - gravel - earth;

                // Lake
                float lx = (fx - 9.5f) / 6.0f, ly = (fy - 23f) / 4.2f;
                float ld = lx * lx + ly * ly;
                if (ld < 1.0f)
                {
                    c = ld > 0.86f ? Gfx.Shore.Darkened(noise * 0.1f) : Gfx.Water.Darkened(0.2f * (1 - ld));
                    (gravel, earth, wet, grass) = ld > 0.86f ? (0f, 1f, 0.3f, 0f) : (0f, 0f, 0f, 0f);
                }

                foreach (var z in g.Zones)
                {
                    if (fx < z.X + 0.5f || fx > z.X + z.W - 0.5f || fy < z.Y + 0.5f || fy > z.Y + z.H - 0.5f) continue;
                    if (!z.Unlocked && !z.Available)
                    {
                        // Only surveyed so far: grass, trampled where the surveyors walked the lines.
                        float edge = Mathf.Min(Mathf.Min(fx - z.X - 0.5f, z.X + z.W - 0.5f - fx), Mathf.Min(fy - z.Y - 0.5f, z.Y + z.H - 0.5f - fy));
                        if (edge < 0.35f)
                        {
                            float t = (0.35f - edge) / 0.35f * 0.5f * Blob(fx * 2, fy * 2, 1, 71);
                            c = c.Lerp(Gfx.Mud.Lightened(0.1f), t);
                            earth = Mathf.Max(earth, t);
                            grass = 1 - earth;
                        }
                        continue;
                    }
                    (gravel, earth, wet, grass) = (0f, 0f, 0f, 0f);
                    if (!z.Unlocked)
                    {
                        // Authorised: topsoil stripped, the slab going down.
                        c = Gfx.Slab.Darkened(noise * 0.08f);
                        if (Blob(fx, fy, 1, 37) > 0.75f) c = c.Darkened(0.12f);
                        earth = 1;
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
                Mask(px, py, gravel, earth, wet, grass);
            }
        var img = Image.CreateFromData(W, H, false, Image.Format.Rgb8, data);
        img.GenerateMipmaps();
        var mimg = Image.CreateFromData(W, H, false, Image.Format.Rgba8, mask);
        mimg.GenerateMipmaps();
        return (ImageTexture.CreateFromImage(img), ImageTexture.CreateFromImage(mimg));
    }

    // ------------------------------------------------------------- buildings

    void Building(Zone z)
    {
        bool mansion = z.Id == "mansion";
        Material wall = z.Brick ? Textures.Mat(Surface.Brick, mansion ? Colors.White : new Color("c8b8a8"))
                                : Textures.Mat(Surface.Weatherboard, Colors.White);
        var cap = z.Brick ? Gfx.StoneDressing : Gfx.Creosote.Darkened(0.2f);
        float x0 = z.X + 0.5f, x1 = z.X + z.W - 0.5f, y0 = z.Y + 0.5f, y1 = z.Y + z.H - 0.5f;

        // The floor: a high-resolution repeating texture over the coarse painted ground.
        var floorSurface = mansion ? Surface.Parquet : z.Brick ? Surface.QuarryTile : Surface.Floorboards;
        _buildings.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(x1 - x0, y1 - y0) },
            Position = new Vector3((x0 + x1) / 2, 0.008f, (y0 + y1) / 2),
            MaterialOverride = Textures.Mat(floorSurface, Colors.White, floorSurface == Surface.Floorboards ? 0.5f : 1f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

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
        foreach (var (side, ax, ay, bx, by) in sides) Grime(side, ax, ay, bx, by, z.Brick);
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
        Roof(z);
        if (mansion) MansionDetails(z);
        else if (z.Brick) BlockDetails(z);
        else HutDetails(z);
        DoorDetails(z);

        bool doorOnTop = z.DoorY == z.Y;
        var name = Mesh3.Text(_buildings, z.Name.ToUpperInvariant(), new Vector3(z.DoorX + 0.5f, 1.95f, doorOnTop ? z.Y + 0.3f : z.Y + z.H - 0.7f), 64, new Color("1d1a16"), Fonts.Heading);
        name.PixelSize = 0.0075f;
        name.OutlineSize = 16;
        name.OutlineModulate = new Color("d9cfb4");
        name.NoDepthTest = true;   // readable over the roofs
        name.RenderPriority = 20;
        var headLabel = Mesh3.Text(_buildings, "", name.Position + new Vector3(0, -0.42f, 0), 30, new Color("1d1a16"), Fonts.Body);
        headLabel.PixelSize = 0.0075f;
        headLabel.OutlineSize = 10;
        headLabel.OutlineModulate = new Color("d9cfb4");
        headLabel.NoDepthTest = true;
        headLabel.RenderPriority = 20;
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
        if (windows && alongX && gapA is float da && gapB is float db) Doorway(parent, z, da, db, ay, height, wall, outward);

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
                var wc = alongX ? new Vector3(t, wy, ay) : new Vector3(ax, wy, t);
                Window(parent, wc, ww, wh, alongX, outward, z.Brick);
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
            Mesh3.Box(north, new Vector3(0.36f, 0.5f, WallThick + 0.03f), new Vector3(gx, BackWall + 0.4f, y0), Mesh3.Glass);
            Mesh3.Box(north, new Vector3(0.12f, 0.12f, 0.12f), new Vector3(gx, BackWall + 1.2f, y0), stone);
        }
        foreach (float cx in new[] { x0 + 1.2f, (x0 + x1) / 2 - 1.4f, x1 - 1.3f })
            Chimney(north, new Vector3(cx, BackWall, y0 - 0.05f), brick);

        var west = Sided(3, 3, true);
        Mesh3.Box(west, new Vector3(0.5f, 0.14f, z.H - 0.6f), new Vector3(x0 - 0.12f, BackWall + 0.1f, (y0 + y1) / 2), slate);
        float by = (y0 + y1) / 2 + 0.5f;
        Mesh3.Box(west, new Vector3(0.6f, BackWall + 0.2f, 1.8f), new Vector3(x0 - 0.38f, (BackWall + 0.2f) / 2, by), brick);
        for (int k = -1; k <= 1; k++)
            Mesh3.Box(west, new Vector3(0.05f, 0.6f, 0.42f), new Vector3(x0 - 0.7f, 0.95f, by + k * 0.55f), Mesh3.Glass);
        Mesh3.Prism(west, new Vector3(0.7f, 0.5f, 1.9f), new Vector3(x0 - 0.38f, BackWall + 0.45f, by), slate);
        Chimney(west, new Vector3(x0 - 0.05f, BackWall, y1 - 1.6f), brick);

        var corner = Sided(0, 3, true);
        var drum = new Vector3(x0 + 0.35f, BackWall, y0 + 0.35f);
        Mesh3.Cyl(corner, 0.62f, 0.62f, 0.55f, drum + new Vector3(0, 0.27f, 0), new Color("8a4532"), 20);
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.Tau / 8;
            Mesh3.Box(corner, new Vector3(0.12f, 0.3f, 0.03f), drum + new Vector3(Mathf.Cos(a) * 0.62f, 0.3f, Mathf.Sin(a) * 0.62f), Mesh3.Glass)
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
        MansionFront(z, doorSouth, brick, stone);
    }

    /// <summary>
    /// The entrance front, seen when its wall stands (zoomed out, or from the other side): a gabled stone porch on
    /// columns and two square bays with sash windows. Victorian and overdone, like the rest of the house.
    /// </summary>
    void MansionFront(Zone z, bool south, Material brick, Material stone)
    {
        float x0 = z.X + 0.5f, x1 = z.X + z.W - 0.5f;
        float wallZ = south ? z.Y + z.H - 0.5f : z.Y + 0.5f, o = south ? 1 : -1;
        var front = Sided(south ? 2 : 0, south ? 2 : 0, true);
        var slate = _porchSlate ??= Textures.Fading("roof_slates_02", new Color("7c8290"), 2.4f) ?? Textures.Mat(Surface.Felt, new Color("8a8f98"), 2);

        // Porch: four columns, an entablature and a pediment over the door.
        float cx = z.DoorX + 0.5f, depth = 0.85f, ph = 1.15f;
        float pz = wallZ + o * (WallThick / 2 + depth);
        foreach (float dx in new[] { -0.62f, 0.62f })
            foreach (float dz in new[] { 0f, -o * 0.55f })
                Mesh3.Cyl(front, 0.06f, 0.07f, ph, new Vector3(cx + dx, ph / 2, pz + dz), Gfx.StoneDressing, 10);
        Mesh3.Box(front, new Vector3(1.55f, 0.14f, depth + 0.2f), new Vector3(cx, ph + 0.07f, wallZ + o * (WallThick / 2 + depth / 2)), stone);
        Mesh3.Prism(front, new Vector3(1.6f, 0.42f, depth + 0.24f), new Vector3(cx, ph + 0.35f, wallZ + o * (WallThick / 2 + depth / 2)), slate);
        Mesh3.Prism(front, new Vector3(1.6f, 0.42f, 0.06f), new Vector3(cx, ph + 0.35f, pz + o * 0.12f), stone);

        // Two square bays either side of the entrance.
        foreach (float bx in new[] { x0 + (cx - x0) * 0.45f, x1 - (x1 - cx) * 0.45f })
        {
            const float bw = 1.5f, bd = 0.55f, bh = 1.25f;
            float bz = wallZ + o * (WallThick / 2 + bd / 2);
            Mesh3.Box(front, new Vector3(bw, bh, bd), new Vector3(bx, bh / 2, bz), brick);
            Mesh3.Box(front, new Vector3(bw + 0.12f, 0.08f, bd + 0.1f), new Vector3(bx, bh + 0.04f, bz + o * 0.03f), stone);
            Mesh3.Box(front, new Vector3(bw + 0.04f, 0.1f, bd + 0.04f), new Vector3(bx, 0.55f, bz), stone);
            // A tall sash in the front face and a narrow one in each side.
            Mesh3.Box(front, new Vector3(0.62f, 0.62f, 0.03f), new Vector3(bx, 0.95f, bz + o * (bd / 2 + 0.005f)), Mesh3.Glass);
            Mesh3.Box(front, new Vector3(0.66f, 0.04f, 0.04f), new Vector3(bx, 0.95f, bz + o * (bd / 2 + 0.01f)), Gfx.Cream.Darkened(0.1f));
            foreach (float sx in new[] { -1f, 1f })
                Mesh3.Box(front, new Vector3(0.03f, 0.62f, 0.3f), new Vector3(bx + sx * (bw / 2 + 0.005f), 0.95f, bz), Mesh3.Glass);
            Mesh3.Box(front, new Vector3(0.7f, 0.06f, 0.06f), new Vector3(bx, 1.3f - 0.05f, bz + o * (bd / 2 + 0.02f)), stone);
        }
    }

    static ImageTexture? _grimeTex, _greenGrimeTex;

    /// <summary>
    /// The splash-back streaks: soot-brown, or (green) with a film of algae and a few cushions of moss along the
    /// bottom, for the walls the sun seldom reaches.
    /// </summary>
    static ImageTexture GrimeTex(bool green)
    {
        if ((green ? _greenGrimeTex : _grimeTex) is { } done) return done;
        const int W = 128, H = 32;
        var soot = new Color(0.16f, 0.13f, 0.1f);
        var algae = new Color(0.17f, 0.2f, 0.09f);
        var moss = new Color(0.22f, 0.26f, 0.11f);
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        for (int x = 0; x < W; x++)
        {
            float streak = 0.55f + 0.45f * Blob(x * 0.35f, 0, 1, 61) + 0.25f * (Gfx.Rand01(x, 0, 62) - 0.5f);
            for (int y = 0; y < H; y++)
            {
                float up = y / (H - 1f);   // 0 at the ground
                float a = Mathf.Pow(Mathf.Clamp(1 - up / Mathf.Clamp(streak, 0.25f, 1f), 0, 1), 1.6f);
                a *= 0.85f + 0.15f * Gfx.Rand01(x, y, 63);
                var c = soot;
                if (green)
                {
                    // Algae where the splash-back stays wet longest, the low courses; moss tufts in the angle with the ground.
                    c = algae.Lerp(soot, Mathf.Clamp(up * 2.2f, 0, 1));
                    float tuft = Blob(x * 0.5f, y * 0.9f, 1, 64);
                    if (up < 0.22f && tuft > 0.55f)
                    {
                        float t = Mathf.Clamp((tuft - 0.55f) * 6, 0, 1) * (1 - up / 0.22f);
                        c = c.Lerp(moss.Lightened(0.15f * Gfx.Rand01(x, y, 65)), t);
                        a = Mathf.Max(a, t * 0.9f);
                    }
                }
                img.SetPixel(x, y, new Color(c.R, c.G, c.B, a));
            }
        }
        img.GenerateMipmaps();
        var tex = ImageTexture.CreateFromImage(img);
        if (green) _greenGrimeTex = tex; else _grimeTex = tex;
        return tex;
    }

    /// <summary>
    /// Rain splash-back and soot up the foot of each outside wall, in uneven streaks: a decal projected onto the
    /// wall's outer face (and only that: surfaces facing other ways are faded out). Shaded walls (north, west) stay
    /// damp, so theirs is green with algae and moss, and reaches a little higher.
    /// </summary>
    void Grime(int side, float ax, float ay, float bx, float by, bool brick)
    {
        bool alongX = side is 0 or 2;
        float len = alongX ? Mathf.Abs(bx - ax) : Mathf.Abs(by - ay);
        // Outward normal of this side: 0 N (-z), 1 E (+x), 2 S (+z), 3 W (-x).
        var outward = side switch { 0 => Vector3.Forward, 1 => Vector3.Right, 2 => Vector3.Back, _ => Vector3.Left };
        var mid = new Vector3((ax + bx) / 2, 0, (ay + by) / 2) + outward * (WallThick / 2);
        bool green = Mesh3.Shade(outward) > 0.5f;
        float h = (brick ? 0.55f : 0.45f) * (green ? 1.2f : 1f);
        var d = new Decal
        {
            Size = new Vector3(len + WallThick, 0.4f, h),
            TextureAlbedo = GrimeTex(green),
            AlbedoMix = 1,
            Modulate = new Color(1, 1, 1, (brick ? 0.6f : 0.75f) * (green ? 1.1f : 1f)),
            NormalFade = 0.4f,
            UpperFade = 0.05f,
            LowerFade = 0.05f,
        };
        // The decal projects along its -Y: point that into the wall. Its Z runs up the wall, X along it.
        d.Basis = new Basis(outward.Cross(Vector3.Up), outward, Vector3.Up);
        d.Position = mid + Vector3.Up * (h / 2);
        _buildings.AddChild(d);
    }

    /// <summary>Hipped slate on the Mansion, low felt gables on the huts, flat concrete on the brick blocks.</summary>
    void Roof(Zone z)
    {
        float x0 = z.X + 0.5f, x1 = z.X + z.W - 0.5f, y0 = z.Y + 0.5f, y1 = z.Y + z.H - 0.5f;
        if (_felt == null)
        {
            _felt = Textures.Fading("asphalt_02", new Color("67635d"), 1.3f);
            _feltTrim = Textures.Fading("asphalt_02", new Color("67635d"), 1.3f);
            _slate = Textures.Fading("roof_slates_02", new Color("7c8290"), 2.4f);
            _flatRoof = Textures.FadingCopy(Textures.Mat(Surface.Concrete, new Color("8a8a84"), 0.5f));
            _gable = Textures.FadingCopy(Textures.Mat(Surface.Weatherboard, Colors.White));
            foreach (var (m, snow) in new[] { (_felt, 2.1f), (_feltTrim, 2.1f), (_slate, 1.35f), (_flatRoof, 1.05f), (_gable, 0f) })
                if (m != null) { _roofMats.Add(m); _roofTones[m] = (m.AlbedoColor, snow); }
            // Moss and lichen on the slopes (graded down to the eaves, heavier on the north) and scattered on flat roofs.
            foreach (var (m, graded) in new[] { (_felt, true), (_slate, true), (_flatRoof, false) })
                if (m != null) { Textures.Mossy(m, graded); _mossy.Add(m); }
        }
        if (_felt == null || _feltTrim == null || _slate == null) return;
        MeshInstance3D roof;
        if (z.Id == "mansion")
        {
            const float o = 0.28f;
            roof = Mesh3.Roof(_buildings, x0 - o, x1 + o, y0 - o, y1 + o, BackWall + 0.02f, 1.35f, true, _slate);
        }
        else if (z.Brick)
            roof = Mesh3.Box(_buildings, new Vector3(x1 - x0 + 0.45f, 0.14f, y1 - y0 + 0.45f), new Vector3((x0 + x1) / 2, BackWall + 0.08f, (y0 + y1) / 2), _flatRoof!);
        else
        {
            // Overhanging the long walls more than the gable ends, and a ridge batten along the top.
            roof = Mesh3.Roof(_buildings, x0 - 0.14f, x1 + 0.14f, y0 - 0.3f, y1 + 0.3f, BackWall + 0.04f, 0.62f, false, _felt, _gable);
            _roofs.Add(Mesh3.Box(_buildings, new Vector3(x1 - x0 + 0.3f, 0.05f, 0.09f), new Vector3((x0 + x1) / 2, BackWall + 0.67f, (y0 + y1) / 2), _feltTrim));
            // Felt laid in strips down the slope, each joint held by a batten.
            for (float bx = x0 + 0.5f; bx < x1 - 0.2f; bx += 0.9f)
                foreach (float s in new[] { -1f, 1f })
                {
                    float half = (y1 - y0) / 2 + 0.3f, run = Mathf.Sqrt(half * half + 0.62f * 0.62f);
                    var b = Mesh3.Box(_buildings, new Vector3(0.035f, 0.03f, run), new Vector3(bx, BackWall + 0.04f + 0.31f + 0.015f, (y0 + y1) / 2 + s * half / 2), _feltTrim);
                    b.Rotation = new Vector3(s * Mathf.Atan2(0.62f, half), 0, 0);
                    _roofs.Add(b);
                }
        }
        _roofs.Add(roof);
    }

    /// <summary>
    /// The buildings are hundreds of small boxes; drawn one by one they cost integrated GPUs dearly. Every opaque,
    /// static piece is merged with the others sharing its material, within each group that's shown or hidden together
    /// (the Park itself, and each cut-away side), so a whole roofline or row of windows is one draw. Roofs stay apart,
    /// since they fade; anything transparent or with its own shader stays apart too.
    /// </summary>
    void MergeStatic()
    {
        MergeRoofs();
        var keep = new HashSet<Node>(_roofs);
        var containers = new List<Node3D> { _buildings };
        containers.AddRange(_sided.Select(x => x.node));
        var isContainer = new HashSet<Node>(containers);
        foreach (var box in containers)
        {
            var groups = new Dictionary<(Material, GeometryInstance3D.ShadowCastingSetting), SurfaceTool>();
            var merged = new List<MeshInstance3D>();
            void Collect(Node n, Transform3D parent)
            {
                foreach (var c in n.GetChildren())
                {
                    if (isContainer.Contains(c)) continue;
                    if (c is not Node3D c3) continue;
                    var xf = parent * c3.Transform;
                    if (c is MeshInstance3D mi && mi.Mesh != null && !keep.Contains(mi) && mi.GetChildCount() == 0 && mi.Visible)
                    {
                        bool ok = true;
                        var mats = new Material[mi.Mesh.GetSurfaceCount()];
                        for (int s = 0; s < mats.Length && ok; s++)
                        {
                            var m = mi.MaterialOverride ?? mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
                            ok = m is BaseMaterial3D { Transparency: BaseMaterial3D.TransparencyEnum.Disabled };
                            mats[s] = m!;
                        }
                        if (ok)
                        {
                            for (int s = 0; s < mats.Length; s++)
                            {
                                var key = (mats[s], mi.CastShadow);
                                if (!groups.TryGetValue(key, out var st))
                                {
                                    groups[key] = st = new SurfaceTool();
                                    st.Begin(Mesh.PrimitiveType.Triangles);
                                }
                                st.AppendFrom(mi.Mesh, s, xf);
                            }
                            merged.Add(mi);
                            continue;
                        }
                    }
                    Collect(c, xf);
                }
            }
            Collect(box, Transform3D.Identity);
            foreach (var mi in merged) { mi.GetParent().RemoveChild(mi); mi.QueueFree(); }
            foreach (var ((mat, shadow), st) in groups)
            {
                var mesh = st.Commit();
                mesh.SurfaceSetMaterial(0, mat);
                box.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = shadow });
            }
        }
    }

    /// <summary>
    /// The roofs fade together, so every roof piece sharing a material (all the huts' felt, every batten) becomes one
    /// mesh: a handful of draws for the whole roofscape instead of one per batten.
    /// </summary>
    void MergeRoofs()
    {
        var groups = new Dictionary<Material, SurfaceTool>();
        foreach (var mi in _roofs)
        {
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var m = mi.MaterialOverride ?? mi.Mesh.SurfaceGetMaterial(s);
                if (!groups.TryGetValue(m, out var st))
                {
                    groups[m] = st = new SurfaceTool();
                    st.Begin(Mesh.PrimitiveType.Triangles);
                }
                st.AppendFrom(mi.Mesh, s, mi.Transform);
            }
            mi.GetParent().RemoveChild(mi);
            mi.QueueFree();
        }
        _roofs.Clear();
        foreach (var (m, st) in groups)
        {
            var mesh = st.Commit();
            mesh.SurfaceSetMaterial(0, m);
            var mi = new MeshInstance3D { Mesh = mesh };
            _buildings.AddChild(mi);
            _roofs.Add(mi);
        }
    }

    /// <summary>Fades the roofs by the camera's view size: off up close, on when looking at the whole Park.</summary>
    public void SetRoofs(float camSize)
    {
        float a = Mathf.SmoothStep(RoofsFrom, RoofsFull, camSize);
        if (a > 0.9f) a = 1;   // the last of the fade isn't worth blending for
        if (Mathf.Abs(a - _roofAlpha) < 0.01f) return;
        _roofAlpha = a;
        ApplyRoofs();
    }

    void ApplyRoofs()
    {
        foreach (var m in _roofMats)
        {
            // Snow lies on the roof slopes; the upright gable ends stay bare.
            var (tone, snow) = _roofTones[m];
            var c = snow == 0 ? tone : tone.Lerp(new Color("d4d8dd") * snow, _roofSnow * 0.75f);
            c.A = _roofAlpha;
            m.AlbedoColor = c;
            if (_mossy.Contains(m)) m.DetailEnabled = _roofSnow < 0.3f;
            // Blended while fading (a dither looks like static), solid once it's all there.
            m.Transparency = _roofAlpha > 0.99f ? BaseMaterial3D.TransparencyEnum.Disabled : BaseMaterial3D.TransparencyEnum.Alpha;
        }
        foreach (var r in _roofs)
        {
            r.Visible = _roofAlpha > 0.01f;
            r.CastShadow = _roofAlpha > 0.95f ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
        }
        bool roofed = _roofAlpha > 0.5f;
        if (roofed == _roofed) return;
        _roofed = roofed;
        SetView(_quarter);
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

    /// <summary>
    /// A window: a pane of glass just proud of the wall, a painted frame round it and glazing bars dividing it into
    /// small panes (hut casements 3 by 2, sash windows in brick 2 by 3), with thin anti-blast tape across the glass.
    /// </summary>
    static void Window(Node3D parent, Vector3 wc, float ww, float wh, bool alongX, Vector3 outward, bool brick)
    {
        Vector3 Size(float along, float h, float depth) => alongX ? new Vector3(along, h, depth) : new Vector3(depth, h, along);
        Vector3 Along(float t) => alongX ? new Vector3(t, 0, 0) : new Vector3(0, 0, t);
        var paint = brick ? new Color("d2cab6") : new Color("b3ab93");
        float glassD = WallThick + 0.02f, barD = WallThick + 0.05f, frameD = WallThick + 0.06f;
        Mesh3.Box(parent, Size(ww, wh, glassD), wc, Mesh3.Glass);
        const float fw = 0.06f, bw = 0.025f;
        Mesh3.Box(parent, Size(ww + 2 * fw, fw, frameD), wc + new Vector3(0, wh / 2 + fw / 2, 0), paint);
        Mesh3.Box(parent, Size(ww + 2 * fw, fw, frameD), wc - new Vector3(0, wh / 2 + fw / 2, 0), paint);
        Mesh3.Box(parent, Size(fw, wh, frameD), wc + Along(ww / 2 + fw / 2), paint);
        Mesh3.Box(parent, Size(fw, wh, frameD), wc - Along(ww / 2 + fw / 2), paint);
        int cols = brick ? 2 : 3, rows = brick ? 3 : 2;
        for (int i = 1; i < cols; i++)
            Mesh3.Box(parent, Size(bw, wh, barD), wc + Along(-ww / 2 + ww * i / cols), paint);
        for (int j = 1; j < rows; j++)
            Mesh3.Box(parent, Size(ww, bw, barD), wc + new Vector3(0, -wh / 2 + wh * j / rows, 0), paint);
        // The sill, outside only (brick windows have stone sills already).
        if (!brick)
            Mesh3.Box(parent, Size(ww + 0.16f, 0.04f, 0.08f), wc + new Vector3(0, -wh / 2 - fw - 0.02f, 0) + outward * (WallThick / 2 + 0.04f),
                paint.Darkened(0.1f));
        // Anti-blast tape, criss-cross on the inside and outside of the glass.
        var tapeColor = new Color("cbbf9f");
        foreach (float f in new[] { -1f, 1f })
        {
            var face = wc + outward * f * (glassD / 2 + 0.003f);
            for (int k = 0; k < 2; k++)
            {
                float diag = Mathf.Atan2(wh, ww) * (k == 0 ? 1 : -1);
                var tape = Mesh3.Box(parent, new Vector3(Mathf.Sqrt(ww * ww + wh * wh), 0.02f, 0.004f), Vector3.Zero, tapeColor);
                tape.Position = face;
                tape.Rotation = alongX ? new Vector3(0, 0, diag) : new Vector3(diag, Mathf.Pi / 2, 0);
            }
        }
    }

    /// <summary>
    /// A wartime army lorry, nose to +Z: ladder chassis, cab with a glazed windscreen, a long bonnet with a dark grille
    /// and headlamps on the wings, and a canvas tilt over the load bed.
    /// </summary>
    static void Lorry(Node3D lorry)
    {
        var body = Gfx.KhakiGreen;
        var dark = new Color("23221c");
        var canvas = new Color("7a7553");
        Mesh3.Box(lorry, new Vector3(0.7f, 0.12f, 2.5f), new Vector3(0, 0.32f, -0.05f), dark);
        foreach (float wz in new[] { 0.95f, -0.75f })
            foreach (float wx in new[] { -0.4f, 0.4f })
            {
                Mesh3.Cyl(lorry, 0.22f, 0.22f, 0.14f, new Vector3(wx, 0.22f, wz), new Color("1b1a18"), 16).RotationDegrees = new Vector3(0, 0, 90);
                Mesh3.Cyl(lorry, 0.09f, 0.09f, 0.16f, new Vector3(wx, 0.22f, wz), body.Darkened(0.2f), 10).RotationDegrees = new Vector3(0, 0, 90);
            }
        // Cab
        Mesh3.Box(lorry, new Vector3(0.92f, 0.6f, 0.55f), new Vector3(0, 0.68f, 0.5f), body);
        Mesh3.Box(lorry, new Vector3(0.96f, 0.04f, 0.62f), new Vector3(0, 1.0f, 0.5f), body.Darkened(0.1f));
        Mesh3.Box(lorry, new Vector3(0.78f, 0.22f, 0.02f), new Vector3(0, 0.84f, 0.78f), Mesh3.Glass);
        foreach (float sx in new[] { -1f, 1f })
        {
            Mesh3.Box(lorry, new Vector3(0.02f, 0.2f, 0.3f), new Vector3(sx * 0.465f, 0.84f, 0.56f), Mesh3.Glass);
            // Front wing over the wheel, with its headlamp
            Mesh3.Box(lorry, new Vector3(0.2f, 0.05f, 0.56f), new Vector3(sx * 0.4f, 0.47f, 0.98f), body.Darkened(0.1f));
            Mesh3.Cyl(lorry, 0.06f, 0.06f, 0.06f, new Vector3(sx * 0.37f, 0.56f, 1.2f), new Color("d8d0b0"), 10).RotationDegrees = new Vector3(90, 0, 0);
        }
        // Bonnet, grille and bumper
        Mesh3.Box(lorry, new Vector3(0.58f, 0.32f, 0.48f), new Vector3(0, 0.58f, 1.01f), body);
        Mesh3.Box(lorry, new Vector3(0.5f, 0.3f, 0.03f), new Vector3(0, 0.58f, 1.26f), dark);
        for (int i = -2; i <= 2; i++)
            Mesh3.Box(lorry, new Vector3(0.015f, 0.26f, 0.02f), new Vector3(i * 0.09f, 0.58f, 1.28f), body.Darkened(0.25f));
        Mesh3.Box(lorry, new Vector3(0.98f, 0.07f, 0.06f), new Vector3(0, 0.34f, 1.32f), dark);
        // Load bed and the canvas tilt, rounded over its hoops
        Mesh3.Box(lorry, new Vector3(0.98f, 0.28f, 1.45f), new Vector3(0, 0.52f, -0.52f), body.Darkened(0.15f));
        Mesh3.Box(lorry, new Vector3(0.96f, 0.44f, 1.4f), new Vector3(0, 0.88f, -0.52f), canvas);
        var top = Mesh3.Cyl(lorry, 0.48f, 0.48f, 1.4f, new Vector3(0, 1.09f, -0.52f), canvas, 18);
        top.RotationDegrees = new Vector3(90, 0, 0);
        top.Scale = new Vector3(1, 1, 0.32f);
        foreach (float sx in new[] { -1f, 1f })
            Mesh3.Box(lorry, new Vector3(0.2f, 0.05f, 0.5f), new Vector3(sx * 0.4f, 0.47f, -0.75f), dark);
        // Rear flap tied up, and the tailgate
        Mesh3.Box(lorry, new Vector3(0.7f, 0.3f, 0.02f), new Vector3(0, 0.88f, -1.23f), canvas.Darkened(0.3f));
        Mesh3.Box(lorry, new Vector3(0.98f, 0.06f, 0.08f), new Vector3(0, 0.32f, -1.27f), dark);
    }

    /// <summary>
    /// The doorway in a full-height wall: wall over the door on a lintel, a frame, and the door itself standing open
    /// outwards (ledged and braced boards painted green on the huts, oak on the Mansion and the blocks).
    /// </summary>
    void Doorway(Node3D parent, Zone z, float a, float b, float wallZ, float height, Material wall, Vector3 outward)
    {
        const float head = 1.08f, post = 0.06f;
        float w = b - a, mid = (a + b) / 2;
        if (height > head + 0.05f)
            Mesh3.Box(parent, new Vector3(w, height - head, WallThick), new Vector3(mid, (head + height) / 2, wallZ), wall);
        var frame = z.Brick ? Gfx.StoneDressing : new Color("d7cfb8");
        Mesh3.Box(parent, new Vector3(w + 0.14f, 0.08f, WallThick + 0.05f), new Vector3(mid, head + 0.04f, wallZ), frame);
        foreach (float x in new[] { a + post / 2, b - post / 2 })
            Mesh3.Box(parent, new Vector3(post, head, WallThick + 0.03f), new Vector3(x, head / 2, wallZ), frame);

        // The leaf, hinged at one jamb on the outer face and swung out a little past square.
        var paint = z.Brick ? Gfx.Oak : new Color("3d4a3a");
        float leafW = w - 2 * post, leafH = head - 0.04f;
        var hinge = new Vector3(a + post, 0, wallZ) + outward * (WallThick / 2 + 0.02f);
        float yaw = outward.Z < 0 ? Mathf.DegToRad(105) : -Mathf.DegToRad(105);
        var dir = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
        var leaf = new Node3D { Position = hinge + dir * (leafW / 2), Rotation = new Vector3(0, yaw, 0) };
        parent.AddChild(leaf);
        Mesh3.Box(leaf, new Vector3(leafW, leafH, 0.04f), new Vector3(0, leafH / 2 + 0.02f, 0), paint);
        // Ledges and a brace on the back, a knob on the front.
        foreach (float y in new[] { 0.18f, leafH / 2, leafH - 0.15f })
            Mesh3.Box(leaf, new Vector3(leafW - 0.04f, 0.06f, 0.025f), new Vector3(0, y, -0.03f), paint.Darkened(0.15f));
        var brace = Mesh3.Box(leaf, new Vector3(0.05f, Mathf.Sqrt(leafW * leafW + leafH * leafH) * 0.42f, 0.02f), new Vector3(0, leafH * 0.34f, -0.03f), paint.Darkened(0.15f));
        brace.Rotation = new Vector3(0, 0, Mathf.Atan2(leafW, leafH * 0.5f));
        Mesh3.Sphere(leaf, 0.025f, new Vector3(leafW / 2 - 0.09f, 0.5f, 0.035f), Gfx.Brass);
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
        if (!z.Available) SurveyLines(z);
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

    /// <summary>A plot that's only been surveyed: pegs round the footprint, string between them, a ranging pole.</summary>
    /// <summary>
    /// The barrier pole across the gate, pivoting on a post by the north pier with a counterweight, and the sentry
    /// standing at his box. Built outside <see cref="_buildings"/> so they can move.
    /// </summary>
    void GateParts()
    {
        _gate?.QueueFree();
        _gate = new Node3D { Name = "Gate" };
        AddChild(_gate);
        float gy = Grid.Gate.y;

        // Pivot post and the pole: red and white bands, counterweight behind the pivot, a rest post on the far side.
        Mesh3.Box(_gate, new Vector3(0.12f, 0.75f, 0.12f), new Vector3(0.75f, 0.375f, gy - 0.62f), Gfx.Cream);
        Mesh3.Box(_gate, new Vector3(0.08f, 0.62f, 0.08f), new Vector3(0.75f, 0.31f, gy + 1.68f), Gfx.Cream);
        _boom = new Node3D { Position = new Vector3(0.75f, 0.72f, gy - 0.62f) };
        _gate.AddChild(_boom);
        for (int k = 0; k < 6; k++)
            Mesh3.Box(_boom, new Vector3(0.06f, 0.06f, 0.4f), new Vector3(0, 0, 0.2f + k * 0.4f), k % 2 == 0 ? Gfx.Cream : Gfx.PostRed);
        Mesh3.Box(_boom, new Vector3(0.1f, 0.1f, 0.22f), new Vector3(0, 0, -0.18f), new Color("3a3a36"));
        SetBoom(_boomOpen);

        // The sentry: battledress, webbing, steel helmet, rifle slung; facing the road, by his box.
        var guard = new Node3D { Position = new Vector3(1.4f, 0, gy - 1.05f), RotationDegrees = new Vector3(0, 180, 0) };
        _gate.AddChild(guard);
        var serge = new Color("6a6446");
        var webbing = new Color("b9ae86");
        var boots = new Color("1d1a17");
        var skin = new Color("d9b08c");
        foreach (float z in new[] { -0.06f, 0.06f })
        {
            Mesh3.Box(guard, new Vector3(0.09f, 0.42f, 0.09f), new Vector3(0, 0.25f, z), serge);          // legs
            Mesh3.Box(guard, new Vector3(0.12f, 0.06f, 0.09f), new Vector3(0.02f, 0.03f, z), boots);      // boots
            Mesh3.Box(guard, new Vector3(0.08f, 0.08f, 0.09f), new Vector3(0, 0.08f, z), webbing);       // anklets
        }
        Mesh3.Box(guard, new Vector3(0.16f, 0.3f, 0.26f), new Vector3(0, 0.6f, 0), serge);                 // blouse
        Mesh3.Box(guard, new Vector3(0.17f, 0.05f, 0.27f), new Vector3(0, 0.49f, 0), webbing);            // belt
        Mesh3.Box(guard, new Vector3(0.17f, 0.3f, 0.04f), new Vector3(0.0f, 0.62f, 0), webbing).RotationDegrees = new Vector3(40, 0, 0); // cross-strap
        Mesh3.Box(guard, new Vector3(0.08f, 0.1f, 0.12f), new Vector3(0.09f, 0.55f, 0.06f), webbing);     // pouch
        Mesh3.Sphere(guard, 0.07f, new Vector3(0, 0.83f, 0), skin);
        Mesh3.Dome(guard, 0.1f, new Vector3(0, 0.86f, 0), new Color("4b4a36"));
        Mesh3.Cyl(guard, 0.115f, 0.115f, 0.012f, new Vector3(0, 0.865f, 0), new Color("4b4a36"), 14);   // the brim
        // Left arm down, rifle slung on the left shoulder.
        Mesh3.Box(guard, new Vector3(0.07f, 0.3f, 0.07f), new Vector3(0, 0.58f, -0.16f), serge);
        var rifle = Mesh3.Box(guard, new Vector3(0.03f, 0.72f, 0.04f), new Vector3(-0.08f, 0.62f, -0.1f), new Color("4a3424"));
        rifle.RotationDegrees = new Vector3(0, 0, 12);
        // Right arm: hangs by his side, raised to wave a rider through while the pole is up.
        _sentryArm = new Node3D { Position = new Vector3(0, 0.72f, 0.16f) };
        guard.AddChild(_sentryArm);
        Mesh3.Box(_sentryArm, new Vector3(0.07f, 0.3f, 0.07f), new Vector3(0, -0.14f, 0), serge);
        Mesh3.Sphere(_sentryArm, 0.035f, new Vector3(0, -0.3f, 0), skin);
        SetBoom(_boomOpen);
    }

    /// <summary>Raises the barrier (0 down, 1 up) and the sentry's arm with it.</summary>
    public void SetBoom(float open)
    {
        _boomOpen = Mathf.Clamp(open, 0, 1);
        if (_boom != null && IsInstanceValid(_boom)) _boom.RotationDegrees = new Vector3(-80 * _boomOpen, 0, 0);
        if (_sentryArm != null && IsInstanceValid(_sentryArm)) _sentryArm.RotationDegrees = new Vector3(-150 * Mathf.Clamp(_boomOpen * 1.5f, 0, 1), 0, 0);
    }

    public float BoomOpen => _boomOpen;

    void SurveyLines(Zone z)
    {
        float x0 = z.X + 0.5f, x1 = z.X + z.W - 0.5f, y0 = z.Y + 0.5f, y1 = z.Y + z.H - 0.5f;
        var peg = Gfx.Oak.Lightened(0.15f);
        var paint = new Color("d9d4c4");
        void Peg(float x, float y, float h)
        {
            Mesh3.Box(_buildings, new Vector3(0.05f, h, 0.05f), new Vector3(x, h / 2, y), peg);
            Mesh3.Box(_buildings, new Vector3(0.055f, 0.05f, 0.055f), new Vector3(x, h - 0.025f, y), paint);
        }
        // Profile boards at the corners, pegs every couple of tiles along the sides.
        foreach (var (x, y) in new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }) Peg(x, y, 0.3f);
        for (float x = x0 + 2; x < x1 - 0.5f; x += 2) { Peg(x, y0, 0.2f); Peg(x, y1, 0.2f); }
        for (float y = y0 + 2; y < y1 - 0.5f; y += 2) { Peg(x0, y, 0.2f); Peg(x1, y, 0.2f); }
        const float sh = 0.17f, st = 0.012f;
        Mesh3.Box(_buildings, new Vector3(x1 - x0, st, st), new Vector3((x0 + x1) / 2, sh, y0), paint);
        Mesh3.Box(_buildings, new Vector3(x1 - x0, st, st), new Vector3((x0 + x1) / 2, sh, y1), paint);
        Mesh3.Box(_buildings, new Vector3(st, st, y1 - y0), new Vector3(x0, sh, (y0 + y1) / 2), paint);
        Mesh3.Box(_buildings, new Vector3(st, st, y1 - y0), new Vector3(x1, sh, (y0 + y1) / 2), paint);
        // A red-and-white ranging pole left standing in the far corner.
        for (int k = 0; k < 4; k++)
            Mesh3.Cyl(_buildings, 0.025f, 0.025f, 0.25f, new Vector3(x1 - 0.3f, 0.125f + k * 0.25f, y0 + 0.3f), k % 2 == 0 ? new Color("a8322a") : paint, 6);
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
        GateParts();
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
        Lorry(lorry);
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
        StaticWaterTank(new Vector3(30.5f, 0, 10.5f));
    }

    /// <summary>
    /// A static water supply for the fire pumps: a sectional tank of pressed-steel panels bolted together at their
    /// flanges, open at the top and brim-full, stencilled S.W.S.
    /// </summary>
    void StaticWaterTank(Vector3 at)
    {
        const float w = 1.8f, d = 1.2f, h = 0.55f, panel = 0.3f, t = 0.03f;
        var steel = Mesh3.Mat(new Color("4e5a52"));
        var flange = Mesh3.Mat(new Color("434d46"));
        foreach (var (size, off) in new[]
                 {
                     (new Vector3(w, h, t), new Vector3(0, 0, -d / 2)), (new Vector3(w, h, t), new Vector3(0, 0, d / 2)),
                     (new Vector3(t, h, d), new Vector3(-w / 2, 0, 0)), (new Vector3(t, h, d), new Vector3(w / 2, 0, 0)),
                 })
            Mesh3.Box(_buildings, size, at + off + Vector3.Up * (h / 2), steel);
        // Flanges standing proud of the outside at every panel joint, and a horizontal one at mid-height.
        for (float x = -w / 2 + panel; x < w / 2 - 0.01f; x += panel)
            foreach (float s in new[] { -1f, 1f })
                Mesh3.Box(_buildings, new Vector3(0.025f, h, 0.04f), at + new Vector3(x, h / 2, s * (d / 2 + 0.02f)), flange);
        for (float z = -d / 2 + panel; z < d / 2 - 0.01f; z += panel)
            foreach (float s in new[] { -1f, 1f })
                Mesh3.Box(_buildings, new Vector3(0.04f, h, 0.025f), at + new Vector3(s * (w / 2 + 0.02f), h / 2, z), flange);
        Mesh3.Box(_buildings, new Vector3(w + 0.06f, 0.025f, 0.04f), at + new Vector3(0, h / 2, -d / 2 - 0.02f), flange);
        Mesh3.Box(_buildings, new Vector3(w + 0.06f, 0.025f, 0.04f), at + new Vector3(0, h / 2, d / 2 + 0.02f), flange);
        // Top rim
        Mesh3.Box(_buildings, new Vector3(w + 0.08f, 0.03f, 0.06f), at + new Vector3(0, h, -d / 2), flange);
        Mesh3.Box(_buildings, new Vector3(w + 0.08f, 0.03f, 0.06f), at + new Vector3(0, h, d / 2), flange);
        Mesh3.Box(_buildings, new Vector3(0.06f, 0.03f, d), at + new Vector3(-w / 2, h, 0), flange);
        Mesh3.Box(_buildings, new Vector3(0.06f, 0.03f, d), at + new Vector3(w / 2, h, 0), flange);
        // The water: dark and still, showing the sky.
        _buildings.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(w - t, d - t) },
            Position = at + Vector3.Up * (h - 0.06f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("1c2422"), Roughness = 0.04f, MetallicSpecular = 0.6f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        var label = Mesh3.Text(_buildings, "S.W.S.", at + new Vector3(0, 0.3f, d / 2 + 0.045f), 24, Gfx.Cream, Fonts.BodyBold, billboard: false);
        label.PixelSize = 0.005f;
    }

    void Tree(int x, int y)
    {
        float ox = Gfx.Rand01(x, y, 1) * 0.4f - 0.2f, oz = Gfx.Rand01(x, y, 2) * 0.4f - 0.2f;
        float s = 0.55f + Gfx.Rand01(x, y, 5) * 0.35f;
        var root = new Node3D { Position = new Vector3(x + 0.5f + ox, 0, y + 0.5f + oz) };
        _buildings.AddChild(root);
        var barkMat = Textures.Mat(Surface.Bark, new Color("4a3a2c"), 3f);
        float R(int salt) => Gfx.Rand01(x, y, salt);
        if (R(9) < 0.3f)
        {
            // Conifer: a straight trunk under five uneven, drooping tiers
            var trunk = Mesh3.Cyl(root, 0.05f, 0.1f, 0.6f, new Vector3(0, 0.3f, 0), Colors.White, 8);
            trunk.MaterialOverride = barkMat;
            for (int k = 0; k < 5; k++)
            {
                float r = s * (1.0f - k * 0.17f) * (0.9f + R(20 + k) * 0.2f);
                var tier = Mesh3.Cyl(root, r * 0.15f, r, 0.55f, new Vector3((R(30 + k) - 0.5f) * 0.06f, 0.6f + k * 0.38f, (R(40 + k) - 0.5f) * 0.06f), Colors.White, 10);
                tier.MaterialOverride = Textures.Mat(Surface.Foliage, Gfx.TreeDark.Darkened(0.12f - k * 0.03f), 2f);
                tier.RotationDegrees = new Vector3((R(50 + k) - 0.5f) * 8, R(60 + k) * 360, (R(70 + k) - 0.5f) * 8);
            }
            return;
        }
        // Broadleaf: a trunk that forks, and a canopy of overlapping leafy clumps
        var t = Mesh3.Cyl(root, 0.07f, 0.12f, 0.8f, new Vector3(0, 0.4f, 0), Colors.White, 8);
        t.MaterialOverride = barkMat;
        for (int b = 0; b < 3; b++)
        {
            float ang = (b * 120 + R(80 + b) * 40) * Mathf.Pi / 180;
            var limb = Mesh3.Cyl(root, 0.025f, 0.05f, 0.6f * s + 0.2f, new Vector3(Mathf.Sin(ang) * 0.12f, 0.95f, Mathf.Cos(ang) * 0.12f), Colors.White, 6);
            limb.MaterialOverride = barkMat;
            limb.Rotation = new Vector3(Mathf.Cos(ang) * 0.6f, 0, -Mathf.Sin(ang) * 0.6f);
        }
        var greens = new[] { Gfx.TreeDark, Gfx.TreeMid, Gfx.TreeLight };
        int clumps = 9 + (int)(R(90) * 5);
        float crown = 0.95f + s * 1.0f;
        for (int k = 0; k < clumps; k++)
        {
            // Spread over an ellipsoid crown; the lower, outer clumps darker (in shade), the top ones lighter
            float a = R(100 + k) * Mathf.Tau, up = R(120 + k);
            float rr = s * (0.35f + 0.55f * R(140 + k)) * (1 - up * 0.45f);
            var at = new Vector3(Mathf.Cos(a) * rr, crown + (up - 0.35f) * s * 1.1f, Mathf.Sin(a) * rr);
            float size = s * (0.38f + R(160 + k) * 0.25f);
            var col = greens[up > 0.66f ? 2 : up > 0.3f ? 1 : 0].Lerp(new Color("6b6a3a"), R(180 + k) * 0.15f);
            var clump = Mesh3.Sphere(root, size, at, col);
            clump.MaterialOverride = Textures.Mat(Surface.Foliage, col, 2.5f);
            clump.Scale = new Vector3(1.1f, 0.85f + R(200 + k) * 0.2f, 1.05f);
        }
    }
}
