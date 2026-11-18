using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>A short list of things that need attention now. Clicking one takes you to it.</summary>
public partial class Hud
{
    VBoxContainer _alertBox = null!;
    string _alertSig = "";

    sealed record Alert(string Text, Color Color, Action Go);

    void BuildAlerts(VBoxContainer col)
    {
        var panel = new PanelContainer { Visible = false };
        _alertBox = new VBoxContainer();
        _alertBox.AddThemeConstantOverride("separation", 3);
        panel.AddChild(_alertBox);
        col.AddChild(panel);
    }

    List<Alert> CurrentAlerts()
    {
        var list = new List<Alert>();
        void Station(Station st, string text, Color c) =>
            list.Add(new Alert(text, c, () => { _main.CenterOnTile(st.X + 1, st.Y + 1); _main.Select(st.Id, null); }));

        foreach (var f in S.Contracts.Where(c => c.Flash && c.State == ContractState.Offered))
            list.Add(new Alert($"MOST IMMEDIATE from the {Defs.IssuerNames[f.Issuer]}: answer within {Math.Max(0, (int)((f.OfferExpires - S.Now) / 60))} h",
                Gfx.Bad, () => OpenPanel("requests")));
        foreach (var st in S.Stations.Where(s => s.Wears && s.IsDown(S.Now) && !s.Servicing))
            Station(st, $"{st.Def.Name} #{st.Id} broken down", Gfx.Bad);
        if (S.RaidOn)
            list.Add(new Alert($"Air raid: desk work at half pace until the all-clear ({Math.Max(1, (int)((S.RaidUntil - S.Now) / 60 + 0.5))} h)", Gfx.Bad, () => { }));
        if (S.SickCount >= 3)
            list.Add(new Alert($"{S.SickCount} staff off sick", Gfx.Warn, () => OpenPanel("staff")));
        if (S.Overcrowded > 0)
            list.Add(new Alert($"{S.Overcrowded} staff lodged far away: requisition billets", Gfx.Warn, () => OpenPanel("staff")));
        if (S.Funds < 0)
            list.Add(new Alert("Overdrawn: reputation falls every day until you're back in credit", Gfx.Bad, () => OpenPanel("staff")));
        foreach (var st in S.Stations.Where(s => s.Wears && !s.IsDown(S.Now) && s.Wear > 0.7))
            Station(st, $"{st.Def.Name} #{st.Id} badly worn: service it", Gfx.Warn);
        if (S.ColdZones() is { } cold)
            foreach (var z in S.Grid.Zones.Where(z => cold.Contains(z.Id) && S.Stations.Any(s => z.InOuter(s.X, s.Y))))
                list.Add(new Alert($"{z.Name} is freezing: build a coke stove", Gfx.Warn,
                    () => { _main.CenterOnTile(z.X + z.W / 2f, z.Y + z.H / 2f); ShowZone(z); }));
        foreach (var st in S.Stations.Where(s => s.Def.Operator is Role && !s.Obsolete && s.Input.Count > 0 && !S.CanBeStaffed(s)))
            Station(st, $"Nobody can work the {st.Def.Name.ToLowerInvariant()}: hire a {Defs.Roles[st.Def.Operator!.Value].Name.ToLowerInvariant()}", Gfx.Warn);
        return list;
    }

    void RefreshAlerts()
    {
        var alerts = CurrentAlerts();
        string sig = string.Join("|", alerts.Select(a => a.Text));
        if (sig == _alertSig) return;
        _alertSig = sig;
        Clear(_alertBox);
        _alertBox.GetParent<Control>().Visible = alerts.Count > 0;
        if (alerts.Count == 0) return;
        _alertBox.AddChild(UiTheme.Caps("Needs attention", 11, UiTheme.Accent));
        foreach (var a in alerts.Take(6))
        {
            var b = new Button { Text = "• " + a.Text, Flat = true, FocusMode = Control.FocusModeEnum.None, Alignment = HorizontalAlignment.Left,
                                 AutowrapMode = TextServer.AutowrapMode.WordSmart, ClipText = false };
            b.AddThemeColorOverride("font_color", a.Color);
            b.AddThemeColorOverride("font_hover_color", a.Color.Lightened(0.25f));
            b.AddThemeFontSizeOverride("font_size", 12);
            var go = a.Go;
            b.Pressed += () => go();
            _alertBox.AddChild(b);
        }
        if (alerts.Count > 6) _alertBox.AddChild(UiTheme.Label($"…and {alerts.Count - 6} more", 11, UiTheme.TextDim));
    }
}
