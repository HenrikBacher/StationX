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
        // The flow view and the workload view share the floor: one dimming, and with both on the workload ring
        // (which carries the paper waiting too) stands in for the flow view's, so two rings never sit on one desk.
        bool use = Main.Hud.PanelId == "workload";
        if (Main.ShowFlows || use) DrawRect(GetViewportRect(), new Color(0.08f, 0.07f, 0.05f, 0.35f));
        if (Main.ShowFlows) DrawFlows(s, rings: !use);
        if (use) DrawUse(s, zoom);
        if (Main.ShowFlows) DrawFlowKey(use);
        if (Main.BuildType is StationType bt && !Defs.Station(bt).Outdoor) DrawAllowedZones(s, bt);

        foreach (var st in s.Stations)
        {
            if (!Main.StationModels.TryGetValue(st.Id, out var model)) continue;
            var top = Screen(model.TopPoint);
            if (st.Current != null)
            {
                float w = 34;
                var bg = new Rect2(top - new Vector2(w / 2, 3), new Vector2(w, 6));
                DrawRect(bg.Grow(1), new Color(0, 0, 0, 0.6f));
                DrawRect(new Rect2(bg.Position, new Vector2(w * (float)st.Progress, 6)), st.OperatorPresent ? Gfx.Brass : Gfx.Warn);
            }
            else if (st.Def.Operator != null && st.Input.Count > 0 && st.OperatorId == null && !st.Obsolete)
            {
                DrawCircle(top, 10, Gfx.Ink);
                DrawCircle(top, 8.5f, Gfx.Warn);
                DrawString(Fonts.BodyBold, top + new Vector2(-8, 5.5f), "!", HorizontalAlignment.Center, 16, 15, Gfx.Ink);
            }
            if (st.PendingRemoval || st.Obsolete)
            {
                DrawLine(top + new Vector2(-8, -8), top + new Vector2(8, 8), Gfx.Bad, 3);
                DrawLine(top + new Vector2(8, -8), top + new Vector2(-8, 8), Gfx.Bad, 3);
            }
            if (zoom > 0.7f && (st.Input.Count > 7 || st.Output.Count > 7))
                Tag(top + new Vector2(14, 22), $"{st.Input.Count} in · {st.Output.Count} out", UiTheme.PanelEdge, 11);
        }

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
    void DrawFlows(GameState s, bool rings)
    {
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
        if (!rings) return;
        _tags.Clear();
        _taken.Clear();
        foreach (var st in s.Stations)
        {
            if (st.IsRest || !Main.StationModels.TryGetValue(st.Id, out var m)) continue;
            double oldest = Oldest(s, st);
            var c = Screen(m.GlobalPosition + new Vector3(0, 0.05f, 0));
            var col = UseLook.Wait(st.Input.Count, oldest);
            DrawArc(c, 16, 0, Mathf.Tau, 28, new Color(0, 0, 0, 0.6f), 6, true);
            DrawArc(c, 16, 0, Mathf.Tau, 28, col, 4, true);
            _taken.Add(new Rect2(c - new Vector2(19, 19), new Vector2(38, 38)));
            bool focused = st.Id == (Main.HoverStation ?? Main.SelectedStation);
            if (st.Input.Count > 0)
                _tags.Add((focused ? double.MaxValue : oldest, c, focused ? UseLook.Waiting(st.Input.Count, oldest) : UseLook.WaitingShort(st.Input.Count, oldest), col, focused));
        }
        _ringsPlaced = _taken.Count;
        _tags.Sort((x, y) => y.Rank.CompareTo(x.Rank));
        foreach (var (_, c, text, edge, focused) in _tags) PlaceTag(c, 16, text, edge, 11, focused);
    }

    static double Oldest(GameState s, Station st)
    {
        double oldest = 0;
        foreach (var id in st.Input)
            if (s.Messages.TryGetValue(id, out var msg)) oldest = Math.Max(oldest, s.Now - msg.Since);
        return oldest;
    }

    /// <summary>A key for the flow view, low on the right of the screen.</summary>
    void DrawFlowKey(bool withUse)
    {
        var font = Fonts.Body;
        const int size = 11;
        var parts = new System.Collections.Generic.List<(string, Color?)> { ("FLOW VIEW  lines: where paper went, thicker for more", null) };
        if (withUse) parts.Add(("rings: the Workload panel's day; tags: busy, and paper waiting", null));
        else foreach (var (w, c) in UseLook.WaitKey) parts.Add((w, c));
        float width = 0;
        foreach (var (t, c) in parts) width += font.GetStringSize(t, HorizontalAlignment.Left, -1, size).X + (c != null ? 16 : 0) + 14;
        var vp = GetViewportRect().Size;
        var at = new Vector2(vp.X - width - 16, vp.Y - 18);   // bottom right, clear of the side panel
        DrawRect(new Rect2(at - new Vector2(8, 14), new Vector2(width + 8, 22)), new Color(0, 0, 0, 0.6f));
        foreach (var (t, c) in parts)
        {
            if (c is Color col)
            {
                DrawArc(at + new Vector2(5, -4), 5, 0, Mathf.Tau, 16, col, 2.5f, true);
                at.X += 16;
            }
            DrawString(font, at, t, HorizontalAlignment.Left, -1, size, new Color(1, 1, 1, 0.75f));
            at.X += font.GetStringSize(t, HorizontalAlignment.Left, -1, size).X + 14;
        }
    }

    /// <summary>
    /// While the Workload panel is open: a ring round each desk and machine split by what it did over the last day,
    /// a dot in the middle for what it is doing now, and a dot over each person on watch for what they're doing.
    /// </summary>
    void DrawUse(GameState s, float zoom)
    {
        var ink = new Color(0, 0, 0, 0.65f);
        const float r = 15;
        _tags.Clear();
        _taken.Clear();
        int? focus = Main.HoverStation ?? Main.SelectedStation;
        foreach (var st in s.Stations)
        {
            if (st.Use is not { } log || st.Obsolete || !Main.StationModels.TryGetValue(st.Id, out var m)) continue;
            var c = Screen(m.GlobalPosition + new Vector3(0, 0.05f, 0));
            DrawArc(c, r, 0, Mathf.Tau, 32, ink, 9, true);
            // Over its open hours, as in the panel: after hours (before there are watches) left out.
            double total = log.Total() - log.In((int)DeskUse.Closed);
            float a = -Mathf.Pi / 2;
            if (total > 0)
                for (int k = 0; k < log.States; k++)
                {
                    if (k == (int)DeskUse.Closed) continue;
                    float sweep = (float)(log.In(k) / total) * Mathf.Tau;
                    if (sweep <= 0.01f) continue;
                    DrawArc(c, r, a, a + sweep, Math.Max(3, (int)(32 * sweep / Mathf.Tau)), UseLook.Desk[k], 6, true);
                    a += sweep;
                }
            var now = s.DeskState(st);
            DrawCircle(c, 6.5f, ink);
            DrawCircle(c, 5, UseLook.Desk[(int)now]);
            _taken.Add(new Rect2(c - new Vector2(r + 4, r + 4), new Vector2(2 * r + 8, 2 * r + 8)));
            if (total <= 0) continue;
            int busy = (int)Math.Round(log.In((int)DeskUse.Working) / total * 100);
            bool focused = st.Id == focus;
            // Only desks with something to say get a tag (paper waiting, worked hard, out of action), and only when
            // zoomed in enough to read them; the desk under the mouse or selected always has its say, in full.
            bool worth = st.Input.Count > 0 || busy >= 50 || now == DeskUse.Down;
            if (!focused && (!worth || zoom <= 0.6f)) continue;
            double oldest = st.Input.Count > 0 ? Oldest(s, st) : 0;
            string text = focused
                ? $"{st.Def.Name} · {busy}% busy" + (st.Input.Count > 0 ? $" · {UseLook.Waiting(st.Input.Count, oldest)}" : now == DeskUse.Down ? " · out of action" : "")
                : st.Input.Count > 0 ? $"{busy}% · {UseLook.WaitingShort(st.Input.Count, oldest)}" : now == DeskUse.Down ? $"{busy}% · down" : $"{busy}%";
            var edge = st.Input.Count > 0 ? UseLook.Wait(st.Input.Count, oldest) : UseLook.Desk[(int)now] with { A = 1 };
            // The focused desk first, then the longest waits, then the busiest: a tag that would cover one already
            // placed (or a neighbour's ring) tries the other side, and is left out if that is taken too.
            double rank = focused ? double.MaxValue : st.Input.Count > 0 ? 1e6 + oldest : busy;
            _tags.Add((rank, c, text, edge, focused));
        }
        _ringsPlaced = _taken.Count;
        _tags.Sort((x, y) => y.Rank.CompareTo(x.Rank));
        foreach (var (_, c, text, edge, focused) in _tags) PlaceTag(c, r, text, edge, 11, focused);
        foreach (var p in s.Staff)
        {
            if (p.Away || s.PersonState(p) is not PersonUse u || !Main.PersonModels.TryGetValue(p.Id, out var pm)) continue;
            var at = Screen(pm.HeadPoint) + new Vector2(0, -8);
            DrawCircle(at, 4.5f, ink);
            DrawCircle(at, 3.4f, UseLook.Person[(int)u]);
        }
    }

    readonly System.Collections.Generic.List<(double Rank, Vector2 At, string Text, Color Edge, bool Always)> _tags = new();
    /// <summary>Screen space already covered by rings and tags this frame.</summary>
    readonly System.Collections.Generic.List<Rect2> _taken = new();

    /// <summary>
    /// A tag beside a ring at <paramref name="c"/>: right of it if that's clear, else left, above or below. If every
    /// side would cover another tag, or lie off screen or under the HUD, it is left out, unless <paramref name="always"/>. Neighbouring rings only push it
    /// aside, they never hide it (rings sit close in a busy hut).
    /// </summary>
    void PlaceTag(Vector2 c, float ringR, string text, Color edge, int size, bool always)
    {
        var sz = Fonts.Body.GetStringSize(text, HorizontalAlignment.Left, -1, size) + new Vector2(10, 7);
        Span<Vector2> spots = stackalloc Vector2[]
        {
            c + new Vector2(ringR + 4, 6), c + new Vector2(1 - ringR - sz.X, 6),
            c + new Vector2(5 - sz.X / 2, -ringR - 9), c + new Vector2(5 - sz.X / 2, ringR - 5 + sz.Y),
        };
        Vector2? best = null;
        int bestHits = int.MaxValue;
        var view = GetViewportRect();
        foreach (var at in spots)
        {
            var rect = TagRect(at, sz);
            int hits = 0;
            bool onTag = false;
            for (int i = 0; i < _taken.Count; i++)
                if (_taken[i].Intersects(rect))
                {
                    hits++;
                    if (i >= _ringsPlaced) onTag = true;
                }
            // Off screen or under the HUD is as bad as on another tag: nobody would see it.
            if (onTag || !view.Encloses(rect) || Main.Hud.Covers(rect)) continue;
            if (hits < bestHits) { best = at; bestHits = hits; }
            if (hits == 0) break;
        }
        if (best == null && !always) return;
        var pos = best ?? spots[0];
        _taken.Add(TagRect(pos, sz));
        Tag(pos, text, edge, size);
    }

    /// <summary>How many of <see cref="_taken"/> are rings (they come first).</summary>
    int _ringsPlaced;

    static Rect2 TagRect(Vector2 at, Vector2 sz) => new(at - new Vector2(5, sz.Y - 7), sz);

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

    void Tag(Vector2 at, string text, Color edge, int size = 13)
    {
        var font = Fonts.Body;
        var sz = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var r = new Rect2(at - new Vector2(5, sz.Y), sz + new Vector2(10, 7));
        DrawRect(r, UiTheme.PaperBg);
        DrawRect(r, edge, false, 1.5f);
        DrawString(font, at + new Vector2(0, -3), text, HorizontalAlignment.Left, -1, size, UiTheme.PaperInk);
    }
}
