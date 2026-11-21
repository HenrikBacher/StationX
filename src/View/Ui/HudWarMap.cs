using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>The war map: what each accepted request was really for, and how it went.</summary>
public partial class Hud
{
    string WarMapSig() =>
        string.Join(",", S.Operations.Select(o => $"{o.Id}{(int)o.State}{(int)o.Use}")) + $"|{(int)(S.WarMood * 100)}|{S.CoverLeft}|{S.Policy}";

    /// <summary>What using this operation's decrypts would bring, at a share of the full result.</summary>
    static string OpGain(Operation o, double share) => o.Kind switch
    {
        OpKind.Convoy or OpKind.Fleet => $"about {Math.Round(o.Stake * share):0} ships brought home",
        OpKind.Raid => $"about {Math.Max(1, Math.Round(o.Stake / 6.0 * share)):0} raiders shot down",
        _ => share >= 1 ? "the commanders warned in full" : "the commanders warned, a little late",
    };

    /// <summary>The context for deciding how to use an operation's decrypts: what's at stake, what each choice would do to suspicion, and what else bears on it.</summary>
    IEnumerable<(string, Color)> OpContext(Operation o)
    {
        var sv = Defs.NetOf(o.Net).Service;
        double now = S.SuspicionOf(sv);
        double act = Math.Min(Defs.SuspicionLimit, now + Defs.OpActSuspicion * S.Rules.SuspicionMult);
        double cover = Math.Min(Defs.SuspicionLimit, now + Defs.OpCoverSuspicion * S.CoverSuspicionFactor(sv) * S.Rules.SuspicionMult);
        yield return ($"At stake: act, {OpGain(o, 1)}; with cover, {OpGain(o, 0.75)}; held back, nothing.", UiTheme.Text);
        string sAct = Words.SuspicionLevel(act), sCov = Words.SuspicionLevel(cover);
        yield return ($"The {Defs.ServiceNames[sv]} is {GameState.SuspicionMeans(now)}. Acting raises its suspicion" +
                      (sAct != Words.SuspicionLevel(now) ? $" and would make it {GameState.SuspicionMeans(act)}" : "") +
                      (S.CoverLeft > 0 ? (sCov != Words.SuspicionLevel(now) ? $"; a cover story would make it {sCov}." : "; a cover story hardly moves it.") : ". No cover stories left this week."),
                      act >= Defs.WorryFrom ? Gfx.Bad : act >= Defs.WaryFrom ? Gfx.Warn : UiTheme.TextDim);
        if (S.DirectionFinding.Contains(sv)) yield return ("Direction-finding makes the cover story more believable here.", UiTheme.TextDim);
        if (o.Kind == OpKind.Convoy && S.CipherRead)
            yield return ($"The U-boats may know the route anyway: while they read our convoy cipher, only {Defs.BdienstConvoyShare * 100:0}% of the ships count as saved.", Gfx.Warn);
        if (S.WarMood < 1.08) yield return ("Acting or covering lifts the grant 2% for a week (up to 8%).", UiTheme.TextDim);
    }

    void WarMapPanel()
    {
        _sideBox.AddChild(UiTheme.Heading("The war"));
        Intro("The war", "Every request is for something: a convoy to route round the U-boats, a raid to meet, a battle to warn of. " +
                                        "Deliver in time and it goes our way.");
        var w = S.War;
        _sideBox.AddChild(UiTheme.Label($"Ships brought home {w.ShipsSaved} · sunk {w.ShipsSunk}", 13));
        _sideBox.AddChild(UiTheme.Label($"Raiders shot down {w.BombersDowned} · raids unopposed {w.RaidsUnopposed}", 13));
        _sideBox.AddChild(UiTheme.Label($"Battles warned of {w.BattlesWarned} · surprised {w.BattlesSurprised}", 13));
        double mood = S.WarMood;
        _sideBox.AddChild(UiTheme.Label(mood > 1.001 ? $"Whitehall is pleased with results: the grant is up {Math.Round((mood - 1) * 100)}% this week."
                                        : "Each operation won in the last week lifts the grant 2%, up to 8%.", 12, mood > 1.001 ? Gfx.Good : UiTheme.TextDim));
        _sideBox.AddChild(Row(Spacer(), UiTheme.Button("Spread out the map", ShowWarMap, tooltip: "The whole theatre, with every operation")));

        var waiting = S.Operations.Where(o => o.AwaitingUse).ToList();
        if (waiting.Count > 0)
        {
            _sideBox.AddChild(UiTheme.Caps("Decrypts in hand · how to use them", 12, Gfx.Warn));
            _sideBox.AddChild(UiTheme.Label($"Acting outright makes the enemy wonder how we knew. A cover story (a reconnaissance aircraft sent to be seen first) " +
                                            $"gives most of the result for little risk: {S.CoverLeft} of {Defs.CoverPerWeek} left this week. " +
                                            $"Undecided, your {Defs.Policies[S.Policy].Name.ToLowerInvariant()} policy chooses in {Defs.OpDecideHours:0} hours.", 12, UiTheme.TextDim));
            foreach (var o in waiting)
            {
                var card = new VBoxContainer();
                card.AddChild(UiTheme.Label($"{o.Name} · {o.Place}", 13, UiTheme.Accent));
                card.AddChild(UiTheme.Label(o.Won, 12, UiTheme.TextDim));
                foreach (var (line, col) in OpContext(o)) card.AddChild(UiTheme.Label(line, 12, col));
                int oid = o.Id;
                var act = UiTheme.Button("Act", () => { S.UseOperation(oid, OpUse.Acted); _sideSig = ""; }, tooltip: "Full result, and more suspicion");
                var cover = UiTheme.Button("With cover", () => { S.UseOperation(oid, OpUse.Covered); _sideSig = ""; }, tooltip: "Three-quarters of the result, and a little suspicion");
                cover.Disabled = S.CoverLeft <= 0;
                var hold = UiTheme.Button("Hold back", () => { S.UseOperation(oid, OpUse.Withheld); _sideSig = ""; }, tooltip: "Nothing is done: no result, no risk");
                if (S.CoverLeft <= 0) cover.TooltipText = $"No cover stories left this week ({Defs.CoverPerWeek} a week).";
                var sv = Defs.NetOf(o.Net).Service;
                double sNow = S.SuspicionOf(sv);
                double sAct = Math.Min(Defs.SuspicionLimit, sNow + Defs.OpActSuspicion * S.Rules.SuspicionMult);
                double sCover = Math.Min(Defs.SuspicionLimit, sNow + Defs.OpCoverSuspicion * S.CoverSuspicionFactor(sv) * S.Rules.SuspicionMult);
                act.TooltipText = $"Act: {OpGain(o, 1)}. {char.ToUpper(Words.SuspicionChange(sAct - sNow)[0])}{Words.SuspicionChange(sAct - sNow)[1..]}: the {Defs.ServiceNames[sv]} would be {GameState.SuspicionMeans(sAct)}.";
                if (S.CoverLeft > 0) cover.TooltipText = $"With cover: {OpGain(o, 0.75)}. {char.ToUpper(Words.SuspicionChange(sCover - sNow)[0])}{Words.SuspicionChange(sCover - sNow)[1..]}: the {Defs.ServiceNames[sv]} would be {GameState.SuspicionMeans(sCover)}. Uses one of {S.CoverLeft} cover stories left this week.";
                hold.TooltipText = "Nothing is done, nothing is given away: no result, and no grant from it. Worth it when the service is close to locking us out.";
                card.AddChild(Row(act, cover, hold, Spacer(), UiTheme.Label($"{Math.Max(0, (o.DecideBy - S.Now) / 60):0} h left", 12, UiTheme.TextDim)));
                _sideBox.AddChild(card);
            }
        }

        _sideBox.AddChild(UiTheme.Caps("Operations", 12, UiTheme.Accent));
        if (S.Operations.Count == 0) _sideBox.AddChild(UiTheme.Label("None yet. Accept a request (Q).", 13, UiTheme.TextDim));
        foreach (var o in Enumerable.Reverse(S.Operations).Take(14))
        {
            if (o.AwaitingUse) continue;
            var col = o.State switch { OpState.Won when o.Use == OpUse.Withheld => UiTheme.TextDim, OpState.Won => Gfx.Good, OpState.Lost => Gfx.Bad, _ => UiTheme.Accent };
            var card = new VBoxContainer();
            card.AddChild(UiTheme.Label($"{S.DateOf(o.Day):d MMM}  {o.Name}", 13, col));
            card.AddChild(UiTheme.Label(o.State == OpState.Pending ? $"{o.Place}: in hand" : o.Outcome, 12, UiTheme.TextDim));
            _sideBox.AddChild(card);
        }
    }

    void ShowWarMap()
    {
        var v = Paper(1000);
        v.AddChild(Ink("OPERATIONAL MAP  ·  MOST SECRET", 15, Fonts.Heading));
        var map = new WarMapView { State = S, CustomMinimumSize = new Vector2(970, 560) };
        v.AddChild(map);
        var legend = new HBoxContainer();
        legend.AddThemeConstantOverride("separation", 18);
        legend.AddChild(Ink("● in hand", 13, Fonts.Body, new Color("c8902a"), wrap: false));
        legend.AddChild(Ink("● went our way", 13, Fonts.Body, new Color("4f7a3a"), wrap: false));
        legend.AddChild(Ink("● went badly", 13, Fonts.Body, new Color("a3392b"), wrap: false));
        legend.AddChild(Ink("● held back", 13, Fonts.Body, new Color("8a8270"), wrap: false));
        legend.AddChild(Ink("Hover a pin to read it.", 13, Fonts.Body, UiTheme.PaperInk.Lightened(0.3f), wrap: false));
        v.AddChild(legend);
        v.AddChild(Row(Spacer(), PaperButton("Fold it up", CloseModal)));
    }
}
