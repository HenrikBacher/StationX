using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StationX.Sim;

/// <summary>Summary shown in the load menu, stored next to the state so the list loads fast.</summary>
public sealed class SaveInfo
{
    public int Version { get; set; }
    public string Title { get; set; } = "";
    public string Date { get; set; } = "";
    public int Funds { get; set; }
    public string Mode { get; set; } = "";
    public DateTime SavedAt { get; set; }
}

public sealed class SaveFile
{
    public SaveInfo Info { get; set; } = new();
    public GameState State { get; set; } = new();
}

/// <summary>Whole-game saves as JSON. The sim is plain data, so this is a straight serialisation.</summary>
public static class SaveGame
{
    /// <summary>2: Barbarossa became an act after U-110, so later acts moved up one.</summary>
    public const int Version = 2;

    static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,              // (int x, int y) tuples in paths
        IgnoreReadOnlyProperties = true,   // computed properties such as Day and Act
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SaveInfo InfoOf(GameState g) => new()
    {
        Version = Version,
        Title = g.Mode == GameMode.FreePlay ? $"Free play · {g.Act.ChapterTitle}" : $"{g.Act.ChapterTitle}: {g.Act.Title}",
        Date = g.Date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
        Funds = g.Funds,
        Mode = g.Mode.ToString(),
        SavedAt = DateTime.Now,
    };

    public static string Save(GameState g) =>
        JsonSerializer.Serialize(new SaveFile { Info = InfoOf(g), State = g }, Options);

    /// <summary>
    /// Serialises a throwaway game on a worker thread, so the serializer's one-off setup for these types (about 180 ms)
    /// is done before the first autosave rather than as a stall at the first midnight (Main starts it 20 s in). The sim keeps no shared mutable
    /// state, so a separate game is safe to build and write alongside the real one.
    /// </summary>
    public static System.Threading.Tasks.Task WarmUp() => System.Threading.Tasks.Task.Run(() =>
    {
        try { Load(Save(GameState.NewGame(1, Difficulty.Normal))); }
        catch (Exception) { /* only a warm-up */ }
    });

    public static GameState Load(string json)
    {
        var file = JsonSerializer.Deserialize<SaveFile>(json, Options) ?? throw new InvalidOperationException("Empty save file");
        if (file.Info.Version > Version) throw new InvalidOperationException("This save is from a newer version of the game.");
        var g = file.State;
        if (file.Info.Version < 2 && g.ActIndex >= Campaign.IndexOf("Barbarossa")) g.ActIndex++;
        // Saves from before Hut 1 existed.
        g.Grid.AddMissingZones();
        if (g.ActIndex >= 3 && g.Grid.Zone("hut1") is { Unlocked: false } hut1) hut1.Available = true;
        if (g.Billets <= 0) g.Billets = Math.Max(Defs.StartBillets, (g.Staff.Count + 9) / 10 * 10);
        // Saves from before everyone was posted to a building.
        g.PostUnposted();
        g.Grid.RebuildOccupancy(g.Stations);
        g.Grid.FitInteriors(g.Stations);
        // Saves from before the ULTRA choice: operations already won were already acted on.
        foreach (var op in g.Operations)
            if (op.State == OpState.Won && op.Use == OpUse.Undecided && op.DecideBy <= 0) op.Use = OpUse.Acted;
        return g;
    }

    /// <summary>Reads only the summary, for the load menu.</summary>
    public static SaveInfo? ReadInfo(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("Info").Deserialize<SaveInfo>(Options);
        }
        catch (Exception) { return null; }
    }
}
