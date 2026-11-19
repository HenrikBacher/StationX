using System;
using System.Collections.Generic;

namespace StationX.Sim;

/// <summary>One step on a path: the station, whether it can be skipped, and a short note.</summary>
public sealed record HandbookStep(StationType Type, bool Optional = false, string? Note = null);

/// <summary>The way a kind of traffic goes through the Park, step by step.</summary>
public sealed record HandbookPath(string Title, string Blurb, Net[] Nets, HandbookStep[][] Routes);

/// <summary>
/// The Park's working handbook: for each kind of cipher, the desks and machines its paper passes through. Kept beside
/// the routing rules (Complete, KeyRoute, AfterCrib) so it stays true to them.
/// </summary>
public static class Handbook
{
    static HandbookStep S(StationType t, string? note = null) => new(t, false, note);
    static HandbookStep Opt(StationType t, string? note = null) => new(t, true, note);

    public static readonly HandbookPath[] Paths =
    {
        new("Hand ciphers",
            "Police and Abwehr traffic in pencil-and-paper ciphers. A cryptanalyst works each message out on its own; some take more than one try.",
            new[] { Net.Police, Net.Abwehr },
            new[]
            {
                new[] { S(StationType.Inbox), S(StationType.Registration), S(StationType.HandCipher), S(StationType.Translation), S(StationType.Dispatch) },
            }),
        new("Enigma: breaking the day's key",
            "The keys change at midnight. One message a day per network goes to the cryptanalysts: a crib becomes a menu, a Bombe finds the settings, " +
            "a checking machine confirms them. Before the indicator change of May 1940, Zygalski sheets can do it instead.",
            new[] { Net.Red, Net.LightBlue, Net.Brown, Net.Chaffinch },
            new[]
            {
                new[] { S(StationType.Registration), Opt(StationType.TrafficAnalysis, "speeds the crib and Bombe"), S(StationType.Crib), S(StationType.Bombe), Opt(StationType.Checking, "without it, Bombe runs take longer") },
                new[] { S(StationType.Registration), S(StationType.Zygalski, "until May 1940") },
            }),
        new("Enigma: reading the messages",
            "Once the day's key is broken, every message on that network is set up on a Typex and read straight off, until midnight.",
            new[] { Net.Red, Net.LightBlue, Net.Brown, Net.Chaffinch, Net.Dolphin, Net.Shark },
            new[]
            {
                new[] { S(StationType.Inbox), S(StationType.Registration), S(StationType.Typex), S(StationType.Translation), Opt(StationType.Index, "in later chapters"), S(StationType.Dispatch) },
            }),
        new("Naval Enigma",
            "The Kriegsmarine's keys are harder: Banburismus cuts down the wheel orders a Bombe has to try. SHARK needs four-wheel Bombes and the short " +
            "weather cipher for cribs. From U-110, a fifth of naval messages are Offizier: enciphered twice, the inner layer unpicked by hand.",
            new[] { Net.Dolphin, Net.Shark },
            new[]
            {
                new[] { S(StationType.Registration), S(StationType.Crib), Opt(StationType.Banburismus, "Bombe runs far shorter"), S(StationType.Bombe, "SHARK: four-wheel Bombe") },
                new[] { S(StationType.Typex), S(StationType.HandCipher, "Offizier messages only"), S(StationType.Translation) },
            }),
        new("TUNNY",
            "The German high command's teleprinter cipher, the Lorenz machine. The wheel settings are found by hand in the Testery, faster on Newman's " +
            "Heath Robinson, fastest on Colossus. Then a Tunny machine prints the plain text.",
            new[] { Net.Tunny },
            new[]
            {
                new[] { S(StationType.Registration), S(StationType.Testery, "or Heath Robinson, or Colossus") },
                new[] { S(StationType.Inbox), S(StationType.Registration), S(StationType.TunnyMachine), S(StationType.Translation), Opt(StationType.Index), S(StationType.Dispatch) },
            }),
    };
}
