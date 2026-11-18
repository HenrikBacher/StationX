using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>How a head of section runs their hut.</summary>
public enum HeadStyle { Shepherd, Driver, MachineMan, Teacher }

public sealed record HeadStyleDef(HeadStyle Style, string Name, string Blurb, double Work, double Morale, double Drain, double Wear, double Learn, double Strain);

public static partial class Defs
{
    public static readonly Dictionary<HeadStyle, HeadStyleDef> HeadStyles = new()
    {
        [HeadStyle.Shepherd] = new(HeadStyle.Shepherd, "Shepherd", "Looks after people: work +6%, morale up each day, strain eases faster.", 1.06, 0.05, 1.0, 1.0, 1.0, -0.03),
        [HeadStyle.Driver] = new(HeadStyle.Driver, "Driver", "Drives the hut hard: work +18%, but people tire 15% faster and morale slips.", 1.18, -0.01, 1.15, 1.0, 1.0, 0),
        [HeadStyle.MachineMan] = new(HeadStyle.MachineMan, "Machine man", "Keeps the machines sweet: work +8%, machines in the hut wear 40% slower.", 1.08, 0.01, 1.0, 0.6, 1.0, 0),
        [HeadStyle.Teacher] = new(HeadStyle.Teacher, "Teacher", "Brings people on: work +8%, experience comes twice as fast.", 1.08, 0.01, 1.0, 1.0, 2.0, 0),
    };
}

public sealed partial class GameState
{
    /// <summary>The style of the head of a zone today, or null if the zone has no head in.</summary>
    public HeadStyleDef? HeadIn(Zone? z) =>
        z == null ? null : Staff.FirstOrDefault(s => s.HeadOf == z.Id && !s.Leaving && !s.Away) is { } h ? Defs.HeadStyles[h.HeadStyle] : null;

    public HeadStyleDef? HeadAt((int x, int y) tile) => HeadIn(Grid.ZoneAt(tile.x, tile.y));

    /// <summary>Each headed zone's style, for the tick (cheaper than looking up per person).</summary>
    /// <param name="inNow">True for heads at the Park right now; false for the daily effects, which count a head
    /// who is merely off watch (but not one off sick or on leave).</param>
    Dictionary<Zone, HeadStyleDef> HeadsToday(bool inNow = true)
    {
        var d = new Dictionary<Zone, HeadStyleDef>();
        foreach (var s in Staff)
            if (s.HeadOf != null && !s.Leaving && (inNow ? !s.Away : !s.IsSick(Now)) && Grid.Zone(s.HeadOf) is { } z) d[z] = Defs.HeadStyles[s.HeadStyle];
        return d;
    }

    Dictionary<Zone, HeadStyleDef> _heads = new();

    HeadStyleDef? HeadOfLastZone(Staff s) => s.LastZone != null && Grid.Zone(s.LastZone) is { } z && _heads.TryGetValue(z, out var h) ? h : null;

    public bool SetHeadStyle(int staffId, HeadStyle style)
    {
        var s = StaffById(staffId);
        if (s == null) return false;
        s.HeadStyle = style;
        return true;
    }

    /// <summary>A style that fits the person, for a new appointment.</summary>
    static HeadStyle StyleFor(Staff s) =>
        s.Has(Trait.Tireless) ? HeadStyle.Driver
        : s.Role == Role.Wren || s.Specialties.Any(t => t is StationType.Bombe or StationType.Bombe4 or StationType.Typex) ? HeadStyle.MachineMan
        : s.Skill >= 3 ? HeadStyle.Teacher
        : HeadStyle.Shepherd;
}
