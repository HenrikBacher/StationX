using Godot;

namespace StationX.View;

/// <summary>The lake: wind ripples, rain rings, sky reflection and sun glints; ice in a cold snap.</summary>
public partial class Water : MeshInstance3D
{
    public const float CentreX = 9.5f, CentreZ = 23f, RadiusX = 6.0f, RadiusZ = 4.2f;

    const string Code = @"
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

uniform vec2 centre;
uniform vec2 radius;
uniform vec3 deep : source_color = vec3(0.06, 0.09, 0.08);
uniform vec3 shallow : source_color = vec3(0.19, 0.22, 0.16);
uniform vec3 sun : source_color = vec3(1.0, 0.94, 0.85);
uniform float sunlight = 1.0;
uniform vec3 sky : source_color = vec3(0.66, 0.72, 0.78);
uniform float wind = 0.4;
uniform float rain = 0.0;
uniform float ice = 0.0;

varying vec3 wpos;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
vec2 hash2(vec2 p) { return vec2(hash(p), hash(p + 17.31)); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

float rings(vec2 p, float t) {
    const float cells = 2.2;
    vec2 cell = floor(p * cells);
    float h = 0.0;
    for (int j = -1; j <= 1; j++)
        for (int i = -1; i <= 1; i++) {
            vec2 c = cell + vec2(float(i), float(j));
            vec2 r = hash2(c);
            float ph = fract(t * 0.9 + r.x * 7.0);
            float d = length(p - (c + r) / cells);
            float rad = ph * 0.32;
            float band = d - rad;
            h += cos(band * 70.0) * smoothstep(0.045, 0.0, abs(band)) * (1.0 - ph);
        }
    return h;
}

float height(vec2 p, float t) {
    float h = 0.0;
    h += sin(dot(p, vec2(1.3, 0.7)) * 3.1 + t * 1.2) * 0.35;
    h += sin(dot(p, vec2(-0.6, 1.4)) * 4.7 + t * 1.8) * 0.22;
    h += (noise(p * 5.0 + vec2(t * 0.35, t * 0.2)) - 0.5) * 0.6;
    h *= wind;
    h += rings(p, TIME) * rain * 0.5;
    return h * (1.0 - ice);
}

void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    vec2 q = (wpos.xz - centre) / radius;
    float d = length(q);
    if (d > 0.95) discard;
    vec2 p = wpos.xz;
    float t = TIME;
    const float e = 0.02;
    float h = height(p, t);
    float hx = height(p + vec2(e, 0.0), t);
    float hz = height(p + vec2(0.0, e), t);
    float strength = 0.11;
    vec3 nw = normalize(vec3(-(hx - h) / e * strength, 1.0, -(hz - h) / e * strength));
    NORMAL = normalize((VIEW_MATRIX * vec4(nw, 0.0)).xyz);

    float depth = smoothstep(0.88, 0.25, d);
    vec3 col = mix(shallow, deep, depth);
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    col = mix(col, sky * 0.8, clamp(0.08 + fres * 0.8, 0.0, 0.45));
    // A dark, muddy margin where the water meets the bank.
    col = mix(col, vec3(0.10, 0.09, 0.06), smoothstep(0.8, 0.95, d) * 0.65 * (1.0 - ice));

    // Ice: milky, cracked, with the dark water showing through near the middle.
    // A few long cracks, not a jigsaw: thin iso-lines of two noise octaves, the finer one only in patches.
    float crack = (smoothstep(0.012, 0.0, abs(noise(p * 1.6) - 0.5))
                 + 0.6 * smoothstep(0.01, 0.0, abs(noise(p * 4.1 + 3.1) - 0.5)) * step(0.62, noise(p * 0.8 + 7.0))) * 0.22;
    // Snow drifts settle on the ice towards the banks.
    crack -= smoothstep(0.55, 0.95, d) * 0.12 * (0.5 + noise(p * 2.3));
    vec3 icecol = mix(vec3(0.70, 0.76, 0.80), vec3(0.42, 0.50, 0.55), depth * 0.6 + noise(p * 1.7) * 0.3) - crack;
    col = mix(col, icecol, ice);

    ALBEDO = col;
    // Sun sparkle: an isometric camera never sees the true reflection, so the glints are faked from small wave crests.
    float crest = noise(p * 24.0 + vec2(t * 1.1, -t * 0.7)) * noise(p * 9.0 - vec2(t * 0.5, t * 0.9));
    float glint = pow(smoothstep(0.6, 0.78, crest), 2.0) * sunlight * (0.3 + wind) * (1.0 - ice) * (1.0 - rain * 0.7) * smoothstep(0.85, 0.6, d);
    EMISSION = sun * glint * 1.1;
    ROUGHNESS = mix(0.07 + rain * 0.1, 0.45, ice);
    SPECULAR = 0.6;
    METALLIC = 0.0;
}";

    ShaderMaterial _mat = null!;

    public override void _Ready()
    {
        Mesh = new PlaneMesh { Size = new Vector2(RadiusX * 2, RadiusZ * 2) };
        Position = new Vector3(CentreX, 0.012f, CentreZ);
        CastShadow = ShadowCastingSetting.Off;
        _mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
        _mat.SetShaderParameter("centre", new Vector2(CentreX, CentreZ));
        _mat.SetShaderParameter("radius", new Vector2(RadiusX, RadiusZ));
        MaterialOverride = _mat;
        AddChild(Reeds());
    }

    /// <summary>Clumps of reeds and rushes around the bank.</summary>
    static MultiMeshInstance3D Reeds()
    {
        var blade = new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.03f, Height = 1f, RadialSegments = 4, Rings = 1 };
        blade.Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f };
        var rng = new RandomNumberGenerator { Seed = 1939 };
        var xf = new System.Collections.Generic.List<(Transform3D t, Color c)>();
        for (int clump = 0; clump < 34; clump++)
        {
            float a = rng.RandfRange(0, Mathf.Tau);
            // Thicker on the far (north-west) bank, sparse where the paths come down to the water.
            if (Mathf.Cos(a - 2.4f) < -0.4f && rng.Randf() < 0.6f) continue;
            float r = rng.RandfRange(0.84f, 0.97f);
            var c0 = new Vector3(Mathf.Cos(a) * RadiusX * r, 0, Mathf.Sin(a) * RadiusZ * r);
            int n = rng.RandiRange(6, 16);
            for (int i = 0; i < n; i++)
            {
                var off = new Vector3(rng.RandfRange(-0.28f, 0.28f), 0, rng.RandfRange(-0.28f, 0.28f));
                float h = rng.RandfRange(0.25f, 0.6f);
                var basis = Basis.FromEuler(new Vector3(rng.RandfRange(-0.25f, 0.25f), rng.RandfRange(0, Mathf.Tau), rng.RandfRange(-0.25f, 0.25f)))
                            .Scaled(new Vector3(1, h, 1));
                var pos = c0 + off;
                pos.Y = h / 2f - 0.02f;
                var col = new Color("3d4e27").Lerp(new Color("6b663a"), rng.Randf() * 0.45f).Darkened(rng.Randf() * 0.3f);
                xf.Add((new Transform3D(basis, pos), col));
            }
        }
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = blade, InstanceCount = xf.Count };
        for (int i = 0; i < xf.Count; i++) { mm.SetInstanceTransform(i, xf[i].t); mm.SetInstanceColor(i, xf[i].c); }
        return new MultiMeshInstance3D { Multimesh = mm, Name = "Reeds" };
    }

    /// <summary>wind and rain 0..1; ice 0..1 (a freeze builds with lying snow).</summary>
    public void Set(float wind, float rain, float ice, Color sky, Color sun, float sunlight)
    {
        _mat.SetShaderParameter("sun", sun);
        _mat.SetShaderParameter("sunlight", sunlight);
        _mat.SetShaderParameter("wind", wind);
        _mat.SetShaderParameter("rain", rain);
        _mat.SetShaderParameter("ice", ice);
        _mat.SetShaderParameter("sky", sky);
    }
}
