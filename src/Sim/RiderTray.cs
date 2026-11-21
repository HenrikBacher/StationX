using System;
using System.Collections.Generic;

namespace StationX.Sim;

/// <summary>
/// The riders' in-tray: a table inside the Mansion's front door where the dispatch riders hand in their bundles. The
/// bundles wait in its out-tray, and a messenger (or an unpinned clerk) carries them on to registration like any other
/// paper. The Park always has exactly one; it can be moved but not removed.
/// </summary>
public sealed partial class GameState
{
    /// <summary>Puts the tray in the Mansion, as near its front door as there's room, off the entrance hall.</summary>
    public Station? PlaceRiderTray()
    {
        var mansion = Grid.Zone("mansion");
        if (mansion == null) return null;
        var door = mansion.InsideDoor;
        var spots = new List<(int d, int x, int y)>();
        for (int y = mansion.Y; y < mansion.Y + mansion.H; y++)
            for (int x = mansion.X; x < mansion.X + mansion.W; x++)
                if (mansion.InInterior(x, y)) spots.Add((Math.Abs(x - door.x) + Math.Abs(y - door.y), x, y));
        spots.Sort();
        foreach (var (_, x, y) in spots)
            for (int rot = 0; rot < 4; rot++)
                if (CheckPlacement(StationType.RiderTray, x, y, rot, null) == null)
                    return Place(StationType.RiderTray, x, y, rot, free: true);
        return null;
    }
}
