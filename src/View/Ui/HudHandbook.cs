using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>The handbook panel: how each kind of cipher travels through the Park, desk by desk.</summary>
public partial class Hud
{
    string HandbookSig() =>
        $"{S.ActIndex}|{string.Join(",", S.Researched)}|" + string.Join(",", Defs.Stations.Keys.Select(t => S.HasStation(t) ? 1 : 0));

    void HandbookPanel()
    {
        _sideBox.AddChild(UiTheme.Heading("Handbook"));
        _sideBox.AddChild(UiTheme.Label("How each kind of traffic goes through the Park. Green: you have one. Grey: not available yet. " +
                                        "Steps marked optional help but can be skipped. Hover a step for more.", 12, UiTheme.TextDim));
        foreach (var path in Handbook.Paths)
        {
            var card = Card();
            card.AddChild(UiTheme.Label(path.Title, 15, UiTheme.Text, Fonts.BodyBold));
            var nets = new HFlowContainer();
            nets.AddThemeConstantOverride("h_separation", 8);
            foreach (var n in path.Nets)
                nets.AddChild(UiTheme.Label(Defs.NetOf(n).Name, 11, Gfx.NetColor(n).Lightened(0.15f), Fonts.BodyBold, wrap: false));
            card.AddChild(nets);
            card.AddChild(UiTheme.Label(path.Blurb, 12, UiTheme.TextDim));
            foreach (var route in path.Routes)
            {
                var flow = new HFlowContainer();
                flow.AddThemeConstantOverride("h_separation", 4);
                flow.AddThemeConstantOverride("v_separation", 4);
                for (int i = 0; i < route.Length; i++)
                {
                    if (i > 0) flow.AddChild(UiTheme.Label("→", 13, UiTheme.TextDim, wrap: false));
                    flow.AddChild(StepChip(route[i]));
                }
                card.AddChild(flow);
            }
        }
    }

    Control StepChip(HandbookStep step)
    {
        var def = Defs.Stations[step.Type];
        bool have = S.HasStation(step.Type);
        bool open = S.StationUnlocked(step.Type, out string why);
        // Hut 3's index is skipped until the Battle of Britain, and required for machine-cipher decrypts after it.
        bool index = step.Type == StationType.Index;
        bool required = index && S.ActIndex >= Defs.IndexFromAct;
        bool optional = step.Optional && !required;
        var edge = have ? Gfx.Good : open ? UiTheme.PanelEdge : UiTheme.PanelEdge.Darkened(0.3f);
        var sb = UiTheme.Box(have ? UiTheme.Panel.Lerp(Gfx.Good, 0.12f) : UiTheme.Panel, edge, 3, 6, 3);
        if (optional) { sb.BorderWidthTop = sb.BorderWidthBottom = sb.BorderWidthLeft = sb.BorderWidthRight = 1; sb.BorderColor = edge.Darkened(0.2f); }
        var chip = new PanelContainer();
        chip.AddThemeStyleboxOverride("panel", sb);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 0);
        var col = open ? UiTheme.Text : UiTheme.TextDim.Darkened(0.25f);
        string suffix = index && !required ? " (later)" : optional ? " (optional)" : "";
        v.AddChild(UiTheme.Label(def.Name + suffix, 12, col, Fonts.BodyBold, wrap: false));
        if (def.Operator is Role r)
            v.AddChild(UiTheme.Label(Defs.Roles[r].Name, 10, UiTheme.TextDim, wrap: false));
        chip.AddChild(v);
        var tip = def.Blurb;
        string? note = required ? "Required: these decrypts wait for a card index before despatch" : step.Note;
        if (note != null) tip = $"{char.ToUpperInvariant(note[0])}{note[1..]}.\n\n{tip}";
        if (!open && !string.IsNullOrEmpty(why)) tip += $"\n\nNot yet: {why}";
        chip.TooltipText = tip;
        chip.MouseFilter = Control.MouseFilterEnum.Stop;
        return chip;
    }
}
