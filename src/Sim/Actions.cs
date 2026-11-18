using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>Everything the player can do. The UI and the autoplayer both go through here.</summary>
public sealed partial class GameState
{
    // ------------------------------------------------------------- building

    public bool StationUnlocked(StationType t, out string reason)
    {
        var d = Defs.Station(t);
        if (ActIndex < d.MinAct) { reason = "Not needed yet"; return false; }
        if (d.Requires is ResearchId r && !Researched.Contains(r))
        {
            reason = $"Research {Defs.Research[r].Name}";
            return false;
        }
        if (t == StationType.Zygalski && IndicatorChanged) { reason = "Obsolete since 1 May 1940"; return false; }
        reason = "";
        return true;
    }

    public string? CanPlace(StationType t, int x, int y, int rot)
    {
        if (!StationUnlocked(t, out var why)) return why;
        return CheckPlacement(t, x, y, rot, null) ?? (Funds < Defs.Station(t).Cost ? $"Needs £{Defs.Station(t).Cost}" : null);
    }

    public string? CanMove(int stationId, int x, int y, int rot)
    {
        var st = StationById(stationId);
        if (st == null) return "Gone";
        if (st.PendingRemoval) return "Being removed";
        return CheckPlacement(st.Type, x, y, rot, st) ?? (Funds < Defs.MoveCost ? $"Needs £{Defs.MoveCost}" : null);
    }

    /// <summary>"Belongs in Hut 6 or Hut 8", naming the buildings that take this station.</summary>
    string BelongsIn(StationType t)
    {
        var names = Defs.ZonesFor(t).Select(id => Grid.Zone(id)).Where(z => z != null).Select(z => z!.Name).ToList();
        if (names.Count == 0) return "Can't go indoors";
        string list = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1];
        bool anyOpen = Defs.ZonesFor(t).Any(id => Grid.Zone(id)?.Unlocked == true);
        return $"Belongs in {list}" + (anyOpen ? "" : " (not open yet)");
    }

    /// <summary>Shared placement rules. <paramref name="moving"/> is ignored as an obstacle.</summary>
    string? CheckPlacement(StationType t, int x, int y, int rot, Station? moving)
    {
        var def = Defs.Station(t);
        var (w, h) = Defs.Dims(t, rot);
        if (moving != null)
            foreach (var (fx, fy) in moving.Footprint()) Grid.Occupant[fy * Grid.Width + fx] = -1;
        try
        {
            Zone? zone = null;
            var occupiedByStaff = new HashSet<(int, int)>(Staff.Where(s => !s.Away).Select(s => s.TilePos));
            var useTiles = new HashSet<(int, int)>(Stations.Where(s => s != moving).Select(s => s.UseTile));
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    int tx = x + dx, ty = y + dy;
                    if (!Grid.InBounds(tx, ty)) return "Off the map";
                    var z = Grid.ZoneAt(tx, ty);
                    if (def.Outdoor)
                    {
                        if (z != null || Grid.Get(tx, ty) != Tile.Lawn) return "Needs open lawn";
                    }
                    else
                    {
                        if (z == null || !z.InInterior(tx, ty)) return "Must be built inside a hut";
                        if (!z.Unlocked) return $"{z.Name} isn't open yet";
                        if (zone != null && z != zone) return "Must be inside one hut";
                        if (zone == null && !Defs.AllowedIn(z.Id, t)) return BelongsIn(t);
                        zone = z;
                        if (z.InsideDoor == (tx, ty)) return "Keep the doorway clear";
                    }
                    if (Grid.OccupantAt(tx, ty) >= 0) return "Something is already there";
                    if (useTiles.Contains((tx, ty))) return "Blocks another station's working space";
                    if (occupiedByStaff.Contains((tx, ty))) return "Someone is standing there";
                }
            var use = Station.UseTileFor(t, x, y, rot);
            if (!def.Outdoor && (zone == null || !zone.InInterior(use.x, use.y))) return "Needs working space in front, inside the hut";
            if (!Grid.Walkable(use.x, use.y)) return "Needs clear working space in front";

            // Everything must stay reachable after placing.
            var fake = new Station { Id = int.MaxValue, Type = t, X = x, Y = y, Rot = rot };
            foreach (var (fx, fy) in fake.Footprint()) Grid.Occupant[fy * Grid.Width + fx] = fake.Id;
            try
            {
                var reach = Pathfinder.Reachable(Grid, Grid.Gate);
                bool ok(int tx, int ty) => reach[ty * Grid.Width + tx];
                if (!ok(use.x, use.y)) return "Working space can't be reached";
                foreach (var s in Stations)
                    if (s != moving && !ok(s.UseTile.x, s.UseTile.y)) return "Would block access to another station";
                foreach (var z in Grid.Zones)
                    if (z.Unlocked && !ok(z.InsideDoor.x, z.InsideDoor.y)) return "Would block a doorway";
            }
            finally
            {
                foreach (var (fx, fy) in fake.Footprint()) Grid.Occupant[fy * Grid.Width + fx] = -1;
            }
            return null;
        }
        finally
        {
            Grid.RebuildOccupancy(Stations);
        }
    }

    public Station? Place(StationType t, int x, int y, int rot, bool free = false)
    {
        if (!free && CanPlace(t, x, y, rot) != null) return null;
        var st = new Station { Id = NextId++, Type = t, X = x, Y = y, Rot = rot & 3 };
        Stations.Add(st);
        Grid.RebuildOccupancy(Stations);
        if (!free) Funds -= st.Def.Cost;
        return st;
    }

    /// <summary>Picks a station up and puts it down elsewhere. Its trays go with it.</summary>
    public bool Move(int stationId, int x, int y, int rot)
    {
        if (CanMove(stationId, x, y, rot) != null) return false;
        var st = StationById(stationId)!;
        ReleaseOperator(st);
        foreach (var s in Staff.Where(s => s.Task is { Kind: TaskKind.Rest } t && t.Dest == st.Id)) { s.Task = null; s.Path.Clear(); }
        st.X = x; st.Y = y; st.Rot = rot & 3;
        Grid.RebuildOccupancy(Stations);
        Funds -= Defs.MoveCost;
        // Anyone walking to it re-plans on arrival at the old spot.
        return true;
    }

    /// <summary>Marks a station for removal; it goes once its trays are empty.</summary>
    public void RequestRemove(int stationId)
    {
        var st = StationById(stationId);
        if (st == null) return;
        st.PendingRemoval = !st.PendingRemoval;
    }

    public bool UnlockZone(string id)
    {
        var z = Grid.Zone(id);
        if (z == null || z.Unlocked || !z.Available || Funds < z.Cost) return false;
        Funds -= z.Cost;
        z.Unlocked = true;
        Emit(EventKind.Good, $"{z.Name} is open. Its floor is ready for desks and machines.");
        return true;
    }

    // ------------------------------------------------------------- staff

    public Staff NewPerson(Role role, int skill)
    {
        bool female = role == Role.Wren || Rng.Chance(role switch { Role.Linguist => 0.6, Role.Messenger => 0.3, _ => 0.45 });
        var first = female ? Rng.Pick(Defs.FemaleFirst) : Rng.Pick(Defs.MaleFirst);
        var last = Rng.Pick(Defs.Surnames);
        int baseSalary = Defs.Roles[role].Salary;
        var traits = new List<Trait>();
        if (Rng.Chance(0.07)) traits.Add(Trait.CarelessTalker);
        if (Rng.Chance(0.3))
        {
            var options = new List<Trait> { Trait.NightOwl, Trait.Tireless, Trait.Meticulous };
            if (role == Role.Cryptanalyst) { options.Add(Trait.CrosswordFiend); options.Add(Trait.ChessPlayer); }
            if (role == Role.Linguist) { options.Add(Trait.NativeGerman); options.Add(Trait.NativeGerman); }
            traits.Add(Rng.Pick(options));
        }
        return new Staff
        {
            Traits = traits,
            Energy = 1,
            Id = NextId++,
            Name = $"{first} {last}",
            Role = role,
            Skill = skill,
            Female = female,
            Look = Rng.Range(0, 1000),
            Salary = baseSalary + (skill - 1) * baseSalary / 3,
        };
    }

    void RefreshCandidates()
    {
        Candidates.RemoveAll(c => c.Notable == null);
        var roles = new List<Role> { Role.Clerk, Role.Cryptanalyst, Role.Linguist, Role.Clerk };
        if (ActIndex >= 1) roles.Add(Role.Messenger);
        if (ActIndex >= 2) { roles.Add(Role.Wren); roles.Add(Role.Wren); }
        if (ActIndex >= 5) roles.Add(Role.Messenger);
        foreach (var r in roles)
        {
            int skill = Rng.Chance(0.15) ? 3 : Rng.Chance(0.4) ? 2 : 1;
            Candidates.Add(NewPerson(r, skill));
        }
    }

    public bool Hire(int candidateId)
    {
        var c = Candidates.Find(x => x.Id == candidateId);
        if (c == null || Funds < c.Salary) return false;
        Funds -= c.Salary;
        Candidates.Remove(c);
        c.X = Grid.Gate.x + 0.5;
        c.Y = Grid.Gate.y + 0.5;
        c.HomeX = c.X; c.HomeY = c.Y;
        Staff.Add(c);
        Emit(EventKind.Info, $"{c.Name} signs the Official Secrets Act and reports for duty.");
        return true;
    }

    public void Fire(int staffId)
    {
        var s = StaffById(staffId);
        if (s == null) return;
        s.Leaving = true;
        if (s.Task == null) RemoveStaff(s);
    }

    void RemoveStaff(Staff s)
    {
        foreach (var st in Stations.Where(st => st.OperatorId == s.Id))
        {
            st.OperatorId = null;
            st.OperatorPresent = false;
        }
        Staff.Remove(s);
        Emit(EventKind.Info, $"{s.Name} has left Bletchley Park.");
    }

    void WeeklyAccounts()
    {
        int grant = WeeklyGrant, wages = WeeklyWages, upkeep = WeeklyUpkeep;
        Funds += grant - wages - upkeep;
        Emit(grant >= wages + upkeep ? EventKind.Info : EventKind.Bad,
            $"Weekly accounts: Treasury grant £{grant}, wages £{wages}, upkeep £{upkeep}." +
            (DailyExpectation > 0 ? " Whitehall expects more every month; reputation slips unless you keep delivering." : ""));
    }

    public int WeeklyWages => Staff.Sum(s => s.Salary);
    /// <summary>Reputation lost each morning, before difficulty: what have you done for us lately?</summary>
    public double DailyExpectation => ActIndex < 1 ? 0 : Defs.ExpectationBase + Defs.ExpectationPerAct * ActIndex;
    public int WeeklyUpkeep => Stations.Sum(s => s.Def.Upkeep) + OutstationBombes * Defs.OutstationUpkeep;
    public int WeeklyGrant => (int)((Defs.GrantBase + Defs.GrantPerRep * Reputation) * Defs.Difficulties[Difficulty].GrantMult);

    // ------------------------------------------------------------- research

    public bool DoResearch(ResearchId id)
    {
        var r = Defs.Research[id];
        if (Researched.Contains(id) || !ResearchAvailable.Contains(id) || Points < r.Cost) return false;
        Points -= r.Cost;
        Researched.Add(id);
        Emit(EventKind.Good, $"Breakthrough: {r.Name}. {r.Blurb}");
        return true;
    }

    // ------------------------------------------------------------- contracts

    public bool Accept(int contractId)
    {
        var c = ContractById(contractId);
        if (c == null || c.State != ContractState.Offered) return false;
        Activate(c);
        return true;
    }

    public void Decline(int contractId)
    {
        var c = ContractById(contractId);
        if (c != null && c.State == ContractState.Offered) c.State = ContractState.Expired;
    }

    void Activate(Contract c)
    {
        c.State = ContractState.Active;
        c.AcceptedAt = Now;
        c.Due = Now + c.Days * Defs.MinutesPerDay;
        double window = c.Days * Defs.MinutesPerDay * 0.6;
        c.SpawnTimes = Enumerable.Range(0, c.Quantity)
            .Select(_ => Now + 20 + Rng.NextDouble() * window)
            .OrderBy(t => t).ToList();
    }

    public Contract MakeContract(ContractTemplate tpl)
    {
        var net = Defs.NetOf(tpl.Net);
        int qty = Rng.Range(tpl.MinQty, tpl.MaxQty + 1);
        double days = Math.Max(2, Rng.Range(tpl.MinDays, tpl.MaxDays + 1) + Defs.Difficulties[Difficulty].ExtraDays);
        var c = new Contract
        {
            Id = NextId++,
            Issuer = net.Issuer,
            Net = tpl.Net,
            Quantity = qty,
            Days = days,
            Reward = 80 + qty * net.PayPerMsg,
            Rep = 2 + qty / 6,
            OfferedAt = Now,
            OfferExpires = Now + Defs.OfferLifetimeDays * Defs.MinutesPerDay,
            Title = $"{qty} {net.Name} decrypts",
            Flavour = Rng.Pick(Flavours[net.Issuer]),
            State = ContractState.Offered,
        };
        return c;
    }

    static readonly Dictionary<Issuer, string[]> Flavours = new()
    {
        [Issuer.WarOffice] = new[]
        {
            "MI8 wants unit identifications and movement orders.",
            "The General Staff asks for order-of-battle details.",
            "Supply returns would tell us what the enemy is short of.",
        },
        [Issuer.AirMinistry] = new[]
        {
            "Fighter Command needs warning of tomorrow's raids.",
            "Air Intelligence asks for squadron strengths and airfields.",
            "Bomber Command wants to know where the night fighters are.",
        },
        [Issuer.Admiralty] = new[]
        {
            "The Operational Intelligence Centre needs U-boat positions.",
            "The Trade Division is routing a convoy and needs to know where the wolf packs are.",
            "The Home Fleet asks about surface raiders leaving Norway.",
        },
        [Issuer.SIS] = new[]
        {
            "Section V wants to read the Abwehr's agent traffic.",
            "SIS needs to know which of 'our' agents the Germans think they run.",
            "The Twenty Committee is checking that its double agents are believed.",
        },
    };

    public Contract AddScripted(Net net, int qty, double days, int reward, int rep, string tag, string title, string flavour)
    {
        var c = new Contract
        {
            Id = NextId++,
            Issuer = Defs.NetOf(net).Issuer,
            Net = net,
            Quantity = qty,
            Days = days,
            Reward = reward,
            Rep = rep,
            Tag = tag,
            Title = title,
            Flavour = flavour,
            OfferedAt = Now,
        };
        Contracts.Add(c);
        if (Mode == GameMode.FreePlay)
        {
            // Free play has no must-do requests: it's offered like any other.
            c.Tag = null;
            c.State = ContractState.Offered;
            c.OfferExpires = Now + 2 * Defs.MinutesPerDay;
            Emit(EventKind.Info, $"New request from the {Defs.IssuerNames[c.Issuer]}: {title}.");
            return c;
        }
        Activate(c);
        Emit(EventKind.Info, $"MOST IMMEDIATE from the {Defs.IssuerNames[c.Issuer]}: {title}.");
        return c;
    }

    void TickContracts()
    {
        // Offers
        if (Now >= NextOfferAt)
        {
            NextOfferAt = Now + Act.OfferEveryHours * 60 * Rng.Range(0.7, 1.3);
            int offers = Contracts.Count(c => c.State == ContractState.Offered);
            var pool = Act.Pool.Where(p => p.Requires == null || Researched.Contains(p.Requires.Value)).ToList();
            if (offers < Defs.MaxOffers && pool.Count > 0)
            {
                double total = pool.Sum(p => p.Weight), r = Rng.NextDouble() * total;
                var tpl = pool[^1];
                foreach (var p in pool) { r -= p.Weight; if (r <= 0) { tpl = p; break; } }
                var c = MakeContract(tpl);
                Contracts.Add(c);
                Emit(EventKind.Info, $"New request from the {Defs.IssuerNames[c.Issuer]}: {c.Title}.");
            }
        }

        Station? inbox = null;
        List<Contract>? retries = null;
        foreach (var c in Contracts)
        {
            switch (c.State)
            {
                case ContractState.Offered when Now > c.OfferExpires:
                    c.State = ContractState.Expired;
                    break;
                case ContractState.Active:
                    while (c.Spawned < c.Quantity && c.SpawnTimes[c.Spawned] <= Now)
                    {
                        inbox ??= Stations.Where(s => s.Type == StationType.Inbox && !s.PendingRemoval)
                            .OrderBy(s => s.Output.Count).FirstOrDefault();
                        if (inbox == null) break;
                        double hour = MinuteOfDay / 60;
                        var m = new Message
                        {
                            Id = NextId++, Net = c.Net, ContractId = c.Id, Day = Day, Created = Now, Since = Now,
                            Loc = Loc.Output, LocId = inbox.Id,
                            Garbled = ActIndex >= 1 && Rng.Chance(Defs.GarbledChance * (hour < 6 || hour >= 20 ? 1.6 : 1)),
                        };
                        Messages[m.Id] = m;
                        inbox.Output.Add(m.Id);
                        c.Spawned++;
                    }
                    if (c.Delivered >= c.Quantity)
                    {
                        c.State = ContractState.Completed;
                        var pol = Defs.Policies[Policy];
                        int reward = (int)(c.Reward * pol.Reward);
                        Funds += reward;
                        AdjustRep(c.Rep * pol.Rep);
                        ChangeMorale(0.02);
                        Stats.ContractsCompleted++;
                        if (c.Tag != null) Stats.ScriptedDone.Add(c.Tag);
                        Emit(EventKind.Good, $"{Defs.IssuerNames[c.Issuer]} request complete: {c.Title}. +£{reward}");
                    }
                    else if (Now > c.Due)
                    {
                        c.State = ContractState.Failed;
                        Stats.ContractsFailed++;
                        AdjustRep(-(4 + c.Rep));
                        ChangeMorale(-0.04);
                        Emit(EventKind.Bad, $"Missed deadline: {c.Title} for the {Defs.IssuerNames[c.Issuer]}. Reputation falls.");
                        if (c.Tag != null) (retries ??= new()).Add(c);
                    }
                    break;
            }
        }
        Contracts.RemoveAll(c => c.State == ContractState.Expired ||
                                 (c.State is ContractState.Completed or ContractState.Failed && Now - c.Due > 2 * Defs.MinutesPerDay));

        // A scripted request that fails comes back: the war doesn't wait, and the act needs it.
        if (retries != null)
            foreach (var f in retries)
                AddScripted(f.Net, f.Quantity, f.Days, f.Reward, f.Rep, f.Tag!, f.Title,
                    "They are trying again with the next day's traffic. Don't miss it twice.");
    }
}
