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

    public int ActIndex { get; set; }
    public int ActStartDay { get; set; }
    public DateOnly ActDate { get; set; }
    public bool ActCompletePending { get; set; }
    public Stats Stats { get; set; } = new();
    public Stats ActBaseline { get; set; } = new();
    public int NextId { get; set; } = 1;
    public double NextOfferAt { get; set; }
    public double NextScheduleAt { get; set; }

    /// <summary>Events for the UI. The view drains this list.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<GameEvent> Events { get; } = new();

    public int Day => (int)Math.Floor(Now / Defs.MinutesPerDay);
    public double MinuteOfDay => Now - Day * Defs.MinutesPerDay;
    public DateOnly Date => ActDate.AddDays(Day - ActStartDay);
    public DateOnly DateOf(int day) => ActDate.AddDays(day - ActStartDay);
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
        DailyPeople();
        DailyIntelligence();
        foreach (var k in Flows.Keys.ToList())
            if ((Flows[k] *= 0.5) < 0.25) Flows.Remove(k);
        if (Day % 7 == 0) WeeklyAccounts();
        if (Day % 7 == 3) RefreshCandidates();
        if (SharkBlackout) TonnageLost += Rng.Range(14000, 26000);
        if (Funds < 0)
        {
            AdjustRep(-1.5);
            Emit(EventKind.Bad, "The Treasury is asking why GC&CS is overdrawn. Reputation falls.");
        }
        // Forget key records older than a week.
        int cutoff = Day - 7;
        foreach (var k in Keys.Keys.ToList())
            if (int.Parse(k[(k.IndexOf(':') + 1)..]) < cutoff) Keys.Remove(k);
    }

    // ------------------------------------------------------------- movement

    void MoveStaff(double dt)
    {
        double step = Defs.WalkTilesPerMinute * dt;
        for (int i = Staff.Count - 1; i >= 0; i--)
        {
            var s = Staff[i];
            if (s.Path.Count == 0) continue;
            double remaining = s.Role == Role.Messenger ? step * Defs.MessengerSpeed : step;
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
        foreach (var st in Stations)
        {
            if (st.Def.Operator == null)
            {
                if (st.Def.Minutes > 0) WorkAutomatic(st, dt);
                continue;
            }
            if (st.IsMachine && st.Current != null)
            {
                // Running unattended until it stops.
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
                    int id = st.Input[0];
                    st.Input.RemoveAt(0);
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

            st.WorkDone += dt * op.WorkRate;
            st.LastWorked = Now;
            if (st.WorkDone >= st.WorkTotal)
            {
                var m = Messages[st.Current!.Value];
                st.Current = null;
                st.WorkDone = st.WorkTotal = 0;
                st.Processed++;
                Complete(st, op, m);
            }
        }
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
                t *= net.Difficulty;
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
                break;
            case StationType.Translation:
                if (Researched.Contains(ResearchId.Hut3Reporting)) t *= 0.6;
                break;
        }
        return Math.Max(1, t / op.SpeedFor(st.Type));
    }

    /// <summary>How long a Bombe run takes relative to the base time, before the operator's speed.</summary>
    double BombeFactor(Message m)
    {
        double t = Defs.NetOf(m.Net).Difficulty;
        if (m.Banbury) t *= 0.35;
        if (Researched.Contains(ResearchId.DiagonalBoard)) t *= 0.5;
        if (m.Rerun) t *= 0.5;
        if (!HasStation(StationType.Checking)) t *= 1.25;
        return t;
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
                break;
            case StationType.HandCipher:
            {
                double p = 0.55 + 0.12 * (op?.Skill ?? 2) + ((op?.Specialties.Contains(st.Type) ?? false) ? 0.15 : 0) + ((op?.Has(Trait.Meticulous) ?? false) ? 0.1 : 0);
                if ((op?.Energy ?? 1) < 0.3) p -= 0.1;
                if (!Rng.Chance(p)) { Requeue(st, m); return; }
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
            case StationType.Typex:
            case StationType.TunnyMachine:
                m.Next = StationType.Translation;
                break;
            case StationType.Translation:
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

    void Deliver(Message m)
    {
        m.Loc = Loc.Done;
        Messages.Remove(m.Id);
        var net = Defs.NetOf(m.Net);
        Stats.DeliveredTotal++;
        DeliveredToday++;
        Stats.Inc(Stats.Delivered, m.Net);
        Points += net.Points;
        OnDecryptUsed(m);
        var c = ContractById(m.ContractId);
        if (c != null && c.State == ContractState.Active) c.Delivered++;
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
        var waiting = Messages.Values
            .Where(m => m.Next == null && m.Loc == Loc.Output && !m.Carrier && m.HoldUntil < 0)
            .GroupBy(m => (m.Net, m.Day));
        foreach (var g in waiting)
        {
            var (net, day) = g.Key;
            if (KeyBroken(net, day)) { foreach (var m in g) m.Next = Defs.DecryptStep(net); continue; }
            if (KeyState(net, day) == KeyStatus.Attempting) continue;
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

    public void AdjustRep(double delta)
    {
        if (delta < 0) delta *= Defs.Difficulties[Difficulty].RepLossMult;
        Reputation = Math.Clamp(Reputation + delta, Mode == GameMode.FreePlay ? 5 : 0, 100);
        if (Reputation <= 0 && Status == GameStatus.Playing)
        {
            Status = GameStatus.Lost;
            EndReason = "Whitehall has lost confidence in Bletchley Park. The Government Code and Cypher School is broken up and its staff sent back to their regiments and colleges.";
            Emit(EventKind.Lost, EndReason);
        }
    }
}
