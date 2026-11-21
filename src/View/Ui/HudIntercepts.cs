using System;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>The intercept reader: every message on the floor and the last few delivered, as Y-station form, decrypt and translation.</summary>
public partial class Hud
{
    const int LiveShown = 14, ArchiveShown = 12;

    string InterceptsSig() =>
        string.Join(",", S.Messages.Values.OrderByDescending(m => m.Created).Take(LiveShown)
                          .Select(m => $"{m.Id}{(int)Intercepts.StageOf(m)}{m.Garbled}")) + $"|{S.Archive.Count}|{S.Archive.LastOrDefault()?.Id}";

    void InterceptsPanel()
    {
        _sideBox.AddChild(UiTheme.Heading("Intercepts"));
        _sideBox.AddChild(UiTheme.Label("Every message on the floor, and the last few delivered. Open one to read the intercept, " +
                                        "and the German and the English once the huts have got that far.", 13, UiTheme.TextDim));

        _sideBox.AddChild(UiTheme.Caps("On the floor", 12, UiTheme.Accent));
        var live = S.Messages.Values.OrderByDescending(m => m.Created).Take(LiveShown).ToList();
        if (live.Count == 0) _sideBox.AddChild(UiTheme.Label("Nothing in the building.", 13, UiTheme.TextDim));
        foreach (var m in live)
        {
            var stage = Intercepts.StageOf(m);
            string state = m.Garbled ? "garbled" : m.HoldUntil >= 0 ? "re-sending" : stage switch
            {
                ReadStage.Translated => "translated",
                ReadStage.Decrypted => "decrypted",
                _ => m.Next is StationType next ? "to " + Defs.Station(next).Name.ToLowerInvariant() : "awaiting key",
            };
            InterceptRow(m.Net, m.Day, m.Created, state, () => ShowIntercept(S.Intercept(m), stage, m.Net, m.Day, m.Garbled, m.Next));
        }

        _sideBox.AddChild(UiTheme.Caps("Delivered", 12, UiTheme.Accent));
        if (S.Archive.Count == 0) _sideBox.AddChild(UiTheme.Label("None yet.", 13, UiTheme.TextDim));
        foreach (var a in Enumerable.Reverse(S.Archive).Take(ArchiveShown).ToList())
            InterceptRow(a.Net, a.Day, a.Created, "sent", () => ShowIntercept(S.Intercept(a), ReadStage.Translated, a.Net, a.Day, false, null));
    }

    void InterceptRow(Net net, int day, double created, string state, Action open)
    {
        int min = (int)(created % Defs.MinutesPerDay);
        var date = S.DateOf(day);
        var b = UiTheme.Button($"{date.Day}.{date.Month}  {min / 60:00}{min % 60:00}   {Defs.NetOf(net).Name}", open);
        b.Alignment = HorizontalAlignment.Left;
        b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        b.AddThemeColorOverride("font_color", Gfx.NetColor(net).Lightened(0.35f));
        var tag = UiTheme.Label(state, 12, state is "garbled" or "awaiting key" ? Gfx.Warn : UiTheme.TextDim, wrap: false);
        tag.CustomMinimumSize = new Vector2(84, 0);
        tag.TooltipText = state.StartsWith("to ") ? "Still in cipher, on its way to this desk next" : "";
        tag.MouseFilter = Control.MouseFilterEnum.Pass;
        tag.HorizontalAlignment = HorizontalAlignment.Right;
        _sideBox.AddChild(Row(b, tag));
    }

    void ShowIntercept(InterceptText t, ReadStage stage, Net net, int day, bool garbled, StationType? next)
    {
        var v = Paper(880);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(840, 520), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(body);
        var netDef = Defs.NetOf(net);

        void Section(string title)
        {
            body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
            body.AddChild(Ink(title, 13, Fonts.BodyBold, UiTheme.Stamp, wrap: false));
        }
        Label Pending(string text) { var l = Ink(text, 14, Fonts.Body, UiTheme.PaperInk.Lightened(0.35f)); body.AddChild(l); return l; }

        Section($"1. Y-STATION LOG SHEET  ·  {netDef.Name.ToUpperInvariant()}");
        body.AddChild(Ink(t.Log, 14));
        body.AddChild(Ink(t.Preamble, 14));
        body.AddChild(Ink(t.Cipher, 16));
        if (garbled) body.AddChild(Ink("Groups corrupt in reception. A clean copy has been asked for from the station.", 13, Fonts.Body, UiTheme.Stamp));

        string machine = netDef.Family switch { Family.Hand => "HAND CIPHER DESK", Family.Lorenz => "TUNNY MACHINE", _ => "TYPEX" };
        Section($"2. DECRYPT  ·  {machine} OUTPUT");
        if (stage >= ReadStage.Decrypted)
        {
            if (netDef.Family != Family.Lorenz) body.AddChild(Ink(t.DecryptRaw, 16));
            body.AddChild(Ink("Read as:  " + t.German, 14, Fonts.Typewriter, UiTheme.PaperInk.Lightened(0.15f)));
        }
        else if (netDef.Family == Family.Hand) Pending("Not yet worked. It waits for a cryptanalyst at the hand-cipher desk.");
        else if (next == StationType.Registration) Pending("Not yet registered: nobody has logged its callsigns and identified the key yet.");
        else if (S.KeyBroken(net, day)) Pending($"Today's {netDef.Name} key is broken. It waits for a {(netDef.Family == Family.Lorenz ? "Tunny machine" : "Typex")} to set up and type it through.");
        else Pending($"Unreadable until today's {netDef.Name} key is broken.");

        Section($"3. TRANSLATION  ·  {t.Serial}");
        if (stage >= ReadStage.Translated) body.AddChild(Ink(t.English, 16, Fonts.Body));
        else if (stage == ReadStage.Decrypted) Pending("Decrypted, waiting for a linguist.");
        else Pending("Nothing to translate yet.");

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(PaperButton("File it", CloseModal));
        v.AddChild(row);
    }
}
