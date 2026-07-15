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
    `sceneLoaded` hook so PLAY AGAIN scene reloads restart matches); spawns 1 player +
    N bots in code, random hunter, INTRO→HIDE→SEEK→RESULT, conversion + win logic, HUD
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
  - `ThirdPersonCamera.cs`, `Shotgun.cs` (pellet cone, converts hiders), `PaintableBody.cs`
    (runtime tex, `FillCamo`, `AverageColor` for AI), `ArenaMap.cs` (marker + floor sampling),
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
  cover clutter. `ArenaMap` overrides: 9 characters, hide 50s, seek 210s,
  `floorNormalMinY 0.85` (no spawns on sloped house roofs)
- `Assets/Game/Scenes/Arena04.unity` — 32×24m two-house map, kept as secondary
- `Assets/Game/Scenes/Arena03.unity` — smaller first kit map (16×12m), kept as tertiary
- `Assets/Game/Scenes/Diorama02.unity` — small test arena (BuildingKit slice, same wiring)
- `Assets/Game/Scenes/Diorama01.unity` — old demo scene (desk diorama)
- `Assets/Game/Art/Kits/` — Kenney CC0 kits (see Art/CREDITS.md): `BuildingKit2` (textured
  original, 2m grid, wall 2.4h), `FurnitureKit` (**10× oversized — place at scale 0.25**),
  `NatureKit` (small — trees/rocks at scale 2–3), `SuburbanKit`/`RoadKit` (miniature —
  **place at scale 7**; 21 whole houses building-type-a..u, 1×1 road tiles), `CarKit`
  (real-world scale — place at 1). Source: user's local Kenney All-in-1 3.4.0 at
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
- Poses (6): Stand / Crouch(sit clip) / Statue(static) / Lie(static + body tipped -90°,
  procedural, offset scales with rig) / Scarecrow(holding-both) / Chair(drive clip).
  Pose int drives animator; movement input breaks the pose. Touch UI: POSE button opens
  a 6-option picker strip (PlayerRig `_posePanel`); editor P key cycles.
  **NOT yet play-verified: pose picker UI + Scarecrow/Chair states (added right before
  the 2026-07-15 GitHub push) — verify first thing next session.**
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
- Gesture rules: strokes starting on a paintable body belong to the brush; gestures starting
  over UI belong to UI (`UiGuard`); everything else is camera.
- ASCII-only UI labels (LegacyRuntime.ttf tofu risk on device).
- Poses only transform the visual body child; the CC hitbox stays upright (accepted for now).

## Unity MCP + editor quirks (hard-won)

- `.mcp.json` = uvx `mcpforunityserver==9.7.1`; editor package pinned in manifest.
- **The editor barely pumps play-mode frames without OS focus** (frameCount freezes; MCP
  still works because it pumps the main thread per command). For play tests: run
  `scratchpad/focus-unity.ps1 -WaitSeconds N` (SetForegroundWindow + Alt-tap workaround),
  or ask the user to click Unity. Console **Error Pause must stay off** — MCP bridge logs
  socket errors that otherwise pause play instantly (disable: `LogEntries.SetConsoleFlag(4,false)`).
- Don't request script compiles while in play mode — the mid-play domain reload wipes
  runtime state. Stop play first.
- `manage_camera` screenshots go through the camera path → **Screen-Space-Overlay UI is
  missing** from captures; verify UI via hierarchy/execute_code instead.
- execute_code compiles with CodeDom (C#6): no `?.` on Unity objects habit anyway, and use
  `UnityEngine.Object.DestroyImmediate` (bare `Object` is ambiguous).

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
 Assets/Game/Scripts/Game/*.cs Assets/Game/Scripts/Arena/*.cs
```

## Verified E2E (2026-07-14, play mode via MCP)

- Diorama02: full loop — spawn → random roles → bots hide/camo/pose → hunter suspicion →
  shots → infection cascade incl. the human player → HUNTERS WIN → scene-reload restart.
- Arena03 with blocky characters: animation states drive correctly (Aim for hunters, Walk/
  Sit for hiders), bots climbed the mezzanine stairs on their own, garden bot camouflaged
  to exact grass green after the UV-sampling fix, cascade converted a bot mid-seek, and a
  timed-out round ended HIDERS WIN. Both endings observed.
- Human touch controls & paint-mode UX still need a hands-on device/editor test.

## Roadmap (reordered 2026-07-14: user wants rich, good-looking levels first)

1. **Level beauty pass** (continue): doors/roof details, lighting + postprocessing (bloom,
   vignette), decor density, maybe a second themed map; fix furniture clipping into walls
2. **Hands-on feel pass** (human): joystick/camera tuning, dash/climb feel, paint-mode UX;
   character polish — jump/fall blends (blocky set lacks those clips), die anim on
   conversion, `AverageColor` alpha-0 fix
3. Hider fun: taunt button (emote-yes/no clips are imported already!), respot-during-seek
   risk, points for line-of-sight hiding (the Steam game's signature scoring),
   spectate-after-conversion
4. Match config screen (bot count, timers); SFX/juice (shot, conversion sting, win fanfare)
5. Android build via `unity-android-release` skill; then real netcode (NGO or Photon) to
   replace bots with players
