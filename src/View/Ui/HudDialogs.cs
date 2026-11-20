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
        _titleShowing = _updateOpen = _progressOpen = false;
        Clear(_modalCenter);
    }

    /// <summary>A sheet of government paper with typed text.</summary>
    VBoxContainer Paper(int width)
    {
        Clear(_modalCenter);
        _titleShowing = _updateOpen = _progressOpen = false;
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
        // An offer that arrived while another sheet was open comes first.
        if (Updater.Latest != null && !_offerShown && !Updater.Busy && !_waitingForCheck) { ShowUpdate(true); return; }
        _titleOpen = true;
        var v = Paper(760);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        var title = Ink("STATION X", 64, Fonts.Heading);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        v.AddChild(title);
        var sub = Ink("Bletchley Park, 1939 to 1945", 18, Fonts.Heading, UiTheme.Stamp);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        v.AddChild(sub);
        v.AddChild(Ink(
            "You run the floor at the Government Code and Cypher School. Messages arrive from the listening stations. " +
            "Your clerks, cryptanalysts, Wrens and linguists turn them into intelligence, one desk and one machine at a time.\n\n" +
            "Start with pencil and paper against German police ciphers. Break the U-boats' four-rotor SHARK key, then Hitler's own " +
            "teleprinter cipher with Colossus, and see the war through D-Day and Arnhem to victory in Europe.", 15));
        var controls = Ink(
            "Mouse: click to select · right-drag to pan · wheel to zoom\n" +
            "Keys: WASD pan · , . turn the view · Space pause · 1 2 3 speed · R rotate while building · M move · F flow view · Esc menu\n" +
            "B build · H staff · Q requests · T research · G huts · I intelligence · Y Y-stations · N intercepts · O war map · K statistics\n" +
            "F1 handbook (how each kind of traffic goes through the Park) · F5 quicksave · F8 quickload", 12, Fonts.Body, UiTheme.PaperInk with { A = 0.75f });
        v.AddChild(controls);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(PaperButton("Report for duty", () => { _titleOpen = false; CloseModal(); ShowBriefing(); }));
        row.AddChild(PaperButton("New game…", ShowNewGame));
        if (Saves.List().Any(x => x.info != null)) row.AddChild(PaperButton("Load…", () => ShowSaves(false)));
        row.AddChild(PaperButton("Settings", ShowSettings));
        row.AddChild(PaperButton("Quit", () => Main.QuitGame()));
        v.AddChild(row);
        _titleShowing = true;
        HookUpdater();
    }

    public bool TitleShowing => _titleShowing;
    bool _updaterHooked, _titleShowing, _updateOpen, _progressOpen, _updateBackToTitle, _offerShown, _waitingForCheck;
    Label? _updateStatus;
    ProgressBar? _updateBar;

    /// <summary>At launch: a new version is offered first, on its own sheet, and the title follows.</summary>
    public async void ShowStart()
    {
        HookUpdater();
        _waitingForCheck = true;
        Clear(_modalCenter);
        _modalLayer.Visible = true;
        await System.Threading.Tasks.Task.WhenAny(Updater.Checking, System.Threading.Tasks.Task.Delay(4000));
        _waitingForCheck = false;
        if (!IsInstanceValid(this) || !IsInsideTree()) return;
        if (Updater.Latest != null && !Updater.Busy) ShowUpdate(true);
        else ShowTitle();
    }

    void HookUpdater()
    {
        if (_updaterHooked) return;
        _updaterHooked = true;
        Updater.Changed += OnUpdaterChanged;
    }

    /// <summary>The updater's event is static: let go of it when the HUD goes, so it never calls a freed one.</summary>
    public override void _ExitTree()
    {
        if (_updaterHooked) Updater.Changed -= OnUpdaterChanged;
        _updaterHooked = false;
    }

    void OnUpdaterChanged()
    {
        if (!IsInstanceValid(this) || !IsInsideTree() || _waitingForCheck) return;
        if (_progressOpen)
        {
            if (!Updater.Busy) ShowUpdateProgress(_updateBackToTitle);   // failed: say why, and offer a way back
            else
            {
                if (_updateStatus != null) _updateStatus.Text = Updater.Status ?? "";
                if (_updateBar != null) _updateBar.Value = Math.Max(0, Updater.Progress);
            }
        }
        else if (_updateOpen) ShowUpdate(_updateBackToTitle);   // the patch check finished: the size may have changed
        // A check that finishes late still offers the update straight away, over the title or the game.
        else if (Updater.Latest != null && !_offerShown && (_titleShowing || !ModalOpen)) ShowUpdate(_titleShowing);
    }

    /// <summary>What the new version brings, and the player's go-ahead to install it.</summary>
    void ShowUpdate() => ShowUpdate(_titleOpen);

    void ShowUpdate(bool backToTitle)
    {
        _titleOpen = false;
        var v = Paper(720);
        _updateOpen = _offerShown = true;
        _updateBackToTitle = backToTitle;
        v.AddChild(Ink($"Version {Updater.Latest} is ready", 28, Fonts.Heading));
        v.AddChild(Ink($"You have {(Updater.Current == "" ? "a development build" : $"version {Updater.Current}")}. Your saves and settings are kept. The game downloads " +
                       $"{(Updater.PatchFits ? "just the changed game files" : "the new version")} (about {Updater.DownloadMB} MB), installs it over this one and restarts.", 14));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 360), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var notes = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        notes.AddThemeConstantOverride("separation", 8);
        foreach (var (version, text) in Updater.Notes)
        {
            if (Updater.Notes.Count > 1) notes.AddChild(Ink($"Version {version}", 15, Fonts.BodyBold, UiTheme.Stamp));
            foreach (var line in text.Split('\n'))
            {
                var l = line.Trim();
                if (l.Length == 0) continue;
                bool heading = l is "New" or "Fixes";
                notes.AddChild(heading ? Ink(l, 13, Fonts.BodyBold, UiTheme.Stamp) : Ink(l, 14));
            }
        }
        scroll.AddChild(notes);
        v.AddChild(scroll);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(PaperButton("Not now", () => { if (backToTitle) ShowTitle(); else CloseModal(); }));
        row.AddChild(PaperButton("Update and restart", () => { Updater.Install(); ShowUpdateProgress(backToTitle); }));
        v.AddChild(row);
    }

    /// <summary>The download on its own sheet, until the game restarts (or the update fails and says why).</summary>
    void ShowUpdateProgress(bool backToTitle)
    {
        _titleOpen = false;
        var v = Paper(560);
        _progressOpen = true;
        _updateBackToTitle = backToTitle;
        bool failed = !Updater.Busy && Updater.Status != null;
        v.AddChild(Ink(failed ? "The update didn't work" : $"Updating to version {Updater.Latest}", 26, Fonts.Heading));
        if (!failed) v.AddChild(Ink("Your saves and settings are kept. The game restarts by itself when it's done.", 14));
        _updateBar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001, Value = Math.Max(0, Updater.Progress),
                                       ShowPercentage = false, CustomMinimumSize = new Vector2(0, 14), Visible = !failed };
        _updateBar.AddThemeStyleboxOverride("background", UiTheme.Box(UiTheme.PaperInk with { A = 0.12f }, UiTheme.PaperInk, 1, 0, 0));
        _updateBar.AddThemeStyleboxOverride("fill", UiTheme.Box(UiTheme.Stamp, UiTheme.Stamp, 0, 0, 0));
        v.AddChild(_updateBar);
        _updateStatus = Ink(Updater.Status ?? "Starting…", 14, failed ? Fonts.Typewriter : Fonts.BodyBold, failed ? UiTheme.PaperInk : UiTheme.Stamp);
        v.AddChild(_updateStatus);
        if (failed)
        {
            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
            row.AddThemeConstantOverride("separation", 12);
            if (Updater.PageUrl is { } page) row.AddChild(PaperButton("Releases page", () => OS.ShellOpen(page)));
            row.AddChild(PaperButton("Back", () => { if (backToTitle) ShowTitle(); else CloseModal(); }));
            v.AddChild(row);
        }
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

    /// <summary>The Park's war in figures, two columns: what was read, what came of it, and who did it.</summary>
    Control WarRecord()
    {
        var st = S.Stats;
        static string D(DateOnly d) => $"{d.Day} {d.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}";
        var left = new System.Collections.Generic.List<string>
        {
            $"{D(S.DateOf(0))} to {D(S.Date)}, {S.Day:N0} days at the Park",
            $"Decrypts delivered: {st.DeliveredTotal:N0}" + (st.LateDecrypts > 0 ? $" ({st.LateDecrypts:N0} late)" : ""),
            $"Keys broken: {st.KeyBreaks.Values.Sum():N0}",
            $"Requests completed: {st.ContractsCompleted:N0} · missed: {st.ContractsFailed:N0}",
            $"ULTRA decisions taken: {st.DilemmasResolved:N0}",
        };
        var nets = st.Delivered.Where(x => x.Value > 0).OrderByDescending(x => x.Value).Take(5).ToList();
        if (nets.Count > 0) left.Add("Most read: " + string.Join(" · ", nets.Select(x => $"{Defs.NetOf(x.Key).Name} {x.Value:N0}")));
        var right = new System.Collections.Generic.List<string>();
        foreach (var (id, outcome) in S.OpOutcomes)
            if (GreatOps.ById(id) is { } op)
                right.Add($"{op.Name}: " + outcome switch { OpOutcome.Success => "a success", OpOutcome.Partial => "went badly", _ => "failed" });
        int peak = S.History.Count > 0 ? Math.Max(S.History.Max(h => h.Staff), S.Staff.Count) : S.Staff.Count;
        right.Add($"Staff: {S.Staff.Count} at the end, {peak} at the most");
        if (st.MoleLeakDays > 0) right.Add(st.MoleCaught ? $"A mole caught after {st.MoleLeakDays} days" : S.MoleState == 3 ? $"A leak stopped after {st.MoleLeakDays} days, its source never known" : $"A leak for {st.MoleLeakDays} days");
        if (S.Agents.Count > 0) right.Add($"Double Cross agents: {S.Agents.Count(a => !a.Blown)} of {S.Agents.Count} kept" + (S.Agents.Any(a => a.Blown) ? $" ({string.Join(", ", S.Agents.Where(a => a.Blown).Select(a => a.Name))} blown)" : ""));
        if (st.DaytonStops > 0) right.Add($"SHARK menus run at Dayton: {st.DaytonStops}");
        if (st.Breakdowns > 0) right.Add($"Machine breakdowns: {st.Breakdowns}" + (st.FitterRepairs > 0 ? $", {st.FitterRepairs} mended by our own fitters" : ""));
        if (st.Delivered.TryGetValue(Net.Jn25, out int jn) && jn > 0)
            right.Add($"JN-25 read: {jn:N0}" + (S.OshimaRead > 0 ? $", with {S.OshimaRead} of Ōshima's reports" : ""));
        if (S.TonnageLost > 0) right.Add($"Shipping lost in the SHARK blackout: {S.TonnageLost:N0} tons");
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 24);
        foreach (var lines in new[] { left, right })
        {
            var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(370, 0) };
            col.AddThemeConstantOverride("separation", 2);
            foreach (var line in lines) col.AddChild(Ink(line, 13));
            grid.AddChild(col);
        }
        return grid;
    }

    void ShowEnd(bool won)
    {
        var v = Paper(820);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        string heading = !won ? "Closed down" : S.ActIndex == Campaign.Acts.Length - 1 ? "VICTORY" : S.ActIndex > Campaign.EnigmaFinale ? "TUNNY" : "SHARK";
        v.AddChild(Ink(heading, 34, Fonts.Heading, won ? UiTheme.PaperInk : UiTheme.Stamp));
        if (!string.IsNullOrEmpty(S.EndReason)) v.AddChild(Ink(S.EndReason, 15));
        v.AddChild(WarRecord());
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
            if (won && S.ActIndex == Campaign.Acts.Length - 1)
                v.AddChild(Ink("The Park was broken up that summer and its machines taken apart. Those who worked here kept the secret: the story was " +
                               "first told in 1974, and many never told their families at all.", 13, Fonts.Body, UiTheme.Stamp));
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
        row.AddChild(PaperButton("Quit", () => Main.QuitGame()));
        v.AddChild(row);
    }
}
