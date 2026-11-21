using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace StationX.Sim;

/// <summary>What a desk or machine was doing.</summary>
public enum DeskUse
{
    /// <summary>Working a message (a machine running).</summary>
    Working,
    /// <summary>Someone at it, or a machine ready, but no paper.</summary>
    Starved,
    /// <summary>Paper waiting (or a message half done) and nobody working it.</summary>
    Unstaffed,
    /// <summary>Paper waiting and someone given the desk, but still on the way to it (often from another building).</summary>
    Coming,
    /// <summary>Broken down, being serviced, or stopped by a power cut.</summary>
    Down,
    /// <summary>Nobody at it and nothing for it.</summary>
    Quiet,
    /// <summary>After hours, before there are watches: nobody is on duty, so it isn't counted against the desk.</summary>
    Closed,
}

/// <summary>What a person was doing.</summary>
public enum PersonUse { Working, Carrying, Walking, Break, Idle, OffWatch, Away }

/// <summary>
/// A rolling record of the last 24 game hours in hourly slots (slot = hour of the day): minutes in each state, and
/// for desks the messages finished. Kept on the station or person, never saved.
/// </summary>
public sealed class UseLog
{
    public const int Slots = 24;
    public readonly int States;
    public readonly float[] Minutes;
    public readonly int[] Done = new int[Slots];
    public int LastProcessed;

    public UseLog(int states)
    {
        States = states;
        Minutes = new float[Slots * states];
    }

    public void Clear(int slot)
    {
        Array.Clear(Minutes, slot * States, States);
        Done[slot] = 0;
    }

    public double In(int state)
    {
        double t = 0;
        for (int h = 0; h < Slots; h++) t += Minutes[h * States + state];
        return t;
    }

    public double In(int slot, int state) => Minutes[slot * States + state];

    public double Total()
    {
        double t = 0;
        foreach (var m in Minutes) t += m;
        return t;
    }

    public double SlotTotal(int slot)
    {
        double t = 0;
        for (int k = 0; k < States; k++) t += Minutes[slot * States + k];
        return t;
    }

    public int DoneTotal() => Done.Sum();

    /// <summary>For a person: minutes on watch (working, carrying, walking, on a break or idle).</summary>
    public double OnWatch()
    {
        double t = 0;
        for (int k = 0; k <= (int)PersonUse.Idle; k++) t += In(k);
        return t;
    }

    /// <summary>For a person: the share of their time on watch in a state.</summary>
    public double OfWatch(PersonUse u)
    {
        double w = OnWatch();
        return w > 0 ? In((int)u) / w : 0;
    }
}

/// <summary>One finished day of the record, kept for the trend lines: percentages, so the save stays small.</summary>
public sealed class UseDay
{
    public int Day { get; set; }
    /// <summary>All desks and machines: share of their open time spent working.</summary>
    public int Busy { get; set; }
    /// <summary>Everyone: share of their time on watch spent idle.</summary>
    public int Idle { get; set; }
    public Dictionary<StationType, int> Steps { get; set; } = new();
    public Dictionary<Role, int> Roles { get; set; } = new();
}

/// <summary>One kind of desk or machine over the last day.</summary>
/// <param name="Peak">The most of them worked at once in any hour of the last day: how many the work has needed.</param>
/// <param name="StuckHours">Hours of the last day in which this step was the bottleneck (busy, paper piling up).</param>
/// <param name="NobodyHours">Hours of the last day in which paper sat at most of them with nobody to work it.</param>
/// <param name="NobodyWatch">The watch in which that was worst, and its hours in it.</param>
public sealed record StepUse(StationType Type, int Count, double[] Share, double Minutes, int Done, int Waiting,
                             double AvgWait, double OldestWait, int? Worst, int Peak = 0, int StuckHours = 0,
                             int NobodyHours = 0, Shift? NobodyWatch = null, int NobodyWatchHours = 0)
{
    /// <summary>The share of the hours it could be worked (not after hours before there are watches) in a state.</summary>
    public double OfOpen(DeskUse u)
    {
        double open = 1 - Share[(int)DeskUse.Closed];
        return open > 1e-6 ? Share[(int)u] / open : 0;
    }

    public double OpenMinutes => Minutes * (1 - Share[(int)DeskUse.Closed]);
}

/// <summary>One role over the last day: shares are of their time on watch.</summary>
/// <param name="Spare">People idle most of their time on watch over the last day: the ones to move or let go.</param>
public sealed record RoleUse(Role Role, int Staff, int OnDuty, double[] Share, double Minutes, int? Pick, int Spare = 0);

/// <summary>
/// One role on one watch over the last day: how many are rostered on it, how many desks they work, how many people
/// the work needed at its busiest hour (desks with paper, plus paper being carried), and how it went.
/// </summary>
public sealed record RoleWatch(Role Role, Shift Shift, int On, int Desks, int Needed, double Nobody, double Idle, double Minutes)
{
    public bool Short => Nobody >= 0.2 || (Needed > On && Desks > 0);
    public bool Spare => On >= 2 && Idle >= 0.5 && On > Needed + 1;
}

/// <summary>One watch's hours over the last day.</summary>
public sealed record WatchUse(Shift Shift, int Rostered, double DeskBusy, double DeskUnstaffed, double PeopleWorking, double PeopleIdle);

/// <summary>A plain-English observation about where the Park is stuck or idle, most serious first.</summary>
public sealed record UseFinding(string Kind, string Text, double Score, StationType? Type = null, Role? Role = null,
                               Shift? Shift = null, int Count = 0);

public sealed class UseReport
{
    public double Hours;
    public List<StepUse> Steps = new();
    public List<RoleUse> Roles = new();
    public List<WatchUse> Watches = new();
    public List<UseFinding> Findings = new();
    /// <summary>Roles by watch, once there are watches.</summary>
    public List<RoleWatch> Staffing = new();
    /// <summary>What has held for most of the last day, not just on the whole: for Needs attention.</summary>
    public List<UseFinding> Lasting = new();
    public UseFinding? Top => Findings.Count > 0 ? Findings[0] : null;
    public RoleWatch? Watch(Role r, Shift s) => Staffing.Find(x => x.Role == r && x.Shift == s);
}

/// <summary>
/// Utilisation: how busy every desk, machine and person has been over the last day. Read-only bookkeeping, done once a
/// tick after everything else; it never feeds back into the simulation and isn't saved.
/// </summary>
public sealed partial class GameState
{
    /// <summary>Off for tests that check the bookkeeping changes nothing.</summary>
    [JsonIgnore] public bool TrackUse { get; set; } = true;
    /// <summary>When the record began (the start of the game, or loading it).</summary>
    [JsonIgnore] public double UseSince { get; private set; } = -1;
    int _useHour = -1;
    readonly HashSet<int> _useAtWork = new();
    /// <summary>Paper waiting for each step at the end of each hour of the last day (slot = hour of day).</summary>
    readonly Dictionary<StationType, int[]> _useWaiting = new();

    /// <summary>
    /// The last <see cref="UseDaysKept"/> finished days of the record, for the trend lines. Saved (it is small); only the
    /// bookkeeping writes it and nothing in the simulation reads it. Older saves simply start with none.
    /// </summary>
    public List<UseDay> UseDays { get; set; } = new();
    public const int UseDaysKept = 30;
    UseReport? _useReport;
    int _useReportAt = int.MinValue;

    /// <summary>Desks and machines in the order paper meets them.</summary>
    public static readonly StationType[] UseOrder =
    {
        StationType.Registration, StationType.TrafficAnalysis, StationType.HandCipher, StationType.Zygalski,
        StationType.Banburismus, StationType.Crib, StationType.Bombe, StationType.Bombe4, StationType.Checking,
        StationType.OutstationLink, StationType.Typex, StationType.Testery, StationType.HeathRobinson,
        StationType.Colossus, StationType.TunnyMachine, StationType.Translation, StationType.Japanese, StationType.Index, StationType.Dispatch,
    };

    public const int DeskStates = 7;

    public static bool Tracked(Station st) => st.Def.Operator != null;

    /// <summary>Game minutes between samples: states last far longer, and it keeps the cost down with 150 people.</summary>
    const double UseEvery = 2;
    double _useAcc;

    void TrackUsage(double dt)
    {
        if (!TrackUse) return;
        _useAcc += dt;
        if (_useAcc < UseEvery - 1e-9) return;
        dt = _useAcc;
        _useAcc = 0;
        double t0 = Now - dt;
        int hour = (int)Math.Floor(t0 / 60);
        if (hour != _useHour)
        {
            if (_useHour < 0) UseSince = t0;
            else
            {
                // The hour just ended: how much paper was left waiting for each step.
                int ended = _useHour % UseLog.Slots;
                foreach (var a in _useWaiting.Values) a[ended] = 0;
                foreach (var (t, w) in WaitingByStep())
                {
                    if (!_useWaiting.TryGetValue(t, out var a)) _useWaiting[t] = a = new int[UseLog.Slots];
                    a[ended] = w.n;
                }
                for (int h = _useHour + 1; h <= hour && h <= _useHour + UseLog.Slots; h++)
                {
                    int s = h % UseLog.Slots;
                    // Midnight: the record holds the day just ended, before its first hour is cleared.
                    if (s == 0 && h * 60 - UseSince >= 20 * 60) RecordUseDay(h / UseLog.Slots - 1);
                    foreach (var st in Stations) st.Use?.Clear(s);
                    foreach (var p in Staff) p.Use?.Clear(s);
                }
            }
            _useHour = hour;
        }
        int slot = hour % UseLog.Slots;
        float m = (float)dt;

        _useAtWork.Clear();
        foreach (var st in Stations)
        {
            if (st.OperatorPresent && st.Current != null && st.OperatorId is int op) _useAtWork.Add(op);
            if (!Tracked(st)) continue;
            if (st.Use == null) { st.Use = new UseLog(DeskStates); st.Use.LastProcessed = st.Processed; }
            st.Use.Minutes[slot * DeskStates + (int)DeskState(st)] += m;
            if (st.Processed != st.Use.LastProcessed)
            {
                st.Use.Done[slot] += Math.Max(0, st.Processed - st.Use.LastProcessed);
                st.Use.LastProcessed = st.Processed;
            }
        }
        foreach (var p in Staff)
        {
            if (PersonState(p) is not PersonUse u) continue;
            p.Use ??= new UseLog(7);
            p.Use.Minutes[slot * 7 + (int)u] += m;
        }
    }

    /// <summary>What a desk or machine is doing right now.</summary>
    public DeskUse DeskState(Station st)
    {
        if (st.IsDown(Now) || Unpowered(st)) return DeskUse.Down;
        if (st.Working) return DeskUse.Working;
        if (!WatchesAvailable && !AllHands && !Defs.Shifts[Shift.Office].OnDuty(MinuteOfDay)) return DeskUse.Closed;
        if (st.Current != null || (st.Input.Count > 0 && !st.Obsolete))
            return st.OperatorId != null && !st.OperatorPresent ? DeskUse.Coming : DeskUse.Unstaffed;
        return st.OperatorPresent || st.IsMachine ? DeskUse.Starved : DeskUse.Quiet;
    }

    /// <summary>What a person is doing right now; null for a recruit still on the way.</summary>
    public PersonUse? PersonState(Staff p)
    {
        if (p.Arriving(Now)) return null;
        if (p.IsSick(Now)) return PersonUse.Away;
        if (p.Away) return PersonUse.OffWatch;
        return p.Task switch
        {
            { Kind: TaskKind.Operate } => _useAtWork.Contains(p.Id) || AtWork(p) ? PersonUse.Working
                                        : p.Path.Count > 0 ? PersonUse.Walking : PersonUse.Idle,
            { Kind: TaskKind.Carry } => PersonUse.Carrying,
            { Kind: TaskKind.Rest, Loafing: true } => PersonUse.Idle,
            { Kind: TaskKind.Rest } => PersonUse.Break,
            { Kind: TaskKind.Leave } => PersonUse.Walking,
            { Kind: TaskKind.Repair } t => p.Path.Count > 0 ? PersonUse.Walking : PersonUse.Working,
            _ => p.HeadOf != null ? PersonUse.Working : PersonUse.Idle,
        };
    }

    // Outside the tick (the view asking about one person) the set of people at work isn't filled in.
    bool AtWork(Staff p) => _useAtWork.Count == 0 && p.Task is { } t && StationById(t.Dest) is { OperatorPresent: true, Current: not null } st && st.OperatorId == p.Id;

    /// <summary>The hours the record covers, up to a day.</summary>
    public double UseHours => UseSince < 0 ? 0 : Math.Min(UseLog.Slots, (Now - UseSince) / 60);

    /// <summary>The slot of the hour now under way (the newest in the record).</summary>
    public int UseSlotNow => (int)Math.Floor(Now / 60) % UseLog.Slots;

    /// <summary>A role in running text, plural: "clerks", but "Wrens" (members of the WRNS).</summary>
    public static string PluralWord(Role r) => Defs.RolePlural(r);

    public static string Plural(StationType t) => t switch
    {
        StationType.Dispatch => "Despatch boxes",
        StationType.Index => "Card indexes",
        StationType.TrafficAnalysis => "Traffic-analysis desks",
        StationType.Colossus => "Colossi",
        _ => Defs.Station(t).Name + "s",
    };

    /// <summary>The utilisation report for the panel, the overlay and tips; worked out at most once a game minute.</summary>
    /// <summary>
    /// A role standing idle and another whose desks go unstaffed, with a course from the one to the other: the Workload
    /// panel's advice to cross-train. Null when there's none (or too little record yet).
    /// </summary>
    public (Role from, Role to)? CourseAdvice()
    {
        if (UseHours < 6) return null;
        var fs = Utilisation().Findings;
        foreach (var idle in fs)
        {
            if (idle.Kind != "idle" || idle.Role is not Role from) continue;
            foreach (var f in fs)
                if (f.Kind == "unstaffed" && f.Role is Role to && Defs.Course(from, to) != null) return (from, to);
        }
        return null;
    }

    public UseReport Utilisation()
    {
        int at = (int)Now;
        if (_useReport != null && _useReportAt == at) return _useReport;
        _useReportAt = at;
        return _useReport = BuildUseReport();
    }

    /// <summary>Paper waiting for each step: in trays, out trays and runners' hands, not garbled copies sent back.</summary>
    Dictionary<StationType, (int n, double sum, double oldest)> WaitingByStep()
    {
        var waiting = new Dictionary<StationType, (int n, double sum, double oldest)>();
        foreach (var msg in Messages.Values)
        {
            if (msg.Next is not StationType nx || msg.Loc is Loc.Processing or Loc.Done or Loc.Remote || msg.HoldUntil > Now) continue;
            var w = waiting.GetValueOrDefault(nx);
            double age = Now - msg.Since;
            waiting[nx] = (w.n + 1, w.sum + age, Math.Max(w.oldest, age));
        }
        return waiting;
    }

    static readonly Shift[] WatchOrder = { Shift.Day, Shift.Evening, Shift.Night };

    /// <summary>The watch an hour of the day belongs to.</summary>
    public static Shift WatchAt(int hour) => hour < 8 ? Shift.Night : hour < 16 ? Shift.Day : Shift.Evening;

    /// <summary>Desks with paper (worked or not) in an hour, in desks: 1 is one desk kept in work all hour.</summary>
    static double Demand(UseLog log, int slot)
    {
        double total = log.SlotTotal(slot);
        return total > 0 ? (log.In(slot, (int)DeskUse.Working) + log.In(slot, (int)DeskUse.Unstaffed) + log.In(slot, (int)DeskUse.Coming)) / total : 0;
    }

    /// <summary>A demand in desks or people as a whole number: 1.1 is one most of the time, 1.3 needs a second.</summary>
    static int Whole(double x) => (int)Math.Ceiling(Math.Max(0, x - 0.2));

    /// <summary>Writes the day just ended into the trend, from the 24 hours of the record.</summary>
    void RecordUseDay(int day)
    {
        var rec = new UseDay { Day = day };
        double allWork = 0, allOpen = 0;
        foreach (var t in UseOrder)
        {
            double work = 0, open = 0;
            foreach (var st in Stations)
            {
                if (st.Type != t || st.Use is not { } log || st.Obsolete) continue;
                work += log.In((int)DeskUse.Working);
                open += log.Total() - log.In((int)DeskUse.Closed);
            }
            if (open < 60) continue;
            rec.Steps[t] = (int)Math.Round(work / open * 100);
            allWork += work; allOpen += open;
        }
        double allIdle = 0, allDuty = 0;
        foreach (var role in Enum.GetValues<Role>())
        {
            double idle = 0, duty = 0;
            foreach (var p in Staff)
            {
                if (p.Role != role || p.Use is not { } log) continue;
                idle += log.In((int)PersonUse.Idle);
                duty += log.OnWatch();
            }
            if (duty < 60) continue;
            rec.Roles[role] = (int)Math.Round(idle / duty * 100);
            allIdle += idle; allDuty += duty;
        }
        rec.Busy = allOpen > 0 ? (int)Math.Round(allWork / allOpen * 100) : 0;
        rec.Idle = allDuty > 0 ? (int)Math.Round(allIdle / allDuty * 100) : 0;
        UseDays.Add(rec);
        if (UseDays.Count > UseDaysKept) UseDays.RemoveAt(0);
    }

    UseReport BuildUseReport()
    {
        var r = new UseReport { Hours = UseHours };

        var waiting = WaitingByStep();
        int nowSlot = UseSlotNow;

        foreach (var t in UseOrder)
        {
            var list = Stations.Where(s => s.Type == t && s.Use != null && !s.Obsolete).ToList();
            if (list.Count == 0) continue;
            var share = new double[DeskStates];
            double total = 0;
            int done = 0;
            foreach (var s in list)
            {
                for (int k = 0; k < DeskStates; k++) share[k] += s.Use!.In(k);
                total += s.Use!.Total();
                done += s.Use.DoneTotal();
            }
            if (total > 0) for (int k = 0; k < DeskStates; k++) share[k] /= total;
            var w = waiting.GetValueOrDefault(t);
            // The one to look at: paper with nobody on it, else the fullest tray, else the busiest.
            var worst = list.OrderByDescending(s => DeskState(s) == DeskUse.Unstaffed ? 1 : 0)
                            .ThenByDescending(s => s.Input.Count).ThenByDescending(s => s.Use!.In((int)DeskUse.Working)).First();
            // Hour by hour: how many were worked at once, and the hours stuck or with paper and nobody at it.
            double peak = 0;
            int stuck = 0, nobody = 0;
            var byWatch = new Dictionary<Shift, int>();
            var waitLog = _useWaiting.GetValueOrDefault(t);
            for (int h = 0; h < UseLog.Slots; h++)
            {
                double demand = 0, work = 0, unst = 0, open = 0;
                foreach (var s in list)
                {
                    double slotTotal = s.Use!.SlotTotal(h);
                    if (slotTotal > 0) demand += s.Use.In(h, (int)DeskUse.Working) / slotTotal;
                    work += s.Use!.In(h, (int)DeskUse.Working);
                    unst += s.Use.In(h, (int)DeskUse.Unstaffed);
                    open += s.Use.SlotTotal(h) - s.Use.In(h, (int)DeskUse.Closed);
                }
                peak = Math.Max(peak, demand);
                // Only whole hours count towards "most of the day"; the hour under way is still running.
                if (h == nowSlot || open < 30 * list.Count) continue;
                if (work / open >= 0.75 && (waitLog?[h] ?? 0) >= list.Count + 2) stuck++;
                if (unst / open >= 0.5)
                {
                    nobody++;
                    var sh = WatchAt(h);
                    byWatch[sh] = byWatch.GetValueOrDefault(sh) + 1;
                }
            }
            var worstWatch = WatchesAvailable && byWatch.Count > 0 ? byWatch.MaxBy(kv => kv.Value) : default;
            r.Steps.Add(new StepUse(t, list.Count, share, total, done, w.n, w.n > 0 ? w.sum / w.n : 0, w.oldest, worst.Id,
                Math.Min(list.Count, Whole(peak)), stuck, nobody, worstWatch.Value > 0 ? worstWatch.Key : null, worstWatch.Value));
        }

        foreach (var role in Enum.GetValues<Role>())
        {
            var people = Staff.Where(p => p.Role == role && !p.Arriving(Now)).ToList();
            if (people.Count == 0) continue;
            var share = new double[7];
            double onDuty = 0;
            foreach (var p in people)
            {
                if (p.Use == null) continue;
                for (int k = 0; k <= (int)PersonUse.Idle; k++) { share[k] += p.Use.In(k); onDuty += p.Use.In(k); }
            }
            if (onDuty > 0) for (int k = 0; k < share.Length; k++) share[k] /= onDuty;
            // Clicking the row picks someone standing about if anyone is, else someone at work.
            var pick = people.Where(p => !p.Away).OrderBy(p => PersonState(p) switch { PersonUse.Idle => 0, PersonUse.Working => 1, _ => 2 }).FirstOrDefault();
            r.Roles.Add(new RoleUse(role, people.Count, people.Count(p => !p.Away), share, onDuty, pick?.Id, people.Count(SpareHand)));
        }

        if (WatchesAvailable)
            foreach (var sh in new[] { Shift.Day, Shift.Evening, Shift.Night })
            {
                var def = Defs.Shifts[sh];
                double dw = 0, du = 0, dt = 0, pw = 0, pi = 0, pt = 0;
                for (int h = def.StartHour; h < def.EndHour; h++)
                {
                    int slot = h % UseLog.Slots;
                    foreach (var s in Stations)
                    {
                        if (s.Use == null) continue;
                        dw += s.Use.In(slot, (int)DeskUse.Working);
                        du += s.Use.In(slot, (int)DeskUse.Unstaffed);
                        dt += s.Use.SlotTotal(slot) - s.Use.In(slot, (int)DeskUse.Down) - s.Use.In(slot, (int)DeskUse.Closed);
                    }
                    foreach (var p in Staff)
                    {
                        if (p.Use == null) continue;
                        double work = p.Use.In(slot, (int)PersonUse.Working) + p.Use.In(slot, (int)PersonUse.Carrying);
                        double idle = p.Use.In(slot, (int)PersonUse.Idle);
                        double duty = work + idle + p.Use.In(slot, (int)PersonUse.Walking) + p.Use.In(slot, (int)PersonUse.Break);
                        pw += work; pi += idle; pt += duty;
                    }
                }
                r.Watches.Add(new WatchUse(sh, Staff.Count(p => p.Shift == sh && !p.Arriving(Now)),
                    dt > 0 ? dw / dt : 0, dt > 0 ? du / dt : 0, pt > 0 ? pw / pt : 0, pt > 0 ? pi / pt : 0));
            }

        if (WatchesAvailable) r.Staffing = RoleWatches();
        r.Findings = UseFindings(r);
        r.Lasting = LastingFindings(r);
        return r;
    }

    /// <summary>Idle most of the time they were on watch over the last day (at least four hours of it).</summary>
    public bool SpareHand(Staff p) =>
        p.Use is { } log && !p.Leaving && log.OnWatch() >= 240 && log.OfWatch(PersonUse.Idle) >= 0.6;

    /// <summary>
    /// Who of a role could be let go without leaving a watch short: on each watch with hands to spare, the idlest of the
    /// people rostered on it, keeping what its busiest hour needed and one more. Heads of huts and the famous names stay.
    /// Empty before the watches, or with less than most of a day on record.
    /// </summary>
    public List<Staff> Expendable(Role role)
    {
        var list = new List<Staff>();
        var use = Utilisation();
        if (use.Hours < 8) return list;
        foreach (var w in use.Staffing)
        {
            if (w.Role != role || !w.Spare) continue;
            // Counted now, not from the report (cached per game minute): those already let go are gone.
            var sh = w.Shift;
            int on = Staff.Count(p => p.Role == role && OnWatch(p, sh) && !p.Leaving && !p.Arriving(Now) && !p.IsSick(Now));
            int extra = on - Math.Max(w.Needed, 1) - 1;
            if (extra <= 0) continue;
            list.AddRange(Staff.Where(p => p.Role == role && p.Shift == w.Shift && SpareHand(p) && p.HeadOf == null && p.Notable == null && !p.IsSick(Now))
                               .OrderByDescending(p => p.Use!.OfWatch(PersonUse.Idle)).Take(extra));
        }
        return list;
    }

    /// <summary>Dismisses everyone <see cref="Expendable"/> names for a role; returns how many went.</summary>
    public int LetGo(Role role)
    {
        var go = Expendable(role);
        foreach (var p in go) Fire(p.Id);
        if (go.Count > 0)
            Emit(EventKind.Info, $"{go.Count} {(go.Count == 1 ? Defs.RoleWord(role) : PluralWord(role))} who stood idle most of their watch " +
                                 $"have been thanked and let go, saving £{go.Sum(p => p.Salary)} a week.");
        return go.Count;
    }

    /// <summary>Who covers a watch: its own people, and those on long days (07–22) for the day and evening.</summary>
    static bool OnWatch(Staff p, Shift sh) => p.Shift == sh || (p.Shift == Shift.Office && sh != Shift.Night);

    List<RoleWatch> RoleWatches()
    {
        var list = new List<RoleWatch>();
        foreach (var role in Enum.GetValues<Role>())
        {
            var desks = Stations.Where(s => s.Def.Operator == role && !s.Obsolete && Tracked(s)).ToList();
            var people = Staff.Where(p => p.Role == role && !p.Leaving && !p.Arriving(Now)).ToList();
            if (people.Count == 0 && desks.Count == 0) continue;
            foreach (var sh in WatchOrder)
            {
                var def = Defs.Shifts[sh];
                var mine = people.Where(p => OnWatch(p, sh)).ToList();
                double peak = 0, unst = 0, open = 0, idle = 0, duty = 0;
                for (int h = def.StartHour; h < def.EndHour; h++)
                {
                    int slot = h % UseLog.Slots;
                    double need = 0;
                    foreach (var s in desks)
                    {
                        if (s.Use is not { } log) continue;
                        need += Demand(log, slot);
                        unst += log.In(slot, (int)DeskUse.Unstaffed);
                        open += log.SlotTotal(slot) - log.In(slot, (int)DeskUse.Closed);
                    }
                    foreach (var p in mine)
                    {
                        if (p.Use is not { } log) continue;
                        double total = log.SlotTotal(slot);
                        if (total > 0) need += log.In(slot, (int)PersonUse.Carrying) / total;
                        idle += log.In(slot, (int)PersonUse.Idle);
                        for (int k = 0; k <= (int)PersonUse.Idle; k++) duty += log.In(slot, k);
                    }
                    peak = Math.Max(peak, need);
                }
                list.Add(new RoleWatch(role, sh, mine.Count(p => !p.IsSick(Now)), desks.Count, Whole(peak),
                    open > 0 ? unst / open : 0, duty > 0 ? idle / duty : 0, duty));
            }
        }
        return list;
    }

    /// <summary>
    /// What has held for most of the last day rather than on the whole: a step stuck for twelve hours or more, paper
    /// with nobody at it for most of a watch, a role standing about. For Needs attention, so each says what to do.
    /// </summary>
    List<UseFinding> LastingFindings(UseReport r)
    {
        var list = new List<UseFinding>();
        if (r.Hours < 12) return list;
        foreach (var s in r.Steps)
        {
            var def = Defs.Station(s.Type);
            string name = Plural(s.Type);
            if (s.StuckHours >= 12)
                list.Add(new UseFinding("stuck", $"{name} the bottleneck for {s.StuckHours} of the last 24 h: build another", 10 + s.StuckHours / 24.0, s.Type, Count: s.StuckHours));
            else if (def.Operator is Role role && s.Waiting > 0)
            {
                string who = PluralWord(role);
                if (s.NobodyWatch is Shift sh && s.NobodyWatchHours >= 5)
                    list.Add(new UseFinding("nobody", $"{name}: paper and nobody at them most of the {Defs.Shifts[sh].Name.ToLowerInvariant()}: put more {who} on it",
                        8 + s.NobodyWatchHours / 8.0, s.Type, role, sh, s.NobodyWatchHours));
                else if (!WatchesAvailable && s.NobodyHours >= 8)
                    list.Add(new UseFinding("nobody", $"{name}: paper and nobody at them {s.NobodyHours} h of the last day: hire {who}",
                        8 + s.NobodyHours / 24.0, s.Type, role, Count: s.NobodyHours));
            }
        }
        // One line for everyone standing about, the roles with most of them first (Role: the one with the most).
        var spare = r.Roles.Where(ro => ro.Spare >= 2).OrderByDescending(ro => ro.Spare).ToList();
        int spareTotal = spare.Sum(ro => ro.Spare);
        if (spareTotal >= 3)
            list.Add(new UseFinding("spare", "Idle most of their watch: " + string.Join(", ", spare.Select(ro => $"{ro.Spare} {PluralWord(ro.Role)}")) +
                ": move them or let some go", 2 + spareTotal / 10.0, Role: spare[0].Role, Count: spareTotal));
        return list.OrderByDescending(f => f.Score).ToList();
    }

    List<UseFinding> UseFindings(UseReport r)
    {
        var list = new List<UseFinding>();
        if (r.Hours < 3) return list;
        static string P(double v) => $"{Math.Round(v * 100):0}%";
        static string H(double minutes) => minutes >= 90 ? $"{minutes / 60:0} h" : $"{minutes:0} min";
        foreach (var s in r.Steps)
        {
            if (s.OpenMinutes < 120) continue;
            double busy = s.OfOpen(DeskUse.Working), unstaffed = s.OfOpen(DeskUse.Unstaffed), down = s.OfOpen(DeskUse.Down);
            var def = Defs.Station(s.Type);
            string name = Plural(s.Type);
            bool machine = s.Type is StationType.Bombe or StationType.Bombe4 or StationType.Colossus or StationType.HeathRobinson;
            if (busy >= 0.75 && s.Waiting >= s.Count + 2)
                list.Add(new UseFinding("bottleneck",
                    $"{name} are the bottleneck: {P(busy)} busy over the last day, {s.Waiting} waiting (the oldest {H(s.OldestWait)}). " +
                    (machine ? $"Build another {def.Name.ToLowerInvariant()}." : $"Build another {def.Name.ToLowerInvariant()} and staff it."),
                    10 + busy * s.Waiting / s.Count, s.Type));
            else if (unstaffed >= 0.2 && s.Waiting >= 1 && def.Operator is Role role)
            {
                string when = WorstWatch(s.Type) is Shift sh ? $", worst on the {Defs.Shifts[sh].Name.ToLowerInvariant()}" : "";
                list.Add(new UseFinding("unstaffed",
                    $"{name} had paper but nobody to work them {P(unstaffed)} of the time{(WatchesAvailable ? "" : " in working hours")}{when}. Hire {PluralWord(role)}" +
                    (WatchesAvailable ? " or move some to that watch." : "."),
                    8 + unstaffed * 10 + s.Waiting / (double)s.Count, s.Type, role));
            }
            double coming = s.OfOpen(DeskUse.Coming);
            if (coming >= 0.2 && def.Operator is Role far && StationById(s.Worst ?? -1) is { } w && Grid.ZoneAt(w.X, w.Y) is { } zone)
                list.Add(new UseFinding("far",
                    $"{name} waited {P(coming)} of the time for someone walking over to them. Post some {PluralWord(far)} to {zone.Name} (pick one in Staff, then Posted to) so they wait close by.",
                    5 + coming * 10, s.Type, far));
            if (down >= 0.15)
                list.Add(new UseFinding("down", $"{name} were out of action {P(down)} of the last day. Service them before they wear out.", 6 + down * 10, s.Type));
        }
        foreach (var ro in r.Roles)
        {
            if (ro.Minutes < 240 || ro.Staff < 2 || ro.Role == Role.Fitter) continue;   // fitters wait on call
            double idle = ro.Share[(int)PersonUse.Idle];
            if (idle >= 0.5)
            {
                // Where another role's desks go unstaffed and there's a course for it, say so.
                Role? learn = null;
                foreach (var f in list)
                    if (f.Kind == "unstaffed" && f.Role is Role short_ && Defs.Course(ro.Role, short_) != null) { learn = short_; break; }
                list.Add(new UseFinding("idle",
                    $"{Defs.Roles[ro.Role].Plural} stood idle {P(idle)} of their time on watch: there's more of them than work for them." +
                    (learn is Role l ? $" Send some on a course to work as {PluralWord(l)} too (Staff, pick one, Learns)." : ""),
                    2 + idle * 4, Role: ro.Role));
            }
        }
        // Capacity to sell: short of Whitehall's quota while the desks stand mostly idle and nothing is stuck.
        if (QuotaActive && IntelRate < IntelQuota && !list.Any(f => f.Kind == "bottleneck"))
        {
            double open = r.Steps.Sum(s => s.OpenMinutes);
            double busy = open > 0 ? r.Steps.Sum(s => s.OfOpen(DeskUse.Working) * s.OpenMinutes) / open : 0;
            if (open > 0 && busy < 0.5)
                list.Add(new UseFinding("spare",
                    $"We delivered {IntelRate:0} fresh decrypts a day against Whitehall's {IntelQuota}, and the desks were busy only {P(busy)} of the time: " +
                    "the Park can take on more requests (R).", 6.5));
        }
        return list.OrderByDescending(f => f.Score).ToList();
    }

    /// <summary>The watch in which a kind of desk most often had paper and nobody at it.</summary>
    Shift? WorstWatch(StationType t)
    {
        if (!WatchesAvailable) return null;
        Shift? worst = null;
        double most = 0;
        foreach (var sh in new[] { Shift.Day, Shift.Evening, Shift.Night })
        {
            var def = Defs.Shifts[sh];
            double u = 0, tot = 0;
            foreach (var s in Stations)
            {
                if (s.Type != t || s.Use == null) continue;
                for (int h = def.StartHour; h < def.EndHour; h++)
                {
                    u += s.Use.In(h % UseLog.Slots, (int)DeskUse.Unstaffed);
                    tot += s.Use.SlotTotal(h % UseLog.Slots);
                }
            }
            if (tot > 0 && u / tot > most) { most = u / tot; worst = sh; }
        }
        return most >= 0.25 ? worst : null;
    }
}
