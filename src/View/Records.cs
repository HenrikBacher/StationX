using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>Best campaign scores per difficulty, in user://records.cfg. Never written by test or screenshot runs.</summary>
public static class Records
{
    const string Path = "user://records.cfg";

    public static int Best(Difficulty d)
    {
        if (!Settings.Persist) return 0;
        var cfg = new ConfigFile();
        return cfg.Load(Path) == Error.Ok ? (int)cfg.GetValue("best", d.ToString(), 0) : 0;
    }

    /// <summary>Records the score if it beats the best so far. Returns the previous best.</summary>
    public static int Submit(Difficulty d, int score)
    {
        int previous = Best(d);
        if (!Settings.Persist || score <= previous) return previous;
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("best", d.ToString(), score);
        cfg.Save(Path);
        return previous;
    }

    /// <summary>A finished war: when it began (real time), the difficulty, how it ended, its score and the last date reached.</summary>
    public sealed record War(string Key, string Played, Difficulty Difficulty, bool Won, int Score, string Reached);

    /// <summary>Every war finished on this computer, newest first.</summary>
    public static System.Collections.Generic.List<War> Wars()
    {
        var list = new System.Collections.Generic.List<War>();
        if (!Settings.Persist) return list;
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok || !cfg.HasSection("wars")) return list;
        foreach (var key in cfg.GetSectionKeys("wars"))
        {
            var p = ((string)cfg.GetValue("wars", key, "")).Split('|');
            if (p.Length < 5 || !System.Enum.TryParse<Difficulty>(p[1], out var d)) continue;
            list.Add(new War(key, p[0], d, p[2] == "1", int.TryParse(p[3], out int sc) ? sc : 0, p[4]));
        }
        list.Sort((a, b) => string.CompareOrdinal(b.Played, a.Played));
        return list;
    }

    /// <summary>Records (or updates, if the same game ends again later) a finished war.</summary>
    public static void AddWar(War w)
    {
        if (!Settings.Persist) return;
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("wars", w.Key, $"{w.Played}|{w.Difficulty}|{(w.Won ? 1 : 0)}|{w.Score}|{w.Reached}");
        cfg.Save(Path);
    }
}
