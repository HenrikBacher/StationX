using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

public enum OpKind { Convoy, Raid, Desert, Fleet, Agents, Front }
public enum OpState { Pending, Won, Lost }
/// <summary>How a won operation's intelligence was used.</summary>
public enum OpUse { Undecided, Acted, Covered, Withheld }

/// <summary>
/// What a request is really for: a convoy to route, a raid to meet, a battle to warn of. Accepting a request puts
/// the operation on the war map; delivering the decrypts in time, or not, decides how it goes.
/// </summary>
public sealed class Operation
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public Net Net { get; set; }
    public OpKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Place { get; set; } = "";
    public double Lat { get; set; }
    public double Lon { get; set; }
    /// <summary>Ships in the convoy, bombers in the raid, men or tanks at stake.</summary>
    public int Stake { get; set; }
    public string Won { get; set; } = "";
    public string Lost { get; set; } = "";
    public OpState State { get; set; }
    public OpUse Use { get; set; }
    /// <summary>When the policy decides for an undecided won operation.</summary>
    public double DecideBy { get; set; }
    public bool AwaitingUse => State == OpState.Won && Use == OpUse.Undecided;
    public int Day { get; set; }
    public double ResolvedAt { get; set; }
    public string Outcome => State switch
    {
        OpState.Won when Use == OpUse.Withheld => "Held back to protect the source: nothing was done with it.",
        OpState.Won when Use == OpUse.Undecided => "Decrypts in hand: decide how to use them.",
        OpState.Won when Use == OpUse.Covered => Won + " (A reconnaissance aircraft was seen first.)",
        OpState.Won => Won,
        OpState.Lost => Lost,
        _ => "In progress",
    };
}

/// <summary>The running score of the war as the Park's work shaped it.</summary>
public sealed class WarTally
{
    public int ShipsSaved { get; set; }
    public int ShipsSunk { get; set; }
    public int BombersDowned { get; set; }
    public int RaidsUnopposed { get; set; }
    public int BattlesWarned { get; set; }
    public int BattlesSurprised { get; set; }
}

public sealed partial class GameState
{
    public List<Operation> Operations { get; set; } = new();
    public WarTally War { get; set; } = new();
    public const int OperationsKept = 40;

    /// <summary>
    /// The Treasury and the Chiefs notice results, not decrypt counts: each operation won in the last week lifts the
    /// grant 2%, up to 8%. (Losses already cost reputation, and so the grant; a second cut here sent the Park into a spiral.)
    /// </summary>
    public double WarMood
    {
        get
        {
            double since = Now - 7 * Defs.MinutesPerDay;
            int won = Operations.Count(o => o.State == OpState.Won && o.Use is OpUse.Acted or OpUse.Covered && o.ResolvedAt >= since);
            return Math.Min(1 + 0.02 * won, 1.08);
        }
    }

    public Operation? OperationFor(int contractId) => Operations.FirstOrDefault(o => o.ContractId == contractId);

    void OpenOperation(Contract c)
    {
        if (OperationFor(c.Id) != null) return;
        var r = new Random(Mix(Seed, c.Id, 0x0b5));
        var op = WarOps.Make(c.Net, DateOf(Day), c.Quantity, r);
        op.Id = NextId++;
        op.ContractId = c.Id;
        op.Net = c.Net;
        op.Day = Day;
        Operations.Add(op);
        if (Operations.Count > OperationsKept) Operations.RemoveAt(0);
    }

    void ResolveOperation(Contract c, bool won)
    {
        var op = OperationFor(c.Id);
        if (op == null || op.State != OpState.Pending) return;
        op.State = won ? OpState.Won : OpState.Lost;
        op.ResolvedAt = Now;
        if (won)
        {
            op.DecideBy = Now + Defs.OpDecideHours * 60;
            Emit(EventKind.Info, $"{op.Name}: the decrypts are in. Act on them, act with cover, or hold back (War map, O).");
            return;
        }
        Tally(op);
        ChangeMorale(-0.02);
        Emit(EventKind.Bad, $"{op.Name}: {op.Outcome}");
    }

    /// <summary>
    /// ULTRA's price: using a decrypt tells the enemy something. Acting outright gives the full result and draws suspicion;
    /// a cover story (a reconnaissance aircraft sent to be seen first) gives most of it for little; holding back gives nothing.
    /// </summary>
    public bool UseOperation(int opId, OpUse use)
    {
        var op = Operations.FirstOrDefault(o => o.Id == opId);
        if (op is not { AwaitingUse: true } || use == OpUse.Undecided) return false;
        if (use == OpUse.Covered && CoverLeft <= 0) return false;
        op.Use = use;
        var sv = Defs.NetOf(op.Net).Service;
        switch (use)
        {
            case OpUse.Acted:
                AddSuspicion(sv, Defs.OpActSuspicion);
                Tally(op);
                ChangeMorale(0.015);
                break;
            case OpUse.Covered:
                CoverLeft--;
                AddSuspicion(sv, Defs.OpCoverSuspicion * CoverSuspicionFactor(sv));
                Tally(op, 0.75);
                ChangeMorale(0.01);
                break;
            case OpUse.Withheld:
                Stats.OpsWithheld++;
                break;
        }
        Emit(use == OpUse.Withheld ? EventKind.Info : EventKind.Good, $"{op.Name}: {op.Outcome}");
        return true;
    }

    /// <summary>What the policy does with a decision left too long.</summary>
    public OpUse PolicyUse => Policy switch
    {
        Policy.Cautious => CoverLeft > 0 ? OpUse.Covered : OpUse.Withheld,
        Policy.Bold => OpUse.Acted,
        _ => CoverLeft > 0 ? OpUse.Covered : OpUse.Acted,
    };

    public int CoverLeft { get; set; } = Defs.CoverPerWeek;

    void TickOperations()
    {
        foreach (var op in Operations)
            if (op.AwaitingUse && Now >= op.DecideBy) UseOperation(op.Id, PolicyUse);
    }

    void Tally(Operation op, double share = 1)
    {
        bool won = op.State == OpState.Won;
        switch (op.Kind)
        {
            case OpKind.Convoy or OpKind.Fleet:
                if (won) War.ShipsSaved += (int)Math.Round(op.Stake * share); else War.ShipsSunk += Math.Max(1, op.Stake / 4);
                break;
            case OpKind.Raid:
                if (won) War.BombersDowned += Math.Max(1, (int)Math.Round(op.Stake / 6 * share)); else War.RaidsUnopposed++;
                break;
            default:
                if (won) War.BattlesWarned++; else War.BattlesSurprised++;
                break;
        }
    }
}

/// <summary>Operations by network and date, placed on the map.</summary>
public static class WarOps
{
    record Tpl(OpKind Kind, string Name, string Place, double Lat, double Lon, string Won, string Lost);

    static bool In(DateOnly d, int y0, int m0, int d0, int y1, int m1, int d1) =>
        d >= new DateOnly(y0, m0, d0) && d <= new DateOnly(y1, m1, d1);

    public static Operation Make(Net net, DateOnly d, int qty, Random r)
    {
        var pool = Pool(net, d, r);
        var t = pool[r.Next(pool.Count)];
        int stake = t.Kind switch
        {
            OpKind.Convoy => 20 + qty * 2 + r.Next(10),
            OpKind.Raid => 40 + qty * 8 + r.Next(60),
            OpKind.Fleet => 3 + r.Next(6),
            _ => 0,
        };
        string Fill(string s) => s.Replace("{n}", stake.ToString()).Replace("{q}", Math.Max(1, stake / 4).ToString())
                                  .Replace("{b}", Math.Max(1, stake / 6).ToString());
        return new Operation
        {
            Kind = t.Kind, Name = Fill(t.Name), Place = t.Place, Lat = t.Lat + (r.NextDouble() - 0.5) * 0.6, Lon = t.Lon + (r.NextDouble() - 0.5) * 0.8,
            Stake = stake, Won = Fill(t.Won), Lost = Fill(t.Lost),
        };
    }

    static List<Tpl> Pool(Net net, DateOnly d, Random r)
    {
        var p = new List<Tpl>();
        string convoy() => r.Next(3) switch { 0 => $"HX {r.Next(60, 240)}", 1 => $"SC {r.Next(5, 130)}", _ => $"ON {r.Next(10, 180)}" };
        switch (net)
        {
            case Net.Red:
                if (d < new DateOnly(1940, 5, 10))
                    p.Add(new(OpKind.Raid, "Raid on the Home Fleet", "Scapa Flow", 58.9, -3.1,
                        "The fleet had warning: {b} raiders shot down, no ships hit.", "The raiders found the anchorage: two ships damaged."));
                if (In(d, 1940, 5, 10, 1940, 6, 4))
                    p.AddRange(new Tpl[] { new(OpKind.Front, "The Meuse crossings", "Sedan", 49.7, 4.9,
                        "The French were warned of the Stuka attacks in time to pull back their guns.", "The Stukas broke the line at Sedan."),
                          new(OpKind.Fleet, "Operation DYNAMO", "Dunkirk", 51.05, 2.4,
                        "Warned of the next raid, the little ships went in under the fighters: {n} vessels saved.", "The raid caught the beaches crowded: {q} vessels lost.") });
                if (In(d, 1940, 6, 5, 1940, 8, 12))
                    p.Add(new(OpKind.Convoy, "Channel convoy {n} ships", "Straits of Dover", 51.0, 1.4,
                        "The fighters were over the convoy when the Stukas came: all {n} ships through.", "The Stukas came before the fighters: {q} ships sunk."));
                if (In(d, 1940, 8, 13, 1940, 9, 6))
                {
                    var (name, lat, lon) = r.Next(4) switch { 0 => ("Biggin Hill", 51.33, 0.03), 1 => ("Kenley", 51.30, -0.09), 2 => ("Manston", 51.34, 1.35), _ => ("Hornchurch", 51.54, 0.21) };
                    p.Add(new(OpKind.Raid, $"Adlerangriff on {name}", name, lat, lon,
                        "The squadrons were up and waiting: {b} raiders shot down.", $"The raid caught {name} on the ground."));
                }
                if (In(d, 1940, 9, 7, 1941, 5, 16))
                {
                    var (city, lat, lon) = In(d, 1940, 11, 10, 1940, 11, 15) ? ("Coventry", 52.4, -1.5)
                        : r.Next(6) switch { 0 => ("London", 51.5, -0.1), 1 => ("Liverpool", 53.4, -3.0), 2 => ("Birmingham", 52.5, -1.9), 3 => ("Plymouth", 50.4, -4.1), 4 => ("Clydebank", 55.9, -4.4), _ => ("Belfast", 54.6, -5.9) };
                    p.Add(new(OpKind.Raid, $"Night raid on {city}", city, lat, lon,
                        "The beams were known in time: decoy fires lit, the night fighters up, {b} bombers brought down.", $"The bombers found {city}. The fires burned all night."));
                }
                if (In(d, 1941, 5, 15, 1941, 6, 1))
                    p.Add(new(OpKind.Front, "Operation MERKUR", "Crete", 35.4, 23.7,
                        "Freyberg knew where the paratroops would land: the drop at Maleme was a massacre.", "The paratroops took Maleme airfield."));
                if (In(d, 1944, 6, 1, 1944, 8, 31))
                    p.Add(new(OpKind.Front, "The Normandy beachhead", "Normandy", 49.3, -0.6,
                        "The fighter Gruppen's new fields were known: the fighter-bombers caught them landing.", "German fighters reached the beaches."));
                if (In(d, 1944, 9, 1, 1945, 1, 31))
                    p.Add(new(OpKind.Front, "Luftwaffe in the west", "Rhineland", 50.9, 6.9,
                        "The fighter build-up was tracked airfield by airfield.", "The fighters massed unseen."));
                if (In(d, 1941, 6, 1, 1944, 5, 31))
                    p.Add(new(OpKind.Front, "Luftwaffe moves east", "Eastern Front", 52.0, 30.0,
                        "The warning reached Moscow, discreetly: the airfields were dispersed.", "The Soviet air force was caught on the ground."));
                break;
            case Net.LightBlue:
                p.Add(new(OpKind.Raid, "Raid on Malta", "Malta", 35.9, 14.5,
                    "The Hurricanes were up in time over Grand Harbour: {b} raiders down.", "The raid caught Grand Harbour: the convoy's ships burned at their moorings."));
                if (d >= new DateOnly(1941, 4, 10))
                    p.Add(new(OpKind.Raid, "Stukas over Tobruk", "Tobruk", 32.1, 23.96,
                        "The guns were ready for the Stukas: {b} shot down over the harbour.", "The Stukas sank the supply ship in the harbour."));
                p.Add(new(OpKind.Convoy, "Axis convoy to Tripoli", "Central Mediterranean", 34.5, 15.5,
                    "Located by 'reconnaissance' and sunk by Malta's Blenheims: {n} thousand tons of supplies lost to Rommel.", "The convoy reached Tripoli: Rommel is resupplied."));
                break;
            case Net.Brown:
                if (In(d, 1940, 9, 7, 1941, 5, 16))
                {
                    var (city, lat, lon) = In(d, 1940, 11, 10, 1940, 11, 15) ? ("Coventry", 52.4, -1.5)
                        : r.Next(5) switch { 0 => ("London", 51.5, -0.1), 1 => ("Bristol", 51.45, -2.6), 2 => ("Liverpool", 53.4, -3.0), 3 => ("Plymouth", 50.4, -4.1), _ => ("Glasgow", 55.86, -4.25) };
                    p.Add(new(OpKind.Raid, $"The beams on {city}", city, lat, lon,
                        "The beams were bent and the decoy fires lit: the bombs fell on open fields.", $"The pathfinders followed the beam to {city}."));
                }
                break;
            case Net.Chaffinch:
                if (d >= new DateOnly(1941, 2, 12))
                    p.Add(new(OpKind.Desert, "Rommel's fuel", "Benghazi", 32.1, 20.1,
                        "The tankers were sunk on the way in: Rommel's panzers halt for want of petrol.", "The fuel got through: the panzers roll again."));
                if (In(d, 1941, 11, 18, 1941, 12, 31))
                    p.Add(new(OpKind.Desert, "Operation CRUSADER", "Sidi Rezegh", 31.9, 23.9,
                        "Eighth Army knew where the panzers were massing.", "The panzers caught Eighth Army's armour piecemeal."));
                if (In(d, 1942, 5, 20, 1942, 6, 21))
                    p.Add(new(OpKind.Desert, "Gazala", "Gazala", 32.1, 23.3,
                        "Warned of the hook round Bir Hacheim, the line held.", "Rommel went round the south of the line. Tobruk fell."));
                if (In(d, 1942, 7, 1, 1942, 11, 8))
                    p.Add(new(OpKind.Desert, "The Alamein line", "El Alamein", 30.8, 28.95,
                        "Montgomery knew the plan of attack before Rommel's commanders did.", "The attack came where it wasn't expected."));
                if (d >= new DateOnly(1942, 11, 8))
                    p.Add(new(OpKind.Desert, "Tunisia", "Tunis", 36.8, 10.2,
                        "The reinforcement flights were met over the Sicilian Narrows.", "Reinforcements poured into Tunisia unopposed."));
                p.Add(new(OpKind.Desert, "Afrika Korps movements", "Cyrenaica", 31.5, 22.0,
                    "The next move was known a day before it came.", "The Afrika Korps moved and nobody knew where."));
                break;
            case Net.Dolphin:
                p.Add(new(OpKind.Convoy, $"Convoy {convoy()}: {{n}} ships", "Western Approaches", 55.5, -18.0,
                    "Rerouted round the U-boat line: all {n} ships arrived.", "The U-boats found it: {q} ships sunk."));
                if (In(d, 1941, 5, 18, 1941, 5, 28))
                    p.Add(new(OpKind.Fleet, "The hunt for BISMARCK", "North Atlantic", 48.0, -16.0,
                        "Her course for Brest was known: the Swordfish found her, and the fleet finished her.", "BISMARCK reached the cover of the Luftwaffe."));
                if (In(d, 1942, 2, 1, 1942, 2, 14))
                    p.Add(new(OpKind.Fleet, "Operation CERBERUS", "The Channel", 50.8, 0.0,
                        "The breakout was known: the ships were mined and damaged on their way.", "The battlecruisers came up the Channel unseen."));
                if (In(d, 1942, 6, 25, 1942, 7, 12))
                    p.Add(new(OpKind.Convoy, "Convoy PQ 17: {n} ships", "Barents Sea", 71.5, 25.0,
                        "TIRPITZ was known to be still in Altenfjord: the convoy kept together.", "The convoy was ordered to scatter: {q} ships sunk."));
                if (d.Year <= 1941)
                    p.Add(new(OpKind.Fleet, "Weather ships off Iceland", "Iceland", 66.0, -18.0,
                        "The weather ship was taken with its key tables intact.", "The weather ship got away."));
                break;
            case Net.Shark:
                p.Add(new(OpKind.Convoy, $"Convoy {convoy()}: {{n}} ships", "Mid-Atlantic air gap", 53.0, -32.0,
                    "Rerouted round the wolf pack: all {n} ships arrived.", "The wolf pack found it in the air gap: {q} ships sunk."));
                if (In(d, 1942, 1, 13, 1942, 7, 31))
                    p.Add(new(OpKind.Convoy, "Tankers off Cape Hatteras", "Cape Hatteras", 35.2, -75.5,
                        "Held in port on warning: {n} tankers safe.", "The U-boats picked them off against the lit coast: {q} tankers sunk."));
                if (In(d, 1942, 10, 20, 1942, 11, 30))
                    p.Add(new(OpKind.Fleet, "The TORCH convoys", "Off Gibraltar", 36.0, -9.0,
                        "The U-boats were drawn away: the invasion fleet passed unseen.", "U-boats reached the troopships."));
                if (In(d, 1943, 3, 10, 1943, 3, 25))
                    p.Add(new(OpKind.Convoy, "HX 229 and SC 122: {n} ships", "Mid-Atlantic air gap", 52.0, -30.0,
                        "Three wolf packs evaded: the convoys arrived.", "The biggest convoy battle of the war: {q} ships sunk."));
                break;
            case Net.Police:
                p.Add(new(OpKind.Front, d < new DateOnly(1941, 6, 22) ? "Police in the occupied lands" : "Police battalions in the east",
                    d < new DateOnly(1941, 6, 22) ? "Poland" : "Russia", d < new DateOnly(1941, 6, 22) ? 52.2 : 53.9, d < new DateOnly(1941, 6, 22) ? 21.0 : 27.5,
                    "The reports reached the Prime Minister. They are a record of what is being done.", "The reports did not reach London in time."));
                break;
            case Net.Abwehr:
                p.Add(new(OpKind.Agents, "Agents for Britain", "Lisbon", 38.7, -9.1,
                    "The agents were met on landing: one more for the Double Cross Committee.", "An agent landed unseen."));
                if (In(d, 1942, 10, 1, 1942, 11, 10))
                    p.Add(new(OpKind.Agents, "The TORCH deception", "Madrid", 40.4, -3.7,
                        "The Abwehr believes Malta and Dakar: the real fleet goes unremarked.", "A Spanish source reported the fleet truly."));
                break;
            case Net.Tunny:
                if (d < new DateOnly(1943, 7, 13))
                    p.Add(new(OpKind.Front, "Operation ZITADELLE", "Kursk", 51.7, 36.2,
                        "The plan reached the Russians: the salient was a fortress when the panzers came.", "The panzers hit the salient without warning."));
                if (In(d, 1943, 7, 10, 1943, 10, 31))
                    p.Add(new(OpKind.Front, "Italy", "Salerno", 40.6, 14.8,
                        "Kesselring's counter-attack was known in time.", "The counter-attack nearly threw the landing back into the sea."));
                if (d < new DateOnly(1944, 7, 31))
                    p.Add(new(OpKind.Front, "OB West's appreciation", "Pas de Calais", 50.9, 1.8,
                        "Hitler's generals still expect the main landing in the Pas de Calais. Good.", "The appreciation never reached Whitehall."));
                if (In(d, 1944, 8, 1, 1944, 10, 31))
                    p.Add(new(OpKind.Front, "Panzers in Holland", "Arnhem", 51.98, 5.9,
                        "Where the SS panzers were refitting was known to the day.", "Nobody knew where the panzers had gone."));
                if (In(d, 1944, 11, 1, 1945, 1, 31))
                    p.Add(new(OpKind.Front, "The Ardennes", "Bastogne", 50.0, 5.7,
                        "The build-up behind the Eifel was reported in time.", "The offensive came out of the fog unannounced."));
                if (d >= new DateOnly(1945, 2, 1))
                    p.Add(new(OpKind.Front, "The last battles", "Berlin", 52.5, 13.4,
                        "The high command's last orders were read as they were sent.", "The last orders went unread."));
                break;
        }
        if (p.Count == 0)
            p.Add(new(OpKind.Front, $"{Defs.NetOf(net).Name} traffic", "Germany", 51.0, 10.0,
                "The picture of the enemy is a little clearer.", "The picture of the enemy stays dark."));
        return p;
    }
}
