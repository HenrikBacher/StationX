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
    public const int Version = 1;

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

    public static GameState Load(string json)
    {
        var file = JsonSerializer.Deserialize<SaveFile>(json, Options) ?? throw new InvalidOperationException("Empty save file");
        if (file.Info.Version > Version) throw new InvalidOperationException("This save is from a newer version of the game.");
        var g = file.State;
        // Saves from before Hut 1 existed.
        g.Grid.AddMissingZones();
        if (g.ActIndex >= 3 && g.Grid.Zone("hut1") is { Unlocked: false } hut1) hut1.Available = true;
        if (g.Billets <= 0) g.Billets = Math.Max(Defs.StartBillets, (g.Staff.Count + 9) / 10 * 10);
        g.Grid.RebuildOccupancy(g.Stations);
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
