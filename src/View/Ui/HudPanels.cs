using System;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

public partial class Hud
{
    PanelContainer _side = null!;
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
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _side.AddChild(scroll);
        _sideBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _sideBox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_sideBox);
    }

    public void OpenPanel(string id)
    {
        _panel = id;
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
            "staff" => string.Join(",", S.Staff.Select(s => $"{s.Id}{s.Task?.Kind}{s.Leaving}{s.Away}{s.Shift}{(int)(s.Energy * 5)}{(int)(s.Morale * 5)}{s.FarBillet}{s.IsSick(S.Now)}")) + $"|{S.Billets}{S.Funds >= Defs.BilletCost}|" +
                       string.Join(",", S.Candidates.Select(c => c.Id)) + $"|{S.Funds / 25}",
            "requests" => string.Join(",", S.Contracts.Select(c => $"{c.Id}{c.State}{c.Delivered}{c.Spawned}{(int)((c.State == ContractState.Offered ? c.OfferExpires : c.Due) - S.Now) / 60}")) +
                          $"|{string.Join(",", S.KeyBoardNets().Select(n => S.KeyRoute(n)))}",
            "research" => $"{S.Points}|{string.Join(",", S.Researched)}|{string.Join(",", S.ResearchAvailable)}",
            "huts" => $"{S.Funds / 100}|{S.OutstationBombes}|{S.CanBuyOutstation()}|" + string.Join(",", S.Grid.Zones.Select(z => $"{z.Available}{z.Unlocked}")),
            "intel" => IntelSig(),
            "stats" => $"{S.History.Count}|{_statsRange}",
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
            case "stats": StatsPanel(); break;
        }
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

    /// <summary>"any hut", or the names of the buildings that take this station, open ones first.</summary>
    string WhereItGoes(StationType t)
    {
        if (Defs.Everywhere.Contains(t)) return "any hut";
        return string.Join(", ", Defs.ZonesFor(t).Select(id => S.Grid.Zone(id)).Where(z => z != null)
            .OrderBy(z => !z!.Unlocked).Select(z => z!.Unlocked ? z.Name : $"{z.Name} (not open)"));
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
        _sideBox.AddChild(UiTheme.Label("Desks and machines go inside a hut, with clear space in front for whoever works them. Messages flow from the teleprinter, through each step, to the despatch box.", 13, UiTheme.TextDim));
        foreach (StationType t in Enum.GetValues<StationType>())
        {
            var d = Defs.Station(t);
            bool unlocked = S.StationUnlocked(t, out var why);
            if (!unlocked && why == "Not needed yet") continue;
            var v = Card();
            var name = UiTheme.Label(d.Name, 15, UiTheme.Text, Fonts.BodyBold);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            v.AddChild(Row(name, UiTheme.Label($"£{d.Cost:N0}", 15, S.Funds >= d.Cost ? UiTheme.Accent : Gfx.Bad, Fonts.BodyBold)));
            v.AddChild(UiTheme.Label(d.Blurb, 12, UiTheme.TextDim));
            if (!d.Outdoor) v.AddChild(UiTheme.Label("Goes in: " + WhereItGoes(t), 12, UiTheme.Accent));
            string who = d.Operator is Role r ? $"Worked by a {Defs.Roles[r].Name.ToLowerInvariant()}" : "No operator needed";
            var info = UiTheme.Label($"{who} · {d.W}×{d.H} tiles", 12, UiTheme.TextDim);
            info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            Control action = unlocked
                ? UiTheme.Button("Place", () => { _main.BeginBuild(t); ClosePanel(); }, S.Funds >= d.Cost)
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
        _sideBox.AddChild(UiTheme.Label($"Wages £{S.WeeklyWages:N0} and upkeep £{S.WeeklyUpkeep:N0} a week, against a Treasury grant of £{S.WeeklyGrant:N0}.", 13, UiTheme.TextDim));
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
        if (S.WatchesAvailable)
        {
            var v = Card(UiTheme.Accent);
            v.AddChild(UiTheme.Label("Three watches", 15, UiTheme.Text, Fonts.BodyBold));
            v.AddChild(UiTheme.Label("Day 09–16, evening 16–24, night 00–09. People on the long day (07–22) grow weary now the work never stops.", 12, UiTheme.TextDim));
            foreach (var sh in new[] { Shift.Day, Shift.Evening, Shift.Night, Shift.Office })
            {
                var counts = string.Join("  ", Enum.GetValues<Role>().Select(r => $"{Defs.Roles[r].Name[0]}{S.Staff.Count(x => x.Role == r && x.Shift == sh)}"));
                var l = UiTheme.Label($"{Defs.Shifts[sh].Name}", 12, UiTheme.Text);
                l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                v.AddChild(Row(l, UiTheme.Label(counts, 12, UiTheme.TextDim, Fonts.Typewriter)));
            }
            v.AddChild(Row(Spacer(), UiTheme.Button("Balance watches", () => { S.BalanceWatches(null); _sideSig = ""; Toast("Everyone is spread across the three watches."); },
                tooltip: "Spread every role evenly over day, evening and night. Night owls take the night.")));
        }
        foreach (Role role in Enum.GetValues<Role>())
        {
            var people = S.Staff.Where(s => s.Role == role).ToList();
            var rd = Defs.Roles[role];
            var head = Row(new ColorRect { Color = Gfx.RoleColor(role), CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter },
                UiTheme.Caps($"{rd.Plural} · {people.Count}", 12, UiTheme.Text));
            head.TooltipText = rd.Blurb;
            _sideBox.AddChild(head);
            foreach (var p in people)
            {
                string doing = p.IsSick(S.Now) ? "off sick" : p.Away ? "off watch" : p.Task?.Kind switch
                {
                    null => "idle",
                    TaskKind.Operate => "working",
                    TaskKind.Rest => "on a break",
                    TaskKind.Leave => "going off",
                    _ => "running",
                };
                string shift = S.WatchesAvailable ? $" · {Defs.Shifts[p.Shift].Name.Split(' ')[0].ToLowerInvariant()}" : "";
                string pin = p.Pinned != null ? " · 📌" : "";
                var b = UiTheme.Button($"{p.Name}   {Stars(p.Skill)}   {doing}{shift}{pin}",
                    () => { _main.Select(null, p.Id); if (!p.Away) _main.CenterOnTile((float)p.X, (float)p.Y); });
                b.Alignment = HorizontalAlignment.Left;
                b.Flat = true;
                b.AddThemeFontSizeOverride("font_size", 13);
                if (p.Away) b.AddThemeColorOverride("font_color", UiTheme.TextDim);
                b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                _sideBox.AddChild(Row(b, Meter(p.Energy, "Energy"), Meter(p.Morale, "Morale")));
            }
        }
        _sideBox.AddChild(new HSeparator());
        _sideBox.AddChild(UiTheme.Heading("Candidates", 17));
        _sideBox.AddChild(UiTheme.Label("Recommended by colleges, the Foreign Office and the Daily Telegraph crossword. New names arrive every week. The first week's wages are paid on hiring.", 12, UiTheme.TextDim));
        foreach (var c in S.Candidates.OrderBy(c => c.Notable == null).ThenBy(c => c.Role))
        {
            var v = Card(c.Notable != null ? UiTheme.Accent : null);
            var name = UiTheme.Label(c.Name, 15, UiTheme.Text, Fonts.BodyBold);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            v.AddChild(Row(name, UiTheme.Label(Stars(c.Skill), 14, UiTheme.Accent)));
            var role = UiTheme.Label($"{Defs.Roles[c.Role].Name} · £{c.Salary}/week", 12, UiTheme.TextDim);
            role.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var traits = c.KnownTraits().ToList();
            if (traits.Count > 0)
                v.AddChild(UiTheme.Label(string.Join(" · ", traits.Select(t => Defs.Traits[t].Name)), 12, UiTheme.Accent));
            if (c.Bio != null)
            {
                v.AddChild(UiTheme.Label(c.Bio, 12, UiTheme.Text));
                v.AddChild(UiTheme.Label("Specialist: " + string.Join(", ", c.Specialties.Select(t => Defs.Station(t).Name.ToLowerInvariant())), 12, UiTheme.Accent));
            }
            var id = c.Id;
            v.AddChild(Row(role, UiTheme.Button("Hire", () => S.Hire(id), S.Funds >= c.Salary)));
        }
    }

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
            v.AddChild(UiTheme.Label($"Due in {Hours(left)} · £{c.Reward:N0} on completion", 12, left < Defs.MinutesPerDay * 0.5 ? Gfx.Bad : UiTheme.Text));
        }

        _sideBox.AddChild(UiTheme.Caps($"Offered · {offers.Count}", 12, UiTheme.Accent));
        if (offers.Count == 0) _sideBox.AddChild(UiTheme.Label("No new requests. Whitehall will be in touch.", 13, UiTheme.TextDim));
        foreach (var c in offers)
        {
            var v = Card(Gfx.NetColor(c.Net));
            ContractHeader(v, c);
            v.AddChild(UiTheme.Label(c.Flavour, 12, UiTheme.TextDim));
            v.AddChild(UiTheme.Label($"{c.Quantity} messages within {Hours(c.Days * Defs.MinutesPerDay)} · £{c.Reward:N0} · reputation +{c.Rep}", 13, UiTheme.Text));
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
            v.AddChild(Row(exp, UiTheme.Button("Decline", () => S.Decline(id)), UiTheme.Button("Accept", () => S.Accept(id))));
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
        var issuer = UiTheme.Caps(Defs.IssuerNames[c.Issuer] + (urgent ? " · MOST IMMEDIATE" : ""), 11, urgent ? Gfx.Bad : UiTheme.TextDim);
        v.AddChild(issuer);
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
    }

    // ------------------------------------------------------------------- huts

    void HutsPanel()
    {
        _sideBox.AddChild(UiTheme.Heading("Huts"));
        _sideBox.AddChild(UiTheme.Label("Bletchley grew from a country house into a sprawl of timber huts. Each one opens when the work calls for it.", 13, UiTheme.TextDim));
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
            ? $"st{st.Id}{st.Input.Count}{st.Output.Count}{st.OperatorId}{st.OperatorPresent}{st.Current}{(int)(st.Progress * 20)}{st.PendingRemoval}{st.X},{st.Y}|{string.Join(",", S.PinnedTo(st.Id).Select(p => p.Id))}|{S.Funds >= Defs.MoveCost}|{(int)(st.Wear * 20)}{st.IsDown(S.Now)}{S.Funds >= Defs.ServiceCost}"
            : sf != null ? $"sf{sf.Id}{sf.Task?.Kind}{sf.Task?.Dest}{sf.Leaving}{sf.Decrypts}{sf.Away}{sf.IsSick(S.Now)}{sf.Shift}{sf.Pinned}{(int)(sf.Energy * 10)}{(int)(sf.Morale * 10)}{sf.CarelessKnown}{(int)(sf.Experience / 60)}{sf.Skill}"
            : $"z{z!.Id}{z.Unlocked}{S.Funds >= z.Cost}{S.ColdZones()?.Contains(z.Id)}";
        if (sig == _inspSig) return;
        _inspSig = sig;
        Clear(_inspBox);

        // Reset the panel height so it shrinks to fit new content.
        _inspector.OffsetTop = _inspector.OffsetBottom;

        if (st != null) InspectStation(st);
        else if (sf != null) InspectStaff(sf);
        else InspectZone(z!);
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
                ? (st.Input.Count > 0 ? $"Waiting for a free {Defs.Roles[r].Name.ToLowerInvariant()}" : $"Unstaffed. Needs a {Defs.Roles[r].Name.ToLowerInvariant()}.")
                : st.OperatorPresent ? $"{op.Name} at work" : $"{op.Name} on the way";
            _inspBox.AddChild(UiTheme.Label(who, 13, op == null && st.Input.Count > 0 ? Gfx.Warn : UiTheme.Text));
            _inspBox.AddChild(UiTheme.Label($"In tray {st.Input.Count}/{d.InputCap} · out tray {st.Output.Count}", 13, UiTheme.Text));
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
                _ => $"{nd.Name} message from {S.DateOf(m.Day):d MMMM}",
            };
            _inspBox.AddChild(UiTheme.Label(what, 13, UiTheme.Accent));
            _inspBox.AddChild(new ProgressBar { MinValue = 0, MaxValue = 1, Value = st.Progress, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) });
        }
        if (st.Wears) InspectWear(st);
        int waiting = st.Output.Count(id => S.Messages.TryGetValue(id, out var mm) && mm.Next == null);
        if (waiting > 0)
            _inspBox.AddChild(UiTheme.Label($"{waiting} message(s) in the out tray are waiting for their day's key.", 12, Gfx.PaperWaiting));
        int held = st.Output.Concat(st.Input).Count(id => S.Messages.TryGetValue(id, out var mm) && mm.HoldUntil > S.Now);
        if (held > 0)
            _inspBox.AddChild(UiTheme.Label($"{held} garbled intercept(s) sent back to the listening station for a cleaner copy.", 12, Gfx.Warn));
        if (st.IsRest)
        {
            int using_ = S.Staff.Count(x => x.Task is { Kind: TaskKind.Rest } t && t.Dest == st.Id);
            _inspBox.AddChild(UiTheme.Label($"{using_}/{d.Seats} taking a break here", 13));
            if (st.Type == StationType.Rounders) _inspBox.AddChild(UiTheme.Label("Played between 08:00 and 19:00, weather permitting.", 12, UiTheme.TextDim));
        }
        if (d.Operator is Role pr && !st.IsRest)
        {
            // Pin people to this station: they work here first, and don't run errands.
            var pinned = S.PinnedTo(st.Id).ToList();
            _inspBox.AddChild(UiTheme.Label(pinned.Count == 0 ? "Nobody is pinned here; anyone free will come." : "Pinned: " + string.Join(", ", pinned.Select(p => p.Name)), 12, UiTheme.TextDim));
            var pick = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            pick.AddItem($"Pin a {Defs.Roles[pr].Name.ToLowerInvariant()}…", -1);
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
                : $"Broken down. The RAF fitters expect to have it going again by {until}.", 13, st.Servicing ? UiTheme.Accent : Gfx.Bad));
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
        _inspBox.AddChild(UiTheme.Label($"{rd.Name} · {Stars(s.Skill)} · £{s.Salary}/week", 13, UiTheme.Accent));
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
        string doing = s.IsSick(S.Now) ? $"Off sick: back in about {Math.Max(1, Math.Round((s.SickUntil - S.Now) / Defs.MinutesPerDay))} days" : s.Away ? $"Off watch, back at the billets until {Defs.Shifts[s.Shift].StartHour:00}:00" : s.Task switch
        {
            { Kind: TaskKind.Operate } t => $"Working at the {S.StationById(t.Dest)?.Def.Name.ToLowerInvariant()}",
            { Kind: TaskKind.Carry, Phase: 0 } => "Fetching messages",
            { Kind: TaskKind.Carry } t => $"Carrying {t.Msgs.Count} message(s)",
            { Kind: TaskKind.Rest } t => $"On a break at the {S.StationById(t.Dest)?.Def.Name.ToLowerInvariant()}",
            { Kind: TaskKind.Leave } => "Going off watch",
            _ => "Waiting for work",
        };
        _inspBox.AddChild(UiTheme.Label(doing, 13));
        var bars = new GridContainer { Columns = 2 };
        bars.AddThemeConstantOverride("h_separation", 8);
        foreach (var (what, val) in new[] { ("Energy", s.Energy), ("Morale", s.Morale) })
        {
            bars.AddChild(UiTheme.Label(what, 12, UiTheme.TextDim, wrap: false));
            var pb = new ProgressBar { MinValue = 0, MaxValue = 1, Value = val, ShowPercentage = false, CustomMinimumSize = new Vector2(200, 8), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            pb.AddThemeStyleboxOverride("fill", UiTheme.Box(val < 0.25 ? Gfx.Bad : val < 0.5 ? Gfx.Warn : Gfx.Good, new Color(0, 0, 0, 0), 2, 0));
            bars.AddChild(pb);
        }
        _inspBox.AddChild(bars);
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
