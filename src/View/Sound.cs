using System;
using System.Collections.Generic;
using Godot;

namespace StationX.View;

/// <summary>
/// Every sound in the game is synthesised at startup: no audio files to ship or license.
/// One-shots go to the Sfx bus; three loops (room tone, teleprinters, Bombes) go to the Ambience bus
/// and are faded to match what is happening on the floor.
/// </summary>
public partial class Sound : Node
{
    public static Sound? Instance;
    const int Rate = 22050;

    readonly Dictionary<string, AudioStreamWav> _clips = new();
    readonly List<AudioStreamPlayer> _pool = new();
    readonly Dictionary<string, (AudioStreamPlayer player, float target)> _loops = new();
    int _next;
    readonly Random _rng = new(7);

    public override void _Ready()
    {
        Instance = this;
        EnsureBus("Sfx");
        EnsureBus("Ambience");

        _clips["click"] = Make(0.03f, (t, r) => Noise(r) * Env(t, 0.004f) * 0.5f, highpass: true);
        _clips["type"] = Make(0.07f, (t, r) => (Noise(r) * Env(t, 0.006f) + Mathf.Sin(t * Mathf.Tau * 140) * Env(t, 0.02f) * 0.6f) * 0.6f, highpass: true);
        _clips["stamp"] = Make(0.22f, (t, r) => (Mathf.Sin(t * Mathf.Tau * 85) * Env(t, 0.05f) + Noise(r) * Env(t, 0.01f) * 0.5f) * 0.9f);
        _clips["chime"] = Make(1.4f, (t, r) => (Mathf.Sin(t * Mathf.Tau * 880) * 0.5f + Mathf.Sin(t * Mathf.Tau * 1320) * 0.35f * Step(t, 0.12f)) * Env(t, 0.45f) * 0.45f);
        _clips["bad"] = Make(0.45f, (t, r) => Mathf.Clamp(Mathf.Sin(t * Mathf.Tau * 160) * 3, -1, 1) * Env(t, 0.14f) * 0.25f);
        _clips["bell"] = Make(2.6f, (t, r) => (Mathf.Sin(t * Mathf.Tau * 660) + 0.5f * Mathf.Sin(t * Mathf.Tau * 1587) + 0.25f * Mathf.Sin(t * Mathf.Tau * 2411)) * Env(t, 0.7f) * 0.3f);
        _clips["stop"] = Make(0.9f, (t, r) => (Noise(r) * Env(t, 0.015f) * 0.7f + Mathf.Sin(t * Mathf.Tau * 1175) * Env(t - 0.06f, 0.3f) * Step(t, 0.06f) * 0.4f));
        _clips["paper"] = Make(0.4f, (t, r) => Noise(r) * Mathf.Sin(Mathf.Pi * t / 0.4f) * (0.5f + 0.5f * Mathf.Sin(t * 90)) * 0.35f, highpass: true);

        _clips["room"] = Loop(6f, RoomTone);
        _clips["teleprinter"] = Loop(3f, Teleprinter);
        _clips["hum"] = Loop(2f, BombeHum);
        _clips["rain"] = Loop(5f, Rain);
        _clips["wind"] = Loop(8f, Wind);

        for (int i = 0; i < 10; i++)
        {
            var p = new AudioStreamPlayer { Bus = "Sfx" };
            AddChild(p);
            _pool.Add(p);
        }
        foreach (var name in new[] { "room", "teleprinter", "hum", "rain", "wind" })
        {
            var p = new AudioStreamPlayer { Bus = "Ambience", Stream = _clips[name], VolumeDb = -60 };
            AddChild(p);
            p.Play();
            _loops[name] = (p, 0);
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        foreach (var (name, (p, target)) in _loops)
        {
            float cur = Mathf.DbToLinear(p.VolumeDb);
            cur = Mathf.MoveToward(cur, target, (float)delta * 0.6f);
            p.VolumeDb = cur <= 0.001f ? -60 : Mathf.LinearToDb(cur);
        }
    }

    public static void Play(string name, float volume = 1f, float pitchJitter = 0.05f)
    {
        var s = Instance;
        if (s == null || !s._clips.TryGetValue(name, out var clip)) return;
        var p = s._pool[s._next++ % s._pool.Count];
        p.Stream = clip;
        p.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, volume));
        p.PitchScale = 1 + ((float)s._rng.NextDouble() * 2 - 1) * pitchJitter;
        p.Play();
    }

    /// <summary>Sets how loud a loop should be (0..1); it fades there.</summary>
    public static void SetLoop(string name, float level)
    {
        var s = Instance;
        if (s == null || !s._loops.TryGetValue(name, out var l)) return;
        s._loops[name] = (l.player, Mathf.Clamp(level, 0, 1));
    }

    public static void SetVolumes(float master, float sfx, float ambience)
    {
        EnsureBus("Sfx");
        EnsureBus("Ambience");
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(0.0001f, master)));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Sfx"), Mathf.LinearToDb(Mathf.Max(0.0001f, sfx)));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Ambience"), Mathf.LinearToDb(Mathf.Max(0.0001f, ambience)));
    }

    static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        AudioServer.AddBus();
        int i = AudioServer.BusCount - 1;
        AudioServer.SetBusName(i, name);
        AudioServer.SetBusSend(i, "Master");
    }

    // ------------------------------------------------------------- synthesis

    static float Noise(Random r) => (float)r.NextDouble() * 2 - 1;
    static float Env(float t, float decay) => t < 0 ? 0 : Mathf.Exp(-t / decay) * Mathf.Min(1, t * 2000);
    static float Step(float t, float at) => t >= at ? 1 : 0;

    static AudioStreamWav Make(float seconds, Func<float, Random, float> f, bool highpass = false)
    {
        int n = (int)(seconds * Rate);
        var samples = new float[n];
        var r = new Random(n);
        for (int i = 0; i < n; i++) samples[i] = f(i / (float)Rate, r);
        if (highpass)
            for (int i = n - 1; i > 0; i--) samples[i] -= samples[i - 1] * 0.85f;
        return Wav(samples, loop: false);
    }

    /// <summary>A seamless loop: the tail is cross-faded into the head.</summary>
    static AudioStreamWav Loop(float seconds, Func<float, Random, float> f)
    {
        int n = (int)(seconds * Rate), fade = Rate / 4;
        var raw = new float[n + fade];
        var r = new Random(n);
        for (int i = 0; i < raw.Length; i++) raw[i] = f(i / (float)Rate, r);
        var s = new float[n];
        Array.Copy(raw, s, n);
        for (int i = 0; i < fade; i++)
        {
            float a = i / (float)fade;
            s[i] = s[i] * a + raw[n + i] * (1 - a);
        }
        return Wav(s, loop: true);
    }

    static AudioStreamWav Wav(float[] s, bool loop)
    {
        var data = new byte[s.Length * 2];
        for (int i = 0; i < s.Length; i++)
        {
            short v = (short)(Mathf.Clamp(s[i], -1, 1) * 30000);
            data[2 * i] = (byte)(v & 0xFF);
            data[2 * i + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = data,
            LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = loop ? s.Length : 0,
        };
    }

    // Brown-ish room tone with the odd creak and distant typewriter.
    static float _brown;
    static float RoomTone(float t, Random r)
    {
        _brown = Mathf.Clamp(_brown * 0.995f + Noise(r) * 0.02f, -1, 1);
        float tap = 0;
        float beat = t * 3.1f;
        float frac = beat - Mathf.Floor(beat);
        if (Gfx.Rand01((int)beat, 1, 77) > 0.55f) tap = Noise(r) * Env(frac / 3.1f, 0.004f) * 0.12f;
        return _brown * 0.35f + tap;
    }

    // Rain on felt roofs: a soft hiss with scattered drips.
    static float _rainLp;
    static float Rain(float t, Random r)
    {
        _rainLp = _rainLp * 0.7f + Noise(r) * 0.3f;
        float drip = r.NextDouble() < 0.0009 ? 0.5f : 0;
        return _rainLp * 0.45f + drip * Noise(r);
    }

    // Wind: brown noise swelling and fading in gusts.
    static float _windLp;
    static float Wind(float t, Random r)
    {
        _windLp = Mathf.Clamp(_windLp * 0.998f + Noise(r) * 0.012f, -1, 1);
        float gust = 0.45f + 0.35f * Mathf.Sin(t * Mathf.Tau / 8f) + 0.2f * Mathf.Sin(t * Mathf.Tau * 3 / 8f);
        return _windLp * gust * 1.1f;
    }

    // Creed teleprinter: about 6.7 characters a second, each a rattle of the selector magnets.
    static float Teleprinter(float t, Random r)
    {
        float cps = 6.66f;
        float ch = t * cps, frac = ch - Mathf.Floor(ch);
        float pause = Gfx.Rand01((int)ch, 2, 91) > 0.85f ? 0 : 1;
        float rattle = 0;
        for (int k = 0; k < 5; k++)
        {
            float at = k * 0.13f + 0.02f;
            if (Gfx.Rand01((int)ch, k, 92) > 0.4f) rattle += Env((frac - at) / cps, 0.0025f);
        }
        float motor = Mathf.Sin(t * Mathf.Tau * 50) * 0.08f + Mathf.Sin(t * Mathf.Tau * 100) * 0.04f;
        return (Noise(r) * rattle * 0.55f * pause) + motor;
    }

    // Bombe: motor hum and the tick of the drums stepping round.
    static float BombeHum(float t, Random r)
    {
        float hum = Mathf.Sin(t * Mathf.Tau * 50) * 0.3f + Mathf.Sin(t * Mathf.Tau * 100) * 0.18f + Mathf.Sin(t * Mathf.Tau * 150) * 0.06f;
        float tick = t * 12, frac = tick - Mathf.Floor(tick);
        return hum * (0.85f + 0.15f * Mathf.Sin(t * Mathf.Tau * 3)) + Noise(r) * Env(frac / 12, 0.003f) * 0.35f;
    }
}
