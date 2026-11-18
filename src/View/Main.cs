using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using StationX.Sim;
using StationX.View.Ui;

namespace StationX.View;

public sealed record Ghost(StationType Type, int X, int Y, int Rot, string? Problem);

/// <summary>Root node: owns the game state, the 3D park, the camera, input and the build tool.</summary>
public partial class Main : Node3D
{
    public GameState State = null!;
    public bool Paused { get; private set; }
    public int Speed { get; private set; } = 1;
    public static readonly int[] Speeds = { 1, 3, 6 };

    public CameraRig Rig = null!;
    public Hud Hud = null!;
    Park3D _park = null!;
    Node3D _stationRoot = null!, _peopleRoot = null!;
    DirectionalLight3D _sun = null!;
    WorldEnvironment _env = null!;
    MeshInstance3D _selRing = null!;

    public readonly Dictionary<int, StationModel> StationModels = new();
    public readonly Dictionary<int, PersonModel> PersonModels = new();

    public StationType? BuildType { get; private set; }
    int _buildRot;
    public Ghost? Ghost;
    public StationModel? GhostModel;
    MeshInstance3D _ghostFloor = null!, _ghostUse = null!;
    (StationType, int)? _ghostKey;
    bool? _ghostOk;
    readonly StandardMaterial3D _ghostGood = Mesh3.Ghost(new Color(0.45f, 0.95f, 0.45f, 0.55f));
    readonly StandardMaterial3D _ghostBad = Mesh3.Ghost(new Color(1f, 0.35f, 0.3f, 0.55f));

    public int? SelectedStation, SelectedStaff, HoverStaff, HoverStation;

    bool _dragging, _dragMoved;
    Vector2 _dragLast, _dragStart;

    string? _shotPath;
    int _shotFrames = -1;
    bool _selfTest;

    /// <summary>The station being moved with the move tool, if any.</summary>
    public int? MovingStation { get; private set; }
    public bool ShowFlows;
    /// <summary>False for screenshots and the self-test, which must not stop for dialogs.</summary>
    public bool Interactive => _shotPath == null && !_selfTest;
    int _lastDay = -1, _lastDelivered, _lastKeys;
    double _lastMinute;
    double _stampCooldown;

    public override void _Ready()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Fonts.Load();
        var userArgs = OS.GetCmdlineUserArgs();
        // Test and screenshot runs keep their hands off the player's settings; the self-test also has its own saves.
        if (userArgs.Any(a => a.StartsWith("--selftest") || a.StartsWith("--shot"))) Settings.Persist = false;
        if (userArgs.Any(a => a.StartsWith("--selftest"))) Saves.Dir = "user://selftest-saves";
        Settings.Load();
        AddChild(new Sound { Name = "Sound" });
        AddChild(new FilmGrain { Name = "FilmGrain" });

        _env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("17130f"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("c9c5b8"),
                AmbientLightEnergy = 0.62f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                TonemapExposure = 1.0f,
                SsaoEnabled = true,
                SsaoRadius = 0.7f,
                SsaoIntensity = 2.0f,
                GlowEnabled = true,
                GlowIntensity = 0.3f,
                AdjustmentEnabled = true,
                AdjustmentSaturation = 0.82f,
                AdjustmentContrast = 1.06f,
            },
        };
        AddChild(_env);
        _sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            LightColor = new Color("f4ead8"),
            LightEnergy = 1.0f,
            DirectionalShadowMaxDistance = 140,
            ShadowBlur = 1.5f,
        };
        AddChild(_sun);

        _park = new Park3D { Name = "Park" };
        AddChild(_park);
        _stationRoot = new Node3D { Name = "Stations" };
        _peopleRoot = new Node3D { Name = "People" };
        AddChild(_stationRoot);
        AddChild(_peopleRoot);

        _selRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.42f, OuterRadius = 0.5f, Rings = 24, RingSegments = 6 },
            MaterialOverride = Mesh3.Mat(Gfx.Brass, 1.2f),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_selRing);
        _ghostFloor = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1, 0.02f, 1) }, Visible = false };
        _ghostUse = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.02f }, Visible = false };
        AddChild(_ghostFloor);
        AddChild(_ghostUse);

        Rig = new CameraRig { Name = "Camera" };
        AddChild(Rig);

        var overlayLayer = new CanvasLayer { Name = "OverlayLayer", Layer = 0 };
        AddChild(overlayLayer);
        overlayLayer.AddChild(new Overlay { Main = this, Name = "Overlay" });
        Hud = new Hud(this) { Name = "Hud" };
        AddChild(Hud);
        Settings.Apply();

        var args = OS.GetCmdlineUserArgs().Select(a => a.Split('=', 2)).ToDictionary(a => a[0].TrimStart('-'), a => a.Length > 1 ? a[1] : "");
        int seed = args.TryGetValue("seed", out var sd) ? int.Parse(sd) : (int)(Time.GetUnixTimeFromSystem() % 100000);
        if (args.TryGetValue("shot", out var shot)) _shotPath = shot;
        _selfTest = args.ContainsKey("selftest");
        var diff = args.TryGetValue("difficulty", out var df) ? Enum.Parse<Difficulty>(df, true) : Difficulty.Normal;
        var mode = args.ContainsKey("freeplay") ? GameMode.FreePlay : GameMode.Campaign;
        int startAct = args.TryGetValue("act", out var sa) ? int.Parse(sa) : 0;
        NewGame(seed, showTitle: _shotPath == null && !_selfTest && !args.ContainsKey("skip-title"), diff, mode, startAct);
        if (args.TryGetValue("load", out var slot))
        {
            if (Saves.Read(slot, out var err) is { } loaded) LoadState(loaded);
            else GD.PrintErr($"Could not load {slot}: {err}");
        }
        if (_selfTest) { _ = SelfTest(); return; }

        if (_shotPath != null)
        {
            int days = args.TryGetValue("days", out var d) ? int.Parse(d) : 0;
            if (days > 0)
            {
                var ai = new AutoPlayer(State);
                while (State.Day < days && State.Status == GameStatus.Playing)
                {
                    ai.Think();
                    State.Advance(60);
                    State.Events.Clear();
                }
            }
            if (args.TryGetValue("hour", out var hr))
            {
                double target = State.Day * Defs.MinutesPerDay + double.Parse(hr) * 60;
                if (target < State.Now) target += Defs.MinutesPerDay;
                while (State.Now < target && State.Status == GameStatus.Playing)
                {
                    if (State.ActCompletePending) State.ContinueAfterAct();
                    State.Advance(30);
                    State.Events.Clear();
                }
            }
            if (args.TryGetValue("zoom", out var z)) Rig.Cam.Size = 18f / float.Parse(z);
            if (args.TryGetValue("center", out var c))
            {
                var p = c.Split(',');
                Rig.LookAtTile(float.Parse(p[0]), float.Parse(p[1]));
            }
            if (args.TryGetValue("panel", out var panel)) Hud.OpenPanel(panel);
            if (args.ContainsKey("breakdown") && State.Stations.FirstOrDefault(s => s.Wears) is { } worn)
            {
                worn.DownUntil = State.Now + Defs.RepairMinutes;
                if (State.Stations.Where(s => s.Wears && s != worn).Skip(0).FirstOrDefault() is { } other) other.Wear = 0.85;
            }
            if (args.TryGetValue("select", out var sel) && sel == "bombe")
                Select(State.Stations.FirstOrDefault(s => s.Type is StationType.Bombe or StationType.Bombe4)?.Id, null);
            if (args.ContainsKey("briefing")) Hud.ShowBriefing();
            if (args.ContainsKey("title")) Hud.ShowTitle();
            if (args.TryGetValue("build", out var bt)) BeginBuild(Enum.Parse<StationType>(bt));
            if (args.TryGetValue("quarter", out var q)) { Rig.SetQuarter(int.Parse(q)); _park.SetView(Rig.Quarter); }
            if (args.ContainsKey("flows")) ShowFlows = true;
            if (args.TryGetValue("modal", out var modal)) Hud.ShowNamed(modal);
            _shotFrames = 30;
        }
    }

    public void NewGame(int seed, bool showTitle, Difficulty difficulty = Difficulty.Normal, GameMode mode = GameMode.Campaign, int startAct = 0)
    {
        AdoptState(GameState.NewGame(seed, difficulty, mode, startAct));
        State.Events.RemoveAll(e => e.Kind == EventKind.ActStarted);
        if (showTitle) Hud.ShowTitle();
        else if (_shotPath == null && !_selfTest) Hud.ShowBriefing();
    }

    /// <summary>Switches to a loaded game.</summary>
    public void LoadState(GameState g)
    {
        AdoptState(g);
        State.Events.Clear();
        Hud.Toast($"Loaded: {g.Act.ChapterTitle}, {g.Date:d MMMM yyyy}");
    }

    void AdoptState(GameState g)
    {
        State = g;
        _park.State = State;
        _park.ForceRebuild();
        foreach (var m in StationModels.Values) m.QueueFree();
        foreach (var m in PersonModels.Values) m.QueueFree();
        StationModels.Clear();
        PersonModels.Clear();
        Rig.LookAtTile(11, 8);
        Rig.Cam.Size = 16;
        Paused = false;
        Speed = 1;
        CancelBuild();
        Select(null, null);
        Hud.Reset();
        _lastDay = State.Day;
        _lastDelivered = State.Stats.DeliveredTotal;
        _lastKeys = State.KeysToday;
        _lastMinute = State.MinuteOfDay;
    }

    public string? SaveTo(string slot)
    {
        var err = Saves.Write(slot, State);
        Hud.Toast(err == null ? $"Saved to {Saves.SlotName(slot).ToLowerInvariant()}." : $"Save failed: {err}", err == null ? EventKind.Info : EventKind.Bad);
        return err;
    }

    public bool LoadFrom(string slot)
    {
        var g = Saves.Read(slot, out var err);
        if (g == null) { Hud.Toast($"Load failed: {err}", EventKind.Bad); return false; }
        LoadState(g);
        return true;
    }

    public void RotateView(int steps)
    {
        Rig.Rotate(steps);
        _park.SetView(Rig.Quarter);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_shotFrames >= 0 && _shotFrames-- == 0)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath!);
            GD.Print($"Saved screenshot to {_shotPath}");
            GetTree().Quit();
        }

        bool running = !Paused && !Hud.ModalOpen && _shotPath == null;
        if (running) State.Advance(delta * Defs.GameMinutesPerRealSecond * Speed);
        foreach (var e in State.Events)
        {
            Hud.OnEvent(e);
            switch (e.Kind)
            {
                case EventKind.Good: Sound.Play("chime", 0.7f); break;
                case EventKind.Bad: Sound.Play("bad", 0.8f); break;
                case EventKind.ActStarted or EventKind.Dilemma: Sound.Play("paper"); break;
                case EventKind.Won or EventKind.ActComplete: Sound.Play("bell"); break;
            }
        }
        State.Events.Clear();
        Soundscape(dt);
        Autosave();

        SyncStations(running ? dt : 0);
        SyncPeople(dt);
        PanKeys(dt);
        UpdateHover();
        UpdateGhost();
        UpdateSelectionRing();
        UpdateDayNight();
    }

    // ------------------------------------------------------------- syncing

    void SyncStations(float dt)
    {
        foreach (var st in State.Stations)
        {
            if (!StationModels.TryGetValue(st.Id, out var m))
            {
                m = StationModel.Create(st.Type);
                m.StationId = st.Id;
                m.PlaceAt(st.X, st.Y, st.Rot);
                _stationRoot.AddChild(m);
                StationModels[st.Id] = m;
            }
            m.Sync(st, State, dt);
        }
        foreach (var id in StationModels.Keys.Where(id => State.StationById(id) == null).ToList())
        {
            StationModels[id].QueueFree();
            StationModels.Remove(id);
        }
    }

    void SyncPeople(float dt)
    {
        foreach (var s in State.Staff)
        {
            if (!PersonModels.TryGetValue(s.Id, out var m))
            {
                m = PersonModel.Create(s);
                _peopleRoot.AddChild(m);
                PersonModels[s.Id] = m;
            }
            Vector3? face = null;
            PersonModel.RestSpot? rest = null;
            if (s.Task is { Kind: TaskKind.Operate } t && StationModels.TryGetValue(t.Dest, out var sm)) face = sm.GlobalPosition;
            else if (s.Task is { Kind: TaskKind.Rest } r && StationModels.TryGetValue(r.Dest, out var rm))
            {
                var (at, f) = rm.Seat(r.Seat);
                rest = new PersonModel.RestSpot(rm.ToGlobal(at), rm.ToGlobal(f), rm.Seated, rm.Type == StationType.Rounders);
            }
            m.Sync(s, State, dt, face, rest);
        }
        foreach (var id in PersonModels.Keys.Where(id => State.StaffById(id) == null).ToList())
        {
            PersonModels[id].QueueFree();
            PersonModels.Remove(id);
        }
    }

    void UpdateSelectionRing()
    {
        if (SelectedStaff is int sid && PersonModels.TryGetValue(sid, out var pm))
        {
            _selRing.Visible = true;
            _selRing.Position = pm.GlobalPosition + new Vector3(0, 0.03f, 0);
            _selRing.Scale = new Vector3(0.55f, 1, 0.55f);
        }
        else if (SelectedStation is int stid && State.StationById(stid) is { } st)
        {
            var (w, h) = st.Dims;
            _selRing.Visible = true;
            _selRing.Position = new Vector3(st.X + w / 2f, 0.03f, st.Y + h / 2f);
            _selRing.Scale = new Vector3(w * 1.15f, 1, h * 1.15f);
        }
        else _selRing.Visible = false;
    }

    // ---------------------------------------------------------------- sound

    void Soundscape(float dt)
    {
        _stampCooldown -= dt;
        if (State.Stats.DeliveredTotal > _lastDelivered && _stampCooldown <= 0)
        {
            Sound.Play("stamp", 0.45f);
            _stampCooldown = 0.6;
        }
        _lastDelivered = State.Stats.DeliveredTotal;
        if (State.KeysToday > _lastKeys) Sound.Play("stop", 0.8f);
        _lastKeys = State.KeysToday;

        // The bell for the change of watch (or the start and end of the long day).
        double m = State.MinuteOfDay;
        int[] changes = State.WatchesAvailable ? new[] { 0, 9 * 60, 16 * 60 } : new[] { 7 * 60, 22 * 60 };
        foreach (var c in changes)
            if (_lastMinute < c && m >= c || (c == 0 && m < _lastMinute))
            {
                Sound.Play("bell", 0.7f, 0);
                break;
            }
        _lastMinute = m;

        bool live = !Hud.ModalOpen || _shotPath != null;
        int machines = State.Stations.Count(st => st.IsMachine && st.Working);
        int clatter = State.Stations.Where(st => st.Type == StationType.Inbox).Sum(st => st.Output.Count);
        float near = Mathf.Clamp(14f / Rig.Cam.Size, 0.35f, 1.2f);
        Sound.SetLoop("room", live ? 0.55f : 0.25f);
        Sound.SetLoop("teleprinter", live && !Paused ? Mathf.Clamp(clatter / 8f + (State.Contracts.Any(c => c.State == ContractState.Active) ? 0.2f : 0), 0, 0.8f) * near : 0);
        Sound.SetLoop("hum", live && !Paused ? Mathf.Clamp(machines * 0.2f, 0, 0.9f) * near : 0);
    }

    void Autosave()
    {
        if (State.Day == _lastDay) return;
        _lastDay = State.Day;
        if (_shotPath != null || _selfTest || State.Status != GameStatus.Playing) return;
        Saves.Write("auto", State);
    }

    // ------------------------------------------------------------ day/night

    void UpdateDayNight()
    {
        double h = State.MinuteOfDay / 60.0;
        float night = h switch
        {
            < 5 => 1f,
            < 7.5 => 1f - (float)((h - 5) / 2.5),
            < 18.5 => 0f,
            < 21 => (float)((h - 18.5) / 2.5),
            _ => 1f,
        };
        float dayT = (float)Math.Clamp((h - 6) / 14.0, 0, 1);
        _sun.RotationDegrees = new Vector3(Mathf.Lerp(-58, -42, night), Mathf.Lerp(85, 5, dayT), 0);
        var dusk = new Color("ffbd85");
        var moon = new Color("8fa3d6");
        _sun.LightColor = night < 0.5f ? new Color("f4ead8").Lerp(dusk, night * 2) : dusk.Lerp(moon, (night - 0.5f) * 2);
        _sun.LightEnergy = Mathf.Lerp(1.0f, 0.3f, night);
        var env = _env.Environment;
        env.AmbientLightColor = new Color("c9c5b8").Lerp(new Color("46506e"), night);
        env.AmbientLightEnergy = Mathf.Lerp(0.62f, 0.5f, night);
        foreach (var l in _park.Lamps) l.LightEnergy = night * 1.6f;
    }

    // --------------------------------------------------------------- camera

    public void SetSpeed(int speed) { Speed = speed; Paused = false; }
    public void TogglePause() => Paused = !Paused;
    public void CenterOnTile(float x, float y) => Rig.LookAtTile(x, y);

    void PanKeys(float dt)
    {
        if (Hud.ModalOpen) return;
        var v = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) v.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) v.X += 1;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) v.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) v.Y += 1;
        if (Settings.EdgeScroll && !_selfTest && _shotPath == null && !_dragging && DisplayServer.WindowIsFocused())
        {
            var size = GetViewport().GetVisibleRect().Size;
            var mp = Mouse;
            const float edge = 6;
            if (mp.X >= 0 && mp.Y >= 0 && mp.X <= size.X && mp.Y <= size.Y)
            {
                if (mp.X < edge) v.X -= 1;
                if (mp.X > size.X - edge) v.X += 1;
                if (mp.Y < edge) v.Y -= 1;
                if (mp.Y > size.Y - edge) v.Y += 1;
            }
        }
        if (v != Vector2.Zero) Rig.PanScreen(v.Normalized(), 16, dt);
    }

    // ---------------------------------------------------------------- hover

    /// <summary>The pointer, as last reported by an input event (so synthetic events in the self-test count too).</summary>
    Vector2 Mouse => _mouse ?? GetViewport().GetMousePosition();
    Vector2? _mouse;

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouse m) _mouse = m.Position;
    }

    (int x, int y)? TileUnderMouse(float height = 0)
    {
        var p = Rig.GroundUnder(Mouse, height);
        if (p is not Vector3 v) return null;
        int x = (int)Mathf.Floor(v.X), y = (int)Mathf.Floor(v.Z);
        return Grid.InBounds(x, y) ? (x, y) : null;
    }

    Staff? StaffUnderMouse()
    {
        Staff? best = null;
        float bestD = 18 * 18;
        foreach (var s in State.Staff)
        {
            if (!PersonModels.TryGetValue(s.Id, out var pm)) continue;
            var sp = Rig.Cam.UnprojectPosition(pm.GlobalPosition + new Vector3(0, 0.55f, 0));
            float d = (sp - Mouse).LengthSquared();
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }

    Station? StationUnderMouse()
    {
        // Try the top of tall machines first, then desks, then the floor.
        foreach (float h in new[] { 1.0f, 0.45f, 0f })
            if (TileUnderMouse(h) is var (x, y) && State.Grid.OccupantAt(x, y) is int id && id >= 0)
                return State.StationById(id);
        return null;
    }

    void UpdateHover()
    {
        bool overUi = Hud.IsMouseOverUi() || Hud.ModalOpen;
        var staff = overUi ? null : StaffUnderMouse();
        HoverStaff = staff?.Id;
        HoverStation = staff == null && !overUi ? StationUnderMouse()?.Id : null;
    }

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent e)
    {
        if (Hud.ModalOpen) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } mb:
                Rig.ZoomAt(1.12f, mb.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } mb:
                Rig.ZoomAt(1 / 1.12f, mb.Position);
                break;
            case InputEventMouseButton mb when mb.ButtonIndex is MouseButton.Right or MouseButton.Middle:
                if (mb.Pressed)
                {
                    _dragging = true; _dragMoved = false;
                    _dragLast = _dragStart = mb.Position;
                }
                else
                {
                    _dragging = false;
                    if (!_dragMoved && mb.ButtonIndex == MouseButton.Right)
                    {
                        if (BuildType != null) CancelBuild(); else Select(null, null);
                    }
                }
                break;
            case InputEventMouseMotion mm when _dragging:
                if ((mm.Position - _dragStart).Length() > 4) _dragMoved = true;
                Rig.PanPixels(mm.Position - _dragLast);
                _dragLast = mm.Position;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                LeftClick();
                break;
            case InputEventKey { Pressed: true, Echo: false } k:
                // Brackets by physical position, for layouts where they need AltGr.
                if (k.PhysicalKeycode == Key.Bracketleft) { RotateView(-1); break; }
                if (k.PhysicalKeycode == Key.Bracketright) { RotateView(1); break; }
                HandleKey(k);
                break;
        }
    }

    void HandleKey(InputEventKey k)
    {
        switch (k.Keycode)
        {
            case Key.Space: TogglePause(); break;
            case Key.Key1: SetSpeed(1); break;
            case Key.Key2: SetSpeed(3); break;
            case Key.Key3: SetSpeed(6); break;
            case Key.R when BuildType != null: _buildRot = (_buildRot + (k.ShiftPressed ? 3 : 1)) % 4; break;
            case Key.Escape:
                if (BuildType != null) CancelBuild();
                else if (SelectedStaff != null || SelectedStation != null) Select(null, null);
                else if (Hud.PanelId != null) Hud.ClosePanel();
                else Hud.ShowPauseMenu();
                break;
            case Key.Comma: RotateView(-1); break;
            case Key.Period: RotateView(1); break;
            case Key.F: ShowFlows = !ShowFlows; Hud.Toast(ShowFlows ? "Flow view: lines show where paper goes; rings show how long it waits." : "Flow view off."); break;
            case Key.I: Hud.TogglePanel("intel"); break;
            case Key.K: Hud.TogglePanel("stats"); break;
            case Key.M when SelectedStation is int ms: BeginMove(ms); break;
            case Key.F5: SaveTo("quick"); break;
            case Key.F8: LoadFrom("quick"); break;
            case Key.B: Hud.TogglePanel("build"); break;
            case Key.H: Hud.TogglePanel("staff"); break;
            case Key.Q: Hud.TogglePanel("requests"); break;
            case Key.T: Hud.TogglePanel("research"); break;
            case Key.G: Hud.TogglePanel("huts"); break;
            case Key.F9 when OS.IsDebugBuild():
                State.Funds += 5000; State.Points += 50;
                Hud.Toast("Debug: +£5000, +50 breakthroughs");
                break;
            case Key.F10 when OS.IsDebugBuild():
                State.DebugSkipToAct(State.ActIndex + 1);
                break;
        }
    }

    void LeftClick()
    {
        if (BuildType is StationType t)
        {
            if (Ghost is not { } g) return;
            if (g.Problem != null) { Hud.Toast(g.Problem, EventKind.Bad); Sound.Play("bad", 0.5f); return; }
            if (MovingStation is int mid)
            {
                if (State.Move(mid, g.X, g.Y, g.Rot))
                {
                    if (StationModels.TryGetValue(mid, out var mm)) { mm.Visible = true; mm.PlaceAt(g.X, g.Y, g.Rot); }
                    Hud.Toast($"{Defs.Station(t).Name} moved. −£{Defs.MoveCost}");
                    Sound.Play("stamp", 0.6f);
                    CancelBuild();
                    Select(mid, null);
                }
                return;
            }
            if (State.Place(t, g.X, g.Y, g.Rot) != null)
            {
                Hud.Toast($"{Defs.Station(t).Name} built. −£{Defs.Station(t).Cost}");
                Sound.Play("stamp", 0.7f);
                if (!Input.IsKeyPressed(Key.Shift)) CancelBuild();
            }
            return;
        }
        if (StaffUnderMouse() is { } staff) { Select(null, staff.Id); return; }
        if (StationUnderMouse() is { } st) { Select(st.Id, null); return; }
        if (TileUnderMouse() is var (x, y) && State.Grid.ZoneAt(x, y) is { Unlocked: false } zone)
        {
            Select(null, null);
            Hud.ShowZone(zone);
            return;
        }
        Select(null, null);
    }

    // ----------------------------------------------------------------- build

    public void BeginMove(int stationId)
    {
        var st = State.StationById(stationId);
        if (st == null) return;
        CancelBuild();
        BuildType = st.Type;
        MovingStation = stationId;
        _buildRot = st.Rot;
        Select(null, null);
        if (StationModels.TryGetValue(stationId, out var m)) m.Visible = false;
        Hud.Toast($"Moving the {st.Def.Name.ToLowerInvariant()}: click its new place (£{Defs.MoveCost}) · R to rotate · right-click to cancel");
    }

    public void BeginBuild(StationType t)
    {
        CancelBuild();
        BuildType = t;
        Select(null, null);
        Hud.Toast("Click to place · R to rotate · Shift-click to place several · right-click to stop");
    }

    public void CancelBuild()
    {
        if (MovingStation is int mid && StationModels.TryGetValue(mid, out var mm)) mm.Visible = true;
        MovingStation = null;
        BuildType = null;
        Ghost = null;
        GhostModel?.QueueFree();
        GhostModel = null;
        _ghostKey = null;
        if (_ghostFloor != null) _ghostFloor.Visible = _ghostUse.Visible = false;
    }

    void UpdateGhost()
    {
        if (BuildType is not StationType t) return;
        var (w, h) = Defs.Dims(t, _buildRot);
        var hit = Hud.IsMouseOverUi() ? null : Rig.GroundUnder(Mouse);
        if (hit is not Vector3 p)
        {
            Ghost = null;
            if (GhostModel != null) GhostModel.Visible = false;
            _ghostFloor.Visible = _ghostUse.Visible = false;
            return;
        }
        int gx = (int)Mathf.Floor(p.X - (w - 1) / 2f), gy = (int)Mathf.Floor(p.Z - (h - 1) / 2f);
        var problem = MovingStation is int mid ? State.CanMove(mid, gx, gy, _buildRot) : State.CanPlace(t, gx, gy, _buildRot);
        Ghost = new Ghost(t, gx, gy, _buildRot, problem);

        if (_ghostKey != (t, _buildRot) || GhostModel == null)
        {
            GhostModel?.QueueFree();
            GhostModel = StationModel.Create(t);
            AddChild(GhostModel);
            _ghostKey = (t, _buildRot);
            _ghostOk = null;
        }
        GhostModel.Visible = true;
        GhostModel.PlaceAt(gx, gy, _buildRot);
        bool ok = Ghost.Problem == null;
        if (_ghostOk != ok)
        {
            Mesh3.OverrideAll(GhostModel, ok ? _ghostGood : _ghostBad);
            _ghostOk = ok;
        }
        _ghostFloor.Visible = _ghostUse.Visible = true;
        _ghostFloor.MaterialOverride = _ghostUse.MaterialOverride = ok ? _ghostGood : _ghostBad;
        _ghostFloor.Position = new Vector3(gx + w / 2f, 0.02f, gy + h / 2f);
        _ghostFloor.Scale = new Vector3(w, 1, h);
        var use = Station.UseTileFor(t, gx, gy, _buildRot);
        _ghostUse.Position = new Vector3(use.x + 0.5f, 0.02f, use.y + 0.5f);
    }

    public void Select(int? station, int? staff)
    {
        SelectedStation = station;
        SelectedStaff = staff;
        Hud?.ShowInspector(station, staff);
    }
}
