# Station X

An isometric 3D management game set at Bletchley Park, 1939–1945. You run the floor at the Government Code and Cypher School. Messages arrive from the listening stations, and your staff carry them from desk to desk and machine to machine until they leave as intelligence. The campaign starts with pencil-and-paper police ciphers, breaks the U-boats' four-rotor SHARK key, then Hitler's teleprinter cipher with Colossus, and runs through D-Day, Arnhem and the Ardennes to victory in Europe.

Built with Godot 4.7.2 (.NET) and C# on .NET 10, using the Forward+ renderer. The park is real 3D under an orthographic isometric camera that turns in 90° steps. The walls nearest the camera are cut away. Every model, texture and sound is generated in code, so the game ships with no art or audio assets apart from fonts.

## Running

```sh
godot --path ~/dev/StationX          # play
godot -e --path ~/dev/StationX       # open in the editor
dotnet test tests/StationX.Sim.Tests # run the simulation tests
./export.sh                          # release builds for Linux and Windows into build/
```

### Releases

GitHub Actions (`.github/workflows/release.yml`) runs the sim tests and the UI self-test (on a virtual display), renders a few screenshots, and exports every push to `main`; the builds are kept as workflow artifacts for 14 days. Pushing a tag such as `v0.2.0` also publishes a release with two downloads:

- `StationX-linux-x86_64.tar.gz`: run `./StationX.x86_64`
- `StationX-windows-x86_64.zip`: run `StationX.exe`

Release builds carry their version (CI stamps `application/config/version` from the tag). On start they ask GitHub's
public releases API whether a newer one is out, and the title screen offers **Update and restart**: it downloads the
matching archive, unpacks it inside the install folder and swaps it in (on Windows a small script does the swap after
the game closes). If the engine and .NET runtime are unchanged (each release lists their checksums in
`*-base.txt`), it fetches only the game's own files (`*-patch.zip`, about a third of the size). Saves and settings in `user://` are untouched. It can be turned off in Settings; editor runs,
self-tests and screenshots never check.

Godot lives in `~/.local/opt/godot/` and is linked as `~/.local/bin/godot`. Exports need the 4.7.2 .NET export templates in `~/.local/share/godot/export_templates/4.7.2.stable.mono`.

## How it plays

- **Requests** come from the War Office, Air Ministry, Admiralty and SIS. Each one asks for N decrypts on a network by a deadline. Accepting a request starts its traffic arriving at the teleprinter. Missing a deadline costs reputation by how much is still owed: a request one decrypt short costs a little, one barely started a lot.
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

  Canteens and tubes go anywhere; coke stoves go in any timber hut, extractor fans wherever Bombes go. The Huts panel lists what each one takes, and when you place a station the huts that take it are outlined.

  Machines (the Bombes and Colossus) keep running once a Wren has set them going. **Pneumatic tubes** carry paper between huts on their own.
- **Staff** walk messages between stations and between buildings. Messengers go first, then clerks; cryptanalysts, linguists and Wrens would rather stay by their desks, and only carry paper that a busy messenger or clerk hasn't collected within ten minutes (or straight away if none is out on an errand). Nobody walks through anybody: walkers go round people working at a desk or sitting at a table, wait a moment and squeeze past if there's no way round, and step aside when they meet. A day lasts four minutes at 1×.
- **Enigma keys change at midnight.** Each day, for each network, one message has to go through key-breaking:
  - in 1939–40, Zygalski sheets;
  - later, a crib desk followed by a Bombe;
  - for naval keys, Banburismus comes first.

  After that, that day's traffic needs only a few minutes on Typex. Messages waiting for their key show as pink slips in the out trays.
  - Bombes stop on false positives. A **checking machine** catches most of them; without one, Bombe runs take longer and some cribs come back.
  - Some intercepts arrive **garbled**. They go back to the listening station and return later.
- **Machines wear out.** Bombes, four-rotor Bombes, Typex machines, Colossus and the Tunny machine wear as they run, and the more worn they are, the likelier a breakdown. A broken machine waits about five hours (a Typex two and a half) and £80 for the RAF fitters; the job it was running waits too. **Service** a machine from its inspector (£20, one hour) to reset its wear. A flashing orange lamp means it's badly worn; red, with smoke, means it's broken.
- **RAF fitters.** Once the Bombe is researched the RAF offers fitters of your own (Staff, H). Posted on a watch to the building with the Bombes, checking machines or Typex, a fitter walks over to a breakdown and has it going again in under a third of the time, paying only for the parts, and services worn machines whenever one stands idle. Teleprinters, Colossus and the Tunny machines stay with the Post Office engineers.
- **Matapan.** On 25 March 1941 Knox's section reads the Italian navy's "Today is X minus 3": you decide how Admiral Cunningham is told, straight out, under a Sunderland's cover, or only as a general warning.
- **Ōshima's reports.** Japan's ambassador in Berlin cabled Tokyo what Hitler told him and what he saw of the Atlantic Wall. In the Colossus chapter and as D-Day's opens, the War Office asks for his reports through the Japanese desk; each one read before D-Day adds 1.5% to its readiness (up to 9%).
- **Custom games.** New game → Custom rules lays your own terms over the difficulty: money at the start, the weekly grant, Whitehall's patience, deadlines, German suspicion and how sturdy the machines are, and which chapter to start in. A custom game keeps no best score.
- **Against history.** The closing sheet sets the Park's firsts beside the real ones: the first wartime Enigma key, DOLPHIN, SHARK, TUNNY and Colossus's first break, with how many days sooner or later where the real date is known. Every finished war is kept on this computer, and the sheet says how this one ranks.
- **Flu waves.** Once a winter at most, the Medical Officer warns that influenza will reach the huts in three days. For a fortnight colds and flu are three times as likely (the top bar says FLU). Precautions (£150: staggered meals, windows open, the feverish sent home) halve that. Rested people catch it less, and a sick bay gets the ill back sooner.
- **A mole.** From the Blackout one of the linguists may start passing what they see to the Russians, and German air and army suspicion creeps up with nothing you did to explain it. After a week MI5 brings three names and what is known about each: dismiss one (the wrong one costs morale and standing, and the leak goes on), or move all three out of their building, which stops it without your ever knowing who.
- **Double Cross.** From the Colossus chapter MI5's double agents (GARBO, BRUTUS, TRICYCLE, TATE, TREASURE) feed the Abwehr FORTITUDE's phantom army in Kent, and only the Abwehr's own traffic shows whether Berlin believes them. Each agent's standing slips a little every day; each Abwehr decrypt shows how the least certain one is received. An agent left in doubt for long may be blown (reputation falls). On D-Day, what Berlin still believes adds up to 8% to the readiness. In the D-Day chapter the Gestapo seizes ARTIST, who knows TRICYCLE is ours: stand TRICYCLE down, or keep him on and watch the Abwehr's traffic. Intelligence (I) shows each agent.
- **The Wicked Uncles write early.** If Whitehall's patience runs low (reputation under 25) between Operation Claymore and 'Action this day', Turing, Welchman, Alexander and Milner-Barry offer to write to the Prime Minister there and then: +10 reputation, £2000 and the Admiralty's Wrens at once, and 'Action this day' brings less when it comes. The proper channels give a little now and leave the rest to come. Once a game.
- **The Americans (BRUSA).** A few days into the Testery chapter, Washington offers the agreement of May 1943. Share SHARK and the US Navy's Bombes at Dayton take four-rotor menus over the outstation teleprinter whenever your own Bombes are busy: about four hours slower for the cable, two Bombes at first and one more a month, up to six. Take the US Army's detachment too and its officers (William Bundy, Telford Taylor, Arthur Levenson and others, in olive drab) join the candidates every three weeks, paid by the Army. Either way the Kriegsmarine grows a little warier; keeping SHARK in Hut 8 earns breakthroughs instead.
- **Hut 7.** From the Blackout (February 1942) a new hut between the drive and the lake can be opened for the Japanese naval section (£700): registration, hand-cipher, Japanese, index and dispatch desks. Japanese readers work the Japanese desk only if they're posted to its building.
- **The JN-25 code book.** The Japanese desk looks groups up in the book recovered so far (30% at first): every JN-25 decrypt fills in more, and the desk grows faster. Tokyo brought in new books on 1 August 1942 (JN-25C) and 1 April 1943 (JN-25D); each sets the recovery back to a quarter. The desk's inspector shows how much is known.
- **People get tired.**
  - Energy runs down with work: nobody gets through a watch without a break (a break is due after about four hours at a desk, exhaustion after six). Energy only comes back sitting down at a **canteen** table (six seats), the **common room** (four armchairs) or the **rounders** pitch, and overnight in the billets; standing about is no rest. A cup of tea goes further in company (+25% per companion, up to three) and half as far across the table from a rival. After half an hour with nothing to do, people sit down at a table in their own building, close by, and stay until work comes in; the tired turn them, and anyone already fresh, out of their seats. With nowhere at all to sit, people only catch their breath slowly.
  - Morale goes up with breaks and successes and down with failures, night work and long days. Two days of very low morale and someone asks for a transfer.
  - From the Battle of Britain onwards there are three **watches** (day 08–16, evening 16–24, night 00–08; a weekly rota can change them round), and off-watch staff go back to their billets. A desk left half-way through a message goes cold: picked up within the hour nothing is lost, but after that whoever takes it on must read back through the worksheet, losing up to half the work after six hours. Keep every watch staffed.
- **Experience.** People get better with practice: after about 67 hours of desk or machine work they go up to ★★, and after 200 to ★★★. Faster work and fewer mistakes, but each step brings a pay rise, and losing a seasoned hand to a call-up hurts.
- **Traits.** Some recruits are crossword fiends, native German speakers, night owls, chess players, tireless or meticulous. A few are careless talkers, and you only find out when MI5 does.
- **Flash requests.** From early 1940, every few days a MOST IMMEDIATE request lands: a handful of messages due within hours, at three times the pay. Answer within three hours. Ignoring one costs reputation, declining costs a little less, and missing one after accepting costs a lot. Take them when you're already reading that network today. Every desk works on the most urgent message in its tray first.
- **Put a request first.** Each request in progress has a *Put first* button: every desk, machine and runner then handles its paper before anyone else's. A MOST IMMEDIATE request stops the game with a telegram and a triple ring of the teleprinter bell; accepting it puts it first at once. Until it is answered it throbs at the top of Needs attention.
- **Weather.** Each day brings English weather: mostly grey. Rain and snow close the rounders pitch, snow and gales slow everyone walking between the huts, and storms garble far more intercepts. Snow and rain fall on the park, and fog hangs over it.
- **Winter.** From November to March the timber huts are freezing. People in a hut without a **coke stove** (£60, £4 a week in coke) tire 40% faster and slowly lose heart. The Mansion and Block F have central heating.
- **Summer in the Bombe bay.** From May to September a building with three or more Bombes running is a furnace: everyone working there tires 30% faster and grows short-tempered, unless it has an **extractor fan** (£80, £3 a week; it goes wherever Bombes go).
- **Illness.** Colds in summer, flu in winter. People who work in freezing huts, are worn out, or are billeted far away catch it more easily, and it spreads: the more already off sick, the more fall ill. The sick stay home for 2–5 days. A **sick bay** in the Mansion (£350) gets them back sooner and halves the spread.
- **The Blitz.** From late August 1940 to May 1941 the sirens go on about one night in five. Until the all-clear, desk work runs at half pace in the blackout; the Bombes run on. Searchlights sweep the sky. Now and then a stray bomb falls on the Park (as it really did in November 1940), knocking a machine off its mountings and shaking everyone in that hut.
- **Ministries.** The War Office, Air Ministry, Admiralty and SIS each expect something. A ministry that has been sending requests but has had nothing completed for 10 days complains to the Foreign Office at the weekly accounts (−3 reputation, and again each week). The Needs attention list warns from 6 days.
- **Stale cribs.** From 1941 the Germans now and then change a habit a crib relied on. For 3–5 days that network's menus take 60% longer and its Bombe runs 30% longer (the key panel says so). Gardening still plants a crib for naval keys.
- **All hands.** When a deadline is slipping, call everyone off watch back in for four hours from the Staff panel: £6 overtime each, a morale hit for all, and those called in tire faster. Once a week at most.
- **The war grows.** German traffic grows with every chapter (requests are 6% bigger per chapter), while the huts only hold so many desks: keeping up means staffing them round the clock on three watches. Expect a Park of 60–80 people by the end, and a staffing crisis in October 1941 that ends with Churchill's 'Action this day'.
- **Layout matters more as the Park grows.** Desks for consecutive steps placed side by side in the same hut (working spots within two tiles) pass paper straight across, no runner needed. Crowded aisles slow everyone walking through them, and with 60–80 people that adds up.
- **Rooms, not halls.** The buildings are divided as they were: the Mansion's Library (the Naval Section), Morning Room (Denniston's office), Drawing Room, Ballroom, Dining Room and Entrance Hall; Hut 6's Machine, Registration and Decoding Rooms off a corridor; Hut 3's Watch and Index Rooms; Hut 8's Banburismus Room and the Prof's room; the Testery and Newmanry in Block F. Hut 11 stays one open Bombe room (its partitions came after 1943). No wartime plans of the hut interiors survive, so the layouts follow the accounts. Desks must sit wholly inside one room with their working space, corridors and doorways are kept clear, and paper is only handed across within a room. People with nothing to do wait by a wall in a room where their kind of work happens.
- **Hut 3's Index.** From the Battle of Britain, Enigma and Tunny decrypts are read against the card index (every unit, officer, place and callsign on a card) before they go out: translation → index → despatch. Put a card index beside each translation desk so the paper is passed across. Hand ciphers skip it.
- **Standing orders (from the Fifteenth of September).** Some requests are *N decrypts a day for three days*. Each day's running total is checked (with a day's grace for the pipeline): a day met pays at once and earns a little reputation, a day missed costs a little, and you can catch up. Its messages are queued by the next milestone, not the final deadline.
- **Heads of section have a style.** *Shepherd* (work +6%, morale up, strain eases), *Driver* (work +18%, people tire faster, morale slips), *Machine man* (work +8%, machines wear 40% slower) or *Teacher* (work +8%, experience twice as fast). A new head starts with the style that suits them; change it in their panel.
- **Burnout.** Someone who ends day after day exhausted builds up strain (the *Nerves* bar in their panel). Near breaking point you're asked: send them home on four days' leave, or keep them on and risk a breakdown that takes them away for ten days and shakes the whole hut.
- **Rivalries.** Cryptanalysts fall out (over whose crib it was, usually). While two rivals work in the same room both are slower and grumpier: post one to another building until they make it up.
- **Crises (from chapter 2).** Every so often the Park's power fails and every machine stops for hours: hire a generator for the Bombes (£250) or wait for the engineers. Or a battle opens and a run of MOST IMMEDIATE requests follows: cancel leave and put everyone on overtime for two days (£200, work 25% faster, people tire faster and grumble), or carry on as rostered.
- **Choosing what to break.** There's never enough Bombe time. On the Enigma keys card each network has an order: *First* jumps its key attempt (and then its traffic) to the front of every queue; *Hold* skips that key today, leaving the Bombes to the others while its traffic waits.
- **War map (O).** Each accepted request is a real operation: a convoy to route, a raid to meet, a battle to warn of, dated to the campaign (Dunkirk, the Blitz, BISMARCK, PQ 17, Alamein…). Deliver in time and it goes our way, which lifts morale and, for each operation won in the last week, the grant by 2% (up to 8%). Miss it and ships are sunk, raids go unopposed, and morale suffers. *Spread out the map* shows every operation in the theatre.
- **BROWN (the Blitz).** In the Fifteenth of September and Claymore acts the Air Ministry also asks for BROWN, the key of KG 100's beam-navigation traffic: tonight's X-Gerät settings, in time to bend the beams and light the decoy fires. One more key to break every day.
- **Teleprinters jam.** Every message received wears a teleprinter; a worn one jams and the GPO engineers take about two hours (£30) to mend it. Meanwhile its traffic waits at the Y-station unless another teleprinter can take it. Service them like the machines, and a second teleprinter is insurance.
- **Typex slips.** A Wren sometimes sets a wheel wrongly and the decrypt comes out as gibberish, so it goes through again: 7% for a beginner, 2% for an expert, doubled when worn out, halved for the meticulous and Typex specialists.
- **Intelligence goes off, some sooner than others.** Air and naval decrypts (tonight's raid, a U-boat's position) earn half the breakthroughs after 16 hours and none after 32; army ones after one day and two; Abwehr, police and TUNNY after two days and four. They still count for their requests. A backlog costs research.
- **Handbook (F1).** A panel showing how each kind of traffic goes through the Park, desk by desk, with who works each step: green where you have one, greyed out where it isn't available yet.
- **Offizier messages (from U-110).** A fifth of the naval decrypts come out of the Typex still gibberish: they were enciphered again on an officers-only setting. They go to a hand-cipher desk to be unpicked before translation, so the naval line needs cryptanalysts as well as Bombes.
- **Slide the Banbury sheets yourself (Hut 8).** While a Banburismus table works on a naval key, its panel offers the two punched sheets: slide the pink one along the other. By chance two or three holes line up; where the messages are in depth, seven or more do. Pick that offset and the wheel orders are cut for the Bombes at once; a wrong guess costs twenty minutes.
- **Visits from Whitehall (from the Battle of Britain).** Every week or two "C", the Director of Naval Intelligence or a ministry man is announced for the next day. They judge what they see: overflowing in-trays, broken machines, glum faces, a request about to be missed, an overdrawn account, and heads: three in five of the watch standing about with nothing to do marks the Park down, everyone hard at work (no more than three in ten idle) marks it up. A smooth-running Park earns reputation, a shambles loses it, and the Treasury then holds back a quarter of the next week's grant. The status bar shows when they're due.
- **Reports to the Chiefs of Staff.** Once Whitehall judges the Park by its results, breakthroughs left over from research can go to the Chiefs as a report (Research panel): +2 reputation, at most once a fortnight, each report costing 50 more than the last (from 60). Keep enough back for the methods the war still brings.
- **The Prime Minister's visit.** A few days into Barbarossa, Mr Churchill comes to see the Park for himself, announced the day before. He judges it as any visitor would, but his good opinion is worth more (+5) and his poor one costs reputation without touching the grant; whatever he sees, his speech on the lawn lifts everyone's morale. A black staff car stands by the Mansion while any visitor goes round.
- **Tell people apart at a glance.** Each role has its own look: Wrens in navy uniform and cap, messengers in dark red with cap and satchel, cryptanalysts in tweed with long striped college scarves, clerks in shirtsleeves and waistcoats or grey cardigans, linguists in green with spectacles, bow ties or pearls. And everyone is their own person: height, build, hairstyle, grey hair, glasses, moustaches and beards, and the shade of their clothes all vary.
- **Drag the crib yourself.** While a crib desk works on a key attempt, its panel offers a work sheet: slide the crib (WETTERVORHERSAGE and friends) along the intercept. Enigma never enciphers a letter as itself, so wherever a crib letter sits on the same letter the crib can't be there; exactly one position is clear. Place it there and the menu is done at once; a wrong position costs the desk twenty minutes.
- **Heath Robinson (Tunny chapter).** Research it and build one in the Newmanry: Newman's tape machine breaks Tunny wheel settings in under four hours (the Testery takes nine), but its tapes stretch and snap, so it wears out fast and a fifth of its runs come back inconclusive. Colossus, when it comes, takes over.
- **Traffic analysis (from Victory).** Before a key is broken, its messages' externals say who is talking to whom. A traffic-analysis desk (clerk; Hut 6, Hut 8 or the Mansion) logs two of each network's messages a day; once it has, that day's cribs take 40% less time and its Bombe runs 15% less. Hut 6's traffic analysts became SIXTA.
- **Kisses (from Operation Claymore).** A kiss was the same text sent twice, once in a cipher already read: a perfect crib. The German dockyards and weather stations sent their routine morning reports in WERFT, a dockyard hand cipher, and again in naval Enigma. Set WERFT to *Read* on the Enigma keys card and Scarborough sends two of its signals each morning to a hand-cipher desk. Once one is read, that day's DOLPHIN and SHARK keys are kissed: the menu takes a third of the time, the Bombe run is a fifth shorter (an attempt already under way is cut short too), and the stop holds. A kiss is only good for its own day, and WERFT that can no longer kiss anything is filed unread. The price is about three hours of a cryptanalyst's time at the hand-cipher desk each day, which the Offizier, police and Abwehr traffic also need.
- **JN-25 (from the Blackout, February 1942).** Since Pearl Harbor the Far East Combined Bureau cables the Park the Japanese navy's fleet code: five-figure code groups under an additive. A hand-cipher desk strips the additive; then a **Japanese desk** looks the groups up in the recovered book and puts them into English. Only linguists who have been on the Bedford Japanese course can work it: select a linguist and choose *Learn Japanese at Bedford* under Learns (five days away, £90), or hire one of the new linguists who has already been. Tokyo's working day is the British night, so the traffic comes in between 22:00 and 08:00, which gives the night watch work of its own. ULTRA decisions in the Indian Ocean (submarine patrol lines, the raiders' tankers, Nagumo's carriers off Ceylon, Yamamoto's tour) come with it.
- **ULTRA's price.** When an operation is won, decide how its decrypts are used (War map, O): *Act* for the full result and more enemy suspicion; *With cover* (a reconnaissance aircraft sent to be seen first) for three-quarters of the result and little suspicion, two a week; or *Hold back* for nothing at all. Undecided after 12 hours, your ULTRA policy chooses.
- **Postings.** Everyone is hired onto a watch and into a building: each candidate's card has both, with the most needed marked ★. They work, carry and wait only in their building until you post them elsewhere (select them: *Posted to*); paper bound for another building goes down in a tray for a messenger. Only messengers range over the whole Park. When a building has desks with nobody posted on a watch, a tip says so. *Balance watches* spreads each role's watches building by building.
- **Teleprinter routing.** Each teleprinter's inspector lists the networks it receives: put a second teleprinter in Hut 6 or Hut 8 and route RED or DOLPHIN straight to it. A network routed nowhere still arrives at any teleprinter.
- **Intercepts (N).** Read any message on the floor or recently delivered: the Y-station log sheet and cipher groups, the decrypt as it came off the Typex, and the English translation, each appearing as the huts get that far. Texts are generated per network (Luftwaffe readiness and Knickebein beams, Afrika Korps fuel, U-boat sightings and weather signals, police and Abwehr traffic, Tunny appreciations) and enciphered for real: a working Enigma with the campaign's daily keys (M4 for SHARK), double transposition for police hand ciphers, teleprinter cipher for Tunny.
- **The lake** ripples in the wind, rings with rain, glints in the sun, and freezes over when snow lies long enough.
- **Operation Ruthless.** In the autumn of 1940 Ian Fleming offers his plan to pinch naval Enigma material with a captured bomber. Mount it (£300) for a 40% chance of a windfall of breakthroughs, at the risk of alerting the Kriegsmarine; or shelve it, as the Admiralty did.
- **Heads of huts.** Appoint a cryptanalyst (two stars or better) to head a hut from their inspector, as Welchman headed Hut 6 and Turing Hut 8. While the head is in, everyone working a desk or machine there goes 10% faster, and people there are a little happier each day. But the head no longer works a desk. One head per hut; the head's name appears under the hut's.
- **The Wren shortage.** From the Battle of Britain until Churchill's "Action this day" (October 1941), the WRNS can spare about one Wren a week. Afterwards three a week come forward. Plan your Bombes around the Wrens you have.
- **Vetting.** New recruits are interviewed, vetted and put on a train: they report about 16 hours after you hire them (messengers, local lads, in 6; half that on Easy). Hire ahead of need. The famous names report at once.
- **The Park's own security (from the Polish gift).** Worksheets left out overnight on a desk abandoned mid-message, and staff lodged far away talking on the bus, raise the Abwehr and SD's suspicion. A security sweep from the Intelligence panel plugs the leaks (suspicion −15) at the cost of an hour of everyone's energy and a little morale, once every four days.
- **Stores (from spring 1940).** Every message uses dockets and worksheets at registration. Order 60 messages' worth (£45) from the Staff panel; the Stationery Office lorry takes about 20 hours. Run out and every desk works at half speed until it comes. A standing order reorders by itself when two days' use is left, at a quarter more.
- **Coming and going.** Before each watch its people come up the road on foot or by bicycle, stop at the barrier while the sentry checks their passes, and go on to their huts, a few minutes to half an hour early and never all at once; cyclists leave their bicycles in the rack by the door, and anyone billeted miles away comes on the billets bus, which waits at the gate after the watch to take them home. At the end of a watch they drift out over the next twenty minutes and go home the same way. This is only the picture: it changes nothing in the game.
- **The Recreational Club (from the Phoney War).** Spirits matter: unhappy people work slower and in the end leave. Every ten days the Staff panel can arrange an evening (£50): a concert, Scottish country dancing, the Drama Society's revue, a chess evening or a dance, at half past seven. Everyone's spirits rise, and quarrels are often made up.
- **Billets.** The Park starts with lodgings for 20. Anyone beyond that (the newest arrivals) is billeted miles away and bussed in: 10% slower and steadily unhappier. Requisition 10 more billets at a time (£400) from the Staff panel.
- **Call-up papers.** From the Phoney War until 1942 the Ministry of Labour calls up men on the staff, and it has an eye for the ones you can least spare. Fight for a deferment (reputation), get the post made a reserved occupation (£150), or let him go.
- **Pinning.** You can pin a person to a station; they work there first and don't run errands.
- **ULTRA and suspicion.**
  - Every decrypt that gets used makes its German service (Luftwaffe, Heer, Kriegsmarine, Abwehr/SD) a little more suspicious. At 100, that service tightens procedures and its keys go dark for three days.
  - Choose a cautious, standard or bold policy, and answer the occasional **dilemma**: act on this, or protect the source? Each one says what we know and how (the source, how sure, what's at stake), how close that German service is to locking us out and which keys it would cost, and where each choice would leave its suspicion.
  - The dilemmas come from a deck of about 25 scenarios, each tied to the weeks it fits (the Adlerangriff airfields, Dunkirk, the beams, Crete, Bismarck, PQ 17, weather ships, double agents, the police reports of 1941, Rommel's fuel, Kursk and more). A small variety engine (`Tale`) fills in the places, ships, units, sources and stakes, so the same scenario reads differently each time, and the intercepts carry everyday procedure (repeats, call-sign changes, leave, birthday greetings) beside the news.
  - From 1941 you can ask for **gardening**: minelaying that gives naval cribs, at a price in suspicion.
- **Y-stations (Y).** The listening stations (Denmark Hill, Chicksands, Arkley View, Beaumanor, Scarborough, Knockholt) take down each network. Every network needs two receiver sets: with one its traffic is caught late on repeats and garbles more, with none it arrives hours late, and an extra set copies it cleaner. The Y Board allots one more set each chapter; more cost £120. Traffic comes by dispatch rider in a bundle every two hours (going stale on the road) until you lay a teleprinter line from that station (£250). Direction-finding (£400 per German service, from the Polish gift for the Abwehr, Victory for the Luftwaffe, Claymore for the army and navy) halves the suspicion of cover-story choices. From the Polish gift, a network now and then changes its frequencies and goes quiet until it's found; spare sets make the search much shorter.
- **Outstations** (Eastcote, Stanmore, Wavendon, Adstock, Gayhurst) are bought as remote Bombe capacity. An outstation teleprinter in the park sends them menus when your own Bombes are busy. They are not separate maps.
- **Research** is paid for with breakthroughs, which you earn from delivered decrypts.
- **Reputation** sets the weekly Treasury grant. Wages and station upkeep come out of it every Sunday; the grant alone never pays for a growing Park, and most of the money comes from the requests themselves (the weekly accounts say how much). If reputation reaches zero, the game is lost. From the Phoney War on, Whitehall gets used to miracles: reputation slips a little every day, and a little more each act, so you have to keep delivering. From the Battle of Britain, Whitehall judges the Park by what it delivers: it expects a share of what the ministries ask for as fresh decrypts a day, from half at first to four-fifths late in the war (the line under the reputation bar shows the fresh decrypts a day over the last two days, how many are wanted and today's so far; its tooltip says more). Averaged over the last two days, the shortfall sets the slip: half the quota slips a little faster than before, nothing delivered over twice as fast, and even the quota met slips at 0.6 of the old rate. A visitor from Whitehall also counts heads: most of the watch standing idle marks the Park down, everyone busy marks it up. A decrypt counts three-quarters once its news is stale (air and naval after 16 hours, army after a day) and half once it's dead. An idle Park that takes few requests drifts down; spare desks and people are capacity to sell. Hover over the reputation bar to see the past week's gains and losses by cause. And don't hoard: with more than £2,500 in hand at the weekly accounts, the Treasury holds back part of the grant.

### Modes

- **Campaign.** 7 chapters and 19 acts, from Station X in 1939 through SHARK (1942), Tunny and Colossus (1943–44) and the Second Front to Victory in Europe (1945).
- **Great operations.** Four acts build up to an operation that starts on a fixed day whatever the Park does: Barbarossa (June 1941), D-Day (June 1944), Market Garden (September 1944) and the Ardennes (December 1944). The objectives card counts down to it and shows **readiness**: the act's "before" objectives (60%), how unsuspecting the watched German services still are (25%; a lockdown wipes it out), a base of 15%, and one decision a few days before, worth up to ±20%. At 75% the operation succeeds (reputation, breakthroughs, morale); under 45% it fails and costs heavily. A failed D-Day ends the campaign.
- **Free play.** No objectives and no closure. Start at any chapter, with everything before it already built and researched. The war moves on every ten days.
- **Difficulty.** Easy, Normal and Hard change starting funds, the grant, deadlines, how fast suspicion grows and how much a missed deadline costs.

- **Workload (U).** How busy every desk, machine and person has been over the last 24 game hours. Each kind of desk gets a bar over its working hours (working, no paper, paper with nobody at it, someone on the way to it, out of action; quiet time is left faint) with its busy share, how much paper is waiting for it, and how many of them were ever in use at once ("Registration desks ×4 · at most 1 in use"); each role a bar of its time on watch (at a desk, carrying, walking, on a break, idle) and how many of them are spare. A small line beside each bar is its last fortnight, a day at a time. Under the watches, a table shows each role on each watch against how many the work needed at its busiest hour. At the top a plain-English line names the bottleneck ("Bombes are the bottleneck: 96% busy, 12 waiting"), desks short of staff and on which watch, desks waiting on people walking over from another building (post some there), roles standing idle, and capacity to spare when the Park is short of Whitehall's quota with its desks mostly idle (take on more requests). Click a row to go to the desk most in need, or to someone of that role. While the panel is open, a ring round every desk shows its day and a dot in the middle what it is doing now, and a dot over each person their state; with the flow view (F) on as well, its lines join the rings and the tags show paper waiting in the same words. The desk and person inspectors carry the same day as an hour-by-hour strip. The hourly record starts afresh after loading; the daily trend is saved.
- **Courses.** Anyone can be sent on a short course (12 hours away, £30) to learn clerical work, the messengers' round or the Wrens' machines, and a linguist and a cryptanalyst can learn each other's trade in three days (£80). Pick someone in Staff and choose what they *Learn*: they work that role at 80% speed, and only when their own desks have nothing waiting. Workload suggests it when one role stands idle while another's desks go unstaffed.
- **Letting people go.** Once there's most of a day on record, a role with hands to spare gets a *Let N go* button in Staff: it dismisses the idlest people on watches that had more than they needed, keeping each watch what its busiest hour needed and one more. Heads of huts and the famous names stay.
- **The Telegraph crossword.** From January 1942 the Daily Telegraph will run a crossword competition for the Park (Staff panel, £75, once every four weeks). Three days later three skilled cryptanalysts, crossword fiends with faster, better cribs, join the candidates and wait there until hired or the next competition.
- **Who to move or let go.** The Staff panel (H) puts a small bar of each person's last day on watch beside their name, with how idle they were when it was most of the time, and lists each role idlest first (untick *Idlest first* for the usual order). A line above names everyone idle most of their watch, and the watch table reads "on / needed" for every role: red where paper waited with nobody, amber where there are hands to spare. Statistics (K) charts how busy the desks were and how idle the staff, day by day.

A **Needs attention** list under the Enigma keys shows broken or badly worn machines, freezing huts, unanswered flash requests, requests about to be missed, decrypts waiting for a card index nobody has built, people with nowhere to sit for their break, networks short of receiver sets, an overdraft, desks nobody on the staff can work, air raids, overcrowded billets, illness and ministries left waiting, and what the workload record shows has held for most of a day: a step that was the bottleneck for 12 hours or more (build another), paper with nobody at it for most of a watch (put more people on it), and staff idle most of their watch (move them or let some go). Each says what to do; click one to go to it.

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
| F | flow view: where paper goes, and how long it waits at each desk (a key along the bottom) |
| U | Workload: how busy each desk, machine and role has been over the last day, and the bottleneck |
| B H Q T G I Y K | Build, Staff, Requests, Research, Huts, Intelligence, Y-stations, Statistics |
| F5 / F8 | quicksave / quickload |
| Esc | cancel, deselect, close panel, then the pause menu |
| F9 (debug builds) | +£5000, +50 breakthroughs |
| F10 (debug builds) | skip to the next act |

The settings (in the pause menu or on the title screen) cover volume, interface size (in a small window the interface keeps to a readable size), edge scrolling, hints and tips (a chapter-1 walkthrough, then one tip the first time each later system comes up), graphics (High, Balanced or Low: Low draws the world at about half the cost, for older laptops), film grain and fullscreen. They are saved to `user://settings.cfg`. Saves go in `user://saves/`.

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

- `--panel=requests|build|staff|research|huts|intel|stats|workload`
- `--modal=pause|settings|save|load|newgame|dilemma|update|end` `--place=Type@x,y;...` puts stations down (free, building unlocked) for a visual check. `--lineup=x,y` stands everyone on duty in rows by role, to check how people look. `--age=H` makes every message H hours older; `--inspect=Type` opens the busiest station of that type (`--inspect=Clerk` or another role: someone of that role on duty). `--bench=N` runs N frames at 6x after the warm-up and prints where each frame's time went (script parts, render CPU and GPU, draw calls); `--nofx=ssao,ssil,shadow,msaa,...` switches effects off to price them, `--graphics=0|1|2` picks the quality.
- `--select=bombe`, `--briefing`, `--title`, `--build=Registration`
- `--weather=fine|overcast|rain|fog|snow|storm`, `--raid`, `--head=hut6` (appoints the best cryptanalyst head of that hut)
- `--quarter=0..3`, `--flows`, `--breakdown` (breaks the first machine and wears another)
- `--seed=N`, `--difficulty=easy|normal|hard`, `--freeplay --act=N`
- `--load=slot1|slot2|slot3|quick|auto`
- `--zoom`: a value above 1 moves closer.

## Fonts

Special Elite (Apache 2.0), Libre Baskerville and Libre Franklin (SIL OFL). Their licences are in `assets/fonts/`.
