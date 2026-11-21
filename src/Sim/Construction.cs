using System;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// Building takes time. Opening a hut no longer puts it up at once: the builders are on the plot for
/// <see cref="Defs.HutBuildDays"/> (a brick block <see cref="Defs.BlockBuildDays"/>) before its floor is ready, so a hut is
/// best ordered before the work arrives. The builders can be paid to work on (<see cref="HurryBuilders"/>: half the time
/// left, for a share of the cost). From 1942, when the Office of Works began the Park's brick blocks, a timber hut can be
/// rebuilt in brick around the people working in it (<see cref="RebuildInBrick"/>): its desks are a little slower while the
/// builders are in, and then it is warm (central heating: no stove wanted) and far less likely to burn.
/// </summary>
public sealed partial class GameState
{
    public static readonly DateOnly BrickFrom = new(1942, 3, 1);

    /// <summary>The builders are on this plot now (a new building, or a hut being rebuilt in brick).</summary>
    public bool Building(Zone z) => z.BuildUntil > Now;

    /// <summary>How far the builders have got, 0 to 1.</summary>
    public double BuildProgress(Zone z) =>
        z.BuildDays <= 0 ? 1 : Math.Clamp(1 - (z.BuildUntil - Now) / (z.BuildDays * Defs.MinutesPerDay), 0, 1);

    public double BuildHoursLeft(Zone z) => Math.Max(0, (z.BuildUntil - Now) / 60);

    /// <summary>Warm in winter: the Mansion's boiler, Block F's, or a hut rebuilt in brick with central heating.</summary>
    public static bool Heated(Zone z) => Defs.Heated(z.Id) || z.Brick;

    void StartBuilding(Zone z, double days)
    {
        z.BuildDays = days;
        z.BuildUntil = Now + days * Defs.MinutesPerDay;
        z.Hurried = false;
    }

    void TickConstruction()
    {
        foreach (var z in Grid.Zones)
        {
            if (z.BuildUntil <= 0 || Now < z.BuildUntil) continue;
            z.BuildUntil = 0;
            if (z.Rebuilding)
            {
                z.Rebuilding = false;
                z.Brick = true;
                Stats.Rebuilt++;
                Emit(EventKind.Good, $"{z.Name} has been rebuilt in brick: warm all winter, and an incendiary is far less likely to take hold.");
            }
            else
            {
                z.Unlocked = true;
                Emit(EventKind.Good, $"The builders have finished {z.Name}. Its floor is ready for desks and machines.");
            }
        }
    }

    public int HurryCost(Zone z) => (int)Math.Round(z.Cost * Defs.HurryShare / 10.0) * 10;

    public string? CantHurry(Zone z) =>
        !Building(z) ? "The builders aren't on it" : z.Hurried ? "The builders are already working on" : Funds < HurryCost(z) ? $"Needs £{HurryCost(z):N0}" : null;

    /// <summary>The builders work on into the evenings: half the time left, for a share of the cost.</summary>
    public bool HurryBuilders(string id)
    {
        if (Grid.Zone(id) is not { } z || CantHurry(z) != null) return false;
        Funds -= HurryCost(z);
        z.Hurried = true;
        z.BuildUntil = Now + (z.BuildUntil - Now) / 2;
        Emit(EventKind.Info, $"The builders on {z.Name} work on into the evenings: done in about {Math.Ceiling(BuildHoursLeft(z)):0} hours.");
        return true;
    }

    public bool BrickOpen => Date >= BrickFrom;

    public int RebuildCost(Zone z) => z.Cost + Defs.BrickExtra;

    public string? CantRebuild(Zone z) =>
        !BrickOpen ? "From 1942" : z.Id == "mansion" || z.Brick ? "Already brick" : !z.Unlocked ? "Not open yet" : Building(z) ? "The builders are already in"
        : Funds < RebuildCost(z) ? $"Needs £{RebuildCost(z):N0}" : null;

    /// <summary>A timber hut rebuilt in brick around the people in it.</summary>
    public bool RebuildInBrick(string id)
    {
        if (Grid.Zone(id) is not { } z || CantRebuild(z) != null) return false;
        Funds -= RebuildCost(z);
        z.Rebuilding = true;
        StartBuilding(z, Defs.RebuildDays);
        Emit(EventKind.Info, $"The Office of Works' builders start rebuilding {z.Name} in brick around the people in it. " +
                             $"For about {Defs.RebuildDays:0} days its desks are a little slower for the noise and the dust.");
        return true;
    }

    /// <summary>For screenshots and tests: rebuild in brick now, whatever the date and funds.</summary>
    public void DebugRebuild(string id)
    {
        if (Grid.Zone(id) is not { Unlocked: true, Brick: false } z) return;
        z.Rebuilding = true;
        StartBuilding(z, Defs.RebuildDays);
        z.BuildUntil = Now + (z.BuildUntil - Now) * 0.6;
    }

    /// <summary>The builders' noise and dust in a hut being rebuilt.</summary>
    double BuildersFactor(Zone z) => z.Rebuilding && Building(z) ? Defs.RebuildSlow : 1;

    /// <summary>Timber huts still standing, for the brick tip.</summary>
    public int TimberHuts => Grid.Zones.Count(z => z.Unlocked && !z.Brick && z.Id != "mansion");
}
