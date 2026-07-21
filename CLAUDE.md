# MecchaChameleon — mobile MECCHA CHAMELEON-like (hide-paint-find PvP)

Unity 6000.0.77f1 · URP · Input System · portrait mobile (1080×1920 reference).
GitHub: **https://github.com/tyf9596/duomaomao** (main; Library/ ignored, push after
each session-worth of work; git user tyf9596, HTTPS via Windows credential manager).

## Vision (pivoted 2026-07-14, mirrors the Steam hit MECCHA CHAMELEON)

Multiplayer PvP hide-and-seek, **AI-bot version first, online later**:
players match into a room, one is randomly the **Hunter** (shotgun). **Hiders**
(white bodies) get a hide-phase to run/dash/climb/pose around the map and
paint themselves to blend in. Then the hunter stalks; **every hider shot joins
the hunter team** (infection). Hunters win by clearing everyone before the
clock; survivors win otherwise. Signature ideas from the Steam game to keep:
white paintable bodies, pose-as-scenery, no-undo painting, and (later) points
for hiding *within* the hunter's line of sight.

## Layout

- `Assets/Game/Scripts/Arena/` — **the real game** (all namespace-free, scenes stay dumb)
  - `MatchManager.cs` — bootstraps into any scene with an `ArenaMap` (also via a
    `sceneLoaded` hook so PLAY AGAIN scene reloads restart matches); phase flow
    **LOBBY→TRAVEL→HIDE→SEEK→RESULT** (2026-07-16 lobby feature, mirrors the original):
    everyone spawns in the LobbyRoom, bots trickle in like a matchmaking queue
    (roster HUD top-right, `[H]` = volunteer), volunteering is **Roblox-style: stand
    on the red hunter pad** (0.25s position tick; no button), hunter = roulette pick
    from pad-standers (else anyone), hiders teleport into the map behind a
    LoadingScreen while the **hunter waits in the lobby** until hide time ends, then
    travels in with a red loading screen ("HUNTER INCOMING!" warning for hiders);
    conversion + win logic, HUD. Also owns **style scoring** (original's rule:
    0.5s LOS tick — points for being inside a hunter's view cone+LOS ≤14m, closer =
    faster, `STYLE n` HUD gold-pulses on gain; result screen shows YOUR STYLE SCORE +
    top-3 BOLDEST HIDERS), `DoTaunt` (emote + floating "!" TextMesh + points by
    proximity + `BotBrain.HearTaunt` suspicion spike ≤18m — the original's whistle),
    and `SpawnDecoy` (one-use hider clone: same variant, `CopySkinFrom` paint copy,
    frozen scenery pose, joins Characters so bot hunters stalk it; shot decoys crumple
    to Dead and pop — no conversion, "A DECOY!" banner for a human hunter). Lobby
    pacing fields (joinInterval*, lobbyCountdownSeconds, travelSeconds,
    botVolunteerChance) are public for tests/config
  - `Character.cs` — `Character.Create()` factory: CharacterController root + paintable
    capsule body child (MeshCollider = paint UVs + pellet hits) + googly eyes
  - `CharacterMotor.cs` — walk/dash/jump/wall-climb/pose (Stand/Crouch/Lie); drivers write
    `desired*` fields; `movementLocked` for phases/paint mode
  - `PlayerRig.cs` — touch: left half joystick / right half camera drag / round buttons
    (DASH, JUMP=hold-to-climb, POSE, context PAINT|SHOOT); WASD+mouse fallback in Editor
    (Shift dash, Space jump/climb, P pose, F action)
  - `SelfPaintMode.cs` — paint your own body: body freezes (movement + animator paused),
    brush sized in WORLD cm converted per-triangle to UV via texel density (character
    meshes are import-flagged readable for this), screen cursor ring previews the stamp,
    pinch/ZOOM buttons/mouse-wheel dolly; palette/PICK/size-indicator/CLEAR/DONE bar
  - `BotBrain.cs` — hiders: run to spot → `FillCamo(surface colour)` → pose → freeze;
    hunters: patrol + per-target suspicion (LOS, movement, pose, camo-vs-background match,
    close-range reveal) → approach → shotgun
  - `DecoyStatue.cs` — scene marker that spawns a painted, posed mannequin at runtime
    (random blocky model, PaintableBody, animator pose; paint modes Stone / MatchGround
    two-point auto-camo / Stripes). Character-shaped scenery = doubt for hunters; decoys
    are NOT Characters, so bot hunters ignore them — they exist to fool humans
  - `LobbyRoom.cs` — the matchmaking lobby, **wired in + play-verified 2026-07-16**:
    runtime-built room floating 55m up / 42m west of the map (pattern walls = camo
    teaser: rainbow N, B/W stripes S, warm/cool tiles E/W, checker floor, crates,
    bench, "PAINT - HIDE - SURVIVE" TextMesh sign). Invisible lid (collider only)
    stops wall-climbers, all renderers `ShadowCastingMode.Off` so the room doesn't
    print a shadow on the map. **Red hunter pad** at the north wall ("STAND HERE TO
    HUNT" TextMesh): `OnPlatform(pos)` / `PlatformSpot()`; volunteer bots path onto it
    and sometimes chicken out (`BotBrain.lobbyVolunteer` + rethink churn).
    `Build(map)` idempotent; `SpawnPoint()` for spawns
  - `LoadingScreen.cs` — the travel overlay ("进地图读条"): paint-roller drags a
    rainbow stroke as the progress bar (drips fall off passed sections, splats pop
    around, tips rotate, drifting diagonal stripes bg); hunter variant runs in reds
    ("THE HUNT BEGINS"). Opaque from frame 1 → teleports happen invisibly; ends in
    white flash + fade. `LoadingScreen.Show(mapName, subtitle, seconds, hunterStyle)`,
    self-destroys; map display names come from `MatchManager.MapDisplayName()`
  - `ThirdPersonCamera.cs`, `Shotgun.cs` (pellet cone, converts hiders), `PaintableBody.cs`
    (runtime tex, `FillCamo`, `AverageColor` for AI), `ArenaMap.cs` (marker + floor
    sampling — since 2026-07-19 `RandomPointOnFloor` PIERCES all geometry in the column
    and reservoir-picks a random valid storey: normal.y filter + `maxSpawnY` rooftop cap
    + 1.3m-headroom capsule check, so basements/upper floors get spawns while wall tops,
    closed voids and furniture interiors are rejected on every map),
    `UiKit.cs` (runtime UI helpers + HoldButton)
- `Assets/Game/Scripts/Game/` — OLD pass-and-play demo (ChameleonPainter/GameFlow/PaintUI…);
  still works in Diorama01; `PaintUI.EnsureEventSystem` is reused by the arena code
- `Assets/Game/Scenes/Arena05.unity` — **the main map** (48×36m "Neighborhood", in build
  settings): road cross + streetlights + zebra crossings, 6 whole suburban houses (scale 7)
  + driveways, 8 parked cars (scale 1 — real-world sized), fenced backyards, SW open-interior
  hut with walkable roof deck, corn garden, orchard, suburban perimeter fence; pattern
  surfaces for camo play (tile patio + angled tile wall, 2 rainbow graffiti walls, 2 B/W
  stripe panels, brick wall); NE park has a **climbable 2.8m rock plateau with steps**;
  SW crate yard + back alley between the SE houses (loop route) + hedge S-curves add
  cover clutter. Density pass 2026-07-16 (from reference-game research): 6 `DecoyStatue`
  markers (2 park statues + fallen statue by the plateau, corn-garden scarecrow, B/W-stripe
  mannequin ON the zebra crossing, auto-camo sitter on the tile plaza), pumpkin patch (6),
  cardboard cluster at the alley's south mouth, roadworks cones around the firetruck,
  flatbed truck + climbable cargo boxes (bed y=0.61) by the N road, plush-bear pile at the
  NW bench, rainbow/stripe panels on the SE houses' BACK walls (long wall-rhythm runs).
  **Crossroad Monument** (user request 2026-07-16): ~8.5m roundabout landmark at (0,0) —
  5 stone cylinder tiers (StoneGray.mat, MeshColliders) + 7 patina-green `DecoyStatue`
  figures built from scaled markers (marker localScale × the 0.5 body scale: caryatid
  ring ×4 @0.87 Scarecrow backs to the column, sentinels ×2 @0.9 Statue on the mid tier,
  crown figure @1.1 Scarecrow on top). Players painting themselves patina and posing on
  the pedestal read as the 8th figure. Edit-mode preview verified (clip-sampled poses);
  play-mode spawn NOT yet verified — was blocked by concurrent taunt/LOS work's compile
  errors, re-verify once that lands.
  `ArenaMap` overrides: 9 characters, hide 50s, seek 210s,
  `floorNormalMinY 0.85` (no spawns on sloped house roofs)
- `Assets/Game/Scenes/Arena06.unity` — **second map "The Mansion"** (48×42m, in build
  settings, built 2026-07-19; verticalized same day at user request: **basement + ground
  floor + 2nd floor + full roof/ceilings**). GROUND (40×28 interior): checker **ballroom**,
  now double-height with a stage + CurtainRed backdrop (0.55m lane behind) and a
  **balcony level at y3.2** (two 12-step stairs w/ big under-stair dens, bridge over the
  stage, B/W stripe panels below); parquet **library** (bookcase lanes, walk-in brick
  fireplace); TileBlue **kitchen**+pantry (counter run, island, pantry closet) with a
  **stairwell down**; tall WoodWarm **dining** (table + 8-chair + plate families);
  CarpetRose **bedroom** (2 wardrobes), TileGrout **bathroom** (lie inside the tubs);
  double-height concrete **storage** (crate rows, 4 lockers, crate fort) with the second
  **stairwell down**. 2ND FLOOR (west wing + east wing, floors 3.2..3.49, connected via
  the ballroom balcony): office **study** (desks/PCs — Backrooms vibe), **kids room**
  (beds, bear pile on rug), junk **attic** (cardboard/crate clutter, Dead-pose mannequin
  decoy), **master suite** (walk-in wardrobe) + **lounge**. BASEMENT (28×18 brick, floor
  −3.0, dim point lights = the Sewer-style silhouette zone): **wine cellar** (barrel
  family ×8 + a lying decoy), central corridor, **boiler room** (tank + pipes + crates),
  plus a **1.45m air-duct crawl** secretly linking cellar↔corridor. Roof slab at 6.4;
  interiors lit by ~19 point lights + flat ambient 0.45. Crawl-ins: lockers, wardrobes
  ×3, pantry, fireplace, under-stair dens, crate fort, tubs, curtain lane, air duct.
  8 DecoyStatues total. Sloped closet/locker tops (33°) + `maxSpawnY 5` keep spawns off
  tops/roof. **Art pass 2026-07-19** (user feedback: too empty/monotone, bare ceilings,
  no graffiti, clipping): coffered-ceiling skins (`CeilingCoffer` texture) + wood beams
  in every room; 10 graffiti walls from 3 generated irregular textures (`SplatMural`
  paint splats w/ drips, `DoodleArcs` scribbles on dark, `RainbowDrip` dripping bands) —
  ballroom under-balcony, storage, basement corridor, attic, kids room, and two 10m
  murals on the courtyard face; set pieces: **grand piano** (ballroom), 4 **balloon
  bunches**, **pool table** w/ colored balls (lounge), 7 framed paintings, toy-block
  scatter (kids), **clothesline w/ colored towels** (basement), wine bottles on barrels,
  fruit on tables, 4 crates recolored red/blue/green; dining table/chair clipping fixed
  (desks split, chairs pulled out). Pattern floors are per-room .mat tiling VARIANTS (cube UVs are 0..1 per
  face — bake tiling into `Patterns/<name>_<size>.mat`). `ArenaMap`: 10 chars, hide 55s,
  seek 240s, floorNormalMinY 0.85. Play-verified 2026-07-19 (both passes): hiders
  teleport onto ALL levels (2 straight into the basement — they self-painted concrete
  gray, one took the Lie pose next to the cellar decoy), bedroom bot matched CarpetRose,
  spawn sampling 300-shot audit = 9% basement / 72% ground / 17% upper / 0 roof, zero
  errors. Known quirks: bots path between floors only by accident (stuck-repick), so
  basement hunting pressure is low for bots; humans must check it.
- `Assets/Game/Scenes/Arena04.unity` — 32×24m two-house map, kept as secondary
- `Assets/Game/Scenes/Arena03.unity` — smaller first kit map (16×12m), kept as tertiary
- `Assets/Game/Scenes/Diorama02.unity` — small test arena (BuildingKit slice, same wiring)
- `Assets/Game/Scenes/Diorama01.unity` — old demo scene (desk diorama)
- `Assets/Game/Art/Kits/` — Kenney CC0 kits (see Art/CREDITS.md): `BuildingKit2` (textured
  original, 2m grid, wall 2.4h), `FurnitureKit` (**10× oversized — place at scale 0.25**),
  `NatureKit` (small — trees/rocks at scale 2–3), `SuburbanKit`/`RoadKit` (miniature —
  **place at scale 7**; 21 whole houses building-type-a..u, 1×1 road tiles), `CarKit`
  (real-world scale — place at 1), `FoodKit` (cartoon-oversized — **place at 0.35**;
  added 2026-07-19 for Mansion kitchen/dining clutter). Source: user's local Kenney All-in-1 3.4.0 at
  `C:\ClaudeWorkSpace\Assets\Kenney Game Assets All-in-1 3.4.0` (50 more 3D kits there:
  Castle, Fantasy Town, Pirate, Graveyard, City Commercial/Industrial… for future maps).
  URP materials auto-generated on import.
- `Assets/Game/Resources/Characters/` — Kenney Blocky Characters: 18 rigged FBX (a–r),
  **27 anim clips each** (idle/walk/sprint/sit/static/die/emotes/holding-both-shoot/…),
  six rigid box parts per body (NOT skinned → plain MeshColliders work for painting),
  per-character 1024² skin textures; loop-time set on locomotion/pose clips via importer
- `Assets/Game/Resources/CharacterAnimator.controller` — shared controller built by editor
  script; params `Speed/Pose/Aiming/Shoot`; clips referenced from character-a (bone paths
  identical across variants). Character rig is 2.7m tall → **body child scaled 0.5 =
  1.35m** (user sizing call 2026-07-15; CC 1.35/0.24, EyePos 1.15, cam pivot 1.1).

## Conventions / decisions

- All UI + characters are built in code at runtime; scenes only hold geometry + markers.
- Texture painting batches to `Color32[]`, uploads once per frame in `LateUpdate`.
- Surface colour sampling goes through `PaintableBody.SampleSurfaceColor(hit,…)`: reads the
  texture pixel at hit UV (Kenney colormaps keep colour in the texture, `_BaseColor` stays
  white) with material-tint fallback. Non-readable textures get a blit-once CPU copy via a
  static `ReadableCache`, so NEW kits work without touching import flags. Pattern surfaces
  (`Assets/Game/Art/Patterns/`: TileGrout/BWStripes/Rainbow/Brick, generated PNGs+mats) must
  use **MeshColliders** — `textureCoord` is zero on Box/primitive colliders.
- Patterned-surface hiding is the design meta (user call 2026-07-15, mirrors the Steam
  game): tiles/stripes/graffiti walls let players paint pattern-matching camo. Bots detect
  patterned ground (two-point sample, colour diff > 0.25) and use `FillStripes` camo.
- Poses (9): Stand / Crouch(sit clip) / Statue(static) / Lie(static + body tipped -90°,
  procedural, offset scales with rig) / Scarecrow(holding-both) / Chair(drive clip) /
  **Ball**(sit clip + procedural hands-on-head bone offsets in `CharacterMotor.LateUpdate`
  — head +45°X, arms Euler(-165,0,±30) folding INWARD, tuned visually) / **Dead**(die
  clip, freezes crumpled on last frame) / **Bend**(plays `Resources/BendHold.anim`, a
  1-frame clip baked from pick-up's deepest-stoop phase — **cycleOffset does NOTHING
  when state speed is 0**, the freeze never left frame 0, so bake a hold clip instead).
  Pose int drives animator; movement breaks the pose. Touch UI: POSE button opens a
  2-column 9-option picker (PlayerRig `_posePanel`); editor P key cycles.
  **2026-07-19 pose regression fixed**: an importer pass had left every clip's Root
  Transform un-baked (`lockRootRotation/PositionXZ/HeightY = false`) — on Generic rigs
  the root node's translation is then EXTRACTED as root motion and discarded
  (applyRootMotion=false), so sit/crouch/ball couldn't lower the body and "poses did
  nothing, characters just stood" (user report). All 18 FBX importers now bake all
  three root channels into the pose; walk/sprint are authored in place so nothing
  slides. All 9 poses re-verified visually in play (decoy line-up screenshots).
  `DecoyStatue` builds in **Start** (not Awake) so runtime spawners can AddComponent +
  set fields on the same frame — Awake fired before field assignment and every
  runtime decoy silently used default Statue/Stone. **Idle state speed
  is 0** in the controller (user call 2026-07-19) — standing characters hold the idle
  clip's entry frame perfectly still; the breathing clip ruined posed hiding. Same
  freeze trick as Bend (state speed 0), so walk/run transitions still blend normally.
- Character skins are **pure white, no source texture, no eyes** (user call 2026-07-14).
  The Kenney blocky FBX UVs are unusable for painting (mirrored limbs, shared verts, and
  the head is a 10x mesh on a 0.1x bone), so `PaintableBody` REBUILDS UVs at runtime:
  split vertices per triangle, planar-projected per box face into a 6x6 atlas (part=row,
  face=col), planar coords multiplied by each part's `lossyScale` so ONE texel density
  applies everywhere (verified: 0.216 UV/m on all parts; 3cm brush = 7px on 1024).
- Playtest feedback round (2026-07-14): furniture standard scale is now **0.25** (calibrated
  vs the 0.6-scaled 1.62m character), paint brush is UV-small (default 0.02, SeamJump 0.08 —
  blocky skins have small per-face UV islands, fat brushes flood a whole face), **hunters are
  first-person** (`ThirdPersonCamera.firstPerson`, own renderers hidden, fire from camera),
  and aiming lives on an arms-only Animator layer (`ArmsMask`) so legs keep walking.
  Since the lobby feature: FPS only during SEEK/RESULT (`PlayerRig.UpdateViewMode`) — in
  the lobby the hunter stays third-person to see themselves turn red and get the gun;
  camera near plane clamped to 0.05 so FPS doesn't clip into walls you stand against.
- Gesture rules: strokes starting on a paintable body belong to the brush; gestures starting
  over UI belong to UI (`UiGuard`); everything else is camera.
- ASCII-only UI labels (LegacyRuntime.ttf tofu risk on device).
- Poses only transform the visual body child; the CC hitbox stays upright (accepted for now).

## UI redesign handoff (2026-07-21)

- `Docs/UI/UI-Redesign-Requirements.md` — the full UI visual-redesign requirements doc
  for Claude Design (constraints, per-screen element inventory with exact values,
  13 pain points, deliverable spec, style direction) + `Docs/UI/shots/` = 11 live
  1080×1920 screenshots covering every UI state, generated by
  `Assets/Editor/UiShotDirector.cs` (menu `Tools/UI Shots/Run Full Session`, or drop
  `Docs/UI/shots/_arm.txt` containing `autoplay` and let the next domain reload run it).
  Re-run it after the redesign lands for before/after comparisons.
- **MatchManager net bridge (added same day)**: the WIP netcode in `Arena/Net/` expected
  `MatchManager.Instance / Register / Unregister / AdoptLocalPlayer / AttachNet /
  Request{Taunt,Decoy,Hit} / SpawnTauntMarker / OnLocalScore / OnNet{Banner,HunterReveal,
  Result}` — these now exist as offline-safe implementations (offline falls through to
  the local fast path, behavior unchanged) so the project compiles again. The netcode
  workstream should review the `Request*`/`OnNet*` semantics before shipping online play.

## Reference-game level design (researched 2026-07-16, apply to all maps)

Original has 7 maps (Mansion, Sewer, Backrooms, Indoor Country, Penguin Hotel, Sugar Land,
Osaka — mostly INDOOR diorama-like, small; the smallest, Osaka, is the most liked).
Principles worth copying:
1. **Character-shaped props everywhere** (horse/penguin statues, scarecrows, plushes,
   fallen statues) — a posed hider reads as "the N+1th prop". Ours: `DecoyStatue`.
2. **Prop families / repetition** ("repeated props, merciless colors") — rows of pumpkins,
   crates, cones, trash bags; duplication creates camouflage confusion.
3. **Zone identity** — each map plays as themed zones w/ own palette + prop inventory,
   joined by narrow connectors; players "commit to one room family per round".
4. **Wall rhythm continuity** — long patterned runs (tiles/graffiti/stripes) reward
   painting yourself as a continuation of the pattern.
5. **Verticality & perches** — mezzanine sightlines, sign perches, truck beds; hunters
   rarely look up.
6. **Hide in the OPEN** — LOS scoring means good spots are visible-but-unnoticed stage
   areas facing hunter traffic paths, not occlusion corners.
7. **Silhouette vs colour** — dim zones (Sewer) test shape; monotone zones (Backrooms)
   make colour free and shape expensive. Lighting variety is a camo axis (future maps).

## Unity MCP + editor quirks (hard-won)

- `.mcp.json` = uvx `mcpforunityserver==9.7.1`; editor package pinned in manifest.
- **The editor barely pumps play-mode frames without OS focus** (frameCount freezes; MCP
  still works because it pumps the main thread per command). For play tests: run
  `Tools/focus-unity.ps1 -WaitSeconds N` (SetForegroundWindow + Alt-tap workaround;
  checked into the repo so it survives sessions),
  or ask the user to click Unity. Console **Error Pause must stay off** — MCP bridge logs
  socket errors that otherwise pause play instantly (disable: `LogEntries.SetConsoleFlag(4,false)`).
- Don't request script compiles while in play mode — the mid-play domain reload wipes
  runtime state. Stop play first.
- `manage_camera` screenshots go through the camera path → **Screen-Space-Overlay UI is
  missing** from captures; verify UI via hierarchy/execute_code instead.
- execute_code compiles with CodeDom (C#6): no `?.` on Unity objects habit anyway, and use
  `UnityEngine.Object.DestroyImmediate` (bare `Object` is ambiguous).
- **⚠ 2026-07-19 (PM/QA): `execute_code` is BROKEN since the netcode packages landed** —
  CodeDom's mono.exe invocation now exceeds the Windows command-line length limit
  ("文件名或扩展名太长", the reference list grew with NGO/Multiplayer SDK assemblies), and
  the `compiler:"roslyn"` fallback needs Microsoft.CodeAnalysis which isn't installed.
  Until fixed: use file-based script workflows + built-in MCP tools (manage_scene /
  manage_gameobject / manage_components / read_console / manage_editor). Fix options are
  with the user (install Microsoft.CodeAnalysis, or bump the MCP package if it gains
  response-file support). **Proven workaround (2026-07-21)**: write a real editor script
  under `Assets/Editor/` and drive it with the **arm-file pattern** — an
  `[InitializeOnLoadMethod]` bootstrap checks a marker file (add the word `autoplay` to
  make the post-compile domain reload enter play mode by itself), runs an
  `EditorApplication.update` state machine, and cleans up after itself.
  `Assets/Editor/UiShotDirector.cs` is the reference implementation (self-driving
  play-mode UI screenshot session; stall guard counts GAME time so an unfocused editor
  pauses instead of aborting).

## Offline compile check (no Unity focus needed)

```bash
U="C:/Program Files/Unity/Hub/Editor/6000.0.77f1/Editor/Data"
"$U/NetCoreRuntime/dotnet.exe" exec "$U/DotNetSdkRoslyn/csc.dll" -nologo -noconfig -nostdlib -target:library -out:"$TEMP/check.dll" \
 -r:"$(cygpath -w "$U/NetStandard/ref/2.1.0/netstandard.dll")" \
 -r:"$(cygpath -w "$U/Managed/UnityEngine/UnityEngine.CoreModule.dll")" \
 -r:"$(cygpath -w "$U/Managed/UnityEngine/UnityEngine.PhysicsModule.dll")" \
 -r:"$(cygpath -w "$U/Managed/UnityEngine/UnityEngine.UIModule.dll")" \
 -r:"$(cygpath -w "$U/Managed/UnityEngine/UnityEngine.TextRenderingModule.dll")" \
 -r:"$(cygpath -w "$U/Managed/UnityEngine/UnityEngine.IMGUIModule.dll")" \
 -r:"$(cygpath -w "$U/Managed/UnityEngine/UnityEngine.AnimationModule.dll")" \
 -r:"Library\ScriptAssemblies\UnityEngine.UI.dll" \
 -r:"Library\ScriptAssemblies\Unity.InputSystem.dll" \
 -r:"Library\ScriptAssemblies\Unity.Netcode.Runtime.dll" \
 -r:"Library\ScriptAssemblies\Unity.Collections.dll" \
 -r:"Library\ScriptAssemblies\Unity.Networking.Transport.dll" \
 Assets/Game/Scripts/Game/*.cs Assets/Game/Scripts/Arena/*.cs Assets/Game/Scripts/Arena/Net/*.cs
```
(netcode refs + `Net/*.cs` required since 2026-07-21's net bridge; add `-nowarn:0618`
to mute NGO's RequireOwnership deprecation warnings if noise bothers you)

## Verified E2E (2026-07-14, play mode via MCP)

- Diorama02: full loop — spawn → random roles → bots hide/camo/pose → hunter suspicion →
  shots → infection cascade incl. the human player → HUNTERS WIN → scene-reload restart.
- Arena03 with blocky characters: animation states drive correctly (Aim for hunters, Walk/
  Sit for hiders), bots climbed the mezzanine stairs on their own, garden bot camouflaged
  to exact grass green after the UV-sampling fix, cascade converted a bot mid-seek, and a
  timed-out round ended HIDERS WIN. Both endings observed.
- Human touch controls & paint-mode UX still need a hands-on device/editor test.
- Arena05 density pass (2026-07-16, play mode): all 6 decoys spawn painted + posed
  (Scarecrow/Chair/Sit/Static states confirmed), zebra mannequin + tile-plaza auto-camo
  sitter look right in screenshots, bots settle/patrol normally around the new clutter,
  zero game errors in console.
- Lobby flow (2026-07-16, 4 play runs on Arena05, screenshot-verified): staggered bot
  joins + roster + [H] markers, roulette (roster hides for the reveal), rainbow loading
  screen for hiders / red one for the hunter, hunter waits in the lobby third-person
  (WAIT button, "SEEK IN n"), teleports (CC disable/enable) land everyone on the map,
  in-play scene reload (PLAY AGAIN path) restarts the whole lobby loop cleanly. Player
  verified on BOTH teams end-to-end.
- Hider-abilities batch (2026-07-16, 2 play runs): hunter pad volunteering works for
  player + bots (bot MANGO walked onto the pad and was picked), LOS scoring accrued
  organically (FERN 16 / You 7 / PIXEL 3), taunts gave points + made a bot hunter
  investigate, decoy cloned the player's stripes (variant-matched), drew the list into
  bot scanning, and popped to a crumple on Convert without infecting, spectate (EYE)
  followed the hunter's eyes and auto-cancelled on phase change, result scoreboard
  renders top-3. Ball/Dead/Bend animator states + Ball's procedural head-hug verified
  by screenshot; taunt "!" marker logic verified in code path (visual shot missed the
  1.2s window — recheck casually next hands-on session).

## Roadmap (reordered 2026-07-14: user wants rich, good-looking levels first)

1. **Level beauty pass** (continue): doors/roof details, lighting + postprocessing (bloom,
   vignette), decor density, maybe a second themed map; fix furniture clipping into walls
2. **Hands-on feel pass** (human): joystick/camera tuning, dash/climb feel, paint-mode UX;
   character polish — jump/fall blends (blocky set lacks those clips), die anim on
   conversion, `AverageColor` alpha-0 fix
3. Hider fun: ~~taunt button~~ ~~LOS style scoring~~ ~~hunter spectate~~ ~~decoy~~
   (all shipped 2026-07-16) — remaining: respot-during-seek risk,
   spectate-after-conversion (dead players watch the hunter), taunt SFX (whistle)
4. Match config screen (bot count, timers — natural home: the lobby, whose pacing fields
   are already public); SFX/juice (shot, conversion sting, win fanfare)
5. Android build via `unity-android-release` skill; then real netcode (NGO or Photon) to
   replace bots with players
