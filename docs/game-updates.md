# After a GHPC update

GHPC is a Unity Mono game with no obfuscation, so the mod patches its classes with Harmony and reads private members
through reflection. An update that renames or removes one of those breaks the feature that uses it and nothing else.
This page is how to find what broke and fix it.

## Start with the offline check

```
powershell -ExecutionPolicy Bypass -File tools\check-game.ps1 -GameDir "<GHPC folder>\Bin"
```

It builds the mod and looks every patch target and every `Reflect` member up in the new game's DLLs, without starting
the game. Anything gone shows as `MISSING` with the file it's used in. Names the code only knows at run time are in
`tools\check-game-list.txt`, and the script says `NOT COVERED` if the code gains one the list doesn't have. The
RepairKit patch isn't checked because it hooks another mod, not GHPC.

Then, in this order:

1. Build against the new `Assembly-CSharp.dll` and fix the compile errors. Those are public members and the
   compiler points at every one.
2. Fix whatever `check-game.ps1` lists.
3. Start the game and read `MelonLoader\Latest.log` from the top. Harmony errors and `Reflect` warnings come before the
   main menu.
4. Check the T-90A is in Instant Action, the mission customiser and the campaign.
5. Spawn one and read the log again. Conversion runs at spawn, so most warnings only show up here.
6. Go through the test list at the bottom.
7. Leave the mod on and play a mission with a vanilla T-72M1 and T-80B in it. Some patches touch every unit.

## What it looks like in the log

- `Game field X.y not found: a game update may have changed it, ...` comes from `Reflect`. The word after "Game" can
  also be `method`, `property` or `property setter`. Once per session per member. `can't be read as bool (...)` means
  the field is still there but its type changed. `can't be looked up (...)` usually means a method gained an overload.
- `<class> could not be patched (a game update?), so that part of the mod is off: ...` at startup names the patch
  class whose target went, usually with `Undefined target method`. The other classes still go in.
- `<feature> failed (the game carries on without it this time; 1 of 3 logged)` is `PatchGuard`. The patch ran and the
  code inside threw. The stack trace has the line. It goes quiet after three.
- `Unity exception in our code: ...` is `UnityErrors`, an exception in one of the mod's behaviours or coroutines.
- `T-90A modules: ...` and `T-90A ammunition self-check ...` list what they couldn't find. The ammo one ends by asking
  for the log, ignore that bit.
- `donor prefab 'T80B' not in the unit registry` or `vanilla material '...' not found`: a borrowed part couldn't be
  copied.
- `T-90A: no ...` (armour values, rig map, smoke points) isn't a game update. A text file has gone missing
  from `Resources\`, or didn't get embedded.

A missing reflected member is looked up once and cached, and the feature code checks for null and skips, so a rename
shows up as a feature that's gone quiet, not a crash. The one exception is `TraverseLimits`, which wraps
`AimablePlatform.enforceEulerLimits` in a `MethodDelegate` when the class loads. If that method is removed the limit is
just skipped, but if it changes its parameters or return type the delegate throws and the whole class goes with it.

Things that break most often, roughly in order: a private field or method renamed, a patched method's parameters
changed, a public member renamed (compile error), a prefab or material renamed, and last, nothing renamed but the game's
behaviour around the mod changed. That last one gives no log line at all. You only see it by playing.

## Fixing a renamed member

Open the game's `Assembly-CSharp.dll` (`GHPC_Data\Managed`) in a .NET decompiler, find the class from the warning and
look for the same kind of member with a similar type and name. Find Usages on the old name's neighbours usually gives
it away. For a method, match the parameter types and read the body. Decompiling the previous game version and diffing
the class is the quickest way if you still have it. Change the string or signature, build, and test that feature.

Most of the hooks are in `Common\`, which is sealed, so the seal test fails once you've changed one. In a fork that's
expected, and [MAINTAINING.md](../MAINTAINING.md#layout) says how to re-seal it.

If a whole mechanism has gone (say `LiveRound.penCheck` is split up), the code using it has to be rethought, not
renamed.

## If GHPC moves to a new Unity version

The bundle was built with Unity 2022.3.62f2, the version GHPC runs on, and a bundle has to match the game's Unity
version to load. If that changes, expect `T-90A bundle could not be loaded: <path>` and no `Loaded T-90A visuals` line.
The mod still runs but the T-90A keeps the T-72M1's model and armour. The fix is a new bundle built against
[bundle.md](bundle.md). The code doesn't change.

## Where the mod hooks in

Files are in `Common\` unless they say `T90A\`. Each group ends with what you see in game when it breaks.

**Unit lists and spawning.** `UnitRegistry.cs` patches `MissionConfigMenuPanel.GetUnitKeysByClass`,
`UnitSpawner.GetMetadataByUniqueName` and `UnitSpawner.SpawnUnit`, clears the spawner's and customiser's name caches,
and sets `Unit._uniqueName` and `_friendlyName`. Broken: the T-90A is missing from a list, spawns as a plain T-72M1, or
shows as T-72M1 in the HUD. A unit the spawn patch misses is still caught by the sweep in `ConvertSweep.cs`.

**Ammunition and the menus.** `PortAmmo.Menu.cs` patches the customiser's ammo dropdown and the campaign's logistics
and unlocks, `PortAmmo.Spawn.cs` the restock check, `PortAmmo.cs` the tandem ERA rule on `LiveRound.penCheck`.
`LoadoutManager.RegisterAllBallistics` is called after the ammo list is swapped. Broken: the customiser shows the
T-72M1's rounds, 3BM60 turns up from mission one, or rounds fly with no ballistics. The self-check says what's missing.

**Gun and autoloader.** `GunSetup.cs` rescales `AmmoFeed`'s reload stages and copies the gun's codex entry before
renaming it (every T-72M1 shares it). `CrewVoices.cs` lets one "AZ" callout through per carousel turn. Broken: reload
time is the T-72M1's, every T-72M1 gets the T-90A's gun name, or the loader shouts "AZ" several times per reload.

**Crew voice.** `CrewVoices.cs` sets `CrewVoiceHandler._chassis` and `_reloadType` on the copied T-80B voice and points
`Unit._crewVoiceHandler` at it. Broken: German crew, or a Russian crew that doesn't react to driving.

**Gunner's sight and fire control.** `GunnerFcs.cs` patches `FireControlSystem` (`AdjustAimByRatio`, `Update`,
`FiringBlocked`, `UpdateLaserAim`, `DoLase`). Its `Resync` rewrites the fields the FCS caches in Awake and
DeferredStart. `ReticleMeshes.cs` clears `ReticleMesh.SMR` for every reticle swap. `ThermalSight.cs` patches
`PlayerInput.Update` and two `CameraManager` methods for the night toggle. Broken: the sight swings past its stops, the
gun fires before it's laid, reticles don't draw or keep the old one, the night key does nothing with a dead day sight.

**Shtora and smoke.** `Shtora.cs` patches `LiveRound.DoUpdateImpl` (GHPC has no missile tracker: every guided round
turns towards `GoalVector` there) and the vehicle menu. `ShtoraScreen.*.cs` patch `FireControlSystem.DoLase`,
`UnitAI.SetTarget`, the menu and `WeaponHud.Update`, and read a dozen `VehicleSmokeManager` and `Grenade` fields to
fire the 3D17s. `SmokeLaunchers.cs` patches `VehicleSmokeManager.FireSmokes` to rebuild the crew brains a runtime
spawn never got. Broken: no jammer, no laser warning, no screen, or the smoke launchers never fire.

**The Kord.** `RemoteMg.cs` patches `PlayerInput.Update`, three `CrewBrainWeaponsModule` methods (picked in code) and
four `CameraManager` methods, and writes the unit's camera slots, platforms and the Kord's rack. Broken: free-look in
the commander's seat fires the Kord at the cursor, the station has no view or no ammo, or the camera lands on the wrong
slot.

**Armour and rounds.** `EraBricks.cs` patches `LiveRound.penCheck` and `UniformArmor.Detonate()` by hand (both or
neither, and the log says `ERA bricks: patching failed`). `DmArmourApply.cs` sets `UniformArmor._armorType`, `_isEra` and
friends and `VariableArmor._armorType`. Broken: ERA behaves as GHPC's own, or every piece takes the game's default
armour material, which is the biggest effect of anything on this page.

**Rig, modules and heat.** `Followers.cs`, `PivotMove.cs`, `TiltedRing.cs`, `TraverseLimits.cs` and `MantletCover.cs`
use `LateFollow` shifts and `AimablePlatform._originalLocalRotation`. `ModuleBuilder.cs` sets `DestructibleComponent`
and `UniformArmor` fields from the module list. `ThermalHeat.cs` writes `HeatSource.heat` and clears its renderer list.
`Mobility.cs` writes the top speeds. Broken: parts displaced or crooked, modules with the wrong health, parts black in
white-hot, the T-72M1's speeds.

**Other mods.** `RepairKitCompat.cs` patches RepairKit's `Apply(Unit, NwhChassis)` if it's installed. A rename there
logs `RepairKit found, but not its repair method`. Nothing to do with GHPC updates.

## Names looked up as strings

These don't go through `Reflect`, so a rename gives no warning of that shape.

`tools\check-game.ps1` reads the literal names in `Reflect.*`, `AccessTools.Method`, `Traverse.Create(x).Field`,
`Type.GetField` / `GetMethod` and the hit-zone table of `ModuleBuilder`, and looks each one up in the game's DLLs. It
cannot check a field enumeration (`GetFields`, as in `PortAmmo.MakeClip` or `RemoteMg.Platform`) or a lookup whose
name is a variable: after a game update read those against the class by hand. The script prints every place they are.

The rest, which no script reads:

- **T-72M1 rig** (`BaseRigT72M1.cs`): the `---MESH---/HULL/TURRET/GUN` bones, the renderers it hides, and the hull
  skin, which is swapped for `Resources\T72M1_skin_hull_trimmed.bin` and has to match the game's vertex count. If
  GHPC changes that mesh the log says `bones collapsed only`.
- **The T-72M1 armour War Thunder's model replaces** (`BaseRigT72M1.ImportReplaces`, plus `Fire Wall` in
  `T90A\Armour.cs`). A renamed one stays switched on inside the T-90A's armour, and the armour warning names it. Some
  carry GHPC's own trailing space (`Hull Roof 30mm `). Keep it.
- **Donors**, copied at the main menu by `DonorParts.cs` because VanillaAssetUtil can destroy the prefabs after a
  mission. They are `T80B` (voices, sight sounds, the Buran-M) and `T72M1` from the registry, the first unit whose key
  starts `T64` for the Kord's station (`RemoteMg.cs`), `M60A3TTS` for the ESSA's thermal screen (`ThermalSight.cs`),
  and the first of `LEO2A3`, `M60A3`, `LEO1A3` or `M1` for the 3D17's white cloud (`ShtoraScreen.cs`, which falls back
  to the 3D6's dark one). The glass donors are the last column of `material_roles`.
- **Shtora glow and heat** (`T90A\Countermeasures.ShtoraGlow.cs` and `.ShtoraHeat.cs`): the T-72M1's `Luna visuals`,
  `Luna visuals 2` and `IR light source`, the shader `Legacy Shaders/Particles/Additive`, and `GHPC/VehicleShader` with
  its `_PaintMask`, `_ScorchAmount` and `_FLIR_Emission`. A missing object or shader gets a warning. A renamed shader
  property gets nothing, and the emitters just stay cold in thermal. The `T-72M1 Luna out` step switches off
  `---ELECTRICS SCRIPTS---/spotlight` (`BaseStrip.cs`).
- **Ammunition** (`T90A\Firepower.cs`, `Common\Heat125.cs`): clips `clip_3BM32`, `clip_9M112M` and `clip_3BK18M` from
  the T-80B's list. All three HEAT rounds are copies of `clip_3BK18M`. The 3BK31's ERA cost goes through
  `LiveRound.RhaPenetrationOverride`, and built-in ERA is told apart by the armour's name (Kontakt-5, Relikt): if
  another mod renames its blocks, 3BK29M treats them as add-on.
- **3BK31's second jet** (`Common\SplitCharge.cs`): a postfix on `LiveRound.logHit` (reads `_jetActive`,
  `_jetStartPoint`, sets `_armed`) launches it as a child round, and a postfix on `AarController.createShotTrace` (its
  `_shotTraces`) draws it as a jet. If `logHit` goes, 3BK31 is back to one jet. If the trace list goes, the second jet
  shows as fragment lines.
- **Tandem look** (`Common\TandemLook.cs`): `LiveRound.doImpactVFX` and `ParticleEffectsManager.CreateImpactEffectOfType`
  hold back a tandem round's impact effect for the precursor's pop (`_fusedStatus`, `_materialHit`, `HeatJet`), and
  `LiveRound.logHit` adds the precursor's recap line, coloured through `ShotTraceVisual.jetColor`. If those move, the
  rounds just look like plain HEAT again.
- **HEAT fragments** (`Common\BehindArmour.cs`): a postfix on `LiveRound.getSpallMaxAngle` widens the cone of the
  tank's own HEAT off the first hull or turret plate it holes (up to 80°, full on 50 mm or more), and a prefix and
  postfix on `LiveRound.penCheck` (`_jetActive`, `_jetStartPoint`, `_penetrationLevel`, `createSpall`) give 3BK31's
  second jet the fragments GHPC skips for it. If they go, a strong jet holes a tank cleanly and throws most of its
  fragments out the far side again.
- **Fonts and materials**: `Soviet7SegmentFont SDF`, fonts starting `TPD_Etch`, `Standard (FLIR)`, and
  `see-through teal gray` for the x-ray (no log line if that one goes).
- **Scenes and input**: the menu scenes in `MenuScenes.cs`, `TR01_showcase`, and the buttons `Toggle Night Sight` and
  `Smoke Grenades`. A renamed button silently does nothing.

## Test list after a repair

1. Start the game and read the log from the top: no `Game field`, no Harmony error.
2. T-90A in Instant Action and the customiser. Spawn one and read the log.
3. Drive, traverse, elevate. Lase and fire. One "AZ" callout per reload.
4. Both night sights, then again with the day sight shot out.
5. Shtora: lase it from the front, check the panel, tone and screen, the jammer in the menu and the smoke key.
6. Commander's seat and the Kord: it moves, fires, and the camera cycles through the slots.
7. Shoot it with an APFSDS, a HEAT and a missile and read the damage report.
8. Traverse, then watch the AAR replay.
9. A vanilla T-72M1 and T-80B in the same mission, untouched.
