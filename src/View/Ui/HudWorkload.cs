using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using StationX.Sim;

namespace StationX.View.Ui;

/// <summary>Colours and words for the utilisation states, shared by the panel, the inspectors and the overlay.</summary>
public static class UseLook
{
    public static readonly Color[] Desk =
    {
        Gfx.Good,               // working
        Gfx.Warn,               // staffed or ready, no paper
        Gfx.Bad,                // paper, nobody at it
        new("8a62a8"),          // out of action
        new("7d786a"),          // quiet
        new("3f3b31"),          // after hours
    };

    public static readonly string[] DeskWords = { "working", "no paper", "paper, nobody at it", "out of action", "quiet", "after hours" };

    public static readonly Color[] Person =
    {
        Gfx.Good,               // at work
        new("6fa3c9"),          // carrying
        new("44708f"),          // walking
        new("b08fc4"),          // on a break
        Gfx.Warn,               // idle
        new("3f3b31"),          // off watch
        new("8c4a46"),          // sick or on leave
    };

    public static readonly string[] PersonWords = { "working", "carrying", "walking", "on a break", "idle", "off watch", "away" };

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

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

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
            double duty = 0;
            for (int k = 0; k <= (int)PersonUse.Idle; k++) duty += log.In(k);
            var share = new double[log.States];
            if (duty > 0) for (int k = 0; k <= (int)PersonUse.Idle; k++) share[k] = log.In(k) / duty;
            text = duty <= 0 ? "Not on watch in the last day." : $"{duty / 60:0} h on watch: " + UseLook.Summary(share, UseLook.PersonWords);
        }
        else
        {
            double total = log.Total();
            var share = Enumerable.Range(0, log.States).Select(k => log.In(k) / total).ToArray();
            text = UseLook.Summary(share, UseLook.DeskWords, 2) + $" · {log.DoneTotal()} done";
        }
        DrawString(font, new Vector2(0, BarH + 26), text, HorizontalAlignment.Left, size.X, 11, UiTheme.TextDim);
    }
}

public partial class Hud
{
    /// <summary>Rebuilt every ten game minutes, the figures change slowly.</summary>
    string WorkloadSig() => $"{(int)(S.Now / 10)}|{S.Stations.Count}|{S.Staff.Count}";

    void WorkloadPanel()
    {
        var r = S.Utilisation();
        _sideBox.AddChild(UiTheme.Heading("Workload"));
        string span = r.Hours >= 23.5 ? "the last 24 hours" : r.Hours >= 1 ? $"the last {r.Hours:0} hours (the record builds up to a day)" : "the last few minutes (the record builds up to a day)";
        _sideBox.AddChild(UiTheme.Label($"How busy each kind of desk, machine and person has been over {span}. While this panel is open, " +
                                        "a ring round every desk shows its day, and the dot in the middle what it is doing now. Click a row to go there.", 12, UiTheme.TextDim));

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
        _sideBox.AddChild(Legend(UseLook.Desk, UseLook.DeskWords));
        if (r.Steps.Count == 0) _sideBox.AddChild(UiTheme.Label("No desks yet.", 12, UiTheme.TextDim));
        foreach (var st in r.Steps)
        {
            double busy = st.OfOpen(DeskUse.Working);
            string right = $"{Math.Round(busy * 100):0}% busy" + (st.Waiting > 0 ? $" · {st.Waiting} waiting" : "");
            var colour = busy >= 0.75 && st.Waiting > st.Count ? Gfx.Bad : st.OfOpen(DeskUse.Unstaffed) >= 0.2 ? Gfx.Warn : UiTheme.Text;
            string tip = $"{UseLook.Summary(st.Share, UseLook.DeskWords, 5, 0.005)}.\n{st.Done} finished in the last day." +
                         (st.Waiting > 0 ? $"\n{st.Waiting} waiting for this step, for {Wait(st.AvgWait)} on average (the oldest {Wait(st.OldestWait)})." : "") +
                         "\nClick to go to the one most in need.";
            int? id = st.Worst;
            UseRow($"{GameState.Plural(st.Type)}{(st.Count > 1 ? $" ×{st.Count}" : "")}", st.Share, UseLook.Desk, right, colour, tip,
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
                string tip = ro.Minutes <= 0 ? "" : $"{UseLook.Summary(ro.Share, UseLook.PersonWords, 5, 0.005)}, of {ro.Minutes / 60:0} hours on watch between them.\n" +
                             "At work is at a desk or carrying paper. Click to find one of them, someone standing about first.";
                int? pid = ro.Pick;
                UseRow($"{Defs.Roles[ro.Role].Plural} {ro.Staff}{(ro.OnDuty < ro.Staff ? $" ({ro.OnDuty} in)" : "")}", ro.Share.Take(5).ToArray(), UseLook.Person, right,
                    idle >= 0.5 && ro.Staff >= 2 ? Gfx.Warn : UiTheme.Text, tip,
                    () => { if (pid is int p && S.StaffById(p) is { Away: false } x) { _main.Select(null, x.Id); _main.CenterOnTile((float)x.X, (float)x.Y); } });
            }
        }

        if (r.Watches.Count > 0)
        {
            _sideBox.AddChild(UiTheme.Caps("Watches", 12, UiTheme.Accent));
            foreach (var w in r.Watches)
            {
                var def = Defs.Shifts[w.Shift];
                var name = UiTheme.Label($"{def.Name} {def.StartHour:00}–{def.EndHour:00} · {w.Rostered} on it", 13, UiTheme.Text, Fonts.BodyBold);
                _sideBox.AddChild(name);
                var line = UiTheme.Label($"Desks busy {Math.Round(w.DeskBusy * 100):0}%, paper with nobody at it {Math.Round(w.DeskUnstaffed * 100):0}% · people at work {Math.Round(w.PeopleWorking * 100):0}%, idle {Math.Round(w.PeopleIdle * 100):0}%",
                    12, w.DeskUnstaffed >= 0.2 ? Gfx.Warn : UiTheme.TextDim);
                _sideBox.AddChild(line);
            }
        }
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
            item.AddChild(new ColorRect { Color = colours[i], CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            item.AddChild(UiTheme.Label(words[i], 11, UiTheme.TextDim, wrap: false));
            flow.AddChild(item);
        }
        return flow;
    }

    /// <summary>One clickable row: a name, a bar of shares, and a figure.</summary>
    void UseRow(string name, double[] shares, Color[] colours, string right, Color colour, string tip, Action go)
    {
        var box = new PanelContainer { TooltipText = tip, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        var normal = UiTheme.Box(UiTheme.PanelLight, UiTheme.PanelEdge with { A = 0 }, 3, 6, 3);
        box.AddThemeStyleboxOverride("panel", normal);
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", 2);
        box.AddChild(v);
        var n = UiTheme.Label(name, 13, UiTheme.Text, Fonts.BodyBold, wrap: false);
        n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        n.MouseFilter = Control.MouseFilterEnum.Ignore;
        var fig = UiTheme.Label(right, 12, colour, wrap: false);
        fig.MouseFilter = Control.MouseFilterEnum.Ignore;
        var row = Row(n, fig);
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        v.AddChild(row);
        v.AddChild(new ShareBar { Shares = shares, Colours = colours, CustomMinimumSize = new Vector2(0, 8) });
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
                ? "The last 24 hours, hour by hour: working, carrying, walking, on a break, idle, off watch."
                : "The last 24 hours, hour by hour: working (green), no paper (amber), paper with nobody at it (red), out of action (purple), quiet (grey), after hours (dark).",
        };
    }
}
