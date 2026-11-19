using System.Collections.Generic;

namespace StationX.Sim;

/// <summary>
/// Traffic analysis. Before a key is broken, its messages' externals (callsigns, frequencies, routing, length) say
/// who is talking to whom. Logged for two of a network's messages in a day, they point the crib desk at the right
/// guess, and that day's cribs and Bombe runs are quicker.
/// </summary>
public sealed partial class GameState
{
    /// <summary>Messages logged per network-day ("Net:day").</summary>
    public Dictionary<string, int> SixtaLogged { get; set; } = new();
    /// <summary>Messages sent to the traffic analysts per network-day, so no more than are needed go.</summary>
    public Dictionary<string, int> SixtaRouted { get; set; } = new();

    public bool Analysed(Net net, int day) => SixtaLogged.TryGetValue(KeyId(net, day), out var n) && n >= Defs.SixtaNeeded;

    bool WantsAnalysis(Message m) =>
        Defs.NetOf(m.Net).Family == Family.Enigma && HasStation(StationType.TrafficAnalysis) &&
        KeyState(m.Net, m.Day) != KeyStatus.Broken &&
        SixtaRouted.GetValueOrDefault(KeyId(m.Net, m.Day)) < Defs.SixtaNeeded;

    static void Bump(Dictionary<string, int> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;
    void Bump(Dictionary<string, int> d, Message m) => Bump(d, KeyId(m.Net, m.Day));

    void LogExternals(Message m)
    {
        bool was = Analysed(m.Net, m.Day);
        Bump(SixtaLogged, m);
        if (!was && Analysed(m.Net, m.Day) && !KeyBroken(m.Net, m.Day))
            Emit(EventKind.Good, $"Traffic analysis has today's {Defs.NetOf(m.Net).Name} picture: the crib desk knows who is sending what. Cribs and Bombe runs for it go quicker today.");
    }
}
