using System;
using System.Linq;

namespace StationX.Sim;

/// <summary>
/// Baron Ōshima, Japan's ambassador in Berlin, reported to Tokyo everything Hitler and Ribbentrop told him, and in the autumn
/// of 1943 he toured the Atlantic Wall and cabled a description of it division by division. His cables went by the Japanese
/// diplomatic machine, read by the Americans and at the Park. Here: in the Colossus chapter and as D-Day's opens, Whitehall
/// asks for his reports through the Japanese desk; each one read before D-Day adds to the invasion's readiness.
/// </summary>
public sealed partial class GameState
{
    /// <summary>Ōshima's reports read (delivered) so far.</summary>
    public int OshimaRead { get; set; }

    /// <summary>What his reports add to D-Day's readiness.</summary>
    public double OshimaBonus => Act.Op == "overlord" ? Math.Min(Defs.OshimaMax, OshimaRead * Defs.OshimaPer) : 0;

    /// <summary>Offers a batch of Ōshima's reports (not must-do; a missed one isn't sent again).</summary>
    public Contract? OfferOshima(int qty, string flavour)
    {
        if (!JapanOpen || Mode == GameMode.FreePlay) return null;
        var c = new Contract
        {
            Id = NextId++,
            Issuer = Issuer.WarOffice,
            Net = Net.Jn25,
            Quantity = qty,
            Days = Defs.OshimaDays,
            Reward = Defs.OshimaReward,
            Rep = 3,
            Oshima = true,
            Title = "Baron Ōshima's reports from Berlin",
            Flavour = flavour + $" Each report read before D-Day adds {Defs.OshimaPer * 100:0.#}% to its readiness.",
            OfferedAt = Now,
            OfferExpires = Now + Defs.OfferLifetimeDays * Defs.MinutesPerDay,
        };
        Contracts.Add(c);
        Emit(EventKind.Info, $"New request from the {Defs.IssuerNames[c.Issuer]}: {c.Title}. Each one read before D-Day makes the invasion readier.");
        return c;
    }

    void ReadOshima(Message m)
    {
        if (ContractById(m.ContractId) is not { Oshima: true }) return;
        bool before = Act.Op == "overlord" ? CurrentOp != null : Campaign.IndexOf("D-Day") > ActIndex;
        if (before) OshimaRead++;
    }
}
