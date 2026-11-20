using System;
using System.Collections.Generic;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>
/// The Park after dark. Window glass glows faintly warm where a building has people at work in it, lamplight through
/// blackout curtains, by a small map with one texel per tile that is redrawn only when who is where changes; so every
/// window of the Park stays one merged mesh per wall side. Ceiling lamps burn only in occupied buildings.
/// </summary>
public static class NightLights
{
    const string Code = @"
shader_type spatial;
uniform vec3 albedo : source_color = vec3(0.337, 0.4, 0.42);
uniform sampler2D glow_map : filter_linear;
uniform vec2 grid_size = vec2(64.0, 48.0);
uniform float night = 0.0;
uniform vec3 lamp : source_color = vec3(1.0, 0.62, 0.3);
varying vec2 world;
void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz; }
void fragment() {
    ALBEDO = albedo;
    ROUGHNESS = 0.1;
    METALLIC = 0.2;
    SPECULAR = 0.7;
    float g = texture(glow_map, world / grid_size).r;
    // Heavy curtains: the light shows more at the edges of the pane, where they never quite meet.
    vec2 e = abs(fract(UV * vec2(3.0, 2.0)) - 0.5) * 2.0;   // a box's faces lie 3 by 2 in its UVs
    float edge = smoothstep(0.72, 0.98, max(e.x, e.y));
    EMISSION = lamp * g * night * (0.12 + 1.1 * edge);
}";

    static ShaderMaterial? _glass;
    static Image? _img;
    static ImageTexture? _tex;
    static int _w, _h;
    static readonly Dictionary<string, int> _counts = new();
    static int _signature;

    /// <summary>The window glass of buildings (vehicles keep plain glass).</summary>
    public static ShaderMaterial Glass => _glass ??= new ShaderMaterial { Shader = new Shader { Code = Code } };

    /// <summary>People in the Park now, by building (the view's shared count for windows and lamps).</summary>
    public static IReadOnlyDictionary<string, int> Present => _counts;

    /// <summary>Counts who is in each building and, if that changed, redraws the glow map. Cheap: one pass over the staff.</summary>
    public static void Sync(GameState g, float night)
    {
        Glass.SetShaderParameter("night", night);
        if (night <= 0.01f && _tex != null) return;
        _counts.Clear();
        foreach (var s in g.Staff)
        {
            if (s.Away) continue;
            if (g.Grid.ZoneAt((int)s.X, (int)s.Y) is { } z) _counts[z.Id] = _counts.GetValueOrDefault(z.Id) + 1;
        }
        int signature = 17;
        foreach (var z in g.Grid.Zones)
            signature = signature * 31 + (z.Unlocked ? 1 + Level(_counts.GetValueOrDefault(z.Id)) : 0);
        if (_img != null && signature == _signature && _w == Grid.Width && _h == Grid.Height) return;
        _signature = signature;
        if (_img == null || _w != Grid.Width || _h != Grid.Height)
        {
            _w = Grid.Width;
            _h = Grid.Height;
            _img = Image.CreateEmpty(_w, _h, false, Image.Format.R8);
            _tex = null;
        }
        _img.Fill(Colors.Black);
        foreach (var z in g.Grid.Zones)
        {
            if (!z.Unlocked) continue;
            float v = Level(_counts.GetValueOrDefault(z.Id)) / 4f;
            if (v <= 0) continue;
            // One tile past the walls on every side, so the glass on the boundary samples inside.
            for (int y = Math.Max(0, z.Y - 1); y < Math.Min(_h, z.Y + z.H + 1); y++)
                for (int x = Math.Max(0, z.X - 1); x < Math.Min(_w, z.X + z.W + 1); x++)
                    _img.SetPixel(x, y, new Color(v, 0, 0));
        }
        if (_tex == null)
        {
            _tex = ImageTexture.CreateFromImage(_img);
            Glass.SetShaderParameter("glow_map", _tex);
            Glass.SetShaderParameter("grid_size", new Vector2(_w, _h));
        }
        else _tex.Update(_img);
    }

    /// <summary>How brightly a building shows, in steps (so a person walking in or out doesn't redraw the map): 0 empty .. 4 full.</summary>
    static int Level(int people) => people == 0 ? 0 : people < 3 ? 1 : people < 7 ? 2 : people < 14 ? 3 : 4;
}
