using System.Linq;

namespace StationX.Sim;

/// <summary>
/// Poaching. Once the Park had proved itself, everyone wanted its people: SIS's Section V for the Abwehr, the Admiralty's
/// Operational Intelligence Centre for the U-boat plot, SOE for its own ciphers, the Foreign Office for its diplomatic section.
/// From the SHARK chapter (where the call-up papers stop), every so often (own dice) a department asks for one of the Park's
/// best (three stars, not a notable, head, American or someone already leaving). Let them go for Whitehall's goodwill, lend
/// them for a few days (they come back knowing more), or refuse and spend some goodwill keeping them.
/// </summary>
public sealed partial class GameState
{
    public double LastPoachAt { get; set; } = -1e9;

    static readonly (string who, string why)[] Poachers =
    {
        ("SIS's Section V", "to read the Abwehr's traffic beside the people who run the double agents"),
        ("the Admiralty's Operational Intelligence Centre", "to sit by the U-boat plot and say what the decrypts mean as they come in"),
        ("SOE", "to make the agents' own ciphers safer than the poem codes they are using now"),
        ("the Foreign Office", "for the diplomatic section in Berkeley Street"),
        ("the Air Ministry's Air Intelligence", "to brief the bomber groups on the German night defences"),
    };

    void DailyPoaching()
    {
        if (Mode != GameMode.Campaign || Campaign.SharkFrom < 0 || ActIndex < Campaign.SharkFrom) return;
        if (Dilemmas.Count > 0 || Now - LastDilemmaAt < Defs.MinutesPerDay || Now - LastPoachAt < Defs.PoachEveryDays * Defs.MinutesPerDay) return;
        var r = Rng.Seeded(Mix(Seed, Day, 0x90AC));
        if (!r.Chance(Defs.PoachChance)) return;
        AskToPoach(r);
    }

    /// <summary>Who a department would ask for: the best of the cryptanalysts and linguists who could be spared at all.</summary>
    Staff? PoachTarget(Rng r) =>
        Staff.Where(s => s.Skill >= 3 && s.Role is Role.Cryptanalyst or Role.Linguist && !s.Leaving && s.Notable == null && s.HeadOf == null
                         && !s.American && !s.Arriving(Now) && !s.IsSick(Now))
             .OrderByDescending(s => s.Experience + r.Range(0.0, 3000.0)).FirstOrDefault();

    /// <summary>For screenshots and tests.</summary>
    public bool DebugPoach() => AskToPoach(Rng.Seeded(Mix(Seed, Day, 0x90AD)));

    bool AskToPoach(Rng r)
    {
        var who = PoachTarget(r);
        if (who == null) return false;
        LastPoachAt = Now;
        var (dept, why) = r.Pick(Poachers);
        string role = Defs.RoleWord(who.Role);
        string they = who.Female ? "she" : "he", them = who.Female ? "her" : "him";
        RaiseDilemma(new Dilemma
        {
            Service = Service.Security, StaffId = who.Id, Title = "A request for one of ours", Tag = "poach",
            Text = $"{char.ToUpper(dept[0])}{dept[1..]} asks for {who.Name} ({role}, ★★★) {why}. They have heard {they} is the best we have, and they are right.",
            Context = $"Letting {them} go buys goodwill in Whitehall. A loan of {Defs.PoachLendDays:0} days costs us {them} for a while, and {they} would come back knowing more. " +
                      "Refusing keeps the Park whole, but Whitehall notices who won't share.",
            Options =
            {
                new() { Label = $"Let {them} go", DismissStaff = true, Rep = Defs.PoachGiveRep, Morale = -0.01,
                    Outcome = $"{who.Name} clears {(who.Female ? "her" : "his")} desk and goes to {dept}. Whitehall remembers the favour." },
                new() { Label = $"Lend {them} for {Defs.PoachLendDays:0} days", Rep = Defs.PoachLendRep, Action = "poach-lend",
                    Outcome = $"{who.Name} goes to {dept} for {Defs.PoachLendDays:0} days, and will be back with a few new ideas." },
                new() { Label = "Refuse", Rep = -Defs.PoachRefuseRep, Action = "poach-keep",
                    Outcome = $"Commander Travis says no. {who.Name} hears of it and is quietly pleased; {dept} is not." },
            },
        });
        return true;
    }

    void PoachAction(string action, Staff? who)
    {
        if (who == null) return;
        if (action == "poach-lend")
        {
            who.Pinned = null;
            who.Experience += Defs.PoachLendExperience;
            SendOff(who, Defs.PoachLendDays, "secondment");
        }
        else if (action == "poach-keep") who.Morale = System.Math.Min(1, who.Morale + Defs.PoachKeptMorale);
    }
}
