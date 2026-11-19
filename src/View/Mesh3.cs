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

    /// <summary>
    /// Window glass: dark, since the room behind is dim against daylight, and smooth, so it shows the sky. The slight
    /// metalness stands in for the bright sheen old panes have at a slant.
    /// </summary>
    public static StandardMaterial3D Glass => _glass ??= new StandardMaterial3D
    {
        AlbedoColor = new Color("3a4648"),
        Roughness = 0.08f,
        Metallic = 0.3f,
        MetallicSpecular = 0.7f,
    };
    static StandardMaterial3D? _glass;

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

    /// <summary>
    /// A pitched roof over the rectangle x0..x1 × z0..z1 (eaves at height y, overhang included by the caller), its ridge
    /// along the longer side, `rise` above the eaves. Hipped: the ends slope too, at the same pitch. Otherwise the ends
    /// are upright gables, a second surface for the wall material. UVs run along the eaves and up the slope, in tiles.
    /// </summary>
    public static MeshInstance3D Roof(Node3D parent, float x0, float x1, float z0, float z1, float y, float rise, bool hipped,
        Material roof, Material? gable = null)
    {
        bool alongX = x1 - x0 >= z1 - z0;
        float L = alongX ? x1 - x0 : z1 - z0, S = alongX ? z1 - z0 : x1 - x0, half = S / 2;
        float inset = hipped ? Mathf.Min(half, L / 2) : 0;
        // (a along the ridge, b across it, h up) to world
        Vector3 P(float a, float b, float h) => alongX ? new Vector3(x0 + a, y + h, z0 + b) : new Vector3(x0 + b, y + h, z0 + a);
        float slope = Mathf.Sqrt(half * half + rise * rise);
        float endSlope = Mathf.Sqrt(inset * inset + rise * rise);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void V(Vector3 p, Vector2 uv) { st.SetUV(uv); st.AddVertex(p); }
        void Tri(Vector3 a, Vector2 ua, Vector3 b, Vector2 ub, Vector3 c, Vector2 uc)
        {
            // Wound so the face points up (Godot's front faces are clockwise seen from the front).
            if ((b - a).Cross(c - a).Y > 0) { V(a, ua); V(c, uc); V(b, ub); }
            else { V(a, ua); V(b, ub); V(c, uc); }
        }
        // The two long slopes: eave (u along it), ridge at v = slope length.
        foreach (float side in new[] { 0f, S })
        {
            Vector3 e0 = P(0, side, 0), e1 = P(L, side, 0), r0 = P(inset, half, rise), r1 = P(L - inset, half, rise);
            Vector2 ue0 = new(0, 0), ue1 = new(L, 0), ur0 = new(inset, slope), ur1 = new(L - inset, slope);
            Tri(e0, ue0, e1, ue1, r1, ur1);
            Tri(e0, ue0, r1, ur1, r0, ur0);
        }
        if (hipped)
            foreach (float end in new[] { 0f, L })
            {
                float ridgeA = end == 0 ? inset : L - inset;
                Tri(P(end, 0, 0), new Vector2(0, 0), P(end, S, 0), new Vector2(S, 0), P(ridgeA, half, rise), new Vector2(half, endSlope));
            }
        st.GenerateNormals();
        st.GenerateTangents();
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, roof);
        if (!hipped && gable != null)
        {
            var gt = new SurfaceTool();
            gt.Begin(Mesh.PrimitiveType.Triangles);
            foreach (float end in new[] { 0f, L })
            {
                Vector3 a = P(end, 0, 0), b = P(end, S, 0), c = P(end, half, rise);
                var outward = P(end == 0 ? -1 : L + 1, half, 0) - P(end, half, 0);
                bool ccw = (b - a).Cross(c - a).Dot(outward) > 0;
                foreach (var p in ccw ? new[] { a, c, b } : new[] { a, b, c }) { gt.SetUV(new Vector2(alongX ? p.Z : p.X, p.Y)); gt.AddVertex(p); }
            }
            gt.GenerateNormals();
            gt.GenerateTangents();
            gt.Commit(mesh);
            mesh.SurfaceSetMaterial(1, gable);
        }
        var mi = new MeshInstance3D { Mesh = mesh };
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
