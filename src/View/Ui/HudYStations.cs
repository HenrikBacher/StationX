using System;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>The Y-stations panel: receiver sets across the networks, riders or teleprinter lines, direction-finding.</summary>
public partial class Hud
{
    string YSig() =>
        $"{S.ReceiverSets}|{S.SpareSets}|{S.Funds / 50}|{S.ActIndex}|{string.Join(",", S.Lines)}|{string.Join(",", S.LinesLaying.Keys.Select(k => k + (int)S.LineHoursLeft(k)))}|{S.Stations.Count(x => x.Type == StationType.Inbox)}|{string.Join(",", S.DirectionFinding)}|" +
        string.Join(",", S.CoveredNets().Select(n => $"{n}{S.CoverOf(n)}{S.Searching(n)}{(S.Searching(n) ? (int)((S.SearchUntil[n] - S.Now) / 60) : 0)}"));

    void YStationsPanel()
    {
        _sideBox.AddChild(UiTheme.Heading("Y-stations"));
        Intro("Y-stations", "The listening stations take down German traffic for the Park. Each network needs " +
                                        $"{Defs.SetsWanted} receiver sets; with fewer it comes in late and garbled, and an extra set copies it cleaner. " +
                                        "Spare sets search the band when a network changes its frequencies.");

        var count = UiTheme.Label($"Receiver sets: {S.ReceiverSets}, {S.SpareSets} spare", 14, UiTheme.Text, Fonts.BodyBold);
        count.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        string? buy = S.CanBuySet();
        _sideBox.AddChild(Row(count, UiTheme.Button($"Buy a set (£{Defs.SetCost})", () => { S.BuySet(); _sideSig = ""; }, buy == null, tooltip: buy ?? "One more receiver, with operators trained to work it.")));

        _sideBox.AddChild(UiTheme.Caps($"Receiver sets on each network · {Defs.SetsWanted} wanted", 12, UiTheme.Accent));
        foreach (var n in S.CoveredNets())
        {
            var net = n;
            var name = UiTheme.Label(Defs.NetOf(n).Name, 13, UiTheme.Text, Fonts.BodyBold);
            name.CustomMinimumSize = new Vector2(96, 0);
            var (state, colour) = S.Searching(n)
                ? ($"lost: searching, ~{Math.Max(1, (int)Math.Ceiling((S.SearchUntil[n] - S.Now) / 60))} h", Gfx.Bad)
                : S.CoverShare(n) switch
                {
                    >= 1.5 => ("clean copy", Gfx.Good),
                    >= 1 => ("covered", UiTheme.TextDim),
                    >= 0.5 => ("thin: late, garbled", Gfx.Warn),
                    _ => ("no cover: hours late", Gfx.Bad),
                };
            var st = UiTheme.Label(state, 12, colour);
            st.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var minus = UiTheme.Button("−", () => { S.MoveSet(net, -1); _sideSig = ""; }, S.CanMoveSet(n, -1) == null, tooltip: "Take a set off this network");
            var num = UiTheme.Label($"{S.CoverOf(n)}", 14, UiTheme.Text, Fonts.BodyBold);
            num.CustomMinimumSize = new Vector2(18, 0);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            var plus = UiTheme.Button("+", () => { S.MoveSet(net, 1); _sideSig = ""; }, S.CanMoveSet(n, 1) == null, tooltip: "Put a spare set on this network");
            _sideBox.AddChild(Row(name, st, minus, num, plus));
        }

        _sideBox.AddChild(UiTheme.Caps("Links to the Park", 12, UiTheme.Accent));
        _sideBox.AddChild(UiTheme.Label($"Dispatch riders leave every {Defs.RiderEvery / 60:0} hours with a bundle; a teleprinter line brings each message within minutes. " +
                                        "Intelligence goes stale on the road.", 12, UiTheme.TextDim));
        foreach (var y in Defs.YStations.Where(y => y.Nets.Any(n => S.CoveredNets().Contains(n))))
        {
            var id = y.Id;
            var name = UiTheme.Label(y.Name, 13, UiTheme.Text);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            string? why = S.CanLayLine(id);
            bool printer = S.Stations.Any(x => x.Type == StationType.Inbox && !x.PendingRemoval);
            _sideBox.AddChild(Row(name, S.Lines.Contains(id)
                ? UiTheme.Label(printer ? $"teleprinter line · £{Defs.LineRent} a week" : "line laid: build a teleprinter", 12, printer ? Gfx.Good : Gfx.Warn)
                : S.LinesLaying.ContainsKey(id)
                ? UiTheme.Label($"Post Office laying it: {Math.Ceiling(S.LineHoursLeft(id))} h", 12, UiTheme.Accent)
                : UiTheme.Button($"Lay a line (£{Defs.LineCost})", () => { S.LayLine(id); _sideSig = ""; }, why == null,
                    tooltip: why ?? $"The Post Office lays a teleprinter line from this station in about {Defs.LineLayDays:0} days, then rents it at £{Defs.LineRent} a week. Its traffic prints on a teleprinter in the Park, so build one too.")));
        }

        var dfs = Defs.YStations.Where(y => y.Df is Service sv && S.ServiceInPlay(sv) && S.CanBuyDf(sv) != "Not available yet").ToList();
        if (dfs.Count > 0)
        {
            _sideBox.AddChild(UiTheme.Caps("Direction-finding", 12, UiTheme.Accent));
            _sideBox.AddChild(UiTheme.Label("Stations that take bearings on a transmitter fix where it is. A cover story can then point to the fix: " +
                                            "cover choices draw half the suspicion for that service.", 12, UiTheme.TextDim));
            foreach (var y in dfs)
            {
                var sv = y.Df!.Value;
                var name = UiTheme.Label(Defs.ServiceNames[sv], 13, UiTheme.Text);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                string? why = S.CanBuyDf(sv);
                _sideBox.AddChild(Row(name, S.DirectionFinding.Contains(sv)
                    ? UiTheme.Label("fixing transmitters", 12, Gfx.Good)
                    : UiTheme.Button($"Set up (£{Defs.DfCost})", () => { S.BuyDf(sv); _sideSig = ""; }, why == null, tooltip: why ?? $"Direction-finding on the {Defs.ServiceNames[sv]}'s transmitters.")));
            }
        }
    }
}
