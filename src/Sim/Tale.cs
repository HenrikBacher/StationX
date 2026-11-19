using System;
using System.Collections.Generic;
using System.Text;

namespace StationX.Sim;

/// <summary>
/// The variety engine. A tale fills text templates from its own seeded dice, so one scenario or one signal reads
/// differently each time while staying true to its date:
/// <c>{city}</c> takes a value set earlier, <c>{grim|bleak|hard}</c> picks one inline, and the two nest
/// (<c>{{city}|the docks}</c>). Values can carry a German and an English form, and <see cref="Pair"/> fills a German
/// template and its translation with the same choices, so the two always agree.
/// </summary>
public sealed class Tale
{
    readonly Random _r;
    readonly Dictionary<string, (string de, string en)> _v = new();

    public DateOnly Date { get; }

    /// <summary>How much is at stake this time, around 1: scales a scenario's rewards and risks together.</summary>
    public double Stakes { get; set; } = 1;

    public Tale(Random r, DateOnly date) { _r = r; Date = date; }

    public string this[string key]
    {
        get => _v.TryGetValue(key, out var s) ? s.en : "{" + key + "}";
        set => _v[key] = (value, value);
    }

    /// <summary>Sets a value with a German and an English form.</summary>
    public void Set(string key, string de, string en) => _v[key] = (de, en);
    public void Set(string key, (string de, string en) v) => _v[key] = v;

    public string Pick(params string[] xs) => xs[_r.Next(xs.Length)];
    public T Pick<T>(IReadOnlyList<T> xs) => xs[_r.Next(xs.Count)];
    /// <summary>A whole number from lo up to, not including, hi.</summary>
    public int Range(int lo, int hi) => _r.Next(lo, hi);
    public double Range(double lo, double hi) => lo + _r.NextDouble() * (hi - lo);
    public bool Chance(double p) => _r.NextDouble() < p;
    public int Next() => _r.Next();

    public bool Between(int y0, int m0, int d0, int y1, int m1, int d1) =>
        Date >= new DateOnly(y0, m0, d0) && Date <= new DateOnly(y1, m1, d1);
    public bool From(int y, int m, int d) => Date >= new DateOnly(y, m, d);
    public bool Before(int y, int m, int d) => Date < new DateOnly(y, m, d);

    /// <summary>A reward or risk scaled by the stakes, rounded.</summary>
    public int S(double v) => (int)Math.Round(v * Stakes);

    /// <summary>Fills an English template.</summary>
    public string Fill(string template) => Fill(template, _r, false);

    /// <summary>Fills a German template and its English twin with the same choices.</summary>
    public (string de, string en) Pair(string de, string en)
    {
        int seed = _r.Next();
        return (Fill(de, new Random(seed), true), Fill(en, new Random(seed), false));
    }

    string Fill(string t, Random r, bool german)
    {
        int i = 0;
        return Run(t, ref i, false, r, german);
    }

    string Run(string t, ref int i, bool nested, Random r, bool german)
    {
        var alts = new List<string>();
        var sb = new StringBuilder();
        while (i < t.Length)
        {
            char c = t[i++];
            if (c == '{') { sb.Append(Run(t, ref i, true, r, german)); continue; }
            if (nested && c == '|') { alts.Add(sb.ToString()); sb.Clear(); continue; }
            if (nested && c == '}') break;
            sb.Append(c);
        }
        if (!nested) return sb.ToString();
        alts.Add(sb.ToString());
        if (alts.Count == 1 && IsName(alts[0]))
            return _v.TryGetValue(alts[0], out var v) ? (german ? v.de : v.en) : "{" + alts[0] + "}";
        return alts[r.Next(alts.Count)];
    }

    static bool IsName(string s)
    {
        if (s.Length == 0) return false;
        foreach (var c in s) if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        return true;
    }
}
