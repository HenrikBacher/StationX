using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// A scripted, reasonably sensible player. Used to prove the campaign can be finished,
/// to collect balance numbers, and to populate the park for screenshots.
/// </summary>
public sealed class AutoPlayer
{
    readonly GameState g;
    public AutoPlayer(GameState g) { this.g = g; }

    static readonly Dictionary<StationType, string[]> PreferredZones = new()
    {
        [StationType.Registration] = new[] { "mansion", "hut6" },
        [StationType.HandCipher] = new[] { "mansion" },
        [StationType.Translation] = new[] { "hut3", "mansion" },
        [StationType.Dispatch] = new[] { "hut3", "mansion" },
        [StationType.Index] = new[] { "hut3", "hut8", "mansion" },
        [StationType.Typex] = new[] { "hut6", "hut8" },
        [StationType.Zygalski] = new[] { "hut6", "mansion" },
        [StationType.Crib] = new[] { "hut6", "hut8", "mansion" },
        [StationType.Banburismus] = new[] { "hut8", "hut6", "mansion" },
        [StationType.Bombe] = new[] { "hut11", "hut1" },
        [StationType.Bombe4] = new[] { "hut11" },
        [StationType.Inbox] = new[] { "mansion" },
        [StationType.Canteen] = new[] { "mansion", "hut3", "hut6", "hut8" },
        [StationType.CommonRoom] = new[] { "mansion", "hut3" },
        [StationType.Checking] = new[] { "hut11", "hut1" },
        [StationType.OutstationLink] = new[] { "hut11" },
        [StationType.Testery] = new[] { "blockf", "mansion" },
        [StationType.Colossus] = new[] { "blockf" },
        [StationType.HeathRobinson] = new[] { "blockf" },
        [StationType.TunnyMachine] = new[] { "blockf", "hut3" },
    };

    int Want(StationType t)
    {
        int a = g.ActIndex;
        return t switch
        {
            StationType.Registration => a >= 9 ? 4 : a >= 5 ? 3 : a >= 1 ? 2 : 1,
            // Offizier traffic (the tip) needs a second desk once it appears
            StationType.HandCipher => Campaign.OffizierFrom >= 0 && a >= Campaign.OffizierFrom ? 2 : a >= 5 ? 1 : a >= 1 ? 2 : 1,
            StationType.Translation => a >= 9 ? 5 : a >= 5 ? 4 : a >= 2 ? 2 : 1,
            StationType.Dispatch => a >= 5 ? 2 : 1,
            StationType.Index => a >= Defs.IndexFromAct ? Math.Min(Have(StationType.Translation), Have(StationType.Index) + (UnindexedTranslation().Any() ? 1 : 0)) : 0,
            StationType.Typex => a >= 5 ? 2 : a >= 2 ? 1 : 0,
            // What the traffic-analysis tip teaches: one desk beside the crib desks once there are Bombes.
            StationType.TrafficAnalysis => a >= Defs.SixtaFromAct && Have(StationType.Crib) > 0 ? 1 : 0,
            StationType.Zygalski => a is 2 or 3 ? 1 : 0,
            StationType.Crib => a >= 7 ? 2 : a >= 3 ? 1 : 0,
            StationType.Bombe => a >= 10 ? 4 : a >= 9 ? 3 : a >= 5 ? 2 : a >= 3 ? 1 : 0,
            StationType.Banburismus => a >= 9 ? 2 : a >= 7 ? 1 : 0,
            StationType.Bombe4 => a >= 11 ? 2 : 0,
            StationType.Canteen => 1 + g.Staff.Count / 12,
            StationType.CommonRoom => a >= 1 ? 1 + g.Staff.Count / 20 : 0,
            StationType.Rounders => a >= 2 ? 1 : 0,
            StationType.Checking => a >= 9 ? 2 : a >= 3 ? 1 : 0,
            StationType.OutstationLink => a >= 9 && g.OutstationBombes > 0 ? 1 : 0,
            StationType.Testery => a >= 12 ? 2 : 0,
            StationType.TunnyMachine => a >= 12 ? 1 : 0,
            StationType.Colossus => a >= 13 ? 1 : 0,
            StationType.HeathRobinson => a >= 12 && Have(StationType.Colossus) == 0 ? 1 : 0,
            _ => 0,
        };
    }

    /// <summary>
    /// Would the hand-cipher desks get through everything already promised, plus this request, before its deadline?
    /// Counts working hours only (staff aren't at their desks round the clock).
    /// </summary>
    bool HandWorkFits(Contract c)
    {
        int desks = Math.Max(1, Math.Min(Have(StationType.HandCipher), g.Staff.Count(s => s.Role == Role.Cryptanalyst && !s.Leaving)));
        double per = Defs.Station(StationType.HandCipher).Minutes;
        int owed = g.Contracts.Where(x => x.State == ContractState.Active && Defs.NetOf(x.Net).Family == Family.Hand)
                              .Sum(x => x.Quantity - x.Delivered);
        double needMinutes = (owed + c.Quantity) * per / desks * 2.2;
        return needMinutes < c.Days * Defs.MinutesPerDay * 0.85;
    }

    /// <summary>Keep good people if we can afford to; let the rest go.</summary>
    int CallUpChoice(Dilemma d)
    {
        var who = g.StaffById(d.StaffId!.Value);
        if (who == null) return 2;
        // Never lose the only person who can do a job, however junior.
        bool sole = g.Staff.Count(s => s.Role == who.Role && !s.Leaving) <= 1;
        if (who.Skill < 2 && !sole) return 2;
        if (g.Funds > Defs.CallUpRulingCost + 400) return 1;
        return g.Reputation > 45 ? 0 : 2;
    }

    /// <summary>Each Enigma network needs about one Bombe's day to break its key, so don't take on more networks than Bombes.</summary>
    bool BombeNetFits(Net net)
    {
        // Cribs and Banburismus both lead on to a Bombe run; Zygalski sheets don't.
        bool NeedsBombe(Net n) => g.KeyRoute(n) is StationType.Crib or StationType.Banburismus;
        if (!NeedsBombe(net)) return true;
        var nets = g.Contracts.Where(x => x.State == ContractState.Active && Defs.NetOf(x.Net).Family != Family.Hand)
                              .Select(x => x.Net).ToHashSet();
        int bombes = g.Stations.Count(s => s.Type is StationType.Bombe or StationType.Bombe4 && !s.PendingRemoval) + g.OutstationBombes;
        // Keep Bombe time free for networks the chapter's goals still need.
        bool IsGoal(Net n) => g.Act.Goals.Any(x => x.Net == n && !x.Done(g));
        int starved = g.Act.Goals.Where(x => x.Net is { } n && !x.Done(g) && NeedsBombe(n) && !nets.Contains(n))
                                 .Select(x => x.Net).Distinct().Count();
        int busy = nets.Count(NeedsBombe);
        if (!IsGoal(net) && starved > 0 && busy + starved >= bombes) return false;
        if (nets.Contains(net)) return true;
        return busy + (IsGoal(net) ? 0 : starved) < bombes;
    }

    static readonly StationType[] BuildOrder =
    {
        StationType.Registration, StationType.HandCipher, StationType.Translation,
        StationType.Typex, StationType.Zygalski, StationType.Crib, StationType.Bombe,
        StationType.Banburismus, StationType.Bombe4, StationType.Dispatch, StationType.Index, StationType.TrafficAnalysis,
        StationType.Checking, StationType.Canteen, StationType.CommonRoom, StationType.Rounders,
        StationType.OutstationLink, StationType.Testery, StationType.TunnyMachine, StationType.HeathRobinson, StationType.Colossus,
    };

    int Have(StationType t) => g.Stations.Count(s => s.Type == t && !s.PendingRemoval && !s.Obsolete);

    /// <summary>Whether to carry on into the Tunny chapter after winning.</summary>
    public bool ContinuePastEnigma { get; set; }

    public void Think()
    {
        if (g.Status == GameStatus.Won && ContinuePastEnigma && g.CanContinue) g.ContinueIntoTunny();
        if (g.Status != GameStatus.Playing) return;
        if (g.ActCompletePending) { g.ContinueAfterAct(); return; }

        // ULTRA: answer dilemmas, and ease off when the Germans grow suspicious.
        foreach (var d in g.Dilemmas.ToList())
        {
            int pick = d.Title == "Call-up papers" ? CallUpChoice(d)
                : d.StaffId != null ? 0
                : g.SuspicionOf(d.Service) > 50 ? d.Options.Select((o, i) => (o, i)).OrderBy(x => x.o.Suspicion).First().i
                : 1;
            g.ResolveDilemma(d.Id, pick);
        }
        // What the key-priority tip teaches: when keys queue for the Bombes, the most pressing request's network goes first.
        foreach (var n in g.KeyOrders.Keys.ToList()) g.SetKeyOrder(n, KeyOrder.Normal);
        if (g.KeysQueueing &&
            g.Contracts.Where(c => c.State == ContractState.Active && Defs.NetOf(c.Net).Family != Family.Hand && !g.KeyBroken(c.Net, g.Day))
                       .OrderBy(c => c.Due).FirstOrDefault() is { } urgent)
            g.SetKeyOrder(urgent.Net, KeyOrder.First);
        // What the rivalry message says: post one of a feuding pair to another building with work for them.
        foreach (var a in g.Staff.Where(x => x.Rival != null && x.Posted == null && g.WithRival(x)).ToList())
        {
            var rival = g.StaffById(a.Rival!.Value);
            var here = rival != null ? g.Grid.ZoneAt(rival.TilePos.x, rival.TilePos.y)?.Id : null;
            var other = g.Stations.Where(st => st.Def.Operator == a.Role && !st.Obsolete)
                .Select(st => g.Grid.ZoneAt(st.X, st.Y)?.Id).FirstOrDefault(z => z != null && z != here);
            if (other != null) g.Post(a.Id, other);
        }
        foreach (var a in g.Staff.Where(x => x.Rival == null && x.Posted != null)) g.Post(a.Id, null);
        if (g.MaxSuspicion > 60) g.SetPolicy(Policy.Cautious);
        else if (g.MaxSuspicion < 30) g.SetPolicy(Policy.Standard);
        // Stations the act's goals ask for come first; save up for them rather than spend elsewhere.
        bool saving = false;
        foreach (var goal in g.Act.Goals.Where(x => x.Kind == GoalKind.Build && !x.Done(g) && x.Station != null))
        {
            var gt = goal.Station!.Value;
            if (!g.StationUnlocked(gt, out _)) continue;
            // Keep half a week's wages in hand: an overdraft costs reputation every day.
            if (g.Funds >= Defs.Station(gt).Cost + (g.WeeklyWages + g.WeeklyUpkeep) / 2) TryBuild(gt);
            else saving = true;
        }
        if (!saving)
            foreach (var n in new[] { Net.Dolphin, Net.Shark })
                if (g.Funds > 3000 && g.CanGarden(n) == null && g.Day % 3 == 0) g.Garden(n);

        // Research: whatever the act asks for first, then anything affordable.
        foreach (var goal in g.Act.Goals.Where(x => x.Kind == GoalKind.Research))
            g.DoResearch(goal.Research!.Value);
        foreach (var r in g.ResearchAvailable.OrderBy(r => Defs.Research[r].Cost).ToList())
            g.DoResearch(r);

        // Huts
        foreach (var z in g.Grid.Zones.Where(z => z.Available && !z.Unlocked))
            if (g.Funds > z.Cost + 300) g.UnlockZone(z.Id);

        // Stations
        int reserve = g.WeeklyWages + g.WeeklyUpkeep;
        foreach (var t in BuildOrder)
        {
            if (Have(t) >= Want(t)) continue;
            if (!g.StationUnlocked(t, out _)) continue;
            if (g.Funds < Defs.Station(t).Cost + (Have(t) == 0 ? 0 : reserve)) continue;
            TryBuild(t);
        }
        // Pneumatic tubes in every open hut, once researched.
        if (g.StationUnlocked(StationType.Tube, out _) && g.Funds > 1500)
            foreach (var z in g.Grid.Zones.Where(z => z.Unlocked))
                if (!g.Stations.Any(s => s.Type == StationType.Tube && z.InOuter(s.X, s.Y)) && g.Funds > 1500)
                    TryBuildIn(StationType.Tube, z);
        // Outstation Bombes when rich.
        if (!saving && g.CanBuyOutstation() == null && g.OutstationBombes < 3 && g.Funds > 4500) g.BuyOutstation();

        // A stove in every working timber hut before winter.
        if (g.Date.Month is >= 10 or <= 3)
            foreach (var z in g.Grid.Zones.Where(z => z.Unlocked && !Defs.Heated(z.Id)))
                if (g.Stations.Any(s => z.InOuter(s.X, s.Y)) && !g.Stations.Any(s => s.Type == StationType.Stove && z.InOuter(s.X, s.Y))
                    && g.Funds > Defs.Station(StationType.Stove).Cost + 50)
                    TryBuildIn(StationType.Stove, z);

        // A sick bay once flu season comes.
        if (g.Winter && !g.HasSickBay && g.StationUnlocked(StationType.SickBay, out _) && g.Funds > 1200)
            TryBuildIn(StationType.SickBay, g.Grid.Zone("mansion")!);

        // Stores, as the tip says: order when they're down to a couple of days, before they run out.
        if ((g.StoresLow || g.OutOfStores) && g.CanOrderStores() == null)
            g.OrderStores();

        // All hands when a request is about to be missed.
        if (g.CanCallAllHands() == null && g.Contracts.Any(c => c.State == ContractState.Active && c.Due - g.Now < 8 * 60 && c.Quantity - c.Delivered >= 2))
            g.CallAllHands();

        // Heads for the busy huts, from cryptanalysts the desks can spare.
        if (g.ActIndex >= 3)
            foreach (var zid in new[] { "hut6", "hut8" })
            {
                if (g.Grid.Zone(zid) is not { Unlocked: true } || g.HeadOfZone(zid) != null) continue;
                int desks = g.Stations.Count(st => st.Def.Operator == Role.Cryptanalyst && !st.PendingRemoval);
                var crypts = g.Staff.Where(x => x.Role == Role.Cryptanalyst && !x.Leaving && x.HeadOf == null).ToList();
                // With three watches every desk needs someone on each of them.
                if (crypts.Count <= desks * (g.WatchesAvailable ? 3 : 1) + 1) break;
                var pick = crypts.Where(GameState.CanHead).OrderBy(x => x.Skill).FirstOrDefault();
                if (pick != null) g.AppointHead(pick.Id, zid);
            }

        // Billets ahead of hiring.
        if (g.Staff.Count >= g.Billets - 2 && g.Funds > Defs.BilletCost + 400) g.RequisitionBillets();

        // Service worn machines while they're between runs.
        foreach (var st in g.Stations.Where(s => s.Wears && s.Wear > 0.55 && s.Current == null).ToList())
            if (g.Funds > Defs.ServiceCost + 100) g.ServiceMachine(st.Id);

        // Retire obsolete sheet tables.
        foreach (var st in g.Stations.Where(s => s.Obsolete && !s.PendingRemoval).ToList())
            g.RequestRemove(st.Id);

        // Staff. Round-the-clock watches need more people than long days.
        double cover = g.WatchesAvailable ? Math.Min(3.0, 1.4 + 0.18 * g.ActIndex) : 1.0;
        int Need(double n) => (int)Math.Ceiling(n * cover);
        var want = new Dictionary<Role, int>
        {
            // Messengers do the running, so clerks only need to cover the registration and despatch desks.
            [Role.Clerk] = Need(Have(StationType.Registration) + Have(StationType.Dispatch) + Have(StationType.Index) + Have(StationType.TrafficAnalysis) + (g.ActIndex < 1 ? 1 : 0)),
            [Role.Messenger] = g.ActIndex < 1 ? 0 : Need(g.Grid.Zones.Count(z => z.Unlocked) + g.ActIndex / 3),
            [Role.Cryptanalyst] = Need(Math.Max(Have(StationType.HandCipher), Want(StationType.HandCipher)) + Have(StationType.Crib) + Have(StationType.Zygalski) + Have(StationType.Banburismus) + Have(StationType.Testery)),
            [Role.Wren] = Need(Have(StationType.Bombe) + Have(StationType.Bombe4) + Have(StationType.Typex) + Have(StationType.Checking) + Have(StationType.HeathRobinson)
                               + Have(StationType.OutstationLink) + Have(StationType.Colossus) + Have(StationType.TunnyMachine)),
            [Role.Linguist] = Need(Have(StationType.Translation)),
        };
        if (g.WatchesAvailable)
            foreach (var r in want.Keys.ToList())
                if (want[r] > 0) want[r] = Math.Max(3, want[r]);   // one per watch at least
        int before = g.Staff.Count;
        foreach (var (role, n) in want)
        {
            int have = g.Staff.Count(s => s.Role == role && !s.Leaving);
            while (have < n)
            {
                var c = g.Candidates.Where(c => c.Role == role).OrderByDescending(c => c.Skill).FirstOrDefault();
                if (c == null || g.Funds < c.Salary + 200 || !g.Hire(c.Id)) break;
                have++;
            }
        }
        // Staff-count goals
        foreach (var goal in g.Act.Goals.Where(x => x.Kind == GoalKind.StaffCount && !x.Done(g)))
        {
            var c = g.Candidates.OrderBy(c => c.Salary).FirstOrDefault();
            if (c != null && g.Funds > c.Salary + 500) g.Hire(c.Id);
        }
        // Notables are always worth it.
        foreach (var c in g.Candidates.Where(c => c.Notable != null).ToList())
            if (g.Funds > c.Salary + 300) g.Hire(c.Id);
        if (g.WatchesAvailable && (g.Staff.Count != before || g.Staff.Any(s => s.Shift == Shift.Office))) g.BalanceWatches(null);

        // Requests: keep a sensible workload, and only take ones we can deliver.
        int active = g.Contracts.Count(c => c.State == ContractState.Active);
        if (g.Contracts.Any(c => c.State == ContractState.Active && c.Tag != null)) active += 2;   // focus on the urgent one
        int capacity = 2 + Have(StationType.Bombe) + Have(StationType.Bombe4) + Have(StationType.HandCipher) / 2;
        // Flash requests: only if today's key is already out (or it's a hand cipher and a desk is free); otherwise say so.
        foreach (var f in g.Contracts.Where(c => c.Flash && c.State == ContractState.Offered).ToList())
        {
            var fn = Defs.NetOf(f.Net);
            bool ready = fn.Family == Family.Hand
                ? g.Stations.Any(s => s.Type == StationType.HandCipher && s.Input.Count == 0 && s.Current == null)
                : Have(StationType.Typex) > 0 && g.KeyBroken(f.Net, g.Day) && g.MinuteOfDay < 16 * 60;
            if (ready) g.Accept(f.Id); else g.Decline(f.Id);
        }
        foreach (var c in g.Contracts.Where(c => c.State == ContractState.Offered && !c.Flash).OrderByDescending(c => c.Reward).ToList())
        {
            if (active >= capacity) break;
            var nd = Defs.NetOf(c.Net);
            if (nd.Family != Family.Hand &&
                g.Contracts.Count(x => x.State == ContractState.Active && x.Net == c.Net) >= 2) continue;
            if (g.LockedDown(c.Net) || g.SuspicionOf(nd.Service) > 80) continue;
            bool can = nd.Family == Family.Hand ? Have(StationType.HandCipher) > 0
                : (g.KeyRoute(c.Net) != null || g.KeyBroken(c.Net, g.Day)) && Have(Defs.DecryptStep(c.Net)) > 0;
            if (!can) continue;
            if (nd.Family == Family.Hand && !HandWorkFits(c)) continue;
            if (nd.Family != Family.Hand && !BombeNetFits(c.Net)) continue;
            if (g.Accept(c.Id)) active++;
        }
    }

    public int BuildFails, Builds;
    public bool TryBuild(StationType t)
    {
        bool ok = TryBuildInner(t);
        if (ok) Builds++; else BuildFails++;
        return ok;
    }

    /// <summary>Rooms with a translation desk but no card index yet: each wants one beside it.</summary>
    IEnumerable<Station> UnindexedTranslation() =>
        g.Stations.Where(tr => tr.Type == StationType.Translation &&
            !g.Stations.Any(ix => ix.Type == StationType.Index && g.Grid.RoomAt(ix.UseTile.x, ix.UseTile.y) == g.Grid.RoomAt(tr.UseTile.x, tr.UseTile.y)));

    /// <summary>The closest legal spot to another station, in the same room.</summary>
    bool TryBuildBeside(StationType t, Station anchor)
    {
        var zone = g.Grid.ZoneAt(anchor.UseTile.x, anchor.UseTile.y);
        if (zone == null || !Defs.AllowedIn(zone.Id, t)) return false;
        var room = g.Grid.RoomAt(anchor.UseTile.x, anchor.UseTile.y);
        (int x, int y, int r)? best = null;
        int bestD = int.MaxValue;
        for (int y = zone.Y + 1; y < zone.Y + zone.H - 1; y++)
            for (int x = zone.X + 1; x < zone.X + zone.W - 1; x++)
                foreach (int rot in new[] { 0, 2, 1, 3 })
                {
                    var use = Station.UseTileFor(t, x, y, rot);
                    if (g.Grid.RoomAt(use.x, use.y) != room || g.CanPlace(t, x, y, rot) != null) continue;
                    int d = Math.Abs(use.x - anchor.UseTile.x) + Math.Abs(use.y - anchor.UseTile.y);
                    if (d < bestD) { bestD = d; best = (x, y, rot); }
                }
        return best is var (bx, by, br) && g.Place(t, bx, by, br) != null;
    }

    bool TryBuildInner(StationType t)
    {
        if (t == StationType.Index)
        {
            foreach (var tr in UnindexedTranslation())
                if (TryBuildBeside(t, tr)) return true;
            if (g.Stations.Any(x => x.Type == StationType.Index)) return false;   // no room beside any translator: make do
        }
        if (Defs.Station(t).Outdoor) return TryBuildOutdoor(t);
        var zones = PreferredZones.TryGetValue(t, out var z) ? z : new[] { "mansion" };
        // Index and despatch go where the translations are, so paper doesn't cross the park and back.
        if (t is StationType.Index or StationType.Dispatch)
        {
            var near = Defs.FlowNeighbours(t);
            zones = zones.OrderByDescending(id => g.Stations.Count(st => near.Contains(st.Type) && g.Grid.ZoneAt(st.X, st.Y)?.Id == id)).ToArray();
        }
        foreach (var zid in zones.Concat(g.Grid.Zones.Select(x => x.Id)).Distinct().Where(id => Defs.AllowedIn(id, t)))
        {
            var zone = g.Grid.Zone(zid);
            if (zone == null || !zone.Unlocked) continue;
            // Rows of desks leaving aisles, and as close as possible to the desks it trades paper with,
            // so work can be handed across rather than carried.
            var near = Defs.FlowNeighbours(t);
            var mates = g.Stations.Where(s => near.Contains(s.Type) && g.Grid.ZoneAt(s.UseTile.x, s.UseTile.y) == zone).Select(s => s.UseTile).ToList();
            (int x, int y, int rot)? best = null;
            int bestScore = int.MaxValue;
            for (int y = zone.Y + 1; y < zone.Y + zone.H - 1; y++)
                for (int x = zone.X + 1; x < zone.X + zone.W - 1; x++)
                    foreach (int rot in new[] { 0, 2, 1, 3 })
                    {
                        if (g.CanPlace(t, x, y, rot) != null) continue;
                        var use = Station.UseTileFor(t, x, y, rot);
                        int score = ((y - zone.Y) % 3 == 1 ? 0 : 6) + (mates.Count == 0 ? (y - zone.Y) + (x - zone.X) / 4 : mates.Min(m => Math.Abs(m.x - use.x) + Math.Abs(m.y - use.y)) * 2);
                        if (score < bestScore) { bestScore = score; best = (x, y, rot); }
                    }
            if (best is var (bx, by, br)) return g.Place(t, bx, by, br) != null;
        }
        return false;
    }

    bool TryBuildIn(StationType t, Zone zone)
    {
        for (int y = zone.Y + 1; y < zone.Y + zone.H - 1; y++)
            for (int x = zone.X + 1; x < zone.X + zone.W - 1; x++)
                foreach (int rot in new[] { 0, 2, 1, 3 })
                    if (g.CanPlace(t, x, y, rot) == null)
                        return g.Place(t, x, y, rot) != null;
        return false;
    }

    bool TryBuildOutdoor(StationType t)
    {
        var spots = Enumerable.Range(0, Grid.Width * Grid.Height)
            .Select(i => (x: i % Grid.Width, y: i / Grid.Width))
            .OrderBy(p => Math.Abs(p.x - 12) + Math.Abs(p.y - 15));
        foreach (var (x, y) in spots)
            if (g.CanPlace(t, x, y, 0) == null) return g.Place(t, x, y, 0) != null;
        return false;
    }

    /// <summary>Plays until the campaign ends or the day limit. Returns the day each act finished on.</summary>
    public List<(string act, int day)> PlayCampaign(int maxDays, Action<string>? log = null)
    {
        var finished = new List<(string, int)>();
        int lastAct = -1;
        while ((g.Status == GameStatus.Playing || (ContinuePastEnigma && g.CanContinue)) && g.Day < maxDays)
        {
            if (g.ActCompletePending)
            {
                finished.Add((g.Act.Title, g.Day));
                log?.Invoke($"day {g.Day,3}: finished '{g.Act.Title}' funds £{g.Funds} rep {g.Reputation:0} staff {g.Staff.Count} stations {g.Stations.Count}");
            }
            if (g.ActIndex != lastAct) lastAct = g.ActIndex;
            Think();
            g.Advance(60);
            g.Events.Clear();
        }
        return finished;
    }
}
