using Godot;

namespace StationX.View;

/// <summary>Player preferences, kept in user://settings.cfg.</summary>
public static class Settings
{
    const string Path = "user://settings.cfg";

    public static float MasterVolume = 0.8f, SfxVolume = 0.8f, AmbienceVolume = 0.6f, MusicVolume = 0.45f;
    public static float UiScale = 1f;
    /// <summary>Set once a tutorial has been finished: new games then default to playing the first chapter without it.</summary>
    public static bool TutorialDone;
    public static bool EdgeScroll = true, Hints = true, Fullscreen, FilmGrain = true, CheckUpdates = true;

    /// <summary>0 Low, 1 Balanced, 2 High: what the 3D view spends on looks (see <c>Main.ApplyGraphics</c>).</summary>
    public static int Graphics = 2;
    public static readonly string[] GraphicsNames = { "Low", "Balanced", "High" };

    /// <summary>Set by the game's main node: puts <see cref="Graphics"/> into effect.</summary>
    public static System.Action? ApplyGraphics;

    public static void Load()
    {
        if (!Persist) return;
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        MasterVolume = (float)cfg.GetValue("audio", "master", MasterVolume);
        SfxVolume = (float)cfg.GetValue("audio", "sfx", SfxVolume);
        AmbienceVolume = (float)cfg.GetValue("audio", "ambience", AmbienceVolume);
        MusicVolume = (float)cfg.GetValue("audio", "music", MusicVolume);
        UiScale = (float)cfg.GetValue("display", "ui_scale", UiScale);
        Fullscreen = (bool)cfg.GetValue("display", "fullscreen", Fullscreen);
        FilmGrain = (bool)cfg.GetValue("display", "film_grain", FilmGrain);
        Graphics = System.Math.Clamp((int)cfg.GetValue("display", "graphics", Graphics), 0, 2);
        EdgeScroll = (bool)cfg.GetValue("controls", "edge_scroll", EdgeScroll);
        Hints = (bool)cfg.GetValue("controls", "hints", Hints);
        TutorialDone = (bool)cfg.GetValue("controls", "tutorial_done", TutorialDone);
        CheckUpdates = (bool)cfg.GetValue("updates", "check", CheckUpdates);
    }

    /// <summary>False in self-test and screenshot runs: they use defaults and never write the player's settings.</summary>
    public static bool Persist = true;

    public static void Save()
    {
        if (!Persist) return;
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "master", MasterVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.SetValue("audio", "ambience", AmbienceVolume);
        cfg.SetValue("audio", "music", MusicVolume);
        cfg.SetValue("display", "ui_scale", UiScale);
        cfg.SetValue("display", "fullscreen", Fullscreen);
        cfg.SetValue("display", "film_grain", FilmGrain);
        cfg.SetValue("display", "graphics", Graphics);
        cfg.SetValue("controls", "edge_scroll", EdgeScroll);
        cfg.SetValue("controls", "hints", Hints);
        cfg.SetValue("controls", "tutorial_done", TutorialDone);
        cfg.SetValue("updates", "check", CheckUpdates);
        cfg.Save(Path);
    }

    /// <summary>Applies the window and audio settings. The HUD reads UiScale itself.</summary>
    public static void Apply()
    {
        Sound.SetVolumes(MasterVolume, SfxVolume, AmbienceVolume, MusicVolume);
        ApplyGraphics?.Invoke();
        var mode = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.GetName() != "headless" && DisplayServer.WindowGetMode() != mode
            && !(mode == DisplayServer.WindowMode.Windowed && DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Maximized))
            DisplayServer.WindowSetMode(mode);
    }
}
