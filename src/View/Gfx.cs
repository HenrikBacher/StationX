using System;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>Palette and small drawing helpers. Everything in the park is drawn procedurally.</summary>
public static class Gfx
{
    public const float T = 32f; // pixels per tile

    // Grounds
    public static readonly Color Lawn = new("5e6f40");
    public static readonly Color LawnStripe = new("637545");
    public static readonly Color Gravel = new("8f8268");
    public static readonly Color GravelDot = new("6f6450");
    public static readonly Color Water = new("3c535a");
    public static readonly Color WaterLight = new("56707a");
    public static readonly Color Shore = new("5f5c45");
    public static readonly Color TreeDark = new("2c3d26");
    public static readonly Color TreeMid = new("374a2e");
    public static readonly Color TreeLight = new("455a37");
    public static readonly Color Shadow = new(0.08f, 0.07f, 0.04f, 0.28f);
    public static readonly Color TableWood = new("4a3322");
    public static readonly Color TableWoodLight = new("5d4330");

    // Buildings
    public static readonly Color HutFloor = new("8a7050");
    public static readonly Color HutFloorLine = new("6f5940");
    public static readonly Color HutWall = new("5c5641");
    public static readonly Color HutWallDark = new("3c382b");
    public static readonly Color MansionFloor = new("74503a");
    public static readonly Color MansionFloorAlt = new("664430");
    public static readonly Color Brick = new("8b4a36");
    public static readonly Color BrickDark = new("6a3426");
    public static readonly Color Window = new("7d8f92");
    public static readonly Color Verdigris = new("6f9a88");
    public static readonly Color StoneDressing = new("b3a888");
    public static readonly Color Creosote = new("3f3a2c");
    public static readonly Color Mud = new("5a4a36");
    public static readonly Color Sandbag = new("9b8a64");
    public static readonly Color KhakiGreen = new("4f5532");
    public static readonly Color Slab = new("858174");
    public static readonly Color SlabDark = new("6b675c");

    // Furniture
    public static readonly Color Oak = new("6e4a2b");
    public static readonly Color OakLight = new("86603a");
    public static readonly Color OakDark = new("4f341e");
    public static readonly Color Paper = new("f2ead8");
    public static readonly Color PaperShade = new("d8ccb2");
    public static readonly Color PaperWaiting = new("e9b9a4");
    public static readonly Color Ink = new("2b2620");
    public static readonly Color Steel = new("4b4f52");
    public static readonly Color SteelLight = new("6d7275");
    public static readonly Color Black = new("1f1f1f");
    public static readonly Color Brass = new("c9a14a");
    public static readonly Color PostRed = new("a8322a");
    public static readonly Color Cream = new("e6dcc0");
    public static readonly Color LampGlow = new(1f, 0.86f, 0.55f, 0.85f);

    // UI
    public static readonly Color Good = new("6f9a52");
    public static readonly Color Bad = new("c0504a");
    public static readonly Color Warn = new("d39b3a");

    public static Color RoleColor(Role r) => r switch
    {
        Role.Clerk => new Color("6b6a70"),
        Role.Cryptanalyst => new Color("8a6a43"),
        Role.Wren => new Color("23314f"),
        Role.Linguist => new Color("4f6b4a"),
        Role.Messenger => new Color("6e2f2a"),
        _ => Colors.White,
    };

    static readonly Color[] Skins = { new("f1c9a5"), new("e3b48f"), new("d7a27c"), new("c48c66"), new("f5d6bd") };
    static readonly Color[] Hairs = { new("3b2a1e"), new("5a3d25"), new("8a6236"), new("c9a46a"), new("2a2420"), new("7a7066") };
    public static Color Skin(int look) => Skins[look % Skins.Length];
    public static Color Hair(int look) => Hairs[(look / 7) % Hairs.Length];

    public static Color NetColor(Net n) => n switch
    {
        Net.Police => new Color("8c8c8c"),
        Net.Abwehr => new Color("6b5b8c"),
        Net.Red => new Color("c0504a"),
        Net.LightBlue => new Color("6fa3c9"),
        Net.Chaffinch => new Color("c9a14a"),
        Net.Brown => new Color("9a6a44"),
        Net.Dolphin => new Color("4f8c84"),
        Net.Shark => new Color("3d5a80"),
        _ => Colors.White,
    };

    public static uint Hash(int x, int y, int salt = 0)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }
    }

    public static float Rand01(int x, int y, int salt = 0) => (Hash(x, y, salt) & 0xFFFF) / 65535f;

    public static Vector2[] EllipsePoints(Vector2 c, float rx, float ry, float rot = 0, int n = 18)
    {
        var pts = new Vector2[n];
        float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Tau * i / n;
            float x = Mathf.Cos(a) * rx, y = Mathf.Sin(a) * ry;
            pts[i] = c + new Vector2(x * cs - y * sn, x * sn + y * cs);
        }
        return pts;
    }

    public static void Ellipse(CanvasItem ci, Vector2 c, float rx, float ry, Color col, float rot = 0) =>
        ci.DrawColoredPolygon(EllipsePoints(c, rx, ry, rot), col);

    public static void Box(CanvasItem ci, Rect2 r, Color fill, Color? edge = null, float edgeW = 1.5f)
    {
        ci.DrawRect(r, fill);
        if (edge is Color e) ci.DrawRect(r, e, false, edgeW);
    }

    /// <summary>A raised object: drop shadow, then body with a lighter top edge.</summary>
    public static void Raised(CanvasItem ci, Rect2 r, Color body, Color? rim = null)
    {
        ci.DrawRect(new Rect2(r.Position + new Vector2(2.5f, 3.5f), r.Size), Shadow);
        ci.DrawRect(r, body);
        ci.DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, 2.5f)), (rim ?? body.Lightened(0.18f)));
        ci.DrawRect(r, body.Darkened(0.35f), false, 1.2f);
    }

    public static Rect2 Centered(float w, float h, float ox = 0, float oy = 0) =>
        new(new Vector2(-w / 2 + ox, -h / 2 + oy), new Vector2(w, h));
}
