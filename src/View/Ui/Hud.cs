using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>All on-screen UI, built in code.</summary>
public partial class Hud : CanvasLayer
{
    readonly Main _main = null!;
    GameState S => _main.State;

    Control _root = null!;
    Label _date = null!, _time = null!, _funds = null!, _points = null!, _tonnage = null!;
    ProgressBar _rep = null!;
    Label _quota = null!;
    Control _topSpacer = null!;
    Button _pauseBtn = null!, _flowBtn = null!;
    readonly List<(Button b, int speed)> _speedBtns = new();
    readonly Dictionary<string, Button> _tools = new();

    VBoxContainer _objBox = null!, _keyBox = null!, _feed = null!;
    string _objSig = "", _keySig = "";
    double _refresh;

    public Hud() { }
    public Hud(Main main) { _main = main; }

    public override void _Ready()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Build() };
        AddChild(_root);
        ApplyScale();

        BuildTopBar();
        BuildToolbar();
        BuildRightColumn();
        BuildSidePanel();
        BuildInspector();
        BuildFeed();
        BuildHints();
        BuildTutorial();
        BuildModalLayer();
    }

    /// <summary>The HUD is scaled as a whole; the root is sized so anchors still reach the screen edges.</summary>
    void ApplyScale()
    {
        float s = Mathf.Clamp(Settings.UiScale, 0.75f, 1.75f);
        // A small window shrinks the whole canvas with it (canvas_items stretch from 1600×900). Below about 0.8 the
        // small print can't be read, so the HUD grows back to that size and the park gets less of the screen.
        var visible = GetViewport().GetVisibleRect().Size;
        float shrink = visible.X > 0 ? GetWindow().Size.X / visible.X : 1;
        if (shrink > 0.1f && shrink < MinReadableScale) s *= MinReadableScale / shrink;
        Scale = new Vector2(s, s);
        _root.Position = Vector2.Zero;
        _root.Size = visible / s;
    }

    const float MinReadableScale = 0.8f;

    public string? PanelId => _panel;

    public void Reset()
    {
        _objSig = _keySig = _sideSig = _inspSig = _alertSig = "";
        _tutShown = -1;
        foreach (var c in _feed?.GetChildren() ?? new Godot.Collections.Array<Node>()) c.QueueFree();
        ClosePanel();
        CloseModal();
    }

    public bool IsMouseOverUi() => _root.GetViewport().GuiGetHoveredControl() != null;

    /// <summary>Whether a rectangle on screen (in viewport pixels) lies under a HUD panel or button.</summary>
    public bool Covers(Rect2 r)
    {
        foreach (var n in _root.GetChildren())
            if (n is Control { Visible: true } c && Covers(c, r, 0)) return true;
        return false;
    }

    static bool Covers(Control c, Rect2 r, int depth)
    {
        if (c is PanelContainer or BaseButton)
            return (c.GetGlobalTransformWithCanvas() * new Rect2(Vector2.Zero, c.Size)).Intersects(r);
        if (depth >= 2) return false;
        foreach (var n in c.GetChildren())
            if (n is Control { Visible: true } k && Covers(k, r, depth + 1)) return true;
        return false;
    }

    bool _keysWanted, _alertsWanted;

    /// <summary>
    /// The inspector opens over the bottom of the right column: while it's up, Needs attention steps aside, and so does the keys
    /// card if the inspector would cover it, rather than showing half of each under it.
    /// </summary>
    void ApplyRightColumn()
    {
        if (_keyBox == null || _alertBox == null || _inspector == null) return;
        bool insp = _inspector.Visible;
        _alertBox.GetParent<Control>().Visible = _alertsWanted && !insp;
        var keys = _keyBox.GetParent<Control>();
        bool covered = insp && keys.GetGlobalRect().End.Y > _inspector.GetGlobalRect().Position.Y;
        keys.Visible = _keysWanted && !covered;
    }

    public override void _Process(double delta)
    {
        ApplyScale();
        ApplyRightColumn();
        // An open side panel covers the left of the screen: slide the Park right by half the panel's width.
        _main.Rig.ShiftPx = _side.Visible ? (_side.OffsetRight - _side.OffsetLeft) / 2 * Scale.X : 0;
        ShowNextToast();
        if (!ModalOpen && _main.Interactive && S.Status == GameStatus.Playing
            && S.Contracts.FirstOrDefault(c => c.Flash && c.State == ContractState.Offered && !_flashShown.Contains(c.Id)) is { } flash)
            ShowFlash(flash);
        if (!ModalOpen && _main.Interactive && S.Status == GameStatus.Playing && S.Dilemmas.Count > 0) ShowDilemma(S.Dilemmas[0]);
        RefreshTutorial();   // every frame: the outline pulses and follows a scrolling panel
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.2;
        RefreshTopBar();
        RefreshObjectives();
        RefreshKeys();
        RefreshAlerts();
        RefreshSidePanel();
        RefreshInspector();
        RefreshHints();
        foreach (var n in _feed.GetChildren())
            if (n is Control c && c.HasMeta("born") && Time.GetTicksMsec() - (ulong)c.GetMeta("born") > 14000)
                c.QueueFree();
    }

    // ---------------------------------------------------------------- top bar

    void BuildTopBar()
    {
        var bar = new PanelContainer();
        bar.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, UiTheme.PanelEdge, 0, 14, 6));
        bar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _root.AddChild(bar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);
        bar.AddChild(row);

        var mark = UiTheme.Heading("STATION X", 20, UiTheme.Accent);
        mark.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(mark);
        row.AddChild(new VSeparator());

        var dateBox = new VBoxContainer();
        dateBox.AddThemeConstantOverride("separation", -2);
        _date = UiTheme.Label("", 16, UiTheme.Text, Fonts.Typewriter);
        _date.AutowrapMode = TextServer.AutowrapMode.Off;
        _time = UiTheme.Label("", 12, UiTheme.TextDim, Fonts.Typewriter, wrap: false);
        dateBox.AddChild(_date);
        dateBox.AddChild(_time);
        row.AddChild(dateBox);

        var speeds = new HBoxContainer();
        speeds.AddThemeConstantOverride("separation", 4);
        _pauseBtn = UiTheme.Button("Pause", () => _main.TogglePause(), tooltip: "Space");
        _pauseBtn.ToggleMode = true;
        speeds.AddChild(_pauseBtn);
        for (int i = 0; i < Main.Speeds.Length; i++)
        {
            int sp = Main.Speeds[i];
            var b = UiTheme.Button($"{sp}×", () => _main.SetSpeed(sp), tooltip: $"Key {i + 1}");
            b.ToggleMode = true;
            speeds.AddChild(b);
            _speedBtns.Add((b, sp));
        }
        speeds.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(speeds);

        var view = new HBoxContainer();
        view.AddThemeConstantOverride("separation", 4);
        view.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        view.AddChild(UiTheme.Button("⟲", () => _main.RotateView(-1), tooltip: "Turn the view (, or [)"));
        view.AddChild(UiTheme.Button("⟳", () => _main.RotateView(1), tooltip: "Turn the view (. or ])"));
        _flowBtn = UiTheme.Button("Flow", () => _main.ShowFlows = !_main.ShowFlows, tooltip: "Show where paper goes and where it waits (F)");
        _flowBtn.ToggleMode = true;
        view.AddChild(_flowBtn);
        view.AddChild(UiTheme.Button("Menu", ShowPauseMenu, tooltip: "Save, load, settings (Esc)"));
        row.AddChild(view);

        _topSpacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(_topSpacer);

        // Takes the room left over and trims itself to it, so the bar never runs off the screen.
        _tonnage = UiTheme.Label("", 14, Gfx.Bad, Fonts.BodyBold);
        _tonnage.AutowrapMode = TextServer.AutowrapMode.Off;
        _tonnage.ClipText = true;
        _tonnage.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _tonnage.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _tonnage.HorizontalAlignment = HorizontalAlignment.Right;
        _tonnage.VerticalAlignment = VerticalAlignment.Center;
        _tonnage.TooltipText = "Allied shipping sunk while SHARK can't be read.";
        _tonnage.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddChild(_tonnage);

        row.AddChild(Stat("Funds", out _funds));
        var repBox = new VBoxContainer();
        repBox.AddThemeConstantOverride("separation", 2);
        repBox.AddChild(UiTheme.Caps("Reputation in Whitehall"));
        _rep = new ProgressBar { MinValue = 0, MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(150, 12) };
        _rep.TooltipText = "Completed requests raise it, missed deadlines lower it. It sets the weekly Treasury grant. At zero, GC&CS is closed down.";
        repBox.AddChild(_rep);
        // Today's fresh decrypts against Whitehall's quota, once it counts (the tooltip is the reputation bar's).
        _quota = UiTheme.Label("", 11, UiTheme.TextDim, Fonts.Body, wrap: false);
        _quota.MouseFilter = Control.MouseFilterEnum.Pass;
        repBox.AddChild(_quota);
        repBox.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(repBox);
        row.AddChild(Stat("Breakthroughs", out _points));
        _points.TooltipText = "Earned for every decrypt delivered. Spend them on research.";
        _points.MouseFilter = Control.MouseFilterEnum.Pass;
    }

    static Control Stat(string caption, out Label value)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", -1);
        box.AddChild(UiTheme.Caps(caption));
        value = UiTheme.Label("", 17, UiTheme.Text, Fonts.BodyBold);
        value.AutowrapMode = TextServer.AutowrapMode.Off;
        box.AddChild(value);
        return box;
    }

    void RefreshTopBar()
    {
        _date.Text = S.Date.ToString("dddd d MMMM yyyy");
        int m = (int)S.MinuteOfDay;
        string shift = !S.WatchesAvailable
            ? (m is >= 7 * 60 and < 22 * 60 ? "working day" : "after hours")
            : Defs.Shifts[Shift.Night].OnDuty(m) ? "night watch" : Defs.Shifts[Shift.Day].OnDuty(m) ? "day watch" : "evening watch";
        string mode = S.Mode == GameMode.FreePlay ? " · free play" : "";
        string sky = S.Weather switch { Weather.Fine => "fine", Weather.Overcast => "overcast", Weather.Rain => "rain", Weather.Fog => "fog", Weather.Snow => "snow", _ => "gale" };
        if (S.RaidOn) sky += " · AIR RAID";
        if (S.PowerCut) sky += " · POWER CUT";
        if (S.FluWave) sky += " · FLU";
        else if (S.FluComing) sky += " · flu coming";
        if (S.InspectionDue) sky += $" · {(S.PmVisit ? "PM " : "")}VISIT {(S.InspectionAt - S.Day * Defs.MinutesPerDay >= Defs.MinutesPerDay ? "tomorrow " : "")}{(int)(S.InspectionAt % Defs.MinutesPerDay / 60):00}:00";
        if (S.AllHands) sky += " · ALL HANDS";
        _time.Text = $"{m / 60:00}:{m % 60:00} · {sky} · {shift} · {S.OnDutyCount}/{S.Staff.Count} on duty{mode}";
        _pauseBtn.SetPressedNoSignal(_main.Paused);
        _flowBtn.SetPressedNoSignal(_main.ShowFlows);
        foreach (var (b, sp) in _speedBtns) b.SetPressedNoSignal(!_main.Paused && _main.Speed == sp);
        _funds.Text = UiTheme.Money(S.Funds);
        _funds.AddThemeColorOverride("font_color", S.Funds < 0 ? Gfx.Bad : UiTheme.Text);
        int cut = S.TreasuryCut(S.WeeklyGrant);
        _funds.TooltipText = $"Weekly Treasury grant £{S.WeeklyGrant:N0}, wages £{S.WeeklyWages:N0}, upkeep £{S.WeeklyUpkeep:N0}. Paid every Sunday night. " +
                             $"The grant alone never covers a growing Park: the ministries pay for each request delivered (£{S.EarnedThisWeek:N0} since Sunday)." +
                             (cut > 0 ? $"\nWith more than £{Defs.TreasuryComfortable:N0} in hand, the Treasury will hold back £{cut:N0} of it." : "");
        double slip = S.DailyExpectation * S.ExpectationMult * S.Rules.RepLossMult;
        _rep.TooltipText = "Completed requests raise it, missed deadlines lower it. It sets the weekly Treasury grant. At zero, GC&CS is closed down." +
                           (S.QuotaActive
                               ? $"\n\nWhitehall expects {S.IntelQuota} fresh decrypts a day, a share of what the ministries ask for that grows as the war goes on. Over the last two days " +
                                 $"we delivered {S.IntelRate:0.0} a day (air and naval news counts for less after 16 hours, army after a day): " +
                                 (S.IntelRate < S.IntelQuota ? $"reputation slips {slip:0.0} a day until we deliver more." : $"reputation slips only {slip:0.0} a day while we keep it up.")
                               : slip > 0 ? $"\n\nWhitehall soon gets used to miracles: it slips {slip:0.0} a day, and more as the war goes on." : "") +
                           RepWeek();
        _funds.MouseFilter = Control.MouseFilterEnum.Pass;
        _rep.Value = S.Reputation;
        // At the recent rate, when reputation would run out: in the line under the bar, red.
        int? runsOut = S.RepRunsOutIn();
        if (runsOut is int n)
            _rep.TooltipText += $"\n\nOver the last {Defs.RepTrendDays:0} days reputation has fallen {-(S.RepTrend() ?? 0):0.0} a day. At that rate it runs out " +
                                $"in about {n} day{(n == 1 ? "" : "s")}: deliver more, and miss no deadlines.";
        _quota.Visible = S.QuotaActive || runsOut != null;
        if (S.QuotaActive)
        {
            int q = S.IntelQuota;
            _quota.Text = $"Decrypts {S.IntelRate:0.#} a day of {q} · " + (runsOut is int d ? $"gone in ~{d} day{(d == 1 ? "" : "s")}" : $"{S.FreshToday:0} today");
            _quota.AddThemeColorOverride("font_color", runsOut != null ? Gfx.Bad : S.IntelRate >= q ? Gfx.Good : S.IntelRate >= q * 0.6 ? UiTheme.TextDim : Gfx.Warn);
        }
        else if (runsOut is int d)
        {
            _quota.Text = $"At this rate, gone in ~{d} day{(d == 1 ? "" : "s")}";
            _quota.AddThemeColorOverride("font_color", Gfx.Bad);
        }
        _quota.TooltipText = _rep.TooltipText;
        var fill = UiTheme.Box(S.Reputation < 25 ? Gfx.Bad : S.Reputation < 45 ? Gfx.Warn : UiTheme.Accent, UiTheme.Accent, 3, 0);
        _rep.AddThemeStyleboxOverride("fill", fill);
        _points.Text = S.Points.ToString();
        _tonnage.Visible = S.TonnageLost > 0;
        _topSpacer.Visible = !_tonnage.Visible;
        _tonnage.Text = $"Shipping lost: {S.TonnageLost:N0} tons";
        _tonnage.TooltipText = S.SharkBlackout
            ? $"Allied shipping sunk while SHARK can't be read: {S.TonnageLost:N0} tons so far, and more every day until it is."
            : $"Allied shipping sunk while SHARK couldn't be read: {S.TonnageLost:N0} tons. SHARK is read now, and the losses have stopped.";
    }

    // ---------------------------------------------------------------- toolbar

    void BuildToolbar()
    {
        var bar = new VBoxContainer { Position = new Vector2(12, 74) };
        bar.AddThemeConstantOverride("separation", 6);
        _root.AddChild(bar);
        foreach (var (id, label, key) in new[]
                 {
                     ("build", "Build", "B"), ("staff", "Staff", "H"), ("requests", "Requests", "Q"),
                     ("research", "Research", "T"), ("huts", "Huts", "G"),
                     ("intel", "Intelligence", "I"), ("ystations", "Y-stations", "Y"), ("intercepts", "Intercepts", "N"), ("war", "War map", "O"), ("stats", "Statistics", "K"), ("workload", "Workload", "U"), ("handbook", "Handbook", "F1"),
                 })
        {
            var b = UiTheme.Button(label, () => TogglePanel(id), tooltip: $"Key {key}");
            b.CustomMinimumSize = new Vector2(112, 38);
            b.ToggleMode = true;
            b.Alignment = HorizontalAlignment.Left;
            b.AddThemeFontOverride("font", Fonts.BodyBold);
            bar.AddChild(b);
            _tools[id] = b;
        }
    }

    // ----------------------------------------------------------- right column

    void BuildRightColumn()
    {
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 10);
        col.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        col.OffsetLeft = -332; col.OffsetRight = -12; col.OffsetTop = 74;
        col.GrowHorizontal = Control.GrowDirection.Begin;
        _root.AddChild(col);

        var obj = new PanelContainer();
        _objBox = new VBoxContainer();
        _objBox.AddThemeConstantOverride("separation", 6);
        obj.AddChild(_objBox);
        col.AddChild(obj);

        var keys = new PanelContainer();
        _keyBox = new VBoxContainer();
        _keyBox.AddThemeConstantOverride("separation", 5);
        keys.AddChild(_keyBox);
        col.AddChild(keys);
        BuildAlerts(col);
    }

    void RefreshObjectives()
    {
        var act = S.Act;
        var sig = $"{S.ActIndex}|{S.Day}|{(int)(S.Readiness() * 100)}|{S.CurrentOp == null}|{(int)((S.CurrentOp != null ? S.DDayAt - S.Now : 0) / 60)}|" + string.Join(",", act.Goals.Select(g => g.Progress(S)));
        if (sig == _objSig) return;
        _objSig = sig;
        Clear(_objBox);
        _objBox.AddChild(UiTheme.Caps($"Chapter {act.Chapter} · {act.ChapterTitle}", 11, UiTheme.Accent));
        _objBox.AddChild(UiTheme.Heading(act.Title, 19));
        if (S.Mode == GameMode.FreePlay)
        {
            int left = (int)(GameState.FreePlayActDays - (S.Day - S.ActStartDay));
            _objBox.AddChild(UiTheme.Label(S.ActIndex + 1 < Campaign.Acts.Length
                ? $"Free play. The war moves on in {Math.Max(1, left)} day(s): new nets, new methods, new requests."
                : "Free play. The war is as far as it goes; carry on as long as you like.", 13, UiTheme.TextDim));
        }
        if (S.CurrentOp is { } op)
        {
            double hours = (S.DDayAt - S.Now) / 60;
            double r = S.Readiness();
            var outcome = GameState.OutcomeFor(r);
            var (word, colour) = outcome switch
            {
                OpOutcome.Success => ("on course to succeed", Gfx.Good),
                OpOutcome.Partial => ("it would go badly", Gfx.Warn),
                _ => (op.Fatal ? "it would fail, and the campaign with it" : "it would fail", Gfx.Bad),
            };
            string when = hours >= 36 ? $"in {Math.Ceiling(hours / 24):0} days" : $"in {Math.Max(1, Math.Ceiling(hours)):0} hours";
            var l = UiTheme.Label($"{op.Name.ToUpperInvariant()} {when} · readiness {r * 100:0}% · {word}", 13, colour, Fonts.BodyBold);
            l.TooltipText = $"Readiness is the 'before' objectives ({S.OpIntel() * 100:0}% done), how unsuspecting the " +
                            $"{string.Join(", ", op.Watch.Select(sv => Defs.ServiceNames[sv]))} still are ({S.OpSecrecy(op) * 100:0}%), " +
                            (S.OshimaBonus > 0 ? $"Ōshima's reports from Berlin (+{S.OshimaBonus * 100:0}%), " : "") +
                            (S.FortitudeBonus > 0 ? $"the Double Cross agents Berlin still believes (+{S.FortitudeBonus * 100:0}%), " : "") + "and the decision " +
                            $"a few days before. {Defs.OpSuccessAt * 100:0}% or more and it succeeds; under {Defs.OpPartialAt * 100:0}% it fails.";
            l.MouseFilter = Control.MouseFilterEnum.Pass;
            _objBox.AddChild(l);
        }
        foreach (var g in S.Mode == GameMode.FreePlay ? Enumerable.Empty<Goal>() : act.Goals)
        {
            int p = g.Progress(S);
            bool done = p >= g.Target;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var tick = new Panel { CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            tick.AddThemeStyleboxOverride("panel", UiTheme.Box(done ? Gfx.Good : new Color(0, 0, 0, 0), done ? Gfx.Good : UiTheme.TextDim, 2, 0));
            row.AddChild(tick);
            var text = g.Target > 1 ? $"{g.Text}  ({p}/{g.Target})" : g.Text;
            var l = UiTheme.Label(text, 14, done ? UiTheme.TextDim : UiTheme.Text);
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(l);
            _objBox.AddChild(row);
        }
        var brief = UiTheme.Button("Read the briefing again", ShowBriefing);
        brief.Flat = true;
        brief.AddThemeColorOverride("font_color", UiTheme.Accent);
        brief.AddThemeFontSizeOverride("font_size", 12);
        brief.Alignment = HorizontalAlignment.Left;
        _objBox.AddChild(brief);
    }

    void RefreshKeys()
    {
        var nets = S.KeyBoardNets().ToList();
        var lines = nets.Select(n =>
        {
            int waiting = S.Messages.Values.Count(m => m.Net == n && m.Next == null);
            var state = S.KeyBroken(n, S.Day) ? "broken" : S.KeyState(n, S.Day).ToString();
            return $"{n}:{state}:{waiting}:{S.KeyRouteProblem(n)}:{S.KeyOrderOf(n)}:{S.Kissed(n, S.Day)}";
        }).ToList();
        var sig = S.Day + "|" + string.Join(";", lines) + $"|{S.KissesOpen}{S.ReadWerft}{S.WerftProblem()}|{S.Counters.Count}{S.Researched.Count}|{S.BackTraffic / 2}{S.BackWorking}";
        if (sig == _keySig) return;
        _keySig = sig;
        Clear(_keyBox);
        _keysWanted = nets.Count > 0;
        ApplyRightColumn();
        if (nets.Count == 0) return;
        _keyBox.AddChild(UiTheme.Caps($"Enigma keys · {S.Date:d MMMM}", 11, UiTheme.Accent));
        foreach (var n in nets)
        {
            var nd = Defs.NetOf(n);
            int waiting = S.Messages.Values.Count(m => m.Net == n && m.Next == null);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var chip = new ColorRect { Color = Gfx.NetColor(n), CustomMinimumSize = new Vector2(6, 30) };
            row.AddChild(chip);
            var mark = NetMark.For(n);
            row.AddChild(mark);
            var texts = new VBoxContainer();
            texts.AddThemeConstantOverride("separation", -2);
            texts.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var name = UiTheme.Label(nd.Name.ToUpperInvariant(), 14, UiTheme.Text, Fonts.BodyBold);
            name.TooltipText = nd.Owner;
            name.MouseFilter = Control.MouseFilterEnum.Pass;
            // The waiting count sits beside the name, so the status line below has the card's whole width.
            name.AutowrapMode = TextServer.AutowrapMode.Off;
            var nameRow = new HBoxContainer();
            nameRow.AddThemeConstantOverride("separation", 8);
            nameRow.AddChild(name);
            if (waiting > 0)
            {
                var w = UiTheme.Label($"{waiting} waiting", 12, Gfx.PaperWaiting);
                w.AutowrapMode = TextServer.AutowrapMode.Off;
                w.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                w.TooltipText = "Registered messages waiting for their day's key";
                w.MouseFilter = Control.MouseFilterEnum.Pass;
                nameRow.AddChild(w);
            }
            texts.AddChild(nameRow);

            string status; Color col;
            bool free = S.FreeKeysUntil.TryGetValue(n, out var until) && S.Day <= until;
            if (free) { status = "Pinched keys: readable"; col = Gfx.Good; }
            else if (S.KeyState(n, S.Day) == KeyStatus.Broken) { status = "Today's key broken"; col = Gfx.Good; }
            else if (S.KeyState(n, S.Day) == KeyStatus.Attempting) { status = "Breaking today's key…"; col = Gfx.Warn; }
            else if (S.KeyOrderOf(n) == KeyOrder.Hold) { status = "On hold: not attempted"; col = Gfx.Warn; }
            else if (S.KeyRouteProblem(n) is string prob) { status = prob; col = Gfx.Bad; }
            else { status = "Waiting for today's first message"; col = UiTheme.TextDim; }
            texts.AddChild(UiTheme.Label(status, 12, col));
            if (!free && S.Stale(n, S.Day) && !S.Gardened(n, S.Day))
            {
                var st = UiTheme.Label($"Cribs poor for {S.StaleUntil[n] - S.Day + 1} more day(s)", 11, Gfx.Warn);
                st.TooltipText = "The Germans have changed their habits. Menus take longer to make and Bombe runs take longer until new cribs turn up." +
                                 (nd.Naval ? " Gardening plants a crib regardless." : "");
                st.MouseFilter = Control.MouseFilterEnum.Pass;
                texts.AddChild(st);
            }
            if (!free)
                foreach (var cm in S.CountersOn(n))
                {
                    var cl = UiTheme.Label(cm.Effect, 11, Gfx.Warn);
                    cl.TooltipText = cm.News;
                    cl.MouseFilter = Control.MouseFilterEnum.Pass;
                    texts.AddChild(cl);
                }
            if (!free && S.Kissed(n, S.Day))
            {
                var k = UiTheme.Label("Kissed by WERFT", 11, Gfx.Good);
                k.TooltipText = "A perfect crib: this morning's WERFT signal went out again in this key. The crib desk writes the menu in a third of the time, " +
                                $"the Bombe run is {(1 - Defs.KissBombeMult) * 100:0}% shorter, and the stop holds.";
                k.MouseFilter = Control.MouseFilterEnum.Pass;
                texts.AddChild(k);
            }
            row.AddChild(texts);
            if (!free)
            {
                var order = S.KeyOrderOf(n);
                var net = n;
                var ob = UiTheme.Button(order switch { KeyOrder.First => "First", KeyOrder.Hold => "Hold", _ => "In turn" },
                    () => { S.SetKeyOrder(net, GameState.NextOrder(S.KeyOrderOf(net))); _keySig = ""; },
                    tooltip: "Bombe time is short. First: this key jumps the queue at crib desks and Bombes, and its traffic is carried first.\n" +
                             "Hold: don't attempt this key; its traffic waits and the Bombes work for the others.\n" +
                             "In turn: the usual order, most urgent first.\nClick to change: in turn → first → hold.");
                ob.CustomMinimumSize = new Vector2(64, 26);
                ob.AddThemeFontSizeOverride("font_size", 11);
                if (order == KeyOrder.First) ob.AddThemeColorOverride("font_color", Gfx.Good);
                if (order == KeyOrder.Hold) ob.AddThemeColorOverride("font_color", Gfx.Warn);
                ob.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                row.AddChild(ob);
            }
            _keyBox.AddChild(row);
        }
        if (S.KissesOpen) WerftRow();
        if (S.BackTraffic > 0)
        {
            var b = UiTheme.Label($"Back traffic: {S.BackTraffic} old messages unread" + (S.BackWorking ? ", being read" : ""), 11,
                S.BackWorking ? Gfx.Good : UiTheme.TextDim);
            b.TooltipText = "Each day a key isn't broken leaves its traffic unread. In quiet hours, idle cryptanalysts and Wrens with a Bombe standing " +
                            $"free go back to it without being asked: +{Defs.BackPoints} breakthrough for each message read. It needs both, " +
                            "so spare hands on the same watch earn something.";
            b.MouseFilter = Control.MouseFilterEnum.Pass;
            b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _keyBox.AddChild(b);
        }
    }

    /// <summary>Kisses: the dockyard cipher, read only for the cribs it gives the naval keys.</summary>
    void WerftRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new ColorRect { Color = Gfx.NetColor(Net.Werft), CustomMinimumSize = new Vector2(6, 30) });
        row.AddChild(NetMark.For(Net.Werft));
        var texts = new VBoxContainer();
        texts.AddThemeConstantOverride("separation", -2);
        texts.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var name = UiTheme.Label("WERFT · kisses", 14, UiTheme.Text, Fonts.BodyBold);
        name.TooltipText = Defs.NetOf(Net.Werft).Owner;
        name.MouseFilter = Control.MouseFilterEnum.Pass;
        texts.AddChild(name);
        string? prob = S.WerftProblem();
        var (status, col) = !S.ReadWerft ? ("Not read: no kisses for the naval keys", UiTheme.TextDim)
            : prob != null ? (prob, Gfx.Bad)
            : S.KissesToday > 0 ? ("Read today: naval keys kissed", Gfx.Good)
            : ("Read each morning for kisses", Gfx.Warn);
        texts.AddChild(UiTheme.Label(status, 12, col));
        row.AddChild(texts);
        var b = UiTheme.Button(S.ReadWerft ? "Read" : "Off", () => { S.SetReadWerft(!S.ReadWerft); _keySig = ""; },
            tooltip: "A kiss: the same text sent twice, once in a cipher we already read. The dockyards and weather stations send their routine " +
                     $"reports in WERFT, a hand cipher, and again in naval Enigma. Read: Scarborough sends {Defs.WerftPerDay} WERFT signals each morning " +
                     "to a hand-cipher desk; once one is read, that day's DOLPHIN and SHARK keys have a perfect crib (the menu in a third of the time, " +
                     $"a Bombe run {(1 - Defs.KissBombeMult) * 100:0}% shorter, and a stop that holds). Reading one takes a cryptanalyst about three hours.\nClick to change.");
        b.CustomMinimumSize = new Vector2(64, 26);
        b.AddThemeFontSizeOverride("font_size", 11);
        if (S.ReadWerft) b.AddThemeColorOverride("font_color", Gfx.Good);
        b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(b);
        _keyBox.AddChild(row);
    }

    // ------------------------------------------------------------- event feed

    void BuildFeed()
    {
        _feed = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _feed.AddThemeConstantOverride("separation", 4);
        _feed.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _feed.OffsetLeft = 12; _feed.OffsetRight = 532; _feed.OffsetTop = -16; _feed.OffsetBottom = -16;
        _feed.GrowVertical = Control.GrowDirection.Begin;
        _root.AddChild(_feed);
    }

    readonly Queue<(string text, EventKind kind)> _toasts = new();

    /// <summary>
    /// Queues a line for the feed. They go up one a frame: each new wrapped label lays the feed out again, and the
    /// handful that come at midnight all at once made a visible stutter.
    /// </summary>
    public void Toast(string text, EventKind kind = EventKind.Info)
    {
        _toasts.Enqueue((text, kind));
        while (_toasts.Count > 6) _toasts.Dequeue();   // the feed keeps six; older ones would only be pushed off
    }

    void ShowNextToast()
    {
        if (_toasts.Count == 0) return;
        var (text, kind) = _toasts.Dequeue();
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var edge = kind switch { EventKind.Good => Gfx.Good, EventKind.Bad => Gfx.Bad, _ => UiTheme.PanelEdge };
        var sb = UiTheme.Box(UiTheme.Panel with { A = 0.92f }, edge, 4, 10, 5);
        sb.BorderWidthLeft = 4;
        p.AddThemeStyleboxOverride("panel", sb);
        var l = UiTheme.Label(text, 13);
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        p.AddChild(l);
        p.SetMeta("born", Time.GetTicksMsec());
        _feed.AddChild(p);
        while (_feed.GetChildCount() > 6) { var first = _feed.GetChild(0); _feed.RemoveChild(first); first.QueueFree(); }
    }

    /// <summary>The past seven days' reputation changes, by cause.</summary>
    string RepWeek()
    {
        var week = S.RepLog.Where(e => S.Now - e.At <= 7 * Defs.MinutesPerDay)
                           .GroupBy(e => e.Reason).Select(g => (reason: g.Key, delta: g.Sum(e => e.Delta)))
                           .Where(x => Math.Abs(x.delta) >= 0.05).OrderBy(x => x.delta).ToList();
        if (week.Count == 0) return "";
        return "\n\nThe past week:\n" + string.Join("\n", week.Select(x => $"  {(x.delta > 0 ? "+" : "−")}{Math.Abs(x.delta):0.0}  {x.reason}")) +
               $"\n  = {(week.Sum(x => x.delta) >= 0 ? "+" : "−")}{Math.Abs(week.Sum(x => x.delta)):0.0}";
    }

    public void OnEvent(GameEvent e)
    {
        switch (e.Kind)
        {
            case EventKind.ActStarted: ShowBriefing(); break;
            case EventKind.ActComplete: ShowActComplete(); break;
            case EventKind.Won: ShowEnd(true); break;
            case EventKind.Lost: ShowEnd(false); break;
            case EventKind.Dilemma: break;   // shown from _Process once no other sheet is open
            case EventKind.Sound: break;
            default: Toast(e.Text, e.Kind); break;
        }
    }

    static void Clear(Node n)
    {
        foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); }
    }
}
