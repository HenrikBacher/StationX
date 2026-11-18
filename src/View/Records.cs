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
}
