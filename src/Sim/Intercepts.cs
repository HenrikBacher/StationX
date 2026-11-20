using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StationX.Sim;

/// <summary>How far a message has got: what a reader at the Park could see of it.</summary>
public enum ReadStage { Intercept, Decrypted, Translated }

/// <summary>A delivered message kept for the reader after it leaves the floor.</summary>
public sealed class ArchivedMsg
{
    public int Id { get; set; }
    public Net Net { get; set; }
    public int Day { get; set; }
    public double Created { get; set; }
    public bool Garbled { get; set; }
}

/// <summary>Everything the reader shows for one message. Generated, never stored: the same message always reads the same.</summary>
public sealed record InterceptText(
    string Station, string Log, string Preamble, string Cipher,
    string DecryptRaw, string German, string English, string Serial, bool Garbled);

/// <summary>
/// Procedural intercepts. A German plaintext is built from per-network templates (with a matching English translation),
/// then enciphered for real: Enigma I (rotors I-V, reflector B, plugboard) for the Army and Air Force keys and the
/// Abwehr, the naval four-rotor M4 for SHARK, a double transposition for the police hand cipher, and Lorenz-style
/// teleprinter cipher for Tunny. Daily keys come from the campaign seed, the network and the day.
/// </summary>
public static class Intercepts
{
    public static InterceptText For(int seed, int id, Net net, int day, DateOnly date, double created, bool garbled)
    {
        var r = new Random(Mix(seed, id, (int)net, 7919));
        var (de, en) = Plaintext(net, r, date);
        // Naval operators wrote Q for CH; the readable version restores it, as Hut 8's emenders did.
        string sent = Defs.NetOf(net).Service == Service.Kriegsmarine ? de.Replace("CH", "Q") : de;
        string letters = new(sent.Where(char.IsAsciiLetterUpper).ToArray());

        int minute = (int)(created % Defs.MinutesPerDay);
        string hhmm = $"{minute / 60:00}{minute % 60:00}";
        string dateStr = $"{date.Day}.{date.Month}.{date.Year % 100}";
        string station = StationFor(net);
        int kcs = 2800 + r.Next(0, 60) * 100 + r.Next(0, 4) * 25;
        string from = Callsign(r), to = Callsign(r);
        string log = $"{station}    {dateStr}    T.O.I. {hhmm}    {kcs} KC/S\nFROM {from}  TO {to}";

        string preamble, cipher, raw;
        var family = Defs.NetOf(net).Family;
        if (family == Family.Hand)
        {
            var key = new Random(Mix(seed, day, (int)net, 31));
            string k1 = Keyword(key), k2 = Keyword(key);
            cipher = Groups(Transpose(Transpose(Pad(letters, r), k1), k2), 5);
            preamble = $"{hhmm} = {letters.Length} = DOPPELKASTEN = ";
            raw = Groups(letters, 5);
        }
        else if (family == Family.Lorenz)
        {
            cipher = Tunny(de, r);
            preamble = $"QEP {r.Next(10, 99)} {Cap(r, 2)} {Cap(r, 2)} {Cap(r, 2)} {Cap(r, 2)} {Cap(r, 2)} {Cap(r, 2)}";   // the 12-letter wheel indicator, abbreviated
            raw = de;
        }
        else
        {
            bool naval = Defs.NetOf(net).Service == Service.Kriegsmarine;
            var m = Enigma.DailyKey(seed, net, day);
            string grund = Cap(r, 3), msgKey = Cap(r, 3);
            m.SetPositions(grund);
            string indicator = m.Encipher(msgKey);
            m.SetPositions(msgKey);
            string body = m.Encipher(letters);
            if (naval)
            {
                // Naval messages: four-letter groups, the indicator hidden among the first and last groups by bigram tables.
                string ind = Cap(r, 4), ind2 = Cap(r, 4);
                cipher = Groups(ind + ind2 + body + ind + ind2, 4);
                preamble = $"{hhmm} = {body.Length / 4 + 4} = ";
                raw = Groups(letters, 4);
            }
            else
            {
                // Army and Air Force: Grundstellung in clear, the enciphered message key, then a discriminant group.
                string kenn = Cap(r, 2) + Discriminant(net);
                cipher = Groups(kenn + body, 5);
                preamble = $"{hhmm} = 1TL = {body.Length + 5} = {grund} {indicator} =";
                raw = Groups(letters, 5);
            }
        }
        if (garbled) cipher = Garble(cipher, r);

        string serial = net switch
        {
            Net.Police => $"Police decode {r.Next(100, 999)}",
            Net.Abwehr => $"ISOS {r.Next(1000, 9999)}",
            Net.Dolphin or Net.Shark => $"ZTPG/{r.Next(1000, 99999)}",
            Net.Tunny => $"ZTPGU/{r.Next(100, 9999)}",
            Net.Werft => $"NS/W/{r.Next(100, 9999)}",
            _ => $"CX/MSS/{r.Next(100, 2999)}/T{r.Next(1, 60)}",
        };
        return new InterceptText(station, log, preamble, cipher, raw, de, en, serial, garbled);
    }

    /// <summary>Which stage a live message has reached.</summary>
    public static ReadStage StageOf(Message m)
    {
        if (m.Loc == Loc.Done) return ReadStage.Translated;
        return m.Next switch
        {
            StationType.Translation => ReadStage.Decrypted,
            StationType.Dispatch or StationType.Index => ReadStage.Translated,
            _ => ReadStage.Intercept,
        };
    }

    // ------------------------------------------------------------- helpers

    static int Mix(params int[] xs)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (int x in xs) { h ^= (uint)x; h *= 16777619; h ^= h >> 13; }
            return (int)(h & 0x7FFFFFFF);
        }
    }

    static string Cap(Random r, int n) => new(Enumerable.Range(0, n).Select(_ => (char)('A' + r.Next(26))).ToArray());
    static string Callsign(Random r) => $"{(char)('A' + r.Next(26))}{(char)('A' + r.Next(26))}{r.Next(1, 10)}";

    static string Discriminant(Net net) => net switch
    {
        Net.Red => "RED", Net.LightBlue => "BLU", Net.Chaffinch => "FIN", Net.Brown => "BRN", Net.Abwehr => "ABW", _ => "XYZ",
    } switch { var s => new string(s.Select(c => (char)('A' + (c - 'A' + 7) % 26)).ToArray()) };   // a fixed letter group per key

    static string StationFor(Net net) => net switch
    {
        Net.Red or Net.LightBlue or Net.Brown => "RAF CHICKSANDS",
        Net.Chaffinch => "WAR OFFICE Y, BEAUMANOR",
        Net.Dolphin or Net.Shark or Net.Werft => "ADMIRALTY Y, SCARBOROUGH",
        Net.Abwehr => "R.S.S. ARKLEY",
        Net.Tunny => "FOREIGN OFFICE, KNOCKHOLT",
        _ => "METROPOLITAN POLICE, DENMARK HILL",
    };

    public static string Groups(string s, int n)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i += n)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(s, i, Math.Min(n, s.Length - i));
        }
        return sb.ToString();
    }

    static string Garble(string cipher, Random r)
    {
        var c = cipher.ToCharArray();
        for (int i = 0; i < c.Length; i++)
            if (char.IsAsciiLetterUpper(c[i]) && r.NextDouble() < 0.07) c[i] = r.NextDouble() < 0.4 ? '-' : (char)('A' + r.Next(26));
        return new string(c);
    }

    static string Pad(string s, Random r) { while (s.Length % 5 != 0) s += (char)('A' + r.Next(26)); return s; }

    static readonly string[] Keywords = { "WINTERHILFE", "DONAUSTROM", "HEIMATLAND", "SCHWARZWALD", "OSTMARK", "FRUEHLING", "BERGKRISTALL", "ADLERHORST" };
    static string Keyword(Random r) => Keywords[r.Next(Keywords.Length)];

    /// <summary>Columnar transposition: write in rows under the keyword, read off by columns in alphabetical order.</summary>
    static string Transpose(string s, string key)
    {
        int n = key.Length;
        var order = Enumerable.Range(0, n).OrderBy(i => key[i]).ThenBy(i => i);
        var sb = new StringBuilder(s.Length);
        foreach (int col in order)
            for (int i = col; i < s.Length; i += n) sb.Append(s[i]);
        return sb.ToString();
    }

    /// <summary>Bletchley's notation for the 32 teleprinter characters: letters plus / 9 H 3 4 8 + 5 for the rest.</summary>
    const string Baudot = "/E4A9SIUHDRJNFCKTZLWHYPQOBG+MXV8";
    static string Tunny(string de, Random r)
    {
        var sb = new StringBuilder();
        int col = 0;
        foreach (char ch in de)
        {
            int code = ch == ' ' ? 4 : Math.Max(0, Baudot.IndexOf(ch));
            int key = r.Next(32);
            sb.Append(Baudot[(code ^ key) & 31]);
            if (++col % 40 == 0) sb.Append('\n');
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------- plaintexts

    static string Digits(int n) => string.Join(" ", n.ToString().Select(d => De[d - '0']));
    static readonly string[] De = { "NULL", "EINS", "ZWO", "DREI", "VIER", "FUENF", "SECHS", "SIEBEN", "ACHT", "NEUN" };
    static (string de, string en) P(string de, string en) => (de, en);
    static T Pick<T>(Random r, T[] xs) => xs[r.Next(xs.Length)];

    static (string de, string en) Square(Random r)
    {
        string sq = Pick(r, new[] { "AJ", "AK", "AL", "BC", "BD", "BE", "BF", "CA", "CB", "CF", "AE", "BB" });
        int n = r.Next(1000, 9999);
        return ($"QUADRAT {sq} {Digits(n)}", $"square {sq} {n}");
    }

    static (string de, string en) Time(Random r)
    {
        int h = r.Next(0, 24), m = r.Next(0, 12) * 5;
        int t = h * 100 + m;
        return ($"{Digits(t / 1000)} {Digits(t / 100 % 10)} {Digits(t / 10 % 10)} {Digits(t % 10)} UHR", $"{t:0000} hrs");
    }

    static (string de, string en) Plaintext(Net net, Random r, DateOnly date)
    {
        var parts = new List<(string de, string en)>();
        int n = r.NextDouble() < 0.4 ? 2 : 1;
        for (int i = 0; i < n; i++)
        {
            var p = Sentence(net, r, date);
            if (!parts.Contains(p)) parts.Add(p);
        }
        var head = Header(net, r);
        string de = head.de + " X " + string.Join(" X ", parts.Select(p => p.de)) + " X";
        string en = head.en + "\n\n" + string.Join(" ", parts.Select(p => p.en));
        return (de, en);
    }

    static (string de, string en) Header(Net net, Random r) => net switch
    {
        Net.Red => Pick(r, new[]
        {
            P("AN LUFTFLOTTE DREI VON FLIEGERKORPS EINS", "To: Luftflotte 3. From: Fliegerkorps I."),
            P("AN FLIEGERKORPS ACHT VON LUFTFLOTTE ZWO", "To: Fliegerkorps VIII. From: Luftflotte 2."),
            P("AN KG FUENF FUENF VON FLIEGERKORPS FUENF", "To: KG 55. From: Fliegerkorps V."),
            P("AN GEN QU LUFTWAFFE VON LUFTGAU WEST FRANKREICH", "To: Quartermaster-General, Luftwaffe. From: Luftgau Western France."),
        }),
        Net.LightBlue => Pick(r, new[]
        {
            P("AN FLIEGERFUEHRER AFRIKA VON ZEHNTES FLIEGERKORPS", "To: Fliegerführer Afrika. From: X Fliegerkorps."),
            P("AN ZEHNTES FLIEGERKORPS VON FLIEGERFUEHRER AFRIKA", "To: X Fliegerkorps. From: Fliegerführer Afrika."),
            P("AN JG ZWO SIEBEN VON FLIEGERFUEHRER AFRIKA", "To: JG 27. From: Fliegerführer Afrika."),
        }),
        Net.Brown => Pick(r, new[]
        {
            P("AN KG EINS NULL NULL VON LN VERSUCHSREGIMENT", "To: KG 100. From: Air Signals Experimental Regiment."),
            P("AN X GERAET SENDER CHERBOURG VON KG EINS NULL NULL", "To: X-Gerät transmitter, Cherbourg. From: KG 100."),
            P("AN LUFTFLOTTE DREI VON KG EINS NULL NULL", "To: Luftflotte 3. From: KG 100."),
        }),
        Net.Chaffinch => Pick(r, new[]
        {
            P("AN OKH GEN QU VON PANZERGRUPPE AFRIKA", "To: OKH, Quartermaster-General. From: Panzergruppe Afrika."),
            P("AN DAK VON PANZERGRUPPE AFRIKA OBERQUARTIERMEISTER", "To: Afrika Korps. From: Panzergruppe Afrika, chief supply officer."),
            P("AN PANZERGRUPPE AFRIKA VON FUENFZEHNTE PANZERDIVISION", "To: Panzergruppe Afrika. From: 15th Panzer Division."),
        }),
        Net.Dolphin => Pick(r, new[]
        {
            P("AN MARINEGRUPPE WEST VON FUEHRER DER MINENSUCHBOOTE WEST", "To: Naval Group West. From: Officer commanding minesweepers, West."),
            P("AN ADMIRAL NORWEGEN VON MARINEGRUPPE NORD", "To: Admiral Norway. From: Naval Group North."),
            P("AN BDU VON ADMIRAL FRANKREICH", "To: BdU (U-boat command). From: Admiral France."),
        }),
        Net.Shark => Pick(r, new[]
        {
            P($"VON U {Digits(r.Next(80, 620))}", "From: a U-boat at sea."),
            P("AN ALLE BOOTE IM ATLANTIK VON BDU", "To: all U-boats in the Atlantic. From: BdU."),
            P("AN BDU OP", "To: BdU Operations."),
        }),
        Net.Police => Pick(r, new[]
        {
            P("AN HAUPTAMT ORDNUNGSPOLIZEI BERLIN VON BDO KRAKAU", "To: Ordnungspolizei headquarters, Berlin. From: BdO Kraków."),
            P("AN BDO WARSCHAU VON POLIZEIREGIMENT LUBLIN", "To: BdO Warsaw. From: Police Regiment Lublin."),
            P("AN POLIZEIBATAILLON ZWO NULL ACHT VON BDO POSEN", "To: Police Battalion 208. From: BdO Posen."),
        }),
        Net.Abwehr => Pick(r, new[]
        {
            P("AN ABWEHR HAMBURG VON KO PORTUGAL LISSABON", "To: Abwehr Hamburg. From: Abwehr station, Lisbon."),
            P("AN ABWEHR BERLIN VON KO SPANIEN MADRID", "To: Abwehr Berlin. From: Abwehr station, Madrid."),
            P("AN KO SPANIEN VON NEBENSTELLE ALGECIRAS", "To: Abwehr station, Madrid. From: sub-station, Algeciras."),
        }),
        Net.Werft => Pick(r, new[]
        {
            P("AN HAFENKOMMANDANT BREST VON KRIEGSMARINEWERFT BREST", "To: Port commandant, Brest. From: Naval dockyard, Brest."),
            P("AN MARINEGRUPPE WEST VON MARINEWETTERDIENST", "To: Naval Group West. From: Naval weather service."),
            P("AN KRIEGSMARINEWERFT KIEL VON HAFENSCHUTZFLOTTILLE KIEL", "To: Naval dockyard, Kiel. From: Harbour defence flotilla, Kiel."),
            P("AN ADMIRAL NORWEGEN VON MARINEWERFT TRONDHEIM", "To: Admiral Norway. From: Naval dockyard, Trondheim."),
        }),
        Net.Tunny => Pick(r, new[]
        {
            P("AN OKH GENSTDH OP ABT VON HEERESGRUPPE SUED", "To: OKH, General Staff, Operations Branch. From: Army Group South."),
            P("AN OKW WFST VON OB WEST", "To: OKW Operations Staff. From: C-in-C West."),
            P("AN HEERESGRUPPE MITTE VON OKH", "To: Army Group Centre. From: OKH."),
        }),
        _ => P("AN", "To:"),
    };

    static bool In(DateOnly d, int y0, int m0, int d0, int y1, int m1, int d1) =>
        d >= new DateOnly(y0, m0, d0) && d <= new DateOnly(y1, m1, d1);

    static readonly (string de, string en)[] Names =
    {
        P("MUELLER", "Müller"), P("SCHMIDT", "Schmidt"), P("WEBER", "Weber"), P("BECKER", "Becker"), P("HOFFMANN", "Hoffmann"),
        P("KRUEGER", "Krüger"), P("BRAUN", "Braun"), P("ZIMMERMANN", "Zimmermann"), P("HARTMANN", "Hartmann"), P("KOCH", "Koch"),
        P("RICHTER", "Richter"), P("WOLF", "Wolf"), P("SCHROEDER", "Schröder"), P("NEUMANN", "Neumann"), P("FUCHS", "Fuchs"),
    };

    static (string de, string en)[] Ranks(Net net) => Defs.NetOf(net).Service switch
    {
        Service.Kriegsmarine => new[] { P("LEUTNANT ZUR SEE", "Leutnant zur See"), P("OBERLEUTNANT ZUR SEE", "Oberleutnant zur See"), P("KAPITAENLEUTNANT", "Kapitänleutnant"), P("KORVETTENKAPITAEN", "Korvettenkapitän"), P("OBERMAAT", "Obermaat"), P("FUNKMAAT", "Funkmaat") },
        Service.Security when net == Net.Police => new[] { P("WACHTMEISTER", "Wachtmeister"), P("OBERWACHTMEISTER", "Oberwachtmeister"), P("LEUTNANT", "Leutnant"), P("HAUPTMANN", "Hauptmann"), P("MAJOR", "Major") },
        Service.Security => new[] { P("SONDERFUEHRER", "Sonderführer"), P("HAUPTMANN", "Hauptmann"), P("MAJOR", "Major"), P("OBERST", "Oberst") },
        _ => new[] { P("LEUTNANT", "Leutnant"), P("OBERLEUTNANT", "Oberleutnant"), P("HAUPTMANN", "Hauptmann"), P("MAJOR", "Major"), P("FELDWEBEL", "Feldwebel"), P("UNTEROFFIZIER", "Unteroffizier"), P("OBERGEFREITER", "Obergefreiter") },
    };

    static string Ord(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    /// <summary>
    /// The everyday procedure every network carried, alongside the news: acknowledgements, repeats, call-sign changes,
    /// leave, promotions and birthday greetings. Hut 6 lived on such routine; much of it made good cribs. Two lines per
    /// draw, so routine stays a minority beside the network's own traffic.
    /// </summary>
    static void Routine(Net net, Random r, DateOnly d, List<(string de, string en)> pool)
    {
        var t = new Tale(r, d);
        t.Set("rank", Pick(r, Ranks(net)));
        t.Set("rank2", Pick(r, Ranks(net)));
        t.Set("name", Pick(r, Names));
        int nr = r.Next(2, 400), day = r.Next(1, 28), back = Math.Min(28, day + r.Next(7, 15));
        t.Set("nr", Digits(nr), nr.ToString());
        t.Set("day", Digits(day), Ord(day));
        t.Set("back", Digits(back), Ord(back));
        t.Set("time", Time(r));
        t["table"] = ((char)('A' + r.Next(8))).ToString();
        t["sq"] = Pick(r, new[] { "AJ", "AK", "AL", "BC", "BD", "BE", "BF", "CA", "CB", "CF", "AE", "BB" });
        bool naval = Defs.NetOf(net).Service == Service.Kriegsmarine;
        var item = naval
            ? Pick(r, new[] { P("ERSATZROEHREN FUER FUNKEMPFAENGER", "spare valves for the wireless receivers"), P("TAUWERK", "rope"), P("SCHMIEROEL", "lubricating oil"), P("KARTEN QUADRAT AJ", "charts for square AJ") })
            : Pick(r, new[] { P("REIFEN", "tyres"), P("ZUENDKERZEN", "spark plugs"), P("DECKEN", "blankets"), P("BATTERIEN FUER FUNKGERAETE", "batteries for wireless sets"), P("SCHREIBMASCHINENBAENDER", "typewriter ribbons") });
        t.Set("item", item);
        int n = r.Next(2, 60);
        t.Set("n", Digits(n), n.ToString());
        var lines = new List<(string de, string en)>
        {
            t.Pair("KEINE BESONDEREN {EREIGNISSE|VORKOMMNISSE}", "Nothing {special|unusual} to report."),
            t.Pair("SPRUCH NR {nr} {VERSTUEMMELT|NICHT ENTZIFFERBAR} ERBITTE WIEDERHOLUNG", "Message no. {nr} {corrupt|undecipherable}. Request repeat."),
            t.Pair("SPRUCH NR {nr} VON {time} ERHALTEN", "Message no. {nr} of {time} received."),
            t.Pair("AB {time} NEUE RUFZEICHEN GEMAESS TABELLE {table}", "New call signs from {time}, as per table {table}."),
            t.Pair("FUNKSTILLE BIS {time}", "Wireless silence until {time}."),
            t.Pair("HERZLICHE GLUECKWUENSCHE ZUM GEBURTSTAG {rank} {name}", "Hearty birthday congratulations to {rank} {name}."),
            t.Pair("{rank} {name} URLAUB VOM {day} BIS {back} GENEHMIGT", "{rank} {name}: leave granted from the {day} to the {back}."),
            t.Pair("{rank} {name} MIT WIRKUNG VOM {day} BEFOERDERT ZUM {rank2}", "{rank} {name} promoted to {rank2} with effect from the {day}."),
            t.Pair("ERBITTE {n} {item}", "Request {n} {item}."),
            t.Pair("{rank} {name} {VERSETZT|KOMMANDIERT} ZU {STAB|ERSATZTRUPPENTEIL}", "{rank} {name} {posted|detached} to {staff|the depot}."),
        };
        if (naval) lines.Add(t.Pair("STANDORT QUADRAT {sq} ALLES KLAR", "Position square {sq}. All well."));
        if (net == Net.Abwehr) lines.Add(t.Pair("FUER AGENTEN {n} TAUSEND {ESCUDOS|PESETAS} UEBERWIESEN", "{n} thousand {escudos|pesetas} sent for agents."));
        pool.Add(lines[r.Next(lines.Count)]);
        pool.Add(lines[r.Next(lines.Count)]);
    }

    /// <summary>
    /// One sentence of traffic, true to the date: routine traffic for the network as it stood then, plus (weighted up)
    /// whatever that part of the German forces was doing in that week of the war.
    /// </summary>
    static (string de, string en) Sentence(Net net, Random r, DateOnly d)
    {
        int a = r.Next(4, 40), b = r.Next(1, 15), c = r.Next(2, 28);
        var t = Time(r);
        var pool = new List<(string de, string en)>();
        void Add(params (string de, string en)[] xs) => pool.AddRange(xs);
        void Now(params (string de, string en)[] xs) { pool.AddRange(xs); pool.AddRange(xs); pool.AddRange(xs); }   // topical: three times as likely
        Routine(net, r, d, pool);

        switch (net)
        {
            case Net.Red:
            {
                bool france = d >= new DateOnly(1940, 6, 25);
                var unit = Pick(r, new[] { P("KG ZWO", "KG 2"), P("KG FUENF FUENF", "KG 55"), P("KG ZWO SIEBEN", "KG 27"), P("JG ZWO SECHS", "JG 26"), P("KG EINS NULL NULL", "KG 100"), P("STG ZWO", "StG 2") });
                var field = france
                    ? Pick(r, new[] { P("VILLACOUBLAY", "Villacoublay"), P("CHARTRES", "Chartres"), P("BEAUVAIS", "Beauvais"), P("CAEN", "Caen"), P("SAINT OMER", "St Omer"), P("ABBEVILLE", "Abbeville"), P("VANNES", "Vannes") })
                    : Pick(r, new[] { P("MUENSTER", "Münster"), P("DORTMUND", "Dortmund"), P("KOELN BUTZWEILERHOF", "Cologne-Butzweilerhof"), P("FRANKFURT RHEIN MAIN", "Frankfurt Rhein-Main"), P("JEVER", "Jever"), P("WESTERLAND", "Westerland") });
                Add(P($"{unit.de} STAND {t.de} EINSATZBEREIT {Digits(a)} FLUGZEUGE BEDINGT EINSATZBEREIT {Digits(b)}", $"{unit.en}, state at {t.en}: {a} aircraft serviceable, {b} partly serviceable."),
                    P($"FLIEGERHORST {field.de} MELDET BETRIEBSSTOFF {Digits(a * 10)} KUBIKMETER BOMBEN SC FUENF NULL NULL {Digits(c)} STUECK", $"Airfield {field.en} reports fuel {a * 10} cubic metres; {c} SC 500 bombs."),
                    P($"{unit.de} VERLEGT AM {Digits(c)} NACH {field.de} BODENPERSONAL MIT BAHN", $"{unit.en} transfers to {field.en} on the {c}th. Ground personnel by rail."),
                    P($"WETTER {field.de} {t.de} BEWOELKUNG {Digits(b % 10)} ZEHNTEL SICHT {Digits(a)} KM", $"Weather at {field.en}, {t.en}: cloud {b % 10}/10, visibility {a} km."));
                if (d < new DateOnly(1940, 5, 10))
                    Now(P($"AUFKLAERUNG UEBER SCAPA FLOW {t.de} ERGEBNIS {Digits(b % 4 + 1)} SCHLACHTSCHIFFE IM HAFEN", $"Reconnaissance over Scapa Flow at {t.en}: {b % 4 + 1} battleships in harbour."),
                        P($"KG ZWO SECHS ANGRIFF AUF GELEITZUG VOR DER SCHOTTISCHEN KUESTE", "KG 26 to attack the convoy off the Scottish coast."));
                if (In(d, 1940, 5, 10, 1940, 5, 25))
                    Now(P($"{unit.de} ANGRIFF AUF MAASBRUECKEN BEI SEDAN {t.de}", $"{unit.en} to attack the Meuse bridges at Sedan at {t.en}."),
                        P("STUKAS VOR FUER PANZERSPITZEN RAUM SEDAN", "Stukas forward in support of the armoured spearheads near Sedan."));
                if (In(d, 1940, 5, 26, 1940, 6, 4))
                    Now(P($"ANGRIFF AUF SCHIFFSANSAMMLUNGEN VOR DUENKIRCHEN MIT ALLEN KRAEFTEN {t.de}", $"Attack the concentrations of shipping off Dunkirk with all forces at {t.en}."),
                        P("STRAND OSTWAERTS DUENKIRCHEN VOLL VON TRUPPEN", "Beaches east of Dunkirk crowded with troops."));
                if (In(d, 1940, 7, 10, 1940, 8, 12))
                    Now(P($"GELEITZUG IM KANAL HOEHE DOVER KURS WEST ANGREIFEN {t.de}", $"Attack the convoy in the Channel off Dover, westbound, at {t.en}."),
                        P($"FUNKMESSSTATION VENTNOR BOMBARDIEREN", "Bomb the radar station at Ventnor."));
                if (In(d, 1940, 8, 13, 1940, 9, 6))
                {
                    var rafField = Pick(r, new[] { P("BIGGIN HILL", "Biggin Hill"), P("KENLEY", "Kenley"), P("MANSTON", "Manston"), P("HORNCHURCH", "Hornchurch"), P("NORTH WEALD", "North Weald"), P("DETLING", "Detling") });
                    Now(P($"ADLERANGRIFF {unit.de} ZIEL FLUGPLATZ {rafField.de} {t.de}", $"Adler attack: {unit.en} to attack the airfield at {rafField.en} at {t.en}."),
                        P($"JAGDSCHUTZ JG ZWO SECHS FUER ANGRIFF {rafField.de}", $"JG 26 to provide fighter cover for the attack on {rafField.en}."),
                        P($"ERFOLGSMELDUNG {rafField.de} ROLLFELD MIT {Digits(a)} TREFFERN", $"Report of success: {a} hits on the landing ground at {rafField.en}."));
                }
                if (In(d, 1940, 9, 7, 1940, 11, 9))
                    Now(P($"{unit.de} GREIFT HEUTE NACHT LONDON DOCKS AN ZIELZEIT {t.de}", $"{unit.en} will attack the London docks tonight. Time over target {t.en}."),
                        P($"KNICKEBEIN SENDER CHERBOURG RICHTUNG LONDON AB {t.de}", $"Knickebein beam transmitter at Cherbourg to be laid on London from {t.en}."));
                if (In(d, 1940, 11, 9, 1940, 11, 15))
                    Now(P("UNTERNEHMEN MONDSCHEINSONATE ZIEL KORN", "Operation MONDSCHEINSONATE (Moonlight Sonata). Target: KORN."),
                        P("X GERAET KG EINS NULL NULL ALS BELEUCHTER FUER ZIEL KORN", "X-Gerät: KG 100 to act as fire-raisers for target KORN."));
                if (In(d, 1940, 11, 16, 1941, 5, 16))
                {
                    var city = Pick(r, new[] { P("BIRMINGHAM", "Birmingham"), P("BRISTOL", "Bristol"), P("SOUTHAMPTON", "Southampton"), P("LIVERPOOL", "Liverpool"), P("PLYMOUTH", "Plymouth"), P("HULL", "Hull"), P("CLYDEBANK", "Clydebank"), P("BELFAST", "Belfast"), P("LONDON", "London") });
                    Now(P($"{unit.de} GREIFT HEUTE NACHT {city.de} AN ZIELZEIT {t.de}", $"{unit.en} will attack {city.en} tonight. Time over target {t.en}."),
                        P($"X GERAET SENDER RICHTUNG {city.de} AB {t.de}", $"X-Gerät beams to be laid on {city.en} from {t.en}."));
                }
                if (In(d, 1941, 5, 15, 1941, 6, 1))
                    Now(P("UNTERNEHMEN MERKUR FALLSCHIRMJAEGER ABSPRUNG FLUGPLATZ MALEME", "Operation MERKUR: paratroops to drop on Maleme airfield."),
                        P($"FLIEGERKORPS ACHT ANGRIFF AUF FLOTTE NOERDLICH KRETA {t.de}", $"Fliegerkorps VIII to attack the fleet north of Crete at {t.en}."));
                if (In(d, 1941, 5, 20, 1941, 7, 15))
                    Now(P($"{unit.de} VERLEGT NACH OSTEN NEUER EINSATZRAUM POSEN", $"{unit.en} moves east. New operational area: Posen."),
                        P("LUFTFLOTTE ZWO GEFECHTSSTAND NACH WARSCHAU", "Luftflotte 2 battle headquarters to Warsaw."));
                if (In(d, 1944, 6, 6, 1944, 7, 31))
                    Now(P("INVASION NORMANDIE ALLE VERFUEGBAREN JAGDGRUPPEN SOFORT NACH FRANKREICH", "Invasion in Normandy. All available fighter Gruppen to France at once."),
                        P($"{unit.de} FLUGPLATZ DURCH JABOS ZERSTOERT AUSWEICHPLATZ BEZIEHEN", $"{unit.en}: airfield destroyed by fighter-bombers. Move to the alternative field."));
                if (In(d, 1944, 9, 1, 1944, 9, 30))
                    Now(P("LUFTLANDUNG RAUM ARNHEIM NIMWEGEN EINDHOVEN ALLE KRAEFTE GEGEN BRUECKEN", "Airborne landings at Arnhem, Nijmegen and Eindhoven. All forces against the bridges."),
                        P("II SS PANZERKORPS RAUM ARNHEIM JAGDSCHUTZ ERBETEN", "II SS Panzer Corps, Arnhem area, requests fighter cover."));
                if (In(d, 1944, 11, 20, 1945, 1, 10))
                    Now(P($"JAGDGESCHWADER VERLEGEN NACH WESTEN BETRIEBSSTOFF FUER {Digits(b % 5 + 2)} TAGE BEREITSTELLEN", $"Fighter Geschwader moving west. Fuel for {b % 5 + 2} days to be laid in."),
                        P("ERDKAEMPFUNTERSTUETZUNG FUER SECHSTE PANZERARMEE VORBEREITEN", "Prepare ground-attack support for Sixth Panzer Army."));
                if (d >= new DateOnly(1945, 1, 11))
                    Now(P("BETRIEBSSTOFFLAGE KATASTROPHAL NUR NOCH SCHWERPUNKTEINSAETZE", "Fuel situation catastrophic. Only operations at the main point of effort."),
                        P($"{unit.de} VERLEGT NACH {Pick(r, new[] { "PRAG", "SALZBURG", "FLENSBURG" })}", $"{unit.en} moving to the rear."));
                break;
            }
            case Net.LightBlue:
            {
                var field = Pick(r, new[] { P("DERNA", "Derna"), P("GAZALA", "Gazala"), P("MARTUBA", "Martuba"), P("BENGASI", "Benghazi"), P("CASTEL BENITO", "Castel Benito"), P("GAMBUT", "Gambut") });
                Add(P($"STG DREI EINSATZBEREIT {Digits(b + 4)} JU EINS ACHT SIEBEN", $"StG 3: {b + 4} Ju 87s serviceable."),
                    P($"SANDSTURM {field.de} STARTVERBOT BIS AUF WEITERES", $"Sandstorm at {field.en}. All take-offs forbidden until further notice."),
                    P($"FLUGPLATZ {field.de} BETRIEBSSTOFF NUR NOCH {Digits(a)} TONNEN ERBITTE ZUFUEHRUNG", $"Airfield {field.en} has only {a} tons of fuel left. Request supply."),
                    P($"GELEITSCHUTZ FUER DAMPFER NACH BENGASI AB {t.de} DURCH JG ZWO SIEBEN", $"Fighter escort for steamers bound for Benghazi from {t.en}, by JG 27."));
                if (In(d, 1941, 1, 10, 1941, 5, 31) || In(d, 1942, 1, 1, 1942, 5, 15))
                    Now(P($"ZEHNTES FLIEGERKORPS ANGRIFF AUF MALTA HAFEN LA VALETTA {t.de}", $"X Fliegerkorps to attack Malta, Valletta harbour, at {t.en}."),
                        P("FLUGPLATZ LUQA UND HAL FAR ERNEUT BOMBARDIEREN", "Bomb the airfields at Luqa and Hal Far again."));
                if (In(d, 1941, 4, 10, 1941, 11, 27))
                    Now(P($"STUKAANGRIFF AUF FESTUNG TOBRUK HAFEN {t.de}", $"Stuka attack on the Tobruk fortress harbour at {t.en}."));
                if (In(d, 1941, 11, 18, 1941, 12, 31))
                    Now(P("FEINDLICHER GROSSANGRIFF AUS RAUM SIDI OMAR ALLE VERFUEGBAREN KRAEFTE EINSETZEN", "Major enemy offensive from the Sidi Omar area. Commit all available forces."),
                        P($"FLUGPLATZ GAMBUT GERAEUMT {Digits(b)} FLUGZEUGE ZURUECKGELASSEN", $"Gambut airfield abandoned; {b} aircraft left behind."));
                if (In(d, 1942, 5, 26, 1942, 6, 21))
                    Now(P("STUKAANGRIFFE AUF BIR HACHEIM FORTSETZEN", "Continue the Stuka attacks on Bir Hacheim."),
                        P("TOBRUK GEFALLEN", "Tobruk has fallen."));
                if (In(d, 1942, 7, 1, 1942, 11, 4))
                    Now(P($"JAGDSCHUTZ UEBER EL ALAMEIN AB {t.de}", $"Fighter cover over El Alamein from {t.en}."),
                        P($"MARSEILLE {Digits(a % 9 + 1)} ABSCHUESSE", $"Marseille: {a % 9 + 1} victories."));
                break;
            }
            case Net.Brown:
            {
                var sender = Pick(r, new[] { P("ANTON", "ANTON"), P("BRUNO", "BRUNO"), P("CAESAR", "CAESAR"), P("WOTAN", "WOTAN") });
                var city = Pick(r, new[] { P("LONDON", "London"), P("COVENTRY", "Coventry"), P("BIRMINGHAM", "Birmingham"), P("LIVERPOOL", "Liverpool"), P("BRISTOL", "Bristol"), P("PLYMOUTH", "Plymouth"), P("GLASGOW", "Glasgow") });
                Add(P($"SENDER {sender.de} RICHTSTRAHL {Digits(a * 9 % 360)} GRAD {Digits(b)} MINUTEN AB {t.de}", $"Transmitter {sender.en}: beam bearing {a * 9 % 360} degrees {b} minutes, from {t.en}."),
                    P($"KREUZSTRAHL VORSIGNAL {Digits(c)} KILOMETER VOR ZIEL", $"Cross-beam: advance signal {c} km before the target."),
                    P($"KG EINS NULL NULL {Digits(b)} MASCHINEN EINSATZBEREIT", $"KG 100: {b} aircraft serviceable."),
                    P($"STOERUNG DES STRAHLS FESTGESTELLT FREQUENZWECHSEL AUF {Digits(a + 60)} KOMMA {Digits(b % 10)}", $"Interference with the beam observed. Change frequency to {a + 60}.{b % 10}."));
                if (In(d, 1940, 9, 7, 1941, 5, 16))
                    Now(P($"X GERAET RICHTUNG {city.de} ZIELZEIT {t.de}", $"X-Gerät beams laid on {city.en}; time over target {t.en}."),
                        P($"KG EINS NULL NULL ALS BELEUCHTER FUER ZIEL {city.de}", $"KG 100 to act as fire-raisers for {city.en}."));
                if (In(d, 1940, 11, 9, 1940, 11, 15))
                    Now(P("UNTERNEHMEN MONDSCHEINSONATE ZIEL KORN", "Operation MONDSCHEINSONATE (Moonlight Sonata). Target: KORN."));
                break;
            }
            case Net.Chaffinch:
            {
                var unit = Pick(r, new[] { P("FUENFZEHNTE PANZERDIVISION", "15th Panzer Division"), P("EINUNDZWANZIGSTE PANZERDIVISION", "21st Panzer Division"), P("NEUNZIGSTE LEICHTE DIVISION", "90th Light Division"), P("DAK", "Afrika Korps") });
                var ship = Pick(r, new[] { P("ANKARA", "ANKARA"), P("TINOS", "TINOS"), P("PROCIDA", "PROCIDA"), P("ATLAS", "ATLAS"), P("ANNA ZIPPITELLI", "ANNA ZIPPITELLI") });
                var port = Pick(r, new[] { P("TRIPOLIS", "Tripoli"), P("BENGASI", "Benghazi"), P("TOBRUK", "Tobruk") });
                Add(P($"{unit.de} MELDET EINSATZBEREIT PANZER DREI {Digits(a)} PANZER VIER {Digits(b)} IN INSTANDSETZUNG {Digits(c)}", $"{unit.en} reports serviceable: {a} Pz III, {b} Pz IV; {c} under repair."),
                    P($"DAMPFER {ship.de} MIT {Digits(a * 50)} TONNEN BETRIEBSSTOFF {port.de} EINGELAUFEN", $"Steamer {ship.en} arrived {port.en} with {a * 50} tons of fuel."),
                    P($"DAMPFER {ship.de} LAEUFT {Digits(c)} AUS NEAPEL AUS LADUNG MUNITION UND {Digits(a)} PANZER", $"Steamer {ship.en} sails from Naples on the {c}th, carrying ammunition and {a} tanks."),
                    P($"{unit.de} VERPFLEGUNGSSTAERKE {Digits(a * 300)} WASSER {Digits(b)} LITER JE KOPF UND TAG", $"{unit.en} ration strength {a * 300}; water {b} litres per head per day."));
                if (In(d, 1941, 2, 12, 1941, 4, 15))
                    Now(P("GENERALLEUTNANT ROMMEL IN TRIPOLIS EINGETROFFEN", "Generalleutnant Rommel has arrived in Tripoli."),
                        P("VORMARSCH AUF AGEDABIA BEGINNT", "The advance on Agedabia begins."));
                if (In(d, 1941, 11, 1, 1942, 1, 15) || In(d, 1942, 8, 25, 1942, 9, 10))
                    Now(P($"BETRIEBSSTOFFLAGE ERNST VORRAT REICHT NUR FUER {Digits(b % 4 + 1)} TAGE", $"Fuel situation serious. Stocks will last only {b % 4 + 1} days."),
                        P($"GELEITZUG MIT BETRIEBSSTOFF VERSENKT ERBITTE SOFORTIGEN ERSATZ", "Fuel convoy sunk. Request immediate replacement."));
                if (In(d, 1941, 11, 18, 1941, 12, 31))
                    Now(P("FEINDLICHER GROSSANGRIFF RAUM SIDI REZEGH PANZERSCHLACHT IM GANGE", "Major enemy offensive near Sidi Rezegh. Tank battle in progress."));
                if (In(d, 1942, 5, 26, 1942, 6, 21))
                    Now(P("ANGRIFF AUF GAZALA STELLUNG UMGEHUNG SUEDLICH BIR HACHEIM", "Attack on the Gazala line: outflanking south of Bir Hacheim."),
                        P($"IN TOBRUK ERBEUTET {Digits(a * 100)} TONNEN BETRIEBSSTOFF", $"Captured in Tobruk: {a * 100} tons of fuel."));
                if (In(d, 1942, 10, 23, 1942, 11, 8))
                    Now(P("FEINDLICHER GROSSANGRIFF EL ALAMEIN PANZERARMEE HAELT", "Major enemy attack at El Alamein. Panzerarmee holding."),
                        P($"PANZERARMEE NUR NOCH {Digits(b + 20)} EINSATZBEREITE PANZER RUECKZUG AUF FUKA", $"Panzerarmee down to {b + 20} serviceable tanks. Withdrawal to Fuka."));
                if (d >= new DateOnly(1942, 11, 8))
                    Now(P("LANDUNG STARKER ANGLOAMERIKANISCHER KRAEFTE IN ALGERIEN UND MAROKKO", "Strong Anglo-American forces have landed in Algeria and Morocco."));
                break;
            }
            case Net.Dolphin:
            {
                var port = Pick(r, new[] { P("BREST", "Brest"), P("LORIENT", "Lorient"), P("SAINT NAZAIRE", "St Nazaire"), P("LA PALLICE", "La Pallice"), P("KIEL", "Kiel"), P("BERGEN", "Bergen"), P("TRONDHEIM", "Trondheim") });
                var sq = Square(r);
                int boat = r.Next(30, 600);
                Add(P($"U {Digits(boat)} EINLAUFEN {port.de} AM {Digits(c)} {t.de} SPERRBRECHER ERBETEN", $"U-{boat} entering {port.en} on the {c}th at {t.en}. Request Sperrbrecher escort."),
                    P($"GELEIT AB {port.de} {t.de} TREFFPUNKT {sq.de}", $"Escort leaves {port.en} at {t.en}. Rendezvous {sq.en}."),
                    P($"MINENSUCHFLOTTILLE {Digits(b)} WEG GRUEN GERAEUMT {Digits(c)} MINEN GESCHNITTEN", $"Minesweeping flotilla {b} has swept route Green; {c} mines cut."),
                    P($"WETTERBEOBACHTUNG {port.de} {t.de} WIND WEST STAERKE {Digits(b % 9 + 1)} SEEGANG {Digits(b % 6 + 1)}", $"Weather at {port.en}, {t.en}: wind west force {b % 9 + 1}, sea state {b % 6 + 1}."),
                    P($"VORPOSTENBOOT MELDET FEINDLICHES U BOOT {sq.de}", $"Patrol boat reports an enemy submarine in {sq.en}."));
                if (In(d, 1941, 3, 4, 1941, 3, 12))
                    Now(P("BRITISCHER UEBERFALL AUF LOFOTEN FISCHFABRIKEN ZERSTOERT VORPOSTENBOOT KREBS VERLOREN", "British raid on the Lofotens: fish-oil factories destroyed; patrol boat KREBS lost."));
                if (In(d, 1941, 5, 18, 1941, 5, 28))
                    Now(P("BISMARCK UND PRINZ EUGEN AUSGELAUFEN UNTERNEHMEN RHEINUEBUNG", "BISMARCK and PRINZ EUGEN have sailed. Operation RHEINÜBUNG."),
                        P($"FLOTTENCHEF BISMARCK STEUERT BREST AN TREIBSTOFFMANGEL", "Fleet commander: BISMARCK making for Brest. Short of fuel."),
                        P("ALLE VERFUEGBAREN U BOOTE ZUR UNTERSTUETZUNG BISMARCK", "All available U-boats to support BISMARCK."));
                if (In(d, 1942, 2, 1, 1942, 2, 14))
                    Now(P("UNTERNEHMEN CERBERUS SCHARNHORST GNEISENAU PRINZ EUGEN DURCH DEN KANAL", "Operation CERBERUS: SCHARNHORST, GNEISENAU and PRINZ EUGEN through the Channel."),
                        P("SPERRBRECHER UND MINENSUCHER WEG KANAL FREIHALTEN", "Sperrbrecher and minesweepers to keep the Channel route clear."));
                if (In(d, 1942, 6, 25, 1942, 7, 12))
                    Now(P("GELEITZUG PQ SIEBZEHN GESICHTET UNTERNEHMEN ROESSELSPRUNG", "Convoy PQ 17 sighted. Operation RÖSSELSPRUNG."),
                        P("TIRPITZ AUSGELAUFEN ALTENFJORD", "TIRPITZ has sailed from Altenfjord."),
                        P("GELEITZUG AUFGELOEST EINZELFAHRER ANGREIFEN", "The convoy has scattered. Attack the single ships."));
                break;
            }
            case Net.Shark:
            {
                var sq = Square(r);
                var group = d.Year >= 1943
                    ? Pick(r, new[] { P("RAUBGRAF", "RAUBGRAF"), P("STUERMER", "STÜRMER"), P("DRAENGER", "DRÄNGER"), P("FINK", "FINK") })
                    : Pick(r, new[] { P("PFEIL", "PFEIL"), P("LUCHS", "LUCHS"), P("WOTAN", "WOTAN"), P("TRUTZ", "TRUTZ"), P("STEINBRINCK", "STEINBRINCK") });
                int course = r.Next(0, 36) * 10, speed = r.Next(6, 11);
                Add(P($"GELEITZUG {sq.de} KURS {Digits(course)} FAHRT {Digits(speed)} SEEMEILEN {Digits(a)} DAMPFER", $"Convoy in {sq.en}, course {course}, speed {speed} knots, {a} steamers."),
                    P($"GRUPPE {group.de} OPERIEREN AUF GELEITZUG {sq.de} ANGRIFF FREI", $"Group {group.en} to operate against the convoy in {sq.en}. Free to attack."),
                    P($"WETTERKURZSIGNAL {sq.de} LUFTDRUCK {Digits(990 + b)} WIND {Digits(course)} STAERKE {Digits(b % 9 + 1)}", $"Short weather signal, {sq.en}: pressure {990 + b} mb, wind {course}° force {b % 9 + 1}."),
                    P($"BRENNSTOFF {Digits(a)} KUBIKMETER ERBITTE VERSORGUNG DURCH U {Digits(r.Next(459, 464))}", $"Fuel {a} cubic metres. Request refuelling from a supply U-boat."),
                    P($"ZWO DAMPFER VERSENKT {Digits(a * 1000)} BRT ABGEDRAENGT", $"Sank two steamers, {a * 1000} GRT. Driven off."));
                if (In(d, 1942, 1, 13, 1942, 7, 31))
                    Now(P($"VOR KAP HATTERAS {Digits(b % 4 + 1)} TANKER VERSENKT KUESTE NICHT VERDUNKELT", $"Off Cape Hatteras: {b % 4 + 1} tankers sunk. The coast is not blacked out."),
                        P("OPERATIONSGEBIET AMERIKANISCHE OSTKUESTE FREIGEGEBEN", "Operational area: the American east coast, released."));
                if (In(d, 1942, 11, 1, 1942, 11, 30))
                    Now(P("ALLE BOOTE MIT BRENNSTOFF AUF GIBRALTAR ZU MAXIMALE FAHRT", "All boats with enough fuel to close Gibraltar at maximum speed."));
                if (In(d, 1943, 3, 10, 1943, 3, 25))
                    Now(P("GELEITZUEGE HX ZWO ZWO NEUN UND SC EINS ZWO ZWO ANGREIFEN GRUPPEN RAUBGRAF STUERMER DRAENGER", "Attack convoys HX 229 and SC 122: groups RAUBGRAF, STÜRMER and DRÄNGER."));
                if (d >= new DateOnly(1944, 1, 1))
                    Now(P("MIT SCHNORCHEL GETAUCHT MARSCH IN DAS OPERATIONSGEBIET", "Proceeding submerged with schnorchel to the operational area."),
                        P("ALLE BOOTE GEGEN INVASIONSFLOTTE IM KANAL", "All boats against the invasion fleet in the Channel."));
                if (In(d, 1943, 5, 1, 1943, 5, 31))
                    Now(P("STAENDIGE LUFTUEBERWACHUNG DURCH TRAEGERFLUGZEUGE VERLUSTE SCHWER", "Constant air cover from carrier aircraft. Losses heavy."),
                        P("BDU AN ALLE GRUPPEN NORDATLANTIK RAEUMEN", "BdU to all groups: withdraw from the North Atlantic."));
                break;
            }
            case Net.Police:
            {
                int bn = r.Next(1, 320);
                var place = d < new DateOnly(1941, 6, 22)
                    ? Pick(r, new[] { P("KRAKAU", "Kraków"), P("WARSCHAU", "Warsaw"), P("LUBLIN", "Lublin"), P("RADOM", "Radom"), P("POSEN", "Poznań"), P("TSCHENSTOCHAU", "Częstochowa") })
                    : Pick(r, new[] { P("BIALYSTOK", "Białystok"), P("MINSK", "Minsk"), P("SHITOMIR", "Zhytomyr"), P("RIGA", "Riga"), P("KIEW", "Kiev"), P("BERDITSCHEW", "Berdichev") });
                Add(P($"POLIZEIBATAILLON {Digits(bn)} STAERKE {Digits(b)} OFFIZIERE {Digits(a * 12)} MANN KRANK {Digits(c)}", $"Police Battalion {bn} strength: {b} officers, {a * 12} men; {c} sick."),
                    P($"POLIZEIBATAILLON {Digits(bn)} VERLEGT NACH {place.de} ANKUNFT AM {Digits(c)}", $"Police Battalion {bn} moving to {place.en}, arriving on the {c}th."),
                    P($"ERBITTE {Digits(a * 20)} PAAR STIEFEL UND {Digits(b * 10)} MAENTEL", $"Request {a * 20} pairs of boots and {b * 10} greatcoats."),
                    P($"FUNKVERBINDUNG MIT {place.de} GESTOERT ERBITTE NEUE SCHLUESSELUNTERLAGEN", $"Wireless link with {place.en} disrupted. Request new key tables."));
                if (d < new DateOnly(1940, 1, 1))
                    Now(P("NACH KAPITULATION WARSCHAU SICHERUNG DER STADT DURCH POLIZEIREGIMENT", "After the capitulation of Warsaw, the city to be secured by a police regiment."),
                        P($"IN {place.de} {Digits(a)} FESTNAHMEN", $"{a} arrests in {place.en}."));
                if (In(d, 1940, 4, 9, 1940, 6, 30))
                    Now(P($"POLIZEIBATAILLON {Digits(bn)} NACH OSLO IN MARSCH", $"Police Battalion {bn} on its way to Oslo."),
                        P($"POLIZEIBATAILLON {Digits(bn)} NACH DEN HAAG", $"Police Battalion {bn} to The Hague."));
                if (In(d, 1941, 7, 1, 1941, 12, 31))
                    // What the police decrypts of 1941 actually told Bletchley; Churchill called it a crime without a name.
                    Now(P($"POLIZEIBATAILLON {Digits(bn)} MELDET SAEUBERUNGSAKTION {place.de} {Digits(a * 30)} JUDEN ERSCHOSSEN", $"Police Battalion {bn} reports a 'cleansing action' at {place.en}: {a * 30} Jews shot."),
                        P($"HOEHERER SS UND POLIZEIFUEHRER RUSSLAND MITTE MELDET {Digits(a * 50)} EXEKUTIONEN", $"Higher SS and Police Leader, Russia Centre, reports {a * 50} executions."));
                break;
            }
            case Net.Abwehr:
            {
                var agent = d < new DateOnly(1942, 4, 1)
                    ? Pick(r, new[] { P("IVAN", "IVAN"), P("LEONHARD", "LEONHARD"), P("GELATINE", "GELATINE"), P("ARABEL", "ARABEL") })
                    : Pick(r, new[] { P("ARABEL", "ARABEL"), P("IVAN", "IVAN"), P("HAMLET", "HAMLET"), P("HUBERT", "HUBERT") });
                Add(P($"MELDUNG VON {agent.de} ERHALTEN BEURTEILUNG ZUVERLAESSIG", $"Report from {agent.en} received. Assessed as reliable."),
                    P($"FUER {agent.de} {Digits(a * 100)} DOLLAR UEBERWIESEN", $"{a * 100} dollars transferred for {agent.en}."),
                    P($"FLUGZEUGTRAEGER IM HAFEN GIBRALTAR DAZU {Digits(b)} ZERSTOERER", $"Aircraft carrier in Gibraltar harbour, with {b} destroyers."),
                    P($"AGENT {agent.de} MELDET GELEITZUG {Digits(a)} SCHIFFE AUSLAUF GIBRALTAR AM {Digits(c)}", $"Agent {agent.en} reports a convoy of {a} ships leaving Gibraltar on the {c}th."));
                if (d < new DateOnly(1940, 6, 1))
                    Now(P("VERBINDUNG ZU AGENT JOHNNY UEBER FUNK LONDON HERGESTELLT", "Wireless contact established with agent JOHNNY in London."));
                if (In(d, 1940, 9, 1, 1940, 11, 30))
                    Now(P("UNTERNEHMEN LENA AGENTEN GELANDET BISHER KEINE MELDUNG", "Operation LENA: agents landed. No report from them yet."),
                        P($"AGENT {agent.de} MELDET LUFTSCHUTZ LONDON SCHWACH", $"Agent {agent.en} reports London's air defences weak."));
                if (In(d, 1944, 1, 1, 1944, 7, 31))
                    Now(P("AGENT ARABEL MELDET ERSTE US ARMEEGRUPPE UNTER PATTON IM RAUM KENT", "Agent ARABEL reports the First US Army Group, under Patton, in Kent."),
                        P("ARABEL BEURTEILUNG NORMANDIE ABLENKUNGSMANOEVER HAUPTSCHLAG FOLGT", "ARABEL's assessment: Normandy is a diversion. The main blow is still to come."));
                if (In(d, 1942, 10, 15, 1942, 11, 10))
                    Now(P("GROSSER GELEITZUG IN GIBRALTAR ZIEL VERMUTLICH MALTA ODER DAKAR", "Large convoy at Gibraltar, destination probably Malta or Dakar."),
                        P($"AGENT {agent.de} BESTAETIGT LANDUNG IN NORWEGEN GEPLANT", $"Agent {agent.en} confirms a landing in Norway is planned."));
                break;
            }
            case Net.Werft:
            {
                // The dockyards' and weather stations' stereotyped reports: dull, regular, and sent again in DOLPHIN.
                var w = new Tale(r, d);
                w.Set("port", Pick(r, new[] { P("BREST", "Brest"), P("LORIENT", "Lorient"), P("SAINT NAZAIRE", "St Nazaire"), P("KIEL", "Kiel"), P("WILHELMSHAVEN", "Wilhelmshaven"), P("BERGEN", "Bergen"), P("TRONDHEIM", "Trondheim") }));
                w.Set("time", t);
                int boat = r.Next(30, 600);
                w.Set("boat", Digits(boat), $"U-{boat}");
                w.Set("n", Digits(b), b.ToString());
                w.Set("days", Digits(c), c.ToString());
                w.Set("force", Digits(b % 9 + 1), (b % 9 + 1).ToString());
                w.Set("vis", Digits(a % 20 + 1), (a % 20 + 1).ToString());
                w.Set("dir", Pick(r, new[] { P("NORD", "north"), P("NORDWEST", "north-west"), P("WEST", "west"), P("SUEDWEST", "south-west"), P("OST", "east") }));
                Add(w.Pair("WETTERBEOBACHTUNG {port} {time} WIND {dir} STAERKE {force} SICHT {vis} SEEMEILEN {BEDECKT|REGEN|DIESIG|KLAR}",
                           "Weather at {port}, {time}: wind {dir} force {force}, visibility {vis} miles, {overcast|rain|hazy|clear}."),
                    w.Pair("U {boat} {EINGEDOCKT|AUSGEDOCKT} {port} WERFTLIEGEZEIT {days} TAGE", "{boat} {docked|undocked} at {port}. Time in dockyard {days} days."),
                    w.Pair("MINENSUCHBOOT M {n} {KLAR|BEDINGT KLAR} AB {time}", "Minesweeper M {n} {ready|partly ready} from {time}."),
                    w.Pair("SPERRBRECHER {n} {EINGELAUFEN|AUSGELAUFEN} {port} {time}", "Sperrbrecher {n} {entered|left} {port} at {time}."),
                    w.Pair("HAFENSCHUTZFLOTTILLE {port} KEINE BESONDEREN {VORKOMMNISSE|EREIGNISSE}", "Harbour defence flotilla, {port}: nothing {unusual|special} to report."),
                    w.Pair("WEG {GRUEN|ROT|BLAU} VOR {port} GERAEUMT {FREI|BEDINGT FREI}", "Route {Green|Red|Blue} off {port} swept. {Open|Open with care}."));
                if (In(d, 1941, 3, 22, 1942, 2, 11))
                    Now(w.Pair("SCHARNHORST {UND GNEISENAU |}IM DOCK BREST {AUSBESSERUNG|INSTANDSETZUNG} DAUERT AN", "SCHARNHORST {and GNEISENAU |}in dock at Brest. {Repairs|Refit} continuing."));
                if (In(d, 1942, 1, 16, 1944, 11, 12))
                    Now(w.Pair("TIRPITZ {LIEGEPLATZ|NETZSPERRE} {FAETTENFJORD|KAAFJORD} {UNVERAENDERT|VERSTAERKT}", "TIRPITZ: {berth|anti-torpedo nets} at {Fættenfjord|Kåfjord} {unchanged|strengthened}."));
                break;
            }
            case Net.Tunny:
            {
                var place = Pick(r, new[] { P("KURSK", "Kursk"), P("ORJOL", "Orel"), P("BJELGOROD", "Belgorod"), P("CHARKOW", "Kharkov") });
                Add(P($"PANZERLAGE EINSATZBEREIT {Digits(a * 10)} IN KURZFRISTIGER INSTANDSETZUNG {Digits(c * 3)}", $"Tank state: {a * 10} serviceable, {c * 3} in short-term repair."),
                    P($"MUNITIONSLAGE {Digits(b)} AUSSTATTUNGEN VORHANDEN", $"Ammunition: {b} issues on hand."),
                    P($"FEINDBEURTEILUNG STAERKERE FEINDLICHE KRAEFTE RAUM {place.de} ERKANNT", $"Appreciation: stronger enemy forces identified in the {place.en} area."));
                if (d < new DateOnly(1943, 7, 13))
                    Now(P($"BEGINN UNTERNEHMEN ZITADELLE {Digits(c % 9 + 1)} TAGE NACH STICHWORT", $"Operation ZITADELLE to begin {c % 9 + 1} days after the code word."),
                        P("ZITADELLE ANGRIFFSRAUM ORJOL BJELGOROD PANTHER NOCH NICHT EINSATZBEREIT", "ZITADELLE: attack from the Orel and Belgorod areas. Panthers not yet ready."));
                if (In(d, 1943, 7, 10, 1943, 8, 17))
                    Now(P("LANDUNG FEINDLICHER KRAEFTE AUF SIZILIEN", "Enemy forces have landed in Sicily."));
                if (In(d, 1943, 9, 3, 1943, 10, 31))
                    Now(P("LANDUNG BEI SALERNO GEGENANGRIFF ANGESETZT", "Landing at Salerno. Counter-attack ordered."));
                if (In(d, 1943, 11, 1, 1944, 6, 20))
                    Now(P("OB WEST ERWARTET HAUPTLANDUNG IM RAUM PAS DE CALAIS", "C-in-C West expects the main landing in the Pas de Calais."),
                        P("ATLANTIKWALL AUSBAU NORMANDIE VERSTAERKEN", "Strengthen the Atlantic Wall works in Normandy."));
                if (In(d, 1944, 6, 6, 1944, 7, 31))
                    Now(P("LANDUNG NORMANDIE NACH AUFFASSUNG OKW ABLENKUNG ZWEITE LANDUNG PAS DE CALAIS ERWARTET", "OKW considers the Normandy landing a diversion. A second landing in the Pas de Calais is expected."),
                        P("FUENFZEHNTE ARMEE VERBLEIBT IM RAUM PAS DE CALAIS", "Fifteenth Army to remain in the Pas de Calais."));
                if (In(d, 1944, 8, 25, 1944, 9, 30))
                    Now(P("II SS PANZERKORPS NEUNTE UND ZEHNTE SS PANZERDIVISION ZUR AUFFRISCHUNG RAUM ARNHEIM", "II SS Panzer Corps, 9th and 10th SS Panzer Divisions, to refit in the Arnhem area."));
                if (In(d, 1944, 11, 15, 1945, 1, 25))
                    Now(P("WACHT AM RHEIN FUNKSTILLE BIS ANGRIFFSBEGINN", "WACHT AM RHEIN: wireless silence until the attack begins."),
                        P($"SECHSTE PANZERARMEE BEREITSTELLUNG ABGESCHLOSSEN {Digits(a * 10)} PANZER", $"Sixth Panzer Army assembled: {a * 10} tanks."));
                if (d >= new DateOnly(1945, 2, 1))
                    Now(P("HEERESGRUPPE MELDET KEINE RESERVEN MEHR", "Army Group reports no reserves left."),
                        P("VERTEIDIGUNG BERLIN BIS ZUM LETZTEN", "Berlin to be defended to the last."));
                break;
            }
        }
        return pool.Count > 0 ? pool[r.Next(pool.Count)] : P("KEINE BESONDEREN VORKOMMNISSE", "Nothing to report.");
    }
}

/// <summary>A working Enigma. Rotors I-V and reflector B; the M4 adds the Beta wheel and thin reflector B.</summary>
public sealed class Enigma
{
    static readonly string[] Wiring = { "EKMFLGDQVZNTOWYHXUSPAIBRCJ", "AJDKSIRUXBLHWTMCQGZNPYFVOE", "BDFHJLCPRTXVZNYEIWGAKMUSQO", "ESOVPZJAYQUIRHXLNFTGKDCMWB", "VZBRGITYUPSDNHLXAWMJQOFECK" };
    static readonly char[] Notch = { 'Q', 'E', 'V', 'J', 'Z' };
    const string ReflectorB = "YRUHQSLDPXNGOKMIEBFZCWVJAT", ThinB = "ENKQAUYWJICOPBLMDXZVFTHRGS", Beta = "LEYJVCNIXWPBQMDRTAKZGFUHOS";

    readonly int[] _rotor = new int[3];   // left, middle, right
    readonly int[] _ring = new int[3];
    readonly int[] _pos = new int[3];
    readonly int[] _plug = Enumerable.Range(0, 26).ToArray();
    readonly bool _m4;
    int _betaPos;

    public Enigma(int[] rotors, int[] rings, IEnumerable<(char, char)> plugs, bool m4 = false)
    {
        rotors.CopyTo(_rotor, 0);
        rings.CopyTo(_ring, 0);
        foreach (var (a, b) in plugs) { _plug[a - 'A'] = b - 'A'; _plug[b - 'A'] = a - 'A'; }
        _m4 = m4;
    }

    public static Enigma DailyKey(int seed, Net net, int day)
    {
        var r = new Random(unchecked(seed * 486187739 + (int)net * 7919 + day * 104729) & 0x7FFFFFFF);
        var rotors = Enumerable.Range(0, 5).OrderBy(_ => r.Next()).Take(3).ToArray();
        var rings = new[] { r.Next(26), r.Next(26), r.Next(26) };
        var letters = Enumerable.Range(0, 26).OrderBy(_ => r.Next()).Take(20).ToArray();
        var plugs = Enumerable.Range(0, 10).Select(i => ((char)('A' + letters[2 * i]), (char)('A' + letters[2 * i + 1])));
        var m = new Enigma(rotors, rings, plugs, net == Net.Shark);
        m._betaPos = r.Next(26);
        return m;
    }

    public void SetPositions(string p) { for (int i = 0; i < 3; i++) _pos[i] = p[i] - 'A'; }

    int Through(int c, int slot, bool back)
    {
        string w = Wiring[_rotor[slot]];
        int shift = _pos[slot] - _ring[slot];
        int x = ((c + shift) % 26 + 26) % 26;
        x = back ? w.IndexOf((char)('A' + x)) : w[x] - 'A';
        return ((x - shift) % 26 + 26) % 26;
    }

    void Step()
    {
        bool midAtNotch = _pos[1] == Notch[_rotor[1]] - 'A';
        bool rightAtNotch = _pos[2] == Notch[_rotor[2]] - 'A';
        if (midAtNotch) { _pos[0] = (_pos[0] + 1) % 26; _pos[1] = (_pos[1] + 1) % 26; }   // the double step
        else if (rightAtNotch) _pos[1] = (_pos[1] + 1) % 26;
        _pos[2] = (_pos[2] + 1) % 26;
    }

    public char Press(char ch)
    {
        Step();
        int c = _plug[ch - 'A'];
        for (int s = 2; s >= 0; s--) c = Through(c, s, false);
        if (_m4)
        {
            c = ((Beta[(c + _betaPos) % 26] - 'A') - _betaPos + 26) % 26;
            c = ThinB[c] - 'A';
            c = (Beta.IndexOf((char)('A' + (c + _betaPos) % 26)) - _betaPos + 26) % 26;
        }
        else c = ReflectorB[c] - 'A';
        for (int s = 0; s < 3; s++) c = Through(c, s, true);
        return (char)('A' + _plug[c]);
    }

    public string Encipher(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text) if (ch is >= 'A' and <= 'Z') sb.Append(Press(ch));
        return sb.ToString();
    }
}
