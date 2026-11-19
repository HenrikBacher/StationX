using System;

namespace StationX.Sim;

/// <summary>
/// Stores: the dockets, worksheets, carbons and card stock every message uses at registration. From the spring of 1940
/// the Park orders its own from the Stationery Office; an order takes most of a day to come by lorry. Run out and the
/// desks scrape by on the backs of old forms, at half speed. A standing order reorders by itself, at a premium.
/// </summary>
public sealed partial class GameState
{
    public double Stores { get; set; } = Defs.StoresStart;
    /// <summary>When the stores on order arrive (game minutes), or -1 if none are on the way.</summary>
    public double StoresDueAt { get; set; } = -1;
    public bool StandingOrder { get; set; }
    public double StoresUsedToday { get; set; }
    /// <summary>A running estimate of the stores used in a day, for the reorder point and the days-left figure.</summary>
    public double StoresPerDay { get; set; }
    public bool StoresOutWarned { get; set; }
    public bool StoresIntroduced { get; set; }

    public bool SuppliesActive => ActIndex >= Defs.StoresFromAct;
    public bool OutOfStores => SuppliesActive && Stores <= 0;
    public bool StoresOnOrder => StoresDueAt >= 0;
    /// <summary>Work speed at the desks: halved when the stores have run out.</summary>
    public double StoresWorkFactor => OutOfStores ? Defs.NoStoresWorkRate : 1;
    public int StoresOrderCost => (int)Math.Round(Defs.StoresOrderCost * (StandingOrder ? Defs.StandingOrderMarkup : 1));
    /// <summary>Days the stores in hand will last at the current rate (∞ before any use).</summary>
    public double StoresDaysLeft => StoresPerDay < 0.5 ? double.PositiveInfinity : Math.Max(0, Stores) / StoresPerDay;
    /// <summary>Below this, it's time to order: what two days use, and never less than a third of an order.</summary>
    public double StoresReorderPoint => Math.Max(Defs.StoresOrderSize / 3.0, 2 * StoresPerDay);
    public bool StoresLow => SuppliesActive && !StoresOnOrder && Stores < StoresReorderPoint;

    public string? CanOrderStores()
    {
        if (!SuppliesActive) return "The Foreign Office supplies the Park for now.";
        if (StoresOnOrder) return "An order is already on its way.";
        if (Stores + Defs.StoresOrderSize > Defs.StoresCap) return "The stores room is full.";
        if (Funds < StoresOrderCost) return $"Needs £{StoresOrderCost}.";
        return null;
    }

    public bool OrderStores()
    {
        if (CanOrderStores() != null) return false;
        Funds -= StoresOrderCost;
        StoresDueAt = Now + Defs.StoresLeadMinutes;
        return true;
    }

    public void SetStandingOrder(bool on) => StandingOrder = on;

    /// <summary>One message's worth, taken when it's registered.</summary>
    void UseStores()
    {
        if (!SuppliesActive) return;
        Stores = Math.Max(0, Stores - 1);
        StoresUsedToday++;
        if (Stores <= 0 && !StoresOutWarned)
        {
            StoresOutWarned = true;
            Emit(EventKind.Bad, "The stores have run out. The desks are making do on the backs of old forms, at half speed, until the next lorry comes.");
        }
    }

    void TickSupplies()
    {
        if (!SuppliesActive) return;
        if (!StoresIntroduced)
        {
            StoresIntroduced = true;
            Emit(EventKind.Info, "From now on the Park orders its own stores from the Stationery Office: every message needs its dockets and " +
                                 "worksheets. Keep an eye on them under Staff; an order takes most of a day to come.");
        }
        if (StoresOnOrder && Now >= StoresDueAt)
        {
            StoresDueAt = -1;
            Stores = Math.Min(Defs.StoresCap, Stores + Defs.StoresOrderSize);
            StoresOutWarned = false;
            Emit(EventKind.Info, $"The Stationery Office lorry has been: {Defs.StoresOrderSize} messages' worth of stores.");
        }
        if (StandingOrder && StoresLow && CanOrderStores() == null)
            OrderStores();
    }

    void DailySupplies()
    {
        StoresPerDay = StoresPerDay <= 0 ? StoresUsedToday : 0.6 * StoresPerDay + 0.4 * StoresUsedToday;
        StoresUsedToday = 0;
    }
}
