using System;
using System.Collections.Generic;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>Save slots in user://saves: three manual slots, a quicksave and a daily autosave.</summary>
public static class Saves
{
    /// <summary>The self-test and screenshot runs point this elsewhere so they never touch real saves.</summary>
    public static string Dir = "user://saves";
    public static readonly string[] Slots = { "slot1", "slot2", "slot3", "quick", "auto" };

    public static string SlotName(string slot) => slot switch
    {
        "quick" => "Quicksave",
        "auto" => "Autosave",
        _ => $"Slot {slot[^1]}",
    };

    static string PathOf(string slot) => $"{Dir}/{slot}.json";

    public static string? Write(string slot, GameState g)
    {
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(Dir);
            using var f = FileAccess.Open(PathOf(slot), FileAccess.ModeFlags.Write);
            if (f == null) return $"Could not write the save ({FileAccess.GetOpenError()}).";
            f.StoreString(SaveGame.Save(g));
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    public static GameState? Read(string slot, out string? error)
    {
        error = null;
        try
        {
            if (!FileAccess.FileExists(PathOf(slot))) { error = "That slot is empty."; return null; }
            return SaveGame.Load(FileAccess.GetFileAsString(PathOf(slot)));
        }
        catch (Exception e) { error = e.Message; return null; }
    }

    public static SaveInfo? Info(string slot) =>
        FileAccess.FileExists(PathOf(slot)) ? SaveGame.ReadInfo(FileAccess.GetFileAsString(PathOf(slot))) : null;

    public static IEnumerable<(string slot, SaveInfo? info)> List()
    {
        foreach (var s in Slots) yield return (s, Info(s));
    }
}
