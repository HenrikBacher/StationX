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

        // Air-raid siren: the rising and falling wail of the alert, and the steady note of the all-clear.
        _clips["siren"] = Make(7f, (t, r) =>
        {
            float phase = Mathf.Tau * 480 * t - 210 * Mathf.Cos(Mathf.Tau * t / 3.5f);
            float env = Mathf.Clamp(t / 1.2f, 0, 1) * Mathf.Clamp((7 - t) / 1.5f, 0, 1);
            return (Mathf.Sin(phase) * 0.6f + Mathf.Sin(phase * 2) * 0.18f + Mathf.Sin(phase * 3) * 0.08f) * env * 0.35f;
        });
        _clips["allclear"] = Make(4f, (t, r) =>
        {
            float env = Mathf.Clamp(t / 0.8f, 0, 1) * Mathf.Clamp((4 - t) / 1.2f, 0, 1);
            float ph = Mathf.Tau * 640 * t;
            return (Mathf.Sin(ph) * 0.6f + Mathf.Sin(ph * 2) * 0.15f) * env * 0.3f;
        });
        // A single-cylinder army motorcycle going by: the thump of each firing, a rasp of exhaust, up and away.
        _clips["motorbike"] = Make(3.2f, (t, r) =>
        {
            float rpm = 16 + 10 * Mathf.Clamp(t / 1.2f, 0, 1);
            float ph = t * rpm % 1f;
            float thump = Mathf.Exp(-ph * 9) * Mathf.Sin(ph * Mathf.Tau * 2.2f);
            float env = Mathf.Clamp(t / 0.5f, 0, 1) * Mathf.Clamp((3.2f - t) / 1.6f, 0, 1);
            return (thump * 0.8f + Noise(r) * Mathf.Exp(-ph * 14) * 0.35f + Mathf.Sin(t * Mathf.Tau * rpm * 3) * 0.12f) * env * 0.5f;
        });
        // A church clock across the fields: a bell's inharmonic partials (hum, prime, tierce, quint, nominal), far off.
        _clips["toll"] = Make(4.5f, (t, r) =>
        {
            float f = 293;
            float v = Mathf.Sin(t * Mathf.Tau * f * 0.5f) * 0.5f * Env(t, 2.2f) + Mathf.Sin(t * Mathf.Tau * f) * 0.6f * Env(t, 1.4f)
                      + Mathf.Sin(t * Mathf.Tau * f * 1.19f) * 0.35f * Env(t, 1.0f) + Mathf.Sin(t * Mathf.Tau * f * 1.5f) * 0.2f * Env(t, 0.8f)
                      + Mathf.Sin(t * Mathf.Tau * f * 2.0f) * 0.25f * Env(t, 0.6f);
            return v * 0.22f;
        });
        _clips["train"] = Train();
        _clips["aircraft"] = Aircraft();
        _clips["birds"] = Loop(11f, Birds);
        // The teleprinter bell rung three times: the signal for a MOST IMMEDIATE message.
        _clips["flash"] = Make(1.6f, (t, r) =>
        {
            float v = 0;
            for (int k = 0; k < 3; k++)
            {
                float lt = t - k * 0.22f;
                if (lt < 0) continue;
                v += (Mathf.Sin(lt * Mathf.Tau * 1568) * 0.6f + Mathf.Sin(lt * Mathf.Tau * 2350) * 0.3f + Mathf.Sin(lt * Mathf.Tau * 3920) * 0.12f) * Env(lt, 0.25f);
            }
            return v * 0.35f;
        });
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
        foreach (var name in new[] { "room", "teleprinter", "hum", "rain", "wind", "birds" })
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
        _clock += delta;
        for (int i = _queue.Count - 1; i >= 0; i--)
            if (_queue[i].at <= _clock)
            {
                var q = _queue[i];
                _queue.RemoveAt(i);
                Play(q.name, q.volume, q.jitter);
            }
        foreach (var (name, (p, target)) in _loops)
        {
            float cur = Mathf.DbToLinear(p.VolumeDb);
            cur = Mathf.MoveToward(cur, target, (float)delta * 0.6f);
            p.VolumeDb = cur <= 0.001f ? -60 : Mathf.LinearToDb(cur);
        }
    }

    readonly List<(double at, string name, float volume, float jitter)> _queue = new();
    double _clock;

    /// <summary>Plays a clip after a delay (seconds of real time): bell strokes, bursts of typing.</summary>
    public static void PlayAfter(float delay, string name, float volume = 1f, float pitchJitter = 0.05f) =>
        Instance?._queue.Add((Instance._clock + delay, name, volume, pitchJitter));

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

    public static void SetVolumes(float master, float sfx, float ambience, float music)
    {
        EnsureBus("Sfx");
        EnsureBus("Ambience");
        EnsureBus("Music");
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Music"), Mathf.LinearToDb(Mathf.Max(0.0001f, music)));
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(0.0001f, master)));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Sfx"), Mathf.LinearToDb(Mathf.Max(0.0001f, sfx)));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Ambience"), Mathf.LinearToDb(Mathf.Max(0.0001f, ambience)));
    }

    public static void EnsureBus(string name)
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

    // Birdsong through the windows: blackbird-ish warbles and the odd sparrow chirp, scattered across the loop.
    static float Birds(float t, Random r)
    {
        float v = 0;
        // Each half-second slot may hold a call: a quick falling or rising sweep, sometimes trilled.
        float slot = t * 2, frac = slot - Mathf.Floor(slot);
        int k = (int)slot;
        if (Gfx.Rand01(k, 1, 301) > 0.55f)
        {
            float f0 = 2200 + 2600 * Gfx.Rand01(k, 2, 301);
            float sweep = (Gfx.Rand01(k, 3, 301) - 0.5f) * 1800;
            float len = 0.08f + 0.18f * Gfx.Rand01(k, 4, 301);
            float lt = frac / 2;
            if (lt < len)
            {
                float trill = Gfx.Rand01(k, 5, 301) > 0.6f ? 0.5f + 0.5f * Mathf.Sin(lt * Mathf.Tau * 28) : 1;
                float ph = Mathf.Tau * (f0 * lt + sweep * lt * lt / (2 * len));
                v += Mathf.Sin(ph) * Mathf.Sin(Mathf.Pi * lt / len) * trill * (0.3f + 0.4f * Gfx.Rand01(k, 6, 301));
            }
        }
        return v * 0.35f;
    }

    // A goods train on the Bletchley line, a mile off: the chuff of the exhaust speeding up and fading, and a whistle.
    static AudioStreamWav Train()
    {
        float len = 16f;
        int n = (int)(len * Rate);
        var s = new float[n];
        var r = new Random(1941);
        float lp = 0, lp2 = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float rate = 2.6f + 0.25f * t;                      // beats a second
            float beat = t * rate, frac = beat - Mathf.Floor(beat);
            float chuff = Mathf.Exp(-frac * 7) * (0.7f + 0.3f * Gfx.Rand01((int)beat, 1, 55));
            float env = Mathf.Clamp(t / 4f, 0, 1) * Mathf.Clamp((len - t) / 6f, 0, 1);
            lp += (Noise(r) - lp) * 0.08f;                        // muffled by distance
            float rumble = Mathf.Sin(t * Mathf.Tau * 38) * 0.15f;
            float w = 0;
            if (t > 4.5f && t < 6.4f)
            {
                float wt = t - 4.5f;
                float wenv = Mathf.Clamp(wt / 0.15f, 0, 1) * Mathf.Clamp((1.9f - wt) / 0.4f, 0, 1);
                w = (Mathf.Sin(t * Mathf.Tau * 523) * 0.5f + Mathf.Sin(t * Mathf.Tau * 659) * 0.4f + Mathf.Sin(t * Mathf.Tau * 784) * 0.25f) * wenv * 0.35f
                    + Noise(r) * wenv * 0.05f;
            }
            float v = (lp * chuff * 1.6f + rumble) * env + w * Mathf.Clamp(env * 1.5f, 0, 1);
            lp2 += (v - lp2) * 0.35f;
            s[i] = lp2 * 0.6f;
        }
        return Wav(s, loop: false);
    }

    // Aircraft passing high over: a beating drone of radial engines, swelling and dying away.
    static AudioStreamWav Aircraft()
    {
        float len = 20f;
        int n = (int)(len * Rate);
        var s = new float[n];
        var r = new Random(1940);
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float doppler = 1 + 0.03f * Mathf.Cos(Mathf.Pi * t / len);
            float drone = Mathf.Sin(t * Mathf.Tau * 86 * doppler) * 0.5f + Mathf.Sin(t * Mathf.Tau * 89.5f * doppler) * 0.45f
                          + Mathf.Sin(t * Mathf.Tau * 172 * doppler) * 0.25f + Mathf.Sin(t * Mathf.Tau * 258 * doppler) * 0.1f;
            lp += (Noise(r) - lp) * 0.05f;
            float env = Mathf.Pow(Mathf.Sin(Mathf.Pi * t / len), 2);
            s[i] = (drone * 0.6f + lp * 0.8f) * env * 0.4f;
        }
        return Wav(s, loop: false);
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
