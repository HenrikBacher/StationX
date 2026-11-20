using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>
/// Colours and words for the utilisation states, shared by the panels, the inspectors and both overlays (Workload and
/// the flow view), so a colour means the same thing wherever it shows.
/// </summary>
public static class UseLook
{
    // Quiet and after hours are faint: time nobody needed the desk isn't something to act on, and the colours that
    // are (working, no paper, paper with nobody at it) must stand out against them.
    public static readonly Color Desk4 = new(0.49f, 0.47f, 0.42f, 0.30f);

    public static readonly Color[] Desk =
    {
        Gfx.Good,                           // working
        Gfx.Warn,                           // staffed or ready, no paper
        Gfx.Bad,                            // paper, nobody at it
        new("8a62a8"),                      // out of action
        Desk4,                              // quiet
        new(0.25f, 0.23f, 0.19f, 0.18f),    // after hours
    };

    public static readonly string[] DeskWords = { "working", "no paper", "paper, nobody at it", "out of action", "quiet", "after hours" };

    public static readonly Color[] Person =
    {
        Gfx.Good,                           // at work
        new("6fa3c9"),                      // carrying
        new("44708f"),                      // walking
        new("b08fc4"),                      // on a break
        Gfx.Warn,                           // idle
        new(0.25f, 0.23f, 0.19f, 0.18f),    // off watch
        new("8c4a46"),                      // sick or on leave
    };

    public static readonly string[] PersonWords = { "working", "carrying", "walking", "on a break", "idle", "off watch", "away" };

    /// <summary>A ring or figure for paper waiting: grey when none, then by how long the oldest has waited.</summary>
    public static Color Wait(int count, double oldest) =>
        count == 0 ? new Color(0.6f, 0.6f, 0.55f, 0.5f) : oldest < 30 ? Gfx.Good : oldest < 120 ? Gfx.Warn : Gfx.Bad;

    public static readonly (string Words, Color Colour)[] WaitKey =
    {
        ("waiting under 30 min", Gfx.Good), ("30 min to 2 h", Gfx.Warn), ("over 2 h", Gfx.Bad), ("nothing waiting", new Color(0.6f, 0.6f, 0.55f)),
    };

    /// <summary>"1h 05m" or "25m": how long the oldest message has waited.</summary>
    public static string Age(double minutes) => minutes >= 60 ? $"{(int)(minutes / 60)}h {(int)(minutes % 60):00}m" : $"{(int)minutes}m";

    /// <summary>"3 waiting, 1h 05m": the same words on the floor in the flow view and the workload view.</summary>
    public static string Waiting(int count, double oldest) => count == 0 ? "" : $"{count} waiting, {Age(oldest)}";

    /// <summary>A desk's shares over its open hours: after hours (before there are watches) left out.</summary>
    public static double[] Open(double[] share)
    {
        double open = 1 - share[(int)DeskUse.Closed];
        var o = new double[share.Length];
        if (open > 1e-6) for (int k = 0; k < share.Length; k++) o[k] = k == (int)DeskUse.Closed ? 0 : share[k] / open;
        return o;
    }

    /// <summary>"62% working · 20% paper, nobody at it" for the largest shares.</summary>
    public static string Summary(double[] share, string[] words, int take = 3, double min = 0.03) =>
        string.Join(" · ", share.Select((v, i) => (v, i)).Where(x => x.v >= min).OrderByDescending(x => x.v).Take(take)
                                .Select(x => $"{Math.Round(x.v * 100):0}% {words[x.i]}"));
}

/// <summary>A horizontal bar split by share, in state colours.</summary>
public partial class ShareBar : Control
{
    public double[] Shares = Array.Empty<double>();
    public Color[] Colours = Array.Empty<Color>();

    public override void _Ready() => MouseFilter = string.IsNullOrEmpty(TooltipText) ? MouseFilterEnum.Ignore : MouseFilterEnum.Pass;

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.3f));
        float x = 0;
        for (int i = 0; i < Shares.Length && i < Colours.Length; i++)
        {
            float w = (float)(Shares[i] * size.X);
            if (w <= 0) continue;
            DrawRect(new Rect2(x, 0, Math.Min(w, size.X - x), size.Y), Colours[i]);
            x += w;
        }
    }
}

/// <summary>A small line of daily figures (0 to 1) for the trend: the last fortnight, today on the right.</summary>
public partial class Spark : Control
{
    /// <summary>One value a day, oldest first; NaN where there's no figure for that day.</summary>
    public double[] Values = Array.Empty<double>();
    public Color Colour = UiTheme.TextDim;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Pass;   // for the tooltip
        CustomMinimumSize = new Vector2(64, 14);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.22f));
        DrawLine(new Vector2(0, size.Y * 0.5f), new Vector2(size.X, size.Y * 0.5f), new Color(1, 1, 1, 0.06f));
        int n = Values.Length;
        if (n == 0) return;
        float step = n > 1 ? (size.X - 4) / (n - 1) : 0;
        // Scaled to the next quarter above the highest day, so a desk busy 5–15% still shows its ups and downs.
        double top = Math.Clamp(Math.Ceiling(Values.Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Max() * 4) / 4, 0.25, 1);
        Vector2 At(int i) => new(2 + i * step, size.Y - 2 - (float)Math.Clamp(Values[i] / top, 0, 1) * (size.Y - 4));
        var run = new List<Vector2>();
        void Flush()
        {
            if (run.Count >= 2) DrawPolyline(run.ToArray(), Colour, 1.5f, true);
            else if (run.Count == 1) DrawCircle(run[0], 1.2f, Colour);
            run.Clear();
        }
        for (int i = 0; i < n; i++)
        {
            if (double.IsNaN(Values[i])) { Flush(); continue; }
            run.Add(At(i));
        }
        Flush();
        for (int i = n - 1; i >= 0; i--)
            if (!double.IsNaN(Values[i])) { DrawCircle(At(i), 2.2f, Colour); break; }
    }
}

/// <summary>The last 24 hours of a desk or person, hour by hour (oldest on the left), with a line of totals.</summary>
public partial class UseStrip : Control
{
    public Func<GameState> State = null!;
    public Func<UseLog?> Log = null!;
    public bool Person;

    const float BarH = 18;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Pass;   // for the tooltip
        CustomMinimumSize = new Vector2(0, BarH + 30);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var s = State();
        var log = Log();
        var size = Size;
        var colours = Person ? UseLook.Person : UseLook.Desk;
        DrawRect(new Rect2(0, 0, size.X, BarH), new Color(0, 0, 0, 0.3f));
        int now = s.UseSlotNow;
        float cw = size.X / UseLog.Slots;
        var font = Fonts.Body;
        var dim = new Color(1, 1, 1, 0.45f);
        for (int i = 0; i < UseLog.Slots; i++)
        {
            int slot = (now + 1 + i) % UseLog.Slots;
            float x = i * cw;
            if (slot % 8 == 0)
            {
                // Watch changes: 00, 08, 16.
                DrawLine(new Vector2(x, 0), new Vector2(x, BarH + 3), dim, 1);
                if (x < size.X - 40) DrawString(font, new Vector2(x + 2, BarH + 12), $"{slot:00}", HorizontalAlignment.Left, -1, 10, dim);
            }
            if (log == null) continue;
            double total = log.SlotTotal(slot);
            if (total <= 0) continue;
            // Each hour is drawn as a share of the hour it covers, so the hour under way grows from the bottom.
            double span = slot == now ? Math.Max(total, 1) : Math.Max(total, 60);
            float y = BarH;
            for (int k = 0; k < log.States; k++)
            {
                float h = (float)(log.In(slot, k) / span * BarH);
                if (h <= 0) continue;
                DrawRect(new Rect2(x + 0.5f, y - h, cw - 1, h), colours[k]);
                y -= h;
            }
        }
        DrawString(font, new Vector2(size.X - 22, BarH + 12), "now", HorizontalAlignment.Left, -1, 10, dim);
        string text;
        if (log == null || log.Total() <= 0) text = "No record yet: it builds up hour by hour.";
        else if (Person)
        {
            double duty = log.OnWatch();
            var share = new double[log.States];
            if (duty > 0) for (int k = 0; k <= (int)PersonUse.Idle; k++) share[k] = log.In(k) / duty;
            text = duty <= 0 ? "Not on watch in the last day." : $"{duty / 60:0} h on watch: " + UseLook.Summary(share, UseLook.PersonWords);
        }
        else
        {
            double total = log.Total();
            var share = UseLook.Open(Enumerable.Range(0, log.States).Select(k => log.In(k) / total).ToArray());
            text = UseLook.Summary(share, UseLook.DeskWords, 2) + $" · {log.DoneTotal()} done";
        }
        DrawString(font, new Vector2(0, BarH + 26), text, HorizontalAlignment.Left, size.X, 11, UiTheme.TextDim);
    }
}

public partial class Hud
{
    /// <summary>Rebuilt every ten game minutes, the figures change slowly.</summary>
    string WorkloadSig() => $"{(int)(S.Now / 10)}|{S.Stations.Count}|{S.Staff.Count}|{S.UseDays.Count}";

    const int TrendDays = 14;

    /// <summary>The last fortnight of one figure from the daily trend, as 0–1 (NaN where there's none).</summary>
    double[] Trend(Func<UseDay, int?> f) =>
        S.UseDays.TakeLast(TrendDays).Select(d => f(d) is int v ? v / 100.0 : double.NaN).ToArray();

    static string TrendTip(double[] values, string what)
    {
        var known = values.Where(v => !double.IsNaN(v)).ToArray();
        if (known.Length == 0) return "";
        return $"\nThe line: {what} day by day over the last {(values.Length == 1 ? "day" : $"{values.Length} days")}, " +
               $"from {Math.Round(known[0] * 100):0}% to {Math.Round(known[^1] * 100):0}% yesterday.";
    }

    void WorkloadPanel()
    {
        var r = S.Utilisation();
        _sideBox.AddChild(UiTheme.Heading("Workload"));
        string span = r.Hours >= 23.5 ? "the last 24 hours" : r.Hours >= 1 ? $"the last {r.Hours:0} hours (the record builds up to a day)" : "the last few minutes (the record builds up to a day)";
        _sideBox.AddChild(UiTheme.Label($"How busy each kind of desk, machine and person has been over {span}, and the trend day by day. While this panel is open, " +
                                        "a ring round every desk shows its day and the dot in the middle what it is doing now; turn on the flow view (F) as well to see where the paper goes. Click a row to go there.", 12, UiTheme.TextDim));

        var top = r.Top;
        var v = Card(top == null ? Gfx.Good : top.Kind is "bottleneck" or "unstaffed" ? Gfx.Bad : Gfx.Warn);
        if (top == null)
            v.AddChild(UiTheme.Label(r.Hours < 3 ? "Too early to say where the line is stuck: give it a few hours." : "No bottleneck: every step has kept up over the last day.", 13, UiTheme.Text, Fonts.BodyBold));
        else
        {
            var head = UiTheme.Label(top.Text, 13, UiTheme.Text, Fonts.BodyBold);
            v.AddChild(head);
            foreach (var f in r.Findings.Skip(1).Take(2))
                v.AddChild(UiTheme.Label(f.Text, 12, UiTheme.TextDim));
        }

        _sideBox.AddChild(UiTheme.Caps("Desks and machines", 12, UiTheme.Accent));
        _sideBox.AddChild(Legend(UseLook.Desk.Take(5).ToArray(), UseLook.DeskWords.Take(5).ToArray()));
        if (r.Steps.Count == 0) _sideBox.AddChild(UiTheme.Label("No desks yet.", 12, UiTheme.TextDim));
        foreach (var st in r.Steps)
        {
            double busy = st.OfOpen(DeskUse.Working);
            string right = $"{Math.Round(busy * 100):0}% busy" + (st.Waiting > 0 ? $" · {st.Waiting} waiting" : "");
            var colour = busy >= 0.75 && st.Waiting > st.Count ? Gfx.Bad : st.OfOpen(DeskUse.Unstaffed) >= 0.2 ? Gfx.Warn : UiTheme.Text;
            var t = st.Type;
            var trend = Trend(d => d.Steps.TryGetValue(t, out var x) ? x : null);
            bool spare = st.Count > 1 && st.Peak < st.Count && r.Hours >= 12;
            string tip = $"{UseLook.Summary(UseLook.Open(st.Share), UseLook.DeskWords, 5, 0.005)}.\n{st.Done} finished in the last day." +
                         (st.Count > 1 ? $"\nAt the busiest hour, {(st.Peak == 0 ? "none" : st.Peak.ToString())} of the {st.Count} were worked at once" + (spare ? ": the rest stood empty." : ".") : "") +
                         (st.Waiting > 0 ? $"\n{st.Waiting} waiting for this step, for {Wait(st.AvgWait)} on average (the oldest {Wait(st.OldestWait)})." : "") +
                         TrendTip(trend, "the share of the day busy") +
                         "\nClick to go to the one most in need.";
            int? id = st.Worst;
            UseRow($"{GameState.Plural(st.Type)}{(st.Count > 1 ? $" ×{st.Count}" : "")}", spare ? st.Peak == 0 ? "none in use" : $"at most {st.Peak} in use" : null,
                UseLook.Open(st.Share), UseLook.Desk, right, colour, tip, trend, Gfx.Good,
                () => { if (id is int sid && S.StationById(sid) is { } x) { _main.CenterOnTile(x.X + 1, x.Y + 1); _main.Select(x.Id, null); } });
        }

        if (r.Roles.Count > 0)
        {
            _sideBox.AddChild(UiTheme.Caps("Staff, while on watch", 12, UiTheme.Accent));
            _sideBox.AddChild(Legend(UseLook.Person.Take(5).ToArray(), UseLook.PersonWords.Take(5).ToArray()));
            foreach (var ro in r.Roles)
            {
                double work = ro.Share[(int)PersonUse.Working] + ro.Share[(int)PersonUse.Carrying], idle = ro.Share[(int)PersonUse.Idle];
                string right = ro.Minutes <= 0 ? "no record yet" : $"{Math.Round(work * 100):0}% at work · {Math.Round(idle * 100):0}% idle";
                var role = ro.Role;
                var trend = Trend(d => d.Roles.TryGetValue(role, out var x) ? x : null);
                string tip = ro.Minutes <= 0 ? "" : $"{UseLook.Summary(ro.Share, UseLook.PersonWords, 5, 0.005)}, of {ro.Minutes / 60:0} hours on watch between them.\n" +
                             "At work is at a desk or carrying paper." +
                             (ro.Spare > 0 ? $"\n{ro.Spare} of them stood idle most of their watch: Staff (H) lists them first, to move or let go." : "") +
                             TrendTip(trend, "the share of their time on watch idle") +
                             "\nClick to find one of them, someone standing about first.";
                int? pid = ro.Pick;
                UseRow($"{Defs.Roles[ro.Role].Plural} {ro.Staff}{(ro.OnDuty < ro.Staff ? $" ({ro.OnDuty} in)" : "")}", ro.Spare > 0 ? $"{ro.Spare} spare" : null,
                    ro.Share.Take(5).ToArray(), UseLook.Person, right, idle >= 0.5 && ro.Staff >= 2 ? Gfx.Warn : UiTheme.Text, tip, trend, Gfx.Warn,
                    () => { if (pid is int p && S.StaffById(p) is { Away: false } x) { _main.Select(null, x.Id); _main.CenterOnTile((float)x.X, (float)x.Y); } });
            }
            if (S.UseDays.Count > 0)
                _sideBox.AddChild(UiTheme.Label("The small lines are the last fortnight, a day at a time: green how busy the desks were, amber how idle the people. " +
                                                "Statistics (K) has the whole Park.", 11, UiTheme.TextDim));
        }

        if (r.Watches.Count > 0)
        {
            _sideBox.AddChild(UiTheme.Caps("Watches", 12, UiTheme.Accent));
            foreach (var w in r.Watches)
            {
                var def = Defs.Shifts[w.Shift];
                var line = UiTheme.Label($"{def.Name} {def.StartHour:00}–{def.EndHour:00}: desks busy {Math.Round(w.DeskBusy * 100):0}%, paper with nobody at it {Math.Round(w.DeskUnstaffed * 100):0}% · people at work {Math.Round(w.PeopleWorking * 100):0}%, idle {Math.Round(w.PeopleIdle * 100):0}%",
                    12, w.DeskUnstaffed >= 0.2 ? Gfx.Warn : UiTheme.TextDim);
                _sideBox.AddChild(line);
            }
            if (r.Staffing.Count > 0) StaffingTable(r, _sideBox);
        }
    }

    /// <summary>
    /// Each role on each watch: how many are on it against how many the work needed at its busiest hour, red where
    /// paper waited with nobody, amber where there are hands to spare. Shared by the Staff and Workload panels.
    /// </summary>
    void StaffingTable(UseReport r, Control into)
    {
        var roles = Enum.GetValues<Role>().Where(S.RoleOpen).Where(x => r.Staffing.Any(w => w.Role == x)).ToList();
        if (roles.Count == 0) return;
        into.AddChild(UiTheme.Label("On each watch, against how many the work needed at its busiest hour (desks with paper, and paper being carried). " +
                                    "Red: paper waited with nobody. Amber: hands to spare.", 11, UiTheme.TextDim));
        var table = new GridContainer { Columns = roles.Count + 1 };
        table.AddThemeConstantOverride("h_separation", 4);
        table.AddThemeConstantOverride("v_separation", 0);
        Label Cell(string text, Color colour, bool first = false, string tip = "")
        {
            var c = UiTheme.Label(text, 12, colour, wrap: false);
            if (first) c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            else { c.CustomMinimumSize = new Vector2(40, 0); c.HorizontalAlignment = HorizontalAlignment.Center; }
            if (tip != "") { c.TooltipText = tip; c.MouseFilter = Control.MouseFilterEnum.Pass; }
            return c;
        }
        table.AddChild(Cell("on / needed", UiTheme.TextDim, true));
        foreach (var ro in roles) table.AddChild(Cell(ShortRole(ro), Gfx.RoleColor(ro).Lightened(0.35f), tip: Defs.Roles[ro].Plural));
        foreach (var sh in new[] { Shift.Day, Shift.Evening, Shift.Night })
        {
            var sd = Defs.Shifts[sh];
            table.AddChild(Cell($"{sh} {sd.StartHour:00}–{sd.EndHour:00}", UiTheme.Text, true));
            foreach (var ro in roles)
            {
                if (r.Watch(ro, sh) is not { } w) { table.AddChild(Cell("–", UiTheme.TextDim)); continue; }
                string who = GameState.PluralWord(ro);
                string tip = $"{Defs.Roles[ro].Plural} on the {sd.Name.ToLowerInvariant()}: {w.On} on it" + (w.Desks > 0 ? $", for {w.Desks} desk{(w.Desks == 1 ? "" : "s")}" : "") + ".\n" +
                             (w.Minutes <= 0 && w.Needed == 0 ? "No record for this watch in the last day." :
                                 $"At its busiest hour the work needed {w.Needed}." +
                                 (w.Desks > 0 ? $" Paper waited at their desks with nobody at it {Pct(w.Nobody)} of the watch." : "") +
                                 (w.Minutes > 0 ? $" Those on it were idle {Pct(w.Idle)} of their time." : "")) +
                             (w.Short ? $"\nShort: move {who} onto this watch, or hire more." : w.Spare ? $"\nMore than the work needs: move some {who} to a short watch, or let them go." : "");
                var colour = w.Short ? Gfx.Bad : w.Spare ? Gfx.Warn : w.On == 0 && w.Needed == 0 ? UiTheme.TextDim : UiTheme.Text;
                table.AddChild(Cell($"{w.On}/{w.Needed}", colour, tip: tip));
            }
        }
        into.AddChild(table);
    }

    static string Wait(double minutes) => minutes >= 90 ? $"{minutes / 60:0.#} h" : $"{minutes:0} min";

    static Control Legend(Color[] colours, string[] words)
    {
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 10);
        flow.AddThemeConstantOverride("v_separation", 2);
        for (int i = 0; i < colours.Length; i++)
        {
            var item = new HBoxContainer();
            item.AddThemeConstantOverride("separation", 4);
            // Faint colours (quiet) get an outline, so the key still shows them.
            var swatch = new Panel { CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            var sb = UiTheme.Box(colours[i], new Color(1, 1, 1, colours[i].A < 0.5f ? 0.35f : 0), 1, 0);
            sb.SetBorderWidthAll(1);
            swatch.AddThemeStyleboxOverride("panel", sb);
            item.AddChild(swatch);
            item.AddChild(UiTheme.Label(words[i], 11, UiTheme.TextDim, wrap: false));
            flow.AddChild(item);
        }
        return flow;
    }

    /// <summary>One clickable row: a name (and a note), a figure, a bar of shares and the fortnight's trend.</summary>
    void UseRow(string name, string? note, double[] shares, Color[] colours, string right, Color colour, string tip, double[] trend, Color trendColour, Action go)
    {
        var box = new PanelContainer { TooltipText = tip, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        var normal = UiTheme.Box(UiTheme.PanelLight, UiTheme.PanelEdge with { A = 0 }, 3, 6, 3);
        box.AddThemeStyleboxOverride("panel", normal);
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 2);
        box.AddChild(v);
        var n = UiTheme.Label(name, 13, UiTheme.Text, Fonts.BodyBold, wrap: false);
        n.MouseFilter = Control.MouseFilterEnum.Ignore;
        var items = new List<Control> { n };
        if (note != null)
        {
            var nl = UiTheme.Label(note, 11, Gfx.Warn, wrap: false);
            nl.MouseFilter = Control.MouseFilterEnum.Ignore;
            items.Add(nl);
        }
        items.Add(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var fig = UiTheme.Label(right, 12, colour, wrap: false);
        fig.MouseFilter = Control.MouseFilterEnum.Ignore;
        items.Add(fig);
        var row = Row(items.ToArray());
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        v.AddChild(row);
        var bar = new ShareBar { Shares = shares, Colours = colours, CustomMinimumSize = new Vector2(0, 8), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        if (trend.Length > 0)
        {
            var spark = new Spark { Values = trend, Colour = trendColour };
            spark.MouseFilter = Control.MouseFilterEnum.Ignore;
            var line = Row(bar, spark);
            line.MouseFilter = Control.MouseFilterEnum.Ignore;
            v.AddChild(line);
        }
        else v.AddChild(bar);
        box.MouseEntered += () => box.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.PanelLight.Lightened(0.08f), UiTheme.Accent, 3, 6, 3));
        box.MouseExited += () => box.AddThemeStyleboxOverride("panel", normal);
        box.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                Sound.Play("click", 0.5f);
                go();
            }
        };
        _sideBox.AddChild(box);
    }

    /// <summary>The last day of a desk or person, for the inspector.</summary>
    Control UseStripFor(Station? st, Staff? p)
    {
        int id = st?.Id ?? p!.Id;
        bool person = p != null;
        return new UseStrip
        {
            State = () => S,
            Log = () => person ? S.StaffById(id)?.Use : S.StationById(id)?.Use,
            Person = person,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = person
                ? "The last 24 hours, hour by hour: working (green), carrying (light blue), walking (blue), on a break (lilac), idle (amber); off watch is left faint."
                : "The last 24 hours, hour by hour: working (green), no paper (amber), paper with nobody at it (red), out of action (purple); quiet and after hours are left faint.",
        };
    }
}
