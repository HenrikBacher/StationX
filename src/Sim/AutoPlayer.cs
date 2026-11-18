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
        [StationType.TunnyMachine] = new[] { "blockf", "hut3" },
    };

    int Want(StationType t)
    {
        int a = g.ActIndex;
        return t switch
        {
            StationType.Registration => a >= 9 ? 4 : a >= 5 ? 3 : a >= 1 ? 2 : 1,
            StationType.HandCipher => a >= 5 ? 1 : a >= 1 ? 2 : 1,
            StationType.Translation => a >= 9 ? 5 : a >= 5 ? 4 : a >= 2 ? 2 : 1,
            StationType.Dispatch => a >= 5 ? 2 : 1,
            StationType.Typex => a >= 5 ? 2 : a >= 2 ? 1 : 0,
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

    static readonly StationType[] BuildOrder =
    {
        StationType.Registration, StationType.HandCipher, StationType.Translation,
        StationType.Typex, StationType.Zygalski, StationType.Crib, StationType.Bombe,
        StationType.Banburismus, StationType.Bombe4, StationType.Dispatch,
        StationType.Checking, StationType.Canteen, StationType.CommonRoom, StationType.Rounders,
        StationType.OutstationLink, StationType.Testery, StationType.TunnyMachine, StationType.Colossus,
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
            int pick = d.StaffId != null ? 0
                : g.SuspicionOf(d.Service) > 50 ? d.Options.Select((o, i) => (o, i)).OrderBy(x => x.o.Suspicion).First().i
                : 1;
            g.ResolveDilemma(d.Id, pick);
        }
        if (g.MaxSuspicion > 60) g.SetPolicy(Policy.Cautious);
        else if (g.MaxSuspicion < 30) g.SetPolicy(Policy.Standard);
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
        if (g.CanBuyOutstation() == null && g.OutstationBombes < 3 && g.Funds > 4500) g.BuyOutstation();

        // Service worn machines while they're between runs.
        foreach (var st in g.Stations.Where(s => s.Wears && s.Wear > 0.55 && s.Current == null).ToList())
            if (g.Funds > Defs.ServiceCost + 100) g.ServiceMachine(st.Id);

        // Retire obsolete sheet tables.
        foreach (var st in g.Stations.Where(s => s.Obsolete && !s.PendingRemoval).ToList())
            g.RequestRemove(st.Id);

        // Staff. Round-the-clock watches need more people than long days.
        double cover = g.WatchesAvailable ? 1.4 : 1.0;
        int Need(double n) => (int)Math.Ceiling(n * cover);
        var want = new Dictionary<Role, int>
        {
            // Messengers do the running, so clerks only need to cover the registration and despatch desks.
            [Role.Clerk] = Need(Have(StationType.Registration) + Have(StationType.Dispatch) + (g.ActIndex < 1 ? 1 : 0)),
            [Role.Messenger] = g.ActIndex < 1 ? 0 : Need(g.Grid.Zones.Count(z => z.Unlocked) + g.ActIndex / 3),
            [Role.Cryptanalyst] = Need(Math.Max(Have(StationType.HandCipher), Want(StationType.HandCipher)) + Have(StationType.Crib) + Have(StationType.Zygalski) + Have(StationType.Banburismus) + Have(StationType.Testery)),
            [Role.Wren] = Need(Have(StationType.Bombe) + Have(StationType.Bombe4) + Have(StationType.Typex) + Have(StationType.Checking)
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
        foreach (var c in g.Contracts.Where(c => c.State == ContractState.Offered).OrderByDescending(c => c.Reward).ToList())
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
            if (g.Accept(c.Id)) active++;
        }
    }

    public bool TryBuild(StationType t)
    {
        if (Defs.Station(t).Outdoor) return TryBuildOutdoor(t);
        var zones = PreferredZones.TryGetValue(t, out var z) ? z : new[] { "mansion" };
        foreach (var zid in zones.Concat(g.Grid.Zones.Select(x => x.Id)).Distinct().Where(id => Defs.AllowedIn(id, t)))
        {
            var zone = g.Grid.Zone(zid);
            if (zone == null || !zone.Unlocked) continue;
            // Rows of desks facing the camera, leaving aisles: try even rows first.
            for (int pass = 0; pass < 2; pass++)
                for (int y = zone.Y + 1; y < zone.Y + zone.H - 1; y++)
                {
                    if (pass == 0 && (y - zone.Y) % 3 != 1) continue;
                    for (int x = zone.X + 1; x < zone.X + zone.W - 1; x++)
                        foreach (int rot in new[] { 0, 2, 1, 3 })
                            if (g.CanPlace(t, x, y, rot) == null)
                                return g.Place(t, x, y, rot) != null;
                }
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
