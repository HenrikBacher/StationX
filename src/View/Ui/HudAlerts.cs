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

    sealed record Alert(string Text, Color Color, Action Go, bool Pulse = false);
    readonly List<Button> _pulsing = new();

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
            list.Add(new Alert($"MOST IMMEDIATE from the {Defs.IssuerNames[f.Issuer]}: answer within {Hours(Math.Max(0, f.OfferExpires - S.Now))}",
                Gfx.Bad, () => ShowFlash(f), Pulse: true));
        foreach (var st in S.Stations.Where(s => s.Wears && s.IsDown(S.Now) && !s.Servicing))
            Station(st, $"{st.Def.Name} #{st.Id} broken down: fitters due {(int)(st.DownUntil % Defs.MinutesPerDay / 60):00}:{(int)(st.DownUntil % 60):00}", Gfx.Bad);
        // A request that will be missed unless its paper goes first.
        foreach (var c in S.Contracts.Where(c => c.State == ContractState.Active && !c.Standing && c.Quantity > c.Delivered && c.Due - S.Now < 6 * 60))
            list.Add(new Alert($"{c.Title} due in {Hours(c.Due - S.Now)}, {c.Quantity - c.Delivered} to go" + (c.Priority ? "" : ": put it first"),
                Gfx.Bad, () => OpenPanel("requests")));
        if (S.Operations.Count(o => o.AwaitingUse) is int dec and > 0)
            list.Add(new Alert($"{dec} operation{(dec == 1 ? "" : "s")} won: act, cover or hold back", Gfx.Warn, () => OpenPanel("war")));
        if (S.PowerCut)
            list.Add(new Alert(S.Generator ? $"Power cut: only the Bombes run, on the generator ({Math.Max(1, (int)((S.PowerCutUntil - S.Now) / 60 + 0.5))} h)"
                                           : $"Power cut: the machines have stopped ({Math.Max(1, (int)((S.PowerCutUntil - S.Now) / 60 + 0.5))} h)", Gfx.Bad, () => { }));
        if (S.Overtime)
            list.Add(new Alert($"Leave cancelled: overtime, work +25%, people tire faster ({Math.Max(1, (int)((S.OvertimeUntil - S.Now) / 60 + 0.5))} h)", Gfx.Warn, () => { }));
        if (S.RaidOn)
            list.Add(new Alert($"Air raid: desk work at half pace until the all-clear ({Math.Max(1, (int)((S.RaidUntil - S.Now) / 60 + 0.5))} h)", Gfx.Bad, () => { }));
        foreach (var (issuer, days) in S.NeglectedMinistries())
            list.Add(new Alert($"{Defs.IssuerNames[issuer]}: nothing from us for {days:0} days. Take one of its requests", days >= Defs.NeglectDays - 2 ? Gfx.Bad : Gfx.Warn, () => OpenPanel("requests")));
        // Decrypts for Hut 3's card index, with no index to read them against.
        if (S.Messages.Values.Count(m => m.Next == StationType.Index) is int unindexed and > 0 && !S.Stations.Any(s => s.Type == StationType.Index))
            list.Add(new Alert($"{unindexed} decrypt{(unindexed == 1 ? "" : "s")} waiting for a card index: build one (Hut 3, Hut 8 or the Mansion)", Gfx.Bad, () => OpenPanel("build")));
        if (S.WaitingForTea >= 2)
            list.Add(new Alert($"{S.WaitingForTea} people due a break with nowhere to sit: build a canteen table", Gfx.Warn, () => OpenPanel("build")));
        foreach (var n in S.CoveredNets().Where(n => !S.Searching(n) && S.CoverShare(n) < 1
                     && S.Contracts.Any(c => c.Net == n && c.State == ContractState.Active)))
            list.Add(new Alert($"{Defs.NetOf(n).Name} has {S.CoverOf(n)} of {Defs.SetsWanted} receiver sets: add one (Y-stations)", Gfx.Warn, () => OpenPanel("ystations")));
        if (S.SickCount >= 3)
            list.Add(new Alert($"{S.SickCount} staff off sick" + (S.Stations.Any(s => s.Type == StationType.SickBay) || !S.StationUnlocked(StationType.SickBay, out _) ? "" : ": a sick bay in the Mansion gets them back sooner"), Gfx.Warn, () => OpenPanel("staff")));
        if (S.Overcrowded > 0)
            list.Add(new Alert($"{S.Overcrowded} staff lodged far away: requisition billets", Gfx.Warn, () => OpenPanel("staff")));
        if (S.OutOfStores)
            list.Add(new Alert(S.StoresOnOrder ? "Out of stores: desks at half speed until the lorry comes" : "Out of stores: desks at half speed. Order more", Gfx.Bad, () => OpenPanel("staff")));
        else if (S.StoresLow && S.StoresDaysLeft < 1)
            list.Add(new Alert("Stores nearly gone: order more", Gfx.Warn, () => OpenPanel("staff")));
        if (S.Funds < 0)
            list.Add(new Alert("Overdrawn: reputation falls every day until you're back in credit", Gfx.Bad, () => OpenPanel("staff")));
        foreach (var st in S.Stations.Where(s => s.Wears && !s.IsDown(S.Now) && s.Wear > 0.7))
            Station(st, $"{st.Def.Name} #{st.Id} badly worn: service it", Gfx.Warn);
        if (S.ColdZones() is { } cold)
            foreach (var z in S.Grid.Zones.Where(z => cold.Contains(z.Id) && S.Stations.Any(s => z.InOuter(s.X, s.Y))))
                list.Add(new Alert($"{z.Name} is freezing: build a coke stove", Gfx.Warn,
                    () => { _main.CenterOnTile(z.X + z.W / 2f, z.Y + z.H / 2f); ShowZone(z); }));
        if (S.HotZones() is { } hot)
            foreach (var z in S.Grid.Zones.Where(z => hot.Contains(z.Id)))
                list.Add(new Alert($"{z.Name} is sweltering with its Bombes running: build an extractor fan", Gfx.Warn,
                    () => { _main.CenterOnTile(z.X + z.W / 2f, z.Y + z.H / 2f); ShowZone(z); }));
        foreach (var st in S.Stations.Where(s => s.Def.Operator is Role && !s.Obsolete && s.Input.Count > 0 && !S.CanBeStaffed(s)))
            Station(st, $"Nobody can work the {st.Def.Name.ToLowerInvariant()}: hire a {RoleWord(st.Def.Operator!.Value)}", Gfx.Warn);
        // What the workload record shows has held for most of the last day (not a passing moment).
        foreach (var f in S.Utilisation().Lasting)
        {
            // With nobody of that role at all, the line above already says to hire one.
            if (f.Kind == "nobody" && f.Role is Role ro && !S.Staff.Any(p => p.Role == ro && !p.Leaving)) continue;
            list.Add(new Alert(f.Text, Gfx.Warn, () => OpenPanel(f.Kind == "stuck" ? "build" : "staff")));
        }
        return list;
    }

    void RefreshAlerts()
    {
        var alerts = CurrentAlerts();
        // Unanswered MOST IMMEDIATE requests throb, so they can't sit unnoticed in the list.
        float throb = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin((float)Time.GetTicksMsec() / 1000f * 3.2f));
        foreach (var b in _pulsing) if (IsInstanceValid(b)) b.Modulate = new Color(1, 1, 1, throb);
        string sig = string.Join("|", alerts.Select(a => a.Text));
        if (sig == _alertSig) return;
        _alertSig = sig;
        _pulsing.Clear();
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
            if (a.Pulse)
            {
                b.Text = "▶ " + a.Text;
                b.AddThemeFontOverride("font", Fonts.BodyBold);
                b.AddThemeFontSizeOverride("font_size", 13);
                _pulsing.Add(b);
            }
            _alertBox.AddChild(b);
        }
        if (alerts.Count > 6) _alertBox.AddChild(UiTheme.Label($"…and {alerts.Count - 6} more", 11, UiTheme.TextDim));
    }
}
