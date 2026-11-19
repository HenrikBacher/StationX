# Station X: notes for Claude

A Bletchley Park management game (1939 → SHARK 1942, optional Tunny chapter): Godot 4.7.2 .NET, C# net10.0, real 3D under an
orthographic isometric camera with cutaway walls. Read `README.md` for the game's systems. This file is the working
state, so the work can be resumed on any machine.

## Build, test, look

- Sim (pure C#, no Godot): `src/Sim` (partial `GameState`). View: `src/View`. Tests: `tests/StationX.Sim.Tests` (xUnit).
- `dotnet build StationX.csproj` before running Godot after C# changes.
- Tests: `dotnet test tests/StationX.Sim.Tests`. Gate commits on `grep -q "Passed!  - Failed:     0,"`.
- UI self-test: `godot --path . -- --selftest` (~4 s on a GPU). It drives real input, renders the world cheaply, and uses
  its own `user://selftest-saves`.
- Balance: `BALANCE=1 BALANCE_SEEDS=40 dotnet test tests/StationX.Sim.Tests --filter Balance --logger "console;verbosity=detailed"`
  and read the `Easy/Normal/Hard: N/40 won` lines (~4 min).
- Screenshots: `godot --path . -- --shot=PATH --seed=7 --days=44 --hour=11 --weather=fine --zoom=2.6 --center=24,6`.
  Other flags: `--panel=NAME`, `--modal=NAME`, `--act=N`, `--place=Type@x,y;...`, `--lineup=x,y`, `--age=H`,
  `--inspect=Type`. Look at every visual change before committing.
- Software rendering like CI's: prefix with `VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json`.
- Exports: `./export.sh` (needs the 4.7.2 mono export templates) → `build/` Linux tar.gz + Windows zip.
- CI (`.github/workflows/release.yml`): jobs `test` ‖ `build` (self-test under xvfb, exports) → `release` on `v*` tags.

## Rules

- Never touch the player's real `user://saves` or `settings.cfg` (tests and shots must not write there).
- No Claude co-author / "Generated with Claude" lines in commits or PRs.
- Commit and push small steps to `main`. Tag (semver, annotated, message with "New"/"Fixes" sections summing up
  everything since the last tag) only after a significant batch; minor bump for features, patch for fixes only.
- Balance: Normal ≈ 50% autoplayer wins (40 seeds); Easy higher, Hard lower. The autoplayer stands in for an average
  player: only teach it what the in-game tips tell a player; rebalance the game, not the bot. Prefer levers other than
  traffic volume (grant, deadlines, reputation loss, extra process steps).
- Seeded dice for new systems: `new Random(GameState.Mix(Seed, x, salt))`, never `HashCode.Combine` (randomised per process).
- Each situational tip needs a unique id (the self-test checks).
- No combined both-platforms zip in CI.

## Current work (2026-11-19)

Last release **v0.26.0** (overhaul parts 1–5 and most of 6, Stores, bad visits cut the grant). Untagged since: cold
handovers, the stranded-staff fix, merged static building meshes (`Park3D.MergeStatic`; screenshots print draw calls).

### Graphics overhaul: period realism, CC0 assets allowed

The user chose realism (not a tabletop diorama or poster art) and allowed CC0 assets. Use Poly Haven 1K JPG maps
(albedo, `nor_gl` normal, roughness) in `assets/textures/<id>/`, and list each one in `CREDITS.md`. Keep sizes modest:
the target includes laptops with integrated GPUs, and each 1K set adds ~2 MB to the downloads.

Importing new textures: run `godot --headless --path . --import`, then in each `.jpg.import` set `compress/mode=2`,
`mipmaps/generate=true` and `detect_3d/compress_to=0`; for normal maps also `compress/normal_map=1`. Delete the matching
`.godot/imported/*` files and import again. Code-built materials can't trigger Godot's 3D detection, hence the manual
settings. Materials are mapped in `Textures.Pbr` (folder, tiles per repeat, tone) in `src/View/Textures.cs`.

1. Photographed materials: brick, weatherboard, floorboards, parquet, plaster, concrete, quarry tile. **Done.**
2. HDRI sky lighting and reflections: `SkyLight.cs`, the `lythwood_field` 1K HDR tinted to the ambient colour. Godot
   ignores `AmbientLightEnergy` once the ambient comes wholly from the sky, so the energy goes into the tint. Windows
   use `Mesh3.Glass`. **Done.** The HDR import needs `mipmaps/generate=true`.
3. Ground: `Ground.cs` shader lays grass, gravel and `brown_mud_02` photos (each divided by its linear mean) over the
   painted layout from `Park3D.PaintGround`, which also writes a mask (r gravel, g earth, b wet, a grass). Path edges
   are ragged (`PathAt`). **Done.**
4. Roofs: `Park3D.Roof` + `Mesh3.Roof` (hipped slate Mansion, felt gables with battens on huts from `asphalt_02`,
   flat concrete on brick blocks). Faded by camera size (`RoofsFrom`..`RoofsFull`), alpha-blended only while fading;
   while roofed all walls stand (no cutaway). Snow settles on them. **Done.**
5. Atmosphere and weathering: depth fog as aerial haze (`FogMode.Depth` from 115, the camera is 120 back; thicker in
   fog/snow/rain), grime decals up the foot of outside walls (`Park3D.Grime`), a bare drip line around buildings in
   `PaintGround`. **Done** (moss not done).
6. Model detail. Done: surveyed plots (`SurveyLines`: grass, pegs, string, ranging pole), the sectional steel water
   tank (`StaticWaterTank`), doorways with wall over a lintel and open ledged doors (`Doorway`). Still to do: Mansion
   bays and gables, hut steps, stove pipes, the lake (a flat grey disc from far off).

### Core-gameplay ideas to alternate with the overhaul

Done: bad Whitehall visits withhold a quarter of next week's grant; Stores (`Supplies.cs`, from act 3: one per
registration, order 60 for £45 with a 20 h lead, out = desks at half speed, standing order at +25%). Cold
handovers (`Station.LeftAt`, `TakeOver`: a desk left mid-message loses nothing within the hour, up to half the work
done after six). Fixed with it: people boxed in by new furniture are freed (`FreeStranded`), and the foreman tries the
next-nearest operator when the nearest can't reach the desk (this had starved whole desks).

- Hut 3 priorities: decrypts carry operational urgency; urgent ones lose value faster, so order matters.
- Security incidents: papers left out or a talkative billet raise suspicion; sweeps cost staff time.

Balance data (2026-11-19, with Stores and handovers): Easy 40, Normal 18, Hard 7 of 40. On Normal, games are lost in acts 7–9 (Claymore to Action This Day) when reputation hits 0;
the early acts rarely decide a game.
