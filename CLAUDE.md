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
- Balance: Normal ≈ 50% autoplayer wins (40 seeds); Easy higher, Hard no more than 5/40. The autoplayer stands in for an average
  player: only teach it what the in-game tips tell a player; rebalance the game, not the bot. Prefer levers other than
  traffic volume (grant, deadlines, reputation loss, extra process steps).
- Seeded dice for new systems: `new Random(GameState.Mix(Seed, x, salt))`, never `HashCode.Combine` (randomised per process).
- Each situational tip needs a unique id (the self-test checks).
- No combined both-platforms zip in CI.

## Current work (2026-11-19)

Last release **v0.29.0** (idle tea breaks, patch updates); v0.28.0 had breaks, watches, collisions, the updater. Before that **v0.27.0**: cold handovers, the stranded-staff fix, intelligence freshness by service, security sweeps,
merged static building meshes (`Park3D.MergeStatic`; screenshots print draw calls). v0.26.0 had overhaul parts 1–5
and most of 6, Stores, and bad visits cutting the grant. Untagged since: furniture wood, glazed windows (`Park3D.Window`: frame, glazing bars, sill, thin tape) and a detailed army lorry (`Park3D.Lorry`).
Then (user requests, 2026-11-19):
- New chapters send their newly opened posts at once (`NewChapterIntake`, own dice), not with the next weekly intake.
- Three watches on the historical hours 08–16/16–24/00–08 (the user chose three watches over two). Optional weekly
  rota (`RotateWatches`), and nights wear harder after a week straight (`NightsRunning`, tip `nights`).
- Breaks matter: drain 1/380 per minute at a desk, so a break is due inside every watch. Energy only comes back at a
  rest station (`EnergyRecoverRest` × company bonus, halved by a rival), or slowly if the Park has none at all. Idle
  people sit down after `LoafAfter` (30) idle minutes at a table in their own building within `LoafRange` (10), and stay
  until work comes (`Loafing` rests, still `Available`); the tired turn out loafers and anyone already fresh. A wider
  range or a shorter wait cost the autoplayer badly (Normal fell to 8/40). Canteen 6 seats,
  common room 4. Tip `teaqueue` when seats run out (the autoplayer adds a table).
- Panels only list what's in play: teleprinter filters (`ActiveNets`), roles (`RoleOpen`), services (`ServiceInPlay`).
- Self-updater (`src/View/Updater.cs`): GitHub latest-release check, download, swap, restart; CI stamps
  `config/version` on tags. Only testable end to end from the second release that has it.
- Patch updates: `export.sh` also makes `<platform>-patch.zip` (StationX.pck + data_*/StationX.*, ~33 MB vs ~100 MB)
  and `<platform>-base.txt` (sha256sum of every other file). The updater takes the patch when `PatchManifest.BaseMatches`
  (Godot-free, linked into the tests); a Godot or .NET upgrade falls back to the full download. Keep
  `application/modify_resources=false` on Windows, or the .exe changes every release and no patch ever fits.
- The user wants **Hard ≤ 5/40** wins (Normal still ≈ 50%).
- No walking through each other: busy people standing still block (`_standing`, not in doorways or corridors); walkers
  detour (≤ `DetourTiles`) or wait `SqueezeAfter` minutes, and the view's `SeparatePeople` sidesteps figures.
- Specialists leave carrying to messengers and clerks (`SpecialistsCarryAfter`). A Park without a canteen is now
  crippling: tests that run a pipeline need a canteen table.
- Intel dilemmas carry a `Context` ("What we know"); the dialog shows lockdown stakes and suspicion after each option.
  Options were rebalanced so none is strictly worse; keep option 1 the middle (cover) choice, the autoplayer's default.

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
   tank (`StaticWaterTank`), doorways with wall over a lintel and open ledged doors (`Doorway`). Also done: the Mansion's porch
   and bays, photographed wood on furniture (`Textures.Wood`: the five furniture wood colours in `Mesh3.Mat` get the
   `wood_table_worn` photo, divided by its mean). Hut floors sit at ground level, so no hut steps. Still open: moss.

### Core-gameplay ideas to alternate with the overhaul

Done: bad Whitehall visits withhold a quarter of next week's grant; Stores (`Supplies.cs`, from act 3: one per
registration, order 60 for £45 with a 20 h lead, out = desks at half speed, standing order at +25%). Cold
handovers (`Station.LeftAt`, `TakeOver`: a desk left mid-message loses nothing within the hour, up to half the work
done after six). Fixed with it: people boxed in by new furniture are freed (`FreeStranded`), and the foreman tries the
next-nearest operator when the nearest can't reach the desk (this had starved whole desks). Intelligence freshness
by service (`Defs.StaleAfter`: air/naval 16 h, army 24 h, Abwehr/police/TUNNY 48 h; dead at twice that). In-tray
order by priority and deadline already existed (`MostUrgent`). Security (`Security.cs`, from act 2): papers left out on cold desks
overnight and far billets raise Abwehr/SD suspicion; a sweep (Intelligence panel) cuts it by 15 for an hour of
everyone's energy, every 4 days. All five listed ideas are now done; new ones are welcome.


Balance data (2026-11-19, with Stores and handovers): Easy 40, Normal 19, Hard 7 of 40 (after security). On Normal, games are lost in acts 7–9 (Claymore to Action This Day) when reputation hits 0;
the early acts rarely decide a game.
