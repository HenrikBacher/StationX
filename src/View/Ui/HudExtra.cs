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
        string.Join(",", S.KeyBoardNets().Select(n => $"{S.CanGarden(n)}{S.LockedDown(n)}")) + $"|{S.CanSecuritySweep() == null}{S.Stats.PapersFound}";

    void IntelPanel()
    {
        _sideBox.AddChild(UiTheme.Heading("Intelligence"));
        _sideBox.AddChild(UiTheme.Label("ULTRA is only useful while the Germans believe their ciphers are safe. Every decrypt that is acted on is a risk. " +
                                        $"Suspicion fades by itself, a few points a day. At {(int)Defs.SuspicionLimit} a service tightens its procedures and its keys go dark for {Defs.LockdownDays + 1} days.", 13, UiTheme.TextDim));

        _sideBox.AddChild(UiTheme.Caps("German suspicion", 12, UiTheme.Accent));
        foreach (var sv in Enum.GetValues<Service>().Where(S.ServiceInPlay))
        {
            double v = S.SuspicionOf(sv);
            bool locked = S.LockdownUntil.TryGetValue(sv, out var until) && S.Day <= until;
            var name = UiTheme.Label(Defs.ServiceNames[sv], 13, UiTheme.Text);
            name.CustomMinimumSize = new Vector2(120, 0);
            var bar = new ProgressBar { MinValue = 0, MaxValue = Defs.SuspicionLimit, Value = v, ShowPercentage = false, CustomMinimumSize = new Vector2(150, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            bar.AddThemeStyleboxOverride("fill", UiTheme.Box(v >= Defs.SuspicionWarn ? Gfx.Bad : v >= 40 ? Gfx.Warn : Gfx.Good, new Color(0, 0, 0, 0), 2, 0));
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var state = UiTheme.Label(locked ? $"locked out, {until - S.Day + 1} d" : $"{(int)v}/{(int)Defs.SuspicionLimit}", 12, locked ? Gfx.Bad : UiTheme.TextDim);
            var row = Row(name, bar, state);
            row.TooltipText = locked ? $"The {Defs.ServiceNames[sv]} have tightened their procedures: their keys can't be broken for {until - S.Day + 1} more day(s)."
                                     : $"{Defs.ServiceNames[sv]} suspicion {(int)v} of {(int)Defs.SuspicionLimit}. Acting on their decrypts raises it; cover stories, direction-finding and a cautious policy keep it down.";
            _sideBox.AddChild(row);
        }

        if (S.ActIndex >= Defs.SecurityFromAct)
        {
            string? why = S.CanSecuritySweep();
            var l = UiTheme.Label(S.Stats.PapersFound > 0 ? $"Papers found left out: {S.Stats.PapersFound}" : "The Park's own security", 13, UiTheme.TextDim);
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (why != null && S.ActIndex >= Defs.SecurityFromAct) l.Text += " · " + why;
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _sideBox.AddChild(Row(l, UiTheme.Button("Security sweep", () => { S.SecuritySweep(); _sideSig = ""; }, why == null,
                tooltip: why ?? $"Desks cleared, safes checked, a word in every ear: Abwehr and SD suspicion −{Defs.SweepRelief:0}. Everyone on duty loses an hour's energy and a little morale. Once every {Defs.SweepCooldownDays} days.")));
        }

        if (S.SluTheatres().Any()) SluCard();
        if (S.DoubleCrossOn) DoubleCrossCard();

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

    /// <summary>MI5's double agents and how far the Abwehr believes them, as the last Abwehr decrypts showed.</summary>
    void DoubleCrossCard()
    {
        _sideBox.AddChild(UiTheme.Caps("Double Cross", 12, UiTheme.Accent));
        _sideBox.AddChild(UiTheme.Label("MI5's double agents, feeding Berlin FORTITUDE's army in Kent. Each Abwehr decrypt shows how one of them is received, " +
                                        "the least certain first; an agent left unread for long may be blown." +
                                        (S.FortitudeBonus > 0 ? $" Adds {S.FortitudeBonus * 100:0}% to D-Day's readiness." : " On D-Day, what Berlin still believes adds to its readiness."), 12, UiTheme.TextDim));
        foreach (var a in S.Agents)
        {
            var name = UiTheme.Label(a.Name, 13, a.Active ? UiTheme.Text : UiTheme.TextDim, Fonts.BodyBold);
            name.CustomMinimumSize = new Vector2(100, 0);
            Control mid;
            if (a.Active)
            {
                var bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.01, Value = a.Trust, ShowPercentage = false, CustomMinimumSize = new Vector2(150, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
                bar.AddThemeStyleboxOverride("fill", UiTheme.Box(a.Trust < Defs.XxBlownBelow * 2 ? Gfx.Bad : a.Trust < 0.5 ? Gfx.Warn : Gfx.Good, new Color(0, 0, 0, 0), 2, 0));
                bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                mid = bar;
            }
            else
            {
                mid = UiTheme.Label(a.Blown ? "blown" : "stood down", 12, a.Blown ? Gfx.Bad : UiTheme.TextDim);
                mid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            }
            var state = UiTheme.Label(a.Active ? (a.Trust >= 0.7 ? "believed" : a.Trust >= 0.4 ? "in doubt" : "in danger") : "", 12, UiTheme.TextDim);
            state.CustomMinimumSize = new Vector2(64, 0);
            var row = Row(name, mid, state);
            row.TooltipText = $"{a.Name}: {a.Who}.";
            _sideBox.AddChild(row);
        }
    }

    void SluCard()
    {
        _sideBox.AddChild(UiTheme.Caps("Special Liaison Units", 12, UiTheme.Accent));
        _sideBox.AddChild(UiTheme.Label("Small units of officers with their own ciphers who hand Hut 3's reports to commanders in the field and burn them after. " +
                                        $"Where there's one, its theatre's decrypts stay fresh {(Defs.SluFresh - 1) * 100:0}% longer, but more people act on them: " +
                                        $"{(Defs.SluSuspicion - 1) * 100:0}% more suspicion each.", 12, UiTheme.TextDim));
        foreach (var d in S.SluTheatres())
        {
            var name = UiTheme.Label(d.Name, 13, UiTheme.Text, Fonts.BodyBold);
            name.CustomMinimumSize = new Vector2(130, 0);
            var serves = UiTheme.Label(string.Join(", ", d.Serves.Select(x => Defs.ServiceNames[x])), 12, UiTheme.TextDim);
            serves.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var id = d.Id;
            var why = S.CantSendSlu(id);
            Control right = S.Slus.Contains(id)
                ? UiTheme.Label($"in {d.Where}", 12, Gfx.Good)
                : UiTheme.Button($"Send one (£{d.Cost})", () => { S.SendSlu(id); _sideSig = ""; }, why == null,
                    tooltip: why ?? $"{d.Blurb} £{d.Cost}, then £{d.Upkeep} a week until the war there is over.");
            var row = Row(name, serves, right);
            row.TooltipText = d.Blurb + (S.Slus.Contains(id) ? $" Upkeep £{d.Upkeep} a week until the war there is over." : "");
            _sideBox.AddChild(row);
        }
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
        if (S.DaytonBombes > 0)
            v.AddChild(UiTheme.Label($"US Navy Bombes at Dayton: {S.DaytonBombes} take four-rotor menus by cable (about {Defs.DaytonCableMinutes / 60} hours more) · " +
                                     $"{S.DaytonJobs} running · {S.Stats.DaytonStops} stops sent back", 12, UiTheme.Text));
        if ((names.Count > 0 || S.DaytonBombes > 0) && !S.Stations.Any(s => s.Type == StationType.OutstationLink))
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
        if (S.ActiveNets.Any(n => Defs.NetOf(n).Family == Family.Enigma) || hist.Any(r => r.KeysBroken > 0))
            Chart("Enigma keys broken per day", hist, r => r.KeysBroken, UiTheme.Accent, "0");
        Chart("Messages in the huts at midnight", hist, r => r.Backlog, Gfx.Warn, "0");
        // The workload trend, beside the backlog: kept for the last month only, and only from when it began.
        var use = S.UseDays.TakeLast(_statsRange).ToList();
        if (use.Count >= 2)
        {
            // Busy shares run low (most desks stand quiet much of the day), so this one is scaled to its tens.
            var busy = use.Select(d => (double)d.Busy).ToArray();
            Chart("Desks and machines busy (Workload, U)", busy, use[0].Day, Gfx.Good, "0'%'", 0, Math.Clamp(Math.Ceiling(busy.Max() * 1.25 / 10) * 10, 20, 100),
                "The share of their working hours all desks and machines spent working a message, day by day. Low means room to spare; near the top, paper queues.");
            Chart("Staff idle while on watch", use.Select(d => (double)d.Idle).ToArray(), use[0].Day, Gfx.Warn, "0'%'", 0, 100,
                "The share of their time on watch everyone spent with nothing to do. High means more people than work: move them to a short watch or let some go.");
        }
        Chart("Funds", hist, r => r.Funds, UiTheme.Text, "£#,0;−£#,0");
        Chart("Reputation in Whitehall", hist, r => r.Reputation, UiTheme.Accent, "0", 0, 100);
        Chart("Staff", hist, r => r.Staff, UiTheme.Text, "0");
        Chart("Average morale", hist, r => r.Morale * 100, Gfx.Good, "0'%'", 0, 100);
        Chart("Highest German suspicion", hist, r => r.Suspicion, Gfx.Bad, "0", 0, Defs.SuspicionLimit);
        _sideBox.AddChild(UiTheme.Label($"Totals: {S.Stats.DeliveredTotal} decrypts · {S.Stats.ContractsCompleted} requests completed, {S.Stats.ContractsFailed} missed · {S.Stats.DilemmasResolved} decisions taken", 12, UiTheme.TextDim));
        var more = new List<string>();
        var st = S.Stats;
        if (st.InspectionsPassed + st.InspectionsFailed > 0) more.Add($"visits from Whitehall: {st.InspectionsPassed} impressed, {st.InspectionsFailed} not");
        if (st.MilestonesMet + st.MilestonesMissed > 0) more.Add($"standing-order days met {st.MilestonesMet} of {st.MilestonesMet + st.MilestonesMissed}");
        if (st.OpsWithheld > 0) more.Add($"{st.OpsWithheld} operations held back to protect the source");
        if (st.Kisses > 0) more.Add($"{st.Kisses} days' naval keys kissed from WERFT");
        if (st.CribsDragged > 0) more.Add($"{st.CribsDragged} cribs placed by hand");
        if (st.BanburiesSlid > 0) more.Add($"{st.BanburiesSlid} Banbury sheets slid by hand");
        if (st.TypexSlips > 0) more.Add($"{st.TypexSlips} Typex runs set up wrongly and done again");
        if (st.LateDecrypts > 0) more.Add($"{st.LateDecrypts} decrypts delivered a day or more late");
        if (more.Count > 0) _sideBox.AddChild(UiTheme.Label(char.ToUpperInvariant(more[0][0]) + string.Join(" · ", more)[1..], 12, UiTheme.TextDim));
    }

    void Chart(string title, List<DayRecord> hist, Func<DayRecord, double> f, Color color, string fmt, double? min = null, double? max = null) =>
        Chart(title, hist.Select(f).ToArray(), hist[0].Day, color, fmt, min, max);

    void Chart(string title, double[] values, int firstDay, Color color, string fmt, double? min = null, double? max = null, string tip = "")
    {
        var v = Card();
        if (tip != "") { v.GetParent<Control>().TooltipText = tip; v.MouseFilter = Control.MouseFilterEnum.Ignore; }
        var name = UiTheme.Label(title, 13, UiTheme.Text, Fonts.BodyBold);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        v.AddChild(Row(name, UiTheme.Label(values[^1].ToString(fmt), 13, color, Fonts.BodyBold)));
        v.AddChild(new LineChart
        {
            Values = values,
            FirstDay = firstDay,
            Fmt = fmt,
            DayLabel = d => S.DateOf(d).ToString("d MMM yyyy"),
            TooltipText = tip,
            Min = min ?? Math.Min(0, values.Min()),
            Max = max ?? Math.Max(1, values.Max() * 1.1),
            Color = color,
            CustomMinimumSize = new Vector2(0, 72),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
    }

    // ------------------------------------------------------------------ hints

    PanelContainer _hint = null!;
    Label _hintText = null!, _hintHead = null!;
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
        var head = _hintHead = Ink("A WORD FROM COMMANDER DENNISTON", 11, Fonts.BodyBold, UiTheme.Stamp, wrap: false);
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

    /// <summary>Every situational tip: id (shown once per id), when it applies, and its text.</summary>
    IEnumerable<(string id, bool when, string text)> Tips()
    {
        yield return ("research", S.Researched.Count == 0 && S.ResearchAvailable.Any(r => !S.Researched.Contains(r) && S.Points >= Defs.Research[r].Cost),
            "Every decrypt delivered earns breakthroughs, and you have enough for a new method. Spend them in Research (T).");
        yield return ("hutopen", S.Grid.Zones.Any(z => z.Available && !z.Unlocked),
            "The Treasury has authorised a new hut. Open it from Huts (G), or click its foundations, and its desks and machines can go in.");
        yield return ("watches", S.WatchesAvailable && S.Staff.Any(s => s.Shift == Shift.Office && !s.Leaving && !s.Arriving(S.Now)),
            "The Park now works three watches round the clock: day 08–16, evening 16–24, night 00–08. Anyone left on long days (07–22) tires and grows unhappy. " +
            "Staff (H) → Balance watches spreads everyone over the three; a person's own panel sets their watch.");
        yield return ("index", S.ActIndex >= Defs.IndexFromAct && !S.Stations.Any(s => s.Type == StationType.Index),
            "Enigma and Tunny decrypts now go through Hut 3's card index between translation and despatch: every unit, officer and place on a card, so each message is read against all we know. " +
            "Until there is a card index (Hut 3, Hut 8 or the Mansion, worked by a clerk) they wait. Put one beside each translation desk so the paper is passed across.");
        yield return ("storesopen", S.SuppliesActive && !S.StoresLow && !S.StandingOrder,
            "Every message now uses dockets and worksheets at registration, and the stores run down. Staff (H) shows what's left: order more before they run out " +
            "(the lorry takes most of a day), or set up a standing order.");
        yield return ("operation", S.CurrentOp != null,
            "A great operation starts on a fixed day, whatever the Park does. Its readiness, under the objectives, counts the act's 'before' objectives, how unsuspecting the Germans still are, " +
            "and a decision a few days before. Hover it for the figures. Keep suspicion low and deliver the objectives in time.");
        yield return ("quota", S.QuotaActive,
            $"Whitehall now judges the Park by what it delivers: it expects {S.IntelQuota} fresh decrypts a day (hover over the reputation bar). Short of it, reputation slips, " +
            "over twice as fast with nothing delivered; meet it and it slips only a little. Air and naval news counts for less after 16 hours. When Workload (U) shows desks and people standing idle, " +
            "the Park can take on more requests: a request on a network whose key is already broken each day costs only Typex and translation time.");
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
        yield return ("heat", S.HotZones() is not null,
            $"Summer in the Bombe bay: with {Defs.HotBombes} or more Bombes running, the hut is a furnace, and the Wrens there tire faster and grow short-tempered. An extractor fan (£{Defs.Station(StationType.Fan).Cost}, under Build) draws the air through.");
        yield return ("worn", S.Stations.Any(s => s.Wears && s.Wear > 0.7 && !s.IsDown(S.Now)),
            "A machine is badly worn (the orange lamp). Service it from its panel while it's idle: an hour now saves five hours of breakdown later.");
        yield return ("stale", S.ActiveNets.Any(n => S.Stale(n, S.Day)),
            "The Germans have changed a habit and our cribs for one network are poor for a few days (see the key panel). Its Bombe runs take longer: give it more Bombe time or take fewer of its requests.");
        yield return ("sixta", S.ActIndex >= Defs.SixtaFromAct && S.Stations.Any(s => s.Type == StationType.Crib) && !S.Stations.Any(s => s.Type == StationType.TrafficAnalysis),
            "Hut 6's traffic analysts read what's outside a message: callsigns, frequencies, who sends to whom. A traffic-analysis desk (Build, B) by the crib desks logs two of each network's messages a day, and that key's cribs and Bombe runs go quicker.");
        yield return ("kisses", S.KissesOpen && !S.ReadWerft,
            "Hut 8's best cribs are kisses: the same text sent twice, once in a cipher we already read. The dockyards and weather stations send their routine reports in WERFT, a dockyard hand cipher, and again in DOLPHIN. On the Enigma keys card, set WERFT to Read: a hand-cipher desk reads it each morning, and that day's naval key gets a perfect crib, a far quicker menu and a shorter Bombe run. Only good for the day it was sent.");
        yield return ("typexslip", S.Stats.TypexSlips > 0,
            "A Typex came out as gibberish: the Wren set a wheel one place out, and the message goes through again. Green or worn-out operators slip more often; experienced, rested Wrens and Typex specialists rarely do.");
        yield return ("staleintel", S.Messages.Values.Any(S.IsStale),
            "Intelligence goes off, and some sooner than others. Air and naval decrypts (tonight's raid, a U-boat's position) earn half the breakthroughs after 16 hours and none after 32; army ones after a day and two; Abwehr, police and TUNNY after two days and four. They still count for their requests. Clear backlogs before they go stale.");
        yield return ("offizier", S.Messages.Values.Any(m => m.Offizier && m.Next == StationType.HandCipher),
            "An Offizier message: under the day's naval key it's enciphered again on an officers-only setting. After the Typex it goes to a hand-cipher desk to be unpicked. If naval traffic is heavy, build a second hand-cipher desk.");
        yield return ("papers", S.Stats.PapersFound > 0 || (S.ActIndex >= Defs.SecurityFromAct && S.Overcrowded > 3),
            "Leaks raise the Abwehr's suspicion: worksheets left out overnight on desks abandoned mid-message, and staff lodged far away talking on the bus. Keep the watches manned and billets found. When the Abwehr and SD bar climbs past half, a security sweep (Intelligence) plugs the leaks, at an hour of everyone's time.");
        yield return ("handover", S.Stats.ColdHandovers >= 3,
            "Desks are being left half-way through a message for hours. Whoever picks one up must read back through the worksheet: within the hour nothing is lost, after six hours half the work is. Keep enough people on every watch to take over the desks; Balance watches (Staff) spreads them out.");
        yield return ("nights", S.LongNights > 0,
            "Some of the night watch have done more than a week of nights without a break, and their morale is draining twice as fast. Tick Rotate weekly under Staff (H) to change the watches round each week, as Hut 6 did; night owls are happy on nights for good.");
        yield return ("teaqueue", S.WaitingForTea >= 2,
            "People due a tea break are standing about because every seat is taken, and they get no rest until one frees up. Build another canteen table (Build, B), best one in each busy hut so nobody walks far for a cup.");
        yield return ("stores", S.StoresLow,
            "The stores are running low: every message needs its dockets and worksheets at registration, and without them the desks work at half speed. Order more under Staff; the lorry takes most of a day. A standing order reorders by itself, at a quarter more.");
        yield return ("pmvisit", S.PmVisit,
            "The Prime Minister is coming. He judges what he sees as any visitor would: in-trays, broken machines, faces, a request about to be missed, the account, " +
            "and the watch standing about. Whatever he finds, his speech on the lawn lifts everyone's spirits; a good showing earns more reputation than any visit from Whitehall.");
        yield return ("inspection", S.InspectionDue && !S.PmVisit,
            "A visitor from Whitehall is coming. They judge what they see: overflowing in-trays, broken machines, glum faces, a request about to be missed, an overdrawn account, " +
            "and how many of the watch stand about with nothing to do (the Treasury counts heads: Workload, U, shows which roles are idle, so hire no more of them). " +
            "A smooth-running Park earns reputation; a shambles loses it, and a quarter of next week's grant.");
        yield return ("priorities", S.KeysQueueing,
            "Keys are queueing for the Bombes. On the Enigma keys card, put the network with the most pressing request First; a network you have no requests for can go on Hold, leaving the Bombes to the others.");
        yield return ("neglect", S.NeglectedMinistries().Any(),
            "A ministry has had nothing from us for days. Leave one waiting ten days and it complains to the Foreign Office every week. Take one of its requests.");
        yield return ("layout", S.Staff.Count >= 18 && S.Stats.DeliveredTotal > 40 && S.Stats.HandOvers < S.Stats.DeliveredTotal / 4,
            "Put each step's desk beside the next one (registration, Typex, translation, despatch): neighbouring desks pass paper across without a runner. And keep aisles clear: people squeezing past each other walk slower.");
        yield return ("bottleneck", S.UseHours >= 6 && S.Utilisation().Top is { Kind: "bottleneck" or "unstaffed" },
            "Paper is piling up at one step of the line while others wait for it. Workload (U) shows how busy each kind of desk, machine and person has been over the last day, and names the bottleneck; while it's open, a ring round every desk shows the same.");
        yield return ("course", S.CourseAdvice() is not null,
            "Some of the staff stand idle while another kind of desk has paper and nobody at it. Anyone can go on a short course (12 hours, £30) to learn " +
            "clerical work, the messengers' round or the Wrens' machines; a linguist and a cryptanalyst can learn each other's trade in three days. " +
            "Pick someone idle in Staff (H) and choose what they learn: they work it a little slower, and only when their own desks have nothing waiting.");
        yield return ("ycover", S.ActIndex >= 1 && S.CoveredNets().Any(n => S.CoverShare(n) < 1),
            $"A network has fewer than {Defs.SetsWanted} receiver sets at its Y-station: its traffic comes in hours late and garbled. In Y-stations (Y), move a set over from a network with no requests, or buy another. The Y Board allots one more each chapter.");
        yield return ("riders", S.ActIndex >= 2 && S.Lines.Count == 0 && S.Funds >= Defs.LineCost * 2,
            "Dispatch riders bring each Y-station's traffic in a bundle every two hours, and decrypts go stale on the road. A teleprinter line (Y-stations, Y) brings it in minutes: lay one from the station with the busiest networks first.");
        yield return ("noteleprinter", S.Lines.Count > 0 && !S.Stations.Any(x => x.Type == StationType.Inbox),
            "A teleprinter line has been laid, but there is no teleprinter in the Park to print it: the riders still bring its traffic. Build a teleprinter (B).");
        yield return ("freqchange", S.Stats.FreqChanges > 0,
            "A network has changed its frequencies and gone quiet until its Y-station finds it again. Spare receiver sets search the band: keep one or two unallocated in Y-stations (Y) and the next search is much quicker.");
        yield return ("df", S.MaxSuspicion > 30 && Enum.GetValues<Service>().Any(sv => S.CanBuyDf(sv) == null),
            "German suspicion is rising. Direction-finding stations (Y-stations, Y) take bearings on the enemy's transmitters, so a cover story can point to the fix: cover choices for that service then draw half the suspicion.");
        yield return ("chiefs", S.ResearchDone() && S.CantReport() == null,
            $"There's nothing new to research for now, and the breakthroughs are piling up. Research (T) → Report to the Chiefs of Staff turns {S.ReportCost} of them into " +
            $"+{Defs.ReportRep:0} reputation, once a fortnight; each report costs more than the last. Keep enough back for the methods the war will bring.");
        yield return ("social", S.CantHoldSocial() == null && S.AverageMorale < Defs.MoraleBaseline - 0.15,
            $"Spirits are low across the Park ({S.AverageMorale:P0}), and unhappy people work slower and in the end leave. Staff (H) → Arrange an evening " +
            $"(£{Defs.SocialCost}): the Recreational Club puts on a concert, a revue or a dance, and everyone's spirits rise. Once every {Defs.SocialEveryDays:0} days.");
        if (S.PostingGap() is var (pz, pr, pw))
            yield return ("posting", true,
                $"{pz.Name} has {Defs.RolePlural(pr)}' desks but no {Defs.RoleWord(pr)} posted there" +
                (S.WatchesAvailable ? $" on the {Defs.Shifts[pw].Name.ToLowerInvariant()}" : "") +
                ". Everyone stays in the building they're posted to: hire one for it (pick the building on the candidate's card), " +
                "or select someone in Staff (H) and change Posted to.");
        yield return ("fitters", S.FittersOpen && !S.Staff.Any(s => s.Role == Role.Fitter) && S.Stats.Breakdowns > 0,
            $"Machines break down, and the outside fitters take hours and charge £{Defs.RepairCost}. RAF fitters of our own (Staff, H: post them to the Bombe hut) " +
            $"mend a breakdown in a third of the time for the parts alone, and service worn machines between jobs. One on each watch where the Bombes are.");
        yield return ("doublecross", S.LeastSure() is { Trust: < 0.35 } && S.Act.Op == "overlord" || S.LeastSure() is { Trust: < 0.3 },
            $"{S.LeastSure()?.Name} is in danger: we haven't seen how the Abwehr takes its reports for days, and an agent left in doubt may be blown. " +
            "Abwehr decrypts (take Abwehr requests, and keep a hand-cipher desk going) show how they're received. Intelligence (I) shows each agent.");
        foreach (var d in S.SluTheatres())
            if (!S.Slus.Contains(d.Id) && S.Funds >= d.Cost + Defs.SluTipReserve)
            {
                yield return ("slu", true,
                    $"There's no Special Liaison Unit in {d.Where} yet ({d.Name}). Send one (Intelligence, I, £{d.Cost}): commanders there get our " +
                    $"{string.Join(" and ", d.Serves.Select(x => Defs.ServiceNames[x]))} decrypts while they still matter, so they stay fresh a fifth longer (and a fifth more suspicion each).");
                break;
            }
        foreach (var cm in GameState.CounterDefs)
            if (S.Counters.Contains(cm.Id) && !S.Researched.Contains(cm.Answer))
            {
                yield return ("counter-" + cm.Id, true,
                    cm.News + $" It costs {Defs.Research[cm.Answer].Cost} breakthroughs (Research, T); until then every day of it is slower.");
                break;
            }
        yield return ("dayton", S.DaytonBombes > 0 && !S.HasStation(StationType.OutstationLink),
            $"Washington's Bombes at Dayton will run our four-rotor menus whenever our own are busy ({S.DaytonBombes} of them now, more each month), " +
            "but the menus go by the outstation teleprinter and the cable. Build an outstation teleprinter (Build).");
        yield return ("japanese", S.JapanOpen && S.JapanWanted && (!S.HasStation(StationType.Japanese) || S.JapaneseReaders < (S.WatchesAvailable ? 3 : 1)),
            "JN-25, the Japanese navy's code, comes in overnight. A hand-cipher desk strips its additive; then it needs a Japanese desk (Build), " +
            $"and only linguists who've been on the Bedford Japanese course can work that: select a linguist, Learns → Japanese ({Defs.JapaneseCourseHours / 24:0} days away, " +
            $"£{Defs.JapaneseCourseCost}). One reader per watch, posted to the Japanese desk's building, keeps the night's traffic moving" +
            (S.Grid.Zone("hut7") is { Available: true } ? "; Hut 7 was built for the Japanese section." : "."));
        yield return ("crossword", S.CantHoldCrossword() == null && S.CrosswordAt < 0,
            $"The Daily Telegraph will run a crossword competition for us. Staff (H) → Telegraph crossword (£{Defs.CrosswordCost}): in {Defs.CrosswordDays} days " +
            $"{Defs.CrosswordRecruits} skilled cryptanalysts join the candidates, crossword fiends with faster, better cribs. Run one when you're short of cryptanalysts.");
        yield return ("allhands", S.CanCallAllHands() == null && S.Contracts.Any(c => c.State == ContractState.Active && c.Due - S.Now < 6 * 60 && c.Quantity - c.Delivered >= 2),
            "A request is about to be missed. In an emergency, Staff (H) → Call all hands brings everyone off watch back in for four hours. It costs overtime and morale, and works once a week.");
    }

    /// <summary>Tip ids used more than once (each would hide the other); for the self-test.</summary>
    public string[] DuplicateTipIds() => Tips().GroupBy(t => t.id).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();

    /// <summary>Tips for later systems, each shown once when its situation first comes up.</summary>
    (string id, string text)? Situation()
    {
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
            return ("accept", "Open Requests (Q) and accept one as soon as it comes in: the Y-station sends nothing until a ministry has asked for it. " +
                              "The first rider takes a while, so build the desks in your objectives (top right, and first under Build) meanwhile.");
        if (!Has(StationType.Registration))
            return ("reg", "Dispatch riders bring the intercepts up the drive, and need a desk to hand them to. Open Build (B) and place a registration desk in the Mansion: every message is logged there first.");
        if (!Has(StationType.HandCipher))
            return ("hand", "Place a hand-cipher desk. Your cryptanalysts work the police ciphers there with pencil and paper.");
        if (!Has(StationType.Translation))
            return ("trans", "Decrypts are in German. A translation desk, worked by a linguist, turns them into English.");
        if (!Has(StationType.Dispatch))
            return ("disp", "Finished translations leave by the despatch box. Without one, nothing reaches Whitehall.");
        if (Count(Role.Clerk) < 2)
            return ("clerks", "Clerks register paper and carry it between desks. Hire another from Staff (H).");
        if (S.ActIndex >= 1 && Count(Role.Messenger) == 0 && S.Candidates.Any(c => c.Role == Role.Messenger))
            return ("messenger", "Hire a messenger from Staff (H). They're cheap and quick, and they carry paper so your clerks can stay at their desks.");
        if (Count(Role.Linguist) == 0)
            return ("ling", "Hire a linguist for the translation desk.");
        if (S.Stations.Any(s => s.Def.Operator != null && s.Input.Count > 5 && s.OperatorId == null))
            return ("idle", "A desk has a pile of paper and nobody to work it: the ! marks it. Hire someone of that role into its building, or pin a person to it from the desk's panel.");
        if (!Has(StationType.Canteen))
            return ("tea", "Desk work tires people, and standing about is no rest. Build a canteen table (B) so they can take a tea break and come back fresh: " +
                           "nobody lasts a whole day without one.");
        if (S.Stats.DeliveredTotal == 0)
            return ("wait", "Good. Each message goes registration → hand-cipher desk → translation → despatch box, and is delivered when it leaves. " +
                            "Let it run: press 3 for top speed, and F to see where paper flows and where it is held up.");
        if (S.Day >= 2 && Rig_Quarter() == 0)
            return ("rotate", "You can turn the view with the comma and full-stop keys (or the ⟲ ⟳ buttons) to see behind tall walls.");
        return null;
    }

    int Rig_Quarter() => _main.Rig.Quarter;

    void RefreshHints()
    {
        var h = Settings.Hints && !ModalOpen ? NextHint() : null;
        if (h is { } hint && _hintsDone.Contains(hint.id)) h = null;
        _hint.Visible = h != null;
        // Centred over the park, but clear of an open side panel and the right-hand column when the screen is narrow.
        float w = _root.Size.X, left = w / 2 - 250, right = w / 2 + 250;
        float minLeft = _side.Visible ? 536 : 136, maxRight = w - 344;
        if (left < minLeft) { left = minLeft; right = Math.Max(left + 300, Math.Min(left + 500, maxRight)); }
        _hint.OffsetLeft = left - w / 2;
        _hint.OffsetRight = right - w / 2;
        if (h is { } x && x.id != _hintId)
        {
            _hintId = x.id;
            _hintText.Text = x.text;
            // Travis took over from Denniston in February 1942: the tips come from whoever signs the briefings.
            _hintHead.Text = S.Act.From.Contains("Travis") ? "A WORD FROM COMMANDER TRAVIS" : "A WORD FROM COMMANDER DENNISTON";
            _hint.OffsetBottom = _hint.OffsetTop;
        }
    }

    // ------------------------------------------------------------------ menus

    /// <summary>Opens a sheet by name, for screenshots.</summary>
    public void ShowNamed(string name)
    {
        switch (name)
        {
            case "title": ShowStart(); break;
            case "pause": ShowPauseMenu(); break;
            case "settings": ShowSettings(); break;
            case "update":
                Updater.DebugOffer("0.28.0", "New\n- Breaks matter: nobody gets through a watch without a cup of tea, and tea goes further in company\n" +
                                             "- Three watches on the historical hours, with an optional weekly rota\n\nFixes\n" +
                                             "- New chapters' recruits arrive at once\n\n### All changes since v0.27.0\n- x");
                Updater.DebugOffer("0.27.1", "Fixes\n- Teleprinter filters only list networks on the air");
                ShowUpdate();
                break;
            case "updating":
                Updater.DebugOffer("0.29.0", "New\n- x");
                Updater.DebugProgress(0.42, "Downloading… 13 of 31 MB", true);
                ShowUpdateProgress(true);
                break;
            case "updatefailed":
                Updater.DebugOffer("0.29.0", "New\n- x");
                Updater.DebugProgress(-1, "Couldn't install here (Access to the path is denied). Download it from the releases page instead.", false);
                ShowUpdateProgress(true);
                break;
            case "save": ShowSaves(true); break;
            case "load": ShowSaves(false); break;
            case "newgame": ShowNewGame(); break;
            case "custom": _ngCustomOn = true; _ngCustom.Patience = 0.6; ShowNewGame(); break;
            case "flash":
                S.DebugFlash();
                if (S.Contracts.LastOrDefault(c => c.Flash && c.State == ContractState.Offered) is { } fc) ShowFlash(fc);
                break;
            case "matapan":
                S.Dilemmas.Clear();
                S.DebugMatapan();
                if (S.Dilemmas.Count > 0) ShowDilemma(S.Dilemmas[0]);
                break;
            case "welchman" or "knox" or "turing-us":
                S.Dilemmas.Clear();
                foreach (var (who, role) in new[] { ("Gordon Welchman", Role.Cryptanalyst), ("Dilly Knox", Role.Cryptanalyst), ("Alan Turing", Role.Cryptanalyst) })
                    if (!S.Staff.Any(x => x.Notable == who))
                    {
                        var p = S.NewPerson(role, 3);
                        p.Name = p.Notable = who;
                        p.Female = false;
                        S.Staff.Add(p);
                    }
                if (S.DebugStory(name)) ShowDilemma(S.Dilemmas[0]);
                break;
            case "meanwhile":
                S.Dilemmas.Clear();
                S.DebugMeanwhile(new DateOnly(1942, 2, 20), new DateOnly(1942, 10, 30));
                if (S.Dilemmas.Count > 0) ShowDilemma(S.Dilemmas[0]);
                break;
            case "flu":
                S.Dilemmas.Clear();
                S.DebugFlu();
                if (S.Dilemmas.Count > 0) ShowDilemma(S.Dilemmas[0]);
                break;
            case "mole":
                S.Dilemmas.Clear();
                if (S.DebugMole()) ShowDilemma(S.Dilemmas[0]);
                break;
            case "artist":
                S.Dilemmas.Clear();
                S.DebugArtist();
                if (S.Dilemmas.Count > 0) ShowDilemma(S.Dilemmas[0]);
                break;
            case "uncles":
                S.Dilemmas.Clear();
                S.DebugUncles();
                if (S.Dilemmas.Count > 0) ShowDilemma(S.Dilemmas[0]);
                break;
            case "slu":
                S.Funds = Math.Max(S.Funds, 2000);
                S.Slus.Clear();
                if (S.SluTheatres().FirstOrDefault() is { } first) S.SendSlu(first.Id);
                break;
            case "backtraffic":
                S.BackTraffic = 24;
                break;
            case "counters":
                // Every countermeasure in force and unanswered, to see the keys card and the tip.
                foreach (var cm in GameState.CounterDefs) { S.Researched.Remove(cm.Answer); S.DebugCounter(cm.Id); }
                break;
            case "brusa":
                S.Dilemmas.Clear();
                S.DebugBrusa();
                if (S.Dilemmas.Count > 0) ShowDilemma(S.Dilemmas[0]);
                break;
            case "dilemma" when S.Dilemmas.Count > 0: ShowDilemma(S.Dilemmas[0]); break;
            case "dilemma": S.RaiseDilemma(S.DebugDilemma()); ShowDilemma(S.Dilemmas[0]); break;
            case "end": ShowEnd(true); break;
            case "warmap": ShowWarMap(); break;
            case "banbury" when S.Stations.FirstOrDefault(s => S.BanburyFor(s.Id) != null) is { } bs: ShowBanbury(bs.Id); break;
            case "crib" when S.Stations.FirstOrDefault(s => S.PuzzleFor(s.Id) != null) is { } cs: ShowCribPuzzle(cs.Id); break;
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
        var v = Paper(500);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        v.AddChild(Ink("Paused", 30, Fonts.Heading));
        v.AddChild(Ink($"{S.Act.ChapterTitle}: {S.Act.Title}\n{S.Date:dddd d MMMM yyyy} · {Defs.Difficulties[S.Difficulty].Name}{(S.Custom != null ? " · custom" : "")}{(S.Mode == GameMode.FreePlay ? " · free play" : "")}", 14));
        foreach (var (label, act) in new (string, Action)[]
                 {
                     ("Resume", CloseModal),
                     ("Save game", () => ShowSaves(true)),
                     ("Load game", () => ShowSaves(false)),
                     ("Settings", ShowSettings),
                     ("New game", ShowNewGame),
                     ("Quit to desktop", () => Main.QuitGame()),
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
                : $"{Saves.SlotName(slot)}: {info.Title}\n{info.Date} · {UiTheme.Money(info.Funds)} · saved {info.SavedAt:d MMM HH:mm}", 13);
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
            // Ink on paper: a faint rule for the track and a solid one up to the setting.
            var track = new StyleBoxLine { Color = UiTheme.PaperInk with { A = 0.25f }, Thickness = 4 };
            var filled = new StyleBoxLine { Color = UiTheme.PaperInk, Thickness = 4 };
            sl.AddThemeStyleboxOverride("slider", track);
            sl.AddThemeStyleboxOverride("grabber_area", filled);
            sl.AddThemeStyleboxOverride("grabber_area_highlight", new StyleBoxLine { Color = UiTheme.Stamp, Thickness = 4 });
            var val = Ink(show(value), 13, Fonts.Typewriter, wrap: false);
            val.CustomMinimumSize = new Vector2(72, 0);
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
            cb.AddThemeIconOverride("unchecked", EmptyBox());
            cb.Toggled += x => { set(x); Settings.Apply(); Settings.Save(); };
            grid.AddChild(cb);
        }
        static string P(float x) => $"{(int)Math.Round(x * 100)}%";
        Slider("Master volume", Settings.MasterVolume, 0, 1, 0.05f, x => Settings.MasterVolume = x, P);
        Slider("Sound effects", Settings.SfxVolume, 0, 1, 0.05f, x => Settings.SfxVolume = x, P);
        Slider("Ambience", Settings.AmbienceVolume, 0, 1, 0.05f, x => Settings.AmbienceVolume = x, P);
        Slider("Music", Settings.MusicVolume, 0, 1, 0.05f, x => Settings.MusicVolume = x, P);
        Slider("Interface size", Settings.UiScale, 0.75f, 1.75f, 0.05f, x => Settings.UiScale = x, P);
        Slider("Graphics", Settings.Graphics, 0, 2, 1, x => Settings.Graphics = (int)x, x => Settings.GraphicsNames[(int)x]);
        Toggle("Scroll at screen edges", Settings.EdgeScroll, x => Settings.EdgeScroll = x);
        Toggle("Hints and tips", Settings.Hints, x => { Settings.Hints = x; _hintsDone.Clear(); });
        Toggle("Film grain and vignette", Settings.FilmGrain, x => Settings.FilmGrain = x);
        Toggle("Fullscreen", Settings.Fullscreen, x => Settings.Fullscreen = x);
        Toggle("Check for updates", Settings.CheckUpdates, x => { Settings.CheckUpdates = x; if (x) Updater.Check(); });
        var back = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        back.AddChild(PaperButton("Done", () => { if (_titleOpen) ShowTitle(); else ShowPauseMenu(); }));
        v.AddChild(back);
    }

    static ImageTexture? _emptyBox;

    /// <summary>An empty tick box, drawn in white so the ink colour tints it (the default is a filled grey square).</summary>
    static ImageTexture EmptyBox()
    {
        if (_emptyBox != null) return _emptyBox;
        var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                if (x is 1 or 2 or 13 or 14 && y is >= 1 and <= 14 || y is 1 or 2 or 13 or 14 && x is >= 1 and <= 14)
                    img.SetPixel(x, y, Colors.White);
        return _emptyBox = ImageTexture.CreateFromImage(img);
    }

    bool _titleOpen;
    Difficulty _ngDifficulty = Difficulty.Normal;
    GameMode _ngMode = GameMode.Campaign;
    int _ngAct;
    bool _ngCustomOn;
    CustomRules _ngCustom = new();

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
            (GameMode.Campaign, "Campaign", "Seven chapters, from the first police ciphers in 1939 through SHARK, Tunny and Colossus to D-Day and VE Day in 1945."),
            (GameMode.FreePlay, "Free play", "No objectives and no closure: requests keep coming, and the war moves on every ten days."),
        }, _ngMode, m => _ngMode = m));
        v.AddChild(Ink(_ngMode == GameMode.Campaign
            ? "The war as Bletchley saw it, in seven chapters: the police ciphers of 1939, the Polish gift, the Battle of Britain, DOLPHIN, SHARK, Tunny and Colossus, and the Second Front to victory in Europe in May 1945."
            : "A sandbox. Pick where to start; everything earlier is already built and researched.", 13, Fonts.Body));

        v.AddChild(Ink("Difficulty", 13, Fonts.BodyBold, UiTheme.Stamp));
        v.AddChild(Choice(Enum.GetValues<Difficulty>().Select(d => (d, Defs.Difficulties[d].Name, Defs.Difficulties[d].Blurb)), _ngDifficulty, d => _ngDifficulty = d));
        v.AddChild(Ink(Defs.Difficulties[_ngDifficulty].Blurb + $" Starting funds £{Defs.Difficulties[_ngDifficulty].StartFunds:N0}.", 13, Fonts.Body));

        var custom = new CheckBox { Text = "Custom rules", ButtonPressed = _ngCustomOn, FocusMode = Control.FocusModeEnum.None };
        custom.AddThemeColorOverride("font_color", UiTheme.PaperInk);
        custom.AddThemeColorOverride("font_pressed_color", UiTheme.PaperInk);
        custom.AddThemeColorOverride("font_hover_color", UiTheme.PaperInk);
        custom.AddThemeColorOverride("font_hover_pressed_color", UiTheme.PaperInk);
        foreach (var style in new[] { "normal", "pressed", "hover", "hover_pressed", "focus" }) custom.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
        custom.Toggled += on => { _ngCustomOn = on; ShowNewGame(); };
        v.AddChild(custom);
        if (_ngCustomOn)
        {
            v.AddChild(Ink("Laid over the difficulty above. A custom game keeps no best score.", 12, Fonts.Body));
            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", 12);
            grid.AddThemeConstantOverride("v_separation", 4);
            void Rule(string label, string tip, (double value, string name)[] opts, Func<double> get, Action<double> set)
            {
                var l = Ink(label, 13, Fonts.Body);
                l.TooltipText = tip;
                l.MouseFilter = Control.MouseFilterEnum.Pass;
                l.CustomMinimumSize = new Vector2(170, 0);
                grid.AddChild(l);
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 6);
                foreach (var (value, name) in opts)
                {
                    var b = PaperButton(name, () => { set(value); ShowNewGame(); });
                    b.CustomMinimumSize = new Vector2(124, 0);
                    if (get() != value)
                    {
                        b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0, 0, 0, 0), UiTheme.PaperInk, 2, 18, 4));
                        b.AddThemeColorOverride("font_color", UiTheme.PaperInk);
                    }
                    row.AddChild(b);
                }
                grid.AddChild(row);
            }
            var c = _ngCustom;
            Rule("Money at the start", "Starting funds.", new[] { (0.6, "Lean"), (1.0, "As set"), (2.0, "Rich") }, () => c.Funds, x => c.Funds = x);
            Rule("Weekly grant", "The Treasury's weekly grant.", new[] { (0.75, "Lean"), (1.0, "As set"), (1.5, "Generous") }, () => c.Grant, x => c.Grant = x);
            Rule("Whitehall's patience", "How much reputation each failure costs.", new[] { (1.5, "Short"), (1.0, "As set"), (0.6, "Long") }, () => c.Patience, x => c.Patience = x);
            Rule("Deadlines", "Days each request allows.", new[] { (-1.0, "A day less"), (0.0, "As set"), (1.0, "A day more") }, () => c.Deadlines, x => c.Deadlines = x);
            Rule("German suspicion", "How fast acting on ULTRA raises suspicion.", new[] { (1.5, "Sharp"), (1.0, "As set"), (0.6, "Slow") }, () => c.Suspicion, x => c.Suspicion = x);
            Rule("Machines", "Wear, breakdowns, raids and crises.", new[] { (1.6, "Fragile"), (1.0, "As set"), (0.5, "Sturdy") }, () => c.Wear, x => c.Wear = x);
            v.AddChild(grid);
        }

        if (_ngMode == GameMode.FreePlay || _ngCustomOn)
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
            _main.NewGame(Random.Shared.Next(100000), showTitle: false, _ngDifficulty, _ngMode, _ngMode == GameMode.FreePlay || _ngCustomOn ? _ngAct : 0,
                          _ngCustomOn ? new CustomRules { Funds = _ngCustom.Funds, Grant = _ngCustom.Grant, Patience = _ngCustom.Patience,
                                                          Deadlines = _ngCustom.Deadlines, Suspicion = _ngCustom.Suspicion, Wear = _ngCustom.Wear } : null);
        }));
        v.AddChild(row);
    }

    // ---------------------------------------------------------------- dilemmas

    /// <summary>Flash requests already put in front of the player (each is shown once, as a telegram).</summary>
    readonly HashSet<int> _flashShown = new();

    /// <summary>
    /// A MOST IMMEDIATE request: the game stops on a telegram so it can't be missed. Accepting puts it first in
    /// every tray; "Later" leaves it in the requests (and pulsing in Needs attention) until the offer lapses.
    /// </summary>
    public void ShowFlash(Contract c)
    {
        _flashShown.Add(c.Id);
        Sound.Play("flash", 0.9f, 0);
        var v = Paper(620);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        v.AddChild(Ink($"{S.Date:d MMMM yyyy}, {(int)S.MinuteOfDay / 60:00}:{(int)S.MinuteOfDay % 60:00}", 13));
        v.AddChild(Ink("MOST IMMEDIATE", 30, Fonts.Heading, UiTheme.Stamp));
        v.AddChild(Ink($"From the {Defs.IssuerNames[c.Issuer]}: {c.Title}", 17, Fonts.BodyBold));
        v.AddChild(Ink(c.Flavour, 15));
        v.AddChild(Ink($"{c.Quantity} messages within {Hours(c.Days * Defs.MinutesPerDay)} · £{c.Reward:N0} · reputation +{c.Rep}. " +
                       $"Answer within {Hours(Math.Max(0, c.OfferExpires - S.Now))}: ignoring it costs {Defs.FlashIgnoredRep:0} reputation, declining {Defs.FlashIgnoredRep / 2:0}.", 13));
        var nd = Defs.NetOf(c.Net);
        if (nd.Family == Family.Enigma && !S.KeyBroken(c.Net, S.Day))
            v.AddChild(Ink($"Today's {nd.Name} key isn't broken yet: the decrypts will wait for it.", 12, Fonts.Body, UiTheme.Stamp));
        int id = c.Id;
        foreach (var (label, act) in new (string, Action)[]
                 {
                     ("Accept, and put it first in every tray", () => { S.Accept(id); S.SetPriority(id, true); }),
                     ($"Decline (reputation −{Defs.FlashIgnoredRep / 2:0})", () => S.Decline(id)),
                     ("Decide later (it waits in Requests)", () => { }),
                 })
        {
            var b = PaperButton(label, () => { CloseModal(); act(); _sideSig = ""; });
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            v.AddChild(b);
        }
    }

    public void ShowDilemma(Dilemma d)
    {
        var v = Paper(660);
        v.AddChild(Letterhead());
        v.AddChild(new HSeparator());
        v.AddChild(Ink($"{S.Date:d MMMM yyyy}, {(int)S.MinuteOfDay / 60:00}:{(int)S.MinuteOfDay % 60:00}", 13));
        string header = d.Tag switch
        {
            "matapan" => "ULTRA · REGIA MARINA",
            "brusa" => "MOST SECRET · WASHINGTON",
            "uncles" => "MOST SECRET · DOWNING STREET",
            "artist" => "MOST SECRET · MI5 · DOUBLE CROSS",
            "mole" => "MOST SECRET · MI5",
            "flu" => "THE MEDICAL OFFICER",
            "meanwhile" => "MEANWHILE · THE NEWS",
            "welchman" or "knox" or "turing-us" => "PERSONAL · THE DIRECTOR",
            _ => d.Service == Service.Security ? "SECURITY" : $"ULTRA · {Defs.ServiceNames[d.Service].ToUpperInvariant()}",
        };
        v.AddChild(Ink(header, 13, Fonts.BodyBold, UiTheme.Stamp));
        v.AddChild(Ink(d.Title, 26, Fonts.Heading));
        v.AddChild(Ink(d.Text, 15));
        if (d.Context.Length > 0)
        {
            v.AddChild(Ink("What we know", 12, Fonts.BodyBold, UiTheme.Stamp));
            v.AddChild(Ink(d.Context, 13));
        }
        // What's at stake on the German side: how close this service is to tightening up, and what we'd lose if it did.
        double now = S.SuspicionOf(d.Service);
        var keys = S.ActiveNets.Where(n => Defs.NetOf(n).Service == d.Service).Select(n => Defs.NetOf(n).Name).ToList();
        string lose = keys.Count == 0 ? "" : $" At {(int)Defs.SuspicionLimit} it tightens its procedures and {(keys.Count == 1 ? keys[0] : string.Join(", ", keys.Take(keys.Count - 1)) + " and " + keys[^1])} can't be broken for {Defs.LockdownDays + 1} days.";
        if (d.Options.Any(o => o.Suspicion != 0))
            v.AddChild(Ink($"{Defs.ServiceNames[d.Service]} suspicion is {(int)now} of {(int)Defs.SuspicionLimit}.{lose}", 12, Fonts.Body,
                now >= Defs.SuspicionWarn ? UiTheme.Stamp : null));
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
            if (o.Suspicion != 0)
            {
                double after = System.Math.Clamp(now + (o.Suspicion > 0 ? o.Suspicion * S.Rules.SuspicionMult : o.Suspicion), 0, Defs.SuspicionLimit);
                fx += $" (to {(int)after}{(after >= Defs.SuspicionLimit ? ": they'd lock us out" : "")})";
            }
            if (fx.Length > 0) box.AddChild(Ink(fx, 12, Fonts.Body, UiTheme.PaperInk with { A = 0.75f }));
            v.AddChild(box);
        }
    }
}

/// <summary>A small line chart of daily figures.</summary>
/// <summary>
/// A day-by-day line with its scale at the left edge, dated at both ends; hovering shows the day and its value.
/// </summary>
public partial class LineChart : Control
{
    public double[] Values = Array.Empty<double>();
    public int FirstDay;
    public double Min, Max = 1;
    public Color Color = Colors.White;
    public string Fmt = "0";
    /// <summary>How a game day is written (a date); "day N" if not set.</summary>
    public Func<int, string>? DayLabel;
    int _hover = -1;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Pass;
        MouseExited += () => { _hover = -1; QueueRedraw(); };
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseMotion m || Values.Length < 2) return;
        int i = Math.Clamp((int)Math.Round(m.Position.X / Math.Max(1, Size.X) * (Values.Length - 1)), 0, Values.Length - 1);
        if (i != _hover) { _hover = i; QueueRedraw(); }
    }

    string Day(int day) => DayLabel?.Invoke(day) ?? $"day {day + 1}";

    public override void _Draw()
    {
        // The plot leaves a strip at the bottom for the dates, so the line never runs through it.
        var full = Size;
        var size = new Vector2(full.X, full.Y - 12);
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.2f));
        for (int k = 1; k < 4; k++)
            DrawLine(new Vector2(0, size.Y * k / 4), new Vector2(size.X, size.Y * k / 4), new Color(1, 1, 1, 0.05f));
        var font = Fonts.Body;
        var dim = new Color(1, 1, 1, 0.4f);
        // The scale: top and bottom of the plot.
        DrawString(font, new Vector2(3, 10), Max.ToString(Fmt), HorizontalAlignment.Left, -1, 9, dim);
        DrawString(font, new Vector2(3, size.Y - 2), Min.ToString(Fmt), HorizontalAlignment.Left, -1, 9, dim);
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
        if (_hover >= 0 && _hover < pts.Length)
        {
            // The day under the mouse: a rule, a dot, and its date and value in the strip below.
            var p = pts[_hover];
            DrawLine(new Vector2(p.X, 0), new Vector2(p.X, size.Y), new Color(1, 1, 1, 0.25f));
            DrawCircle(p, 3.5f, Color);
            string text = $"{Day(FirstDay + _hover)}: {Values[_hover].ToString(Fmt)}";
            float w = font.GetStringSize(text, HorizontalAlignment.Left, -1, 10).X;
            float x = Mathf.Clamp(p.X - w / 2, 0, full.X - w);
            DrawRect(new Rect2(x - 3, size.Y, w + 6, 12), new Color(0.1f, 0.1f, 0.09f, 0.95f));
            DrawString(font, new Vector2(x, full.Y - 1), text, HorizontalAlignment.Left, -1, 10, Color.Lightened(0.3f));
            return;
        }
        DrawString(font, new Vector2(0, full.Y - 1), Day(FirstDay), HorizontalAlignment.Left, -1, 10, dim);
        DrawString(font, new Vector2(0, full.Y - 1), Day(FirstDay + Values.Length - 1), HorizontalAlignment.Right, full.X, 10, dim);
    }
}
