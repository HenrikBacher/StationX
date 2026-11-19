using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

public enum GameStatus { Playing, Won, Lost }

public sealed partial class GameState
{
    public int Seed { get; set; }
    public Rng Rng { get; set; } = new();
    public double Now { get; set; }
    public int Funds { get; set; } = 1500;
    public Difficulty Difficulty { get; set; } = Difficulty.Normal;
    public GameMode Mode { get; set; } = GameMode.Campaign;
    public Policy Policy { get; set; } = Policy.Standard;
    public Dictionary<Service, double> Suspicion { get; set; } = new();
    public Dictionary<Service, int> LockdownUntil { get; set; } = new();
    public HashSet<Service> SuspicionWarned { get; set; } = new();
    public List<Dilemma> Dilemmas { get; set; } = new();
    public double LastDilemmaAt { get; set; } = -1e9;
    public int OutstationBombes { get; set; }
    public List<RemoteJob> RemoteJobs { get; set; } = new();
    public Dictionary<Net, int> GardenUntil { get; set; } = new();
    /// <summary>Days on which a network's usual cribs have dried up.</summary>
    public Dictionary<Net, int> StaleUntil { get; set; } = new();
    /// <summary>When each ministry last had a request completed.</summary>
    public Dictionary<Issuer, double> LastServed { get; set; } = new();
    /// <summary>Tips the player has dismissed in this campaign.</summary>
    public HashSet<string> TipsDismissed { get; set; } = new();
    public List<DayRecord> History { get; set; } = new();
    /// <summary>Recent message movements between stations, "from>to" → messages, for the flow overlay.</summary>
    public SortedDictionary<string, double> Flows { get; set; } = new();
    public int DeliveredToday { get; set; }
    public int KeysToday { get; set; }
    public double Accum { get; set; }
    /// <summary>After SHARK falls the player may carry on into the Tunny chapter.</summary>
    public bool CanContinue { get; set; }
    public double Reputation { get; set; } = Defs.StartReputation;
    public int Points { get; set; }
    public GameStatus Status { get; set; }
    public string? EndReason { get; set; }

    public Grid Grid { get; set; } = Grid.CreateBletchley();
    public List<Station> Stations { get; set; } = new();
    public List<Staff> Staff { get; set; } = new();
    public List<Staff> Candidates { get; set; } = new();
    public SortedDictionary<int, Message> Messages { get; set; } = new();
    public List<Contract> Contracts { get; set; } = new();
    public HashSet<ResearchId> Researched { get; set; } = new();
    public HashSet<ResearchId> ResearchAvailable { get; set; } = new();
    public Dictionary<string, KeyStatus> Keys { get; set; } = new();
    public Dictionary<Net, int> FreeKeysUntil { get; set; } = new();
    public HashSet<Net> ActiveNets { get; set; } = new();
    public bool IndicatorChanged { get; set; }
    public bool SharkBlackout { get; set; }
    public double TonnageLost { get; set; }
    public Weather Weather { get; set; } = Weather.Fine;
    /// <summary>Lodgings found for staff in the town and villages. 0 in saves from before billets existed.</summary>
    public int Billets { get; set; }

    public int ActIndex { get; set; }
    public int ActStartDay { get; set; }
    public DateOnly ActDate { get; set; }
    public bool ActCompletePending { get; set; }
    public Stats Stats { get; set; } = new();
    public Stats ActBaseline { get; set; } = new();
    public int NextId { get; set; } = 1;
    public double NextOfferAt { get; set; }
    public double NextFlashAt { get; set; }
    public double NextScheduleAt { get; set; }

    /// <summary>Events for the UI. The view drains this list.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<GameEvent> Events { get; } = new();

    public int Day => (int)Math.Floor(Now / Defs.MinutesPerDay);
    public double MinuteOfDay => Now - Day * Defs.MinutesPerDay;
    public DateOnly Date => ActDate.AddDays(Day - ActStartDay);
    /// <summary>
    /// The calendar date of a game day. The calendar jumps forward between acts, so a day from an earlier act is dated
    /// from that act's start, not the current one's.
    /// </summary>
    public DateOnly DateOf(int day)
    {
        if (day >= ActStartDay || ActStarts.Count == 0) return ActDate.AddDays(day - ActStartDay);
        int best = int.MinValue;
        foreach (var k in ActStarts.Keys) if (k <= day && k > best) best = k;
        return best == int.MinValue ? ActDate.AddDays(day - ActStartDay) : ActStarts[best].AddDays(day - best);
    }

    /// <summary>Each act's first day and its calendar date, for dating days from earlier acts.</summary>
    public Dictionary<int, DateOnly> ActStarts { get; set; } = new();
    public Act Act => Campaign.Acts[ActIndex];

    public Station? StationById(int id) => Stations.Find(s => s.Id == id);
    public Staff? StaffById(int id) => Staff.Find(s => s.Id == id);
    public Contract? ContractById(int id) => Contracts.Find(c => c.Id == id);

    public void Emit(EventKind kind, string text) => Events.Add(new GameEvent(kind, text));

    // ------------------------------------------------------------------ time

    /// <summary>Advance by game minutes. The view calls this every frame.</summary>
    public void Advance(double minutes)
    {
        if (Status != GameStatus.Playing || ActCompletePending) return;
        Accum += minutes;
        while (Accum >= Defs.Tick)
        {
            Accum -= Defs.Tick;
            StepOnce(Defs.Tick);
            if (Status != GameStatus.Playing || ActCompletePending) { Accum = 0; break; }
        }
    }

    void StepOnce(double dt)
    {
        int dayBefore = Day;
        Now += dt;
        if (Day != dayBefore) OnNewDay();

        TickContracts();
        TickStaff(dt);
        MoveStaff(dt);
        WorkStations(dt);
        TickRemote();
        TickRaids();
        TickCrises();
        TickInspection();
        TickOperations();

        if (Now >= NextScheduleAt)
        {
            NextScheduleAt = Now + 2;
            ReleaseHeld();
            AssignCarriers();
            Schedule();
            FinishRemovals();
            CheckGoals();
        }
    }

    void OnNewDay()
    {
        RecordDay();
        RepLog.RemoveAll(e => Now - e.At > 14 * Defs.MinutesPerDay);
        RollWeather();
        MaybeStaleCribs();
        PlanRaid();
        PlanCrisis();
        PlanInspection();
        DailyPeople();
        DailyIntelligence();
        foreach (var k in Flows.Keys.ToList())
            if ((Flows[k] *= 0.5) < 0.25) Flows.Remove(k);
        if (Day % 7 == 0) { WeeklyAccounts(); CoverLeft = Defs.CoverPerWeek; }
        if (Day % 7 == 3) RefreshCandidates();
        if (SharkBlackout) TonnageLost += Rng.Range(14000, 26000);
        if (ActIndex >= 1) AdjustRep(-DailyExpectation, "Whitehall's expectations");
        if (Funds < 0)
        {
            AdjustRep(-1.5, "Overdrawn");
            Emit(EventKind.Bad, "The Treasury is asking why GC&CS is overdrawn. Reputation falls.");
        }
        // Forget key records older than a week.
        int cutoff = Day - 7;
        foreach (var k in Keys.Keys.ToList())
            if (int.Parse(k[(k.IndexOf(':') + 1)..]) < cutoff) Keys.Remove(k);
        foreach (var d in new[] { SixtaLogged, SixtaRouted })
            foreach (var k in d.Keys.ToList())
                if (int.Parse(k[(k.IndexOf(':') + 1)..]) < cutoff) d.Remove(k);
    }

    // ------------------------------------------------------------- movement

    readonly Dictionary<(int x, int y), int> _crowd = new();

    double CrowdFactor(Staff s)
    {
        int others = _crowd.GetValueOrDefault(s.TilePos) - 1 + (s.Path.Count > 0 ? _crowd.GetValueOrDefault(s.Path[0]) : 0);
        return others <= 0 ? 1 : Math.Max(Defs.CrowdFloor, 1 / (1 + Defs.CrowdSlow * others));
    }

    /// <summary>For tests and the flow view: how much walking is slowed on a tile by the people on it (1 = clear).</summary>
    public double CrowdingAt((int x, int y) tile) => 1 / (1 + Defs.CrowdSlow * Math.Max(0, Staff.Count(o => !o.Away && o.TilePos == tile) - 1));

    void MoveStaff(double dt)
    {
        double step = Defs.WalkTilesPerMinute * dt;
        double outdoors = OutdoorPace;
        // Crowding: people squeezing past each other (and past those sitting at their desks) slow down.
        _crowd.Clear();
        foreach (var o in Staff)
            if (!o.Away) _crowd[o.TilePos] = _crowd.GetValueOrDefault(o.TilePos) + 1;
        for (int i = Staff.Count - 1; i >= 0; i--)
        {
            var s = Staff[i];
            if (s.Path.Count == 0) continue;
            double remaining = s.Role == Role.Messenger ? step * Defs.MessengerSpeed : step;
            remaining *= CrowdFactor(s);
            if (outdoors < 1 && Grid.ZoneAt((int)s.X, (int)s.Y) == null) remaining *= outdoors;
            while (remaining > 0 && s.Path.Count > 0)
            {
                var (tx, ty) = s.Path[0];
                if (!Grid.Walkable(tx, ty))
                {
                    // Something was built in the way: re-plan to the same goal.
                    var goal = s.Path[^1];
                    var p = Pathfinder.Find(Grid, s.TilePos, goal);
                    if (p == null) { s.Path.Clear(); OnArrive(s); break; }
                    s.Path = p;
                    continue;
                }
                double cx = tx + 0.5, cy = ty + 0.5;
                double dx = cx - s.X, dy = cy - s.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d <= remaining)
                {
                    s.X = cx; s.Y = cy;
                    remaining -= d;
                    s.Path.RemoveAt(0);
                    if (s.Path.Count == 0) OnArrive(s);
                }
                else
                {
                    s.X += dx / d * remaining;
                    s.Y += dy / d * remaining;
                    remaining = 0;
                }
            }
        }
    }

    bool SendTo(Staff s, (int x, int y) tile)
    {
        var from = s.TilePos;
        if (from == tile) { s.Path.Clear(); return true; }
        var p = Pathfinder.Find(Grid, from, tile);
        if (p == null) return false;
        s.Path = p;
        return true;
    }

    void OnArrive(Staff s)
    {
        var t = s.Task;
        if (t == null) return;
        if (t.Kind == TaskKind.Leave) { GoAway(s); return; }
        if (t.Kind == TaskKind.Rest)
        {
            var rest = StationById(t.Dest);
            if (rest == null) { s.Task = null; return; }
            if (s.TilePos != rest.UseTile && !SendTo(s, rest.UseTile)) s.Task = null;
            return;
        }
        if (t.Kind == TaskKind.Operate)
        {
            var st = StationById(t.Dest);
            if (st == null || st.OperatorId != s.Id) { s.Task = null; return; }
            if (s.TilePos != st.UseTile && !SendTo(s, st.UseTile)) { ReleaseOperator(st); return; }
            if (s.TilePos == st.UseTile) st.OperatorPresent = true;
            return;
        }

        // Carry
        if (t.Phase == 0)
        {
            var src = StationById(t.Source);
            if (src == null) { CancelCarry(s); return; }
            if (s.TilePos != src.UseTile) { if (!SendTo(s, src.UseTile)) CancelCarry(s); return; }
            foreach (var id in t.Msgs)
            {
                var m = Messages[id];
                src.Output.Remove(id);
                m.Since = Now;
                m.Loc = Loc.Carried;
                m.LocId = s.Id;
            }
            t.Phase = 1;
            var dest = StationById(t.Dest);
            if (dest == null || !SendTo(s, dest.UseTile)) { DropReservation(t); t.Dest = -1; }
            else if (s.Path.Count == 0) OnArrive(s);   // the two stations share a working tile
        }
        else if (t.Phase == 1 && t.Dest >= 0)
        {
            var dest = StationById(t.Dest);
            if (dest == null) { t.Dest = -1; return; }
            if (s.TilePos != dest.UseTile) { if (!SendTo(s, dest.UseTile)) { DropReservation(t); t.Dest = -1; } return; }
            foreach (var id in t.Msgs)
            {
                var m = Messages[id];
                m.Loc = Loc.Input;
                m.LocId = dest.Id;
                m.Reserved = false;
                m.Since = Now;
                dest.Input.Add(id);
            }
            dest.ReservedInputs = Math.Max(0, dest.ReservedInputs - t.Msgs.Count);
            AddFlow(t.Source, dest.Id, t.Msgs.Count);
            s.Task = null;
            if (s.Leaving) RemoveStaff(s);
        }
        else if (t.Phase == 2)
        {
            var tray = StationById(t.Dest);
            if (tray != null && s.TilePos != tray.UseTile && SendTo(s, tray.UseTile)) return;
            OnDropOff(s, t);
            if (s.Task == null && s.Leaving) RemoveStaff(s);
        }
    }

    void DropReservation(StaffTask t)
    {
        var dest = StationById(t.Dest);
        if (dest != null) dest.ReservedInputs = Math.Max(0, dest.ReservedInputs - t.Msgs.Count);
    }

    void CancelCarry(Staff s)
    {
        var t = s.Task!;
        foreach (var id in t.Msgs)
            if (Messages.TryGetValue(id, out var m) && m.Loc == Loc.Output) m.Reserved = false;
        DropReservation(t);
        s.Task = null;
    }

    // ------------------------------------------------------------- stations

    void WorkStations(double dt)
    {
        _heads = HeadsToday();
        foreach (var st in Stations)
        {
            if (st.Def.Operator == null)
            {
                if (st.Wears && !MachineUp(st)) continue;
                if (st.Def.Minutes > 0) WorkAutomatic(st, dt);
                continue;
            }
            if (st.Wears && !MachineUp(st)) continue;
            if (Unpowered(st)) continue;
            if (st.IsMachine && st.Current != null)
            {
                // Running unattended until it stops.
                if (WearAndMaybeBreak(st, dt)) continue;
                st.WorkDone += dt;
                st.LastWorked = Now;
                if (st.WorkDone >= st.WorkTotal)
                {
                    var m = Messages[st.Current.Value];
                    st.Current = null;
                    st.WorkDone = st.WorkTotal = 0;
                    st.Processed++;
                    Complete(st, null, m);
                }
                continue;
            }
            if (st.OperatorId == null || !st.OperatorPresent) continue;
            var op = StaffById(st.OperatorId.Value);
            if (op == null) { ReleaseOperator(st); continue; }
            // Hand over mid-job only when exhausted; otherwise take the break between messages.
            if (op.Energy < Defs.Exhausted || (st.Current == null && op.Tired)) { ReleaseOperator(st); continue; }

            if (st.Current == null)
            {
                if (st.Input.Count > 0 && !st.Obsolete)
                {
                    int id = MostUrgent(st.Input);
                    st.Input.Remove(id);
                    var m = Messages[id];
                    m.Loc = Loc.Processing;
                    m.LocId = st.Id;
                    st.Current = id;
                    st.WorkDone = 0;
                    st.WorkTotal = Duration(st, op, m);
                    st.IdleMinutes = 0;
                    if (st.IsMachine) { ReleaseOperator(st); continue; }
                }
                else
                {
                    st.IdleMinutes += dt;
                    if (st.IdleMinutes >= Defs.OperatorLingerMinutes || op.Leaving || st.Obsolete || st.PendingRemoval)
                        ReleaseOperator(st);
                    continue;
                }
            }

            if (st.Wears && WearAndMaybeBreak(st, dt)) { ReleaseOperator(st); continue; }
            double head = _heads.Count > 0 && Grid.ZoneAt(st.X, st.Y) is { } hz && _heads.TryGetValue(hz, out var hs) ? hs.Work : 1;
            st.WorkDone += dt * op.WorkRate * RaidWorkFactor * head * OvertimeFactor * RivalryFactor(op, dt);
            st.LastWorked = Now;
            if (st.WorkDone >= st.WorkTotal)
            {
                var m = Messages[st.Current!.Value];
                st.Current = null;
                st.WorkDone = st.WorkTotal = 0;
                st.Processed++;
                op.LastZone = Grid.ZoneAt(st.X, st.Y)?.Id;
                GainExperience(op, st.Def.Minutes * (HeadOfLastZone(op)?.Learn ?? 1));
                Complete(st, op, m);
            }
        }
    }

    /// <summary>The message in a tray whose request is due soonest; first come, first served among equals.</summary>
    /// <summary>For tests.</summary>
    public int DebugMostUrgent(List<int> tray) => MostUrgent(tray);

    int MostUrgent(List<int> tray)
    {
        int best = tray[0];
        (int, double) bestKey = (int.MaxValue, double.MaxValue);
        foreach (var id in tray)
        {
            if (!Messages.TryGetValue(id, out var m)) continue;
            // Networks put First come first; then only live requests set the order: work for a request already
            // missed or met waits until the rest is done.
            double due = ContractById(m.ContractId) is { State: ContractState.Active } c ? WorkDue(c) : double.MaxValue;
            var key = (Rank(m), due);
            if (key.CompareTo(bestKey) < 0) { bestKey = key; best = id; }
        }
        return best;
    }

    /// <summary>False while broken or being serviced. Announces the machine's return when the downtime ends.</summary>
    bool MachineUp(Station st)
    {
        if (st.DownUntil <= 0) return true;
        if (st.IsDown(Now)) return false;
        Emit(EventKind.Good, st.Servicing
            ? $"{st.Def.Name} #{st.Id} is serviced and back at work."
            : $"{Cap(Defs.Fitters(st.Type))} have repaired {st.Def.Name} #{st.Id}.");
        st.DownUntil = 0;
        st.Servicing = false;
        return true;
    }

    /// <summary>Adds wear for dt minutes of work. Returns true if the machine broke down.</summary>
    bool WearAndMaybeBreak(Station st, double dt)
    {
        if (_heads.Count > 0 && Grid.ZoneAt(st.X, st.Y) is { } wz && _heads.TryGetValue(wz, out var wh)) dt *= wh.Wear;
        st.Wear = Math.Min(1.5, st.Wear + dt / Defs.WearLife(st.Type));
        double p = Defs.BreakdownHazard * st.Wear * st.Wear * dt * Defs.Difficulties[Difficulty].WearMult;
        if (!Rng.Chance(p)) return false;
        BreakDown(st);
        return true;
    }

    /// <summary>A teleprinter wears with every message it prints, and a worn one jams.</summary>
    void TeleprinterWear(Station tp)
    {
        tp.Wear = Math.Min(1.5, tp.Wear + Defs.TeleprinterWearPerMsg);
        if (Rng.Chance(Defs.TeleprinterHazard * tp.Wear * tp.Wear * Defs.Difficulties[Difficulty].WearMult)) BreakDown(tp);
    }

    void BreakDown(Station st)
    {
        st.DownUntil = Now + Defs.RepairMinutesFor(st.Type);
        st.Servicing = false;
        st.Wear = 0.1;
        Funds -= Defs.RepairCostFor(st.Type);
        Stats.Breakdowns++;
        string what = st.Type switch
        {
            StationType.Bombe or StationType.Bombe4 => Rng.Pick(new[] { "a drum has seized", "the brushes have shorted", "a relay has burnt out" }),
            StationType.HeathRobinson => Rng.Pick(new[] { "a tape has snapped", "the tapes have slipped out of step", "a tape has stretched and torn on the sprockets" }),
            StationType.TunnyMachine => "the tape has snapped",
            StationType.Typex => Rng.Pick(new[] { "a rotor contact has worn through", "the printer has jammed", "the keyboard has seized" }),
            StationType.Inbox => Rng.Pick(new[] { "the tape has jammed in the printer", "the line has gone dead", "the print head has worn out" }),
            _ => Rng.Pick(new[] { "a valve has blown", "the photocell has drifted" }),
        };
        Emit(EventKind.Bad, $"{st.Def.Name} #{st.Id} has broken down: {what}. {Cap(Defs.Fitters(st.Type))} will take about {Defs.RepairMinutesFor(st.Type) / 60:0} hours (£{Defs.RepairCostFor(st.Type)})." +
            (st.Type == StationType.Inbox ? " Until then its traffic waits at the Y-station, unless another teleprinter can take it." : " Service machines before they wear out."));
    }

    /// <summary>Stations that work by themselves: the pneumatic tubes.</summary>
    void WorkAutomatic(Station st, double dt)
    {
        if (st.Current == null)
        {
            if (st.Input.Count == 0) return;
            int id = st.Input[0];
            st.Input.RemoveAt(0);
            Messages[id].Loc = Loc.Processing;
            Messages[id].LocId = st.Id;
            st.Current = id;
            st.WorkDone = 0;
            st.WorkTotal = st.Def.Minutes;
        }
        st.WorkDone += dt;
        st.LastWorked = Now;
        if (st.WorkDone < st.WorkTotal) return;
        var m = Messages[st.Current.Value];
        st.Current = null;
        st.WorkDone = st.WorkTotal = 0;
        st.Processed++;
        // Pop out at the tube station nearest wherever the message is going next.
        var target = st;
        if (m.Next is StationType next && BestDestination(next, st.UseTile, 1) is { } dest)
            target = Stations.Where(x => x.Type == StationType.Tube && !x.PendingRemoval)
                .OrderBy(x => Manhattan(x.UseTile, dest.UseTile)).First();
        m.Loc = Loc.Output;
        m.LocId = target.Id;
        m.Since = Now;
        target.Output.Add(m.Id);
        AddFlow(st.Id, target.Id, 1);
    }

    void AddFlow(int from, int to, int n)
    {
        if (from < 0 || from == to) return;
        var k = $"{from}>{to}";
        Flows[k] = (Flows.TryGetValue(k, out var v) ? v : 0) + n;
    }

    void ReleaseOperator(Station st)
    {
        if (st.OperatorId is int id)
        {
            var op = StaffById(id);
            if (op != null && op.Task?.Kind == TaskKind.Operate)
            {
                op.Task = null;
                op.Path.Clear();
                if (op.Leaving) RemoveStaff(op);
            }
        }
        st.OperatorId = null;
        st.OperatorPresent = false;
        st.IdleMinutes = 0;
    }

    public double Duration(Station st, Staff op, Message m)
    {
        var def = st.Def;
        var net = Defs.NetOf(m.Net);
        double t = def.Minutes;
        switch (st.Type)
        {
            case StationType.Registration:
                if (Researched.Contains(ResearchId.CardIndex)) t *= 0.6;
                if (Researched.Contains(ResearchId.Hollerith)) t *= 0.7;
                break;
            case StationType.Typex:
                if (Researched.Contains(ResearchId.Hollerith)) t *= 0.7;
                break;
            case StationType.HandCipher:
                t *= m.Offizier ? Defs.OffizierMult : net.Difficulty;
                break;
            case StationType.Zygalski:
                if (Researched.Contains(ResearchId.Cillies)) t *= 0.6;
                break;
            case StationType.Bombe:
            case StationType.Bombe4:
                t *= BombeFactor(m);
                break;
            case StationType.Crib:
                if (Gardened(m.Net, m.Day)) t *= 0.5;
                else if (Stale(m.Net, m.Day)) t *= Defs.StaleCribMult;
                if (Analysed(m.Net, m.Day)) t *= Defs.SixtaCribMult;
                break;
            case StationType.Translation:
                if (Researched.Contains(ResearchId.Hut3Reporting)) t *= 0.6;
                break;
            case StationType.Index:
                if (Researched.Contains(ResearchId.CardIndex)) t *= 0.7;
                if (Researched.Contains(ResearchId.Hollerith)) t *= 0.6;
                break;
        }
        return Math.Max(1, t / op.SpeedFor(st.Type));
    }

    /// <summary>Machine-cipher decrypts go through Hut 3's Index from the Battle of Britain; hand ciphers never do.</summary>
    public bool NeedsIndex(Message m) => ActIndex >= Defs.IndexFromAct && Defs.NetOf(m.Net).Family != Family.Hand;

    /// <summary>How long a Bombe run takes relative to the base time, before the operator's speed.</summary>
    double BombeFactor(Message m)
    {
        double t = Defs.NetOf(m.Net).Difficulty;
        if (m.Banbury) t *= 0.35;
        if (Researched.Contains(ResearchId.DiagonalBoard)) t *= 0.5;
        if (m.Rerun) t *= 0.5;
        if (Analysed(m.Net, m.Day)) t *= Defs.SixtaBombeMult;
        if (!HasStation(StationType.Checking)) t *= 1.25;
        if (Stale(m.Net, m.Day) && !Gardened(m.Net, m.Day)) t *= Defs.StaleBombeMult;
        return t;
    }

    public bool Stale(Net net, int day) => StaleUntil.TryGetValue(net, out var d) && day <= d;

    /// <summary>Now and then an operator stops sending the stereotyped report a crib relied on.</summary>
    void MaybeStaleCribs()
    {
        if (ActIndex < Defs.StaleFromAct || !Rng.Chance(Defs.StaleChance)) return;
        var nets = ActiveNets.Where(n => Defs.NetOf(n).Family == Family.Enigma && InPlay(n) && !Stale(n, Day) && !(FreeKeysUntil.TryGetValue(n, out var f) && Day <= f)).ToList();
        if (nets.Count == 0) return;
        var net = Rng.Pick(nets);
        int days = Rng.Range(3, 6);
        StaleUntil[net] = Day + days - 1;
        var nd = Defs.NetOf(net);
        string why = Rng.Pick(new[]
        {
            "the weather station that sent the same 06:00 report every morning has gone quiet",
            "a new signals officer has stopped his operators repeating the call signs in the text",
            "the 'nothing to report' messages we used as cribs have stopped",
        });
        Emit(EventKind.Bad, $"{nd.Name}: {why}. Our cribs for it are poor for {days} days: menus take longer and the Bombe runs are longer." +
                            (nd.Naval ? " Gardening can still plant one." : ""));
    }

    public bool Gardened(Net net, int day) => GardenUntil.TryGetValue(net, out var d) && day <= d;

    void Complete(Station st, Staff? op, Message m)
    {
        var net = Defs.NetOf(m.Net);
        switch (st.Type)
        {
            case StationType.Registration:
                if (m.Garbled)
                {
                    // Ask the Y-station for a clean copy; it comes back in an hour or two.
                    m.Garbled = false;
                    m.Next = null;
                    m.HoldUntil = Now + Rng.Range(60, 150);
                    break;
                }
                m.Next = net.Family == Family.Hand ? StationType.HandCipher
                    : KeyBroken(m.Net, m.Day) ? Defs.DecryptStep(m.Net) : null;
                if (m.Next == null && WantsAnalysis(m)) { m.Next = StationType.TrafficAnalysis; Bump(SixtaRouted, m); }
                break;
            case StationType.TrafficAnalysis:
                LogExternals(m);
                m.Next = KeyBroken(m.Net, m.Day) ? Defs.DecryptStep(m.Net) : null;
                break;
            case StationType.HandCipher:
            {
                double p = 0.55 + 0.12 * (op?.Skill ?? 2) + ((op?.Specialties.Contains(st.Type) ?? false) ? 0.15 : 0) + ((op?.Has(Trait.Meticulous) ?? false) ? 0.1 : 0);
                if ((op?.Energy ?? 1) < 0.3) p -= 0.1;
                if (!Rng.Chance(p)) { Requeue(st, m); return; }
                if (m.Offizier) Stats.OffizierRead++;
                m.Next = StationType.Translation;
                break;
            }
            case StationType.Crib:
                m.CribQuality = Gardened(m.Net, m.Day) ? 0.99
                    : Math.Min(0.97, 0.62 + 0.11 * (op?.Skill ?? 2) + ((op?.Specialties.Contains(st.Type) ?? false) ? 0.1 : 0) + ((op?.Has(Trait.CrosswordFiend) ?? false) ? 0.08 : 0));
                m.Next = AfterCrib(m.Net);
                break;
            case StationType.Banburismus:
                m.Banbury = true;
                m.Next = net.FourRotor ? StationType.Bombe4 : StationType.Bombe;
                break;
            case StationType.Zygalski:
                if (IndicatorChanged) { ResetCarrier(m); break; }
                if (Rng.Chance(0.68 + 0.1 * (op?.Skill ?? 2) + ((op?.Has(Trait.Meticulous) ?? false) ? 0.08 : 0))) KeyBreak(m, false);
                else { Requeue(st, m); return; }
                break;
            case StationType.Bombe:
            case StationType.Bombe4:
                BombeStop(m);
                break;
            case StationType.Checking:
            {
                double p = Math.Min(0.98, m.CribQuality + ((op?.Has(Trait.Meticulous) ?? false) ? 0.06 : 0));
                if (Rng.Chance(p)) KeyBreak(m, true);
                else if (!m.Rerun)
                {
                    m.Rerun = true;
                    m.Next = net.FourRotor ? StationType.Bombe4 : StationType.Bombe;
                    Emit(EventKind.Info, $"The checking machine rejected a {net.Name} stop. The Bombe carries on from where it stopped.");
                }
                else
                {
                    m.Rerun = false;
                    m.Banbury = false;
                    m.Next = StationType.Crib;
                    Emit(EventKind.Info, $"Another false stop on {net.Name}. The crib goes back for a new menu.");
                }
                break;
            }
            case StationType.OutstationLink:
                // The menu goes by teleprinter to the outstations; the stop comes back later.
                m.Loc = Loc.Remote;
                m.LocId = st.Id;
                RemoteJobs.Add(new RemoteJob
                {
                    Msg = m.Id, Link = st.Id, FourRotor = net.FourRotor,
                    Done = Now + Defs.Station(StationType.Bombe).Minutes * BombeFactor(m) * 1.1 + 30,
                });
                return;
            case StationType.Testery:
                if (Rng.Chance(0.7 + 0.08 * (op?.Skill ?? 2) + ((op?.Has(Trait.Meticulous) ?? false) ? 0.08 : 0))) KeyBreak(m, false);
                else { Requeue(st, m); return; }
                break;
            case StationType.Colossus:
                if (Rng.Chance(0.92)) KeyBreak(m, true);
                else { Requeue(st, m); return; }
                break;
            case StationType.HeathRobinson:
                // The counts were often marginal: a fifth of runs come back inconclusive and go round again.
                if (Rng.Chance(0.8)) KeyBreak(m, true);
                else { Requeue(st, m); return; }
                break;
            case StationType.Typex:
                if (op != null && Rng.Chance(TypexSlipChance(op)))
                {
                    // A wheel set one place out: the output is gibberish and the message has to be set up again.
                    Stats.TypexSlips++;
                    Requeue(st, m);
                    return;
                }
                // Under the naval key, an Offizier message is still gibberish: the inner layer is unpicked by hand.
                if (net.Naval && !m.Offizier && Campaign.OffizierFrom >= 0 && ActIndex >= Campaign.OffizierFrom && HasStation(StationType.HandCipher)
                    && new Random(Mix(Seed, m.Id, 0x0FF1)).NextDouble() < Defs.OffizierShare)
                {
                    m.Offizier = true;
                    m.Next = StationType.HandCipher;
                    break;
                }
                m.Next = StationType.Translation;
                break;
            case StationType.TunnyMachine:
                m.Next = StationType.Translation;
                break;
            case StationType.Translation:
                m.Next = NeedsIndex(m) ? StationType.Index : StationType.Dispatch;
                break;
            case StationType.Index:
                m.Next = StationType.Dispatch;
                break;
            case StationType.Dispatch:
                Deliver(m);
                if (op != null) op.Decrypts++;
                return;
        }
        m.Loc = Loc.Output;
        m.LocId = st.Id;
        m.Since = Now;
        st.Output.Add(m.Id);
    }

    /// <summary>A Bombe has stopped. With a checking machine the stop is tested there; without one, it's a gamble.</summary>
    void BombeStop(Message m)
    {
        if (HasStation(StationType.Checking)) { m.Next = StationType.Checking; return; }
        if (Rng.Chance(m.CribQuality)) { KeyBreak(m, true); return; }
        m.Banbury = false;
        m.Rerun = false;
        m.Next = StationType.Crib;
        Emit(EventKind.Info, $"The Bombe stopped on a false key for {Defs.NetOf(m.Net).Name}. The crib goes back for a new menu.");
    }

    void TickRemote()
    {
        for (int i = RemoteJobs.Count - 1; i >= 0; i--)
        {
            var j = RemoteJobs[i];
            if (j.Done > Now) continue;
            RemoteJobs.RemoveAt(i);
            if (!Messages.TryGetValue(j.Msg, out var m)) continue;
            var link = StationById(j.Link) ?? Stations.FirstOrDefault(s => s.Type == StationType.Inbox);
            if (link == null) continue;
            BombeStop(m);
            m.Loc = Loc.Output;
            m.LocId = link.Id;
            m.Since = Now;
            link.Output.Add(m.Id);
        }
    }

    public int OutstationBusy => RemoteJobs.Count;

    /// <summary>Garbled messages whose clean copy has arrived go back to registration.</summary>
    void ReleaseHeld()
    {
        foreach (var m in Messages.Values)
            if (m.HoldUntil >= 0 && Now >= m.HoldUntil)
            {
                m.HoldUntil = -1;
                m.Next = StationType.Registration;
            }
    }

    void Requeue(Station st, Message m)
    {
        m.Loc = Loc.Input;
        m.LocId = st.Id;
        st.Input.Insert(0, m.Id);
    }

    /// <summary>The last delivered messages, kept so they can still be read.</summary>
    public List<ArchivedMsg> Archive { get; set; } = new();
    public const int ArchiveSize = 30;

    public InterceptText Intercept(Message m) => Intercepts.For(Seed, m.Id, m.Net, m.Day, DateOf(m.Day), m.Created, m.Garbled);
    public InterceptText Intercept(ArchivedMsg a) => Intercepts.For(Seed, a.Id, a.Net, a.Day, DateOf(a.Day), a.Created, a.Garbled);

    void Deliver(Message m)
    {
        m.Loc = Loc.Done;
        Archive.Add(new ArchivedMsg { Id = m.Id, Net = m.Net, Day = m.Day, Created = m.Created, Garbled = m.Garbled });
        if (Archive.Count > ArchiveSize) Archive.RemoveRange(0, Archive.Count - ArchiveSize);
        Messages.Remove(m.Id);
        var net = Defs.NetOf(m.Net);
        Stats.DeliveredTotal++;
        DeliveredToday++;
        Stats.Inc(Stats.Delivered, m.Net);
        Points += PointsFor(m);
        if (Now - m.Created > Defs.StaleIntelAfter) Stats.LateDecrypts++;
        OnDecryptUsed(m);
        var c = ContractById(m.ContractId);
        if (c != null && c.State == ContractState.Active) c.Delivered++;
    }

    public static double TypexSlipChance(Staff op)
    {
        double p = Defs.TypexSlip[Math.Clamp(op.Skill, 1, 3)];
        if (op.Energy < 0.3) p *= 2;
        if (op.Has(Trait.Meticulous)) p *= 0.5;
        if (op.Specialties.Contains(StationType.Typex)) p *= 0.5;
        return p;
    }

    /// <summary>Breakthrough points a decrypt earns now: news goes off, so late ones earn half, very late ones nothing.</summary>
    public int PointsFor(Message m)
    {
        int p = Defs.NetOf(m.Net).Points;
        double age = Now - m.Created;
        return age > Defs.DeadIntelAfter ? 0 : age > Defs.StaleIntelAfter ? p / 2 : p;
    }

    // ------------------------------------------------------------- keys

    public static string KeyId(Net net, int day) => $"{net}:{day}";

    public KeyStatus KeyState(Net net, int day) =>
        Keys.TryGetValue(KeyId(net, day), out var k) ? k : KeyStatus.None;

    public bool KeyBroken(Net net, int day) =>
        KeyState(net, day) == KeyStatus.Broken ||
        (FreeKeysUntil.TryGetValue(net, out var until) && day <= until);

    public bool LockedDown(Net net) =>
        LockdownUntil.TryGetValue(Defs.NetOf(net).Service, out var d) && Day <= d;

    StationType AfterCrib(Net net)
    {
        var nd = Defs.NetOf(net);
        if (nd.Naval && Researched.Contains(ResearchId.Banburismus) && HasStation(StationType.Banburismus))
            return StationType.Banburismus;
        return nd.FourRotor ? StationType.Bombe4 : StationType.Bombe;
    }

    public bool HasStation(StationType t) =>
        Stations.Any(s => s.Accepts(t));

    /// <summary>The first step of a key-breaking attempt for a net, or null when there is no way yet.</summary>
    public StationType? KeyRoute(Net net)
    {
        var nd = Defs.NetOf(net);
        if (LockedDown(net)) return null;
        if (nd.Family == Family.Lorenz)
        {
            if (Researched.Contains(ResearchId.Colossus) && HasStation(StationType.Colossus)) return StationType.Colossus;
            if (Researched.Contains(ResearchId.HeathRobinson) && HasStation(StationType.HeathRobinson)) return StationType.HeathRobinson;
            if (Researched.Contains(ResearchId.Turingery) && HasStation(StationType.Testery)) return StationType.Testery;
            return null;
        }
        bool cribOk = HasStation(StationType.Crib) &&
                      (!nd.FourRotor || Researched.Contains(ResearchId.ShortWeatherCipher));
        bool remote = OutstationBombes > 0 && HasStation(StationType.OutstationLink);
        bool bombeOk = nd.FourRotor
            ? Researched.Contains(ResearchId.FourRotorBombe) && (HasStation(StationType.Bombe4) || remote)
            : Researched.Contains(ResearchId.Bombe) && (HasStation(StationType.Bombe) || remote);
        if (cribOk && bombeOk) return StationType.Crib;
        if (nd.ZygalskiOk && !IndicatorChanged && Researched.Contains(ResearchId.ZygalskiSheets) &&
            HasStation(StationType.Zygalski))
            return StationType.Zygalski;
        return null;
    }

    /// <summary>Why a net can't be broken right now, for the HUD.</summary>
    public string? KeyRouteProblem(Net net)
    {
        if (KeyRoute(net) != null) return null;
        var nd = Defs.NetOf(net);
        if (LockedDown(net))
        {
            int left = LockdownUntil[nd.Service] - Day + 1;
            return $"Procedures tightened: {left} day{(left == 1 ? "" : "s")}";
        }
        if (nd.Family == Family.Lorenz)
        {
            if (!Researched.Contains(ResearchId.Turingery)) return "Research Turingery";
            return "Build a Testery desk";
        }
        if (nd.FourRotor)
        {
            if (!Researched.Contains(ResearchId.ShortWeatherCipher)) return "No cribs for four wheels";
            if (!Researched.Contains(ResearchId.FourRotorBombe)) return "Needs four-rotor Bombe research";
            if (!HasStation(StationType.Bombe4)) return "Build a four-rotor Bombe";
            return "Build a crib desk";
        }
        if (Researched.Contains(ResearchId.Bombe))
        {
            if (!HasStation(StationType.Bombe)) return "Build a Bombe";
            return "Build a crib desk";
        }
        if (nd.ZygalskiOk && Researched.Contains(ResearchId.ZygalskiSheets) && !IndicatorChanged)
            return "Build a Zygalski sheet table";
        return IndicatorChanged && nd.ZygalskiOk ? "Sheets obsolete: research the Bombe" : "No method yet";
    }

    void AssignCarriers()
    {
        // Messages bound for a traffic-analysis desk that's since been removed go back to waiting for the key.
        if (!HasStation(StationType.TrafficAnalysis))
            foreach (var m in Messages.Values)
                if (m.Next == StationType.TrafficAnalysis && m.Loc == Loc.Output && !m.Reserved)
                    m.Next = KeyBroken(m.Net, m.Day) ? Defs.DecryptStep(m.Net) : null;
        var waiting = Messages.Values
            .Where(m => m.Next == null && m.Loc == Loc.Output && !m.Carrier && m.HoldUntil < 0)
            .GroupBy(m => (m.Net, m.Day));
        foreach (var g in waiting)
        {
            var (net, day) = g.Key;
            if (KeyBroken(net, day)) { foreach (var m in g) m.Next = Defs.DecryptStep(net); continue; }
            if (KeyState(net, day) == KeyStatus.Attempting) continue;
            if (KeyOrderOf(net) == KeyOrder.Hold) continue;
            var route = KeyRoute(net);
            if (route == null) continue;
            var carrier = g.OrderBy(m => m.Created).First();
            carrier.Carrier = true;
            carrier.Next = route;
            Keys[KeyId(net, day)] = KeyStatus.Attempting;
        }
    }

    void KeyBreak(Message carrier, bool byBombe)
    {
        var net = carrier.Net;
        if (LockedDown(net))
        {
            ResetCarrier(carrier);
            Emit(EventKind.Bad, $"{Defs.NetOf(net).Name}: the new procedures make this key useless. The work is wasted.");
            return;
        }
        Keys[KeyId(net, carrier.Day)] = KeyStatus.Broken;
        Stats.Inc(Stats.KeyBreaks, net);
        KeysToday++;
        if (byBombe) Stats.Inc(Stats.BombeBreaks, net);
        if (net == Net.Shark && SharkBlackout)
        {
            SharkBlackout = false;
            Emit(EventKind.Good, "SHARK is broken. The Submarine Tracking Room can see the U-boats again.");
        }
        carrier.Carrier = false;
        carrier.Banbury = false;
        carrier.Rerun = false;
        carrier.Next = Defs.DecryptStep(net);
        ReleaseWaiting(net, d => d == carrier.Day);
        var date = DateOf(carrier.Day);
        Emit(EventKind.Good, $"{Defs.NetOf(net).Name} broken for {date.ToString("d MMMM", System.Globalization.CultureInfo.InvariantCulture)}. Its traffic can be decrypted.");
    }

    void ReleaseWaiting(Net net, Func<int, bool> dayMatch)
    {
        foreach (var m in Messages.Values)
            if (m.Net == net && dayMatch(m.Day) && m.Next == null && !m.Carrier && m.Loc != Loc.Done && m.HoldUntil < 0)
                m.Next = Defs.DecryptStep(net);
    }

    void ResetCarrier(Message m)
    {
        if (m.Carrier) Keys.Remove(KeyId(m.Net, m.Day));
        m.Carrier = false;
        m.Banbury = false;
        m.Rerun = false;
        m.Next = null;
    }

    public void GrantFreeKeys(Net net, int days)
    {
        int until = Day + days - 1;
        FreeKeysUntil[net] = Math.Max(until, FreeKeysUntil.TryGetValue(net, out var u) ? u : -1);
        ReleaseWaiting(net, d => d <= until);
    }

    public void ChangeIndicators()
    {
        IndicatorChanged = true;
        foreach (var st in Stations.Where(s => s.Type == StationType.Zygalski))
        {
            st.Obsolete = true;
            foreach (var id in st.Input.Concat(st.Current is int c ? new[] { c } : Array.Empty<int>()).ToList())
            {
                var m = Messages[id];
                ResetCarrier(m);
                m.Loc = Loc.Output;
                m.LocId = st.Id;
                st.Output.Add(id);
            }
            st.Input.Clear();
            st.Current = null;
            st.WorkDone = st.WorkTotal = 0;
            ReleaseOperator(st);
        }
        foreach (var m in Messages.Values.Where(m => m.Next == StationType.Zygalski).ToList())
        {
            ResetCarrier(m);
            if (m.Loc == Loc.Carried)
            {
                // The runner is still holding it; let them deliver it back into a tray.
                m.Next = null;
            }
        }
        // Runners heading to a Zygalski table get re-routed by the scheduler.
        foreach (var s in Staff)
            if (s.Task is { Kind: TaskKind.Carry } t && StationById(t.Dest) is { Type: StationType.Zygalski })
            {
                DropReservation(t);
                t.Dest = -1;
            }
    }

    /// <summary>Recent reputation changes and their causes, for the player to see why it moves. Kept for a fortnight.</summary>
    public List<RepEntry> RepLog { get; set; } = new();

    public void AdjustRep(double delta, string reason = "Other")
    {
        if (delta < 0) delta *= Defs.Difficulties[Difficulty].RepLossMult;
        double before = Reputation;
        Reputation = Math.Clamp(Reputation + delta, Mode == GameMode.FreePlay ? 5 : 0, 100);
        if (Reputation != before)
        {
            Stats.RepByCause[reason] = Stats.RepByCause.GetValueOrDefault(reason) + (Reputation - before);
            var last = RepLog.Count > 0 ? RepLog[^1] : null;
            // Merge same-day entries with the same reason to keep the log (and saves) small.
            if (last != null && last.Reason == reason && (int)(last.At / Defs.MinutesPerDay) == Day) last.Delta += Reputation - before;
            else RepLog.Add(new RepEntry { At = Now, Delta = Reputation - before, Reason = reason });
            RepLog.RemoveAll(e => Now - e.At > 14 * Defs.MinutesPerDay);
        }
        if (Reputation <= 0 && Status == GameStatus.Playing)
        {
            Status = GameStatus.Lost;
            EndReason = "Whitehall has lost confidence in Bletchley Park. The Government Code and Cypher School is broken up and its staff sent back to their regiments and colleges.";
            Emit(EventKind.Lost, EndReason);
        }
    }
}
