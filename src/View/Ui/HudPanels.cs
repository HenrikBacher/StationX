using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

public partial class Hud
{
    /// <summary>The Learns list's id for the Bedford Japanese course (roles use their own numbers).</summary>
    const int JapaneseItem = 1000;
    /// <summary>The watch and building picked on each candidate's card, kept while the panel rebuilds.</summary>
    readonly Dictionary<int, Shift> _hireShift = new();
    readonly Dictionary<int, string> _hireZone = new();
    PanelContainer _side = null!;
    ScrollContainer _sideScroll = null!;
    /// <summary>For screenshots: scroll the side panel to its foot.</summary>
    public void ScrollPanelToEnd() => _sideScroll.ScrollVertical = 100000;
    VBoxContainer _sideBox = null!;
    string? _panel;
    string _sideSig = "";

    PanelContainer _inspector = null!;
    VBoxContainer _inspBox = null!;
    string _inspSig = "";
    int? _inspStation, _inspStaff;
    Zone? _inspZone;

    void BuildSidePanel()
    {
        _side = new PanelContainer { Visible = false };
        _side.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        _side.OffsetLeft = 126; _side.OffsetRight = 126 + 400; _side.OffsetTop = 74; _side.OffsetBottom = -12;
        _root.AddChild(_side);
        var scroll = _sideScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _side.AddChild(scroll);
        _sideBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _sideBox.AddThemeConstantOverride("separation", 8);
        // A gutter so right-aligned text and buttons stay clear of the scrollbar.
        var gutter = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        gutter.AddThemeConstantOverride("margin_right", 12);
        gutter.AddChild(_sideBox);
        scroll.AddChild(gutter);
    }

    public void OpenPanel(string id)
    {
        _panel = id;
        _introOpen.Clear();
        _side.Visible = true;
        _sideSig = "";
        foreach (var (k, b) in _tools) b.SetPressedNoSignal(k == id);
        RefreshSidePanel();
    }

    public void TogglePanel(string id)
    {
        if (_panel == id) ClosePanel(); else OpenPanel(id);
    }

    public void ClosePanel()
    {
        _panel = null;
        if (_side != null) _side.Visible = false;
        foreach (var b in _tools.Values) b.SetPressedNoSignal(false);
    }

    void RefreshSidePanel()
    {
        if (_panel == null) return;
        string sig = _panel switch
        {
            "build" => $"{S.Funds / 50}|{S.ActIndex}|{string.Join(",", S.Researched)}|{S.IndicatorChanged}",
            "staff" => string.Join(",", S.Staff.Select(s => $"{s.Id}{s.Task?.Kind}{s.Leaving}{s.Away}{s.Shift}{(int)(s.Energy * 5)}{(int)(s.Morale * 5)}{s.FarBillet}{s.IsSick(S.Now)}{s.Arriving(S.Now)}{s.HeadOf}")) + $"|{S.Billets}{S.Funds >= Defs.BilletCost}{S.AllHands}{S.CanCallAllHands() == null}{S.CantHoldCrossword() == null}{S.CrosswordPending}{S.CantHoldSocial() == null}{S.SocialPending}{(int)(S.AverageMorale * 100)}|{(int)S.Stores}{S.StoresOnOrder}{S.StandingOrder}{S.CanOrderStores() == null}|" +
                       string.Join(",", S.Candidates.Select(c => c.Id)) + $"|{S.Funds / 25}|{(int)(S.Now / 30)}|{_idleFirst}",
            "requests" => string.Join(",", S.Contracts.Select(c => $"{c.Id}{c.State}{c.Priority}{c.Delivered}{c.Spawned}{c.MilestonesChecked}{(int)((c.State == ContractState.Offered ? c.OfferExpires : c.Due) - S.Now) / 60}")) +
                          $"|{string.Join(",", S.KeyBoardNets().Select(n => S.KeyRoute(n)))}",
            "research" => $"{S.Points}|{string.Join(",", S.Researched)}|{string.Join(",", S.ResearchAvailable)}|{S.ChiefsReports}|{S.CantReport()}",
            "huts" => $"{S.Funds / 100}|{S.OutstationBombes}|{S.CanBuyOutstation()}|" + string.Join(",", S.Grid.Zones.Select(z => $"{z.Available}{z.Unlocked}")),
            "intel" => IntelSig(),
            "ystations" => YSig(),
            "intercepts" => InterceptsSig(),
            "war" => WarMapSig(),
            "stats" => $"{S.History.Count}|{S.UseDays.Count}|{_statsRange}",
            "handbook" => HandbookSig(),
            "workload" => WorkloadSig(),
            _ => "",
        };
        sig = _panel + sig;
        if (sig == _sideSig) return;
        _sideSig = sig;
        Clear(_sideBox);
        switch (_panel)
        {
            case "build": BuildPanel(); break;
            case "staff": StaffPanel(); break;
            case "requests": RequestsPanel(); break;
            case "research": ResearchPanel(); break;
            case "huts": HutsPanel(); break;
            case "intel": IntelPanel(); break;
            case "ystations": YStationsPanel(); break;
            case "intercepts": InterceptsPanel(); break;
            case "war": WarMapPanel(); break;
            case "stats": StatsPanel(); break;
            case "handbook": HandbookPanel(); break;
            case "workload": WorkloadPanel(); break;
        }
    }

    /// <summary>Hours since the oldest message at a station (in, out or on the desk) was intercepted.</summary>
    int OldestHours(Station st)
    {
        double oldest = st.Input.Concat(st.Output).Concat(st.Current is int c ? new[] { c } : System.Array.Empty<int>())
            .Select(id => S.Messages.TryGetValue(id, out var m) ? S.Now - m.Created : 0).DefaultIfEmpty(0).Max();
        return (int)(oldest / 60);
    }

    Control Card(Color? edge = null)
    {
        var p = new PanelContainer();
        var sb = UiTheme.Box(UiTheme.PanelLight, edge ?? UiTheme.PanelEdge, 4, 10, 8);
        if (edge != null) sb.BorderWidthLeft = 4;
        p.AddThemeStyleboxOverride("panel", sb);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 3);
        p.AddChild(v);
        _sideBox.AddChild(p);
        return v;
    }

    static HBoxContainer Row(params Control[] items)
    {
        var r = new HBoxContainer();
        r.AddThemeConstantOverride("separation", 8);
        foreach (var i in items)
        {
            // Short labels beside other items must not wrap, or they collapse to zero width.
            if (i is Label l && (l.SizeFlagsHorizontal & Control.SizeFlags.Expand) == 0)
                l.AutowrapMode = TextServer.AutowrapMode.Off;
            r.AddChild(i);
        }
        return r;
    }

    static Control Spacer() => new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

    /// <summary>Every panel introduction's id (for the --folded shot flag).</summary>
    public static readonly string[] IntroIds = { "Build", "Candidates", "Huts", "Intelligence", "Y-stations", "Intercepts", "The war", "Workload", "Handbook" };

    /// <summary>Introductions unfolded in the panel now open (cleared each time a panel opens).</summary>
    readonly HashSet<string> _introOpen = new();

    /// <summary>
    /// A panel's introduction: in full the first time it's read, then folded to its first sentence with a "More" to unfold it
    /// (until the panel is next opened).
    /// </summary>
    void Intro(string id, string text)
    {
        if (Settings.IntrosRead.Add(id)) { _introOpen.Add(id); Settings.Save(); }
        string first = FirstSentence(text);
        if (_introOpen.Contains(id) || first == text)
        {
            _sideBox.AddChild(UiTheme.Label(text, 13, UiTheme.TextDim));
            return;
        }
        var l = UiTheme.Label(first, 13, UiTheme.TextDim);
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var more = UiTheme.Button("More", () => { _introOpen.Add(id); _sideSig = ""; }, tooltip: "Show the whole introduction");
        more.Flat = true;
        more.AddThemeFontSizeOverride("font_size", 12);
        more.AddThemeColorOverride("font_color", UiTheme.Accent);
        more.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        _sideBox.AddChild(Row(l, more));
    }

    /// <summary>The first sentence of a text (or its first ~110 characters, cut at a word), for folded text.</summary>
    public static string FirstSentence(string text)
    {
        int dot = text.IndexOf(". ", StringComparison.Ordinal);
        if (dot > 0 && dot <= 140) return text[..(dot + 1)];
        if (text.Length <= 140) return text;
        int cut = text.LastIndexOf(' ', 110);
        return text[..(cut > 40 ? cut : 110)] + "…";
    }

    /// <summary>Beside a greyed-out button: why it can't be pressed, so nobody has to hover to find out. A spacer when it can.</summary>
    static Control Why(string? why)
    {
        if (string.IsNullOrEmpty(why)) return Spacer();
        var l = UiTheme.Label(why, 11, UiTheme.TextDim);
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    /// <summary>"any hut", or the open buildings that take this station, then the ones not open yet.</summary>
    string WhereItGoes(StationType t)
    {
        if (Defs.Everywhere.Contains(t)) return "any hut";
        var zones = Defs.ZonesFor(t).Select(id => S.Grid.Zone(id)).OfType<Zone>().ToList();
        string open = string.Join(", ", zones.Where(z => z.Unlocked).Select(z => z.Name));
        string later = string.Join(", ", zones.Where(z => !z.Unlocked).Select(z => z.Name));
        return open.Length == 0 ? $"{later} (not open yet)" : later.Length == 0 ? open : $"{open} · later also {later}";
    }

    /// <summary>The stations a hut takes, by name.</summary>
    static string WhatItTakes(Zone z) =>
        Defs.ZoneRoles.TryGetValue(z.Id, out var role)
            ? string.Join(", ", role.Allows.Select(t => Defs.Station(t).Name.ToLowerInvariant())) + ", plus canteens and tubes anywhere"
            : "anything";

    static string Pct(double v) => $"{(int)Math.Round(v * 100)}%";

    /// <summary>A small vertical gauge for energy or morale.</summary>
    static Control Meter(double value, string what)
    {
        var back = new Panel { CustomMinimumSize = new Vector2(6, 22), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = $"{what} {Pct(value)}" };
        back.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0, 0, 0, 0.35f), new Color(0, 0, 0, 0), 2, 0));
        var fill = new ColorRect
        {
            Color = value < 0.25 ? Gfx.Bad : value < 0.5 ? Gfx.Warn : Gfx.Good,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        fill.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        fill.OffsetTop = -(float)(22 * Math.Clamp(value, 0.05, 1));
        back.AddChild(fill);
        return back;
    }

    static string Stars(int skill) => new string('★', skill) + new string('☆', 3 - skill);

    static string Hours(double minutes)
    {
        if (minutes < 0) return "overdue";
        int h = (int)(minutes / 60);
        return h >= 48 ? $"{h / 24} days" : h >= 1 ? $"{h} h" : $"{(int)minutes} min";
    }

    // ------------------------------------------------------------------ build

    void BuildPanel()
    {
        _sideBox.AddChild(UiTheme.Heading("Build"));
        Intro("Build", "Desks and machines go inside a hut, with clear space in front for whoever works them. Messages come in by dispatch rider (later by teleprinter), pass through each step, and leave by the despatch box.");
        if (S.Funds < 0)
            _sideBox.AddChild(UiTheme.Label($"Overdrawn ({UiTheme.Money(S.Funds)}): nothing can be bought until the Park is back in credit. Finish requests: they pay on completion.", 13, Gfx.Bad));
        // What the chapter's objectives ask to be built comes first, marked, so a new player finds it at once.
        var wanted = S.Mode == GameMode.FreePlay ? new HashSet<StationType>()
            : S.Act.Goals.Where(g => g.Kind == GoalKind.Build && g.Station is not null && g.Progress(S) < g.Target).Select(g => g.Station!.Value).ToHashSet();
        foreach (StationType t in Enum.GetValues<StationType>().OrderBy(t => wanted.Contains(t) ? 0 : 1))
        {
            var d = Defs.Station(t);
            bool unlocked = S.StationUnlocked(t, out var why);
            if (!unlocked && (why == "Not needed yet" || t == StationType.Zygalski && S.IndicatorChanged)) continue;   // not yet, or no longer, in play
            var v = Card();
            var name = UiTheme.Label(d.Name, 15, UiTheme.Text, Fonts.BodyBold);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            v.AddChild(Row(name, UiTheme.Label($"£{d.Cost:N0}", 15, S.Funds >= d.Cost ? UiTheme.Accent : Gfx.Bad, Fonts.BodyBold)));
            if (wanted.Contains(t)) v.AddChild(UiTheme.Caps("An objective of this chapter", 11, Gfx.Good));
            v.AddChild(UiTheme.Label(d.Blurb, 12, UiTheme.TextDim));
            if (!d.Outdoor) v.AddChild(UiTheme.Label("Goes in: " + WhereItGoes(t), 12, UiTheme.Accent));
            string who = d.Operator is Role r ? $"Worked by a {RoleWord(r)}" : "No operator needed";
            var info = UiTheme.Label($"{who} · {d.W}×{d.H} tiles", 12, UiTheme.TextDim);
            info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            Control action = unlocked
                ? UiTheme.Button("Place", () => { _main.BeginBuild(t); ClosePanel(); }, S.Funds >= d.Cost,
                    tooltip: S.Funds >= d.Cost ? "" : $"Needs £{d.Cost:N0}; there's {UiTheme.Money(S.Funds)}.")
                : UiTheme.Label(why, 12, Gfx.Warn);
            action.SetMeta("station", t.ToString());
            if (d.Upkeep > 0) v.AddChild(UiTheme.Label($"Upkeep £{d.Upkeep} a week", 12, UiTheme.TextDim));
            if (d.Seats > 0) v.AddChild(UiTheme.Label($"Seats {d.Seats} for tea breaks{(d.Outdoor ? " · outdoors, on open lawn" : "")}", 12, UiTheme.TextDim));
            v.AddChild(Row(info, action));
        }
    }

    // ------------------------------------------------------------------ staff

    void StaffPanel()
    {
        _sideBox.AddChild(UiTheme.Heading($"Staff ({S.Staff.Count})"));
        _sideBox.AddChild(UiTheme.Label($"Wages £{S.WeeklyWages:N0} and upkeep £{S.WeeklyUpkeep:N0} a week, against a Treasury grant of £{S.WeeklyGrant:N0}" +
                                        (S.EarnedLastWeek > 0 ? $" and £{S.EarnedLastWeek:N0} the ministries paid for requests last week." : "; the ministries pay for each request delivered."), 13, UiTheme.TextDim));
        var morale = UiTheme.Label($"Morale {Pct(S.AverageMorale)} · energy of those on duty {Pct(S.AverageEnergy)}", 13,
            S.AverageMorale < 0.35 ? Gfx.Bad : S.AverageMorale < 0.5 ? Gfx.Warn : UiTheme.Text);
        morale.TooltipText = "Tired people work slower. Canteens restore energy; the common room and rounders restore morale. Two days of very low morale and people ask for a transfer.";
        morale.MouseFilter = Control.MouseFilterEnum.Pass;
        _sideBox.AddChild(morale);
        {
            int living = S.Staff.Count(x => !x.Leaving);
            var bl = UiTheme.Label($"Billets {Math.Min(living, S.Billets)}/{S.Billets}" + (S.Overcrowded > 0 ? $" · {S.Overcrowded} lodged far away (10% slower, unhappy)" : ""), 13,
                S.Overcrowded > 0 ? Gfx.Warn : UiTheme.Text);
            bl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            string? why = S.CanRequisitionBillets();
            _sideBox.AddChild(Row(bl, UiTheme.Button($"+{Defs.BilletBlock} billets (£{Defs.BilletCost})", () => { S.RequisitionBillets(); _sideSig = ""; }, why == null,
                tooltip: why ?? "The billeting officer finds rooms in Bletchley and the villages. Without them, new staff lodge miles away and are bussed in.")));
        }
        if (S.SuppliesActive)
        {
            double days = S.StoresDaysLeft;
            string left = double.IsInfinity(days) ? "" : days < 1 ? " · under a day left" : $" · {days:0} days' worth";
            var sl = UiTheme.Label($"Stores for {Math.Max(0, (int)S.Stores)} messages{left}" + (S.StoresOnOrder ? $" · lorry due {(int)(S.StoresDueAt % Defs.MinutesPerDay / 60):00}:00" : ""), 13,
                S.OutOfStores ? Gfx.Bad : S.StoresLow ? Gfx.Warn : UiTheme.Text);
            sl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            string? why = S.CanOrderStores();
            _sideBox.AddChild(Row(sl, UiTheme.Button($"Order {Defs.StoresOrderSize} (£{S.StoresOrderCost})", () => { S.OrderStores(); _sideSig = ""; }, why == null,
                tooltip: why ?? "Dockets, worksheets, carbons and card stock from the Stationery Office: every message uses some at registration. The lorry takes most of a day.")));
            var so = UiTheme.Label(S.StandingOrder ? "Standing order: reorders when two days are left" : "No standing order", 12, UiTheme.TextDim);
            so.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _sideBox.AddChild(Row(so, UiTheme.Button(S.StandingOrder ? "Cancel" : "Standing order", () => { S.SetStandingOrder(!S.StandingOrder); _sideSig = ""; },
                tooltip: $"The Stationery Office sends more whenever the stores fall to two days' use, at {Defs.StandingOrderMarkup - 1:P0} over the usual price.")));
        }
        {
            string? why = S.CanCallAllHands();
            var ah = UiTheme.Label(S.AllHands ? $"All hands for another {Math.Max(1, Math.Round((S.AllHandsUntil - S.Now) / 60))} h" : "Deadline slipping?", 13, S.AllHands ? Gfx.Warn : UiTheme.TextDim);
            ah.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _sideBox.AddChild(Row(ah, UiTheme.Button("Call all hands", () => { S.CallAllHands(); _sideSig = ""; }, why == null,
                tooltip: why ?? $"Call everyone off watch back in for {Defs.AllHandsHours:0} hours, at £{Defs.AllHandsPay} overtime each. Morale falls for all, those called in tire half as fast again, and you can't do it again for {Defs.AllHandsCooldownDays:0} days.")));
        }
        if (S.CrosswordOpen)
        {
            string? why = S.CantHoldCrossword();
            var cw = UiTheme.Label(S.CrosswordPending ? "Crossword competition under way" : "Short of cryptanalysts?", 13, S.CrosswordPending ? UiTheme.Text : UiTheme.TextDim);
            cw.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _sideBox.AddChild(Row(cw, UiTheme.Button($"Telegraph crossword (£{Defs.CrosswordCost})", () => { S.HoldCrossword(); _sideSig = ""; }, why == null,
                tooltip: why ?? $"The Daily Telegraph sets a puzzle and times the fastest solvers at its offices. {Defs.CrosswordRecruits} skilled cryptanalysts, crossword fiends all " +
                               $"(faster, better cribs), join the candidates in {Defs.CrosswordDays} days and stay until hired or the next competition. Once every {Defs.CrosswordEveryDays:0} days.")));
        }
        if (S.SocialOpen)
        {
            string? why = S.CantHoldSocial();
            var so = UiTheme.Label(S.SocialPending ? $"{char.ToUpper(S.NextSocial.name[0])}{S.NextSocial.name[1..]} at 19:30" : "Recreational Club", 13,
                                   S.SocialPending ? UiTheme.Text : S.AverageMorale < Defs.MoraleBaseline - 0.15 ? Gfx.Warn : UiTheme.TextDim);
            so.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _sideBox.AddChild(Row(so, UiTheme.Button($"Arrange an evening (£{Defs.SocialCost})", () => { S.HoldSocial(); _sideSig = ""; }, why == null,
                tooltip: why ?? $"The Recreational Club puts on {S.NextSocial.name} at half past seven: everyone's spirits rise ({Defs.SocialMorale * 100:0} points), " +
                               $"and quarrels are often made up. Once every {Defs.SocialEveryDays:0} days.")));
        }
        if (S.WatchesAvailable)
        {
            var v = Card(UiTheme.Accent);
            v.AddChild(UiTheme.Label("Three watches", 15, UiTheme.Text, Fonts.BodyBold));
            v.AddChild(UiTheme.Label("Day 08–16, evening 16–24, night 00–08. People on the long day (07–22) grow weary now the work never stops; " +
                                     "more than a week of nights straight wears anyone but a night owl down twice as fast.", 12, UiTheme.TextDim));
            // Who is on which watch against what the work needed, by role; then anyone left on long days.
            var use = S.Utilisation();
            if (use.Staffing.Count > 0 && use.Hours >= 8) StaffingTable(use, v);
            var roles = Enum.GetValues<Role>().Where(S.RoleOpen).ToList();
            var table = new GridContainer { Columns = roles.Count + 1 };
            table.AddThemeConstantOverride("h_separation", 4);
            table.AddThemeConstantOverride("v_separation", 0);
            Label Cell(string text, Color colour, bool first = false)
            {
                var c = UiTheme.Label(text, 12, colour, wrap: false);
                if (first) c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                else { c.CustomMinimumSize = new Vector2(36, 0); c.HorizontalAlignment = HorizontalAlignment.Center; }
                return c;
            }
            table.AddChild(Cell("", UiTheme.TextDim, true));
            foreach (var r in roles)
            {
                var h = Cell(ShortRole(r), Gfx.RoleColor(r).Lightened(0.35f));
                h.TooltipText = Defs.Roles[r].Plural;
                h.MouseFilter = Control.MouseFilterEnum.Pass;
                table.AddChild(h);
            }
            bool counts = use.Staffing.Count == 0 || use.Hours < 8;
            foreach (var sh in counts ? new[] { Shift.Day, Shift.Evening, Shift.Night, Shift.Office } : new[] { Shift.Office })
            {
                var sd = Defs.Shifts[sh];
                bool office = sh == Shift.Office;
                int total = S.Staff.Count(x => x.Shift == sh && !x.Leaving);
                if (office && total == 0) continue;
                string watch = sh switch { Shift.Day => "Day", Shift.Evening => "Evening", Shift.Night => "Night", _ => "Long days" };
                table.AddChild(Cell($"{watch} {sd.StartHour:00}–{sd.EndHour:00}", office ? Gfx.Warn : UiTheme.Text, true));
                foreach (var r in roles)
                {
                    int n = S.Staff.Count(x => x.Role == r && x.Shift == sh && !x.Leaving);
                    table.AddChild(Cell(n.ToString(), n == 0 ? UiTheme.TextDim : office ? Gfx.Warn : UiTheme.Text));
                }
            }
            if (table.GetChildCount() > roles.Count + 1) v.AddChild(table);
            var rota = new CheckBox { Text = "Rotate weekly", ButtonPressed = S.RotateWatches,
                TooltipText = "Change the watches round every week (day to evening to night), as Hut 6 did, so nobody is stuck on nights. Night owls lose their pick of the night watch." };
            rota.AddThemeFontSizeOverride("font_size", 12);
            rota.Toggled += on => { S.SetRotation(on); _sideSig = ""; };
            v.AddChild(Row(rota, Spacer(), UiTheme.Button("Balance watches", () => { S.BalanceWatches(null); _sideSig = ""; Toast("Everyone is spread across the three watches."); },
                tooltip: "Spread every role evenly over day, evening and night. Night owls take the night, unless the watches rotate.")));
        }
        // Who stood about most of their watch: the ones to move or let go, listed first in their role.
        var spareByRole = Enum.GetValues<Role>().Where(S.RoleOpen).Select(ro => (ro, n: S.Staff.Count(x => x.Role == ro && S.SpareHand(x)))).Where(x => x.n > 0).ToList();
        {
            var head = UiTheme.Label(spareByRole.Count == 0
                    ? S.UseHours < 8 ? "Who is idle shows here once there's most of a day on record." : "Nobody stood idle most of their watch over the last day."
                    : "Idle most of their watch over the last day: " + string.Join(", ", spareByRole.Select(x => $"{x.n} {(x.n == 1 ? RoleWord(x.ro) : GameState.PluralWord(x.ro))}")) +
                      (S.CourseAdvice() is var (cf, ct)
                          ? $". Send some {GameState.PluralWord(cf)} on a course to work as {GameState.PluralWord(ct)} too (click one, then Learns), move them to a short watch, or dismiss some."
                          : ". Move them to a short watch, or dismiss some (click one)."),
                12, spareByRole.Count == 0 ? UiTheme.TextDim : Gfx.Warn);
            head.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var sort = new CheckBox { Text = "Idlest first", ButtonPressed = _idleFirst, TooltipText = "List each role by how idle they were on watch over the last day, the idlest first." };
            sort.AddThemeFontSizeOverride("font_size", 12);
            sort.Toggled += on => { _idleFirst = on; _sideSig = ""; };
            _sideBox.AddChild(head);
            _sideBox.AddChild(Row(UiTheme.Caps("By role", 11, UiTheme.Accent), Spacer(), sort));
        }
        foreach (Role role in Enum.GetValues<Role>().Where(S.RoleOpen))
        {
            var people = S.Staff.Where(s => s.Role == role).ToList();
            if (_idleFirst)
                people = people.OrderBy(p => p.Use is { } l && l.OnWatch() >= 60 ? 0 : 1)
                               .ThenByDescending(p => p.Use?.OfWatch(PersonUse.Idle) ?? 0).ToList();
            int spare = spareByRole.FirstOrDefault(x => x.ro == role).n;
            var rd = Defs.Roles[role];
            // Long lists start folded, with a count of what everyone is doing; click the heading to open one.
            bool open = _rolesOpen.TryGetValue(role, out var o) ? o : people.Count <= 8;
            var r0 = role;
            var toggle = UiTheme.Button($"{(open ? "▾" : "▸")} {rd.Plural.ToUpperInvariant()} · {people.Count}", () => { _rolesOpen[r0] = !open; _sideSig = ""; },
                tooltip: rd.Blurb + (open ? "" : "\n\nClick to list them."));
            toggle.Flat = true;
            toggle.Alignment = HorizontalAlignment.Left;
            toggle.AddThemeFontOverride("font", Fonts.BodyBold);
            toggle.AddThemeFontSizeOverride("font_size", 12);
            var summary = UiTheme.Label(open ? spare > 0 ? $"{spare} spare" : "" : (spare > 0 ? $"{spare} spare · " : "") +
                string.Join(" · ", people.GroupBy(Doing).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {g.Key}")), 11, spare > 0 ? Gfx.Warn : UiTheme.TextDim);
            summary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var header = Row(new ColorRect { Color = Gfx.RoleColor(role), CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter },
                toggle, summary);
            if (spare > 0 && S.Expendable(role) is { Count: > 0 } go)
            {
                var r1 = role;
                var letGo = UiTheme.Button($"Let {go.Count} go", () => { S.LetGo(r1); _sideSig = ""; },
                    tooltip: $"Dismiss the {(go.Count == 1 ? "one" : go.Count.ToString())} idlest on watches with hands to spare ({string.Join(", ", go.Select(x => x.Name))}), " +
                             $"keeping every watch what its busiest hour needed and one more. Saves £{go.Sum(x => x.Salary)} a week in wages, and the Treasury counts idle heads.");
                letGo.AddThemeFontSizeOverride("font_size", 12);
                header.AddChild(letGo);
            }
            _sideBox.AddChild(header);
            if (!open) continue;
            foreach (var p in people) _sideBox.AddChild(PersonRow(p));
        }
        _sideBox.AddChild(new HSeparator());
        _sideBox.AddChild(UiTheme.Heading("Candidates", 17));
        Intro("Candidates", "Recommended by colleges, the Foreign Office and the Daily Telegraph crossword. New names arrive every week. The first week's wages are paid on hiring.");
        foreach (var c in S.Candidates.OrderBy(c => c.Notable == null).ThenBy(c => c.Role))
        {
            var v = Card(c.Notable != null ? UiTheme.Accent : null);
            var name = UiTheme.Label(c.Name, 15, UiTheme.Text, Fonts.BodyBold);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            v.AddChild(Row(name, UiTheme.Label(Stars(c.Skill), 14, UiTheme.Accent)));
            var role = UiTheme.Label($"{Defs.Roles[c.Role].Name} · {(c.American ? "US Army, paid by the Army" : $"£{c.Salary}/week")}", 12, UiTheme.TextDim);
            role.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var traits = c.KnownTraits().ToList();
            if (traits.Count > 0)
                v.AddChild(UiTheme.Label(string.Join(" · ", traits.Select(t => Defs.Traits[t].Name)), 12, UiTheme.Accent));
            if (c.Bio != null)
            {
                v.AddChild(UiTheme.Label(c.Bio, 12, UiTheme.Text));
                if (c.Specialties.Count > 0) v.AddChild(UiTheme.Label("Specialist: " + string.Join(", ", c.Specialties.Select(t => Defs.Station(t).Name.ToLowerInvariant())), 12, UiTheme.Accent));
            }
            var id = c.Id;
            var hire = UiTheme.Button("Hire", () =>
                {
                    S.Hire(id, _hireShift.TryGetValue(id, out var hs) ? hs : null, _hireZone.TryGetValue(id, out var hz) ? hz : null);
                    _hireShift.Remove(id);
                    _hireZone.Remove(id);
                }, S.Funds >= c.Salary,
                tooltip: c.Notable != null ? "Reports at once." : $"Vetting and travel: reports in about {Math.Round(Defs.VettingHours(c.Role, S.Difficulty))} hours. Hire ahead of need.");
            if (!hire.HasMeta("tut")) hire.SetMeta("tut", "hire:" + c.Role);
            v.AddChild(Row(role, hire));
            // Nothing to choose until there's a second building for the role or the watches begin.
            if (S.WatchesAvailable || (!GameState.Roams(c.Role) && S.PostsFor(c.Role).Skip(1).Any()))
                v.AddChild(HirePosting(c));
        }
    }

    /// <summary>
    /// Where a candidate will work: the watch (once there are watches) and the building, both suggested where their
    /// kind of desk has fewest people. Messengers aren't posted: they carry between buildings.
    /// </summary>
    Control HirePosting(Staff c)
    {
        int id = c.Id;
        var (sugShift, sugZone) = S.SuggestPost(c.Role);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        if (GameState.Roams(c.Role))
            row.AddChild(UiTheme.Label("Carries paper between all the buildings", 12, UiTheme.TextDim));
        else
        {
            var zones = S.PostsFor(c.Role).ToList();
            string pick = _hireZone.TryGetValue(id, out var z) && zones.Any(x => x.Id == z) ? z : sugZone ?? zones.FirstOrDefault()?.Id ?? "mansion";
            var where = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, CustomMinimumSize = new Vector2(80, 0) };
            for (int i = 0; i < zones.Count; i++)
            {
                where.AddItem(zones[i].Name + (zones[i].Id == sugZone ? " ★" : ""), i);
                if (zones[i].Id == pick) where.Select(i);
            }
            where.TooltipText = "The building they're posted to (★ most needed). They work, carry and wait only there until you post them elsewhere (select them in the list: Posted to).";
            where.ItemSelected += idx => { _hireZone[id] = zones[where.GetItemId((int)idx)].Id; _sideSig = ""; };
            row.AddChild(where);
        }
        if (S.WatchesAvailable)
        {
            var watches = new[] { Shift.Day, Shift.Evening, Shift.Night };
            string? zoneNow = GameState.Roams(c.Role) ? null : _hireZone.TryGetValue(id, out var z2) ? z2 : sugZone;
            var sug = _hireZone.ContainsKey(id) ? S.SuggestShift(c.Role, zoneNow) : sugShift;
            var pickShift = _hireShift.TryGetValue(id, out var hs) ? hs : sug;
            var when = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, CustomMinimumSize = new Vector2(80, 0) };
            foreach (var w in watches)
            {
                when.AddItem(Defs.Shifts[w].Name + (w == sug ? " ★" : ""), (int)w);
                if (w == pickShift) when.Select(when.ItemCount - 1);
            }
            when.TooltipText = "The watch they'll keep (★ most needed). You can change it later in their details.";
            when.ItemSelected += idx => _hireShift[id] = (Shift)when.GetItemId((int)idx);
            row.AddChild(when);
        }
        return row;
    }

    /// <summary>
    /// One person in the Staff list: the name (which gives way first when space is short), skill, what they're doing
    /// and their watch in a word each, their day, energy and morale. The full wording is in the tooltip.
    /// </summary>
    Control PersonRow(Staff p)
    {
        var sd = Defs.Shifts[p.Shift];
        string shift = S.WatchesAvailable ? p.Shift switch { Shift.Office => "long", Shift.Evening => "eve", _ => sd.Name.Split(' ')[0].ToLowerInvariant() } : "";
        string where = p.Posted is string po && S.Grid.Zone(po) is { } pz ? $"\nPosted to {pz.Name}." : "";
        string pinned = p.Pinned is int pid && S.StationById(pid) is { } pst ? $"\nPinned to the {pst.Def.Name.ToLowerInvariant()}." : p.Pinned != null ? "\nPinned to a desk." : "";
        string tip = $"{p.Name}, {RoleWord(p.Role)} {Stars(p.Skill)}: {Doing(p)}" +
                     (S.WatchesAvailable ? $", {sd.Name.ToLowerInvariant()} ({sd.StartHour:00}–{sd.EndHour % 24:00})" : "") + "." + where + pinned +
                     "\nClick to select them.";
        var b = UiTheme.Button(p.Name, () => { _main.Select(null, p.Id); if (!p.Away) _main.CenterOnTile((float)p.X, (float)p.Y); });
        b.Alignment = HorizontalAlignment.Left;
        b.Flat = true;
        b.AddThemeFontSizeOverride("font_size", 13);
        b.ClipText = true;   // a long name must not widen the panel
        b.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        b.TooltipText = tip;
        b.CustomMinimumSize = new Vector2(60, 0);
        b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (p.Away) b.AddThemeColorOverride("font_color", UiTheme.TextDim);
        var stars = UiTheme.Label(Stars(p.Skill), 12, UiTheme.Accent, wrap: false);
        var state = UiTheme.Label(DoingShort(p) + (shift != "" ? $" · {shift}" : "") + (p.Pinned != null ? " · pinned" : ""), 12,
            p.Away ? UiTheme.TextDim : UiTheme.Text, wrap: false);
        foreach (var l in new[] { stars, state })
        {
            l.TooltipText = tip;
            l.MouseFilter = Control.MouseFilterEnum.Pass;
            l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        }
        return Row(b, stars, state, DayBar(p), Meter(p.Energy, "Energy"), Meter(p.Morale, "Morale"));
    }

    /// <summary>What someone is doing in a word, for a Staff row (the tooltip has <see cref="Doing"/>).</summary>
    string DoingShort(Staff p) =>
        p.Arriving(S.Now) ? "arriving" : p.IsSick(S.Now) ? (p.OffReason switch { "leave" => "on leave", "breakdown" => "broken down", "course" => "on a course", _ => "sick" }) : p.Away ? "off"
        : p.HeadOf != null && p.Task == null ? "head" : p.Task switch
        {
            null => S.BackWorking && p.Role is Role.Cryptanalyst or Role.Wren ? "on back traffic" : "idle",
            { Kind: TaskKind.Operate } => "working",
            { Kind: TaskKind.Rest, Loafing: true } => "free",
            { Kind: TaskKind.Rest } => "break",
            { Kind: TaskKind.Leave } => "going off",
            { Kind: TaskKind.Repair, Phase: < 2 } => "mending",
            { Kind: TaskKind.Repair } => "servicing",
            _ => "carrying",
        };

    /// <summary>The Staff panel lists each role idlest first (by the last day on watch).</summary>
    bool _idleFirst = true;

    /// <summary>
    /// A person's last day on watch in a small bar (working, carrying, walking, on a break, idle), with how idle they
    /// were beside it when it was most of the time.
    /// </summary>
    Control DayBar(Staff p)
    {
        var log = p.Use;
        double duty = log?.OnWatch() ?? 0;
        var shares = new double[5];
        if (log != null && duty > 0) for (int k = 0; k < 5; k++) shares[k] = log.In(k) / duty;
        double idle = shares[(int)PersonUse.Idle];
        string tip = duty < 30 ? "No time on watch on record in the last day."
            : $"Last day: {duty / 60:0} h on watch, " + UseLook.Summary(shares, UseLook.PersonWords) + (S.SpareHand(p) ? ".\nIdle most of the watch: move them to a short watch, or let them go." : ".");
        var bar = new ShareBar { Shares = duty < 30 ? Array.Empty<double>() : shares, Colours = UseLook.Person, TooltipText = tip,
                                 CustomMinimumSize = new Vector2(44, 7), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var idleLabel = UiTheme.Label(duty >= 30 && idle >= 0.5 ? $"{Math.Round(idle * 100):0}%" : "", 11, Gfx.Warn, wrap: false);
        idleLabel.CustomMinimumSize = new Vector2(28, 0);
        idleLabel.HorizontalAlignment = HorizontalAlignment.Right;
        idleLabel.TooltipText = tip;
        idleLabel.MouseFilter = Control.MouseFilterEnum.Pass;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 3);
        row.AddChild(idleLabel);
        row.AddChild(bar);
        return row;
    }

    /// <summary>Which role lists the player has opened or folded in the Staff panel.</summary>
    readonly Dictionary<Role, bool> _rolesOpen = new();

    /// <summary>A role in running text: "a clerk", but "a Wren" (a member of the WRNS).</summary>
    static string RoleWord(Role r) => Defs.RoleWord(r);

    static string ShortRole(Role r) => r switch
    {
        Role.Clerk => "Clerks", Role.Cryptanalyst => "Crypt.", Role.Wren => "Wrens", Role.Linguist => "Ling.", Role.Fitter => "Fitters", _ => "Msgrs",
    };

    /// <summary>What someone is doing, in a word or two, for the Staff list.</summary>
    string Doing(Staff p) =>
        p.Arriving(S.Now) ? "arriving" : p.IsSick(S.Now) ? (p.OffReason switch { "leave" => "on leave", "breakdown" => "broken down", "course" => "on a course", _ => "off sick" }) : p.Away ? "off watch"
        : p.HeadOf != null && p.Task == null ? $"head of {S.Grid.Zone(p.HeadOf)?.Name}" : p.Task switch
        {
            null => S.BackWorking && p.Role is Role.Cryptanalyst or Role.Wren ? "on back traffic" : "idle",
            { Kind: TaskKind.Operate } => "working",
            { Kind: TaskKind.Rest, Loafing: true } => S.BackWorking && p.Role is Role.Cryptanalyst or Role.Wren ? "on back traffic" : "sitting, free",
            { Kind: TaskKind.Rest } => "on a break",
            { Kind: TaskKind.Leave } => "going off",
            _ => "carrying",
        };

    // --------------------------------------------------------------- requests

    void RequestsPanel()
    {
        _sideBox.AddChild(UiTheme.Heading("Requests"));
        var offers = S.Contracts.Where(c => c.State == ContractState.Offered).ToList();
        var active = S.Contracts.Where(c => c.State == ContractState.Active).ToList();
        var done = S.Contracts.Where(c => c.State is ContractState.Completed or ContractState.Failed).ToList();

        _sideBox.AddChild(UiTheme.Caps($"In progress · {active.Count}", 12, UiTheme.Accent));
        if (active.Count == 0) _sideBox.AddChild(UiTheme.Label("Nothing in progress. Accept a request below to start traffic flowing.", 13, UiTheme.TextDim));
        foreach (var c in active.OrderBy(c => c.Due))
        {
            double left = c.Due - S.Now;
            var v = Card(left < Defs.MinutesPerDay * 0.5 ? Gfx.Bad : Gfx.NetColor(c.Net));
            ContractHeader(v, c);
            var bar = new ProgressBar { MinValue = 0, MaxValue = c.Quantity, Value = c.Delivered, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) };
            v.AddChild(bar);
            var status = UiTheme.Label($"{c.Delivered}/{c.Quantity} delivered · {c.Spawned - c.Delivered} in the huts · {c.Quantity - c.Spawned} still to be intercepted", 12, UiTheme.TextDim);
            v.AddChild(status);
            if (c.Standing)
            {
                int next = c.MilestonesChecked + 1;
                double at = GameState.MilestoneAt(c, next);
                int need = c.PerDay * next;
                bool behind = c.Delivered < need && at - S.Now < 8 * 60;
                v.AddChild(UiTheme.Label(next <= c.TotalDays
                        ? $"Day {next} of {c.TotalDays}: {need} delivered in all by {S.DateOf((int)(at / Defs.MinutesPerDay)):ddd} {(int)(at % Defs.MinutesPerDay) / 60:00}:{(int)(at % 60):00} ({Hours(at - S.Now)}) · milestones met {c.MilestonesMet}/{c.MilestonesChecked}"
                        : $"Milestones met {c.MilestonesMet}/{c.MilestonesChecked}", 12, behind ? Gfx.Bad : UiTheme.Text));
            }
            // The last line carries the priority button, so a card is a line shorter.
            var last = c.Standing
                ? UiTheme.Label($"£{c.MilestonePay:N0} for each day met · £{c.Reward:N0} bonus on completion (half if a day was missed)", 12, UiTheme.TextDim)
                : UiTheme.Label($"Due in {Hours(left)} · £{c.Reward:N0} on completion", 12, left < Defs.MinutesPerDay * 0.5 ? Gfx.Bad : UiTheme.Text);
            if (c.Priority)
            {
                last.Text += " · put first";
                last.TooltipText = "Put first: its paper is worked and carried before anything else.";
                last.MouseFilter = Control.MouseFilterEnum.Pass;
            }
            last.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            last.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            var cid = c.Id;
            v.AddChild(Row(last, UiTheme.Button(c.Priority ? "Back in turn" : "Put first", () => { S.SetPriority(cid, !c.Priority); _sideSig = ""; },
                tooltip: c.Priority ? "Let this request's paper take its turn by deadline again."
                                    : "Every desk, machine and runner handles this request's paper before anything else's.")));
        }

        _sideBox.AddChild(UiTheme.Caps($"Offered · {offers.Count}", 12, UiTheme.Accent));
        if (offers.Count == 0) _sideBox.AddChild(UiTheme.Label("No new requests. Whitehall will be in touch.", 13, UiTheme.TextDim));
        foreach (var c in offers)
        {
            var v = Card(Gfx.NetColor(c.Net));
            ContractHeader(v, c);
            v.AddChild(UiTheme.Label(c.Flavour, 12, UiTheme.TextDim));
            v.AddChild(UiTheme.Label(c.Standing
                ? $"{c.PerDay} messages every day for {c.TotalDays} days · £{c.MilestonePay:N0} each day met, £{c.Reward:N0} bonus · reputation +{c.Rep}. A day missed costs {Defs.MilestoneMissedRep} reputation."
                : $"{c.Quantity} messages within {Hours(c.Days * Defs.MinutesPerDay)} · £{c.Reward:N0} · reputation +{c.Rep}", 13, UiTheme.Text));
            if (c.Flash)
                v.AddChild(UiTheme.Label($"Urgent. Ignoring it costs {Defs.FlashIgnoredRep:0} reputation; declining costs {Defs.FlashIgnoredRep / 2:0}. Best taken when today's key is already broken.", 11, Gfx.Warn));
            var nd = Defs.NetOf(c.Net);
            if (nd.Family == Family.Enigma && S.KeyRouteProblem(c.Net) is string prob && !S.KeyBroken(c.Net, S.Day))
                v.AddChild(UiTheme.Label($"Warning: no way to break {nd.Name} yet ({prob.ToLowerInvariant()}).", 12, Gfx.Warn));
            else if (nd.Family == Family.Hand && !S.Stations.Any(s => s.Type == StationType.HandCipher))
                v.AddChild(UiTheme.Label("Warning: you have no hand-cipher desk.", 12, Gfx.Warn));
            var id = c.Id;
            var exp = UiTheme.Label($"Offer lapses in {Hours(c.OfferExpires - S.Now)}", 12, UiTheme.TextDim);
            exp.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var accept = UiTheme.Button("Accept", () => S.Accept(id));
            accept.SetMeta("tut", "accept");
            v.AddChild(Row(exp, UiTheme.Button("Decline", () => S.Decline(id)), accept));
        }

        if (done.Count > 0)
        {
            _sideBox.AddChild(UiTheme.Caps("Recently finished", 12, UiTheme.TextDim));
            foreach (var c in done)
                _sideBox.AddChild(UiTheme.Label($"{(c.State == ContractState.Completed ? "Completed" : "Missed")}: {c.Title} ({Defs.IssuerNames[c.Issuer]})", 12,
                    c.State == ContractState.Completed ? Gfx.Good : Gfx.Bad));
        }
    }

    static void ContractHeader(Control v, Contract c)
    {
        bool urgent = c.Tag != null || c.Flash;
        var issuer = UiTheme.Caps(Defs.IssuerNames[c.Issuer] + " · " + Defs.NetOf(c.Net).Name + (urgent ? " · MOST IMMEDIATE" : "") +
            (c.Priority && c.State == ContractState.Active ? " · FIRST" : ""), 11, urgent || c.Priority ? Gfx.Bad : UiTheme.TextDim);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        head.AddChild(NetMark.For(c.Net, 13));
        issuer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(issuer);
        v.AddChild(head);
        v.AddChild(UiTheme.Label(c.Title, 15, UiTheme.Text, Fonts.BodyBold));
    }

    // --------------------------------------------------------------- research

    void ResearchPanel()
    {
        _sideBox.AddChild(UiTheme.Heading("Research"));
        _sideBox.AddChild(UiTheme.Label($"You have {S.Points} breakthroughs. Every delivered decrypt teaches us something; spend what you learn on new methods.", 13, UiTheme.TextDim));
        foreach (var id in S.ResearchAvailable.Where(r => !S.Researched.Contains(r)))
        {
            var r = Defs.Research[id];
            var v = Card(UiTheme.Accent);
            var name = UiTheme.Label(r.Name, 15, UiTheme.Text, Fonts.BodyBold);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            v.AddChild(Row(name, UiTheme.Label($"{r.Cost} breakthroughs", 13, S.Points >= r.Cost ? UiTheme.Accent : Gfx.Bad)));
            v.AddChild(UiTheme.Label(r.Blurb, 12, UiTheme.TextDim));
            v.AddChild(Row(Spacer(), UiTheme.Button("Research", () => S.DoResearch(id), S.Points >= r.Cost)));
        }
        if (S.Researched.Count > 0)
        {
            _sideBox.AddChild(UiTheme.Caps("In use", 12, UiTheme.TextDim));
            foreach (var id in S.Researched)
                _sideBox.AddChild(UiTheme.Label($"{Defs.Research[id].Name}: {Defs.Research[id].Blurb}", 12, UiTheme.TextDim));
        }
        _sideBox.AddChild(UiTheme.Label("More methods will become possible as the war goes on.", 12, UiTheme.TextDim));
        {
            // Breakthroughs left over once the methods are paid for can go to Whitehall as standing.
            var v = Card(S.ResearchDone() ? UiTheme.Accent : null);
            var name = UiTheme.Label("Report to the Chiefs of Staff", 15, UiTheme.Text, Fonts.BodyBold);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            v.AddChild(Row(name, UiTheme.Label($"{S.ReportCost} breakthroughs", 13, S.Points >= S.ReportCost ? UiTheme.Accent : Gfx.Bad)));
            v.AddChild(UiTheme.Label($"Write up what the Park has learned for the Chiefs: +{Defs.ReportRep:0} reputation. Each report costs {Defs.ReportCostStep} more than the last, " +
                                     $"and there's one a fortnight at most{(S.ChiefsReports > 0 ? $" ({S.ChiefsReports} sent so far)" : "")}. " +
                                     "Keep enough back for the methods the war will bring.", 12, UiTheme.TextDim));
            string? why = S.CantReport();
            v.AddChild(Row(Why(why), UiTheme.Button("Send a report", () => { S.ReportToChiefs(); _sideSig = ""; }, why == null, tooltip: why ?? "")));
        }
    }

    // ------------------------------------------------------------------- huts

    void HutsPanel()
    {
        _sideBox.AddChild(UiTheme.Heading("Huts"));
        Intro("Huts", "Bletchley grew from a country house into a sprawl of timber huts. Each one opens when the work calls for it.");
        foreach (var z in S.Grid.Zones)
        {
            var v = Card(z.Unlocked ? Gfx.Good : z.Available ? UiTheme.Accent : null);
            var name = UiTheme.Label(z.Name, 15, UiTheme.Text, Fonts.BodyBold);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            Control right = z.Unlocked ? UiTheme.Label("Open", 13, Gfx.Good)
                : z.Available ? UiTheme.Button($"Open for £{z.Cost:N0}", () => S.UnlockZone(z.Id), S.Funds >= z.Cost)
                : UiTheme.Label("Not yet authorised", 12, UiTheme.TextDim);
            v.AddChild(Row(name, right));
            if (Defs.ZoneRoles.TryGetValue(z.Id, out var role))
            {
                v.AddChild(UiTheme.Label(role.Purpose, 12, UiTheme.Text));
                v.AddChild(UiTheme.Label("Takes: " + WhatItTakes(z), 12, UiTheme.TextDim));
            }
            var zone = z;
            var go = UiTheme.Button("Show", () => _main.CenterOnTile(zone.X + zone.W / 2f, zone.Y + zone.H / 2f));
            go.Flat = true;
            go.AddThemeColorOverride("font_color", UiTheme.Accent);
            v.AddChild(Row(UiTheme.Label($"{z.W - 2}×{z.H - 2} tiles of floor", 12, UiTheme.TextDim), Spacer(), go));
        }
        OutstationCard();
    }

    // -------------------------------------------------------------- inspector

    void BuildInspector()
    {
        _inspector = new PanelContainer { Visible = false };
        _inspector.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _inspector.OffsetLeft = -332; _inspector.OffsetRight = -12; _inspector.OffsetBottom = -12; _inspector.OffsetTop = -12;
        _inspector.GrowVertical = Control.GrowDirection.Begin;
        _inspector.GrowHorizontal = Control.GrowDirection.Begin;
        _root.AddChild(_inspector);
        _inspBox = new VBoxContainer();
        _inspBox.AddThemeConstantOverride("separation", 4);
        _inspector.AddChild(_inspBox);
    }

    public void ShowInspector(int? station, int? staff)
    {
        _inspStation = station;
        _inspStaff = staff;
        _inspZone = null;
        _inspSig = "";
        RefreshInspector();
    }

    public void ShowZone(Zone z)
    {
        _inspStation = _inspStaff = null;
        _inspZone = z;
        _inspSig = "";
        RefreshInspector();
    }

    void RefreshInspector()
    {
        if (_inspector == null) return;
        var st = _inspStation is int a ? S.StationById(a) : null;
        var sf = _inspStaff is int b ? S.StaffById(b) : null;
        var z = _inspZone;
        _inspector.Visible = st != null || sf != null || z != null;
        if (!_inspector.Visible) { _inspSig = ""; return; }

        string sig = st != null
            ? $"st{st.Id}{st.Input.Count}{st.Output.Count}{st.OperatorId}{st.OperatorPresent}{st.Current}{(int)(st.Progress * 20)}{st.PendingRemoval}{st.X},{st.Y}|{string.Join(",", S.PinnedTo(st.Id).Select(p => p.Id))}|{S.Funds >= Defs.MoveCost}|{(int)(st.Wear * 20)}{st.IsDown(S.Now)}{S.Funds >= Defs.ServiceCost}|{(st.Receives == null ? "all" : string.Join(",", st.Receives))}{S.ActiveNets.Count}|{OldestHours(st)}|{(int)(S.BookRecovered * 100)}"
            : sf != null ? $"sf{sf.Id}{sf.Task?.Kind}{sf.Task?.Dest}{sf.Leaving}{sf.Decrypts}{sf.Away}{sf.IsSick(S.Now)}{sf.Shift}{sf.Pinned}{sf.Posted}{sf.HeadOf}{sf.HeadStyle}{S.Grid.Zones.Count(z => z.Unlocked)}{(int)(sf.Energy * 10)}{(int)(sf.Morale * 10)}{(int)(sf.Strain * 10)}{sf.Rival}{(sf.Rival != null && S.WithRival(sf))}{sf.OffReason}{sf.Second}{(int)S.Funds / 50}{sf.CarelessKnown}{(int)(sf.Experience / 60)}{sf.Skill}"
            : $"z{z!.Id}{z.Unlocked}{S.Funds >= z.Cost}{S.ColdZones()?.Contains(z.Id)}{S.HotZones()?.Contains(z.Id)}";
        if (sig == _inspSig) return;
        _inspSig = sig;
        Clear(_inspBox);

        // Reset the panel height so it shrinks to fit new content.
        _inspector.OffsetTop = _inspector.OffsetBottom;

        if (st != null) InspectStation(st);
        else if (sf != null) InspectStaff(sf);
        else InspectZone(z!);
    }

    /// <summary>Which networks' intercepts come in on this teleprinter. Unrouted networks go to any teleprinter.</summary>
    void InspectTeleprinter(Station st)
    {
        var nets = Enum.GetValues<Net>().Where(S.ActiveNets.Contains).ToList();   // only networks that have come on the air
        if (nets.Count == 0) return;
        _inspBox.AddChild(UiTheme.Label(st.Receives == null ? "Receives every network. Untick one to send it to another teleprinter." : "Receives:", 12, UiTheme.TextDim));
        var grid = new GridContainer { Columns = 3 };
        int sid = st.Id;
        foreach (var n in nets)
        {
            var cb = new CheckBox { Text = Defs.NetOf(n).Name, ButtonPressed = st.ReceivesNet(n), FocusMode = Control.FocusModeEnum.None };
            cb.AddThemeFontSizeOverride("font_size", 12);
            cb.AddThemeColorOverride("font_color", Gfx.NetColor(n));
            var net = n;
            cb.Toggled += on => { S.SetReceives(sid, net, on); _inspSig = ""; };
            grid.AddChild(cb);
        }
        _inspBox.AddChild(grid);
        int others = S.Stations.Count(x => x.Type == StationType.Inbox && x.Id != st.Id);
        if (others == 0)
            _inspBox.AddChild(UiTheme.Label("With one teleprinter everything comes in here anyway. Build another in a hut to split the traffic.", 11, UiTheme.TextDim));
    }

    void InspectStation(Station st)
    {
        var d = st.Def;
        _inspBox.AddChild(UiTheme.Heading(d.Name, 18));
        _inspBox.AddChild(UiTheme.Label(d.Blurb, 12, UiTheme.TextDim));
        if (st.Obsolete) _inspBox.AddChild(UiTheme.Label("Obsolete since the indicator change. Remove it to get half its cost back.", 12, Gfx.Warn));
        if (d.Operator is Role r)
        {
            var op = st.OperatorId is int id ? S.StaffById(id) : null;
            string who = st.IsDown(S.Now) ? (st.Servicing ? "Off for servicing" : "Out of action")
                : op == null
                ? (st.Input.Count > 0 ? $"Waiting for a free {RoleWord(r)}" : $"Unstaffed. Needs a {RoleWord(r)}.")
                : st.OperatorPresent ? $"{op.Name} at work" : $"{op.Name} on the way";
            _inspBox.AddChild(UiTheme.Label(who, 13, op == null && st.Input.Count > 0 ? Gfx.Warn : UiTheme.Text));
            _inspBox.AddChild(UiTheme.Label($"In tray {st.Input.Count}/{d.InputCap} · out tray {st.Output.Count}", 13, UiTheme.Text));
            _inspBox.AddChild(UseStripFor(st, null));
        }
        else
        {
            _inspBox.AddChild(UiTheme.Label($"Out tray {st.Output.Count}", 13));
        }
        if (st.Current is int cur && S.Messages.TryGetValue(cur, out var m))
        {
            var nd = Defs.NetOf(m.Net);
            string what = st.Type switch
            {
                StationType.Bombe or StationType.Bombe4 => $"Running a {nd.Name} menu for {S.DateOf(m.Day):d MMMM}",
                StationType.Crib => $"Writing a {nd.Name} crib for {S.DateOf(m.Day):d MMMM}",
                StationType.Zygalski => $"Searching for the {nd.Name} key of {S.DateOf(m.Day):d MMMM}",
                StationType.Banburismus => $"Ruling out wheel orders for {nd.Name}",
                StationType.HandCipher when m.Offizier => $"Unpicking the Offizier layer of a {nd.Name} message",
                _ => $"{nd.Name} message from {S.DateOf(m.Day):d MMMM}",
            };
            _inspBox.AddChild(UiTheme.Label(what, 13, UiTheme.Accent));
            _inspBox.AddChild(new ProgressBar { MinValue = 0, MaxValue = 1, Value = st.Progress, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) });
            if (S.BanburyFor(st.Id) != null)
            {
                int sid = st.Id;
                _inspBox.AddChild(Row(UiTheme.Label("Slide the Banbury sheets yourself and the wheel orders are cut at once.", 12, UiTheme.TextDim),
                    UiTheme.Button("Slide the sheets…", () => ShowBanbury(sid))));
            }
            if (S.PuzzleFor(st.Id) != null)
            {
                int sid = st.Id;
                _inspBox.AddChild(Row(UiTheme.Label("Place the crib yourself and the menu is done at once.", 12, UiTheme.TextDim),
                    UiTheme.Button("Drag the crib…", () => ShowCribPuzzle(sid))));
            }
        }
        if (st.Wears) InspectWear(st);
        if (st.Type == StationType.Japanese)
            _inspBox.AddChild(UiTheme.Label($"Code book {S.BookName}: {S.BookRecovered * 100:0}% recovered, so a message takes {S.BookFactor:0.0}× the usual time here. " +
                                            "Every JN-25 decrypt fills in more of the book.", 12, S.BookRecovered < 0.4 ? Gfx.Warn : UiTheme.TextDim));
        int oldest = OldestHours(st);
        if (oldest >= 8)
        {
            var here = st.Input.Concat(st.Output).Concat(st.Current is int inHand ? new[] { inHand } : System.Array.Empty<int>())
                .Select(id => S.Messages.TryGetValue(id, out var mm) ? mm : null).OfType<Message>().ToList();
            int dead = here.Count(S.IsDead), stale = here.Count(m => S.IsStale(m) && !S.IsDead(m));
            string note = dead > 0 ? $" {dead} of them too late to earn breakthroughs." : stale > 0 ? $" {stale} going stale (half the breakthroughs)." : "";
            _inspBox.AddChild(UiTheme.Label($"The oldest paper here was intercepted {oldest} hours ago.{note}", 12, dead + stale > 0 ? Gfx.Bad : Gfx.Warn));
        }
        int waiting = st.Output.Count(id => S.Messages.TryGetValue(id, out var mm) && mm.Next == null);
        if (waiting > 0)
            _inspBox.AddChild(UiTheme.Label($"{waiting} message(s) in the out tray are waiting for their day's key.", 12, Gfx.PaperWaiting));
        int held = st.Output.Concat(st.Input).Count(id => S.Messages.TryGetValue(id, out var mm) && mm.HoldUntil > S.Now);
        if (held > 0)
            _inspBox.AddChild(UiTheme.Label($"{held} garbled intercept(s) sent back to the listening station for a cleaner copy.", 12, Gfx.Warn));
        if (st.IsRest)
        {
            int using_ = S.Staff.Count(x => x.Task is { Kind: TaskKind.Rest } t && t.Dest == st.Id);
            int idle = S.Staff.Count(x => x.Task is { Kind: TaskKind.Rest, Loafing: true } t && t.Dest == st.Id);
            _inspBox.AddChild(UiTheme.Label($"{using_}/{d.Seats} seats taken" + (idle > 0 ? $": {using_ - idle} on a break, {idle} with nothing to do (they give up the seat to anyone tired)" : ""), 13));
            if (st.Type == StationType.Rounders) _inspBox.AddChild(UiTheme.Label("Played between 08:00 and 19:00, weather permitting.", 12, UiTheme.TextDim));
        }
        if (d.Operator is Role pr && !st.IsRest)
        {
            // Pin people to this station: they work here first, and don't run errands.
            var pinned = S.PinnedTo(st.Id).ToList();
            _inspBox.AddChild(UiTheme.Label(pinned.Count == 0 ? "Nobody is pinned here; anyone free will come." : "Pinned: " + string.Join(", ", pinned.Select(p => p.Name)), 12, UiTheme.TextDim));
            var pick = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            pick.AddItem($"Pin a {RoleWord(pr)}…", -1);
            foreach (var p in S.Staff.Where(x => x.Role == pr && x.Pinned != st.Id).OrderBy(x => x.Name))
                pick.AddItem(p.Name + (p.Pinned != null ? " (pinned elsewhere)" : ""), p.Id);
            foreach (var p in pinned) pick.AddItem($"Unpin {p.Name}", -1000 - p.Id);
            var sid = st.Id;
            pick.ItemSelected += idx =>
            {
                int v = pick.GetItemId((int)idx);
                if (v >= 0) S.Pin(v, sid);
                else if (v <= -1000) S.Pin(-1000 - v, null);
                _inspSig = "";
            };
            _inspBox.AddChild(pick);
        }
        if (st.Type == StationType.Inbox) InspectTeleprinter(st);
        if (st.Type == StationType.Inbox && S.Stations.Count(s => s.Type == StationType.Inbox) <= 1)
            return;
        var id2 = st.Id;
        var move = UiTheme.Button($"Move (£{Defs.MoveCost})", () => _main.BeginMove(id2), S.Funds >= Defs.MoveCost && !st.PendingRemoval, tooltip: "Pick it up and put it somewhere else (M)");
        _inspBox.AddChild(Row(Spacer(), move, UiTheme.Button(st.PendingRemoval ? "Keep it" : $"Remove (refund £{(int)(d.Cost * Defs.SellRefund)})",
            () => { S.RequestRemove(id2); _inspSig = ""; })));
        if (st.PendingRemoval)
            _inspBox.AddChild(UiTheme.Label("It will be taken away once its trays are empty.", 12, UiTheme.TextDim));
    }

    /// <summary>Condition bar, downtime, and the Service button for machines that wear.</summary>
    void InspectWear(Station st)
    {
        double condition = Math.Clamp(1 - st.Wear, 0, 1);
        if (st.IsDown(S.Now))
        {
            string until = $"{(int)(st.DownUntil % Defs.MinutesPerDay / 60):00}:{(int)(st.DownUntil % 60):00}";
            _inspBox.AddChild(UiTheme.Label(st.Servicing
                ? $"Being serviced. Back at work at {until}."
                : $"Broken down. {char.ToUpperInvariant(Defs.Fitters(st.Type)[0])}{Defs.Fitters(st.Type)[1..]} expect to have it going again by {until}.", 13, st.Servicing ? UiTheme.Accent : Gfx.Bad));
            return;
        }
        var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Value = condition, ShowPercentage = false, CustomMinimumSize = new Vector2(110, 8),
                                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        bar.AddThemeStyleboxOverride("fill", UiTheme.Box(condition < 0.3 ? Gfx.Bad : condition < 0.5 ? Gfx.Warn : Gfx.Good, Colors.Transparent, 2, 0));
        string note = condition < 0.3 ? "Badly worn: likely to break down" : condition < 0.5 ? "Getting worn" : "Running well";
        _inspBox.AddChild(Row(UiTheme.Label($"Condition {Pct(condition)}", 13), bar, UiTheme.Label(note, 12, condition < 0.3 ? Gfx.Bad : UiTheme.TextDim)));
        var id = st.Id;
        string? why = S.CanServiceMachine(id);
        var btn = UiTheme.Button($"Service (£{Defs.ServiceCost}, 1 h)", () => { S.ServiceMachine(id); _inspSig = ""; }, why == null,
            tooltip: why ?? "Take it off for an hour to clean the contacts and check the drums. Any run in progress waits. Worn machines break down, and the fitters take hours.");
        _inspBox.AddChild(Row(Spacer(), btn));
    }

    void InspectStaff(Staff s)
    {
        var rd = Defs.Roles[s.Role];
        _inspBox.AddChild(UiTheme.Heading(s.Name, 18));
        _inspBox.AddChild(UiTheme.Label($"{rd.Name}{(s.Second is Role sec ? $" (and {Defs.RoleWord(sec)})" : "")} · {Stars(s.Skill)} · £{s.Salary}/week", 13, UiTheme.Accent));
        if (s.Skill < 3 && s.Role != Role.Messenger)
            _inspBox.AddChild(UiTheme.Label($"Experience: {Hours(Defs.ExperienceFor[s.Skill + 1] - s.Experience)} more work to reach {Stars(s.Skill + 1)} (with a pay rise)", 11, UiTheme.TextDim));
        if (s.Bio != null) _inspBox.AddChild(UiTheme.Label(s.Bio, 12, UiTheme.Text));
        else _inspBox.AddChild(UiTheme.Label(rd.Blurb, 12, UiTheme.TextDim));
        if (s.Specialties.Count > 0)
            _inspBox.AddChild(UiTheme.Label("Works 50% faster at: " + string.Join(", ", s.Specialties.Select(t => Defs.Station(t).Name.ToLowerInvariant())), 12, UiTheme.Accent));
        foreach (var t in s.KnownTraits())
        {
            var td = Defs.Traits[t];
            var l = UiTheme.Label($"{td.Name}: {td.Blurb}", 12, td.Good ? UiTheme.Accent : Gfx.Bad);
            _inspBox.AddChild(l);
        }
        string doing = s.Arriving(S.Now) ? $"On the way: vetting and the train to Bletchley. Reports in about {Math.Max(1, Math.Round((s.ArrivesAt - S.Now) / 60))} h" : s.IsSick(S.Now) ? (s.OffReason == "course" ? $"On a course to learn the {Defs.RoleWord(s.Second ?? s.Role)}'s work: back in about {Math.Max(1, Math.Round((s.SickUntil - S.Now) / 60))} h" : $"{(s.OffReason switch { "leave" => "On leave", "breakdown" => "Sent home after a breakdown", _ => "Off sick" })}: back in about {Math.Max(1, Math.Round((s.SickUntil - S.Now) / Defs.MinutesPerDay))} days") : s.Away ? $"Off watch, back at the billets until {Defs.Shifts[s.Shift].StartHour:00}:00" : s.Task switch
        {
            { Kind: TaskKind.Operate } t => $"Working at the {S.StationById(t.Dest)?.Def.Name.ToLowerInvariant()}",
            { Kind: TaskKind.Carry, Phase: 0 } => "Fetching messages",
            { Kind: TaskKind.Carry } t => $"Carrying {t.Msgs.Count} message(s)",
            { Kind: TaskKind.Rest, Loafing: true } t => $"Nothing to do, so sitting at the {S.StationById(t.Dest)?.Def.Name.ToLowerInvariant()}: up as soon as work comes",
            { Kind: TaskKind.Rest } t => $"On a break at the {S.StationById(t.Dest)?.Def.Name.ToLowerInvariant()}",
            { Kind: TaskKind.Leave } => "Going off watch",
            _ => "Waiting for work",
        };
        _inspBox.AddChild(UiTheme.Label(doing, 13));
        if (!s.Arriving(S.Now)) _inspBox.AddChild(UseStripFor(null, s));
        var bars = new GridContainer { Columns = 2 };
        bars.AddThemeConstantOverride("h_separation", 8);
        foreach (var (what, val) in new[] { ("Energy", s.Energy), ("Morale", s.Morale), ("Nerves", 1 - s.Strain) })
        {
            if (what == "Nerves" && s.Strain < 0.15) continue;
            bars.AddChild(UiTheme.Label(what, 12, UiTheme.TextDim, wrap: false));
            var pb = new ProgressBar { MinValue = 0, MaxValue = 1, Value = val, ShowPercentage = false, CustomMinimumSize = new Vector2(200, 8), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            pb.AddThemeStyleboxOverride("fill", UiTheme.Box(val < 0.25 ? Gfx.Bad : val < 0.5 ? Gfx.Warn : Gfx.Good, new Color(0, 0, 0, 0), 2, 0));
            bars.AddChild(pb);
        }
        _inspBox.AddChild(bars);
        if (s.Strain >= 0.15)
            _inspBox.AddChild(UiTheme.Label(s.Strain >= Defs.StrainWarnAt ? "Close to breaking down: give them easier days or send them on leave."
                                            : "Strain builds on days they end exhausted and eases on easier ones.", 11, s.Strain >= Defs.StrainWarnAt ? Gfx.Bad : UiTheme.TextDim));
        if (s.Rival is int rid && S.StaffById(rid) is { } rival)
            _inspBox.AddChild(UiTheme.Label($"Has fallen out with {rival.Name}{(S.WithRival(s) ? ": in the same room now, both are slower and grumpier." : ". Keep them in different rooms.")}", 12,
                                            S.WithRival(s) ? Gfx.Bad : Gfx.Warn));
        var id = s.Id;
        if (S.WatchesAvailable)
        {
            var shift = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            foreach (var sh in new[] { Shift.Day, Shift.Evening, Shift.Night, Shift.Office })
            {
                var sd = Defs.Shifts[sh];
                shift.AddItem($"{sd.Name} ({sd.StartHour:00}–{sd.EndHour:00})", (int)sh);
                if (sh == s.Shift) shift.Select(shift.ItemCount - 1);
            }
            shift.ItemSelected += idx => { S.SetShift(id, (Shift)shift.GetItemId((int)idx)); _inspSig = ""; };
            _inspBox.AddChild(Row(UiTheme.Label("Watch", 12, UiTheme.TextDim), shift));
        }
        if (!s.Leaving && s.HeadOf == null)
        {
            // Posting: keep this person to one building. Messengers and anyone left on "anywhere" do the walking between.
            var post = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            bool roams = GameState.Roams(s.Role);
            if (roams || s.Posted == null) post.AddItem(roams ? "The whole Park" : "Anywhere needed", -1);
            var open = (roams ? S.Grid.Zones.Where(z => z.Unlocked) : S.PostsFor(s.Role)).ToList();
            for (int i = 0; i < open.Count; i++)
            {
                post.AddItem(open[i].Name, i);
                if (s.Posted == open[i].Id) post.Select(post.ItemCount - 1);
            }
            post.TooltipText = roams ? "Messengers carry paper between the buildings. Posted to one, they run errands only inside it."
                                     : "They work, carry and wait only in the building they're posted to. Paper bound elsewhere goes down in a tray for a messenger.";
            post.ItemSelected += idx => { int v = post.GetItemId((int)idx); S.Post(id, v < 0 ? null : open[v].Id); _inspSig = ""; };
            _inspBox.AddChild(Row(UiTheme.Label("Posted to", 12, UiTheme.TextDim), post));
        }
        if (!s.Leaving && s.HeadOf == null && !s.Arriving(S.Now) && !(s.IsSick(S.Now) && s.OffReason == "course"))
        {
            // Cross-training: a course in a second role, worked when their own has nothing waiting.
            var learn = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            learn.TooltipText = $"Send them on a course to learn a second role. They work it at {Defs.SecondRoleSpeed:P0} speed, and only when their own desks have nothing waiting.";
            learn.AddItem(s.Second is Role had ? $"{Defs.Roles[had].Name} as well" : "Only their own work", -1);
            learn.Select(0);
            if (S.JapanOpen && s.Role == Role.Linguist && !s.Has(Trait.Japanese))
            {
                learn.AddItem($"Learn Japanese at Bedford ({Defs.JapaneseCourseHours:0} h away, £{Defs.JapaneseCourseCost})", JapaneseItem);
                if (S.CantLearnJapanese(s) is string jwhy)
                {
                    learn.SetItemDisabled(learn.ItemCount - 1, true);
                    learn.SetItemTooltip(learn.ItemCount - 1, jwhy);
                }
                else learn.SetItemTooltip(learn.ItemCount - 1, "Captain Tuck's course in naval Japanese. They come back able to work the Japanese desk (JN-25), and keep their own work too.");
            }
            foreach (Role r in Enum.GetValues<Role>())
            {
                if (r == s.Second || Defs.Course(s.Role, r) is not var (hours, cost)) continue;
                learn.AddItem($"Learn {Defs.RoleWord(r)}'s work ({hours:0} h away, £{cost})", (int)r);
                if (S.CantTrain(s, r) is string why)
                {
                    learn.SetItemDisabled(learn.ItemCount - 1, true);
                    learn.SetItemTooltip(learn.ItemCount - 1, why);
                }
            }
            if (learn.ItemCount > 1)
            {
                learn.ItemSelected += idx =>
                {
                    int v = learn.GetItemId((int)idx);
                    if (v == JapaneseItem) S.LearnJapanese(id);
                    else if (v >= 0) S.Train(id, (Role)v);
                    _inspSig = "";
                };
                _inspBox.AddChild(Row(UiTheme.Label("Learns", 12, UiTheme.TextDim), learn));
            }
        }
        var stations = S.Stations.Where(st => st.Def.Operator == s.Role && !st.IsRest).ToList();
        if (stations.Count > 0)
        {
            var pin = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            pin.AddItem("Anywhere needed", -1);
            foreach (var st in stations)
            {
                var zone = S.Grid.ZoneAt(st.X, st.Y)?.Name ?? "";
                pin.AddItem($"{st.Def.Name}, {zone} #{st.Id}", st.Id);
                if (s.Pinned == st.Id) pin.Select(pin.ItemCount - 1);
            }
            pin.ItemSelected += idx => { int v = pin.GetItemId((int)idx); S.Pin(id, v < 0 ? null : v); _inspSig = ""; };
            _inspBox.AddChild(Row(UiTheme.Label("Pinned to", 12, UiTheme.TextDim), pin));
        }
        if (GameState.CanHead(s))
        {
            var head = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            head.TooltipText = "A head manages a hut instead of working a desk. How the hut changes depends on their style.";
            head.AddItem("Works a desk", -1);
            var zones = S.Grid.Zones.Where(z => z.Unlocked).ToList();
            for (int i = 0; i < zones.Count; i++)
            {
                var other = S.HeadOfZone(zones[i].Id);
                head.AddItem(zones[i].Name + (other != null && other != s ? $" (instead of {other.Name})" : ""), i);
                if (s.HeadOf == zones[i].Id) head.Select(head.ItemCount - 1);
            }
            head.ItemSelected += idx => { int v = head.GetItemId((int)idx); S.AppointHead(id, v < 0 ? null : zones[v].Id); _inspSig = ""; };
            _inspBox.AddChild(Row(UiTheme.Label("Head of", 12, UiTheme.TextDim), head));
            if (s.HeadOf != null)
            {
                var style = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                foreach (var hs in Defs.HeadStyles.Values)
                {
                    style.AddItem(hs.Name, (int)hs.Style);
                    style.SetItemTooltip(style.ItemCount - 1, hs.Blurb);
                    if (s.HeadStyle == hs.Style) style.Select(style.ItemCount - 1);
                }
                style.ItemSelected += idx => { S.SetHeadStyle(id, (HeadStyle)style.GetItemId((int)idx)); _inspSig = ""; };
                _inspBox.AddChild(Row(UiTheme.Label("Style", 12, UiTheme.TextDim), style));
                _inspBox.AddChild(UiTheme.Label(Defs.HeadStyles[s.HeadStyle].Blurb, 11, UiTheme.TextDim));
            }
        }
        if (s.Leaving) _inspBox.AddChild(UiTheme.Label("Leaving once the current job is done.", 12, Gfx.Warn));
        else
        {
            _inspBox.AddChild(Row(Spacer(), UiTheme.Button("Dismiss", () => { S.Fire(id); _inspSig = ""; })));
        }
    }

    void InspectZone(Zone z)
    {
        _inspBox.AddChild(UiTheme.Heading(z.Name, 18));
        if (Defs.ZoneRoles.TryGetValue(z.Id, out var role))
        {
            _inspBox.AddChild(UiTheme.Label(role.Purpose, 12, UiTheme.Text));
            _inspBox.AddChild(UiTheme.Label("Takes: " + WhatItTakes(z), 12, UiTheme.TextDim));
        }
        if (z.Unlocked && S.ColdZones() is { } cold && cold.Contains(z.Id))
            _inspBox.AddChild(UiTheme.Label("Freezing: no stove. People here tire faster and grumble. Build a coke stove (£60).", 12, Gfx.Warn));
        if (z.Unlocked && S.HotZones() is { } hot && hot.Contains(z.Id))
            _inspBox.AddChild(UiTheme.Label($"Sweltering: its Bombes heat the air and nothing draws it out. People here tire faster and grow short-tempered. Build an extractor fan (£{Defs.Station(StationType.Fan).Cost}).", 12, Gfx.Warn));
        if (!z.Available)
        {
            _inspBox.AddChild(UiTheme.Label("The Treasury hasn't authorised this hut yet. It will be needed later in the war.", 13, UiTheme.TextDim));
            return;
        }
        _inspBox.AddChild(UiTheme.Label($"Foundations are laid. Building the hut costs £{z.Cost:N0}.", 13));
        _inspBox.AddChild(Row(Spacer(), UiTheme.Button($"Open {z.Name}", () =>
        {
            if (S.UnlockZone(z.Id)) { _inspZone = null; _inspSig = ""; }
            else Toast($"You need £{z.Cost:N0} to open {z.Name}.", EventKind.Bad);
        }, S.Funds >= z.Cost)));
    }
}
