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
    static readonly Dictionary<Vector3, BoxMesh> Boxes = new();
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

    static BoxMesh BoxMesh(Vector3 size)
    {
        if (!Boxes.TryGetValue(size, out var m)) Boxes[size] = m = new BoxMesh { Size = size };
        return m;
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
            Spheres[r] = mesh = new SphereMesh { Radius = r, Height = r * 2, RadialSegments = 14, Rings = 7 };
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
