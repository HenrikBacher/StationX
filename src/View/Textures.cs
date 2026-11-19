using System;
using System.Collections.Generic;
using Godot;

namespace StationX.View;

public enum Surface { Brick, Weatherboard, Concrete, Felt, Stone, Lino, Hessian, Matchboard, Plaster, Parquet, Floorboards, QuarryTile, Grit }

/// <summary>
/// Procedural, slightly grimy textures for the buildings. Each is mapped in world space (triplanar),
/// one texture repeat per tile, so walls of any size line up.
/// </summary>
public static class Textures
{
    const int N = 128;
    static readonly Dictionary<Surface, ImageTexture> Cache = new();
    static readonly Dictionary<(Surface, Color, float), StandardMaterial3D> Mats = new();

    public static StandardMaterial3D Mat(Surface s, Color tint, float repeatsPerTile = 1)
    {
        var key = (s, tint, repeatsPerTile);
        if (Mats.TryGetValue(key, out var m)) return m;
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
