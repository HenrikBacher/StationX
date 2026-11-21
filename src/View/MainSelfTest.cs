using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using StationX.Sim;

namespace StationX.View;

/// <summary>
/// `--selftest`: drives the game through the real input path (synthetic key presses, mouse moves and clicks)
/// and checks that each tool does what it should. Exits with code 1 on any failure.
/// </summary>
public partial class Main
{
    readonly List<string> _testFails = new();

    void Check(bool ok, string what)
    {
        GD.Print((ok ? "PASS  " : "FAIL  ") + what);
        if (ok) return;
        _testFails.Add(what);
        // What was in the way: the game's seed (to replay it), any sheet open, what the mouse is over.
        GD.Print($"      seed {State.Seed}, day {State.Day} {State.MinuteOfDay / 60:00}:{State.MinuteOfDay % 60:00}, sheet open {Hud.ModalOpen}, " +
                 $"panel {Hud.PanelId ?? "none"}, over {GetViewport().GuiGetHoveredControl()?.GetPath().ToString() ?? "the world"}");
    }

    async Task Frames(int n = 2)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>
    /// Waits until the condition holds or the time runs out, in real seconds. For anything animated: those run on time,
    /// not frames, and the frame rate varies wildly (a few frames a second in software rendering, hundreds without vsync).
    /// </summary>
    async Task Until(System.Func<bool> done, double seconds)
    {
        ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (!done() && Time.GetTicksMsec() < end) await Frames(1);
    }

    async Task For(double seconds) => await Until(() => false, seconds);

    static void SendKey(Godot.Key k, bool down)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = down });
    }

    async Task Tap(Godot.Key k)
    {
        SendKey(k, true);
        await Frames(1);
        SendKey(k, false);
        await Frames(2);
    }

    /// <summary>Viewport pixels to window pixels (the window may be stretched to a different size).</summary>
    Vector2 ToWindow(Vector2 p)
    {
        var win = (Vector2)DisplayServer.WindowGetSize();
        var vp = GetViewport().GetVisibleRect().Size;
        return new Vector2(p.X * win.X / vp.X, p.Y * win.Y / vp.Y);
    }

    async Task MouseTo(Vector2 p)
    {
        p = ToWindow(p);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = p, GlobalPosition = p });
        await Frames(2);
    }

    async Task Click(Vector2 p, MouseButton b = MouseButton.Left)
    {
        await MouseTo(p);
        p = ToWindow(p);
        Input.ParseInputEvent(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = b, Pressed = true });
        await Frames(1);
        Input.ParseInputEvent(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = b, Pressed = false });
        await Frames(2);
    }

    /// <summary>Screen point of a control's centre, accounting for the HUD scale.</summary>
    Vector2 CentreOf(Control c) => c.GetGlobalRect().GetCenter() * Hud.Scale;

    static T? Find<T>(Node root, Func<T, bool> pred) where T : Node
    {
        foreach (var n in root.GetChildren())
        {
            if (n is T t && pred(t) && (n is not CanvasItem ci || ci.IsVisibleInTree())) return t;
            if (Find(n, pred) is T found) return found;
        }
        return null;
    }

    Button? ButtonNamed(string text) => Find<Button>(Hud, b => b.Text == text && !b.Disabled);

    Vector2 TileScreen(int x, int y, float h = 0) => Rig.Cam.UnprojectPosition(new Vector3(x + 0.5f, h, y + 0.5f));

    async Task SelfTest()
    {
        try
        {
            await Frames(10);
            var dupTips = Hud.DuplicateTipIds();
            Check(dupTips.Length == 0, "tip ids are unique" + (dupTips.Length > 0 ? $" (duplicated: {string.Join(", ", dupTips)})" : ""));
            Settings.Hints = false;
            Hud.ShowBriefing();
            await Frames(2);

            // The briefing sheet is up: dismiss it with its own button.
            Check(Hud.ModalOpen, "briefing opens at the start");
            if (ButtonNamed("Understood") is { } ok) await Click(CentreOf(ok));
            Check(!Hud.ModalOpen, "clicking Understood closes the briefing");

            // Panels by keyboard
            foreach (var (key, id) in new[] { (Godot.Key.B, "build"), (Godot.Key.H, "staff"), (Godot.Key.Q, "requests"), (Godot.Key.T, "research"),
                                              (Godot.Key.G, "huts"), (Godot.Key.I, "intel"), (Godot.Key.Y, "ystations"), (Godot.Key.N, "intercepts"), (Godot.Key.O, "war"), (Godot.Key.K, "stats"), (Godot.Key.U, "workload") })
            {
                await Tap(key);
                Check(Hud.PanelId == id, $"key {key} opens the {id} panel");
            }
            await Tap(Godot.Key.Escape);
            Check(Hud.PanelId == null, "Esc closes the panel");

            // Accept a request by clicking it. Paused: the panel rebuilds every game hour, and a rebuild between
            // finding the button and clicking it made this check flaky.
            Paused = true;
            await Tap(Godot.Key.Q);
            await Frames(2);
            int active = State.Contracts.Count(c => c.State == ContractState.Active);
            if (ButtonNamed("Accept") is { } acc) await Click(CentreOf(acc));
            Check(State.Contracts.Count(c => c.State == ContractState.Active) == active + 1, "clicking Accept takes on a request");
            Paused = false;
            await Tap(Godot.Key.Escape);

            // Build: open the panel, click Place on the registration desk, click a free tile in the Mansion.
            await Tap(Godot.Key.B);
            var place = Find<Button>(Hud, b => b.HasMeta("station") && (string)b.GetMeta("station") == nameof(StationType.Registration) && !b.Disabled);
            Check(place != null, "build panel lists the registration desk");
            if (place != null) await Click(CentreOf(place));
            Check(BuildType == StationType.Registration, "Place starts the build tool");
            Rig.LookAtTile(11, 8);
            await Frames(3);
            (int x, int y)? spot = null;
            for (int y = 3; y < 13 && spot == null; y++)
                for (int x = 4; x < 18 && spot == null; x++)
                    if (State.CanPlace(StationType.Registration, x, y, 0) == null) spot = (x, y);
            Check(spot != null, "there is room for a desk in the Mansion");
            int count = State.Stations.Count, funds = State.Funds;
            if (spot is var (sx, sy))
            {
                await MouseTo(TileScreen(sx, sy));
                Check(Ghost is { Problem: null } gh && gh.X == sx && gh.Y == sy, "the ghost follows the mouse to the tile");
                await Click(TileScreen(sx, sy));
            }
            Check(State.Stations.Count == count + 1 && State.Funds < funds, "clicking places the desk and charges for it");
            var built = State.Stations.Last();

            // Select it by clicking, then move it with the inspector's Move button.
            await Frames(3);
            await Click(TileScreen(built.X, built.Y, 0.45f));
            Check(SelectedStation == built.Id, "clicking a desk selects it");
            await Frames(6);
            var mv = Find<Button>(Hud, b => b.Text.StartsWith("Move") && !b.Disabled);
            Check(mv != null, "the inspector offers Move");
            if (mv != null) await Click(CentreOf(mv));
            Check(MovingStation == built.Id, "Move starts the move tool");
            (int x, int y)? to = null;
            for (int y = built.Y + 2; y < 13 && to == null; y++)
                for (int x = 4; x < 18 && to == null; x++)
                    if (State.CanMove(built.Id, x, y, built.Rot) == null) to = (x, y);
            if (to is var (tx, ty))
            {
                await Click(TileScreen(tx, ty));
                Check(built.X == tx && built.Y == ty, "clicking a new tile moves the desk");
            }
            else Check(false, "there is room to move the desk");
            await Click(new Vector2(800, 450), MouseButton.Right);
            Check(SelectedStation == null && BuildType == null, "right-click clears the selection");

            // Select a person
            await Frames(10);
            var person = State.Staff.FirstOrDefault(s => !s.Away && PersonModels.ContainsKey(s.Id));
            if (person != null)
            {
                var at = Rig.Cam.UnprojectPosition(PersonModels[person.Id].GlobalPosition + new Vector3(0, 0.55f, 0));
                Paused = true;
                await Click(at);
                Check(SelectedStaff != null, $"clicking a person selects them (got staff {SelectedStaff}, station {SelectedStation})");
                Paused = false;
            }

            // Time controls
            await Tap(Godot.Key.Key2);
            Check(Speed == 3 && !Paused, "key 2 sets 3× speed");
            await Tap(Godot.Key.Space);
            Check(Paused, "Space pauses");
            await Tap(Godot.Key.Space);
            Check(!Paused, "Space resumes");

            // Camera: pan, zoom, rotate
            var before = Rig.Target;
            SendKey(Godot.Key.D, true);
            await Frames(2);
            await For(0.3);
            SendKey(Godot.Key.D, false);
            Check((Rig.Target - before).Length() > 0.3f, "holding D pans the camera");
            float size = Rig.Cam.Size;
            var mid = ToWindow(GetViewport().GetVisibleRect().Size / 2);
            Input.ParseInputEvent(new InputEventMouseButton { Position = mid, GlobalPosition = mid, ButtonIndex = MouseButton.WheelUp, Pressed = true });
            await Frames(2);
            Check(Rig.Cam.Size < size, "the mouse wheel zooms in");
            await Tap(Godot.Key.Period);
            Check(Rig.Quarter == 1, "full stop turns the view a quarter");
            await Until(() => !Rig.Turning, 5);
            Check(!Rig.Turning, "the turn finishes");
            await Tap(Godot.Key.Comma);
            await Until(() => !Rig.Turning, 5);
            Check(Rig.Quarter == 0, "comma turns it back");

            // Flow view
            await Tap(Godot.Key.F);
            Check(ShowFlows, "F shows the flow view");
            await Tap(Godot.Key.F);

            // Pause menu and saves
            for (int i = 0; i < 3 && !Hud.ModalOpen; i++) await Tap(Godot.Key.Escape);
            Check(Hud.ModalOpen, "Esc (after clearing any selection) opens the pause menu");
            if (ButtonNamed("Resume") is { } resume) await Click(CentreOf(resume));
            Check(!Hud.ModalOpen, "Resume closes it");
            await Tap(Godot.Key.F5);
            Check(Saves.Info("quick") != null, "F5 writes a quicksave");
            int savedFunds = State.Funds;
            State.Funds += 12345;
            await Tap(Godot.Key.F8);
            Check(State.Funds == savedFunds, "F8 loads the quicksave");

            // A dilemma gets answered through its sheet
            State.RaiseDilemma(State.DebugDilemma());
            Hud.ShowDilemma(State.Dilemmas[0]);
            await Frames(3);
            var option = Find<Button>(Hud, b => b.Text == State.Dilemmas[0].Options[0].Label);
            if (option != null) await Click(CentreOf(option));
            Check(State.Dilemmas.Count == 0 && !Hud.ModalOpen, "choosing an option resolves the dilemma");

            // The tutorial: its card at the first step, Next, and Skip (which ends the guidance and stays in the chapter)
            if (State.ActIndex <= 1)
            {
                Hud.ClosePanel();
                State.Tutorial = true;
                State.TutorialStep = 0;
                await Until(() => Find<Button>(Hud, b => b.Text == "Next" && b.IsVisibleInTree()) != null, 2);
                var next = Find<Button>(Hud, b => b.Text == "Next" && b.IsVisibleInTree());
                Check(next != null, "the tutorial card shows its first step");
                if (next != null) await Click(CentreOf(next));
                Check(State.TutorialStep >= 1, "Next moves the tutorial on");
                int act = State.ActIndex;
                var skip = Find<Button>(Hud, b => b.Text == "Skip" && b.IsVisibleInTree());
                if (skip != null) await Click(CentreOf(skip));
                Check(!State.Tutorial && State.ActIndex == act && !Hud.ModalOpen, "Skip ends the guidance and stays in the chapter");
            }
        }
        catch (Exception e)
        {
            Check(false, "exception: " + e);
        }
        GD.Print(_testFails.Count == 0 ? "SELFTEST OK" : $"SELFTEST FAILED ({_testFails.Count})");
        QuitGame(_testFails.Count == 0 ? 0 : 1);
    }
}
