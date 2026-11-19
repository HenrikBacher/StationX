using Godot;

namespace StationX.View;

/// <summary>
/// The park's ground: the painted layout (mown stripes, worn patches, paths, puddles, plots) with photographed grass,
/// gravel and trodden mud laid over it (Poly Haven, CC0). Each photo is divided by its own average colour, so it adds
/// blades, stones and ruts without shifting the painted palette.
/// </summary>
public static class Ground
{
    const string Code = @"
shader_type spatial;
uniform sampler2D paint : source_color, filter_linear_mipmap_anisotropic;
// r gravel, g earth, b hollows (puddles after rain), a grass; whatever is left over stays plain paint
uniform sampler2D mask : filter_linear_mipmap;
uniform sampler2D grass_a : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_n : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_r : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_a : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_n : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_r : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D earth_a : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D earth_n : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D earth_r : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 grass_mean = vec3(0.5);
uniform vec3 gravel_mean = vec3(0.5);
uniform vec3 earth_mean = vec3(0.5);
uniform vec3 repeat_tiles = vec3(1.6, 1.1, 1.8);   // tiles one photo spans: grass, gravel, earth
uniform float strength = 0.85;
uniform float wet = 0.0;   // 0 dry .. 1 soaked

varying vec2 world;
void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz; }

// The photo relative to its own (linear) average, so it modulates the paint rather than replacing it.
vec3 rel(sampler2D t, vec2 uv, vec3 mean) {
    return clamp(texture(t, uv).rgb / max(mean, vec3(0.01)), 0.0, 2.5);
}

void fragment() {
    vec4 w = texture(mask, UV);
    float plain = clamp(1.0 - w.r - w.g - w.a, 0.0, 1.0);
    // A second, rotated sample of the grass at a larger scale breaks up the repeat seen from high up.
    vec2 ug = world / repeat_tiles.x;
    vec2 ug2 = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * world / (repeat_tiles.x * 2.7) + vec2(0.37, 0.71);
    vec2 uv = world / repeat_tiles.y, ue = world / repeat_tiles.z;
    vec3 d = rel(grass_a, ug, grass_mean) * mix(1.0, rel(grass_a, ug2, grass_mean).g, 0.35) * w.a
        + rel(gravel_a, uv, gravel_mean) * w.r + rel(earth_a, ue, earth_mean) * w.g + vec3(plain);
    vec3 col = texture(paint, UV).rgb * mix(vec3(1.0), d, strength);
    vec3 n = texture(grass_n, ug).rgb * w.a + texture(gravel_n, uv).rgb * w.r + texture(earth_n, ue).rgb * w.g
        + vec3(0.5, 0.5, 1.0) * plain;
    float r = texture(grass_r, ug).r * w.a + texture(gravel_r, uv).r * w.r + texture(earth_r, ue).r * w.g + 0.97 * plain;
    // Wet ground is darker and has a sheen; puddles fill the hollows: dark, glassy, flat.
    float pud = smoothstep(0.15, 0.6, w.b) * wet;
    col *= 1.0 - 0.22 * wet;
    col = mix(col, vec3(0.035, 0.04, 0.045), pud * 0.75);
    ALBEDO = col;
    NORMAL_MAP = mix(n, vec3(0.5, 0.5, 1.0), pud);
    ROUGHNESS = mix(clamp(r, 0.55, 1.0) - 0.2 * wet, 0.06, pud);
    SPECULAR = 0.4;
}";

    static Shader? _shader;
    static Texture2D? Load(string dir, string map)
    {
        string path = $"res://assets/textures/{dir}/{map}.jpg";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    /// <summary>The ground material, or null if a photo set is missing (the caller then keeps the plain paint).</summary>
    public static ShaderMaterial? Make(Texture2D paint, Texture2D mask)
    {
        if (!Textures.UsePhotoMaterials) return null;
        var sets = new[] { ("grass", "sparse_grass"), ("gravel", "gravel_floor_02"), ("earth", "brown_mud_02") };
        var m = new ShaderMaterial { Shader = _shader ??= new Shader { Code = Code } };
        foreach (var (key, dir) in sets)
            foreach (var (suffix, map) in new[] { ("a", "albedo"), ("n", "normal"), ("r", "rough") })
            {
                if (Load(dir, map) is not { } t) return null;
                m.SetShaderParameter($"{key}_{suffix}", t);
                if (suffix == "a") m.SetShaderParameter($"{key}_mean", Textures.LinearMean(dir, t));
            }
        m.SetShaderParameter("paint", paint);
        m.SetShaderParameter("mask", mask);
        return m;
    }
}
