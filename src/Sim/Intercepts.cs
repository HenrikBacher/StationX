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
        var (de, en) = Plaintext(net, r);
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
            StationType.Dispatch => ReadStage.Translated,
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
        Net.Red => "RED", Net.LightBlue => "BLU", Net.Chaffinch => "FIN", Net.Abwehr => "ABW", _ => "XYZ",
    } switch { var s => new string(s.Select(c => (char)('A' + (c - 'A' + 7) % 26)).ToArray()) };   // a fixed letter group per key

    static string StationFor(Net net) => net switch
    {
        Net.Red or Net.LightBlue => "RAF CHICKSANDS",
        Net.Chaffinch => "WAR OFFICE Y, BEAUMANOR",
        Net.Dolphin or Net.Shark => "ADMIRALTY Y, SCARBOROUGH",
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

    static (string de, string en) Plaintext(Net net, Random r)
    {
        var parts = new List<(string de, string en)>();
        int n = r.NextDouble() < 0.4 ? 2 : 1;
        for (int i = 0; i < n; i++) parts.Add(Sentence(net, r));
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
        Net.Tunny => Pick(r, new[]
        {
            P("AN OKH GENSTDH OP ABT VON HEERESGRUPPE SUED", "To: OKH, General Staff, Operations Branch. From: Army Group South."),
            P("AN OKW WFST VON OB WEST", "To: OKW Operations Staff. From: C-in-C West."),
            P("AN HEERESGRUPPE MITTE VON OKH", "To: Army Group Centre. From: OKH."),
        }),
        _ => P("AN", "To:"),
    };

    static (string de, string en) Sentence(Net net, Random r)
    {
        int a = r.Next(4, 40), b = r.Next(1, 15), c = r.Next(2, 28);
        var t = Time(r);
        switch (net)
        {
            case Net.Red:
            {
                var unit = Pick(r, new[] { P("KG ZWO", "KG 2"), P("KG FUENF FUENF", "KG 55"), P("KG ZWO SIEBEN", "KG 27"), P("JG ZWO SECHS", "JG 26"), P("KG EINS NULL NULL", "KG 100"), P("STG ZWO", "StG 2") });
                var field = Pick(r, new[] { P("VILLACOUBLAY", "Villacoublay"), P("CHARTRES", "Chartres"), P("BEAUVAIS", "Beauvais"), P("CAEN", "Caen"), P("SAINT OMER", "St Omer"), P("ABBEVILLE", "Abbeville"), P("VANNES", "Vannes") });
                var target = Pick(r, new[] { P("LONDON", "London"), P("COVENTRY", "Coventry"), P("BIRMINGHAM", "Birmingham"), P("LIVERPOOL", "Liverpool"), P("PLYMOUTH", "Plymouth"), P("SOUTHAMPTON", "Southampton"), P("BRISTOL", "Bristol") });
                return Pick(r, new[]
                {
                    P($"{unit.de} STAND {t.de} EINSATZBEREIT {Digits(a)} FLUGZEUGE BEDINGT EINSATZBEREIT {Digits(b)}", $"{unit.en}, state at {t.en}: {a} aircraft serviceable, {b} partly serviceable."),
                    P($"{unit.de} GREIFT HEUTE NACHT {target.de} AN ZIELZEIT {t.de} ANFLUG UEBER {field.de}", $"{unit.en} will attack {target.en} tonight. Time over target {t.en}. Approach via {field.en}."),
                    P($"KNICKEBEIN SENDER CHERBOURG RICHTUNG {target.de} AB {t.de}", $"Knickebein beam transmitter at Cherbourg to be laid on {target.en} from {t.en}."),
                    P($"FLIEGERHORST {field.de} MELDET BETRIEBSSTOFF {Digits(a * 10)} KUBIKMETER BOMBEN SC FUENF NULL NULL {Digits(c)} STUECK", $"Airfield {field.en} reports fuel {a * 10} cubic metres; {c} SC 500 bombs."),
                    P($"{unit.de} VERLEGT AM {Digits(c)} NACH {field.de} BODENPERSONAL MIT BAHN", $"{unit.en} transfers to {field.en} on the {c}th. Ground personnel by rail."),
                    P($"WETTER {field.de} {t.de} BEWOELKUNG {Digits(b % 10)} ZEHNTEL SICHT {Digits(a)} KM", $"Weather at {field.en}, {t.en}: cloud {b % 10}/10, visibility {a} km."),
                });
            }
            case Net.LightBlue:
            {
                var field = Pick(r, new[] { P("DERNA", "Derna"), P("GAZALA", "Gazala"), P("MARTUBA", "Martuba"), P("BENGASI", "Benghazi"), P("CASTEL BENITO", "Castel Benito"), P("GAMBUT", "Gambut") });
                var target = Pick(r, new[] { P("TOBRUK", "Tobruk"), P("MALTA", "Malta"), P("MARSA MATRUH", "Mersa Matruh"), P("SIDI BARRANI", "Sidi Barrani") });
                return Pick(r, new[]
                {
                    P($"STG DREI EINSATZBEREIT {Digits(b + 4)} JU EINS ACHT SEVEN".Replace("SEVEN", "SIEBEN"), $"StG 3: {b + 4} Ju 87s serviceable."),
                    P($"MORGEN {t.de} ANGRIFF AUF {target.de} MIT ALLEN VERFUEGBAREN KRAEFTEN", $"Attack on {target.en} tomorrow at {t.en} with all available forces."),
                    P($"SANDSTURM {field.de} STARTVERBOT BIS AUF WEITERES", $"Sandstorm at {field.en}. All take-offs forbidden until further notice."),
                    P($"FLUGPLATZ {field.de} BETRIEBSSTOFF NUR NOCH {Digits(a)} TONNEN ERBITTE ZUFUEHRUNG", $"Airfield {field.en} has only {a} tons of fuel left. Request supply."),
                    P($"GELEITSCHUTZ FUER DAMPFER NACH BENGASI AB {t.de} DURCH JG ZWO SIEBEN", $"Fighter escort for steamers bound for Benghazi from {t.en}, by JG 27."),
                });
            }
            case Net.Chaffinch:
            {
                var unit = Pick(r, new[] { P("FUENFZEHNTE PANZERDIVISION", "15th Panzer Division"), P("EINUNDZWANZIGSTE PANZERDIVISION", "21st Panzer Division"), P("NEUNZIGSTE LEICHTE DIVISION", "90th Light Division"), P("DAK", "Afrika Korps") });
                var ship = Pick(r, new[] { P("ANKARA", "ANKARA"), P("TINOS", "TINOS"), P("PROCIDA", "PROCIDA"), P("ATLAS", "ATLAS"), P("ANNA ZIPPITELLI", "ANNA ZIPPITELLI") });
                var port = Pick(r, new[] { P("TRIPOLIS", "Tripoli"), P("BENGASI", "Benghazi"), P("TOBRUK", "Tobruk") });
                return Pick(r, new[]
                {
                    P($"{unit.de} MELDET EINSATZBEREIT PANZER DREI {Digits(a)} PANZER VIER {Digits(b)} IN INSTANDSETZUNG {Digits(c)}", $"{unit.en} reports serviceable: {a} Pz III, {b} Pz IV; {c} under repair."),
                    P($"DAMPFER {ship.de} MIT {Digits(a * 50)} TONNEN BETRIEBSSTOFF {port.de} EINGELAUFEN", $"Steamer {ship.en} arrived {port.en} with {a * 50} tons of fuel."),
                    P($"BETRIEBSSTOFFLAGE ERNST VORRAT REICHT NUR FUER {Digits(b % 6 + 2)} TAGE", $"Fuel situation serious. Stocks will last only {b % 6 + 2} days."),
                    P($"DAMPFER {ship.de} LAEUFT {Digits(c)} AUS NEAPEL AUS LADUNG MUNITION UND {Digits(a)} PANZER", $"Steamer {ship.en} sails from Naples on the {c}th, carrying ammunition and {a} tanks."),
                    P($"{unit.de} VERPFLEGUNGSSTAERKE {Digits(a * 300)} WASSER {Digits(b)} LITER JE KOPF UND TAG", $"{unit.en} ration strength {a * 300}; water {b} litres per head per day."),
                });
            }
            case Net.Dolphin:
            {
                var port = Pick(r, new[] { P("BREST", "Brest"), P("LORIENT", "Lorient"), P("SAINT NAZAIRE", "St Nazaire"), P("LA PALLICE", "La Pallice"), P("KIEL", "Kiel"), P("BERGEN", "Bergen"), P("TRONDHEIM", "Trondheim") });
                var sq = Square(r);
                int boat = r.Next(30, 600);
                return Pick(r, new[]
                {
                    P($"U {Digits(boat)} EINLAUFEN {port.de} AM {Digits(c)} {t.de} SPERRBRECHER ERBETEN", $"U-{boat} entering {port.en} on the {c}th at {t.en}. Request Sperrbrecher escort."),
                    P($"GELEIT AB {port.de} {t.de} TREFFPUNKT {sq.de}", $"Escort leaves {port.en} at {t.en}. Rendezvous {sq.en}."),
                    P($"MINENSUCHFLOTTILLE {Digits(b)} WEG GRUEN GERAEUMT {Digits(c)} MINEN GESCHNITTEN", $"Minesweeping flotilla {b} has swept route Green; {c} mines cut."),
                    P($"WETTERBEOBACHTUNG {port.de} {t.de} WIND WEST STAERKE {Digits(b % 9 + 1)} SEEGANG {Digits(b % 6 + 1)}", $"Weather at {port.en}, {t.en}: wind west force {b % 9 + 1}, sea state {b % 6 + 1}."),
                    P($"VORPOSTENBOOT MELDET FEINDLICHES U BOOT {sq.de}", $"Patrol boat reports an enemy submarine in {sq.en}."),
                });
            }
            case Net.Shark:
            {
                var sq = Square(r);
                var group = Pick(r, new[] { P("RAUFBOLD", "RAUFBOLD"), P("VORWAERTS", "VORWÄRTS"), P("PFEIL", "PFEIL"), P("LUCHS", "LUCHS"), P("WOTAN", "WOTAN"), P("TRUTZ", "TRUTZ") });
                int course = r.Next(0, 36) * 10, speed = r.Next(6, 11);
                return Pick(r, new[]
                {
                    P($"GELEITZUG {sq.de} KURS {Digits(course)} FAHRT {Digits(speed)} SEEMEILEN {Digits(a)} DAMPFER", $"Convoy in {sq.en}, course {course}, speed {speed} knots, {a} steamers."),
                    P($"GRUPPE {group.de} OPERIEREN AUF GELEITZUG {sq.de} ANGRIFF FREI", $"Group {group.en} to operate against the convoy in {sq.en}. Free to attack."),
                    P($"WETTERKURZSIGNAL {sq.de} LUFTDRUCK {Digits(990 + b)} WIND {Digits(course)} STAERKE {Digits(b % 9 + 1)}", $"Short weather signal, {sq.en}: pressure {990 + b} mb, wind {course}° force {b % 9 + 1}."),
                    P($"BRENNSTOFF {Digits(a)} KUBIKMETER ERBITTE VERSORGUNG DURCH U {Digits(r.Next(459, 464))}", $"Fuel {a} cubic metres. Request refuelling from a supply U-boat."),
                    P($"ZWO DAMPFER VERSENKT {Digits(a * 1000)} BRT ABGEDRAENGT", $"Sank two steamers, {a * 1000} GRT. Driven off."),
                });
            }
            case Net.Police:
            {
                var place = Pick(r, new[] { P("KRAKAU", "Kraków"), P("WARSCHAU", "Warsaw"), P("LUBLIN", "Lublin"), P("RADOM", "Radom"), P("POSEN", "Poznań"), P("TSCHENSTOCHAU", "Częstochowa") });
                int bn = r.Next(1, 320);
                return Pick(r, new[]
                {
                    P($"POLIZEIBATAILLON {Digits(bn)} STAERKE {Digits(b)} OFFIZIERE {Digits(a * 12)} MANN KRANK {Digits(c)}", $"Police Battalion {bn} strength: {b} officers, {a * 12} men; {c} sick."),
                    P($"POLIZEIBATAILLON {Digits(bn)} VERLEGT NACH {place.de} ANKUNFT AM {Digits(c)}", $"Police Battalion {bn} moving to {place.en}, arriving on the {c}th."),
                    P($"IN {place.de} {Digits(a)} FESTNAHMEN", $"{a} arrests in {place.en}."),
                    P($"ERBITTE {Digits(a * 20)} PAAR STIEFEL UND {Digits(b * 10)} MAENTEL", $"Request {a * 20} pairs of boots and {b * 10} greatcoats."),
                    P($"FUNKVERBINDUNG MIT {place.de} GESTOERT ERBITTE NEUE SCHLUESSELUNTERLAGEN", $"Wireless link with {place.en} disrupted. Request new key tables."),
                });
            }
            case Net.Abwehr:
            {
                var agent = Pick(r, new[] { P("ARABEL", "ARABEL"), P("IVAN", "IVAN"), P("LEONHARD", "LEONHARD"), P("HAMLET", "HAMLET"), P("GELATINE", "GELATINE") });
                return Pick(r, new[]
                {
                    P($"AGENT {agent.de} MELDET GELEITZUG {Digits(a)} SCHIFFE AUSLAUF GIBRALTAR AM {Digits(c)}", $"Agent {agent.en} reports a convoy of {a} ships leaving Gibraltar on the {c}th."),
                    P($"MELDUNG VON {agent.de} ERHALTEN BEURTEILUNG ZUVERLAESSIG", $"Report from {agent.en} received. Assessed as reliable."),
                    P($"FUER {agent.de} {Digits(a * 100)} DOLLAR UEBERWIESEN", $"{a * 100} dollars transferred for {agent.en}."),
                    P($"FLUGZEUGTRAEGER IM HAFEN GIBRALTAR DAZU {Digits(b)} ZERSTOERER", $"Aircraft carrier in Gibraltar harbour, with {b} destroyers."),
                    P($"FUNKER FUER {agent.de} AUSGEBILDET ABREISE {Digits(c)}", $"Wireless operator for {agent.en} trained; departs on the {c}th."),
                });
            }
            case Net.Tunny:
            {
                var place = Pick(r, new[] { P("KURSK", "Kursk"), P("ORJOL", "Orel"), P("BJELGOROD", "Belgorod"), P("CHARKOW", "Kharkov"), P("NORMANDIE", "Normandy"), P("PAS DE CALAIS", "the Pas de Calais") });
                return Pick(r, new[]
                {
                    P($"PANZERLAGE EINSATZBEREIT {Digits(a * 10)} IN KURZFRISTIGER INSTANDSETZUNG {Digits(c * 3)}", $"Tank state: {a * 10} serviceable, {c * 3} in short-term repair."),
                    P($"BEGINN UNTERNEHMEN ZITADELLE {Digits(c % 9 + 1)} TAGE NACH STICHWORT", $"Operation ZITADELLE to begin {c % 9 + 1} days after the code word."),
                    P($"FEINDBEURTEILUNG STAERKERE FEINDLICHE KRAEFTE RAUM {place.de} ERKANNT", $"Appreciation: stronger enemy forces identified in the {place.en} area."),
                    P($"LANDUNG IM RAUM {place.de} WIRD ERWARTET", $"A landing is expected in {place.en}."),
                    P($"MUNITIONSLAGE {Digits(b)} AUSSTATTUNGEN VORHANDEN", $"Ammunition: {b} issues on hand."),
                });
            }
        }
        return P("KEINE BESONDEREN VORKOMMNISSE", "Nothing to report.");
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
