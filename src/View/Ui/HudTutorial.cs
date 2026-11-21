using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>
/// The first chapter as a tutorial: one step at a time on a card where the tips usually sit, with the control to use outlined.
/// A step with a condition moves on by itself once it's done (or already was); the rest have a Next button. The game's own
/// tips wait until it's over. "Skip" ends the guidance and the chapter carries on as it is.
/// </summary>
public partial class Hud
{
    /// <summary>A step: what to do, the control to outline (see <see cref="FindTarget"/>), and when it's done (null: press Next).</summary>
    sealed record TutStep(string Id, string Text, Func<string?>? Target, Func<bool>? Done);

    PanelContainer _tut = null!;
    Label _tutHead = null!, _tutText = null!;
    Button _tutNext = null!;
    Panel _tutRing = null!;
    int _tutShown = -1;
    TutStep[]? _tutSteps;

    /// <summary>Whether the tutorial is running now (it covers the first chapter of a campaign).</summary>
    public bool TutorialOn => S.Tutorial && S.Mode == GameMode.Campaign && S.Status == GameStatus.Playing && S.ActIndex <= 1
                              && S.TutorialStep < Steps.Length;

    bool Has(StationType t) => S.Stations.Any(s => s.Type == t);
    int CountOf(Role r) => S.Staff.Count(s => s.Role == r && !s.Leaving);

    /// <summary>"place:X" when the Build panel is open, else the Build button: placing closes the panel again.</summary>
    string PlaceOrBuild(StationType t) => _panel == "build" ? "place:" + t : "tool:build";

    TutStep[] Steps => _tutSteps ??= new TutStep[]
    {
        new("welcome",
            "Welcome to Bletchley Park. This training takes you through the first chapter a step at a time; the outlined control is the one to use. " +
            "Move about by dragging with the right mouse button (or W A S D) and zoom with the wheel. Skip it whenever you like.", null, null),
        new("orders",
            "Each chapter's orders are top right: tick them all off and the next chapter opens. Needs attention, below the keys, lists anything going " +
            "wrong; click a line and it opens what fixes it.", () => "objectives", null),
        new("requests",
            "Nothing comes in until a ministry asks for it. Open Requests (R).",
            () => "tool:requests", () => _panel == "requests" || AnyAccepted()),
        new("accept",
            "Accept the War Office's request (if none is offered yet, one comes within hours). Denmark Hill starts sending police traffic, and the " +
            "first dispatch rider is on the way. Mind the deadline: a late request costs reputation.",
            () => _panel == "requests" ? "accept" : "tool:requests", AnyAccepted),
        new("registration",
            "While the rider comes, set up the desks. Open Build (B), press Place on the registration desk and click inside the Mansion; R turns it. " +
            "Leave clear floor in front for the clerk who works it. Every message is logged here first.",
            () => PlaceOrBuild(StationType.Registration), () => Has(StationType.Registration)),
        new("handcipher",
            "Next a hand-cipher desk, where a cryptanalyst breaks the police cipher with pencil and squared paper.",
            () => PlaceOrBuild(StationType.HandCipher), () => Has(StationType.HandCipher)),
        new("translation",
            "Then a translation desk: a linguist turns each decrypt into English. The despatch box by the Mansion's door sends it to Whitehall.",
            () => PlaceOrBuild(StationType.Translation), () => Has(StationType.Translation)),
        new("canteen",
            "Desk work tires people, and standing about is no rest. A canteen table lets them take a tea break and come back fresh; nobody lasts a " +
            "whole watch without one.",
            () => PlaceOrBuild(StationType.Canteen), () => Has(StationType.Canteen)),
        new("flow",
            "Each message goes registration → hand-cipher desk → translation → despatch box. Press Flow (F) to see the paper's way through the Park " +
            "and where it waits.",
            () => "flow", () => _main.ShowFlows),
        new("inspect",
            "Click a desk or a person to look at them: what they're doing, how tired they are, and which watch and building they're on.",
            null, () => _inspStation != null || _inspStaff != null),
        new("staff",
            "Open Staff (H). Your four people are listed by role, with candidates for hire at the foot and what each costs a week.",
            () => "tool:staff", () => _panel == "staff"),
        new("hire",
            "Hand ciphers are slow work: hire a second cryptanalyst. Dilly Knox is among the candidates, and reports at once; anyone else takes " +
            "most of a day to be vetted and arrive, so hire ahead of need.",
            () => _panel == "staff" ? "hire:Cryptanalyst" : "tool:staff", () => CountOf(Role.Cryptanalyst) >= 2),
        new("speed",
            "Let the time run faster: 3× or 6× (keys 2 and 3). Space pauses, and a decision or a telegram stops the clock by itself.",
            () => "speed:6", () => _main.Speed > 1),
        new("deliver",
            "Now let the Park work. Decrypts leave by the despatch box; deliver five and this part of the chapter is done. Keep accepting " +
            "requests as they come, but no more than the desks can finish in time.",
            () => "objectives", () => S.ActIndex >= 1),
        new("phoney",
            "The Phoney War. The Abwehr's hand cipher is harder than the police one, and the orders want seven staff and four requests done.",
            () => "objectives", null),
        new("messenger",
            "Hire a messenger from Staff (H). They're cheap and quick, and carrying paper between desks is their whole job, so your clerks can stay at " +
            "registration.",
            () => _panel == "staff" ? "hire:Messenger" : "tool:staff",
            () => CountOf(Role.Messenger) > 0 || !S.Candidates.Any(c => c.Role == Role.Messenger)),
        new("reputation",
            "Whitehall's goodwill is the bar at the top. Each request delivered on time raises it, a late one lowers it, and it sets the weekly grant " +
            "that pays the wages. At zero the Park is closed.",
            () => "rep", null),
        new("workload",
            "Workload (U) shows how busy each desk and person has been, and what holds the work up: a desk with paper and nobody at it, or people " +
            "standing idle who could take on more.",
            () => "tool:workload", () => _panel == "workload"),
        new("done",
            "That's the training. F5 saves and F8 loads; Esc opens the menu. Finish this chapter's orders and the war moves on to the Polish gift. " +
            "The Handbook (F1) shows how each kind of traffic goes through the Park.", null, null),
    };

    bool AnyAccepted() => S.Contracts.Any(c => c.State is ContractState.Active or ContractState.Completed);

    void BuildTutorial()
    {
        _tutRing = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, TopLevel = false };
        // A thick gold border with a clear middle (a glow would tint the button under it and make it look greyed out).
        var ring = UiTheme.Box(new Color(0, 0, 0, 0), UiTheme.Accent, 6, 0, 0);
        ring.SetBorderWidthAll(3);
        ring.DrawCenter = false;
        _tutRing.AddThemeStyleboxOverride("panel", ring);
        _root.AddChild(_tutRing);

        _tut = new PanelContainer { Visible = false };
        var sb = UiTheme.Box(UiTheme.PaperBg, UiTheme.Stamp, 4, 14, 10);
        sb.BorderWidthLeft = 5;
        _tut.AddThemeStyleboxOverride("panel", sb);
        _tut.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _tut.OffsetLeft = -250; _tut.OffsetRight = 250; _tut.OffsetTop = 70;
        _tut.GrowHorizontal = Control.GrowDirection.Both;
        _root.AddChild(_tut);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        _tut.AddChild(v);
        _tutHead = Ink("TRAINING", 11, Fonts.BodyBold, UiTheme.Stamp, wrap: false);
        _tutHead.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var skip = UiTheme.Button("Skip", SkipTutorial, tooltip: "Stop the training and carry on with the chapter on your own, with the usual tips");
        skip.Flat = true;
        skip.AddThemeColorOverride("font_color", UiTheme.Stamp);
        skip.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(Row(_tutHead, skip));
        _tutText = Ink("", 14, Fonts.Body);
        v.AddChild(_tutText);
        _tutNext = PaperButton("Next", () => { S.TutorialStep++; FinishTutorialIfDone(); });
        _tutNext.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        v.AddChild(_tutNext);
    }

    void FinishTutorialIfDone()
    {
        if (S.TutorialStep < Steps.Length) return;
        S.Tutorial = false;
        if (!Settings.TutorialDone) { Settings.TutorialDone = true; Settings.Save(); }
    }

    void RefreshTutorial()
    {
        if (!TutorialOn || ModalOpen)
        {
            _tut.Visible = false;
            _tutRing.Visible = false;
            return;
        }
        // Steps already done move on by themselves (a player ahead of the training isn't held back).
        while (S.TutorialStep < Steps.Length && Steps[S.TutorialStep].Done is { } done && done()) S.TutorialStep++;
        FinishTutorialIfDone();
        if (!TutorialOn) { _tut.Visible = false; _tutRing.Visible = false; return; }

        var step = Steps[S.TutorialStep];
        _tut.Visible = true;
        if (_tutShown != S.TutorialStep)
        {
            _tutShown = S.TutorialStep;
            _tutHead.Text = $"TRAINING · STEP {S.TutorialStep + 1} OF {Steps.Length}";
            _tutText.Text = step.Text;
            _tutNext.Visible = step.Done == null;
            _tutNext.Text = S.TutorialStep == Steps.Length - 1 ? "Finish" : "Next";
            _tut.OffsetBottom = _tut.OffsetTop;
            _scrolledTo = null;
        }
        // Where the tips sit: centred over the park, clear of an open side panel and the right-hand column.
        _tut.OffsetLeft = _hint.OffsetLeft;
        _tut.OffsetRight = _hint.OffsetRight;
        if (_tut.OffsetRight - _tut.OffsetLeft < 300) { _tut.OffsetLeft = -250; _tut.OffsetRight = 250; }

        var target = step.Target?.Invoke() is { } key ? FindTarget(key) : null;
        if (target == null || !target.IsVisibleInTree()) { _tutRing.Visible = false; return; }
        if (_scrolledTo != target && _sideScroll.IsAncestorOf(target))
        {
            _sideScroll.EnsureControlVisible(target);
            _scrolledTo = target;
        }
        var r = target.GetGlobalRect().Grow(4);
        if (_sideScroll.IsAncestorOf(target) && !_sideScroll.GetGlobalRect().Encloses(target.GetGlobalRect())) { _tutRing.Visible = false; return; }
        _tutRing.Visible = true;
        _tutRing.Position = r.Position - _root.GetGlobalRect().Position;
        _tutRing.Size = r.Size;
        float pulse = 0.8f + 0.2f * Mathf.Sin((float)Time.GetTicksMsec() / 180f);
        _tutRing.Modulate = new Color(1, 1, 1, pulse);
    }

    Control? _scrolledTo;

    /// <summary>
    /// The control a step points at: "tool:ID" (a left-hand button), "place:StationType" (its Place button in Build),
    /// "hire:Role" (the first Hire button for that role), "accept" (the first Accept), "speed:N", "flow", "objectives", "rep".
    /// </summary>
    Control? FindTarget(string key)
    {
        if (key.StartsWith("tool:")) return _tools.TryGetValue(key[5..], out var b) ? b : null;
        if (key.StartsWith("speed:") && int.TryParse(key[6..], out int sp)) return _speedBtns.FirstOrDefault(x => x.Item2 == sp).Item1;
        switch (key)
        {
            case "flow": return _flowBtn;
            case "objectives": return _objBox.GetParent<Control>();
            case "rep": return _rep;
        }
        if (key.StartsWith("place:")) return FindMeta(_sideBox, "station", key[6..]);
        return FindMeta(_sideBox, "tut", key);
    }

    static Control? FindMeta(Node root, string meta, string value)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Control c && c.HasMeta(meta) && c.GetMeta(meta).AsString() == value) return c;
            if (FindMeta(child, meta, value) is { } hit) return hit;
        }
        return null;
    }

    /// <summary>Skip: the guidance goes, the game carries on where it is, and the usual tips come back.</summary>
    void SkipTutorial()
    {
        S.Tutorial = false;
        _tut.Visible = false;
        _tutRing.Visible = false;
        if (!Settings.TutorialDone) { Settings.TutorialDone = true; Settings.Save(); }
    }
}
