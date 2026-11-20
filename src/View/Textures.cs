using System;
using System.Collections.Generic;
using Godot;

namespace StationX.View;

public enum Surface { Brick, Weatherboard, Concrete, Felt, Stone, Lino, Hessian, Matchboard, Plaster, Parquet, Floorboards, QuarryTile, Grit, Foliage, Bark }

/// <summary>
/// Procedural, slightly grimy textures for the buildings. Each is mapped in world space (triplanar),
/// one texture repeat per tile, so walls of any size line up.
/// </summary>
public static class Textures
{
    const int N = 128;
    static readonly Dictionary<Surface, ImageTexture> Cache = new();
    static readonly Dictionary<(Surface, Color, float), StandardMaterial3D> Mats = new();

    /// <summary>
    /// Photographed CC0 materials (Poly Haven, see CREDITS.md): folder under res://assets/textures, how many tiles one
    /// texture repeat spans (so bricks and boards come out at a believable size next to the people), and a tone the photo
    /// is multiplied by.
    /// </summary>
    static readonly Dictionary<Surface, (string dir, float tilesPerRepeat, Color tone)> Pbr = new()
    {
        [Surface.Brick] = ("red_bricks_04", 1.6f, Colors.White),
        [Surface.Weatherboard] = ("weathered_brown_planks", 1.4f, new Color("b8aa98")),
        [Surface.Floorboards] = ("old_planks_02", 2.0f, Colors.White),
        [Surface.Parquet] = ("herringbone_parquet", 2.8f, new Color("b5a089")),
        [Surface.Plaster] = ("white_plaster_02", 2.5f, Colors.White),
        [Surface.Concrete] = ("concrete_floor_02", 3.0f, Colors.White),
        [Surface.QuarryTile] = ("terracotta_floor_tiles", 1.5f, new Color("c8b0a0")),
    };

    /// <summary>Set false to fall back to the procedural textures everywhere (for comparison shots).</summary>
    public static bool UsePhotoMaterials = true;

    static Texture2D? Load(string dir, string map)
    {
        string path = $"res://assets/textures/{dir}/{map}.jpg";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    public static StandardMaterial3D Mat(Surface s, Color tint, float repeatsPerTile = 1)
    {
        var key = (s, tint, repeatsPerTile);
        if (Mats.TryGetValue(key, out var m)) return m;
        if (UsePhotoMaterials && Pbr.TryGetValue(s, out var pbr) && Load(pbr.dir, "albedo") is { } albedo)
        {
            m = new StandardMaterial3D
            {
                AlbedoTexture = albedo,
                AlbedoColor = tint * pbr.tone,   // the photo's own colour, toned to the period (waxed oak, creosote)
                NormalEnabled = true,
                NormalTexture = Load(pbr.dir, "normal"),
                NormalScale = 1.0f,
                RoughnessTexture = Load(pbr.dir, "rough"),
                RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red,
                Roughness = 1f,
                Uv1Triplanar = true,
                Uv1WorldTriplanar = true,
                Uv1TriplanarSharpness = 4f,
                Uv1Scale = Vector3.One / pbr.tilesPerRepeat,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            };
            Mats[key] = m;
            return m;
        }
        m = new StandardMaterial3D
        {
            AlbedoTexture = Get(s),
            AlbedoColor = tint,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = Vector3.One * repeatsPerTile,
            Roughness = s == Surface.Lino ? 0.6f : 0.95f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        Mats[key] = m;
        return m;
    }

    /// <summary>
    /// A photographed material on a mesh's own UVs (one unit = one tile), as its own instance that can fade: the roofs.
    /// Null if the photos are missing.
    /// </summary>
    public static StandardMaterial3D? Fading(string dir, Color tone, float tilesPerRepeat)
    {
        if (!UsePhotoMaterials || Load(dir, "albedo") is not { } albedo) return null;
        return new StandardMaterial3D
        {
            AlbedoTexture = albedo,
            AlbedoColor = tone,
            NormalEnabled = true,
            NormalTexture = Load(dir, "normal"),
            RoughnessTexture = Load(dir, "rough"),
            RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red,
            Roughness = 1f,
            Uv1Scale = Vector3.One / tilesPerRepeat,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    }

    static readonly Dictionary<string, Vector3> Means = new();

    /// <summary>A photo's average colour in linear light (what shaders sample), from a sparse grid of its pixels.</summary>
    public static Vector3 LinearMean(string key, Texture2D t)
    {
        if (Means.TryGetValue(key, out var m)) return m;
        var img = t.GetImage();
        if (img.IsCompressed()) img.Decompress();
        var sum = Vector3.Zero;
        int n = 0;
        for (int y = 0; y < img.GetHeight(); y += 7)
            for (int x = 0; x < img.GetWidth(); x += 7)
            {
                var c = img.GetPixel(x, y).SrgbToLinear();
                sum += new Vector3(c.R, c.G, c.B);
                n++;
            }
        return Means[key] = sum / Math.Max(1, n);
    }

    /// <summary>The furniture's wood colours: desks, tables, chairs and shelves get real grain in those shades.</summary>
    static readonly HashSet<Color> WoodColors = new() { Gfx.TableWood, Gfx.TableWoodLight, Gfx.Oak, Gfx.OakLight, Gfx.OakDark };
    static readonly Dictionary<Color, StandardMaterial3D> WoodMats = new();

    /// <summary>
    /// Photographed worn tabletop wood (Poly Haven, CC0) in the given shade, or null if the colour isn't one of the
    /// furniture woods. The photo is divided by its own average colour, so the shade stays and only the grain is added.
    /// </summary>
    public static StandardMaterial3D? Wood(Color c)
    {
        if (!UsePhotoMaterials || !WoodColors.Contains(c)) return null;
        if (WoodMats.TryGetValue(c, out var m)) return m;
        if (Load("wood_table_worn", "albedo") is not { } albedo) return null;
        var mean = LinearMean("wood_table_worn", albedo);
        var lin = c.SrgbToLinear();
        var tone = new Color(lin.R / Math.Max(0.01f, mean.X), lin.G / Math.Max(0.01f, mean.Y), lin.B / Math.Max(0.01f, mean.Z)).LinearToSrgb();
        m = new StandardMaterial3D
        {
            AlbedoTexture = albedo,
            AlbedoColor = tone,
            NormalEnabled = true,
            NormalTexture = Load("wood_table_worn", "normal"),
            NormalScale = 0.6f,
            RoughnessTexture = Load("wood_table_worn", "rough"),
            RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red,
            Roughness = 0.9f,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1TriplanarSharpness = 4f,
            Uv1Scale = Vector3.One / 1.2f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        WoodMats[c] = m;
        return m;
    }

    static ImageTexture? _mossGraded, _mossEven;

    /// <summary>Periodic value noise on a square texture of side `size`, `cells` cells across.</summary>
    static float Periodic(int x, int y, int size, int cells, int salt)
    {
        float gx = x * cells / (float)size, gy = y * cells / (float)size;
        int ix = (int)gx, iy = (int)gy;
        float tx = gx - ix, ty = gy - iy;
        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        float a = Gfx.Rand01(ix % cells, iy % cells, salt), b = Gfx.Rand01((ix + 1) % cells, iy % cells, salt);
        float c = Gfx.Rand01(ix % cells, (iy + 1) % cells, salt), d = Gfx.Rand01((ix + 1) % cells, (iy + 1) % cells, salt);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    /// <summary>
    /// Moss and lichen for the roofs' detail layer, drawn by hand rather than downloaded: cushions of dark olive moss
    /// with lighter tips, and pale grey-green crusts of lichen dotted between, its alpha saying how much shows. Graded,
    /// each row down the texture has less (the roof's UV2 picks a row by how shaded and how low on the slope a point
    /// is: more at the eaves and on the north side); even, the same thin scatter everywhere (flat roofs).
    /// </summary>
    public static ImageTexture Moss(bool graded)
    {
        if ((graded ? _mossGraded : _mossEven) is { } done) return done;
        const int S = 256;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        var moss = new Color("424826"); var tip = new Color("59602f"); var dark = new Color("2e331f");
        var lichen = new Color("8e8f76");
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float amount = graded ? 1 - y / (S - 1f) : 0.2f;
                // Where moss takes: clumps (low frequency) broken up by smaller cushions.
                float n = Periodic(x, y, S, 6, 201) * 0.55f + Periodic(x, y, S, 16, 202) * 0.3f + Periodic(x, y, S, 48, 203) * 0.15f;
                float a = Mathf.SmoothStep(n - 0.06f, n + 0.06f, amount * 0.9f + 0.05f) * 0.62f;
                float fine = Gfx.Rand01(x, y, 204);
                var c = moss.Lerp(tip, Periodic(x, y, S, 64, 205)).Lerp(dark, fine > 0.8f ? 0.5f : 0);
                // Lichen: small round crusts where the moss hasn't taken, thinning out where there's little of anything.
                float spot = Periodic(x, y, S, 85, 206);
                if (a < 0.2f && spot > 0.84f - 0.05f * amount && amount > 0.06f)
                {
                    float l = Mathf.Clamp((spot - 0.84f) * 10, 0, 1) * 0.4f;
                    c = c.Lerp(lichen, l / Mathf.Max(0.01f, l + a));
                    a = Mathf.Max(a, l);
                }
                c.A = a;
                img.SetPixel(x, y, c);
            }
        img.GenerateMipmaps();
        var t = ImageTexture.CreateFromImage(img);
        if (graded) _mossGraded = t; else _mossEven = t;
        return t;
    }

    /// <summary>
    /// Lays moss over a roof material as its detail layer: graded on the pitched roofs' own UV2 (see Mesh3.Roof),
    /// an even scatter mapped from above on flat ones. No extra draws, one more texture read.
    /// </summary>
    public static void Mossy(StandardMaterial3D m, bool graded)
    {
        m.DetailEnabled = true;
        m.DetailAlbedo = Moss(graded);
        m.DetailBlendMode = BaseMaterial3D.BlendModeEnum.Mix;
        m.DetailUVLayer = BaseMaterial3D.DetailUV.UV2;
        if (!graded)
        {
            m.UV2Triplanar = true;
            m.UV2WorldTriplanar = true;
            m.UV2Scale = Vector3.One / 3.5f;
        }
    }

    /// <summary>A fading copy of a wall material, for the gable ends under a fading roof.</summary>
    public static StandardMaterial3D FadingCopy(StandardMaterial3D m) => (StandardMaterial3D)m.Duplicate();

    public static ImageTexture Get(Surface s)
    {
        if (Cache.TryGetValue(s, out var t)) return t;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgb8);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                img.SetPixel(x, y, Pixel(s, x, y));
        img.GenerateMipmaps();
        Cache[s] = t = ImageTexture.CreateFromImage(img);
        return t;
    }

    static float Noise(int x, int y, int salt) => (Gfx.Hash(x, y, salt) & 0xFFFF) / 65535f;

    /// <summary>Cheap value noise, tiling at the texture size.</summary>
    static float Smooth(int x, int y, int cell, int salt)
    {
        int cx = x / cell, cy = y / cell, n = N / cell;
        float fx = (x % cell) / (float)cell, fy = (y % cell) / (float)cell;
        float a = Noise(cx % n, cy % n, salt), b = Noise((cx + 1) % n, cy % n, salt);
        float c = Noise(cx % n, (cy + 1) % n, salt), d = Noise((cx + 1) % n, (cy + 1) % n, salt);
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static Color Grime(Color c, int x, int y, float amount)
    {
        float g = Smooth(x, y, 32, 71) * 0.6f + Smooth(x, y, 8, 72) * 0.4f;
        float speck = Noise(x, y, 73) > 0.97f ? 0.12f : 0;
        return c.Darkened(amount * g + speck);
    }

    static Color Pixel(Surface s, int x, int y)
    {
        switch (s)
        {
            case Surface.Brick:
            {
                // Running bond, 8 courses per tile, soot-stained.
                int course = y / 16, yy = y % 16;
                int off = course % 2 == 0 ? 0 : 16;
                int bx = (x + off) / 32, xx = (x + off) % 32;
                bool mortar = yy < 2 || xx < 2;
                if (mortar) return Grime(new Color("8a8275"), x, y, 0.35f);
                float v = Noise(bx, course, 11);
                var c = new Color("8a4532").Lerp(new Color("6b3325"), v).Lerp(new Color("9a5a3c"), Noise(bx, course, 12) * 0.4f);
                c = c.Lightened((Noise(x, y, 13) - 0.5f) * 0.08f);
                return Grime(c, x, y, 0.45f);
            }
            case Surface.Weatherboard:
            {
                // Overlapping horizontal boards, creosoted, with weathering streaks running down.
                int yy = y % 16;
                var c = new Color("5c5641");
                if (yy < 2) c = c.Darkened(0.45f);          // shadow under the board above
                else if (yy < 4) c = c.Lightened(0.12f);    // the board's top edge catches light
                float grain = Smooth(x * 4 % N, y, 4, 21);
                c = c.Lightened((grain - 0.5f) * 0.1f);
                float streak = Smooth(x, 0, 6, 22);
                c = c.Darkened(streak * 0.25f * (y / (float)N));
                if (x % 64 == 63 && yy > 2) c = c.Darkened(0.35f);  // board joints
                return Grime(c, x, y, 0.3f);
            }
            case Surface.Concrete:
            {
                var c = new Color("8e8a80").Darkened(Smooth(x, y, 16, 31) * 0.18f).Lightened((Noise(x, y, 32) - 0.5f) * 0.08f);
                // A hairline crack
                int cy = 40 + (int)(Smooth(x, 0, 16, 33) * 30);
                if (Math.Abs(y - cy) < 1 && x > 20 && x < 100) c = c.Darkened(0.4f);
                return Grime(c, x, y, 0.25f);
            }
            case Surface.Felt:
            {
                var c = new Color("2e2c2a").Lightened(Noise(x, y, 41) * 0.08f);
                if (y % 32 < 2) c = c.Darkened(0.3f);
                return c;
            }
            case Surface.Stone:
            {
                int course = y / 32, off = course % 2 == 0 ? 0 : 32;
                bool joint = y % 32 < 1 || (x + off) % 64 < 1;
                var c = new Color("b7ab8e").Darkened(Smooth(x, y, 16, 51) * 0.2f);
                if (joint) c = c.Darkened(0.25f);
                return Grime(c, x, y, 0.4f);
            }
            case Surface.Lino:
            {
                // Worn brown linoleum with scuffs
                var c = new Color("6e5a42").Lightened((Smooth(x, y, 8, 61) - 0.5f) * 0.12f);
                if (Noise(x, y, 62) > 0.985f) c = c.Lightened(0.15f);
                return c;
            }
            case Surface.Matchboard:
            {
                // Painted tongue-and-groove boards, about 12 cm wide: neutral, so the tint gives the paint colour.
                int xx = x % 16;
                var c = new Color("ece6d8").Darkened(Noise(x / 16, 0, 81) * 0.05f);
                if (xx == 0) c = c.Darkened(0.26f);              // the groove
                else if (xx == 1) c = c.Darkened(0.08f);
                else if (xx == 15) c = c.Lightened(0.06f);       // bead catching light
                c = c.Darkened(Smooth(x * 3 % N, y, 8, 82) * 0.04f);
                return Grime(c, x, y, 0.12f);
            }
            case Surface.Foliage:
            {
                // Neutral leaf mottle (tinted green by the material): clusters of lit and shaded leaves.
                float v = 0.78f + Smooth(x, y, 8, 141) * 0.25f + Smooth(x, y, 4, 142) * 0.12f;
                if (Noise(x / 3, y / 3, 143) > 0.82f) v += 0.12f;      // leaves catching light
                if (Noise(x / 2, y / 2, 144) > 0.86f) v -= 0.22f;      // gaps between leaves
                v = Mathf.Clamp(v, 0, 1.0f);
                return new Color(v, v, v);
            }
            case Surface.Bark:
            {
                // Vertical fissured bark, neutral
                float v = 0.75f + Smooth((x * 5) % N, y / 4, 4, 151) * 0.2f;
                if ((x + (int)(Smooth(0, y, 16, 152) * 6)) % 9 == 0) v -= 0.25f;
                return new Color(v, v, v);
            }
            case Surface.Grit:
            {
                // Neutral detail multiplied over the painted ground: blades, grit and specks so it isn't flat paint.
                float v = 0.9f + (Noise(x, y, 131) - 0.5f) * 0.12f + (Smooth(x, y, 8, 132) - 0.5f) * 0.1f;
                // Short slanted strokes, like blades of grass or raked gravel
                int sx = (x + y / 3) % 7, sy = y % 9;
                if (sx == 0 && sy < 6 && Noise(x / 7, y / 9, 133) > 0.45f) v -= 0.1f;
                if (Noise(x, y, 134) > 0.985f) v += 0.12f;      // a pale stone
                if (Noise(x, y, 135) > 0.99f) v -= 0.15f;       // a dark speck
                v = Mathf.Clamp(v, 0, 1);
                return new Color(v, v, v);
            }
            case Surface.Parquet:
            {
                // Oak blocks laid in basket weave: 32 px squares of four blocks, alternating direction.
                int bx = x / 32, by = y / 32, lx = x % 32, ly = y % 32;
                bool across = (bx + by) % 2 == 0;
                int plank = across ? ly / 8 : lx / 8, along = across ? lx : ly, side = across ? ly % 8 : lx % 8;
                float tone = Noise(bx * 4 + plank, by, 101);
                var c = new Color("7a5235").Lerp(new Color("946642"), tone).Darkened(Noise(bx, by * 4 + plank, 102) * 0.08f);
                float grain = Smooth(across ? (along * 6) % N : x, across ? y : (along * 6) % N, 4, 103);
                c = c.Lightened((grain - 0.5f) * 0.1f);
                if (side == 0) c = c.Darkened(0.3f);                       // seams between blocks
                if (lx == 0 || ly == 0) c = c.Darkened(0.35f);
                // Polish worn dull in places
                c = c.Lerp(new Color("6a5440"), Smooth(x, y, 32, 104) * 0.25f);
                return c;
            }
            case Surface.Floorboards:
            {
                // Deal boards about 12 cm wide running one way, butt joints staggered, scuffed along the walkways.
                int board = y / 16, yy = y % 16;
                int joint = (int)(Noise(board, 0, 111) * N);
                var c = new Color("8e7252").Lerp(new Color("7a6044"), Noise(board, (x + joint) / N, 112));
                c = c.Lightened((Smooth((x * 3) % N, y, 4, 113) - 0.5f) * 0.12f);
                if (yy == 0) c = c.Darkened(0.4f);
                else if (yy == 1) c = c.Darkened(0.12f);
                if ((x + joint) % N == 0) c = c.Darkened(0.4f);
                if ((x + joint) % N is 6 or 122 && yy == 8) c = new Color("2e2a26");   // nail heads
                return Grime(c, x, y, 0.18f);
            }
            case Surface.QuarryTile:
            {
                // Red quarry tiles, 25 cm, in grey grout.
                int lx = x % 32, ly = y % 32;
                if (lx < 2 || ly < 2) return Grime(new Color("7d776c"), x, y, 0.3f);
                var c = new Color("74463a").Lerp(new Color("5f3a30"), Noise(x / 32, y / 32, 121)).Lerp(new Color("5a4a40"), 0.2f);
                c = c.Lightened((Noise(x, y, 122) - 0.5f) * 0.06f);
                return Grime(c, x, y, 0.3f);
            }
            case Surface.Plaster:
            {
                // Distempered plaster: soft mottling and the odd scuff.
                var c = new Color("f2eee4").Darkened(Smooth(x, y, 32, 91) * 0.07f + Smooth(x, y, 8, 92) * 0.03f);
                if (Noise(x, y, 93) > 0.993f) c = c.Darkened(0.1f);
                return c;
            }
            default:
            {
                var c = new Color("9c8a63");
                if ((x + y) % 4 == 0) c = c.Darkened(0.12f);
                return Grime(c, x, y, 0.3f);
            }
        }
    }
}
