using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// Everyone is hired onto a watch and posted to a building, and stays in it until the player posts them elsewhere:
/// a Hut 6 clerk is a Hut 6 clerk. Only messengers range over the whole Park, since carrying paper between buildings is
/// their job (anyone posted puts paper bound elsewhere down in a tray for them). The hiring card suggests the watch and
/// building with the fewest people for their desks; the tip <c>posting</c> points out a building whose desks have
/// nobody posted on a watch.
/// </summary>
public sealed partial class GameState
{
    /// <summary>Messengers carry paper between buildings, so they aren't posted to one.</summary>
    public static bool Roams(Role r) => r == Role.Messenger;

    /// <summary>Open buildings with desks or machines this role works.</summary>
    public IEnumerable<Zone> PostsFor(Role r) =>
        Grid.Zones.Where(z => z.Unlocked && Defs.Stations.Values.Any(d => Works(r, d.Type) && Defs.AllowedIn(z.Id, d.Type)));

    /// <summary>The desks a role works, or for fitters the machines they look after.</summary>
    static bool Works(Role r, StationType t) => r == Role.Fitter ? Defs.FitterServes(t) : Defs.Station(t).Operator == r;

    int DesksIn(Role r, string zoneId)
    {
        int n = 0;
        foreach (var st in Stations)
            if (Works(r, st.Type) && !st.Obsolete && !st.PendingRemoval && Grid.ZoneAt(st.X, st.Y)?.Id == zoneId) n++;
        return n;
    }

    int PostedIn(Role r, string zoneId, Shift? shift)
    {
        int n = 0;
        foreach (var s in Staff)
            if (s.Role == r && s.Posted == zoneId && !s.Leaving && (shift == null || s.Shift == shift)) n++;
        return n;
    }

    IEnumerable<Shift> Watches => WatchesAvailable ? new[] { Shift.Day, Shift.Evening, Shift.Night } : new[] { Shift.Office };

    /// <summary>
    /// The watch and building a new hire of this role is most wanted on: the fewest people for its desks there.
    /// Messengers get no building. With no desks for the role yet, the Mansion (or the first building that takes them).
    /// </summary>
    public (Shift shift, string? zone) SuggestPost(Role r)
    {
        Shift best = Watches.OrderBy(w => Staff.Count(s => s.Role == r && !s.Leaving && s.Shift == w)).First();
        if (Roams(r)) return (best, null);
        string? zone = null;
        double bestScore = double.MaxValue;
        foreach (var z in PostsFor(r))
        {
            int desks = DesksIn(r, z.Id);
            if (desks == 0) continue;
            foreach (var w in Watches)
            {
                double score = (PostedIn(r, z.Id, w) + 1.0) / desks;
                if (score < bestScore - 1e-9) { bestScore = score; zone = z.Id; best = w; }
            }
        }
        zone ??= PostsFor(r).Select(z => z.Id).OrderBy(id => id != "mansion").FirstOrDefault() ?? "mansion";
        return (best, zone);
    }

    /// <summary>The watch suggested for this role in one building, once the player has picked the building.</summary>
    public Shift SuggestShift(Role r, string? zone) =>
        Watches.OrderBy(w => zone == null ? Staff.Count(s => s.Role == r && !s.Leaving && s.Shift == w) : PostedIn(r, zone, w)).First();

    /// <summary>Anyone not yet posted (the starting staff, older saves) goes to the building they last worked in, else where they stand, else the Mansion.</summary>
    public void PostUnposted()
    {
        foreach (var s in Staff)
        {
            if (s.Posted != null || Roams(s.Role)) continue;
            string? z = s.HeadOf ?? (s.LastZone is string lz && Grid.Zone(lz) is { Unlocked: true } ? lz : null)
                        ?? Grid.ZoneAt(s.TilePos.x, s.TilePos.y)?.Id;
            s.Posted = z != null && Grid.Zone(z) is { Unlocked: true } ? z : "mansion";
        }
    }

    /// <summary>A building with desks for a role and nobody of that role posted on a watch, if any: (building, role, watch).</summary>
    public (Zone zone, Role role, Shift shift)? PostingGap() => PostingGaps().Select(x => ((Zone, Role, Shift)?)x).FirstOrDefault();

    /// <summary>People of a role wanted to have one in every building with its desks, on every watch.</summary>
    public int PostsWanted(Role r) => Roams(r) ? 0 : PostsFor(r).Count(z => DesksIn(r, z.Id) >= (r == Role.Fitter ? 3 : 1)) * Watches.Count();

    /// <summary>Every building, role and watch with desks and nobody posted.</summary>
    public IEnumerable<(Zone zone, Role role, Shift shift)> PostingGaps()
    {
        foreach (var z in Grid.Zones)
        {
            if (!z.Unlocked) continue;
            foreach (Role r in Enum.GetValues<Role>())
            {
                // Fitters are a help, not a step every message needs: their own tip covers them.
                if (Roams(r) || r == Role.Fitter || DesksIn(r, z.Id) == 0) continue;
                // Only once the Park has posted people of that role at all: a role nobody's hired yet is the hiring tip's business.
                if (!Staff.Any(s => s.Role == r && s.Posted != null && !s.Leaving)) continue;
                foreach (var w in Watches)
                    if (PostedIn(r, z.Id, w) == 0) yield return (z, r, w);
            }
        }
    }

    /// <summary>
    /// Who could move to fill a gap: someone of that role on that watch in the building with the most of them per desk
    /// (and more than one there), the least skilled first. Null if nobody can be spared.
    /// </summary>
    public Staff? PostingDonor(Role r, Shift w, string toZone)
    {
        Staff? best = null;
        double bestScore = 0;
        foreach (var z in PostsFor(r))
        {
            if (z.Id == toZone) continue;
            int have = PostedIn(r, z.Id, w);
            if (have < 2) continue;
            double score = have / Math.Max(1.0, DesksIn(r, z.Id));
            if (score <= bestScore) continue;
            var who = Staff.Where(s => s.Role == r && s.Posted == z.Id && s.Shift == w && !s.Leaving && s.HeadOf == null && s.Pinned == null)
                .OrderBy(s => s.Skill).FirstOrDefault();
            if (who == null) continue;
            best = who;
            bestScore = score;
        }
        return best;
    }
}
