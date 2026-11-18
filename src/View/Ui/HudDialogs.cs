using System;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

public partial class Hud
{
    Control _modalLayer = null!;
    CenterContainer _modalCenter = null!;

    public bool ModalOpen => _modalLayer != null && _modalLayer.Visible;

    void BuildModalLayer()
    {
        _modalLayer = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _modalLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_modalLayer);
        var dim = new ColorRect { Color = new Color(0.07f, 0.06f, 0.04f, 0.72f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _modalLayer.AddChild(dim);
        _modalCenter = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _modalCenter.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _modalLayer.AddChild(_modalCenter);
    }

    void CloseModal()
    {
        if (_modalLayer == null) return;
        _modalLayer.Visible = false;
        Clear(_modalCenter);
    }

    /// <summary>A sheet of government paper with typed text.</summary>
    VBoxContainer Paper(int width)
    {
        Clear(_modalCenter);
        var paper = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        paper.AddThemeStyleboxOverride("panel", UiTheme.Paper());
        _modalCenter.AddChild(paper);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        paper.AddChild(v);
        _modalLayer.Visible = true;
        return v;
    }

    static Label Ink(string text, int size, Font? font = null, Color? color = null, bool wrap = true)
    {
        var l = UiTheme.Label(text, size, color ?? UiTheme.PaperInk, font ?? Fonts.Typewriter, wrap);
        return l;
    }

    static Button PaperButton(string text, Action onPressed)
    {
        var b = UiTheme.Button(text, onPressed);
        b.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.PaperInk, UiTheme.PaperInk, 2, 18, 8));
        b.AddThemeStyleboxOverride("hover", UiTheme.Box(UiTheme.Stamp, UiTheme.Stamp, 2, 18, 8));
        b.AddThemeStyleboxOverride("pressed", UiTheme.Box(UiTheme.Stamp.Darkened(0.2f), UiTheme.Stamp, 2, 18, 8));
        b.AddThemeColorOverride("font_color", UiTheme.PaperBg);
        b.AddThemeColorOverride("font_hover_color", UiTheme.PaperBg);
        b.AddThemeFontOverride("font", Fonts.BodyBold);
        b.AddThemeFontSizeOverride("font_size", 16);
        return b;
    }

    static Control Letterhead()
    {
        var row = new HBoxContainer();
        var gov = Ink("GOVERNMENT CODE AND CYPHER SCHOOL\nBLETCHLEY PARK, BUCKS.", 12, Fonts.BodyBold, wrap: false);
        gov.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(gov);
        var stamp = new PanelContainer();
        stamp.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0, 0, 0, 0), UiTheme.Stamp, 2, 10, 4));
        var st = (StyleBoxFlat)stamp.GetThemeStylebox("panel");
        st.SetBorderWidthAll(3);
        stamp.AddChild(Ink("MOST SECRET", 18, Fonts.Heading, UiTheme.Stamp, wrap: false));
        stamp.Rotation = -0.06f;
        stamp.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(stamp);
        return row;
    }

    public void ShowTitle()
    {
        _titleOpen = true;
        var v = Paper(760);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        var title = Ink("STATION X", 64, Fonts.Heading);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        v.AddChild(title);
        var sub = Ink("Bletchley Park, 1939 to 1942", 18, Fonts.Heading, UiTheme.Stamp);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        v.AddChild(sub);
        v.AddChild(Ink(
            "You run the floor at the Government Code and Cypher School. Messages arrive from the listening stations. " +
            "Your clerks, cryptanalysts, Wrens and linguists turn them into intelligence, one desk and one machine at a time.\n\n" +
            "Start with pencil and paper against German police ciphers. Finish with four-rotor Bombes against the U-boats' SHARK key, " +
            "and then, if you like, with Colossus against Hitler's own teleprinter cipher.", 15));
        var controls = Ink(
            "Mouse: click to select · right-drag to pan · wheel to zoom\n" +
            "Keys: WASD pan · , . turn the view · Space pause · 1 2 3 speed · R rotate while building · M move · F flow view · Esc menu\n" +
            "B build · H staff · Q requests · T research · G huts · I intelligence · K statistics · F5 quicksave · F8 quickload", 12, Fonts.Body, UiTheme.PaperInk with { A = 0.75f });
        v.AddChild(controls);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(PaperButton("Report for duty", () => { _titleOpen = false; CloseModal(); ShowBriefing(); }));
        row.AddChild(PaperButton("New game…", ShowNewGame));
        if (Saves.List().Any(x => x.info != null)) row.AddChild(PaperButton("Load…", () => ShowSaves(false)));
        row.AddChild(PaperButton("Settings", ShowSettings));
        row.AddChild(PaperButton("Quit", () => GetTree().Quit()));
        v.AddChild(row);
    }

    public void ShowBriefing()
    {
        var act = S.Act;
        var v = Paper(720);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        v.AddChild(Ink($"{act.Date:d MMMM yyyy}", 14));
        v.AddChild(Ink($"From: {act.From}\nTo: Officer in charge, huts and Mansion", 14));
        v.AddChild(Ink($"Chapter {act.Chapter}: {act.ChapterTitle}", 13, Fonts.BodyBold, UiTheme.Stamp));
        v.AddChild(Ink(act.Title, 28, Fonts.Heading));
        v.AddChild(Ink(act.Briefing, 15));
        v.AddChild(Ink("Objectives", 13, Fonts.BodyBold, UiTheme.Stamp));
        foreach (var g in act.Goals) v.AddChild(Ink("—  " + g.Text, 15));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(PaperButton("Understood", CloseModal));
        v.AddChild(row);
    }

    void ShowActComplete()
    {
        var act = S.Act;
        var v = Paper(620);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        v.AddChild(Ink($"{S.Date:d MMMM yyyy}", 14));
        v.AddChild(Ink(act.Title, 28, Fonts.Heading));
        v.AddChild(Ink("Every objective is complete. Whitehall has noticed.", 15));
        v.AddChild(Ink(
            $"Decrypts delivered so far: {S.Stats.DeliveredTotal}\n" +
            $"Requests completed: {S.Stats.ContractsCompleted} · missed: {S.Stats.ContractsFailed}\n" +
            $"Staff: {S.Staff.Count} · funds £{S.Funds:N0}", 14));
        bool last = S.ActIndex + 1 >= Campaign.Acts.Length;
        if (!last)
        {
            var next = Campaign.Acts[S.ActIndex + 1];
            v.AddChild(Ink($"Next: {next.Title}, {next.Date:MMMM yyyy}", 14, Fonts.BodyBold, UiTheme.Stamp));
        }
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(PaperButton(last ? "Read the epilogue" : "Continue", () =>
        {
            CloseModal();
            S.ContinueAfterAct();
            // ContinueAfterAct emits ActStarted or Won, which opens the next sheet.
        }));
        v.AddChild(row);
    }

    void ShowEnd(bool won)
    {
        var v = Paper(720);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        string heading = !won ? "Closed down" : S.ActIndex > Campaign.EnigmaFinale ? "TUNNY" : "SHARK";
        v.AddChild(Ink(heading, 34, Fonts.Heading, won ? UiTheme.PaperInk : UiTheme.Stamp));
        v.AddChild(Ink(S.EndReason ?? "", 15));
        v.AddChild(Ink(
            $"Decrypts delivered: {S.Stats.DeliveredTotal}\n" +
            $"Requests completed: {S.Stats.ContractsCompleted} · missed: {S.Stats.ContractsFailed}\n" +
            $"Staff at the end: {S.Staff.Count}\n" +
            (S.TonnageLost > 0 ? $"Shipping lost during the SHARK blackout: {S.TonnageLost:N0} tons" : ""), 14));
        if (S.Mode == GameMode.Campaign)
        {
            int score = S.Score();
            var (honour, citation) = S.Honour();
            int previous = Records.Submit(S.Difficulty, score);
            string best = score > previous && previous > 0 ? $"A new best on {Defs.Difficulties[S.Difficulty].Name} (was {previous:N0})."
                        : previous > 0 ? $"Best on {Defs.Difficulties[S.Difficulty].Name}: {previous:N0}." : "";
            v.AddChild(new HSeparator());
            v.AddChild(Ink($"Score {score:N0}", 22, Fonts.Heading));
            v.AddChild(Ink(won ? honour : "No honours", 18, Fonts.Heading, UiTheme.Stamp));
            v.AddChild(Ink((won ? citation : "Lost campaigns score half.") + (best != "" ? "  " + best : ""), 13));
            // Where the reputation went: the lesson of a lost campaign, and a footnote to a won one.
            var causes = S.Stats.RepByCause.Where(x => Math.Abs(x.Value) >= 0.5).OrderBy(x => x.Value).ToList();
            if (causes.Count > 0)
                v.AddChild(Ink("Reputation over the war: " + string.Join(" · ", causes.Select(x => $"{x.Key} {(x.Value > 0 ? "+" : "−")}{Math.Abs(x.Value):0}")), 12, Fonts.Body));
        }
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 12);
        if (won && S.CanContinue)
        {
            v.AddChild(Ink("The war isn't over. Tunny, the Lorenz cipher of the German High Command, is still unread. " +
                           "Carry on into 1943 with the Testery, Tutte's method and Colossus.", 14, Fonts.Body, UiTheme.Stamp));
            row.AddChild(PaperButton("Continue to 1943", () => { CloseModal(); S.ContinueIntoTunny(); }));
        }
        row.AddChild(PaperButton("New game…", ShowNewGame));
        row.AddChild(PaperButton("New campaign", () => { CloseModal(); _main.NewGame(Random.Shared.Next(100000), showTitle: false); }));
        row.AddChild(PaperButton("Quit", () => GetTree().Quit()));
        v.AddChild(row);
    }
}
