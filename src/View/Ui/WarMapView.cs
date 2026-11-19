using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>
/// A staff map of the war in the west, drawn on paper: coastlines, a ten-degree graticule, and the Park's operations as
/// pins (amber in hand, green won, red lost). Hover a pin to read it.
/// </summary>
public partial class WarMapView : Control
{
    public GameState? State;
    const double Lon0 = -80, Lon1 = 42, Lat0 = 24, Lat1 = 73;
    static readonly double Squash = Math.Cos(50 * Math.PI / 180);
    Operation? _hover;

    static readonly Color Sea = new("c9c3a6"), Land = new("e8dcc0"), Coast = new("6d5f48"), Grid = new(0.42f, 0.36f, 0.27f, 0.22f),
        InkCol = new("3a3226"), Pending = new("c8902a"), Held = new("8a8270"), Won = new("4f7a3a"), Lost = new("a3392b");

    public Vector2 Project(double lat, double lon)
    {
        double w = (Lon1 - Lon0) * Squash, h = Lat1 - Lat0;
        double k = Math.Min(Size.X / w, Size.Y / h);
        double ox = (Size.X - w * k) / 2, oy = (Size.Y - h * k) / 2;
        return new Vector2((float)(ox + (lon - Lon0) * Squash * k), (float)(oy + (Lat1 - lat) * k));
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseMotion mm || State == null) return;
        Operation? best = null;
        float bd = 14;
        foreach (var o in State.Operations)
        {
            float d = Project(o.Lat, o.Lon).DistanceTo(mm.Position);
            if (d < bd) { bd = d; best = o; }
        }
        if (best != _hover) { _hover = best; QueueRedraw(); }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Sea);
        for (int lon = -80; lon <= 40; lon += 10)
            DrawLine(Project(Lat0, lon), Project(Lat1, lon), Grid, 1);
        for (int lat = 30; lat <= 70; lat += 10)
            DrawLine(Project(lat, Lon0), Project(lat, Lon1), Grid, 1);
        foreach (var poly in Coastlines)
        {
            var pts = poly.Select(p => Project(p.lat, p.lon)).ToArray();
            DrawColoredPolygon(pts, Land);
            DrawPolyline(pts.Append(pts[0]).ToArray(), Coast, 1.3f, true);
        }
        var font = Fonts.Heading;
        foreach (var (name, lat, lon) in SeaNames)
            DrawString(font, Project(lat, lon), name, HorizontalAlignment.Center, -1, 13, new Color(InkCol, 0.45f));
        // Bletchley Park
        var bp = Project(51.997, -0.74);
        DrawCircle(bp, 3.5f, InkCol);
        DrawString(Fonts.Body, bp + new Vector2(6, 4), "Station X", HorizontalAlignment.Left, -1, 11, InkCol);

        if (State == null) return;
        foreach (var o in State.Operations)
        {
            var at = Project(o.Lat, o.Lon);
            var col = o.State switch { OpState.Won when o.Use == OpUse.Withheld => Held, OpState.Won when o.Use == OpUse.Undecided => Pending, OpState.Won => Won, OpState.Lost => Lost, _ => Pending };
            float r = o == _hover ? 7 : 5;
            if (o.State == OpState.Pending || o.AwaitingUse)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() / 300f);
                DrawArc(at, r + 3 + pulse * 3, 0, Mathf.Tau, 24, new Color(col, 0.6f - pulse * 0.4f), 1.5f, true);
            }
            DrawCircle(at, r, col);
            DrawArc(at, r, 0, Mathf.Tau, 20, InkCol, 1, true);
        }
        if (_hover is { } h)
        {
            var at = Project(h.Lat, h.Lon);
            string line1 = $"{h.Name}  ·  {h.Place}  ·  {State.DateOf(h.Day):d MMM yyyy}";
            string line2 = h.Outcome;
            float w = Math.Max(font.GetStringSize(line1, HorizontalAlignment.Left, -1, 13).X, Fonts.Body.GetStringSize(line2, HorizontalAlignment.Left, -1, 12).X) + 16;
            var box = new Rect2(at + new Vector2(10, -46), new Vector2(w, 40));
            if (box.End.X > Size.X) box.Position = new Vector2(at.X - w - 10, box.Position.Y);
            if (box.Position.Y < 0) box.Position = new Vector2(box.Position.X, at.Y + 10);
            DrawRect(box, new Color("f2ead8"));
            DrawRect(box, InkCol, false, 1);
            DrawString(font, box.Position + new Vector2(8, 16), line1, HorizontalAlignment.Left, -1, 13, InkCol);
            DrawString(Fonts.Body, box.Position + new Vector2(8, 33), line2, HorizontalAlignment.Left, -1, 12,
                h.State switch { OpState.Won => Won, OpState.Lost => Lost, _ => InkCol });
        }
    }

    public override void _Process(double delta)
    {
        if (State?.Operations.Any(o => o.State == OpState.Pending || o.AwaitingUse) == true) QueueRedraw();
    }

    static readonly (string, double, double)[] SeaNames =
    {
        ("NORTH ATLANTIC", 50, -38), ("NORTH SEA", 56, 3.5), ("MEDITERRANEAN", 36.5, 18), ("BAY OF BISCAY", 45.5, -5),
        ("NORWEGIAN SEA", 67, 2), ("BARENTS SEA", 71.5, 30),
    };

    /// <summary>Simplified coastlines (lat, lon), enough for a staff map.</summary>
    static readonly (double lat, double lon)[][] Coastlines =
    {
        // Great Britain
        new[] { (50.0, -5.7), (50.6, -3.0), (50.8, 0.3), (51.2, 1.4), (52.0, 1.6), (52.7, 1.7), (53.5, 0.2), (54.5, -0.6), (55.6, -1.6), (56.5, -2.6), (57.6, -1.8), (57.7, -3.8), (58.6, -3.0), (58.6, -5.0), (57.5, -5.8), (56.0, -5.6), (55.0, -4.9), (54.9, -3.2), (54.2, -3.2), (53.4, -3.0), (53.3, -4.6), (52.2, -4.2), (51.7, -5.3), (51.4, -3.0), (51.2, -4.2), (50.0, -5.7) },
        // Ireland
        new[] { (52.2, -6.0), (53.9, -6.2), (54.6, -5.5), (55.3, -7.3), (54.5, -8.5), (53.5, -10.0), (52.0, -9.6), (51.6, -8.0) },
        // Iceland
        new[] { (64.0, -22.6), (65.5, -24.0), (66.4, -22.0), (66.5, -16.0), (65.2, -13.6), (64.2, -15.0), (63.4, -18.7) },
        // Continental Europe
        new[] { (36.0, -5.6), (37.0, -8.9), (38.8, -9.5), (42.0, -8.8), (43.3, -8.3), (43.5, -5.0), (43.4, -1.8), (46.0, -1.2), (47.3, -2.6), (48.4, -4.6), (48.7, -1.6), (49.7, -1.3), (49.4, 0.1), (50.1, 1.6), (51.1, 2.5), (51.4, 3.6), (52.9, 4.7), (53.3, 6.9), (53.9, 8.6), (55.0, 8.6), (56.8, 8.1), (57.7, 10.5), (56.0, 10.6), (54.4, 10.9), (53.9, 14.2), (54.8, 18.5), (56.0, 21.0), (57.5, 21.0), (57.6, 24.0), (59.3, 23.5), (59.5, 28.0), (60.5, 30.0), (61.0, 42.0),
                (45.0, 42.0), (44.6, 37.5), (46.4, 30.7), (44.0, 28.6), (42.5, 27.7), (41.0, 29.0), (40.5, 26.5), (40.9, 23.5), (40.0, 22.6), (38.3, 23.9), (36.8, 22.4), (38.3, 21.2), (39.5, 20.0), (41.8, 19.5), (43.5, 16.4), (45.6, 13.6), (44.5, 12.3), (43.6, 13.5), (41.9, 16.0), (40.6, 18.0), (40.0, 18.4), (39.0, 17.1), (38.0, 16.0), (38.9, 16.2), (40.0, 15.6), (40.8, 14.0), (41.8, 12.4), (43.0, 10.5), (44.4, 8.8), (43.8, 7.6), (43.3, 5.3), (43.4, 4.8), (43.0, 3.1), (41.9, 3.2), (41.0, 0.9), (39.5, -0.3), (38.7, 0.1), (37.6, -0.7), (36.7, -4.4) },
        // Scandinavia and Finland
        new[] { (58.0, 5.6), (58.0, 7.0), (59.0, 9.8), (59.2, 10.6), (58.8, 11.2), (57.6, 12.0), (55.6, 12.8), (55.6, 14.3), (56.6, 16.4), (58.6, 16.7), (59.9, 18.9), (61.5, 17.3), (62.8, 17.8), (64.8, 21.4), (65.6, 23.5), (65.4, 25.4), (64.9, 24.7), (63.0, 21.6), (61.0, 21.4), (59.9, 22.9), (60.4, 25.5), (60.6, 28.0), (61.0, 30.0), (65.0, 32.0), (69.0, 33.0), (69.8, 30.5), (71.1, 25.8), (70.0, 19.0), (68.3, 15.0), (66.0, 13.0), (64.3, 10.8), (63.0, 7.6), (62.0, 5.0), (60.5, 5.0) },
        // North Africa
        new[] { (35.8, -6.0), (35.1, -2.0), (36.8, 3.0), (37.2, 10.0), (35.0, 11.0), (33.8, 10.2), (33.1, 11.5), (32.4, 15.0), (30.6, 19.9), (32.1, 20.1), (32.8, 22.0), (32.6, 23.0), (31.6, 25.0), (31.2, 29.9), (31.2, 32.3), (31.5, 35.0), (33.0, 35.3), (36.0, 36.0), (37.0, 42.0), (24.0, 42.0), (24.0, -16.0), (28.0, -13.0), (31.5, -9.8), (34.0, -6.8) },
        // North America
        new[] { (24.0, -80.0), (25.5, -80.1), (28.4, -80.6), (30.7, -81.4), (32.0, -80.8), (33.2, -79.2), (35.2, -75.5), (37.0, -76.0), (38.9, -74.9), (40.5, -74.0), (41.2, -72.0), (41.5, -70.0), (43.0, -70.6), (44.8, -67.0), (45.2, -66.0), (44.6, -63.5), (43.5, -65.6), (45.9, -60.0), (47.0, -60.5), (47.6, -59.3), (46.8, -53.0), (47.6, -52.8), (49.5, -53.5), (51.6, -55.4), (55.0, -60.0), (58.0, -62.0), (60.3, -64.5), (61.5, -70.0), (62.5, -78.0), (73.0, -80.0) },
        // Greenland
        new[] { (60.0, -44.0), (63.0, -50.0), (66.0, -53.0), (70.0, -55.0), (73.0, -56.0), (73.0, -20.0), (70.0, -22.0), (68.0, -25.0), (66.0, -35.0), (65.0, -40.0) },
        // Islands of the Mediterranean
        new[] { (38.2, 12.4), (38.3, 15.6), (36.7, 15.1), (37.6, 12.4) },
        new[] { (41.2, 9.2), (41.2, 9.8), (39.0, 9.6), (38.9, 8.4), (40.8, 8.2) },
        new[] { (43.0, 9.4), (42.0, 9.5), (41.4, 9.1), (42.6, 8.6) },
        new[] { (35.6, 23.5), (35.3, 26.3), (35.0, 24.0) },
        new[] { (35.2, 32.3), (35.7, 34.6), (34.6, 33.0) },
    };
}
