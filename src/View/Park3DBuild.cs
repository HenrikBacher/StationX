using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>
/// Building work in the Park (Construction.cs): a new hut's timber frame (or a block's brick courses) rising on its slab
/// as the builders get on, with a stack of materials; a hut being rebuilt in brick stands inside scaffolding. Part of the
/// static Park, rebuilt when the work moves on a step.
/// </summary>
public partial class Park3D
{
    /// <summary>Steps of progress the view shows (the Park rebuilds at each).</summary>
    public const int BuildSteps = 5;

    public static int BuildStep(GameState g, Zone z) => g.Building(z) ? 1 + (int)(g.BuildProgress(z) * (BuildSteps - 1) + 0.0001) : 0;

    /// <summary>A new building going up: frame or brick courses to a height by progress.</summary>
    void GoingUp(Zone z, float progress)
    {
        float x0 = z.X + 0.5f, x1 = z.X + z.W - 0.5f, y0 = z.Y + 0.5f, y1 = z.Y + z.H - 0.5f;
        float h = Mathf.Lerp(0.25f, BackWall, progress);
        if (z.Brick)
        {
            // Brick courses round the slab, rising.
            var brick = Textures.Mat(Surface.Brick, new Color("c8b8a8"));
            Mesh3.Box(_buildings, new Vector3(x1 - x0, h, WallThick), new Vector3((x0 + x1) / 2, h / 2, y0), brick);
            Mesh3.Box(_buildings, new Vector3(x1 - x0, h, WallThick), new Vector3((x0 + x1) / 2, h / 2, y1), brick);
            Mesh3.Box(_buildings, new Vector3(WallThick, h, y1 - y0), new Vector3(x0, h / 2, (y0 + y1) / 2), brick);
            Mesh3.Box(_buildings, new Vector3(WallThick, h, y1 - y0), new Vector3(x1, h / 2, (y0 + y1) / 2), brick);
            Scaffold(x0, y0, x1, y1, h + 0.3f);
        }
        else
        {
            // Timber studs at every tile with a sole plate, a head plate once they reach full height, and boarding
            // climbing the far walls.
            var stud = Gfx.Oak.Lightened(0.2f);
            float studH = Mathf.Min(BackWall, h * 1.4f);
            void Run(float ax, float ay, float bx, float by)
            {
                float len = Mathf.Max(Mathf.Abs(bx - ax), Mathf.Abs(by - ay));
                bool alongX = Mathf.Abs(bx - ax) > Mathf.Abs(by - ay);
                Mesh3.Box(_buildings, alongX ? new Vector3(len, 0.06f, 0.1f) : new Vector3(0.1f, 0.06f, len), new Vector3((ax + bx) / 2, 0.03f, (ay + by) / 2), stud);
                for (float t = 0; t <= len + 0.01f; t += 1f)
                {
                    float px = alongX ? Mathf.Min(ax, bx) + t : ax, py = alongX ? ay : Mathf.Min(ay, by) + t;
                    Mesh3.Box(_buildings, new Vector3(0.08f, studH, 0.08f), new Vector3(px, studH / 2, py), stud);
                }
                if (progress > 0.7f)
                    Mesh3.Box(_buildings, alongX ? new Vector3(len, 0.06f, 0.1f) : new Vector3(0.1f, 0.06f, len), new Vector3((ax + bx) / 2, studH, (ay + by) / 2), stud);
            }
            Run(x0, y0, x1, y0);
            Run(x1, y0, x1, y1);
            Run(x0, y1, x1, y1);
            Run(x0, y0, x0, y1);
            if (progress > 0.4f)
            {
                var boards = Textures.Mat(Surface.Weatherboard, Colors.White);
                float bh = studH * (progress - 0.4f) / 0.6f;
                Mesh3.Box(_buildings, new Vector3(x1 - x0, bh, 0.05f), new Vector3((x0 + x1) / 2, bh / 2, y0 - 0.06f), boards);
                Mesh3.Box(_buildings, new Vector3(0.05f, bh, y1 - y0), new Vector3(x1 + 0.06f, bh / 2, (y0 + y1) / 2), boards);
            }
        }
        // Materials stacked by the plot: boards or bricks, and a barrow.
        float sx = x0 + 0.3f, sy = y1 + 0.75f;
        if (z.Brick)
            for (int k = 0; k < 3; k++) Mesh3.BoxAt(_buildings, sx + k * 0.55f, 0, sy, 0.45f, 0.35f, 0.4f, Gfx.Brick.Darkened(0.05f * k));
        else
            for (int k = 0; k < 5; k++) Mesh3.BoxAt(_buildings, sx, k * 0.06f, sy + (k % 2) * 0.04f, 2.2f, 0.06f, 0.3f, Gfx.Oak.Lightened(0.15f));
        Mesh3.BoxAt(_buildings, sx + 2.6f, 0.12f, sy, 0.4f, 0.14f, 0.3f, new Color("5b5e5c"));
        Mesh3.Cyl(_buildings, 0.09f, 0.09f, 0.04f, new Vector3(sx + 3.05f, 0.09f, sy + 0.15f), Gfx.OakDark, 10);
    }

    /// <summary>Tube-and-fitting scaffolding round a building: standards at the corners and every few tiles, ledgers at two lifts.</summary>
    void Scaffold(float x0, float y0, float x1, float y1, float top)
    {
        var steel = new Color("8d918f");
        float o = 0.4f;
        float ax = x0 - o, bx = x1 + o, ay = y0 - o, by = y1 + o;
        var board = Gfx.Oak.Lightened(0.1f);
        void Side(float sx, float sy, float ex, float ey)
        {
            bool alongX = Mathf.Abs(ex - sx) > Mathf.Abs(ey - sy);
            float len = alongX ? Mathf.Abs(ex - sx) : Mathf.Abs(ey - sy);
            for (float t = 0; t <= len + 0.01f; t += 2f)
                Mesh3.Box(_buildings, new Vector3(0.05f, top, 0.05f), new Vector3(alongX ? Mathf.Min(sx, ex) + t : sx, top / 2, alongX ? sy : Mathf.Min(sy, ey) + t), steel);
            var mid = new Vector3((sx + ex) / 2, 0, (sy + ey) / 2);
            foreach (float lift in new[] { top * 0.45f, top * 0.95f })
            {
                Mesh3.Box(_buildings, alongX ? new Vector3(len, 0.04f, 0.04f) : new Vector3(0.04f, 0.04f, len), mid + new Vector3(0, lift, 0), steel);
                Mesh3.Box(_buildings, alongX ? new Vector3(len, 0.03f, 0.28f) : new Vector3(0.28f, 0.03f, len), mid + new Vector3(0, lift + 0.03f, 0), board);
            }
        }
        Side(ax, ay, bx, ay);
        Side(bx, ay, bx, by);
        Side(ax, by, bx, by);
        Side(ax, ay, ax, by);
    }

    /// <summary>A hut being rebuilt in brick around the people in it: scaffolding over it.</summary>
    void Rebuilding(Zone z)
    {
        float x0 = z.X + 0.5f, x1 = z.X + z.W - 0.5f, y0 = z.Y + 0.5f, y1 = z.Y + z.H - 0.5f;
        Scaffold(x0, y0, x1, y1, BackWall + 0.6f);
        float sx = x0 + 0.3f, sy = y1 + 0.75f;
        for (int k = 0; k < 3; k++) Mesh3.BoxAt(_buildings, sx + k * 0.55f, 0, sy, 0.45f, 0.35f, 0.4f, Gfx.Brick.Darkened(0.05f * k));
    }
}
