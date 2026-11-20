using System;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>2D marks drawn over the 3D park: progress bars, warnings, labels.</summary>
public partial class Overlay : Control
{
    public Main Main = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta) => QueueRedraw();

    Vector2 Screen(Vector3 world) => Main.Rig.Cam.UnprojectPosition(world);

    public override void _Draw()
    {
        var s = Main.State;
        if (s == null || Main.Hud.ModalOpen) return;
        float zoom = 18f / Main.Rig.Cam.Size;
        if (Main.ShowFlows) DrawFlows(s);
        if (Main.Hud.PanelId == "workload") DrawUse(s, zoom);
        if (Main.BuildType is StationType bt && !Defs.Station(bt).Outdoor) DrawAllowedZones(s, bt);

        // Markers over desks and machines indoors go under the roofs as they fade in, like the rooms' painted names.
        float roofed = Main.Park.RoofAlpha;
        foreach (var st in s.Stations)
        {
            _fade = st.Def.Outdoor ? 1 : 1 - roofed;
            if (_fade < 0.01f || !Main.StationModels.TryGetValue(st.Id, out var model)) continue;
            var top = Screen(model.TopPoint);
            if (st.Current != null)
            {
                float w = 34;
                var bg = new Rect2(top - new Vector2(w / 2, 3), new Vector2(w, 6));
                DrawRect(bg.Grow(1), F(new Color(0, 0, 0, 0.6f)));
                DrawRect(new Rect2(bg.Position, new Vector2(w * (float)st.Progress, 6)), F(st.OperatorPresent ? Gfx.Brass : Gfx.Warn));
            }
            else if (st.Def.Operator != null && st.Input.Count > 0 && st.OperatorId == null && !st.Obsolete)
            {
                DrawCircle(top, 10, F(Gfx.Ink));
                DrawCircle(top, 8.5f, F(Gfx.Warn));
                DrawString(Fonts.BodyBold, top + new Vector2(-8, 5.5f), "!", HorizontalAlignment.Center, 16, 15, F(Gfx.Ink));
            }
            if (st.PendingRemoval || st.Obsolete)
            {
                DrawLine(top + new Vector2(-8, -8), top + new Vector2(8, 8), F(Gfx.Bad), 3);
                DrawLine(top + new Vector2(8, -8), top + new Vector2(-8, 8), F(Gfx.Bad), 3);
            }
            if (zoom > 0.7f && (st.Input.Count > 7 || st.Output.Count > 7))
                Tag(top + new Vector2(14, 22), $"{st.Input.Count} in · {st.Output.Count} out", UiTheme.PanelEdge, 11);
        }
        _fade = 1;

        if (Main.Ghost is { Problem: string prob } g && Main.GhostModel != null)
            Tag(Screen(Main.GhostModel.TopPoint) + new Vector2(-40, -10), prob, Gfx.Bad);

        if (Main.HoverStaff is int sid && s.StaffById(sid) is { } p && Main.PersonModels.TryGetValue(sid, out var pm))
        {
            string doing = p.Task switch
            {
                { Kind: TaskKind.Operate } t => $"at the {s.StationById(t.Dest)?.Def.Name.ToLowerInvariant()}",
                { Kind: TaskKind.Carry } => "carrying messages",
                { Kind: TaskKind.Rest } => "on a break",
                { Kind: TaskKind.Leave } => "going off watch",
                _ => "waiting for work",
            };
            Tag(Screen(pm.HeadPoint) + new Vector2(12, -6), $"{p.Name} · {Defs.Roles[p.Role].Name}, {doing}", Gfx.Brass);
        }
        else if (Main.HoverStation is int hid && s.StationById(hid) is { } hs && Main.BuildType == null &&
                 Main.StationModels.TryGetValue(hid, out var hm))
        {
            Tag(Screen(hm.TopPoint) + new Vector2(12, -14), hs.Def.Name, Gfx.Brass);
        }
    }

    /// <summary>
    /// Lines for where paper has gone recently (thicker = more), and a ring on each desk coloured by how long
    /// the oldest message in its in tray has waited.
    /// </summary>
    void DrawFlows(GameState s)
    {
        var dim = new Color(0.08f, 0.07f, 0.05f, 0.35f);
        DrawRect(GetViewportRect(), dim);
        foreach (var (key, amount) in s.Flows)
        {
            var parts = key.Split('>');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var a) || !int.TryParse(parts[1], out var b)) continue;
            if (!Main.StationModels.TryGetValue(a, out var ma) || !Main.StationModels.TryGetValue(b, out var mb)) continue;
            var pa = Screen(ma.GlobalPosition + new Vector3(0, 0.5f, 0));
            var pb = Screen(mb.GlobalPosition + new Vector3(0, 0.5f, 0));
            float w = Mathf.Clamp(1.5f + Mathf.Log(1 + (float)amount) * 1.6f, 1.5f, 9);
            var col = new Color(0.95f, 0.85f, 0.55f, 0.75f);
            DrawLine(pa, pb, new Color(0, 0, 0, 0.5f), w + 2, true);
            DrawLine(pa, pb, col, w, true);
            var dir = (pb - pa).Normalized();
            var midp = (pa + pb) / 2;
            var n = new Vector2(-dir.Y, dir.X);
            float ah = 6 + w;
            DrawColoredPolygon(new[] { midp + dir * ah, midp - dir * ah * 0.6f + n * ah * 0.7f, midp - dir * ah * 0.6f - n * ah * 0.7f }, col);
        }
        foreach (var st in s.Stations)
        {
            if (st.IsRest || !Main.StationModels.TryGetValue(st.Id, out var m)) continue;
            double oldest = 0;
            foreach (var id in st.Input)
                if (s.Messages.TryGetValue(id, out var msg)) oldest = Math.Max(oldest, s.Now - msg.Since);
            var c = Screen(m.GlobalPosition + new Vector3(0, 0.05f, 0));
            var col = st.Input.Count == 0 ? new Color(0.6f, 0.6f, 0.55f, 0.5f)
                : oldest < 30 ? Gfx.Good : oldest < 120 ? Gfx.Warn : Gfx.Bad;
            DrawArc(c, 16, 0, Mathf.Tau, 28, new Color(0, 0, 0, 0.6f), 6, true);
            DrawArc(c, 16, 0, Mathf.Tau, 28, col, 4, true);
            if (st.Input.Count > 0)
                Tag(c + new Vector2(18, 6), oldest >= 60 ? $"{st.Input.Count} · {(int)(oldest / 60)}h {(int)(oldest % 60)}m" : $"{st.Input.Count} · {(int)oldest}m", col, 11);
        }
    }

    /// <summary>
    /// While the Workload panel is open: a ring round each desk and machine split by what it did over the last day,
    /// a dot in the middle for what it is doing now, and a dot over each person on watch for what they're doing.
    /// </summary>
    void DrawUse(GameState s, float zoom)
    {
        DrawRect(GetViewportRect(), new Color(0.08f, 0.07f, 0.05f, 0.35f));
        var ink = new Color(0, 0, 0, 0.65f);
        foreach (var st in s.Stations)
        {
            if (st.Use is not { } log || st.Obsolete || !Main.StationModels.TryGetValue(st.Id, out var m)) continue;
            var c = Screen(m.GlobalPosition + new Vector3(0, 0.05f, 0));
            const float r = 15;
            DrawArc(c, r, 0, Mathf.Tau, 32, ink, 9, true);
            double total = log.Total();
            float a = -Mathf.Pi / 2;
            if (total > 0)
                for (int k = 0; k < log.States; k++)
                {
                    float sweep = (float)(log.In(k) / total) * Mathf.Tau;
                    if (sweep <= 0.01f) continue;
                    DrawArc(c, r, a, a + sweep, Math.Max(3, (int)(32 * sweep / Mathf.Tau)), UseLook.Desk[k], 6, true);
                    a += sweep;
                }
            var now = s.DeskState(st);
            DrawCircle(c, 6.5f, ink);
            DrawCircle(c, 5, UseLook.Desk[(int)now]);
            if (zoom > 0.6f && total > 0)
            {
                double open = total - log.In((int)DeskUse.Closed);
                int busy = open > 0 ? (int)Math.Round(log.In((int)DeskUse.Working) / open * 100) : 0;
                Tag(c + new Vector2(r + 4, 6), st.Input.Count > 0 ? $"{busy}% · {st.Input.Count} in tray" : $"{busy}%", UseLook.Desk[(int)now], 11);
            }
        }
        foreach (var p in s.Staff)
        {
            if (p.Away || s.PersonState(p) is not PersonUse u || !Main.PersonModels.TryGetValue(p.Id, out var pm)) continue;
            var at = Screen(pm.HeadPoint) + new Vector2(0, -8);
            DrawCircle(at, 4.5f, ink);
            DrawCircle(at, 3.4f, UseLook.Person[(int)u]);
        }
    }

    /// <summary>While placing, outline the huts that take this station; the rest are greyed over.</summary>
    void DrawAllowedZones(GameState s, StationType t)
    {
        foreach (var z in s.Grid.Zones)
        {
            if (!z.Unlocked) continue;
            float x0 = z.X + 1, y0 = z.Y + 1, x1 = z.X + z.W - 1, y1 = z.Y + z.H - 1;
            var pts = new[]
            {
                Screen(new Vector3(x0, 0.03f, y0)), Screen(new Vector3(x1, 0.03f, y0)),
                Screen(new Vector3(x1, 0.03f, y1)), Screen(new Vector3(x0, 0.03f, y1)),
            };
            bool ok = Defs.AllowedIn(z.Id, t);
            DrawColoredPolygon(pts, ok ? new Color(0.45f, 0.95f, 0.45f, 0.10f) : new Color(0.05f, 0.04f, 0.03f, 0.45f));
            if (ok) DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, new Color(0.55f, 1f, 0.55f, 0.8f), 2, true);
        }
    }

    /// <summary>How much of the station markers being drawn shows (they fade under the roofs).</summary>
    float _fade = 1;
    Color F(Color c) => c with { A = c.A * _fade };

    void Tag(Vector2 at, string text, Color edge, int size = 13)
    {
        var font = Fonts.Body;
        var sz = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var r = new Rect2(at - new Vector2(5, sz.Y), sz + new Vector2(10, 7));
        DrawRect(r, F(UiTheme.PaperBg));
        DrawRect(r, F(edge), false, 1.5f);
        DrawString(font, at + new Vector2(0, -3), text, HorizontalAlignment.Left, -1, size, F(UiTheme.PaperInk));
    }
}
