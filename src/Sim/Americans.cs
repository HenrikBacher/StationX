using System;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// The Americans. Under the BRUSA agreement of 17 May 1943 the US Navy's OP-20-G took on much of SHARK: Joseph Desch's
/// four-rotor Bombes at Dayton ran Hut 8's menus, sent over the cable, and by the end of 1943 did most of the work. The
/// US Army's Special Branch sent the 6813th Signal Security Detachment into Hut 3 and Hut 6. Here: a decision once the
/// agreement is offered. Sharing puts Dayton's Bombes behind the outstation teleprinter for four-rotor menus (slower, for
/// the cable, and more of them each month); taking the detachment too sends American cryptanalysts and linguists, paid
/// by the US Army. Washington's handling of what it reads makes the Kriegsmarine a little warier.
/// </summary>
public sealed partial class GameState
{
    public const string BrusaTitle = "BRUSA";

    /// <summary>0 not offered yet; 1 SHARK shared with OP-20-G; 2 and the US Army's detachment; −1 kept at the Park.</summary>
    public int Brusa { get; set; }
    public int BrusaDay { get; set; }
    public int UsArmySent { get; set; }

    /// <summary>US Navy Bombes at Dayton taking our four-rotor menus: a few at first, more each month.</summary>
    public int DaytonBombes => Brusa <= 0 || !Researched.Contains(ResearchId.FourRotorBombe) ? 0
        : Math.Min(Defs.DaytonMax, Defs.DaytonStart + (TuringWent ? 1 : 0) + Math.Max(0, Day - BrusaDay) / Defs.DaytonGrowDays);

    public int DaytonJobs
    {
        get
        {
            int n = 0;
            foreach (var j in RemoteJobs) if (j.Dayton) n++;
            return n;
        }
    }

    /// <summary>For screenshots and tests: the agreement now, whatever the date.</summary>
    public void DebugBrusa()
    {
        Stats.ScriptedDone.Remove("brusa");
        var mode = Mode;
        Mode = GameMode.Campaign;
        MaybeBrusa(force: true);
        Mode = mode;
    }

    void MaybeBrusa(bool force = false)
    {
        if (Mode != GameMode.Campaign || Dilemmas.Count > 0 || Stats.ScriptedDone.Contains("brusa")) return;
        // The agreement came in May 1943; the Testery's chapter may be over long before then, so it comes a few days in.
        int act = Campaign.IndexOf("The Testery");
        if (!force && (ActIndex < act || (ActIndex == act && Day < ActStartDay + Defs.BrusaAfterDays && Date < Defs.BrusaFrom))) return;
        Stats.ScriptedDone.Add("brusa");
        RaiseDilemma(new Dilemma
        {
            Service = Service.Kriegsmarine, Title = BrusaTitle, Tag = "brusa",
            Text = "Washington and London have signed an agreement on signals intelligence. The US Navy's cryptanalysts at OP-20-G have built " +
                   "four-rotor Bombes of their own at Dayton, Ohio, faster than ours, and want to run SHARK menus on them. The US Army asks to " +
                   "send a detachment of its own people to work in the huts beside ours. Travis wants your view.",
            Context = "Dayton's Bombes would take four-rotor menus sent over the outstation teleprinter and cable, when our own are busy: slower " +
                      $"for the cable (about {Defs.DaytonCableMinutes / 60} hours more), but {Defs.DaytonStart} at first and more every month. The " +
                      "Army's detachment would be cryptanalysts and linguists the US Army pays for. The Americans are not always careful with " +
                      "what they read: the U-boat refuelling rendezvous are tempting targets.",
            Options =
            {
                new() { Label = "Share SHARK with OP-20-G", Rep = Defs.BrusaRep, Suspicion = Defs.BrusaSuspicion, Action = "brusa1",
                    Outcome = "Hut 8's menus go to Washington by cable each day and the stops come back the same way. The Admiralty grumbles " +
                              "about the delay, and is glad of the Bombes." },
                new() { Label = "Share SHARK, and take the Army's detachment", Rep = Defs.BrusaRep + 1, Suspicion = Defs.BrusaSuspicion + 2, Action = "brusa2",
                    Outcome = "Dayton takes our SHARK menus, and the first officers of the 6813th Signal Security Detachment are on their way, " +
                              "to learn the trade in Hut 6 and Hut 3. They will be paid by the US Army." },
                new() { Label = "Keep SHARK in Hut 8", Points = Defs.BrusaPoints,
                    Outcome = "Travis tells Washington that Hut 8 can manage. The Admiralty is pleased, and Hut 8 keeps every break to itself. " +
                              "OP-20-G builds its Bombes all the same." },
            },
        });
    }

    void AgreeBrusa(bool army)
    {
        Brusa = army ? 2 : 1;
        BrusaDay = Day;
        if (army) SendUsArmy(2);
    }

    void DailyAmericans()
    {
        if (Stats.ScriptedDone.Contains("brusa") && Brusa == 0 && !Dilemmas.Any(d => d.Tag == "brusa")) Brusa = -1;
        if (Brusa == 2 && UsArmySent < Defs.UsArmyMax && Day > BrusaDay && (Day - BrusaDay) % Defs.UsArmyEveryDays == 0) SendUsArmy(1);
    }

    /// <summary>The detachment's next officers (own dice): cryptanalysts and linguists, paid by the US Army.</summary>
    void SendUsArmy(int n)
    {
        var shared = Rng;
        Rng = Rng.Seeded(Mix(Seed, UsArmySent, 0x05A1));
        for (int i = 0; i < n && UsArmySent < Defs.UsArmyMax; i++, UsArmySent++)
        {
            (string name, Role role, string bio)? known = UsArmySent < Defs.UsArmyNotables.Length ? Defs.UsArmyNotables[UsArmySent] : null;
            var role = known?.role ?? (UsArmySent % 2 == 0 ? Role.Cryptanalyst : Role.Linguist);
            var p = NewPerson(role, known != null ? 3 : Rng.Chance(0.5) ? 2 : 3);
            p.Female = false;
            p.Name = known?.name ?? $"{Rng.Pick(Defs.UsFirst)} {Rng.Pick(Defs.UsSurnames)}";
            if (known != null) { p.Notable = known.Value.name; p.Bio = known.Value.bio; }
            p.Traits.Remove(Trait.NativeGerman);
            p.Traits.Remove(Trait.Japanese);
            p.American = true;
            p.Salary = 0;
            Candidates.Add(p);
        }
        Rng = shared;
        Emit(EventKind.Good, n == 1 ? "Another officer of the US Army's 6813th detachment is ready to join us. See Hire."
                                    : "Officers of the US Army's 6813th Signal Security Detachment are ready to join us, paid by the Army. See Hire.");
    }

    /// <summary>Whether this four-rotor menu goes to Dayton: our outstations are full and Dayton has a Bombe free.</summary>
    bool ToDayton(bool fourRotor)
    {
        if (!fourRotor || DaytonBombes == 0) return false;
        int dayton = DaytonJobs;
        return dayton < DaytonBombes && RemoteJobs.Count - dayton >= OutstationBombes;
    }
}
