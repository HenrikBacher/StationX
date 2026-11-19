using System.Collections.Generic;
using System.Linq;

namespace StationX.Sim;

/// <summary>How a network's daily key is treated when Bombe and crib time is short.</summary>
public enum KeyOrder { Normal, First, Hold }

/// <summary>
/// Choosing what to break. There was never enough Bombe time: each morning the huts argued over which keys the Bombes
/// would run on first. A network put First jumps the queue at crib desks and Bombes; one on Hold isn't attempted,
/// leaving the time for the others while its traffic waits.
/// </summary>
public sealed partial class GameState
{
    public Dictionary<Net, KeyOrder> KeyOrders { get; set; } = new();

    public KeyOrder KeyOrderOf(Net n) => KeyOrders.TryGetValue(n, out var o) ? o : KeyOrder.Normal;

    public void SetKeyOrder(Net n, KeyOrder o)
    {
        if (o == KeyOrder.Normal) KeyOrders.Remove(n); else KeyOrders[n] = o;
    }

    /// <summary>Lower goes first: a First network's key attempt, then the rest of its traffic, then everything else.</summary>
    /// <summary>0 for paper of a request the player has put first, 1 otherwise.</summary>
    int Prio(Message m) => ContractById(m.ContractId) is { State: ContractState.Active, Priority: true } ? 0 : 1;

    int Rank(Message m) => KeyOrderOf(m.Net) != KeyOrder.First ? 2 : m.Carrier ? 0 : 1;

    /// <summary>True when two or more key attempts are queueing for crib desks or Bombes.</summary>
    public bool KeysQueueing =>
        Stations.Where(s => s.Type is StationType.Crib or StationType.Bombe or StationType.Bombe4)
                .Sum(s => s.Input.Count(id => Messages.TryGetValue(id, out var m) && m.Carrier)) >= 2;

    /// <summary>The next key order in the cycle Normal → First → Hold, for the key board's button.</summary>
    public static KeyOrder NextOrder(KeyOrder o) => o switch { KeyOrder.Normal => KeyOrder.First, KeyOrder.First => KeyOrder.Hold, _ => KeyOrder.Normal };
}
