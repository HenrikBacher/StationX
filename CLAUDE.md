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

Last release **v0.37.0** (JN-25 and the Japanese desk, the Recreational Club, the billets bus, umbrellas and torches, smoother frames); v0.36.0 had Chiefs reports, geese, smoke, the Graphics setting, commuters through the gate; v0.35.0 had the Telegraph crossword, the Prime Minister's visit, Let N go, bicycles; v0.34.0 had courses, Bombe-bay heat; v0.33.0 had Whitehall's quota, Workload panel, kisses; v0.32.0 put a request first, MOST IMMEDIATE telegrams; v0.31.0 had dispatch riders and the gate, music and ambience, failable operations; v0.30.0 had the
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

### Since v0.37.0 (2026-11-20)

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
