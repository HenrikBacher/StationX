# Station X

An isometric 3D management game set at Bletchley Park, 1939–1944. You run the floor at the Government Code and Cypher School. Messages arrive from the listening stations, and your staff carry them from desk to desk and machine to machine until they leave as intelligence. The campaign starts with pencil-and-paper police ciphers and ends with breaking the U-boats' four-rotor SHARK key. After that there is an optional continuation into Tunny and Colossus.

Built with Godot 4.7.2 (.NET) and C# on .NET 10, using the Forward+ renderer. The park is real 3D under an orthographic isometric camera that turns in 90° steps. The walls nearest the camera are cut away. Every model, texture and sound is generated in code, so the game ships with no art or audio assets apart from fonts.

## Running

```sh
godot --path ~/dev/StationX          # play
godot -e --path ~/dev/StationX       # open in the editor
dotnet test tests/StationX.Sim.Tests # run the simulation tests
./export.sh                          # release builds for Linux and Windows into build/
```

### Releases

GitHub Actions (`.github/workflows/release.yml`) runs the sim tests and the UI self-test (on a virtual display), renders a few screenshots, and exports every push to `main`; the builds are kept as workflow artifacts for 14 days. Pushing a tag such as `v0.2.0` also publishes a release with three downloads:

- `StationX-linux-x86_64.tar.gz`: run `./StationX.x86_64`
- `StationX-windows-x86_64.zip`: run `StationX.exe`
- `StationX-linux-windows.zip`: both in one folder. There's no single executable that runs natively on both systems, but the two launchers share the same game data (`StationX.pck`), so one download works on either.

Godot lives in `~/.local/opt/godot/` and is linked as `~/.local/bin/godot`. Exports need the 4.7.2 .NET export templates in `~/.local/share/godot/export_templates/4.7.2.stable.mono`.

## How it plays

- **Requests** come from the War Office, Air Ministry, Admiralty and SIS. Each one asks for N decrypts on a network by a deadline. Accepting a request starts its traffic arriving at the teleprinter.
- **Stations** go inside huts and need clear working space in front. Each one is worked by a role:
  - registration desk and despatch box: Clerk
  - hand-cipher desk, crib desk, Zygalski table, Banburismus table and Testery: Cryptanalyst
  - Bombe, checking machine, Typex, Colossus, Tunny machine and outstation teleprinter: Wren
  - translation desk: Linguist
  - Messengers work no desk. They carry paper between desks and buildings, a bit faster than everyone else, and they're cheap. Like the real Park, which took on local teenagers as messengers.

  Each building has a job, and a station can only be built where it belongs:
  - the Mansion takes any desk work, but no machines;
  - Hut 1, beside the Mansion: the first Bombe, "Victory" (1940);
  - Hut 6: Army and Luftwaffe Enigma (cribs, Zygalski sheets, and Typex in the decoding room);
  - Hut 11, from summer 1940: the Bombes, the checking machines and the outstation teleprinter;
  - Hut 3: translation and despatch;
  - Hut 8: naval Enigma, with its own Typex decoding room;
  - Block F: Tunny and Colossus.

  Machine placement follows where the machines really stood.

  Canteens and tubes go anywhere; coke stoves go in any timber hut. The Huts panel lists what each one takes, and when you place a station the huts that take it are outlined.

  Machines (the Bombes and Colossus) keep running once a Wren has set them going. **Pneumatic tubes** carry paper between huts on their own.
- **Staff** walk messages between stations and between buildings. Messengers go first, then clerks, then anyone whose own desks are quiet. A day lasts four minutes at 1×.
- **Enigma keys change at midnight.** Each day, for each network, one message has to go through key-breaking:
  - in 1939–40, Zygalski sheets;
  - later, a crib desk followed by a Bombe;
  - for naval keys, Banburismus comes first.

  After that, that day's traffic needs only a few minutes on Typex. Messages waiting for their key show as pink slips in the out trays.
  - Bombes stop on false positives. A **checking machine** catches most of them; without one, Bombe runs take longer and some cribs come back.
  - Some intercepts arrive **garbled**. They go back to the listening station and return later.
- **Machines wear out.** Bombes, four-rotor Bombes, Colossus and the Tunny machine wear as they run, and the more worn they are, the likelier a breakdown. A broken machine waits about five hours (and £80) for the RAF fitters; the job it was running waits too. **Service** a machine from its inspector (£20, one hour) to reset its wear. A flashing orange lamp means it's badly worn; red, with smoke, means it's broken.
- **People get tired.**
  - Energy runs down with work. Tired people work slower and take tea breaks if there is somewhere to sit: the **canteen**, the **common room**, or the **rounders** pitch on the lawn.
  - Morale goes up with breaks and successes and down with failures, night work and long days. Two days of very low morale and someone asks for a transfer.
  - From the Battle of Britain onwards there are three **watches** (day 09–16, evening 16–24, night 00–09), and off-watch staff go back to their billets.
- **Experience.** People get better with practice: after about 67 hours of desk or machine work they go up to ★★, and after 200 to ★★★. Faster work and fewer mistakes, but each step brings a pay rise, and losing a seasoned hand to a call-up hurts.
- **Traits.** Some recruits are crossword fiends, native German speakers, night owls, chess players, tireless or meticulous. A few are careless talkers, and you only find out when MI5 does.
- **Flash requests.** From early 1940, every few days a MOST IMMEDIATE request lands: a handful of messages due within hours, at three times the pay. Answer within three hours. Ignoring one costs reputation, declining costs a little less, and missing one after accepting costs a lot. Take them when you're already reading that network today. Every desk works on the most urgent message in its tray first.
- **Weather.** Each day brings English weather: mostly grey. Rain and snow close the rounders pitch, snow and gales slow everyone walking between the huts, and storms garble far more intercepts. Snow and rain fall on the park, and fog hangs over it.
- **Winter.** From November to March the timber huts are freezing. People in a hut without a **coke stove** (£60, £4 a week in coke) tire 40% faster and slowly lose heart. The Mansion and Block F have central heating.
- **Illness.** Colds in summer, flu in winter. People who work in freezing huts, are worn out, or are billeted far away catch it more easily, and it spreads: the more already off sick, the more fall ill. The sick stay home for 2–5 days. A **sick bay** in the Mansion (£350) gets them back sooner and halves the spread.
- **The Blitz.** From late August 1940 to May 1941 the sirens go on about one night in five. Until the all-clear, desk work runs at half pace in the blackout; the Bombes run on. Searchlights sweep the sky. Now and then a stray bomb falls on the Park (as it really did in November 1940), knocking a machine off its mountings and shaking everyone in that hut.
- **Vetting.** New recruits are interviewed, vetted and put on a train: they report about 16 hours after you hire them (messengers, local lads, in 6; half that on Easy). Hire ahead of need. The famous names report at once.
- **Billets.** The Park starts with lodgings for 20. Anyone beyond that (the newest arrivals) is billeted miles away and bussed in: 10% slower and steadily unhappier. Requisition 10 more billets at a time (£400) from the Staff panel.
- **Call-up papers.** From the Phoney War until 1942 the Ministry of Labour calls up men on the staff, and it has an eye for the ones you can least spare. Fight for a deferment (reputation), get the post made a reserved occupation (£150), or let him go.
- **Pinning.** You can pin a person to a station; they work there first and don't run errands.
- **ULTRA and suspicion.**
  - Every decrypt that gets used makes its German service (Luftwaffe, Heer, Kriegsmarine, Abwehr/SD) a little more suspicious. At 100, that service tightens procedures and its keys go dark for three days.
  - Choose a cautious, standard or bold policy, and answer the occasional **dilemma**: act on this, or protect the source?
  - From 1941 you can ask for **gardening**: minelaying that gives naval cribs, at a price in suspicion.
- **Outstations** (Eastcote, Stanmore, Wavendon, Adstock, Gayhurst) are bought as remote Bombe capacity. An outstation teleprinter in the park sends them menus when your own Bombes are busy. They are not separate maps.
- **Research** is paid for with breakthroughs, which you earn from delivered decrypts.
- **Reputation** sets the weekly Treasury grant. Wages and station upkeep come out of it every Sunday. If reputation reaches zero, the game is lost. From the Phoney War on, Whitehall gets used to miracles: reputation slips a little every day, and a little more each act, so you have to keep delivering. And don't hoard: with more than £2,500 in hand at the weekly accounts, the Treasury holds back part of the grant.

### Modes

- **Campaign.** 5 chapters and 12 acts from Station X to SHARK. After that comes an optional sixth chapter, Tunny, with two acts: The Testery (1943) and Colossus (1944).
- **Free play.** No objectives and no closure. Start at any chapter, with everything before it already built and researched. The war moves on every ten days.
- **Difficulty.** Easy, Normal and Hard change starting funds, the grant, deadlines, how fast suspicion grows and how much a missed deadline costs.

A **Needs attention** list under the Enigma keys shows broken or badly worn machines, freezing huts, unanswered flash requests, an overdraft, and desks nobody on the staff can work. Click one to go to it.

At the end of a campaign you get a **score** (decrypts, requests, reputation and speed, less missed deadlines and shipping lost; ×0.75 on Easy, ×1.4 on Hard) and an honour, from "Thanked by the Director" to Churchill's "geese that laid the golden eggs". The best score on each difficulty is kept in `user://records.cfg`.

The game autosaves every morning. It also has three save slots and a quicksave.

### Controls

| | |
|---|---|
| Left click | select, place |
| Right-drag / middle-drag | pan |
| Wheel | zoom |
| WASD / arrows / screen edges | pan |
| , and . (or [ and ]) | turn the view 90° |
| Space | pause |
| 1 2 3 | speed 1×, 3×, 6× |
| R / Shift+R | rotate while building or moving |
| Shift-click | place several |
| M | move the selected station (£25) |
| F | flow view: where paper goes, and how long it waits at each desk |
| B H Q T G I K | Build, Staff, Requests, Research, Huts, Intelligence, Statistics |
| F5 / F8 | quicksave / quickload |
| Esc | cancel, deselect, close panel, then the pause menu |
| F9 (debug builds) | +£5000, +50 breakthroughs |
| F10 (debug builds) | skip to the next act |

The settings (in the pause menu or on the title screen) cover volume, interface size, edge scrolling, chapter-1 hints, film grain and fullscreen. They are saved to `user://settings.cfg`. Saves go in `user://saves/`.

## Layout

```
src/Sim/          Pure C# simulation, no Godot references (unit-tested)
  Defs.cs         every tunable number: stations, nets, research, roles, traits, shifts, economy, difficulty
  Grid.cs         the park map, huts, A* pathfinding
  Entities.cs     stations, staff, messages, contracts, dilemmas
  GameState.cs    tick loop, movement, station work, routing, daily keys, tubes, outstations
  People.cs       watches, energy, tea breaks, morale, pinning
  Intelligence.cs German suspicion, ULTRA policy, dilemmas, gardening, outstations
  Scheduler.cs    assigns operators and runners to waiting work
  Actions.cs      everything the player can do; contracts; economy
  Campaign.cs     the acts: briefings, goals, events; free play; the Tunny continuation
  SaveGame.cs     JSON save and load
  AutoPlayer.cs   scripted player used by tests and screenshots
src/View/         Godot nodes that draw and drive the sim
  Main.cs         root: syncs models to the sim, input, build/move ghost, day/night, sound cues, autosave
  MainSelfTest.cs --selftest: drives the game through synthetic input and checks each tool
  CameraRig.cs    orthographic isometric camera: pan, zoom, 90° turns, mouse-to-ground rays
  Park3D.cs       ground, lake, trees, the Mansion and huts (per-side cutaway walls), fences, props
  Textures.cs     procedural brick, weatherboard, concrete, felt, stone
  StationModel.cs 3D model of each desk and machine, animations, message piles, break seats
  PersonModel.cs  staff figures: walking, carrying, working, sitting, tired
  Sound.cs        synthesised sound effects and ambience loops
  Settings.cs, Saves.cs, FilmGrain.cs
  Ui/             HUD, side panels, inspector, menus, charts, 2D overlay over the 3D view
tests/            xUnit tests (37), including a full-campaign autoplay on 3 seeds
```

### Screenshot mode and self-test

```sh
godot --path . --resolution 1600x900 -- --shot=/tmp/park.png --days=22 --hour=14 --zoom=0.85 --center=24,14
godot --path . --resolution 1600x900 -- --selftest      # exits 1 on any failure
```

Optional flags:

- `--panel=requests|build|staff|research|huts|intel|stats`
- `--modal=pause|settings|save|load|newgame|dilemma|end`
- `--select=bombe`, `--briefing`, `--title`, `--build=Registration`
- `--weather=fine|overcast|rain|fog|snow|storm`, `--raid`
- `--quarter=0..3`, `--flows`, `--breakdown` (breaks the first machine and wears another)
- `--seed=N`, `--difficulty=easy|normal|hard`, `--freeplay --act=N`
- `--load=slot1|slot2|slot3|quick|auto`
- `--zoom`: a value above 1 moves closer.

## Fonts

Special Elite (Apache 2.0), Libre Baskerville and Libre Franklin (SIL OFL). Their licences are in `assets/fonts/`.
