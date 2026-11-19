using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace StationX.View;

/// <summary>
/// Period music: public-domain recordings of Elgar and Holst by US military bands (see CREDITS.md). The tracks are
/// streamed from loose .ogg files beside the game (music/ in a release, assets/music/ in the project, which Godot
/// does not import), so a game update need not download them again.
///
/// The title plays the Serenade; in play the tracks come round in turn with a few minutes of quiet between them, so
/// the Park's own sounds carry most of the time; a won war ends with Pomp and Circumstance.
/// </summary>
public partial class Music : Node
{
    public enum Mood { Title, Game, Won, Silent }

    static readonly string[] GameTracks = { "elgar-serenade", "holst-chaconne", "holst-march" };
    const string TitleTrack = "elgar-serenade", WonTrack = "elgar-pomp";
    const float Fade = 2.5f;

    readonly Dictionary<string, AudioStreamOggVorbis?> _cache = new();
    readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[2];
    readonly float[] _level = new float[2];
    int _current;
    string? _playing;
    Mood _mood = Mood.Silent;
    float _gap = 25;               // seconds of quiet before the next game track
    int _next;
    readonly Random _rng = new();
    string? _dir;

    public override void _Ready()
    {
        Sound.EnsureBus("Music");
        for (int i = 0; i < 2; i++)
        {
            _players[i] = new AudioStreamPlayer { Bus = "Music", VolumeDb = -80 };
            AddChild(_players[i]);
        }
        _dir = FindDir();
        _next = _rng.Next(GameTracks.Length);
    }

    static string? FindDir()
    {
        foreach (var d in new[] { OS.GetExecutablePath().GetBaseDir().PathJoin("music"), ProjectSettings.GlobalizePath("res://assets/music") })
            if (DirAccess.DirExistsAbsolute(d)) return d;
        return null;
    }

    AudioStreamOggVorbis? Load(string name)
    {
        if (_cache.TryGetValue(name, out var s)) return s;
        string? path = _dir?.PathJoin(name + ".ogg");
        s = path != null && FileAccess.FileExists(path) ? AudioStreamOggVorbis.LoadFromFile(path) : null;
        _cache[name] = s;
        return s;
    }

    /// <summary>Called every frame with what the game is doing.</summary>
    public void Update(Mood mood, float dt)
    {
        if (mood != _mood)
        {
            var was = _mood;
            _mood = mood;
            switch (mood)
            {
                case Mood.Title: if (_playing != TitleTrack) Start(TitleTrack); break;
                case Mood.Won: Start(WonTrack); break;
                case Mood.Silent: Start(null); break;
                case Mood.Game:
                    // From the title, let the Serenade play on into the game; otherwise a short quiet first.
                    if (was != Mood.Title) { Start(null); _gap = 25; }
                    break;
            }
        }

        if (_mood == Mood.Game && !_players[_current].Playing && _level[_current] <= 0.01f)
        {
            _gap -= dt;
            if (_gap <= 0)
            {
                Start(GameTracks[_next]);
                _next = (_next + 1 + _rng.Next(GameTracks.Length - 1)) % GameTracks.Length;
                _gap = 120 + (float)_rng.NextDouble() * 150;
            }
        }
        else if (_mood == Mood.Title && !_players[_current].Playing && Load(TitleTrack) != null) Start(TitleTrack);

        for (int i = 0; i < 2; i++)
        {
            float target = i == _current && _playing != null ? 1 : 0;
            _level[i] = Mathf.MoveToward(_level[i], target, dt / Fade);
            _players[i].VolumeDb = _level[i] <= 0.001f ? -80 : Mathf.LinearToDb(_level[i]);
            if (_level[i] <= 0.001f && i != _current && _players[i].Playing) _players[i].Stop();
        }
        if (_players[_current].Playing == false && _playing != null && _level[_current] >= 0.99f) _playing = null;
    }

    /// <summary>Cross-fades to a track (null: fade out).</summary>
    void Start(string? name)
    {
        var stream = name == null ? null : Load(name);
        _current = 1 - _current;
        _playing = stream == null ? null : name;
        var p = _players[_current];
        p.Stop();
        if (stream == null) return;
        p.Stream = stream;
        p.Play();
    }
}
