using Godot;

namespace StationX.View;

/// <summary>
/// Light from a photographed overcast sky (Poly Haven HDRI, CC0, see CREDITS.md). Ambient light then comes from above
/// brighter than from the grass below, so roofs and floors catch the sky while walls and undersides fall into shade, and
/// glass, brass and polished floors reflect clouds. The photo is tinted by the hour and the weather. The camera never
/// sees it: the backdrop stays a plain colour.
/// </summary>
public static class SkyLight
{
    const string PanoramaPath = "res://assets/hdri/lythwood_field_1k.hdr";

    // The photo's sky is ~3.3 overhead and ~1.4 low down; scaled so a floor facing straight up gets about `tint`.
    // `ground` lifts the field below the horizon (dark, saturated grass in the photo) towards the park's own ground.
    const string Code = @"
shader_type sky;
uniform sampler2D panorama : filter_linear_mipmap, repeat_enable;
uniform vec3 tint = vec3(1.0);
uniform vec3 ground = vec3(0.3, 0.29, 0.24);
void sky() {
    vec3 c = texture(panorama, SKY_COORDS).rgb * 0.4;
    float below = smoothstep(0.02, -0.08, EYEDIR.y);
    COLOR = mix(c, ground + c * 0.5, below) * tint;
}";

    /// <summary>Against a flat ambient of the same colour and energy.</summary>
    const float Strength = 1f;

    static ShaderMaterial? _mat;
    static Color _tint = new(-1, -1, -1);

    /// <summary>The sky for the environment, or null if the photo is missing (then the ambient light stays a flat colour).</summary>
    public static Sky? Make()
    {
        if (!ResourceLoader.Exists(PanoramaPath)) return null;
        _mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
        _mat.SetShaderParameter("panorama", GD.Load<Texture2D>(PanoramaPath));
        // Small radiance map, recomputed over several frames when the tint changes: blurry overcast reflections
        // don't need more, and laptops' integrated GPUs must cope.
        return new Sky
        {
            SkyMaterial = _mat,
            RadianceSize = Sky.RadianceSizeEnum.Size128,
            ProcessMode = Sky.ProcessModeEnum.Incremental,
        };
    }

    /// <summary>
    /// Tints the sky to the ambient light (an sRGB colour and energy, as the environment's flat ambient would be:
    /// Godot ignores the ambient energy once the light comes wholly from the sky). Each change recomputes the radiance
    /// map, so small drifts (the hour creeping on) are ignored until they add up to a visible step.
    /// </summary>
    public static void Set(Color color, float energy)
    {
        if (_mat == null) return;
        var lin = color.SrgbToLinear() * energy * Strength;   // shader uniforms aren't converted from sRGB
        if (Mathf.Abs(lin.R - _tint.R) + Mathf.Abs(lin.G - _tint.G) + Mathf.Abs(lin.B - _tint.B) < 0.004f) return;
        _tint = lin;
        _mat.SetShaderParameter("tint", new Vector3(lin.R, lin.G, lin.B));
    }
}
