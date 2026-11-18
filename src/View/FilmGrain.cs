using Godot;

namespace StationX.View;

/// <summary>A newsreel finish over the park (not the HUD): vignette, faint grain and a warm, desaturated grade.</summary>
public partial class FilmGrain : CanvasLayer
{
    ColorRect _rect = null!;

    const string Code = @"
shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_linear;
uniform float grain = 0.045;
uniform float vignette = 0.42;
uniform float desat = 0.22;

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

void fragment() {
    vec3 c = texture(screen, SCREEN_UV).rgb;
    float l = dot(c, vec3(0.299, 0.587, 0.114));
    c = mix(c, vec3(l), desat);
    c *= vec3(1.04, 1.0, 0.9);                       // a faint sepia cast
    c = (c - 0.5) * 1.06 + 0.5;                      // a touch more contrast
    vec2 uv = SCREEN_UV - 0.5;
    float v = smoothstep(0.85, 0.2, length(uv * vec2(1.0, 0.8)));
    c *= mix(1.0 - vignette, 1.0, v);
    float n = hash(floor(FRAGCOORD.xy / 1.5) + fract(TIME * 7.3) * 113.0) - 0.5;
    c += n * grain;
    COLOR = vec4(c, 1.0);
}";

    public override void _Ready()
    {
        Layer = -1;
        _rect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _rect.Material = new ShaderMaterial { Shader = new Shader { Code = Code } };
        AddChild(_rect);
    }

    public override void _Process(double delta) => _rect.Visible = Settings.FilmGrain;
}
