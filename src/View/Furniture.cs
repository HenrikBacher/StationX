using System.Collections.Generic;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>
/// The still parts of every desk and machine (tops, legs, chairs, cabinets, papers laid out), merged per building and
/// per material: a roomful of furniture in a handful of draws rather than one per leg. Each StationModel keeps what
/// moves or changes colour (drums, tapes, lamps, the in and out trays). A building's batch is rebuilt when one of its
/// stations is built, moved, picked up to move, or sold, and hidden with everything else indoors under solid roofs.
/// </summary>
public partial class Furniture : Node3D
{
    /// <summary>A station as last batched: where it stood, and whether it was in (not picked up to move).</summary>
    struct Seen
    {
        public string Group;
        public int X, Y, Rot, Stamp;
        public bool In;
    }

    readonly Dictionary<int, Seen> _seen = new();
    readonly Dictionary<string, Node3D> _groups = new();
    readonly HashSet<string> _dirty = new();
    readonly List<int> _gone = new();
    int _stamp;

    public void Clear()
    {
        foreach (var n in _groups.Values) n.QueueFree();
        _groups.Clear();
        _seen.Clear();
        _dirty.Clear();
    }

    /// <summary>Indoor stations batch with their building, the rest (the rounders pitch) together outside.</summary>
    static string GroupOf(GameState g, Station st) =>
        st.Def.Outdoor ? "" : g.Grid.ZoneAt(st.X, st.Y)?.Id ?? "";

    public void Sync(GameState g, Dictionary<int, StationModel> models, int? moving, bool roofed)
    {
        _stamp++;
        foreach (var st in g.Stations)
        {
            if (!models.TryGetValue(st.Id, out var m) || !m.Detached) continue;
            bool inPlace = st.Id != moving;
            if (_seen.TryGetValue(st.Id, out var was) && was.X == st.X && was.Y == st.Y && was.Rot == st.Rot && was.In == inPlace)
            {
                was.Stamp = _stamp;
                _seen[st.Id] = was;
                continue;
            }
            var group = GroupOf(g, st);
            if (_seen.ContainsKey(st.Id)) _dirty.Add(was.Group);
            _dirty.Add(group);
            _seen[st.Id] = new Seen { Group = group, X = st.X, Y = st.Y, Rot = st.Rot, In = inPlace, Stamp = _stamp };
        }
        if (_seen.Count > 0)
        {
            foreach (var (id, s) in _seen)
                if (s.Stamp != _stamp) _gone.Add(id);
            foreach (var id in _gone)
            {
                _dirty.Add(_seen[id].Group);
                _seen.Remove(id);
            }
            _gone.Clear();
        }
        if (_dirty.Count > 0)
        {
            foreach (var group in _dirty) Rebuild(g, models, group);
            _dirty.Clear();
        }
        foreach (var (group, node) in _groups)
        {
            bool show = group == "" || !roofed;
            if (node.Visible != show) node.Visible = show;
        }
    }

    void Rebuild(GameState g, Dictionary<int, StationModel> models, string group)
    {
        if (_groups.TryGetValue(group, out var old)) { RemoveChild(old); old.QueueFree(); }
        var node = new Node3D { Name = group == "" ? "Outdoors" : group };
        AddChild(node);
        _groups[group] = node;
        var tools = new Dictionary<(Material, GeometryInstance3D.ShadowCastingSetting), SurfaceTool>();
        var painted = new Dictionary<(bool, GeometryInstance3D.ShadowCastingSetting), Mesh3.PaintedMesh>();
        foreach (var (id, seen) in _seen)
        {
            if (seen.Group != group || !seen.In || !models.TryGetValue(id, out var m)) continue;
            var at = StationModel.Placement(m.Type, seen.X, seen.Y, seen.Rot);
            foreach (var part in m.StaticParts)
            {
                // Plain colours and shades of wood go in one mesh each, coloured per vertex.
                if (Mesh3.Paintable(part.Mat, out var colour, out bool wood))
                {
                    if (!painted.TryGetValue((wood, part.Shadow), out var pm)) painted[(wood, part.Shadow)] = pm = new Mesh3.PaintedMesh();
                    pm.Add(part.Mesh, part.Surface, at * part.Local, colour);
                    continue;
                }
                var key = (part.Mat, part.Shadow);
                if (!tools.TryGetValue(key, out var st))
                {
                    tools[key] = st = new SurfaceTool();
                    st.Begin(Mesh.PrimitiveType.Triangles);
                }
                st.AppendFrom(part.Mesh, part.Surface, at * part.Local);
            }
        }
        foreach (var ((wood, shadow), pm) in painted)
            node.AddChild(new MeshInstance3D { Mesh = pm.Commit(Mesh3.PaintMat(wood)), CastShadow = shadow });
        foreach (var ((mat, shadow), st) in tools)
        {
            var mesh = st.Commit();
            mesh.SurfaceSetMaterial(0, mat);
            node.AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = shadow });
        }
    }
}
