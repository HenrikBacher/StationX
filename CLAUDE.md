# Station X: notes for Claude

A Bletchley Park management game (1939 → VE Day 1945: SHARK, Tunny, the Second Front): Godot 4.7.2 .NET, C# net10.0, real 3D under an
orthographic isometric camera with cutaway walls. Read `README.md` for the game's systems. This file is the working
state, so the work can be resumed on any machine.

## Build, test, look

- Sim (pure C#, no Godot): `src/Sim` (partial `GameState`). View: `src/View`. Tests: `tests/StationX.Sim.Tests` (xUnit).
- `dotnet build StationX.csproj` before running Godot after C# changes.
- Tests: `dotnet test tests/StationX.Sim.Tests`. Gate commits on `grep -q "Passed!  - Failed:     0,"`.
- UI self-test: `godot --path . -- --selftest` (~4 s on a GPU). It drives real input, renders the world cheaply, and uses
  its own `user://selftest-saves`.
- Balance: `BALANCE=1 BALANCE_SEEDS=40 dotnet test tests/StationX.Sim.Tests --filter Balance --logger "console;verbosity=detailed"`
  and read the `Easy/Normal/Hard: N/40 won` lines (~2 min: the games run in parallel, server GC; `BALANCE_DIFF=Normal,Hard`
  plays only those).
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

Last release **v0.46.0** (friendships and marriages with visible ties, building time and brick rebuilds, scenarios and medals); v0.45.0 had our own cipher read, Hut 3's checking, suspicion effects, no numbers for reputation or suspicion, fire-watching; v0.44.0 had more time on requests, poaching, pay and conditions; v0.43.0 had choosing the crib; v0.42.0 had Unpin on a person's own panel; v0.41.0 had the tutorial first chapter, the camera slide beside panels, folded tips and panel introductions; v0.40.0 had German countermeasures, back traffic, Special Liaison Units, Meanwhile, the interface pass; v0.39.0 had the Americans, the Wicked Uncles' early letter, Double Cross, a mole, flu waves, against history, custom games, the notables' stories; v0.38.0 had postings for everyone, RAF fitters, the JN-25 code book, Matapan, Ōshima, Hut 7, the Park at night, reputation forecast, war record, colour-blind networks, first-hour pass; v0.37.0 had JN-25 and the Japanese desk, the Recreational Club, the billets bus, umbrellas and torches, smoother frames; v0.36.0 had Chiefs reports, geese, smoke, the Graphics setting, commuters through the gate; v0.35.0 had the Telegraph crossword, the Prime Minister's visit, Let N go, bicycles; v0.34.0 had courses, Bombe-bay heat; v0.33.0 had Whitehall's quota, Workload panel, kisses; v0.32.0 put a request first, MOST IMMEDIATE telegrams; v0.31.0 had dispatch riders and the gate, music and ambience, failable operations; v0.30.0 had the
campaign to VE Day with great operations, Y-stations, the variety engine and update sheets;
v0.29.0 had idle tea breaks and patch updates; v0.28.0 had breaks, watches, collisions, the updater. Before that **v0.27.0**: cold handovers, the stranded-staff fix, intelligence freshness by service, security sweeps,
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
  `config/version` on tags. v0.33.0 is the first release it can install: every file in v0.32.0's Linux and Windows downloads matches v0.33.0's
  `*-base.txt`, so the patch applies on both. The offer is its own sheet: at launch the title waits up to 4 s
  for the check (`Hud.ShowStart`) and the offer comes first; a later result opens it at once over the title or the game.
  The download has its own sheet with a bar (`ShowUpdateProgress`; shots: `--modal=update|updating|updatefailed|title`).
- Patch updates: `export.sh` also makes `<platform>-patch.zip` (StationX.pck + data_*/StationX.*, ~33 MB vs ~100 MB)
  and `<platform>-base.txt` (sha256sum of every other file). The updater takes the patch when `PatchManifest.BaseMatches`
  (Godot-free, linked into the tests); a Godot or .NET upgrade falls back to the full download. Keep
  `application/modify_resources=false` on Windows, or the .exe changes every release and no patch ever fits.
- The user wants **Hard ≤ 5/40** wins (Normal still ≈ 50%).
- Y-stations (`YStations.cs`, panel `HudYStations.cs`, key Y): receiver sets per network (`SetsWanted` 2), riders vs teleprinter
  lines (`TransitMinutes`), direction-finding per service (`CoverSuspicionFactor`, cover options are `DilemmaOption.Cover`),
  frequency changes (`SearchUntil`, spare sets search faster). Pipeline tests call `TestUtil.QuickIntake`.
- The campaign runs to VE Day (19 acts). Barbarossa was inserted after U-110 (save version 2 shifts old act indices); never
  hard-code act numbers, use `Campaign.IndexOf(title)`. Great operations (`GreatOps.cs`): Barbarossa, D-Day (fatal), Market
  Garden, Ardennes. `Act.Op`, soft goals (`Goal.Soft`, count for readiness, don't hold the act), D-Day `PrepDays` after the
  act opens, a decision `DecisionDaysBefore` (`Dilemma.Tag = "op"`, `Action = "op+15"`). Expectations and traffic stop
  growing after `Defs.IntensityCap`. Balance runs go to day 320.
- No walking through each other: busy people standing still block (`_standing`, not in doorways or corridors); walkers
  detour (≤ `DetourTiles`) or wait `SqueezeAfter` minutes, and the view's `SeparatePeople` sidesteps figures.
- No teleprinter at the start: without a line, Y-station traffic comes by dispatch rider straight to a registration
  desk (`RiderDesk`, `RiderDrops` for the view); a line needs a teleprinter (`LineWorking`), else it falls back to
  riders. `Riders.cs` draws the motorcycle (gate → building door, `--rider` screenshot flag); the gate's barrier and
  sentry (`Park3D.GateParts`, `SetBoom`) live outside the merged static meshes. Tests that need a teleprinter call
  `TestUtil.Teleprinter`.
- Request priority: `Contract.Priority` (`SetPriority`, "Put first" on request cards) sorts ahead of everything in
  `MostUrgent` and the carry queue (`Prio`); accepting a flash request sets it. Flash offers open a telegram sheet
  (`Hud.ShowFlash`, once each, `--modal=flash`) and throb in Needs attention.
- Music (`Music.cs`): public-domain US military band recordings (Elgar, Holst; CREDITS.md) as loose .ogg in
  `assets/music` (`.gdignore`, so not packed; `export.sh` copies them to `music/` beside the game, part of the base,
  not the patch). Re-encode new tracks with GStreamer (`vorbisenc quality=0.2`); no ffmpeg here. Ambience is all
  synthesised in `Sound.cs` (birds, church clock, trains, aircraft, typing; `Main.OutdoorSounds`).
- Specialists leave carrying to messengers and clerks (`SpecialistsCarryAfter`). A Park without a canteen is now
  crippling: tests that run a pipeline need a canteen table.
- Variety engine (`Tale.cs`): templates with `{var}` and `{a|b|c}` (nesting), own seeded dice, `Pair` fills German and English
  alike. ULTRA decisions come from a dated deck (`UltraDeck.cs`, ~25 scenarios by service and TUNNY; `RecentScenarios` avoids repeats;
  `Tale.Stakes` scales rewards and risks). Intercepts add routine procedure (`Intercepts.Routine`). Tests in `Variety.cs` check
  every scenario fills cleanly and that no option is strictly worse than another.
- Intel dilemmas carry a `Context` ("What we know"); the dialog shows lockdown stakes and suspicion after each option.
  Options were rebalanced so none is strictly worse; keep option 1 the middle (cover) choice, the autoplayer's default.

### Since v0.32.0 (2026-11-20, parallel agents in worktrees)

- Workload panel (U, `HudWorkload.cs`, `Utilisation.cs`): `Station.Use`/`Staff.Use` are `UseLog`s of 24 hourly slots
  (slot = hour of day), `[JsonIgnore]` (not saved), sampled every 2 game minutes by `TrackUsage` at the end of `StepOnce`;
  read-only, never touches `Rng` (`TrackUse=false` turns it off; `UtilisationTests.TrackingLeavesTheGameUnchanged` compares
  whole saves). `DeskUse.Closed` ("after hours") only before watches. `g.Utilisation()` is cached per game minute: steps,
  roles, watches and `Findings` (bottleneck, unstaffed, down, idle). Floor rings (`Overlay.DrawUse`) only while the panel
  is open; inspector strips `UseStrip`. Tip `bottleneck`. Shots: `--panel=workload`, `--inspect=Clerk` (any role).
  Finding: the autoplayer's mid/late Park is mostly idle (Wrens ~90% idle); night watches go unstaffed.
- Kisses (`Kisses.cs`, from `Campaign.KissesFrom` = Operation Claymore): `Net.Werft` is a hand-cipher feed with no
  requests and not in `ActiveNets`. `ReadWerft` (the keys card's WERFT row) sends `Defs.WerftPerDay` (2) a day at 05–09
  on own dice; reading one kisses that day's naval keys (`KissedKeys` "Net:day"): crib ×`KissCribMult` 0.35, Bombe
  ×`KissBombeMult` 0.8, crib quality 0.99, an attempt under way is cut (`ApplyKiss`). Ranks as due at noon of its day
  (`KissDue`), filed unread once useless (`FileUnwantedWerft`). Tip `kisses`; the autoplayer follows it. Lever: WERFT
  difficulty 1.0 (~3 h at a hand-cipher desk); 0.5 put Normal at 27–29. Any new `Net` needs a Handbook path, intercept
  header and sentences, `StationFor`, serials and a Y-station (the tests check every network).
- HUD pass: tips `research`, `hutopen`, `watches`, `index`, `storesopen`, `operation`; the walkthrough's `tea` comes before
  `wait`. The HUD never shrinks below 0.8 of its 1600x900 design (`Hud.MinReadableScale`). Staff wording via `Hud.RoleWord`
  and `Hud.Doing`. Needs attention entries end with the action and open the panel that fixes it.
- Graphics: moss (overhaul item 6 done) is a code-drawn texture in the roofs' detail layer (`Textures.Moss`/`Mossy`,
  `Mesh3.Roof` UV2 by height and `Mesh3.Shade`), green grime on north/west walls, damp moss in `PaintGround`. Roofs merge
  per material (`MergeRoofs`; battens `_feltTrim`); the porch has its own non-fading slate. Ground wetness
  (`Park3D.SetWet`: puddles only after rain, dry over ~10 h); rain/snow particles dim at night (`WeatherFx.Sync(light)`);
  snow covers unbuilt plots. Shots: `--days` past ~110 on seed 7 reach VE Day; use ~100 for late shots.
- Workload round 2: `DeskUse.Coming` ("someone on the way") is apart from `Unstaffed`; finding `far` says post people
  nearer. `StepUse.Peak` = most desks worked at once. `RoleWatch` (report `Staffing`, `r.Watch(role, shift)`) gives
  on/needed per role and watch (`Short`/`Spare`); `SpareHand(p)` = idle ≥ 60% of ≥ 4 h on watch. `UseDays` (last 30 days,
  percent) is written at midnight and saved; the save-comparing tests clear it. `UseReport.Lasting` drives Needs attention
  (`stuck` ≥ 12 h, `nobody` ≥ 5 h of a watch, one `spare` line). `UseLook.Wait`/`Waiting` are shared by the flow and
  workload overlays; with both on only the workload rings show. Staff panel: per-person day bar, idlest first, watch
  table on/needed. Statistics: desks busy and staff idle charts.
- Sim performance (3.4× faster per day, allocation 8.7 → 0.9 GB per game, bit-identical). Perf:
  `PERF=1 [PERF_SEEDS=3,40 PERF_DIFF=Normal] dotnet test tests/StationX.Sim.Tests -c Release --filter Perf.TimePerDay
  --logger "console;verbosity=detailed"` (ms/day, time and MB per phase; allocations are the steady guide on a busy CPU).
  Bit-identity: `FINGERPRINT=/path/fp.txt dotnet test ... -c Release --filter Perf.CompareFingerprint` records 12 games'
  end state on the first run and asserts it after (record on main before a refactor). `INVARIANTS=1 [INVARIANTS_DAYS=320]
  ... --filter Invariants` checks message bookkeeping, energy/morale ranges, frozen walkers, starved desks. Hot-path rules:
  no lambdas capturing a loop variable in per-tick loops (move that code to its own method), `StationById`/`StaffById`
  (`IdIndex<T>`) not scans, no LINQ in anything that runs every tick.
- Fixed since: standing orders' `TotalDays` (from Quantity/PerDay), the act-1 briefing (accept first). Clean quit by every
  path (`Main.QuitGame`: free the world, collect and finalize, then quit; Godot aborted on "!rc_owner"). Draw calls:
  nothing indoors drawn under solid roofs, each figure one skinned mesh, furniture merged per building (`Furniture.cs`,
  `StationModel.DetachStatic`; moving uses the ghost).
- Whitehall's quota (from `Defs.QuotaFromAct` = 5): `IntelQuota` fresh decrypts a day (`QuotaShare` 0.5 + 0.04/act, max
  0.8, of the act's offers at full rate), `IntelRate` over the last two days (`DayRecord.Fresh`, `QuotaValue`: stale 3/4,
  dead 1/2); `ExpectationMult` = 2.25 × shortfall, clamped 0.6..2.25 (`QuotaFloor`; slope 2.5 put Normal at 17, 2 at 28).
  `MissedRep` scales by what's still owed (base 2.5). Visits count idle heads (`IdleShare`). `MaxOffers` 5. Workload
  finding `spare`; the autoplayer takes `SpareRequests` more on networks read today and hires no more of an idle role
  (`UseWorkload`; off in the save-comparing test). Tip `quota`. `ParkDiag` (PARKDIAG=1) reports a game act by act.
  Balance 2026-11-20: Easy 40, Normal 21, Hard 0 of 40 (main before it: 40/26/2).
- Top bar: `Hud._quota` under the reputation bar (rate of quota, today; keep it short, the bar overflowed at 1600 late
  on). Staff panel's money line adds `EarnedLastWeek`. Plot signs are signwritten on the board (two non-billboard labels).
- Summer heat: `StationType.Fan` (appended last; "Extractor fan" £80, after the Bombe research, `AllowedIn` = where
  Bombes go). `HotZoneList`/`HotZones` (May–Sep, ≥ `HotBombes` 3 running Bombes in a building, no fan): drain ×1.3 and a
  little morale, like cold. Alert, tip `heat`, zone inspector line, autoplayer builds one. Model `StationModel.Fan`, its
  hub turned in `Sync` in summer and kept out of the furniture batch.
- Courses (cross-training): `Staff.Second`, `Defs.Course(from, to)` (Clerk/Messenger/Wren from anyone 12 h £30;
  Linguist↔Cryptanalyst 72 h £80), `Train`/`CantTrain` (away via `SendOff(…, "course")`), worked at `SecondRoleSpeed`
  0.8 in `SpeedFor`, only when `!RoleNeeded(own role)` (`Scheduler.SecondHand`, after the own-role search). `CourseAdvice`
  (an idle role and an unstaffed one with a course) drives the idle finding's hint, tip `course` and the autoplayer
  (one a day). Inspector row "Learns". Balance with it: Easy 40, Normal 18, Hard 2.
- Also fixed: the Mansion's gables have cross roofs (`MansionGables`, in `_roofs` so they fade); telegraph wires
  (`TelegraphLine`, `Wire`: sagging segments, a terminal post by the Mansion); nothing indoors shows under solid roofs.
  Shots hold reputation ≥ 25 so `--days` always reaches the date (seed 7 now loses on Normal around day 60).

### In v0.35.0 and v0.36.0 (2026-11-20)

- Telegraph crossword (`Crossword.cs`, from `Defs.CrosswordFrom` 13 Jan 1942): `HoldCrossword` (£75, every 28 days) sends
  `CrosswordRecruits` (3) skilled cryptanalysts with `CrosswordFiend` after 3 days (`DailyCrossword`, own dice); they carry
  `Staff.Crossword` so the weekly refresh keeps them. Staff panel row, tip `crossword`, the autoplayer runs one when short
  of cryptanalysts. Balance: Easy 40, Normal 22, Hard 4.
- The Prime Minister's visit: a scripted inspection (`PmVisit`, `ScriptedDone` "pm1941") announced `PmVisitAfterDays` (2) into
  Barbarossa for 11:00 next day, before the op decision; `PrimeMinisterLeaves`: morale +`PmMorale` always, rep +5 / +1 / −3,
  no grant cut. Tip `pmvisit`, top bar "PM VISIT". `VisitedAt`/`VisitorHere` show a staff car (`Park3D.StaffCar`, in the
  gate node, `SetVisitor`) from 30 min before a visit to 2 h after; shots: `--visitor`. Balance: Easy 40, Normal 21, Hard 2.
- Staff panel "Let N go" per role: `Expendable(role)` (watches with `RoleWatch.Spare`; keeps `max(Needed,1)+1` counted live,
  not from the cached report; idlest `SpareHand`s, no heads or notables), `LetGo`. The autoplayer doesn't use it.
- Bicycle racks (`Bikes.cs`): beside each open building's door (the Mansion's clear of its porch), filled with up to 6
  bicycles by how many are in the building (the far-billeted come by bus); one merged mesh per fill level, two draws a rack.
- Geese on the lake (`Geese.cs`, real-time, still while paused, standing on ice). `Mesh3.MergePainted` merges a node of
  plain-colour parts into one vertex-coloured mesh (used by the bikes and geese). Statistics charts: scale, dates, hover.
- Reports to the Chiefs (`Reports.cs`): a sink for breakthroughs once `QuotaActive`: +`ReportRep` (2) per report, every
  `ReportEveryDays` (14), cost 60 + 50 per report sent. Research panel card, tip `chiefs`, the autoplayer sends one when
  `ResearchDone()`. Reputation is touchy here: +4 weekly from act 1 put Normal at 25–29; +2 fortnightly from the quota at
  24 (baseline 21). Balance: Normal 24, Hard 2.
- Chimney smoke (`Smoke.cs`, CPU particles, one draw each): on the Mansion's chimneys (`Park3D.Chimney`) and the huts'
  back-eaves stove pipes, parented to the same wall-side node so it hides with them. `Park3D.SetSmoke`: October–April, the
  Mansion always, a hut only with a stove in it; the wind bends it.
- Commuters (`Commuters.cs`, view only, the sim never sees it): when someone goes Away their own `PersonModel` walks or
  cycles (`Commuters.Cycles`: 60% by id, never the far-billeted) from where they were to the gate and down the road
  (`RoadX` -18), leaving up to `Linger` 20 game minutes late. Arrivals for each watch start are planned together
  `PlanAhead` (300 min) before it: due at their `HomeX/HomeY` door `Early` 3–30 min early, pass checks
  (`CheckMinutes` 3.5 at `CheckPoint`, by the sentry, who turns to them: `Park3D.SetSentryFacing`) scheduled
  back-to-back from the latest, so nobody queues. Walkers keep left of cyclists (lanes in `Where`). Trips run on game
  time; the sim reclaiming the person ends a trip. `PersonModel.SyncCommute` (poses `Ride`, `ShowPass`, a shared
  `Bikes.Single` mesh); racks count cyclists present plus `Commuters.Parked`; the barrier lifts for them (`AtBarrier`).
  Walk 0.42 and bike 1.05 tiles a game minute (sim walkers do 0.28: a sim-side commute would cost hours of watch).
- The billets bus (`Bus.cs`): the far-billeted ride it (`Commuters.BusWanted`: passengers about to step off, someone
  walking out to it, a far-billeted watch ending within `BusDrive` or anyone of them off watch but still on site). It
  stops with its door at `Commuters.StopPoint`, turns in the gateway to leave. Its passengers' pass checks are one block
  (`BusGap` 1.6 apart, `BusCheck` 1.2 each) that others' checks are scheduled round. Shots: `--farbillet=N`.
- Out of doors (`PersonModel.Outfit`, commuters and everyone crossing the Park): walkers carry an umbrella in rain or a
  storm (one shared merged mesh, held in the left hand); after dark (before 06:30, from 19:30) anyone moving shows a dim
  additive pool of a blacked-out torch or bicycle lamp a step ahead.
- Draw-call notes: Godot already instances identical mesh+material draws, so many copies of one mesh cost little; distinct
  boxes each cost a draw. `STX_COUNT=1` with `--shot` prints visible surfaces by owner (Stations by type, Buildings by
  kind). Bombe drums are two MultiMeshes per Bombe (`StationModel.TurnDrums`); the sentry, barrier pole and staff car
  body are merged with `Mesh3.MergePainted`.
- Performance review (2026-11-20, Intel Panther Lake iGPU, 1600x900, late Park at 6x): the main script ~1.4 ms a frame
  (people 0.5, stations 0.35, sim 0.3), the sim ~47 ms per game day; the frame is GPU- and fill-bound (half resolution
  halves it). `--bench=N` (with a shot's flags) runs N frames and prints ms per part, render CPU/GPU and draws;
  `--nofx=ssao,ssil,glow,fog,sky,shadow,msaa,half,adjust,orth,split2,4k,2k` switches effects off to price them;
  `--graphics=0|1|2`. Settings → Graphics (`Main.ApplyGraphics`): High (default) ~10 ms GPU, Balanced (no SSIL, FXAA,
  4k shadows) ~8, Low (also no SSAO/glow, 2k shadows, 0.75 scale FSR) ~5. The sun uses 2 cascades (4 looked the same).

### In v0.37.0 (2026-11-20)

- Smoother frames: the save serializer warms up on a worker thread 20 s after launch (`SaveGame.WarmUp`, first save 178 → 24 ms; at launch it made the self-test's timed input flaky), toasts go
  up one a frame (`Hud.ShowNextToast`), GC in `SustainedLowLatency`; `--bench` lists the five worst frames, slow event
  handlers and collections. The measuring tools live in `MainBench.cs`. v0.35.0 installs match v0.36.0's base lists
  (patch path checked).
- The Recreational Club (`Social.cs`, from act `SocialFromAct` 1): Staff panel "Arrange an evening" (£50, every 10 days)
  puts on the next of five kinds at 19:30 (`TickSocial`): morale +`SocialMorale` 0.08 for all, each quarrel mended with
  `SocialMend` 0.5. Tip `social` (average morale < baseline − 0.15); the autoplayer follows it. Balance: 40/24/2.
- `Rng.Seeded(int)`: a system's own dice go through splitmix64. Raw xorshift from a small `Mix` seed made a fresh die's
  first `Chance` nearly always true (crossword winners, new-chapter intake). Use it for any new per-system dice; the main
  game `Rng` is unchanged (existing seeds and tests keep their games).
- JN-25 (`Japan.cs`, from `Campaign.JapanFrom` = the Blackout): `Net.Jn25` (hand family, not naval), `Service.Japan`, Y-station
  "Kilindini cables" (no DF; `CanBuyDf` copes with services without one). Path: registration → hand-cipher (strips the
  additive) → `StationType.Japanese` (appended last; Linguist, mansion/hut3/hut8) → dispatch. Only staff with
  `Trait.Japanese` work that desk (`GameState.CanOperate`, used in `FindOperator`, `SecondHand`, `CanBeStaffed`; left out
  of `RoleNeeded`). The trait comes from the Bedford course (`LearnJapanese`, inspector Learns item `JapaneseItem`, 120 h
  £90) or a quarter of linguist candidates (`MaybeJapanese`, own dice). `NightTraffic` maps spawn times onto 22:00–08:00.
  Intercepts: romaji text, code groups divisible by 3 under a non-carrying additive (`Intercepts.Jn25`), its own routine.
  `JapanDeck` in UltraDeck, a Suez convoy on the war map. Tip `japanese`; the autoplayer builds one desk and sends
  linguists to Bedford until each watch has a reader. Every act from the Blackout has a JN-25 template (weight 2 in the
  Blackout, 1 after). Balance: Easy 40, Normal 19, Hard 1.
- Watches: measured in autoplayer games, shortages are spread evenly over the three watches (clerks, linguists at
  peaks); nights are not specially unstaffed any more.

### In v0.38.0 (2026-11-20)

- Postings for everyone (user request): `Hire(id, shift, zone)` posts each hire (`Postings.cs`: `SuggestPost`, `SuggestShift`,
  `PostsFor`, `PostingGaps`, `PostingDonor`, `PostsWanted`). Only `Roams(Role.Messenger)`; `Post(id, null)` fails for the
  rest, pinning to a desk elsewhere re-posts, heads are posted to their hut. `PostUnposted` posts the starting staff and old
  saves' staff. Candidate cards: building and watch dropdowns (★ suggested; `Hud._hireShift/_hireZone`). Tip `posting`;
  the autoplayer fills gaps by moving from where most are spare, else hires into the gap, and wants one per watch in each
  building with the role's desks. `BalanceWatches` counts on building by building. Pipeline tests that use the starting
  staff in huts call `TestUtil.Unpost` (BombeLine does). Hard rep loss 1.15 → 1.2. Balance: Easy 39, Normal 23, Hard 2.
  Shots: `--scroll=end` scrolls the open panel to its foot.
- RAF fitters (user request, `Fitters.cs`): `Role.Fitter` (appended), open with the Bombe research (`FittersOpen`; two
  candidates on own dice, `FitterCandidates`). `Defs.FitterServes`: Bombe, Bombe4, Checking, Typex (GPO keeps the rest).
  A breakdown with a fitter on watch posted to the building costs `FitterPartsShare` 0.25 and a `TaskKind.Repair` (appended)
  walks them over; on arrival downtime becomes `FitterRepairShare` 0.3 of the outside time. Idle fitters service idle
  machines with wear ≥ `FitterServiceAt` (free, half the time, `Station.QuietService`: no announcement). `Station.FitterId`
  holds the job. Not idle in `IdleShare` or the idle finding, not in `PostingGaps`; `PostsWanted` counts buildings with ≥ 3
  of their machines. Tip `fitters` after the first breakdown; the autoplayer hires one per watch. Balance: 39/24/1.
- JN-25 code book (`Japan.cs`): `BookRecovered` (from `BookStart` 0.3, +`BookPerDecrypt` 0.02 of what's left per JN-25
  delivered) scales the Japanese desk 1.5× → 0.6× (`BookFactor`); `DailyBook` brings in `Defs.JapaneseBooks` (JN-25C
  1 Aug 1942, JN-25D 1 Apr 1943), keeping `BookKept` 0.25. Desk inspector line. Balance: 39/22/0.
- Matapan (`Matapan.cs`): a scripted dilemma (`Tag` "matapan", `ScriptedDone`) the first midnight from `Defs.MatapanFrom`
  (25 Mar 1941) to the end of May with no other dilemma open; option 1 is the Sunderland cover. Header "REGIA MARINA". Balance 39/24/4.
  Shots: `--modal=matapan`.
- Ōshima (`Oshima.cs`): `OfferOshima` (offered, not tagged, `Contract.Oshima`, War Office, JN-25 path) in the Colossus and
  D-Day `OnStart`s; each delivered before D-Day is `OshimaRead`, and `OshimaBonus` (`OshimaPer` 0.015, max 0.09) joins
  `Readiness` for overlord (readiness tooltip). Balance 39/24/4.
- Reputation forecast: `RepTrend()` (RepLog over `RepTrendDays` 3) and `RepRunsOutIn()` (falling more than `RepTrendFloor`
  0.3 a day and out within `RepForecastDays` 21). The line under the reputation bar ends "gone in ~N days" in red (alone
  before the quota), with the rate in the tooltip. View only.
- End sheet: `Hud.WarRecord()` (two columns: dates and days, decrypts and late, keys, requests, decisions, most-read
  networks; great operations, staff now and at peak, breakdowns and fitters' repairs, JN-25 and Ōshima, tonnage) and a
  postscript at VE Day. `Defs.RoleWord`/`RolePlural` lower-case role names but keep "Wren" and "RAF".
- The Park at night (`NightLights.cs`): building glass is one shader (`NightLights.Glass`; vehicles keep `Mesh3.CarGlass`)
  that reads an R8 map with a texel per tile (zone rect + 1), redrawn only when a building's occupancy step (`Level`)
  changes; warm emission at the pane edges × night. `MergeStatic` accepts that material, so no extra draws.
  `Park3D.SetNight`: ceiling lamps only in occupied buildings, hooded door lamps (`DoorLamp`, out in a raid), and desk
  lamps (`StationModel.DeskLamps` now (light, owner)) only where someone is at the desk.
- Hut 7 (`hut7`, x2..11 y15..19, £700, Available from the Blackout's `OnStart` and on load): the Japanese section's desks.
  To fit it the lake moved south: `Grid.LakeY` is saved (old saves 23, new games `NewLakeY` 24.6); `Water.CentreZ` and
  the view's lake paint follow it (`Water.Place` in `Park3D.Rebuild`). Old saves' lake keeps Hut 7 out (`AddMissingZones`).
  The autoplayer posts a Japanese reader to the desk's building on each watch (the `japanese` tip says so). Balance 40/23/1.
- Colour-blind check: `Gfx.NetColor` re-picked so every pair is ≥ ΔE 8 under Machado protan/deutan/tritan (was 1.5:
  WERFT/JN-25 deutan, RED/BROWN). `NetMark` draws a shape per network (circle RED, square LIGHT BLUE, triangles CHAFFINCH/
  DOLPHIN, diamond BROWN, star SHARK, plus POLICE, X ABWEHR, ring JN-25, bar WERFT, hexagon TUNNY) on the keys card and
  request cards, whose header now names the network.
- First-hour pass: the Build panel lists the chapter's unbuilt "Build a …" objectives first, marked; candidate cards show
  no posting row until there's a second building for the role or the watches begin; tip wording (accept as soon as one
  comes; hire into the desk's building) and the Build intro (riders before the teleprinter).

### In v0.39.0 (2026-11-20; user: "do all of them": Americans, mid-game help, Double Cross, mole, flu, record vs history, custom games, notables)

- The Americans (`Americans.cs`): BRUSA dilemma (`Tag` "brusa", `ScriptedDone`) `BrusaAfterDays` (3) into the Testery (or from
  `Defs.BrusaFrom` 17 May 1943); `Action` "brusa1"/"brusa2" (`CrisisAction` → `AgreeBrusa`). `Brusa` 1/2 (−1 kept), `DaytonBombes`
  (`DaytonStart` 2 + 1 per `DaytonGrowDays` 30, max 6, needs the four-rotor research) take four-rotor menus through the outstation link
  (`OutstationTakes`, `ToDayton` after our outstations, `RemoteJob.Dayton`, + `DaytonCableMinutes` 240); `KeyRoute` counts them.
  The 6813th (`SendUsArmy`, own dice salt 0x05A1): 2 at once then 1 per `UsArmyEveryDays` 21, up to 6, `Staff.American`, salary 0,
  kept by `RefreshCandidates`; first three are `Defs.UsArmyNotables`. Olive drab and a garrison cap (no scarf). Tip `dayton`,
  `--modal=brusa`, `--usarmy` (with `--lineup`). Balance 40/22/1.
- Mid-game help (`Uncles.cs`): below `UnclesAt` (25) reputation from `Campaign.KissesFrom` (Claymore) to before `ActionThisDay`, once
  (`ScriptedDone` "uncles", checked in the 2-minute schedule block): option 1 (`Action` "uncles" → `LetterSent`: `UnclesWrote`, 3 Wren
  candidates on own dice, Wren intake at the later rate) +10 rep +£2000; option 0 proper channels +4 +£500. Action This Day's `OnStart`
  then gives `UnclesLaterFunds`/`UnclesLaterRep`. Dilemma header by tag; the suspicion line only when an option moves suspicion.
  `--modal=uncles`. Balance 40/23/1; Normal losses now spread over acts 4–14 (only one in 7–9).
- Double Cross (`DoubleCross.cs`): `Agents` (`XxAgent`: Trust, Blown, Dropped) from the Colossus `OnStart` (`StartDoubleCross`; Abwehr
  added to Colossus's pool). Daily `XxDrift` 0.04 (own dice 0x0DC5), below `XxBlownBelow` 0.15 a `XxBlownChance` 0.25 of blown
  (−3 rep); each Abwehr delivery (`ReadAbwehr`) +`XxPerDecrypt` 0.12 to `LeastSure`; runs until D-Day. `FortitudeBonus` = Σ trust ×
  `XxReadiness` 0.016 in `Readiness`. ARTIST (`Tag` "artist", `XxArtistDay` 2 into D-Day; `Action` "artist-drop"/"artist-keep").
  Intelligence card, tip `doublecross`, readiness tooltip, war record line, `--modal=artist`. Balance 40/24/2.
- The mole (`Mole.cs`): `DailyMole` from `SharkFrom` to before D-Day picks a linguist (own dice 0x30AE; not notable, head, American
  or `MoleCleared`) once there are 3; while `MoleState` 1, Luftwaffe +`MoleLeakAir` 1.2 and Heer +0.8 a day; after `MoleNoticeDays` 6
  a dilemma (`Tag` "mole", own dice 0x30AF) names the mole and two others (same building if possible) with clue lines (`MoleClues`
  vs `HerringClues`). Options: dismiss each (`Action` "mole:id"), and at index 1 move all three (`"mole-move:ids"`, re-posted,
  state 3). Wrong dismissal: −morale, −3 rep, `MoleCleared`, asked again; too few left and MI5 finds them. `--modal=mole`.
  Balance 40/24/2.
- Flu waves (`Flu.cs`): illness already existed (`SpreadIllness`). `DailyFlu` (own dice 0xF1A0, `FluChance` 0.03 a winter day from
  act 1, once per `FluWinter`) raises a dilemma (`Tag` "flu") `FluWarningDays` 3 ahead; the wave lasts `FluDays` 14 and multiplies
  `SpreadIllness`'s chance by `FluMult` (`FluWaveMult` 3, × `FluPrecautionShare` 0.5 with precautions, `Action` "flu", £150). Top bar
  "flu coming"/"FLU". `--modal=flu`. Balance 39/22/1.
- Against history: `Stats.Firsts` (game day of the first "Enigma", each `Net` name, "Colossus"; set in `KeyBreak` and the Colossus step)
  beside `Defs.HistoryFirsts` (key, text, date, exact) in `Hud.AgainstHistory()` on the end sheet. `Records.Wars()`/`AddWar` keep
  every finished war in `user://records.cfg` section "wars" (key seed-difficulty, so a game ending again updates its line;
  never written when `Settings.Persist` is off), and the end sheet ranks this one.
- Custom games (`Custom.cs`): `CustomRules` (Funds, Grant, Patience, Deadlines, Suspicion, Wear) → `g.Rules` (the `DifficultyDef`
  with them laid over, cached; Difficulty/Custom setters reset it). Every sim use of `Defs.Difficulties[Difficulty]` is now `Rules`.
  `NewGame(…, custom)` keeps it unless plain at the start (a later start always counts as custom). New-game sheet: Custom rules
  checkbox, a row of three per rule, "Start in" for the campaign too. No best score or war record for custom games.
  `--modal=custom`.
- Notables' stories (`Notables.cs`, `DailyNotables` in `DailyPeople`, once each via `ScriptedDone`, only with them on the staff
  and not off sick; `Away` is also true off watch, so never test that): `welchman` (from Their Finest Hour; option 1 `welchman:zone`
  → `Reorganised`/`DisruptedUntil`, `ZoneFactor` in `Duration`: ×1.3 for 2 days then ×0.9; option 0 `story-later:` → `StoryAgain`
  +14 days), `knox` (from Claymore; `knox-home`: `KnoxAtHome`, sent off, +`KnoxPoints` 5 a week; `knox-stay`: +0.3 strain; dies
  from `Defs.KnoxDied` or after the Testery: fired, morale), `turing-us` (from U-559; away 30 days, `TuringWent` → Dayton +1), the
  bicycle toast. The autoplayer takes option 0 for staff dilemmas. `--modal=welchman|knox|turing-us`. Balance 39/23/3.

### In v0.40.0 (2026-11-21; user: "proceed with the gameplay ideas": countermeasures, back traffic, SLUs, Meanwhile)

- Countermeasures (`Countermeasures.cs`): `CounterDefs` (id, act, days in, which nets, which steps, ×, answer research) come in by
  `DailyCounters` (ukwd: D-Day +1, Luftwaffe Enigma Bombe ×1.8; sz42: Market Garden +1, Lorenz Testery/Heath Robinson/Colossus
  ×1.8; uhr: Ardennes +1, Luftwaffe crib ×1.7), add their answer to `ResearchAvailable` (`ResearchId` Duenna 150, ColossusII 200,
  UhrBox 150, appended) and slow `CounterMult` in `BombeFactor`/`Duration` until researched. Keys card line per network
  (`CountersOn`), tip `counter-<id>`, `--modal=counters`. The autoplayer buys the answers at once (it has the points), so balance
  is unchanged: 39/23/3.
- Back traffic (`BackTraffic.cs`, from the Bombe research): `DailyBackTraffic` adds `BackPerMissedKey` (2) per covered machine-cipher
  net whose key wasn't broken yesterday, up to `BackMax` 40. `TickBackTraffic` every `BackStep` 10 minutes by the clock (no unsaved
  timer): idle (no task, or loafing) on-duty cryptanalysts and Wrens with free Bombes read at min(crypt/`BackCryptMinutes` 180,
  min(wrens, free Bombes)/`BackBombeMinutes` 240) a minute; +`BackPoints` 1 each, `Stats.BackRead`. Nobody's task changes, so the
  Workload panel still counts them idle; the Staff list says "on back traffic" (`BackWorking`, view only). Keys card line,
  `--modal=backtraffic`. Balance: 90/120 minutes put Normal at 26, with +1 rep per 8 messages 36; now 39/24/0.
- Special Liaison Units (`Slus.cs`): `SluDefs` (cairo: Claymore until Colossus, Heer+Luftwaffe £300 +£30/wk; malta: U-110 until
  Colossus, Kriegsmarine £250 +£24; normandy: D-Day on, £400 +£40). `SendSlu`/`CantSendSlu`, `Slus` (saved), `DailySlus` brings
  them home. `SluServes(service)` → `GameState.StaleAfter`/`DeadAfter` (instance, ×`SluFresh` 1.2; use these, not `Defs.`, in the
  sim) and `SuspicionPerDecrypt` ×`SluSuspicion` 1.2; upkeep in `WeeklyUpkeep`. Intelligence panel card, tip `slu` (funds ≥ cost +
  `SluTipReserve` 400), the autoplayer follows it. `--modal=slu`. Very sensitive: 1.5/1.15 put Normal at 28, 1.3/1.2 at 59 of 80;
  1.2/1.2 is neutral (50 of 80 with or without). 80-seed Normal baseline is 50 (62%).
- Meanwhile (`Meanwhile.cs`): on the player's own step to the next act (not `DebugSkipToAct` or a later start), a gap of ≥
  `MeanwhileMinDays` 42 raises a dilemma (`Tag` "meanwhile", header "MEANWHILE · THE NEWS") with up to `MeanwhileHeadlines` 4 of
  `Headlines` spread over the gap; per month (max `MeanwhileMaxMonths` 6): option 0 £`MeanwhileFunds` 100, option 1 (the default)
  `MeanwhilePoints` 5 breakthroughs, option 2 `meanwhile-quiet:N` (int) suspicion −`MeanwhileQuiet` 4 everywhere. `--modal=meanwhile`.
  Balance 38/25/1; 80 Normal seeds 44 (55%).
- Interface pass (user: "review the interface and improve it"): `UiTheme.Money` (−£1,552, never £-1,552; use it for any
  amount that can go negative); the side panel's content has a 12 px gutter clear of its scrollbar; `Why(reason)` beside a
  greyed-out button says why (Chiefs report; the sweep line appends it); an overdraft line heads the Build panel; request cards
  carry "Put first" on their due line; the keys card puts "N waiting" beside the network's name; while the inspector is open,
  Needs attention (and the keys card if it would be covered) steps aside (`ApplyRightColumn`, every frame); the Intercepts list
  names the next desk ("to typex machine") instead of "cipher".
- Tutorial (`HudTutorial.cs`, user request): `GameState.Tutorial`/`TutorialStep` (saved); `TutorialOn` while campaign act ≤ 1.
  `Steps` are (id, text, target, done): a step whose `Done` is already true moves on by itself, null `Done` gets a Next button.
  Targets (`FindTarget`): `tool:ID`, `place:StationType` (Build card's Place, meta "station"), `hire:Role`/`accept` (meta "tut"),
  `speed:N`, `flow`, `objectives`, `rep`; a gold ring (`_tutRing`) follows it every frame, scrolling the side panel to it once.
  Tips (`NextHint`) wait while it runs. Skip only ends the guidance (user: no jumping ahead) and sets `Settings.TutorialDone`
  (also set on finishing), which makes new games default to "Without" (New game → First chapter). Off for shots and the
  self-test unless `--tutorial`; `--tutstep=N` shows a step. The self-test checks card, Next and Skip.
- Folding text: a tip shows its first sentence (`FirstSentence`, ≤ 140 chars) with More/Less beside Got it; the tutorial card
  stays whole. Panel introductions go through `Intro(id, text)`: in full the first time (`Settings.IntrosRead`, saved as
  `intros_read`), then folded with More, unfolded until the panel next opens (`_introOpen`). `--folded` marks all read (`IntroIds`).
- An open side panel slides the picture right by half its width (`CameraRig.ShiftPx` → `Cam.HOffset`; shots settle it).

### In v0.43.0 (2026-11-21)

- Choosing the crib (`Cribs.cs`, user picked it from the ideas list): `CribKind` Desk/Weather/Routine/Reencoded, `CribPlan`
  ("Net:day", saved), `FalseStops` (noted in `BombeStop` and the checking machine's first rejection). `CribChoiceOpen`: Enigma,
  Bombe researched, not broken/kissed/gardened, and `Stale` or a false stop today. Weather (naval/Luftwaffe) and Reencoded
  (needs another Enigma key broken today, `OtherKeyBroken`; falls back to Desk if that lapses) skip the stale crib and Bombe
  penalties; times `CribWeatherTime` 1.2 / `CribRoutineTime` 0.6 / `CribReencodedTime` 1.4, quality +0.04 / −0.15 / 0.96,
  re-encoded Bombe ×0.85. Keys card button cycles (`NextCrib`), tip `crib`; the autoplayer follows the tip (re-encodement,
  else weather when stale). Distinct from crib dragging (`CribPuzzle.cs`, where the crib fits). `--modal=cribs`.
  Balance: Easy 37, Normal 24, Hard 1 (40 seeds), but 80 Normal seeds showed it worth ~10 wins (41 → 51) through the
  weather crib skipping the stale penalty; since v0.43.0 the weather crib only softens it (`CribWeatherStale` 1.3 in place of
  `StaleCribMult` 1.6, Bombe stale kept, `CribWeatherTime` 1.1) and the re-encodement is 1.6×/0.93/Bombe ×1.

### In v0.44.0 (2026-11-21)

- More time (`Extensions.cs`, from the ideas list): `CantExtend`/`Extend` on an active request in its last
  `ExtendWithinHours` 24 (not flash, standing or tagged; once, `Contract.Extended`; per ministry every `ExtendEveryDays` 10,
  `LastExtension`): due + half its days (0.5–1 day, `ExtendBy`), reward −25%, rep −`ExtendRep` 2, completion rep ×
  `ExtendedRepShare` 0.5, and its war-map operation is lost at once (`ResolveOperation(c, false)`). `Behind(c)` drives the
  card's "Heading for a miss", tip `extend` and the autoplayer. `Stats.Extensions`. `--modal=extend`. First cut (1 rep,
  0.5–2 days, full completion rep) put Normal at 33/40. Balance (80 Normal seeds): with it 42, Easy 39, Hard 1 of 40.
- Poaching (`Poaching.cs`, from the ideas list; careers already existed as `Experience`/`ExperienceFor`): `DailyPoaching` from
  `Campaign.SharkFrom` (where call-up papers stop), own dice 0x90AC, `PoachChance` 0.06 a day, ≥ `PoachEveryDays` 12 apart, no
  other dilemma. `PoachTarget`: three-star cryptanalyst or linguist, not notable/head/American/leaving/away. Dilemma `Tag`
  "poach": let go (`DismissStaff`, +3 rep), lend (default, `poach-lend`: +1 rep, `SendOff` 4 days "secondment" shown "on loan",
  +1500 experience), refuse (`poach-keep`: −2 rep, their morale +0.1). `--modal=poach`. Balance: Normal 40/80, Easy 39, Hard 0.
- Pay and conditions (`Grievances.cs`, from the ideas list): `DailyGrievances` from "Their Finest Hour", own dice 0x6A1E,
  `GrievanceChance` 0.05 a day, ≥ 16 days apart, no other dilemma. Kinds (group, minimum): wrens, nights (night watch),
  billets (`FarBillet`), clerks, linguists; Americans and unpaid staff left out. `GrievanceGroup` ("kind:ids", saved) carries
  the group to the action. Options: raise (`grievance-raise`: salary +10%, min £2; morale +0.15), allowance (default,
  £12 each, +0.07), refuse (−0.12), all for the group only. `--modal=grievance`. Balance: Normal 40/80, Easy 39, Hard 1.

### In v0.45.0 (2026-11-21; user: "implement all of those": own cipher, Hut 3 accuracy, fire-watching; friendships, construction, scenarios still to do)

- Our own cipher (`OwnCipher.cs`): `CipherRead` from `Defs.BdienstFrom` 1 Feb 1942 to `BdienstUntil` 10 Jun 1943 unless
  `CipherChanged`. Daily tons (own dice 0xBD1E), won convoys save ×`BdienstConvoyShare` 0.7 (`Tally`), and once
  `CipherSuspected` (evidence ≥ 0.15) −0.25 rep a day. Evidence +0.02 per naval decrypt; `StudyCipher` sends the best free
  cryptanalyst off 3 days ("study") for +0.3; `ChangeCipher` at 1: +4 rep. Intelligence card, tip `owncipher`, autoplayer
  follows. `--modal=owncipher` (needs a 1942 date: seed 7 `--days=88`). Balance: Normal 43/80.
- Hut 3's checking (`Hut3.cs`): `Checking` Quick/Standard/Careful (Intelligence panel row): translation time ×0.75/1/1.25,
  mistakes 6%/1.5%/0.3% × (1.3 − 0.15 × skill) on own dice per message (0x4A73) in `CheckTranslation` after the
  Translation step: −`MistakeRep` 0.4 each, `Stats.Mistranslations`. Tip `checking` (≥ 3 mistakes); the autoplayer stays on
  Standard. Balance: Normal 39/80.
- Suspicion effects (user request, `SuspicionEffects.cs`): `WaryMult(sv)` = 1 + `WaryMax` 0.5 × (s − `WaryFrom` 40)/60 on the
  key-breaking steps (`KeyBreakingStep`: crib, hand cipher, Testery, Heath Robinson, Colossus, Zygalski, Banburismus, in
  `Duration`) and in `BombeFactor`; from `WorryFrom` 75 −`WorryRep` 0.3 rep a day per service (`DailySuspicionWorry`).
  `SuspicionMeans(s)` words it for the panel rows, the dilemma dialog and the War map's decision cards (`OpContext`, `OpGain`).
  Tip `wary` (≥ 50); the autoplayer goes cautious above 50 (was 60). Without that, Normal fell to 32/80; with it 40/80,
  Easy 39, Hard 0. Shots: `--modal=wary` (War map with two decisions waiting), `--modal=waryintel`.
- No numbers for reputation or suspicion anywhere in the interface (user request): `Words` (Sim/Words.cs) gives
  `SuspicionLevel` (unsuspecting/untroubled/wary/suspicious/alarmed/locked out), `RepStanding` (on the brink … held in high
  esteem), `RepChange`/`RepAmount`/`SuspicionChange` (sizes of change), `RepRate` (slowly … fast), `Relative` (multipliers).
  `DilemmaOption.Effects` uses them; the rep bar tooltip and weekly breakdown, request cards, telegrams, War map, Intelligence
  panel, end sheet, and the two statistics charts (`LineChart.Say`: words on hover, no scale) all do. Keep new texts to words.
- Fire-watching (`FireWatch.cs`, view `Fires.cs`): raids now in `InRaidSeason` (Blitz + Steinbock 21 Jan–29 May 1944). At a
  raid's start `IncendiaryRaid` (own dice 0xF14E per building: timber 0.07, brick/Mansion 0.015) and `TurnOutFireWatchers`
  (`FireRota` None/Light/Full = 0/1/2 per building, from those off watch, least recently watched first: morale −0.03,
  strain +0.04, `Staff.FireWatchedAt`). Caught (0.8/0.97): the building's stations down `FireCaughtMinutes` 60. Not caught:
  down 16 h (brick 8), £150, a machine +0.4 wear and £, morale −0.1 inside, `BurningUntil`/`ScorchedUntil` for the view.
  Staff panel row, top bar "FIRE", alert, tip `firewatch`, autoplayer Light in raid seasons. An energy penalty on the next
  watch made the autoplayer *win more* (51/80, a hiring artefact), so lost sleep is strain and morale. Balance: Normal 45/80,
  Easy 39, Hard 2. Shots: `--modal=fire` (night, e.g. `--days=40 --hour=21 --zoom=1.3 --center=17,17`).

### In v0.46.0 (2026-11-21; user: "add these": friendships, construction, scenarios; then tag)

- Friendships and marriages (`Friendships.cs`, `DailyFriendships` in `DailyPeople`, own dice 0xF71E): `Staff.Friend` (one each)
  forms between two people `Together` (same `Shift`, same `Posted ?? LastZone`), `FriendChance` 0.12 × pool/20 a day;
  together +`FriendMorale` 0.01 a day. Man and woman, not notables, friends ≥ 21 days: `CourtChance` 0.03 → `Courting`;
  courting ≥ 30 days: `WedChance` 0.04 → dilemma `Tag` "wedding" (`StaffId` set, so the autoplayer takes option 0, the
  middle: 2 days off + £30 toast; 1: a week; 2: no leave, couple morale −0.1); `wedding:a,b:days` → `Spouse`, `SendOff(…,
  "wedding")`. Married on different watches −`MarriedApartMorale` 0.03 a day (`CouplesApart`, tip `couple`, autoplayer
  moves one). `DilemmaOption.Note` words effects the fields can't. Visible (user request): `Ties(s)` (text, sign, other)
  drives the person panel lines (+/−), a dot in the Staff list (green helping, red hurting), the Staff panel's "Friends
  and feuds" line (`TieCounts`), and green/red rings under the selected person's friend and rival (`Main.TieRing`). Shots:
  `--modal=wedding`, `--inspect=Role --ties`, `--scroll=N` (pixels). Balance: Normal 43/80.
- Construction (`Construction.cs`, view `Park3DBuild.cs`): `UnlockZone` pays and starts the builders (`Zone.BuildUntil`,
  `BuildDays`, `Hurried`, `Rebuilding`); `TickConstruction` (each step) opens it. `HutBuildDays` 2, `BlockBuildDays` 4;
  `HurryBuilders` (`HurryShare` 0.4 of cost, half the time left, once). `RebuildInBrick` from `BrickFrom` 1 Mar 1942 (cost
  + `BrickExtra` 400, `RebuildDays` 6, desks ×`RebuildSlow` 1.12 via `ZoneFactor`), then `Zone.Brick`: `GameState.Heated(z)`
  (no cold) and the brick fire chance. Huts panel `BuildingRow`, zone panel, objective "(builders: N h)", Park plot frame or
  courses by `BuildStep` (5 steps in the rebuild signature), scaffolding (`Scaffold`). Tip `brick` (funds ≥ 3000); the
  autoplayer rebuilds the busiest timber hut and waits for a hut being built before placing its desks elsewhere.
  Balance: even a one-tick delay in opening raised the bot's Normal wins from 85 to 108 of 160 (it fills other buildings in
  that turn, and its results are very sensitive to when it orders and fills a hut), so Normal's `RepLossMult` is now 1.1
  (was 1.0): Normal 36–40/80, Easy 40, Hard 2–3. Shots: `--modal=building` (Hut 11 half built), `--modal=rebuild`
  (needs 1942: `--days=88`).
- Scenarios and medals (`Scenarios.cs`): `ScenarioDef` (id, chapter title, days, measure, `Counts(net)`, bronze/silver/gold).
  `GameState.NewScenario` builds the Park by autoplaying the campaign on Easy to the chapter (another seed if it loses; ~1–2 s
  in Release), then the chosen difficulty, reputation ≥ `ScenarioRep` 60, funds ≥ `ScenarioFunds` 2000, `ScenarioBaseline`
  of deliveries. `CheckScenario` at midnight ends it (`Status` Won, `ScenarioMedal`); a loss gets no medal. Marks from the
  autoplayer (`SCENARIOS=1 [SCENARIO_SEEDS=N] dotnet test … --filter ScenarioCalibration`): silver ≈ its median, gold ≈ its
  top tenth. New game → Scenarios (`ScenarioChoices`, `BeginScenario` shows a note first), the chapter card's line, the end
  sheet (`ScenarioEnd`), `Records.Medal`/`SubmitMedal` (section "medals"). Shots: `--scenario=ID`, `--modal=scenarios`,
  `--modal=scenarioend`. A scenario is `Mode` Campaign with `ScenarioId` set.

### Since v0.46.0

- Delta updates (user: "make the updater faster"; chose our own over Velopack): `export.sh` also publishes `<platform>.pck`
  (raw), `<platform>-pck.txt` (`tools/pckblocks.py`: 64 KB blocks, rsync weak checksum + SHA-256, whole-file SHA-256) and
  `<platform>-code.zip` (data_*/StationX.*). `PckDelta` (plain C#, linked into the tests) rolls the weak checksum over the
  local pack, confirms hits by SHA-256 (`Match`), joins missing blocks into ranges, and `Assemble` checks the whole file.
  The check works out the plan (`Updater._delta`, offer says "under 1 MB"); `InstallDelta` fetches the code zip and the
  ranges (4 at a time, `FetchRange` needs 206) and falls back to the patch on any failure. Big downloads come in 4 ranged
  parts (`Download`), one stream if the server won't. v0.45.0 → v0.46.0: 0.11 MB of a 39.5 MB pack, match 0.1 s.
  `LIVE_RANGE=1` test checks GitHub keeps ranges across its redirect; `PCK_OLD/PCK_NEW/PCK_MANIFEST` test real packs.
  The first release with these files can only be delta-updated *from* by the next one (older copies use the patch).

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
   `wood_table_worn` photo, divided by its mean). Hut floors sit at ground level, so no hut steps. Moss done since.

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


Balance data (2026-11-19, riders, operation targets near the bot's output, Hard rep loss 1.15): Easy 40, Normal 21,
Hard 1 of 40. Watches open in act 5. On Normal great operations mostly succeed, some partial (Barbarossa ~1 in 5). On Normal, games are lost in acts 7–9 (Claymore to Action This Day) when reputation hits 0;
the early acts rarely decide a game.
