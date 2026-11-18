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
        BuildModalLayer();
    }

    /// <summary>The HUD is scaled as a whole; the root is sized so anchors still reach the screen edges.</summary>
    void ApplyScale()
    {
        float s = Mathf.Clamp(Settings.UiScale, 0.75f, 1.75f);
        Scale = new Vector2(s, s);
        _root.Position = Vector2.Zero;
        _root.Size = GetViewport().GetVisibleRect().Size / s;
    }

    public string? PanelId => _panel;

    public void Reset()
    {
        _objSig = _keySig = _sideSig = _inspSig = _alertSig = "";
        foreach (var c in _feed?.GetChildren() ?? new Godot.Collections.Array<Node>()) c.QueueFree();
        ClosePanel();
        CloseModal();
    }

    public bool IsMouseOverUi() => _root.GetViewport().GuiGetHoveredControl() != null;

    public override void _Process(double delta)
    {
        ApplyScale();
        if (!ModalOpen && _main.Interactive && S.Status == GameStatus.Playing && S.Dilemmas.Count > 0) ShowDilemma(S.Dilemmas[0]);
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

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _tonnage = UiTheme.Label("", 14, Gfx.Bad, Fonts.BodyBold);
        _tonnage.AutowrapMode = TextServer.AutowrapMode.Off;
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
            : m < 9 * 60 ? "night watch" : m < 16 * 60 ? "day watch" : "evening watch";
        string mode = S.Mode == GameMode.FreePlay ? " · free play" : "";
        string sky = S.Weather switch { Weather.Fine => "fine", Weather.Overcast => "overcast", Weather.Rain => "rain", Weather.Fog => "fog", Weather.Snow => "snow", _ => "gale" };
        _time.Text = $"{m / 60:00}:{m % 60:00} · {sky} · {shift} · {S.OnDutyCount}/{S.Staff.Count} on duty{mode}";
        _pauseBtn.SetPressedNoSignal(_main.Paused);
        _flowBtn.SetPressedNoSignal(_main.ShowFlows);
        foreach (var (b, sp) in _speedBtns) b.SetPressedNoSignal(!_main.Paused && _main.Speed == sp);
        _funds.Text = $"£{S.Funds:N0}";
        _funds.AddThemeColorOverride("font_color", S.Funds < 0 ? Gfx.Bad : UiTheme.Text);
        int cut = S.TreasuryCut(S.WeeklyGrant);
        _funds.TooltipText = $"Weekly Treasury grant £{S.WeeklyGrant:N0}, wages £{S.WeeklyWages:N0}. Paid every Sunday night." +
                             (cut > 0 ? $"\nWith more than £{Defs.TreasuryComfortable:N0} in hand, the Treasury will hold back £{cut:N0} of it." : "");
        double slip = S.DailyExpectation * Defs.Difficulties[S.Difficulty].RepLossMult;
        _rep.TooltipText = "Completed requests raise it, missed deadlines lower it. It sets the weekly Treasury grant. At zero, GC&CS is closed down." +
                           (slip > 0 ? $"\n\nWhitehall soon gets used to miracles: it slips {slip:0.0} a day, and more as the war goes on." : "");
        _funds.MouseFilter = Control.MouseFilterEnum.Pass;
        _rep.Value = S.Reputation;
        var fill = UiTheme.Box(S.Reputation < 25 ? Gfx.Bad : S.Reputation < 45 ? Gfx.Warn : UiTheme.Accent, UiTheme.Accent, 3, 0);
        _rep.AddThemeStyleboxOverride("fill", fill);
        _points.Text = S.Points.ToString();
        _tonnage.Visible = S.TonnageLost > 0;
        _tonnage.Text = S.SharkBlackout
            ? $"Shipping lost: {S.TonnageLost:N0} tons"
            : $"Shipping lost: {S.TonnageLost:N0} tons · SHARK read";
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
                     ("intel", "Intelligence", "I"), ("stats", "Statistics", "K"),
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
        var sig = $"{S.ActIndex}|{S.Day}|" + string.Join(",", act.Goals.Select(g => g.Progress(S)));
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
            return $"{n}:{state}:{waiting}:{S.KeyRouteProblem(n)}";
        }).ToList();
        var sig = S.Day + "|" + string.Join(";", lines);
        if (sig == _keySig) return;
        _keySig = sig;
        Clear(_keyBox);
        _keyBox.GetParent<Control>().Visible = nets.Count > 0;
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
            var texts = new VBoxContainer();
            texts.AddThemeConstantOverride("separation", -2);
            texts.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var name = UiTheme.Label(nd.Name.ToUpperInvariant(), 14, UiTheme.Text, Fonts.BodyBold);
            name.TooltipText = nd.Owner;
            name.MouseFilter = Control.MouseFilterEnum.Pass;
            texts.AddChild(name);

            string status; Color col;
            bool free = S.FreeKeysUntil.TryGetValue(n, out var until) && S.Day <= until;
            if (free) { status = "Pinched keys: readable"; col = Gfx.Good; }
            else if (S.KeyState(n, S.Day) == KeyStatus.Broken) { status = "Today's key broken"; col = Gfx.Good; }
            else if (S.KeyState(n, S.Day) == KeyStatus.Attempting) { status = "Breaking today's key…"; col = Gfx.Warn; }
            else if (S.KeyRouteProblem(n) is string prob) { status = prob; col = Gfx.Bad; }
            else { status = "Waiting for today's first message"; col = UiTheme.TextDim; }
            texts.AddChild(UiTheme.Label(status, 12, col));
            row.AddChild(texts);
            if (waiting > 0)
            {
                var w = UiTheme.Label($"{waiting} waiting", 12, Gfx.PaperWaiting);
                w.AutowrapMode = TextServer.AutowrapMode.Off;
                w.TooltipText = "Registered messages waiting for their day's key";
                w.MouseFilter = Control.MouseFilterEnum.Pass;
                row.AddChild(w);
            }
            _keyBox.AddChild(row);
        }
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

    public void Toast(string text, EventKind kind = EventKind.Info)
    {
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
