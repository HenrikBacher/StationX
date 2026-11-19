using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>Intelligence and statistics panels, tutorial hints, and the menus (pause, saves, settings, new game, dilemmas).</summary>
public partial class Hud
{
    int _statsRange = 30;

    // ------------------------------------------------------------ intelligence

    string IntelSig() =>
        $"{S.Policy}|{S.Funds / 100}|{S.OutstationBombes}|{S.RemoteJobs.Count}|{S.Dilemmas.Count}|" +
        string.Join(",", Enum.GetValues<Service>().Select(sv => (int)S.SuspicionOf(sv) / 2)) + "|" +
        string.Join(",", S.KeyBoardNets().Select(n => $"{S.CanGarden(n)}{S.LockedDown(n)}"));

    void IntelPanel()
    {
        _sideBox.AddChild(UiTheme.Heading("Intelligence"));
        _sideBox.AddChild(UiTheme.Label("ULTRA is only useful while the Germans believe their ciphers are safe. Every decrypt that is acted on is a risk. " +
                                        "Suspicion fades by itself, a few points a day.", 13, UiTheme.TextDim));

        _sideBox.AddChild(UiTheme.Caps("German suspicion", 12, UiTheme.Accent));
        foreach (var sv in Enum.GetValues<Service>())
        {
            double v = S.SuspicionOf(sv);
            bool locked = S.LockdownUntil.TryGetValue(sv, out var until) && S.Day <= until;
            var name = UiTheme.Label(Defs.ServiceNames[sv], 13, UiTheme.Text);
            name.CustomMinimumSize = new Vector2(120, 0);
            var bar = new ProgressBar { MinValue = 0, MaxValue = Defs.SuspicionLimit, Value = v, ShowPercentage = false, CustomMinimumSize = new Vector2(150, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            bar.AddThemeStyleboxOverride("fill", UiTheme.Box(v >= Defs.SuspicionWarn ? Gfx.Bad : v >= 40 ? Gfx.Warn : Gfx.Good, new Color(0, 0, 0, 0), 2, 0));
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var state = UiTheme.Label(locked ? $"locked {until - S.Day + 1}d" : $"{(int)v}", 12, locked ? Gfx.Bad : UiTheme.TextDim);
            _sideBox.AddChild(Row(name, bar, state));
        }

        _sideBox.AddChild(UiTheme.Caps("Policy on using ULTRA", 12, UiTheme.Accent));
        var pol = new HBoxContainer();
        pol.AddThemeConstantOverride("separation", 4);
        foreach (var p in Enum.GetValues<Policy>())
        {
            var pd = Defs.Policies[p];
            var b = UiTheme.Button(pd.Name, () => S.SetPolicy(p), tooltip: pd.Blurb);
            b.ToggleMode = true;
            b.SetPressedNoSignal(S.Policy == p);
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            pol.AddChild(b);
        }
        _sideBox.AddChild(pol);
        var cur = Defs.Policies[S.Policy];
        _sideBox.AddChild(UiTheme.Label($"{cur.Blurb} Rewards ×{cur.Reward:0.##}, reputation ×{cur.Rep:0.##}, suspicion ×{cur.Suspicion:0.##}.", 12, UiTheme.TextDim));

        if (S.Dilemmas.Count > 0)
        {
            var v = Card(Gfx.Bad);
            v.AddChild(UiTheme.Label($"{S.Dilemmas.Count} decision(s) waiting", 14, UiTheme.Text, Fonts.BodyBold));
            v.AddChild(Row(Spacer(), UiTheme.Button("Decide now", () => ShowDilemma(S.Dilemmas[0]))));
        }

        var naval = S.KeyBoardNets().Where(n => Defs.NetOf(n).Naval).ToList();
        if (naval.Count > 0 && S.ActIndex >= 7)
        {
            _sideBox.AddChild(UiTheme.Caps("Gardening", 12, UiTheme.Accent));
            _sideBox.AddChild(UiTheme.Label($"Ask Coastal Command to lay mines where the Germans will report them. The warnings are perfect cribs for that key today and tomorrow. £{Defs.GardeningCost}, and the Kriegsmarine may wonder.", 12, UiTheme.TextDim));
            foreach (var n in naval)
            {
                var prob = S.CanGarden(n);
                var name = UiTheme.Label(Defs.NetOf(n).Name, 13, UiTheme.Text, Fonts.BodyBold);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                var net = n;
                _sideBox.AddChild(Row(name, prob == null
                    ? UiTheme.Button("Lay mines", () => S.Garden(net))
                    : UiTheme.Label(prob, 12, UiTheme.TextDim)));
            }
        }
        OutstationCard();
    }

    void OutstationCard()
    {
        var why = S.CanBuyOutstation();
        if (why == "Not needed yet") return;
        var v = Card(UiTheme.Accent);
        v.AddChild(UiTheme.Label("Outstations", 15, UiTheme.Text, Fonts.BodyBold));
        v.AddChild(UiTheme.Label("Bombes at Eastcote, Stanmore, Wavendon, Adstock and Gayhurst, worked by Wrens. Build an outstation teleprinter in the park; " +
                                 "when your own Bombes are busy, menus go down the line instead.", 12, UiTheme.TextDim));
        var names = S.OutstationNames.ToList();
        v.AddChild(UiTheme.Label(names.Count == 0 ? "No outstation Bombes yet." : $"{names.Count} Bombe(s): {string.Join(", ", names)} · {S.RemoteJobs.Count} job(s) running", 12, UiTheme.Text));
        if (names.Count > 0 && !S.Stations.Any(s => s.Type == StationType.OutstationLink))
            v.AddChild(UiTheme.Label("No outstation teleprinter: they have nothing to do.", 12, Gfx.Warn));
        var info = UiTheme.Label($"£{Defs.OutstationBombeCost:N0} each, upkeep £{Defs.OutstationUpkeep} a week", 12, UiTheme.TextDim);
        info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        v.AddChild(Row(info, why == null
            ? UiTheme.Button("Install a Bombe", () => S.BuyOutstation())
            : UiTheme.Label(why, 12, UiTheme.TextDim)));
    }

    // ------------------------------------------------------------- statistics

    void StatsPanel()
    {
        _sideBox.AddChild(UiTheme.Heading("Statistics"));
        var range = new HBoxContainer();
        range.AddThemeConstantOverride("separation", 4);
        foreach (var (label, days) in new[] { ("2 weeks", 14), ("30 days", 30), ("All", 9999) })
        {
            var b = UiTheme.Button(label, () => { _statsRange = days; _sideSig = ""; });
            b.ToggleMode = true;
            b.SetPressedNoSignal(_statsRange == days);
            range.AddChild(b);
        }
        _sideBox.AddChild(range);
        var hist = S.History.TakeLast(_statsRange).ToList();
        if (hist.Count < 2)
        {
            _sideBox.AddChild(UiTheme.Label("Figures are collected at midnight. Come back after a day or two.", 13, UiTheme.TextDim));
            return;
        }
        Chart("Decrypts delivered per day", hist, r => r.Delivered, Gfx.Good, "0");
        Chart("Enigma keys broken per day", hist, r => r.KeysBroken, UiTheme.Accent, "0");
        Chart("Messages in the huts at midnight", hist, r => r.Backlog, Gfx.Warn, "0");
        Chart("Funds", hist, r => r.Funds, UiTheme.Text, "£#,0");
        Chart("Reputation in Whitehall", hist, r => r.Reputation, UiTheme.Accent, "0", 0, 100);
        Chart("Staff", hist, r => r.Staff, UiTheme.Text, "0");
        Chart("Average morale", hist, r => r.Morale * 100, Gfx.Good, "0'%'", 0, 100);
        Chart("Highest German suspicion", hist, r => r.Suspicion, Gfx.Bad, "0", 0, Defs.SuspicionLimit);
        _sideBox.AddChild(UiTheme.Label($"Totals: {S.Stats.DeliveredTotal} decrypts · {S.Stats.ContractsCompleted} requests completed, {S.Stats.ContractsFailed} missed · {S.Stats.DilemmasResolved} decisions taken", 12, UiTheme.TextDim));
    }

    void Chart(string title, List<DayRecord> hist, Func<DayRecord, double> f, Color color, string fmt, double? min = null, double? max = null)
    {
        var values = hist.Select(f).ToArray();
        var v = Card();
        var name = UiTheme.Label(title, 13, UiTheme.Text, Fonts.BodyBold);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        v.AddChild(Row(name, UiTheme.Label(values[^1].ToString(fmt), 13, color, Fonts.BodyBold)));
        v.AddChild(new LineChart
        {
            Values = values,
            FirstDay = hist[0].Day,
            Min = min ?? Math.Min(0, values.Min()),
            Max = max ?? Math.Max(1, values.Max() * 1.1),
            Color = color,
            CustomMinimumSize = new Vector2(0, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
    }

    // ------------------------------------------------------------------ hints

    PanelContainer _hint = null!;
    Label _hintText = null!;
    string? _hintId;
    readonly HashSet<string> _hintsDone = new();

    void BuildHints()
    {
        _hint = new PanelContainer { Visible = false };
        var sb = UiTheme.Box(UiTheme.PaperBg, UiTheme.Stamp, 4, 14, 10);
        sb.BorderWidthLeft = 5;
        _hint.AddThemeStyleboxOverride("panel", sb);
        _hint.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _hint.OffsetLeft = -250; _hint.OffsetRight = 250; _hint.OffsetTop = 70;
        _hint.GrowHorizontal = Control.GrowDirection.Both;
        _root.AddChild(_hint);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        _hint.AddChild(v);
        var head = Ink("A WORD FROM COMMANDER DENNISTON", 11, Fonts.BodyBold, UiTheme.Stamp, wrap: false);
        head.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var close = UiTheme.Button("Got it", () => { if (_hintId != null) { _hintsDone.Add(_hintId); S.TipsDismissed.Add(_hintId); } _hint.Visible = false; });
        close.Flat = true;
        close.AddThemeColorOverride("font_color", UiTheme.Stamp);
        close.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(Row(head, close));
        _hintText = Ink("", 14, Fonts.Body);
        v.AddChild(_hintText);
    }

    (string id, string text)? NextHint() => Walkthrough() ?? Situation();

    /// <summary>Tips for later systems, each shown once when its situation first comes up.</summary>
    (string id, string text)? Situation()
    {
        IEnumerable<(string id, bool when, string text)> Tips()
        {
            yield return ("vetting", S.Staff.Any(s => s.Arriving(S.Now)),
                "New recruits are vetted and put on a train: they take most of a day to report. Hire ahead of need, not when the pile is already on the desk.");
            yield return ("raid", S.RaidOn,
                "An air raid. Desk work runs at half pace in the blackout until the all-clear; the Bombes run on. In the Blitz, don't count on the night watch.");
            yield return ("sick", S.SickCount > 0,
                "Someone is off sick. Illness spreads, worst in winter. Stoves in the huts, enough billets and a sick bay in the Mansion keep it down.");
            yield return ("billets", S.Overcrowded > 0,
                "More staff than billets: the newest are lodged miles away, slower and unhappy. Requisition more billets from Staff (H).");
            yield return ("cold", S.Winter && S.ColdZones() is { Count: > 0 },
                "Winter. People in a timber hut without a coke stove tire faster and fall ill more. A stove is £60.");
            yield return ("worn", S.Stations.Any(s => s.Wears && s.Wear > 0.7 && !s.IsDown(S.Now)),
                "A machine is badly worn (the orange lamp). Service it from its panel while it's idle: an hour now saves five hours of breakdown later.");
            yield return ("stale", S.ActiveNets.Any(n => S.Stale(n, S.Day)),
                "The Germans have changed a habit and our cribs for one network are poor for a few days (see the key panel). Its Bombe runs take longer: give it more Bombe time or take fewer of its requests.");
            yield return ("sixta", S.ActIndex >= Defs.SixtaFromAct && S.Stations.Any(s => s.Type == StationType.Crib) && !S.Stations.Any(s => s.Type == StationType.TrafficAnalysis),
                "Hut 6's traffic analysts read what's outside a message: callsigns, frequencies, who sends to whom. A traffic-analysis desk (Build, B) by the crib desks logs two of each network's messages a day, and that key's cribs and Bombe runs go quicker.");
            yield return ("priorities", S.KeysQueueing,
                "Keys are queueing for the Bombes. On the Enigma keys card, put the network with the most pressing request First; a network you have no requests for can go on Hold, leaving the Bombes to the others.");
            yield return ("neglect", S.NeglectedMinistries().Any(),
                "A ministry has had nothing from us for days. Leave one waiting ten days and it complains to the Foreign Office every week. Take one of its requests.");
            yield return ("layout", S.Staff.Count >= 18 && S.Stats.DeliveredTotal > 40 && S.Stats.HandOvers < S.Stats.DeliveredTotal / 4,
                "Put each step's desk beside the next one (registration, Typex, translation, despatch): neighbouring desks pass paper across without a runner. And keep aisles clear: people squeezing past each other walk slower.");
            yield return ("allhands", S.CanCallAllHands() == null && S.Contracts.Any(c => c.State == ContractState.Active && c.Due - S.Now < 6 * 60 && c.Quantity - c.Delivered >= 2),
                "A request is about to be missed. In an emergency, Staff (H) → Call all hands brings everyone off watch back in for four hours. It costs overtime and morale, and works once a week.");
        }
        if (S.Mode == GameMode.FreePlay && S.ActIndex < 1) return null;
        foreach (var (id, when, text) in Tips())
            if (when && !S.TipsDismissed.Contains(id)) return (id, text);
        return null;
    }

    /// <summary>The first-chapter walkthrough: one suggestion at a time, from the state of the floor.</summary>
    (string id, string text)? Walkthrough()
    {
        if (S.ActIndex > 1 || S.Mode == GameMode.FreePlay) return null;
        bool Has(StationType t) => S.Stations.Any(s => s.Type == t);
        int Count(Role r) => S.Staff.Count(s => s.Role == r);
        if (!S.Contracts.Any(c => c.State is ContractState.Active or ContractState.Completed))
            return ("accept", "Open Requests (Q) and accept one. Traffic only flows once a ministry has asked for it.");
        if (!Has(StationType.Registration))
            return ("reg", "Messages arrive at the teleprinter. Open Build (B) and place a registration desk in the Mansion: every message is logged there first.");
        if (!Has(StationType.HandCipher))
            return ("hand", "Place a hand-cipher desk. Your cryptanalysts work the police ciphers there with pencil and paper.");
        if (!Has(StationType.Translation))
            return ("trans", "Decrypts are in German. A translation desk, worked by a linguist, turns them into English.");
        if (!Has(StationType.Dispatch))
            return ("disp", "Finished translations leave by the despatch box. Without one, nothing reaches Whitehall.");
        if (S.ActIndex >= Defs.IndexFromAct && !Has(StationType.Index))
            return ("index", "Machine-cipher decrypts now go through Hut 3's Index before despatch: a card for every unit, officer and place, so each message is read against all we know. Build a card index (Hut 3, Hut 8 or the Mansion), worked by a clerk.");
        if (Count(Role.Clerk) < 2)
            return ("clerks", "Clerks register paper and carry it between desks. Hire another from Staff (H).");
        if (S.ActIndex >= 1 && Count(Role.Messenger) == 0 && S.Candidates.Any(c => c.Role == Role.Messenger))
            return ("messenger", "Hire a messenger from Staff (H). They're cheap and quick, and they carry paper so your clerks can stay at their desks.");
        if (Count(Role.Linguist) == 0)
            return ("ling", "Hire a linguist for the translation desk.");
        if (S.Stations.Any(s => s.Def.Operator != null && s.Input.Count > 5 && s.OperatorId == null))
            return ("idle", "A desk has a pile of paper and nobody to work it: the ! marks it. Hire someone of that role, or pin a person to it from the desk's panel.");
        if (S.Stats.DeliveredTotal == 0)
            return ("wait", "Good. Let it run: press 3 for top speed. Press F to see where paper flows and where it is held up.");
        if (!Has(StationType.Canteen) && S.AverageEnergy < 0.6)
            return ("tea", "People are tiring. A canteen lets them take a tea break and come back fresh.");
        if (S.Day >= 2 && Rig_Quarter() == 0)
            return ("rotate", "You can turn the view with , and . (or the ⟲ ⟳ buttons) to see behind tall walls.");
        return null;
    }

    int Rig_Quarter() => _main.Rig.Quarter;

    void RefreshHints()
    {
        var h = Settings.Hints && !ModalOpen ? NextHint() : null;
        if (h is { } hint && _hintsDone.Contains(hint.id)) h = null;
        _hint.Visible = h != null;
        if (h is { } x && x.id != _hintId)
        {
            _hintId = x.id;
            _hintText.Text = x.text;
            _hint.OffsetBottom = _hint.OffsetTop;
        }
    }

    // ------------------------------------------------------------------ menus

    /// <summary>Opens a sheet by name, for screenshots.</summary>
    public void ShowNamed(string name)
    {
        switch (name)
        {
            case "pause": ShowPauseMenu(); break;
            case "settings": ShowSettings(); break;
            case "save": ShowSaves(true); break;
            case "load": ShowSaves(false); break;
            case "newgame": ShowNewGame(); break;
            case "dilemma" when S.Dilemmas.Count > 0: ShowDilemma(S.Dilemmas[0]); break;
            case "dilemma": S.RaiseDilemma(S.DebugDilemma()); ShowDilemma(S.Dilemmas[0]); break;
            case "end": ShowEnd(true); break;
            case "warmap": ShowWarMap(); break;
            case "intercept" when S.Archive.Count > 0:
                var a = S.Archive[^1];
                ShowIntercept(S.Intercept(a), ReadStage.Translated, a.Net, a.Day, false, null);
                break;
            case "cipher" when S.Messages.Values.FirstOrDefault(m => Intercepts.StageOf(m) == ReadStage.Intercept) is { } live:
                ShowIntercept(S.Intercept(live), ReadStage.Intercept, live.Net, live.Day, live.Garbled, live.Next);
                break;
        }
    }

    public void ShowPauseMenu()
    {
        var v = Paper(460);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        v.AddChild(Ink("Paused", 30, Fonts.Heading));
        v.AddChild(Ink($"{S.Act.ChapterTitle}: {S.Act.Title}\n{S.Date:dddd d MMMM yyyy} · {Defs.Difficulties[S.Difficulty].Name}{(S.Mode == GameMode.FreePlay ? " · free play" : "")}", 14));
        foreach (var (label, act) in new (string, Action)[]
                 {
                     ("Resume", CloseModal),
                     ("Save game", () => ShowSaves(true)),
                     ("Load game", () => ShowSaves(false)),
                     ("Settings", ShowSettings),
                     ("New game", ShowNewGame),
                     ("Quit to desktop", () => GetTree().Quit()),
                 })
        {
            var b = PaperButton(label, act);
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            v.AddChild(b);
        }
    }

    void ShowSaves(bool saving)
    {
        var v = Paper(560);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        v.AddChild(Ink(saving ? "Save game" : "Load game", 28, Fonts.Heading));
        foreach (var (slot, info) in Saves.List())
        {
            if (saving && slot == "auto") continue;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var text = Ink(info == null
                ? $"{Saves.SlotName(slot)}: empty"
                : $"{Saves.SlotName(slot)}: {info.Title}\n{info.Date} · £{info.Funds:N0} · saved {info.SavedAt:d MMM HH:mm}", 13);
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(text);
            var s = slot;
            if (saving)
                row.AddChild(PaperButton(info == null ? "Save" : "Overwrite", () => { if (_main.SaveTo(s) == null) CloseModal(); }));
            else if (info != null)
                row.AddChild(PaperButton("Load", () => { _titleOpen = false; CloseModal(); _main.LoadFrom(s); }));
            v.AddChild(row);
            v.AddChild(new HSeparator());
        }
        v.AddChild(Ink("F5 quicksaves and F8 loads the quicksave. The game also saves itself every morning.", 12, Fonts.Body));
        var back = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        back.AddChild(PaperButton("Back", () => { if (_titleOpen) ShowTitle(); else ShowPauseMenu(); }));
        v.AddChild(back);
    }

    void ShowSettings()
    {
        var v = Paper(560);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        v.AddChild(Ink("Settings", 28, Fonts.Heading));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 10);
        v.AddChild(grid);

        void Slider(string label, float value, float min, float max, float step, Action<float> set, Func<float, string> show)
        {
            grid.AddChild(Ink(label, 14, Fonts.Body, wrap: false));
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var sl = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(260, 20), FocusMode = Control.FocusModeEnum.None };
            var val = Ink(show(value), 13, Fonts.Typewriter, wrap: false);
            val.CustomMinimumSize = new Vector2(52, 0);
            sl.ValueChanged += x => { set((float)x); val.Text = show((float)x); Settings.Apply(); Settings.Save(); };
            row.AddChild(sl);
            row.AddChild(val);
            grid.AddChild(row);
        }
        void Toggle(string label, bool value, Action<bool> set)
        {
            grid.AddChild(Ink(label, 14, Fonts.Body, wrap: false));
            var cb = new CheckBox { ButtonPressed = value, FocusMode = Control.FocusModeEnum.None };
            cb.AddThemeColorOverride("font_color", UiTheme.PaperInk);
            foreach (var st in new[] { "normal", "pressed", "hover", "hover_pressed", "focus" })
                cb.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
            cb.AddThemeColorOverride("icon_normal_color", UiTheme.PaperInk);
            cb.Toggled += x => { set(x); Settings.Apply(); Settings.Save(); };
            grid.AddChild(cb);
        }
        static string P(float x) => $"{(int)Math.Round(x * 100)}%";
        Slider("Master volume", Settings.MasterVolume, 0, 1, 0.05f, x => Settings.MasterVolume = x, P);
        Slider("Sound effects", Settings.SfxVolume, 0, 1, 0.05f, x => Settings.SfxVolume = x, P);
        Slider("Ambience", Settings.AmbienceVolume, 0, 1, 0.05f, x => Settings.AmbienceVolume = x, P);
        Slider("Interface size", Settings.UiScale, 0.75f, 1.75f, 0.05f, x => Settings.UiScale = x, P);
        Toggle("Scroll at screen edges", Settings.EdgeScroll, x => Settings.EdgeScroll = x);
        Toggle("Hints and tips", Settings.Hints, x => { Settings.Hints = x; _hintsDone.Clear(); });
        Toggle("Film grain and vignette", Settings.FilmGrain, x => Settings.FilmGrain = x);
        Toggle("Fullscreen", Settings.Fullscreen, x => Settings.Fullscreen = x);
        var back = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        back.AddChild(PaperButton("Done", () => { if (_titleOpen) ShowTitle(); else ShowPauseMenu(); }));
        v.AddChild(back);
    }

    bool _titleOpen;
    Difficulty _ngDifficulty = Difficulty.Normal;
    GameMode _ngMode = GameMode.Campaign;
    int _ngAct;

    void ShowNewGame()
    {
        var v = Paper(640);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        v.AddChild(Ink("New game", 28, Fonts.Heading));

        HBoxContainer Choice<T>(IEnumerable<(T value, string label, string tip)> items, T current, Action<T> set)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            foreach (var (value, label, tip) in items)
            {
                var b = PaperButton(label, () => { set(value); ShowNewGame(); });
                b.TooltipText = tip;
                if (!EqualityComparer<T>.Default.Equals(value, current))
                {
                    b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0, 0, 0, 0), UiTheme.PaperInk, 2, 18, 8));
                    b.AddThemeColorOverride("font_color", UiTheme.PaperInk);
                }
                row.AddChild(b);
            }
            return row;
        }

        v.AddChild(Ink("Game", 13, Fonts.BodyBold, UiTheme.Stamp));
        v.AddChild(Choice(new[]
        {
            (GameMode.Campaign, "Campaign", "From the first police ciphers in 1939 to SHARK in December 1942, with Tunny and Colossus to follow."),
            (GameMode.FreePlay, "Free play", "No objectives and no closure: requests keep coming, and the war moves on every ten days."),
        }, _ngMode, m => _ngMode = m));
        v.AddChild(Ink(_ngMode == GameMode.Campaign
            ? "Five chapters of the war as Bletchley saw it, from the police ciphers of 1939 to the breaking of SHARK in 1942. A sixth, Tunny and Colossus, follows if you want it."
            : "A sandbox. Pick where to start; everything earlier is already built and researched.", 13, Fonts.Body));

        v.AddChild(Ink("Difficulty", 13, Fonts.BodyBold, UiTheme.Stamp));
        v.AddChild(Choice(Enum.GetValues<Difficulty>().Select(d => (d, Defs.Difficulties[d].Name, Defs.Difficulties[d].Blurb)), _ngDifficulty, d => _ngDifficulty = d));
        v.AddChild(Ink(Defs.Difficulties[_ngDifficulty].Blurb + $" Starting funds £{Defs.Difficulties[_ngDifficulty].StartFunds:N0}.", 13, Fonts.Body));

        if (_ngMode == GameMode.FreePlay)
        {
            v.AddChild(Ink("Start in", 13, Fonts.BodyBold, UiTheme.Stamp));
            var pick = new OptionButton { FocusMode = Control.FocusModeEnum.None };
            for (int i = 0; i < Campaign.Acts.Length; i++)
            {
                var a = Campaign.Acts[i];
                if (i > 0 && Campaign.Acts[i - 1].Chapter == a.Chapter) continue;
                pick.AddItem($"Chapter {a.Chapter}: {a.ChapterTitle} ({a.Date:MMMM yyyy})", i);
                if (i == _ngAct) pick.Select(pick.ItemCount - 1);
            }
            if (pick.Selected < 0) { pick.Select(0); _ngAct = pick.GetItemId(0); }
            pick.ItemSelected += idx => _ngAct = pick.GetItemId((int)idx);
            v.AddChild(pick);
        }

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(PaperButton("Back", () => { if (_titleOpen) ShowTitle(); else ShowPauseMenu(); }));
        row.AddChild(PaperButton("Begin", () =>
        {
            _titleOpen = false;
            CloseModal();
            _main.NewGame(Random.Shared.Next(100000), showTitle: false, _ngDifficulty, _ngMode, _ngMode == GameMode.FreePlay ? _ngAct : 0);
        }));
        v.AddChild(row);
    }

    // ---------------------------------------------------------------- dilemmas

    public void ShowDilemma(Dilemma d)
    {
        var v = Paper(660);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        v.AddChild(Ink($"{S.Date:d MMMM yyyy}, {(int)S.MinuteOfDay / 60:00}:{(int)S.MinuteOfDay % 60:00}", 13));
        v.AddChild(Ink(d.Service == Service.Security ? "SECURITY" : $"ULTRA · {Defs.ServiceNames[d.Service].ToUpperInvariant()}", 13, Fonts.BodyBold, UiTheme.Stamp));
        v.AddChild(Ink(d.Title, 26, Fonts.Heading));
        v.AddChild(Ink(d.Text, 15));
        v.AddChild(Ink($"{Defs.ServiceNames[d.Service]} suspicion now {(int)S.SuspicionOf(d.Service)} of {(int)Defs.SuspicionLimit}.", 12, Fonts.Body,
            S.SuspicionOf(d.Service) >= Defs.SuspicionWarn ? UiTheme.Stamp : null));
        for (int i = 0; i < d.Options.Count; i++)
        {
            var o = d.Options[i];
            int idx = i, id = d.Id;
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 2);
            var b = PaperButton(o.Label, () =>
            {
                CloseModal();
                S.ResolveDilemma(id, idx);
            });
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            box.AddChild(b);
            var fx = o.Effects();
            if (fx.Length > 0) box.AddChild(Ink(fx, 12, Fonts.Body, UiTheme.PaperInk with { A = 0.75f }));
            v.AddChild(box);
        }
    }
}

/// <summary>A small line chart of daily figures.</summary>
public partial class LineChart : Control
{
    public double[] Values = Array.Empty<double>();
    public int FirstDay;
    public double Min, Max = 1;
    public Color Color = Colors.White;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.2f));
        for (int k = 1; k < 4; k++)
            DrawLine(new Vector2(0, size.Y * k / 4), new Vector2(size.X, size.Y * k / 4), new Color(1, 1, 1, 0.05f));
        if (Values.Length < 2) return;
        double span = Math.Max(1e-6, Max - Min);
        var pts = new Vector2[Values.Length];
        for (int i = 0; i < Values.Length; i++)
            pts[i] = new Vector2(size.X * i / (Values.Length - 1), size.Y - 4 - (float)((Values[i] - Min) / span) * (size.Y - 8));
        var fill = new Vector2[pts.Length + 2];
        pts.CopyTo(fill, 0);
        fill[^2] = new Vector2(size.X, size.Y);
        fill[^1] = new Vector2(0, size.Y);
        DrawColoredPolygon(fill, Color with { A = 0.12f });
        DrawPolyline(pts, Color, 2, true);
        DrawCircle(pts[^1], 3, Color);
        var font = Fonts.Body;
        DrawString(font, new Vector2(2, size.Y - 3), $"day {FirstDay + 1}", HorizontalAlignment.Left, -1, 10, new Color(1, 1, 1, 0.4f));
    }
}
