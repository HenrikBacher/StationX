using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>Watches, tea breaks, fatigue and morale.</summary>
public sealed partial class GameState
{
    public bool WatchesAvailable => ActIndex >= Defs.WatchesFromAct;

    public bool OnDuty(Staff s) => AllHands || OnWatch(s);
    bool OnWatch(Staff s) => Defs.Shifts[s.Shift].OnDuty(MinuteOfDay);

    /// <summary>Everyone called in, off watch or not, for a few hours.</summary>
    public double AllHandsUntil { get; set; }
    public double LastAllHandsAt { get; set; } = -1e9;
    public bool AllHands => Now < AllHandsUntil;

    public string? CanCallAllHands() =>
        AllHands ? "Everyone is already in." :
        ActIndex < 1 ? "Not until the war gets going." :
        Now - LastAllHandsAt < Defs.AllHandsCooldownDays * Defs.MinutesPerDay
            ? $"People won't stand for it again so soon (another {Math.Ceiling((LastAllHandsAt + Defs.AllHandsCooldownDays * Defs.MinutesPerDay - Now) / 60)} h)." : null;

    public bool CallAllHands()
    {
        if (CanCallAllHands() != null) return false;
        int called = Staff.Count(s => !s.Leaving && !OnWatch(s) && !s.IsSick(Now) && !s.Arriving(Now));
        AllHandsUntil = Now + Defs.AllHandsHours * 60;
        LastAllHandsAt = Now;
        Funds -= called * Defs.AllHandsPay;
        foreach (var s in Staff.Where(s => !s.Leaving)) s.Morale = Math.Max(0, s.Morale - Defs.AllHandsMorale);
        Emit(EventKind.Info, $"All hands: messengers go round the billets and {called} people off watch are called back in for {Defs.AllHandsHours:0} hours (£{called * Defs.AllHandsPay} overtime). Nobody is pleased.");
        return true;
    }

    public int OnDutyCount => Staff.Count(s => !s.Away);

    void TickStaff(double dt)
    {
        double hour = MinuteOfDay / 60;
        bool night = hour >= 20 || hour < 6;
        var cold = ColdZones();
        bool anyRest = Stations.Any(RestOpen);
        for (int i = Staff.Count - 1; i >= 0; i--)
        {
            var s = Staff[i];
            bool duty = OnDuty(s);
            if (s.Away)
            {
                if (duty && !s.Leaving && !s.IsSick(Now) && !s.Arriving(Now))
                {
                    if (s.ArrivesAt > 0)
                    {
                        s.ArrivesAt = 0;
                        Emit(EventKind.Info, $"{s.Name} signs the Official Secrets Act and reports for duty.");
                    }
                    ComeBack(s);
                }
                continue;
            }

            var t = s.Task;
            double drain = 0;
            if (t is { Kind: TaskKind.Rest } && s.Path.Count == 0 && StationById(t.Dest) is { } rest)
            {
                // A cup of tea goes further in company, and nowhere at all across the table from a rival.
                int company = RestingAt(rest.Id, s);
                bool rival = RivalRestingAt(s, rest.Id);
                double rate = rival ? Defs.RestRivalMult : 1 + Defs.RestCompany * Math.Min(company, Defs.RestCompanyMax);
                s.Energy = Math.Min(1, s.Energy + Defs.EnergyRecoverRest * rate * dt);
                s.Morale = Math.Min(1, s.Morale + dt * (rival ? 0 : rest.Type switch
                {
                    StationType.Rounders => 0.008,
                    StationType.CommonRoom => 0.004,
                    _ => 0.001,
                }));
                // A proper break ends when they're fresh; someone sitting there for want of work stays until work comes.
                if (s.Energy >= Defs.BreakUntil && !t.Loafing) s.Task = null;
            }
            else if (t is { Kind: TaskKind.Operate } && StationById(t.Dest) is { Working: true })
                drain = Defs.EnergyDrainWorking;
            else if (t == null && s.Path.Count == 0)
            {
                // Standing about is no rest, unless there's nowhere at all to sit down with a cup of tea.
                if (!anyRest) s.Energy = Math.Min(1, s.Energy + Defs.EnergyRecoverNowhere * dt);
            }
            else
                drain = Defs.EnergyDrainOther;

            if (drain > 0)
            {
                if (IsCold(s, cold))
                {
                    s.Chilled = true;
                    drain *= Defs.ColdDrainMult;
                    s.Morale = Math.Max(0, s.Morale - Defs.ColdMoralePerMinute * dt);
                }
                if (s.Has(Trait.Tireless)) drain *= 0.7;
                if (AllHands && !OnWatch(s)) drain *= Defs.AllHandsDrain;
                if (Overtime) drain *= Defs.OvertimeDrain;
                if (_heads.Count > 0 && Grid.ZoneAt((int)s.X, (int)s.Y) is { } dz && _heads.TryGetValue(dz, out var dh)) drain *= dh.Drain;
                if (night && s.Has(Trait.NightOwl)) drain *= 0.67;
                s.Energy = Math.Max(0, s.Energy - drain * dt);
                if (s.Energy < s.LowToday) s.LowToday = s.Energy;
                if (s.Energy < Defs.Exhausted) s.Morale = Math.Max(0, s.Morale - 0.0004 * dt);
            }

            if (s.Leaving) continue;
            if (!duty)
            {
                switch (s.Task?.Kind)
                {
                    case null:
                        StartLeave(s);
                        break;
                    case TaskKind.Operate:
                        if (StationById(s.Task.Dest) is { } st && st.OperatorId == s.Id) ReleaseOperator(st);
                        s.Task = null;
                        StartLeave(s);
                        break;
                    case TaskKind.Rest:
                        s.Task = null;
                        StartLeave(s);
                        break;
                    // Runners finish their delivery first.
                }
                continue;
            }
            s.IdleMinutes = s.Task == null ? s.IdleMinutes + dt : 0;
            if (s.Task == null && s.Tired) StartBreak(s, false);
            else if (s.Task == null && s.Path.Count == 0 && s.IdleMinutes >= Defs.LoafAfter && s.HeadOf == null && !s.Arriving(Now)) StartBreak(s, true);
        }
    }

    /// <summary>Off watch: walk out of the hut and go back to the billets.</summary>
    void StartLeave(Staff s)
    {
        var zone = Grid.ZoneAt(s.TilePos.x, s.TilePos.y);
        if (zone == null) { GoAway(s); return; }
        var exit = zone.OutsideDoor;
        if (s.TilePos == exit || !SendTo(s, exit)) { GoAway(s); return; }
        s.Task = new StaffTask { Kind = TaskKind.Leave };
    }

    void GoAway(Staff s)
    {
        s.Away = true;
        s.Task = null;
        s.Path.Clear();
        s.HomeX = s.X;
        s.HomeY = s.Y;
    }

    void ComeBack(Staff s)
    {
        bool calledIn = AllHands && !OnWatch(s);
        s.Away = false;
        int hx = (int)Math.Floor(s.HomeX), hy = (int)Math.Floor(s.HomeY);
        if (Grid.Walkable(hx, hy)) { s.X = s.HomeX; s.Y = s.HomeY; }
        else { s.X = Grid.Gate.x + 0.5; s.Y = Grid.Gate.y + 0.5; }
        // Called back from the billets they've had a few hours' rest at best.
        s.Energy = calledIn ? Math.Max(s.Energy, 0.65) : 1;
        s.Morale = Math.Min(1, s.Morale + (calledIn ? 0 : 0.01));
    }

    bool RestOpen(Station st) =>
        !st.PendingRemoval && st.IsRest &&
        (st.Type != StationType.Rounders || MinuteOfDay is >= 8 * 60 and < 19 * 60 && Weather is Weather.Fine or Weather.Overcast);

    /// <summary>Others sitting (not still walking up) at a rest station.</summary>
    public int RestingAt(int stationId, Staff? except = null) =>
        Staff.Count(o => o != except && o.Task is { Kind: TaskKind.Rest } t && t.Dest == stationId && o.Path.Count == 0 && !o.Away);

    public bool RivalRestingAt(Staff s, int stationId) =>
        s.Rival is int rid && StaffById(rid) is { Task: { Kind: TaskKind.Rest } rt } && rt.Dest == stationId;

    int SeatsTaken(Station st) => Staff.Count(x => x.Task is { Kind: TaskKind.Rest } t && t.Dest == st.Id);

    /// <summary>
    /// Tea break. Unhappy people head for the common room or the rounders pitch. The tired go wherever there's a seat,
    /// and may turn out someone who is only sitting there for want of work; the idle only sit down nearby, and only in
    /// a free seat.
    /// </summary>
    void StartBreak(Staff s, bool loafing)
    {
        Station? best = null;
        Staff? bump = null;
        int bestScore = int.MaxValue;
        foreach (var st in Stations)
        {
            if (!RestOpen(st)) continue;
            int score = Manhattan(st.UseTile, s.TilePos);
            // The idle only sit down in their own building, close by, so they're quickly back when work comes.
            if (loafing && (score > Defs.LoafRange || Grid.ZoneAt(st.UseTile.x, st.UseTile.y) != Grid.ZoneAt(s.TilePos.x, s.TilePos.y))) continue;
            Staff? loafer = null;
            if (SeatsTaken(st) >= st.Def.Seats)
            {
                if (loafing) continue;
                // Anyone sitting there for want of work, or already fresh, gives up their seat to the tired.
                loafer = Staff.FirstOrDefault(x => x.Task is { Kind: TaskKind.Rest } t && t.Dest == st.Id && (t.Loafing || x.Energy >= Defs.BreakUntil));
                if (loafer == null) continue;
                score += 4;
            }
            if (s.Morale < 0.5 && st.Type != StationType.Canteen) score -= 20;
            // Company draws people in; a rival at the table keeps them away.
            score -= 2 * Math.Min(RestingAt(st.Id), Defs.RestCompanyMax);
            if (RivalRestingAt(s, st.Id)) score += 30;
            if (score < bestScore) { bestScore = score; best = st; bump = loafer; }
        }
        if (best == null || !SendTo(s, best.UseTile)) return;
        if (bump != null) { bump.Task = null; bump.Path.Clear(); }
        var used = Staff.Where(x => x.Task is { Kind: TaskKind.Rest } t && t.Dest == best.Id).Select(x => x.Task!.Seat).ToHashSet();
        int seat = 0;
        while (used.Contains(seat)) seat++;
        s.Task = new StaffTask { Kind = TaskKind.Rest, Dest = best.Id, Seat = seat, Loafing = loafing };
        if (s.Path.Count == 0) OnArrive(s);
    }

    /// <summary>People due a break with no seat free anywhere: they stand about, getting no rest.</summary>
    public int WaitingForTea => Staff.Count(s => s.Task == null && s.Tired && !s.Away && !s.Leaving && OnDuty(s));

    /// <summary>
    /// Men of military age could be called up. GC&amp;CS fought for each deferment, spending Whitehall's goodwill,
    /// or got the Foreign Office to rule the post a reserved occupation, which cost money and paperwork.
    /// </summary>
    void MaybeCallUp()
    {
        if (ActIndex < 1 || ActIndex > 10) return;
        if (Dilemmas.Count > 0 || Now - LastDilemmaAt < Defs.MinutesPerDay) return;
        if (!Rng.Chance(Defs.CallUpChance)) return;
        var men = Staff.Where(s => !s.Female && !s.Leaving && s.Notable == null && s.Role != Role.Messenger && s.Role != Role.Wren).ToList();
        if (men.Count == 0) return;
        // The Ministry of Labour has an unerring eye for the people you can least spare.
        var who = men.OrderByDescending(s => s.Skill * 10 + Rng.Range(0, 15)).First();
        var role = Defs.Roles[who.Role].Name.ToLowerInvariant();
        RaiseDilemma(new Dilemma
        {
            Service = Service.Security, StaffId = who.Id, Title = "Call-up papers",
            Text = $"{who.Name} ({role}, {new string('★', who.Skill)}) has received call-up papers from the Ministry of Labour. " +
                   "Without a deferment he reports to an Army training depot on Monday, and we can't tell the Army why he matters.",
            Options =
            {
                new() { Label = "Fight for a deferment", Rep = -4,
                    Outcome = $"Commander Denniston calls in a favour at the War Office. {who.Name} stays, and Whitehall remembers it." },
                new() { Label = "Ask the Foreign Office to make the post reserved", Funds = -Defs.CallUpRulingCost,
                    Outcome = $"A ruling comes through after a week of forms. {who.Name} stays." },
                new() { Label = "Let him go", DismissStaff = true, Morale = -0.02, Rep = 1,
                    Outcome = $"{who.Name} leaves for the Army. He may never say where he has been." },
            },
        });
    }

    public void DebugGainExperience(Staff s, double minutes) => GainExperience(s, minutes);

    /// <summary>Practice makes perfect, and the perfect ask for more money.</summary>
    void GainExperience(Staff s, double minutes)
    {
        s.Experience += minutes;
        if (s.Skill >= 3 || s.Experience < Defs.ExperienceFor[s.Skill + 1]) return;
        s.Skill++;
        int raise = Defs.Roles[s.Role].Salary / 3;
        s.Salary += raise;
        s.Morale = Math.Min(1, s.Morale + 0.05);
        Emit(EventKind.Good, $"{s.Name} has got the hang of it: now {new string('★', s.Skill)}, and {(s.Female ? "her" : "his")} pay goes up £{raise} a week.");
    }

    /// <summary>Walking speed outdoors: snow and gales slow everyone between the huts.</summary>
    public double OutdoorPace => Weather switch { Weather.Snow => 0.65, Weather.Storm => 0.8, Weather.Rain => 0.92, _ => 1 };

    /// <summary>Atmospherics: storms and fog make intercepts harder to take down cleanly.</summary>
    public double WeatherGarble => Weather switch { Weather.Storm => 2.5, Weather.Fog => 1.3, Weather.Rain => 1.15, _ => 1 };

    /// <summary>The day's weather, rolled each midnight from the month. English, so mostly grey.</summary>
    void RollWeather()
    {
        int m = Date.Month;
        bool winter = m is 12 or 1 or 2;
        double r = Rng.NextDouble();
        Weather = winter
            ? r < 0.15 ? Weather.Fine : r < 0.45 ? Weather.Overcast : r < 0.62 ? Weather.Rain : r < 0.75 ? Weather.Fog : r < 0.93 ? Weather.Snow : Weather.Storm
            : m is >= 5 and <= 8
            ? r < 0.45 ? Weather.Fine : r < 0.75 ? Weather.Overcast : r < 0.93 ? Weather.Rain : r < 0.97 ? Weather.Fog : Weather.Storm
            : r < 0.25 ? Weather.Fine : r < 0.55 ? Weather.Overcast : r < 0.8 ? Weather.Rain : r < 0.92 ? Weather.Fog : Weather.Storm;
        string? note = Weather switch
        {
            Weather.Snow => "Snow overnight. The paths between the huts are treacherous, and there's no rounders today.",
            Weather.Storm => "A gale is blowing. Reception at the listening stations is poor: expect garbled intercepts.",
            Weather.Fog => "Thick fog over the Park.",
            _ => null,
        };
        if (note != null) Emit(EventKind.Info, note);
    }

    public bool Winter => Date.Month is 11 or 12 or 1 or 2 or 3;

    /// <summary>True if this person is standing in one of the cold zones.</summary>
    public bool IsCold(Staff s, HashSet<string>? cold) =>
        cold != null && Grid.ZoneAt((int)s.X, (int)s.Y) is { } zone && cold.Contains(zone.Id);

    /// <summary>Open timber huts with no stove, while it's winter; null in summer.</summary>
    public HashSet<string>? ColdZones()
    {
        if (!Winter) return null;
        var cold = Grid.Zones.Where(z => z.Unlocked && !Defs.Heated(z.Id)).Select(z => z.Id).ToHashSet();
        foreach (var st in Stations)
            if (st.Type == StationType.Stove && !st.PendingRemoval && Grid.ZoneAt(st.X, st.Y) is { } z) cold.Remove(z.Id);
        return cold;
    }

    /// <summary>Staff beyond the billets available (the newest arrivals) are lodged far away.</summary>
    public int Overcrowded => Math.Max(0, Staff.Count(s => !s.Leaving) - Billets);

    public void AssignBillets()
    {
        int over = Overcrowded;
        var far = Staff.Where(s => !s.Leaving).OrderByDescending(s => s.Id).Take(over).Select(s => s.Id).ToHashSet();
        foreach (var s in Staff) s.FarBillet = far.Contains(s.Id);
    }

    public string? CanRequisitionBillets() => Funds < Defs.BilletCost ? $"Needs £{Defs.BilletCost}." : null;

    public bool RequisitionBillets()
    {
        if (CanRequisitionBillets() != null) return false;
        Funds -= Defs.BilletCost;
        Billets += Defs.BilletBlock;
        AssignBillets();
        Emit(EventKind.Info, $"The billeting officer finds {Defs.BilletBlock} more rooms in Bletchley and the villages. Billets: {Billets}.");
        return true;
    }

    public int SickCount => Staff.Count(s => s.IsSick(Now));
    public bool HasSickBay => Stations.Any(s => s.Type == StationType.SickBay && !s.PendingRemoval);

    /// <summary>Colds and flu: worse in winter, in freezing huts, among the worn out and the far-billeted, and catching.</summary>
    void SpreadIllness()
    {
        int sick = SickCount;
        bool bay = HasSickBay;
        double baseChance = (Winter ? Defs.SickChanceWinter : Defs.SickChanceSummer)
                            + Defs.SickContagion * Math.Min(sick, 8) * (bay ? Defs.SickBayContagion : 1);
        var fallen = new List<Staff>();
        foreach (var s in Staff)
        {
            bool chilled = s.Chilled;
            s.Chilled = false;
            if (s.Leaving || s.IsSick(Now)) continue;
            double p = baseChance * (chilled ? Defs.SickChilledMult : 1) * (s.FarBillet ? Defs.SickFarBilletMult : 1)
                       * (s.Energy < 0.3 ? Defs.SickTiredMult : 1);
            if (!Rng.Chance(p)) continue;
            s.SickUntil = Now + Rng.Range(2.0, 5.0) * (bay ? Defs.SickBayDuration : 1) * Defs.MinutesPerDay;
            s.OffReason = null;
            fallen.Add(s);
        }
        if (fallen.Count == 1)
            Emit(EventKind.Bad, $"{fallen[0].Name} has gone down with {(Winter ? "flu" : "a heavy cold")} and is off for {Math.Round((fallen[0].SickUntil - Now) / Defs.MinutesPerDay)} days.");
        else if (fallen.Count > 1)
            Emit(EventKind.Bad, $"{(Winter ? "Flu" : "A cold")} is going round the Park: {string.Join(", ", fallen.Take(3).Select(s => s.Name))}" +
                                $"{(fallen.Count > 3 ? $" and {fallen.Count - 3} more" : "")} are off sick. Warm huts and decent billets keep it down{(bay ? "" : ", and a sick bay in the Mansion gets people back sooner")}.");
    }

    void DailyPeople()
    {
        MaybeCallUp();
        MaybeRuthless();
        SpreadIllness();
        _heads = HeadsToday(inNow: false);
        DailyStrain();
        DailyRivalries();
        AssignBillets();
        // Heads of section: each person feels the head of the hut they last worked in.
        _heads = HeadsToday(inNow: false);
        foreach (var s in Staff.Where(s => s.HeadOf == null))
            if (HeadOfLastZone(s) is { } h) s.Morale = Math.Clamp(s.Morale + h.Morale, 0, 1);
        foreach (var s in Staff.Where(s => s.FarBillet)) s.Morale = Math.Max(0, s.Morale - Defs.FarBilletMoralePerDay);
        if (Overcrowded > 0 && Day % 3 == 0)
            Emit(EventKind.Bad, $"{Overcrowded} of the staff are billeted miles away and bussed in. They're slower and unhappy. Requisition more billets from the Staff panel.");
        if (Winter && Stats.ScriptedDone.Add($"winter{Date.Year - (Date.Month <= 3 ? 1 : 0)}"))
            Emit(EventKind.Bad, "Winter has come to the Park. The timber huts are freezing: build a coke stove in each one, " +
                                "or people there will tire faster and grumble. The Mansion has central heating.");
        foreach (var s in Staff.ToList())
        {
            s.Morale += (Defs.MoraleBaseline - s.Morale) * 0.15;
            if (s.Shift == Shift.Night && !s.Has(Trait.NightOwl))
                s.Morale -= ++s.NightsRunning > Defs.LongNightsAfter ? Defs.LongNightsMorale : Defs.NightMorale;
            else s.NightsRunning = 0;
            if (s.Shift == Shift.Office && WatchesAvailable) s.Morale -= 0.01; // long days wear people down
            s.Morale = Math.Clamp(s.Morale, 0, 1);
            s.LowMoraleDays = s.Morale < 0.2 ? s.LowMoraleDays + 1 : 0;
            if (s.LowMoraleDays >= 2 && s.Notable == null && !s.Leaving && Rng.Chance(0.35))
            {
                Emit(EventKind.Bad, $"{s.Name} has asked for a transfer. Morale in the huts is too low.");
                Fire(s.Id);
            }
        }
    }

    public void ChangeMorale(double delta)
    {
        foreach (var s in Staff) s.Morale = Math.Clamp(s.Morale + delta, 0, 1);
    }

    public double AverageMorale => Staff.Count == 0 ? Defs.MoraleBaseline : Staff.Average(s => s.Morale);
    public double AverageEnergy => Staff.Count == 0 ? 1 : Staff.Where(s => !s.Away).DefaultIfEmpty().Average(s => s?.Energy ?? 1);

    // ----------------------------------------------------------- player actions

    public bool SetShift(int staffId, Shift shift)
    {
        var s = StaffById(staffId);
        if (s == null || (shift != Shift.Office && !WatchesAvailable)) return false;
        s.Shift = shift;
        return true;
    }

    /// <summary>Spreads everyone of a role (or everyone) evenly across the three watches.</summary>
    public void BalanceWatches(Role? role)
    {
        if (!WatchesAvailable) return;
        var watches = new[] { Shift.Day, Shift.Evening, Shift.Night };
        // On a weekly rota this week's turn decides who has the nights; otherwise night owls take them.
        int week = RotateWatches ? Day / 7 : 0;
        foreach (var group in Staff.Where(s => role == null || s.Role == role).GroupBy(s => s.Role))
        {
            int i = 0;
            foreach (var s in group.OrderByDescending(s => !RotateWatches && s.Has(Trait.NightOwl)).ThenBy(s => s.Id))
                s.Shift = watches[(i++ + 2 + week) % 3];
        }
    }

    /// <summary>
    /// Hut 6's rota: the watches change round every week, day to evening to night and back to day, so nobody is left on
    /// nights for good. Night owls lose their pick of the night watch.
    /// </summary>
    public bool RotateWatches { get; set; }

    public void SetRotation(bool on) { if (WatchesAvailable) RotateWatches = on; }

    void WeeklyRota()
    {
        if (!RotateWatches || !WatchesAvailable) return;
        foreach (var s in Staff)
            s.Shift = s.Shift switch { Shift.Day => Shift.Evening, Shift.Evening => Shift.Night, Shift.Night => Shift.Day, var o => o };
        Emit(EventKind.Info, "The watches change round for the week: day to evening, evening to night, night back to days.");
    }

    /// <summary>People who have worked more than a week of nights straight, and are paying for it.</summary>
    public int LongNights => Staff.Count(s => s.NightsRunning > Defs.LongNightsAfter);

    public bool Pin(int staffId, int? stationId)
    {
        var s = StaffById(staffId);
        if (s == null) return false;
        if (stationId is int id)
        {
            var st = StationById(id);
            if (st == null || st.Def.Operator != s.Role) return false;
        }
        s.Pinned = stationId;
        // Pinning to a desk in another building ends a posting elsewhere.
        if (stationId is int sid && StationById(sid) is { } pst && s.Posted != null && Grid.ZoneAt(pst.X, pst.Y)?.Id != s.Posted) s.Posted = null;
        return true;
    }

    /// <summary>Posts someone to one building (or back to anywhere with null). They work, carry and wait only there.</summary>
    public bool Post(int staffId, string? zoneId)
    {
        var s = StaffById(staffId);
        if (s == null) return false;
        if (zoneId != null && Grid.Zone(zoneId) is not { Unlocked: true }) return false;
        s.Posted = zoneId;
        if (zoneId != null && s.Pinned is int pid && StationById(pid) is { } pst && Grid.ZoneAt(pst.X, pst.Y)?.Id != zoneId) s.Pinned = null;
        // Someone working or carrying outside their new building finishes nothing there: they're sent over.
        if (zoneId != null && s.Task is { Kind: TaskKind.Operate } t && StationById(t.Dest) is { } st && Grid.ZoneAt(st.X, st.Y)?.Id != zoneId && st.OperatorId == s.Id)
            ReleaseOperator(st);
        return true;
    }

    /// <summary>True if this person may work at, or carry within, the building at a tile.</summary>
    /// <summary>
    /// Anyone that new or moved furniture has boxed into a corner (or stood on) steps out to the nearest floor that
    /// can be reached from the building's door. Otherwise they'd stand there for good, and the foreman would keep
    /// picking them for work they can never walk to.
    /// </summary>
    void FreeStranded()
    {
        foreach (var z in Grid.Zones)
        {
            if (!z.Unlocked) continue;
            bool[]? reach = null;
            foreach (var s in Staff)
            {
                if (s.Away) continue;
                var t = s.TilePos;
                if (!z.InOuter(t.x, t.y)) continue;
                reach ??= Pathfinder.Reachable(Grid, z.InsideDoor);
                if (reach[t.y * Grid.Width + t.x]) continue;
                (int x, int y)? best = null;
                int bestD = int.MaxValue;
                for (int y = z.Y; y < z.Y + z.H; y++)
                    for (int x = z.X; x < z.X + z.W; x++)
                    {
                        if (!reach[y * Grid.Width + x] || !Grid.Walkable(x, y)) continue;
                        int d = Math.Abs(x - t.x) + Math.Abs(y - t.y);
                        if (d < bestD) { bestD = d; best = (x, y); }
                    }
                if (best is not { } b) continue;
                (int, int)? dest = s.Path.Count > 0 ? s.Path[^1] : null;
                s.X = b.x + 0.5;
                s.Y = b.y + 0.5;
                s.Path.Clear();
                if (dest is { } goal) SendTo(s, goal);   // carry on to wherever they were going
            }
        }
    }

    public bool MayWorkAt(Staff s, (int x, int y) tile) => s.Posted == null || Grid.ZoneAt(tile.x, tile.y)?.Id == s.Posted;

    /// <summary>Routes a network to a teleprinter, or stops routing it there. All on, or all off, means "everything".</summary>
    public bool SetReceives(int stationId, Net net, bool on)
    {
        var st = StationById(stationId);
        if (st == null || st.Type != StationType.Inbox) return false;
        var list = st.Receives ?? Enum.GetValues<Net>().ToList();
        list.Remove(net);
        if (on) list.Add(net);
        st.Receives = list.Count == Enum.GetValues<Net>().Length ? null : list;
        return true;
    }

    public IEnumerable<Staff> PinnedTo(int stationId) => Staff.Where(s => s.Pinned == stationId);

    // ---- Heads of huts: Welchman ran Hut 6, Turing Hut 8 ----
    public Staff? HeadOfZone(string zoneId) => Staff.FirstOrDefault(s => s.HeadOf == zoneId && !s.Leaving);

    public static bool CanHead(Staff s) => s.Role == Role.Cryptanalyst && (s.Skill >= 2 || s.Notable != null);

    /// <summary>Zones whose head is in today and well: work there goes faster.</summary>
    public HashSet<Zone> HeadedZones() =>
        Staff.Where(s => s.HeadOf != null && !s.Leaving && !s.Away).Select(s => Grid.Zone(s.HeadOf!)).OfType<Zone>().ToHashSet();

    public bool AppointHead(int staffId, string? zoneId)
    {
        var s = StaffById(staffId);
        if (s == null || !CanHead(s)) return false;
        if (zoneId != null)
        {
            var z = Grid.Zone(zoneId);
            if (z is not { Unlocked: true }) return false;
            if (HeadOfZone(zoneId) is { } old && old != s) old.HeadOf = null;
            if (s.HeadOf == null) s.HeadStyle = StyleFor(s);
            s.HeadOf = zoneId;
            s.Pinned = null;
            Emit(EventKind.Info, $"{s.Name} is now head of {z.Name}, a {Defs.HeadStyles[s.HeadStyle].Name.ToLowerInvariant()} by temperament. They'll manage rather than work a desk.");
        }
        else s.HeadOf = null;
        return true;
    }
}
