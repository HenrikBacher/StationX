using System;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>
/// A network's symbol in its colour: a shape of its own for each network, so a player who can't tell the colours apart
/// can still tell the keys and requests apart. Drawn, not a font glyph, so every font shows it.
/// </summary>
public partial class NetMark : Control
{
    public Net Net { get; set; }

    public static NetMark For(Net n, float size = 16)
    {
        var m = new NetMark { Net = n, CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = SizeFlags.ShrinkCenter,
                              MouseFilter = MouseFilterEnum.Ignore };
        return m;
    }

    public override void _Draw()
    {
        var c = Gfx.NetColor(Net);
        var ink = new Color(1, 1, 1, 0.55f);   // a faint light edge: the panels are dark, and so are some networks
        float s = Math.Min(Size.X, Size.Y), r = s / 2 - 1;
        var o = Size / 2;
        Vector2[] Poly(int n, float rot, float rad) =>
            System.Linq.Enumerable.Range(0, n).Select(i => o + Vector2.FromAngle(rot + i * Mathf.Tau / n) * rad).ToArray();
        void Fill(Vector2[] pts)
        {
            DrawColoredPolygon(pts, c);
            DrawPolyline(pts.Append(pts[0]).ToArray(), ink, 1.2f, true);
        }
        switch (Shape(Net))
        {
            case 0: DrawCircle(o, r, c); DrawArc(o, r, 0, Mathf.Tau, 24, ink, 1.2f, true); break;
            case 1: Fill(Poly(4, Mathf.Pi / 4, r * 1.25f)); break;                              // square
            case 2: Fill(Poly(3, -Mathf.Pi / 2, r * 1.15f)); break;                             // triangle up
            case 3: Fill(Poly(4, 0, r * 1.15f)); break;                                          // diamond
            case 4: Fill(Poly(3, Mathf.Pi / 2, r * 1.15f)); break;                              // triangle down
            case 5:                                                                              // star
                var star = new Vector2[10];
                for (int i = 0; i < 10; i++) star[i] = o + Vector2.FromAngle(-Mathf.Pi / 2 + i * Mathf.Pi / 5) * (i % 2 == 0 ? r * 1.15f : r * 0.5f);
                Fill(star);
                break;
            case 6:                                                                              // plus
                float t = r * 0.42f;
                Fill(new[] { o + new Vector2(-t, -r), o + new Vector2(t, -r), o + new Vector2(t, -t), o + new Vector2(r, -t), o + new Vector2(r, t), o + new Vector2(t, t),
                             o + new Vector2(t, r), o + new Vector2(-t, r), o + new Vector2(-t, t), o + new Vector2(-r, t), o + new Vector2(-r, -t), o + new Vector2(-t, -t) });
                break;
            case 7: DrawArc(o, r * 0.75f, 0, Mathf.Tau, 24, c, r * 0.5f, true); DrawArc(o, r, 0, Mathf.Tau, 24, ink, 1.0f, true); break;   // ring
            case 8: Fill(Poly(6, 0, r * 1.1f)); break;                                          // hexagon
            case 9: Fill(new[] { o + new Vector2(-r, -r * 0.45f), o + new Vector2(r, -r * 0.45f), o + new Vector2(r, r * 0.45f), o + new Vector2(-r, r * 0.45f) }); break;   // bar
            default:                                                                             // cross
                DrawLine(o + new Vector2(-r, -r) * 0.8f, o + new Vector2(r, r) * 0.8f, ink, r * 0.6f + 1.5f, true);
                DrawLine(o + new Vector2(-r, r) * 0.8f, o + new Vector2(r, -r) * 0.8f, ink, r * 0.6f + 1.5f, true);
                DrawLine(o + new Vector2(-r, -r) * 0.8f, o + new Vector2(r, r) * 0.8f, c, r * 0.6f, true);
                DrawLine(o + new Vector2(-r, r) * 0.8f, o + new Vector2(r, -r) * 0.8f, c, r * 0.6f, true);
                break;
        }
    }

    static int Shape(Net n) => n switch
    {
        Net.Red => 0, Net.LightBlue => 1, Net.Chaffinch => 2, Net.Brown => 3, Net.Dolphin => 4, Net.Shark => 5,
        Net.Police => 6, Net.Abwehr => 10, Net.Jn25 => 7, Net.Werft => 9, _ => 8,
    };
}
