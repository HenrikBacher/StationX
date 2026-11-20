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
                new[] { Opt(StationType.Inbox, "for traffic over a teleprinter line; riders bring the rest to registration"), S(StationType.Registration), S(StationType.HandCipher), S(StationType.Translation), S(StationType.Dispatch) },
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
                new[] { Opt(StationType.Inbox, "for traffic over a teleprinter line; riders bring the rest to registration"), S(StationType.Registration), S(StationType.Typex), S(StationType.Translation), Opt(StationType.Index, "in later chapters"), S(StationType.Dispatch) },
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
        new("Kisses: WERFT",
            "From Operation Claymore. The dockyards and weather stations sent their routine reports in WERFT, a hand cipher (about three hours' work a signal), and the same text " +
            "again in naval Enigma. Turn on WERFT on the Enigma keys card: once a hand-cipher desk has read one of the day's signals, that day's " +
            "DOLPHIN and SHARK keys are kissed. The menu takes a third of the time, the Bombe run is a fifth shorter, and the stop holds. Only good for the day it was sent.",
            new[] { Net.Werft },
            new[]
            {
                new[] { Opt(StationType.Inbox, "for traffic over a teleprinter line; riders bring the rest to registration"), S(StationType.Registration), S(StationType.HandCipher, "the kiss: a perfect crib"), S(StationType.Crib, "today's naval key"), S(StationType.Bombe, "a shorter run") },
            }),
        new("JN-25",
            "From the Blackout. The Japanese navy's fleet code, cabled from the Far East Combined Bureau and coming in overnight. A hand-cipher desk strips the " +
            "additive; then a Japanese desk looks the code groups up and puts them into English. Only linguists back from the Bedford Japanese course " +
            "(select a linguist: Learns) can work the Japanese desk; a few new linguists have already been.",
            new[] { Net.Jn25 },
            new[]
            {
                new[] { Opt(StationType.Inbox, "for traffic over a teleprinter line; riders bring the cables to registration"), S(StationType.Registration), S(StationType.HandCipher, "strips the additive"), S(StationType.Japanese, "Japanese readers only"), S(StationType.Dispatch) },
            }),
        new("TUNNY",
            "The German high command's teleprinter cipher, the Lorenz machine. The wheel settings are found by hand in the Testery, faster on Newman's " +
            "Heath Robinson, fastest on Colossus. Then a Tunny machine prints the plain text.",
            new[] { Net.Tunny },
            new[]
            {
                new[] { S(StationType.Registration), S(StationType.Testery, "or Heath Robinson, or Colossus") },
                new[] { Opt(StationType.Inbox, "for traffic over a teleprinter line; riders bring the rest to registration"), S(StationType.Registration), S(StationType.TunnyMachine), S(StationType.Translation), Opt(StationType.Index), S(StationType.Dispatch) },
            }),
    };
}
