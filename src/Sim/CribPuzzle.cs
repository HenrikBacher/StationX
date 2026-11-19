using System;
using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// Crib dragging. Enigma never enciphers a letter as itself, so a guessed plaintext (the crib) can't sit anywhere along
/// the ciphertext where one of its letters falls on the same letter. Slide it along: exactly one position survives.
/// </summary>
public sealed class CribPuzzle
{
    public string Cipher { get; init; } = "";
    public string Crib { get; init; } = "";
    public int Answer { get; init; }
    public int Positions => Cipher.Length - Crib.Length + 1;

    /// <summary>Indexes into the crib where it would "crash" (a letter on itself) at this position.</summary>
    public IEnumerable<int> Crashes(int pos)
    {
        for (int i = 0; i < Crib.Length; i++)
            if (pos + i < Cipher.Length && Cipher[pos + i] == Crib[i]) yield return i;
    }

    public bool Possible(int pos) => pos >= 0 && pos < Positions && !Crashes(pos).Any();

    static readonly Dictionary<Family, string[]> Cribs = new()
    {
        [Family.Enigma] = new[] { "WETTERVORHERSAGE", "KEINEBESONDERENEREIGNISSE", "ANXOBERKOMMANDO", "WETTERFUERDIENACHT", "FEINDLICHEKRAEFTE", "QUANTITYXWETTER" },
    };

    public static CribPuzzle Make(int seed, Net net)
    {
        var r = new Random(seed);
        var cribs = Cribs[Family.Enigma];
        string crib = net is Net.Dolphin or Net.Shark && r.NextDouble() < 0.5 ? "WETTERVORHERSAGEBISKAYA" : cribs[r.Next(cribs.Length)];
        int len = crib.Length + 10 + r.Next(6);
        for (int attempt = 0; attempt < 200; attempt++)
        {
            var ct = new char[len];
            int answer = r.Next(len - crib.Length + 1);
            for (int i = 0; i < len; i++)
            {
                char c;
                // Within the answer's span, never the crib's own letter there.
                do c = (char)('A' + r.Next(26)); while (i >= answer && i < answer + crib.Length && c == crib[i - answer]);
                ct[i] = c;
            }
            // Every other position must crash at least once; plant a crash where it doesn't disturb the answer.
            bool ok = true;
            for (int q = 0; q <= len - crib.Length && ok; q++)
            {
                if (q == answer) continue;
                bool crashes = false;
                for (int i = 0; i < crib.Length; i++) if (ct[q + i] == crib[i]) { crashes = true; break; }
                if (crashes) continue;
                var spots = Enumerable.Range(0, crib.Length).Where(i =>
                {
                    int at = q + i;
                    bool inAnswer = at >= answer && at < answer + crib.Length;
                    return !inAnswer || crib[at - answer] != crib[i];
                }).ToList();
                if (spots.Count == 0) { ok = false; break; }
                int k = spots[r.Next(spots.Count)];
                ct[q + k] = crib[k];
            }
            var p = new CribPuzzle { Cipher = new string(ct), Crib = crib, Answer = answer };
            if (ok && Enumerable.Range(0, p.Positions).Count(p.Possible) == 1) return p;
        }
        // Can't happen in practice; a trivial fallback keeps the game going.
        return new CribPuzzle { Cipher = new string('Q', crib.Length), Crib = crib, Answer = 0 };
    }
}

public sealed partial class GameState
{
    /// <summary>The crib puzzle for the key attempt on a crib desk now, or null if there isn't one to drag.</summary>
    public CribPuzzle? PuzzleFor(int stationId)
    {
        var st = StationById(stationId);
        if (st is not { Type: StationType.Crib, Current: int id } || !Messages.TryGetValue(id, out var m) || !m.Carrier || m.Dragged) return null;
        if (Defs.NetOf(m.Net).Family != Family.Enigma) return null;
        return CribPuzzle.Make(Mix(Seed, m.Id, 0xC21B), m.Net);
    }

    /// <summary>
    /// Places the crib by hand. Right: the menu is done at once. Wrong: a crash at that position is spotted the hard way,
    /// and the desk loses twenty minutes.
    /// </summary>
    /// <param name="msgId">The key attempt the sheet was opened for; if the desk has moved on, nothing happens.</param>
    public bool DragCrib(int stationId, int position, int? msgId = null)
    {
        var p = PuzzleFor(stationId);
        var st = StationById(stationId);
        if (p == null || st?.Current is not int id || (msgId != null && msgId != id)) return false;
        var m = Messages[id];
        if (position == p.Answer)
        {
            m.Dragged = true;
            st.WorkDone = st.WorkTotal;
            Stats.CribsDragged++;
            Emit(EventKind.Good, $"The crib fits at position {position + 1}: no letter falls on itself. The menu for {Defs.NetOf(m.Net).Name} goes to the Bombes.");
            return true;
        }
        st.WorkTotal += Defs.WrongDragMinutes;
        Emit(EventKind.Bad, $"Position {position + 1} won't do: a letter would be enciphered as itself, and Enigma never does that. Twenty minutes lost.");
        return false;
    }
}
