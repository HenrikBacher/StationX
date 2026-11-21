using System;

namespace StationX.Sim;

/// <summary>
/// Our own cipher is read. From early 1942 the German naval codebreakers (B-Dienst) read the Admiralty's Naval Cypher No. 3,
/// the convoy cipher, so the U-boats found convoys whatever Hut 8 did: convoys lose more ships (a won convoy operation saves
/// less), the Admiralty's patience wears, and the tonnage mounts. Naval decrypts slowly give it away (U-boats waiting where
/// they couldn't know to wait), and once it is suspected, a cryptanalyst set to study our own traffic builds the case faster.
/// With enough evidence the Admiralty changes the cipher; if nobody proves it, it changes anyway on 10 June 1943, as it did.
/// </summary>
public sealed partial class GameState
{
    /// <summary>How far the case against Naval Cypher No. 3 has got (1 = proved).</summary>
    public double CipherEvidence { get; set; }
    /// <summary>The Admiralty has changed the convoy cipher.</summary>
    public bool CipherChanged { get; set; }
    /// <summary>A cryptanalyst away studying our own traffic, and until when.</summary>
    public double CipherStudyUntil { get; set; } = -1;

    /// <summary>The Germans are reading the convoy cipher today.</summary>
    public bool CipherRead => Mode == GameMode.Campaign && !CipherChanged && Date >= Defs.BdienstFrom && Date < Defs.BdienstUntil;
    /// <summary>The Park has begun to suspect it (the card shows from here).</summary>
    public bool CipherSuspected => CipherRead && CipherEvidence >= Defs.BdienstNoticeAt;
    public bool CipherProved => CipherEvidence >= 1;
    public bool CipherStudying => CipherStudyUntil > Now;

    void DailyOwnCipher()
    {
        if (!CipherChanged && Mode == GameMode.Campaign && Date >= Defs.BdienstUntil && CipherEvidence > 0)
        {
            CipherChanged = true;
            Emit(EventKind.Info, "The Admiralty replaces Naval Cypher No. 3 at last. The U-boats stop finding the convoys quite so easily.");
            return;
        }
        if (!CipherRead) return;
        var r = Rng.Seeded(Mix(Seed, Day, 0xBD1E));
        TonnageLost += r.Range(Defs.BdienstTonsMin, Defs.BdienstTonsMax);
        if (CipherSuspected) AdjustRep(-Defs.BdienstRepPerDay, "Convoys found anyway");
    }

    /// <summary>Each naval decrypt read while the cipher is read: now and then the U-boats are where they couldn't have known to be.</summary>
    void NavalReadWhileOwnRead()
    {
        if (!CipherRead || CipherProved) return;
        bool before = CipherSuspected;
        CipherEvidence = Math.Min(1, CipherEvidence + Defs.BdienstPerDecrypt);
        if (!before && CipherSuspected)
            Emit(EventKind.Bad, "Hut 8 notices U-boats waiting on convoy routes changed only hours before. Are the Germans reading our own cipher? (Intelligence, I)");
        if (CipherProved) Emit(EventKind.Good, "The case is made: the Germans read Naval Cypher No. 3. Take it to the Admiralty (Intelligence, I).");
    }

    public string? CantStudyCipher()
    {
        if (!CipherSuspected) return "Nothing to study yet";
        if (CipherProved) return "The case is already made";
        if (CipherStudying) return "Someone is already on it";
        if (StudyCandidate() == null) return "No cryptanalyst free to send";
        return null;
    }

    Staff? StudyCandidate()
    {
        Staff? best = null;
        foreach (var s in Staff)
            if (s.Role == Role.Cryptanalyst && !s.Leaving && s.HeadOf == null && !s.IsSick(Now) && !s.Arriving(Now) && (best == null || s.Skill > best.Skill))
                best = s;
        return best;
    }

    /// <summary>Puts a cryptanalyst on our own traffic for a few days: the case moves on by a good deal when they come back.</summary>
    public bool StudyCipher()
    {
        if (CantStudyCipher() != null || StudyCandidate() is not { } s) return false;
        s.Pinned = null;
        SendOff(s, Defs.BdienstStudyDays, "study");
        CipherStudyUntil = Now + Defs.BdienstStudyDays * Defs.MinutesPerDay;
        CipherEvidence = Math.Min(1, CipherEvidence + Defs.BdienstStudyEvidence);
        Emit(EventKind.Info, $"{s.Name} goes over the Admiralty's own convoy signals beside the U-boat sightings for {Defs.BdienstStudyDays:0} days.");
        if (CipherProved) Emit(EventKind.Good, "The case is made: the Germans read Naval Cypher No. 3. Take it to the Admiralty (Intelligence, I).");
        return true;
    }

    public bool ChangeCipher()
    {
        if (!CipherRead || !CipherProved) return false;
        CipherChanged = true;
        AdjustRep(Defs.BdienstProvedRep, "Our own cipher");
        Emit(EventKind.Good, "The Admiralty believes us, and changes the convoy cipher. The U-boats lose the convoys they were finding so easily.");
        return true;
    }

    /// <summary>For screenshots and tests.</summary>
    public void DebugCipher(double evidence) => CipherEvidence = evidence;
}
