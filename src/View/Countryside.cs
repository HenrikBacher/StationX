using System.Collections.Generic;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>Buckinghamshire beyond the fence: a patchwork of fields and hedgerows, the lane from the gate, and woodland
/// round the park, all fading into the dark so the park never looks like a slab floating in space.</summary>
public partial class Countryside : Node3D
{
    const string Code = @"
shader_type spatial;
render_mode cull_back;

uniform vec2 park;           // park size in tiles; the park covers (0,0)..park
uniform float gate_z;        // centre of the gate on the west fence
uniform float snow = 0.0;
uniform vec3 fade : source_color = vec3(0.09, 0.075, 0.06);

varying vec3 wpos;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

void fragment() {
    vec2 p = wpos.xz;
    vec2 outside = max(max(-p, p - park), vec2(0.0));
    float dout = length(outside);

    // Fields: irregular rectangles, every other row shifted, edges wobbled.
    vec2 w = p + vec2(noise(p * 0.07), noise(p * 0.07 + 9.0)) * 5.0;
    vec2 size = vec2(13.0, 9.0);
    float row = floor(w.y / size.y);
    vec2 cw = vec2(w.x + row * 5.3, w.y);
    vec2 cell = floor(cw / size);
    vec2 f = fract(cw / size) * size;
    float kind = hash(cell);
    float n = noise(p * 0.9);
    vec3 col;
    if (kind < 0.42)      col = mix(vec3(0.30, 0.38, 0.20), vec3(0.36, 0.42, 0.24), n);          // pasture
    else if (kind < 0.66) {                                                                     // ploughed
        vec2 dir = hash(cell + 3.0) < 0.5 ? vec2(1.0, 0.0) : vec2(0.0, 1.0);
        float furrow = 0.5 + 0.5 * sin(dot(cw, dir) * 9.0);
        col = mix(vec3(0.27, 0.20, 0.14), vec3(0.36, 0.28, 0.19), furrow * 0.6 + n * 0.4);
    }
    else if (kind < 0.84) col = mix(vec3(0.50, 0.44, 0.27), vec3(0.58, 0.51, 0.32), n);          // stubble
    else                  col = mix(vec3(0.33, 0.40, 0.24), vec3(0.42, 0.45, 0.28), n);          // meadow

    // Rough verge right round the park.
    col = mix(vec3(0.31, 0.37, 0.21) * (0.85 + 0.3 * n), col, smoothstep(1.0, 3.0, dout));

    // Hedgerows along field edges.
    float edge = min(min(f.x, size.x - f.x), min(f.y, size.y - f.y));
    float hedge = smoothstep(0.55, 0.25, edge) * step(1.5, dout);
    col = mix(col, vec3(0.13, 0.18, 0.10) * (0.8 + 0.4 * noise(p * 3.0)), hedge);

    // The lane: out of the gate westward, wandering a little.
    float lz = gate_z + (p.x < -4.0 ? sin((p.x + 4.0) * 0.06) * 4.0 : 0.0);
    float lane = smoothstep(1.3, 1.0, abs(p.y - lz)) * step(p.x, 0.2);
    col = mix(col, vec3(0.45, 0.41, 0.34) * (0.9 + 0.2 * n), lane);

    // Snow lies everywhere but the hedge bottoms.
    float sn = snow * (1.0 - hedge * 0.5) * (0.85 + 0.15 * n);
    col = mix(col, vec3(0.86, 0.88, 0.9), sn);

    // Fade into the night beyond.
    float far = smoothstep(8.0, 30.0, dout);
    // Colours above are written as sRGB; the renderer wants linear.
    ALBEDO = mix(pow(col, vec3(2.2)), fade, far);
    ROUGHNESS = 0.95;
    SPECULAR = 0.2;
}";

    ShaderMaterial _mat = null!;

    public override void _Ready()
    {
        _mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
        _mat.SetShaderParameter("park", new Vector2(Grid.Width, Grid.Height));
        _mat.SetShaderParameter("gate_z", Grid.Gate.y + 0.5f);
        AddChild(new MeshInstance3D
        {
            Name = "Fields",
            Mesh = new PlaneMesh { Size = new Vector2(200, 180) },
            Position = new Vector3(Grid.Width / 2f, -0.015f, Grid.Height / 2f),
            MaterialOverride = _mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        AddChild(Woods());
    }

    public void SetSnow(float cover) => _mat.SetShaderParameter("snow", cover);

    /// <summary>Woodland round the boundary and copses further out, as multimeshes so hundreds of trees cost little.</summary>
    static Node3D Woods()
    {
        var root = new Node3D { Name = "Woods" };
        var rng = new RandomNumberGenerator { Seed = 1938 };
        var spots = new List<(Vector3 p, float s, bool conifer)>();
        float W = Grid.Width, H = Grid.Height;
        float gz = Grid.Gate.y + 0.5f;
        bool Clear(Vector3 p)
        {
            // Keep the lane and the strip just outside the fence open.
            if (p.X > -1.4f && p.X < W + 1.4f && p.Z > -1.4f && p.Z < H + 1.4f) return false;
            if (p.X < 0.5f && Mathf.Abs(p.Z - gz) < 2.6f) return false;
            foreach (var q in spots)
                if ((q.p - p).LengthSquared() < q.s * q.s * 1.6f) return false;
            return true;
        }
        // A belt of trees just beyond the fence, thicker in clumps.
        for (int i = 0; i < 900 && spots.Count < 190; i++)
        {
            float side = rng.Randf();
            float depth = 2.0f + Mathf.Pow(rng.Randf(), 1.3f) * 12f;
            Vector3 p = side switch
            {
                < 0.3f => new Vector3(rng.RandfRange(-depth, W + depth), 0, -depth),
                < 0.6f => new Vector3(W + depth, 0, rng.RandfRange(-depth, H + depth)),
                < 0.8f => new Vector3(rng.RandfRange(-depth, W + depth), 0, H + depth),
                _ => new Vector3(-depth, 0, rng.RandfRange(-depth, H + depth)),
            };
            p += new Vector3(rng.RandfRange(-0.6f, 0.6f), 0, rng.RandfRange(-0.6f, 0.6f));
            // Gaps and clumps rather than an even wall.
            if (Mathf.Sin(p.X * 0.21f + 1.3f) * Mathf.Cos(p.Z * 0.17f) > 0.1f && rng.Randf() < 0.85f) continue;
            float s = rng.RandfRange(0.5f, 0.95f);
            if (Clear(p)) spots.Add((p, s, rng.Randf() < 0.22f));
        }
        // Copses out in the fields.
        for (int c = 0; c < 14; c++)
        {
            var centre = new Vector3(rng.RandfRange(-30, W + 30), 0, rng.RandfRange(-26, H + 26));
            int n = rng.RandiRange(4, 12);
            for (int i = 0; i < n; i++)
            {
                var p = centre + new Vector3(rng.RandfRange(-2.5f, 2.5f), 0, rng.RandfRange(-2.5f, 2.5f));
                float s = rng.RandfRange(0.6f, 1.1f);
                if (Clear(p)) spots.Add((p, s, rng.Randf() < 0.3f));
            }
        }

        var leaf = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.95f };
        var bark = new StandardMaterial3D { AlbedoColor = new Color("3e3024"), Roughness = 0.95f };
        MultiMesh Make(Mesh mesh, Material mat, List<(Transform3D t, Color c)> xs)
        {
            mesh.SurfaceSetMaterial(0, mat);
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = xs.Count };
            for (int i = 0; i < xs.Count; i++) { mm.SetInstanceTransform(i, xs[i].t); mm.SetInstanceColor(i, xs[i].c); }
            return mm;
        }
        var trunks = new List<(Transform3D, Color)>();
        var crowns = new List<(Transform3D, Color)>();
        var cones = new List<(Transform3D, Color)>();
        foreach (var (p, s, conifer) in spots)
        {
            float tint = rng.Randf();
            var baseCol = Gfx.TreeDark.Lerp(new Color("4a5a30"), tint * 0.5f).Lerp(new Color("5a4a2a"), rng.Randf() < 0.12f ? 0.4f : 0f);
            trunks.Add((new Transform3D(Basis.Identity.Scaled(new Vector3(s, s, s)), p + new Vector3(0, 0.35f * s, 0)), Colors.White));
            if (conifer)
            {
                for (int k = 0; k < 3; k++)
                    cones.Add((new Transform3D(Basis.Identity.Scaled(new Vector3(s * (1.0f - k * 0.25f), s, s * (1.0f - k * 0.25f))),
                                p + new Vector3(0, s * (0.85f + k * 0.55f), 0)), Gfx.TreeDark.Darkened(0.1f + k * 0.04f)));
                continue;
            }
            crowns.Add((new Transform3D(Basis.Identity.Scaled(new Vector3(s, s * 0.9f, s)), p + new Vector3(0, 0.75f + s * 0.8f, 0)), baseCol));
            crowns.Add((new Transform3D(Basis.Identity.Scaled(Vector3.One * s * 0.72f), p + new Vector3(-0.2f * s, 0.95f + s * 1.15f, 0.15f * s)), baseCol.Lightened(0.08f)));
            crowns.Add((new Transform3D(Basis.Identity.Scaled(Vector3.One * s * 0.45f), p + new Vector3(-0.25f * s, 1.15f + s * 1.45f, 0.25f * s)), baseCol.Lightened(0.16f)));
        }
        root.AddChild(new MultiMeshInstance3D { Multimesh = Make(new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.11f, Height = 0.7f, RadialSegments = 6, Rings = 1 }, bark, trunks) });
        root.AddChild(new MultiMeshInstance3D { Multimesh = Make(new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 6 }, leaf, crowns) });
        root.AddChild(new MultiMeshInstance3D { Multimesh = Make(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.95f, Height = 0.9f, RadialSegments = 8, Rings = 1 }, leaf, cones) });
        return root;
    }
}
