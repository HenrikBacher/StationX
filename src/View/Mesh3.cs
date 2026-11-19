using System;
using System.Collections.Generic;
using Godot;

namespace StationX.View;

/// <summary>
/// Builds the chunky low-poly models from primitives. Meshes and materials are cached and shared.
/// World units are tiles: tile (x, y) spans x..x+1 and z = y..y+1, with Y up.
/// </summary>
public static class Mesh3
{
    static readonly Dictionary<(Color, float, bool), StandardMaterial3D> Mats = new();
    static readonly Dictionary<(float, float, float), CylinderMesh> Cyls = new();
    static readonly Dictionary<float, SphereMesh> Spheres = new();

    public static StandardMaterial3D Mat(Color c, float emission = 0, bool glossy = false)
    {
        emission = MathF.Round(emission * 20) / 20;   // animated glows would otherwise fill the cache
        var key = (c, emission, glossy);
        if (Mats.TryGetValue(key, out var m)) return m;
        m = new StandardMaterial3D
        {
            AlbedoColor = c,
            Roughness = glossy ? 0.25f : 0.85f,
            Metallic = glossy ? 0.2f : 0f,
        };
        if (emission > 0)
        {
            m.EmissionEnabled = true;
            m.Emission = c;
            m.EmissionEnergyMultiplier = emission;
        }
        if (c.A < 1) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        Mats[key] = m;
        return m;
    }

    public static StandardMaterial3D Ghost(Color c) => new()
    {
        AlbedoColor = c,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        NoDepthTest = false,
    };

    static readonly Dictionary<Vector3, Mesh> Bevelled = new();

    /// <summary>
    /// A box with chamfered edges and corners: each edge catches a thin highlight, so furniture and walls read as made
    /// things rather than blocks. Very thin pieces (papers, threads, labels) stay plain boxes.
    /// </summary>
    static Mesh BoxMesh(Vector3 size)
    {
        if (Bevelled.TryGetValue(size, out var cached)) return cached;
        float min = Mathf.Min(size.X, Mathf.Min(size.Y, size.Z));
        float b = Mathf.Min(0.022f, min * 0.18f);
        Mesh m = b < 0.004f ? new BoxMesh { Size = size } : ChamferedBox(size / 2, b);
        Bevelled[size] = m;
        return m;
    }

    static ArrayMesh ChamferedBox(Vector3 h, float b)
    {
        // For each corner (sx, sy, sz) three points, one on each of the faces that meet there.
        Vector3 P(int axis, int sx, int sy, int sz) => axis switch
        {
            0 => new Vector3(sx * h.X, sy * (h.Y - b), sz * (h.Z - b)),
            1 => new Vector3(sx * (h.X - b), sy * h.Y, sz * (h.Z - b)),
            _ => new Vector3(sx * (h.X - b), sy * (h.Y - b), sz * h.Z),
        };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Tri(Vector3 a, Vector3 c, Vector3 d)
        {
            var n = (c - a).Cross(d - a);
            // Godot's front faces wind clockwise seen from outside: the cross product points inward.
            if (n.Dot(a + c + d) > 0) (c, d) = (d, c);
            var outward = -(c - a).Cross(d - a).Normalized();
            st.SetNormal(outward); st.AddVertex(a);
            st.SetNormal(outward); st.AddVertex(c);
            st.SetNormal(outward); st.AddVertex(d);
        }
        void Quad(Vector3 a, Vector3 c, Vector3 d, Vector3 e) { Tri(a, c, d); Tri(a, d, e); }
        int[] S = { -1, 1 };
        // Faces
        foreach (var s in S)
        {
            Quad(P(0, s, -1, -1), P(0, s, 1, -1), P(0, s, 1, 1), P(0, s, -1, 1));
            Quad(P(1, -1, s, -1), P(1, 1, s, -1), P(1, 1, s, 1), P(1, -1, s, 1));
            Quad(P(2, -1, -1, s), P(2, 1, -1, s), P(2, 1, 1, s), P(2, -1, 1, s));
        }
        // Edge chamfers
        foreach (var a in S)
            foreach (var c in S)
            {
                Quad(P(0, a, c, -1), P(0, a, c, 1), P(1, a, c, 1), P(1, a, c, -1));   // along Z
                Quad(P(0, a, -1, c), P(0, a, 1, c), P(2, a, 1, c), P(2, a, -1, c));   // along Y
                Quad(P(1, -1, a, c), P(1, 1, a, c), P(2, 1, a, c), P(2, -1, a, c));   // along X
            }
        // Corner facets
        foreach (var x in S)
            foreach (var y in S)
                foreach (var z in S)
                    Tri(P(0, x, y, z), P(1, x, y, z), P(2, x, y, z));
        return st.Commit();
    }

    public static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 center, Color c, float emission = 0)
    {
        var mi = new MeshInstance3D { Mesh = BoxMesh(size), Position = center, MaterialOverride = Mat(c, emission) };
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 center, Material m)
    {
        var mi = new MeshInstance3D { Mesh = BoxMesh(size), Position = center, MaterialOverride = m };
        parent.AddChild(mi);
        return mi;
    }

    static readonly Dictionary<Vector3, PrismMesh> Prisms = new();

    /// <summary>A triangular prism (a gable or a roof ridge), apex up, extruded along local Z.</summary>
    public static MeshInstance3D Prism(Node3D parent, Vector3 size, Vector3 center, Material m)
    {
        if (!Prisms.TryGetValue(size, out var mesh)) Prisms[size] = mesh = new PrismMesh { Size = size };
        var mi = new MeshInstance3D { Mesh = mesh, Position = center, MaterialOverride = m };
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Dome(Node3D parent, float r, Vector3 center, Color c)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = r, Height = r, IsHemisphere = true, RadialSegments = 20, Rings = 8 },
            Position = center,
            MaterialOverride = Mat(c),
        };
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>A box given by its min corner and size, which reads more naturally for furniture.</summary>
    public static MeshInstance3D BoxAt(Node3D parent, float x, float y, float z, float w, float h, float d, Color c, float emission = 0) =>
        Box(parent, new Vector3(w, h, d), new Vector3(x + w / 2, y + h / 2, z + d / 2), c, emission);

    public static MeshInstance3D Cyl(Node3D parent, float rTop, float rBottom, float height, Vector3 center, Color c, int segs = 16, float emission = 0)
    {
        var key = (rTop, rBottom, height);
        if (!Cyls.TryGetValue(key, out var mesh))
            Cyls[key] = mesh = new CylinderMesh { TopRadius = rTop, BottomRadius = rBottom, Height = height, RadialSegments = segs, Rings = 1 };
        var mi = new MeshInstance3D { Mesh = mesh, Position = center, MaterialOverride = Mat(c, emission) };
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Sphere(Node3D parent, float r, Vector3 center, Color c)
    {
        if (!Spheres.TryGetValue(r, out var mesh))
            Spheres[r] = mesh = new SphereMesh { Radius = r, Height = r * 2, RadialSegments = 22, Rings = 11 };
        var mi = new MeshInstance3D { Mesh = mesh, Position = center, MaterialOverride = Mat(c) };
        parent.AddChild(mi);
        return mi;
    }

    public static Label3D Text(Node3D parent, string text, Vector3 pos, int size, Color color, Font font, bool billboard = true)
    {
        var l = new Label3D
        {
            Text = text,
            Position = pos,
            Font = font,
            FontSize = size,
            Modulate = color,
            OutlineSize = 0,
            PixelSize = 0.006f,
            Billboard = billboard ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.Disabled,
            DoubleSided = true,
            Shaded = false,
        };
        parent.AddChild(l);
        return l;
    }

    /// <summary>Replaces every mesh material under a node, e.g. for the build ghost.</summary>
    public static void OverrideAll(Node n, Material m)
    {
        if (n is MeshInstance3D mi) { mi.MaterialOverride = m; mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; }
        foreach (var c in n.GetChildren()) OverrideAll(c, m);
    }
}
