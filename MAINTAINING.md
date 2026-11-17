# Maintaining the T-90A mod

For someone who knows C# but hasn't done GHPC modding. The mod is a MelonLoader DLL that adds the T-90A as its own
unit: a copy of the game's T-72M1 that gets converted when it spawns. Model, textures and armour meshes come from an
asset bundle, everything else is the code and data here.

If the log fills with warnings after a game update, go to [docs/game-updates.md](docs/game-updates.md).

## Building

You need Windows (GHPC is Windows-only and the tools here are PowerShell scripts), the .NET SDK (6 or newer, online once
for the 4.7.2 reference assemblies) and GHPC with MelonLoader 0.7.3 or newer, because the project references the game's
DLLs. Start the game once after installing MelonLoader so it sets itself up: `tools\check-game.ps1` reads its
Mono.Cecil.

```
dotnet build -c Release -p:GameDir="<GHPC folder>\Bin"
```

`GameDir` is the folder with `GHPC.exe` and defaults to the usual Steam path. The DLL ends up in `bin\Release\T90A.dll`.
Copy it to `Mods\T90A.dll` in the game.

The bundle (model, textures, armour meshes) is not in this repo and is not built from it: take `Mods\T90A\t90a` from the
latest [release zip](https://github.com/marsattackk/GHPC-T90A/releases/latest). A bundle from before 0.5.0 won't
do, because it still has the old armour pieces. Without the bundle's model the mod loads and the T-90A stays a complete
T-72M1: nothing of the conversion is applied.

The DLL logs a stamp when it loads, like `T-90A 0.4.4+3dac81f.public, game <version>, Steam build <number>`, so a
player's log tells you which build they ran. The version comes from `MelonInfo` in `T90A\Mod.cs`, the commit from
`-p:GitCommit=<hash>` (otherwise `unknown`), the flavour from `-p:BuildFlavour` (default `public`).

## Tests

`tests\` covers the bits that don't need the game: the reticle files' expression language, the reticle JSON, MiniJson,
`dm_armour` parsing, the bundle text numbers, version comparison, the settings migrations, the fire gate, the aim clamp
and the Shtora sector. It compiles those files straight out of `Common\` with stand-ins for MelonLoader, so it runs
without GHPC and GitHub Actions runs it on every push. It also reads what ships in `Resources\`: `ShippedTextTests.cs`
takes `dm_armour.txt` and fails on a line that doesn't read, a mode or material the damage model doesn't know, or a
piece count other than the one at its top (change `Pieces` there when you add or remove a piece on purpose), and
`ShippedReticleTests.cs` does the reticles. CI never builds the mod itself, because that needs the game's DLLs, so build
it against your own copy and run `tools\check-game.ps1` before a pull request.

```
dotnet test tests
```

The two checks run from the repo's folder in PowerShell. `check-game.ps1` builds the mod itself into a temporary folder
(no build is needed first), then looks every game hook of the built DLL up in the game's own DLLs, so it needs the game
with MelonLoader started once (it uses MelonLoader's Mono.Cecil). If the game has never been started it stops with "No
Mono.Cecil.dll under <GHPC folder>\Bin\MelonLoader. Start the game once so MelonLoader sets itself up."
`check-settings.ps1` only reads the source and the two settings tables; it builds nothing.

```
powershell -ExecutionPolicy Bypass -File tools\check-game.ps1 -GameDir "<GHPC folder>\Bin"
powershell -ExecutionPolicy Bypass -File tools\check-settings.ps1
```

A second workflow runs `tools\check-settings.ps1` on every push and pull request, Markdown-only ones included, because
the tests skip those and a README edit is the usual way for the settings tables to drift from the code.

The reticle language is described in [docs/reticles.md](docs/reticles.md), and `ExpressionTests.cs` pins it: read both
before changing `ReticleSpec.Expressions.cs`. Every reticle in `Resources\` is also expanded and compared line by line
with `tests\expanded\`. After changing a reticle on purpose, set `T90A_UPDATE_EXPANDED=1`, run the tests once and check
the diff is what you meant.

## Layout

```
T90A\       the tank itself
Common\     the stamped, sealed code shared with my other ports
Resources\  data embedded in the DLL, file by file in its README.md
tools\      check-game.ps1, check-settings.ps1, verify-release.ps1
tests\      the tests above, a project of their own
docs\       game-updates.md, bundle.md, reticles.md and shared-features.md for you; armour.md, screens.md and details.md for
            players; the images
```

`T90A\` is small, one file per job. `Mod.cs` is the entry point: the settings, the data load and the unit's
registration, with the settings' descriptions in `SettingText.cs`. `Conversion.cs` does the converting, calling the
rest: `Armour`, `Firepower` (rounds, counts, gun settings), `Drive`, `Countermeasures` (Shtora-1), and the
commander's station in `Commander.cs` with `Commander.Seat.cs`, `Commander.Sight.cs` (the TKN-4S), `Commander.Avtomat.cs`
(its Avtomat mode), `Commander.Readout.cs` (its indicator strip) and `Commander.Zones.cs`. The sights come a moment later from `Sights.cs`, with `Sights.1G46.cs`, `Sights.Night.cs` and `Sights.Readout.cs`;
`Reticles.cs` is the shared reticle handle.

`tools\verify-release.ps1` rebuilds a tag and compares it with a release DLL or zip (the last step of Releasing).
[docs/shared-features.md](docs/shared-features.md) lists the features in `Common\` and whether the tank switches each
on.

Where to change things:

| What | Where |
|---|---|
| Tunable numbers | `Firepower.cs` (gun, rounds, coax belt), `Mod.cs` (coax rounds), `Commander.cs` (Kord) |
| A setting's description | `SettingText.cs`, except three that live in `Common`: `Weathered paint` (`PaintFinish.cs`), `Campaign prevalence` (`UnitRegistry.cs`) and `Check for updates` (`UpdateCheck.cs`) |
| Armour values, hatches, glass, ERA, smoke points | the text files in `Resources\` |

Every method named `Dev...` is an empty partial method: a hook my own test harness fills in. Without the harness the
compiler removes the declaration and every call to it, so a normal build contains nothing of them. This lists them:
`Select-String -Path T90A\*.cs -Pattern "partial void Dev"`. You can ignore them, or delete one; a hook with a `ref`
argument goes together with the local it sets.

`Common\` is the machinery, fed by settings objects from `T90A\`, so the T-90A's numbers stay in `T90A\`. It is shared
by my GHPC mods and stamped into each repo, so it is never edited here: the same file is the same wherever it appears,
and each tank gets the subset it needs, so it holds code the T-90A doesn't use.
[docs/shared-features.md](docs/shared-features.md) says which features those are and which the tank switches on. It is
sealed: `Common\STAMP.txt` lists a hash for every file and `tests\CommonSealTests.cs` fails, naming the file, when one
differs. To change it, open an issue or a pull request on this repo describing the change; I apply it to the shared copy
and stamp every mod again.

One trade-off comes with that. Each port compiles `Common\` into its own DLL under its own namespace, so a player who
installs the T-90A, T-90M and T-80UM2 together gets one copy of every Harmony patch per port, for example three on
`FireControlSystem.Update`, `PlayerInput.Update` and `WeaponHud.Update`. A copy only acts on its own vehicles and
returns early when the instance isn't one of them, so what the extra copies cost is a lookup per call. The few things
the copies have to share go through `AppDomain` data under a versioned key: the Shtora screen's lasing, blinded and
HUD-lift tables (`ShtoraScreen.Laser.cs`, `ShtoraScreen.Menu.cs`, keys like `"GHPCMods.ShtoraScreen.Lasing.v2"`) and the
Unity error sinks in `UnityErrors.cs`. They hold only game and mscorlib types, because a type from one DLL isn't the
same type to another. If you change the shape of one, bump the version suffix in every port. A port that finds another
shape under its key logs a warning and keeps a table of its own, and the ports stop sharing. A shared core DLL would
avoid all of this, but it would mean one more file to install and a version to match for every player, so I left it out.

`Resources\` is embedded under each file's plain name (one wildcard line in `T90A.csproj`) and read with
`GetManifestResourceStream`: the reticles (`*.jsonc`), round textures and meshes, the Shtora panel art and sounds, the
module, interior and gun depression data, the trimmed T-72M1 hull skin, and the text files that used to sit in the
bundle (armour values, hatches, glass roles, ERA links, smoke points).

The bundle's contents and formats are in [docs/bundle.md](docs/bundle.md).

## What happens when a T-90A spawns

At game start `T90AMod.OnInitializeMelon` makes the settings (`CreateSettings`), stops there if `Enabled` is off,
opens the bundle (`LoadData`) and registers a unit (`RegisterUnit`) `T90A` based on the `T72M1` with the `T80B`'s army
(GHPC has the T-72M1 as NVA and I wanted it Soviet). `UnitRegistry` copies the T-72M1's entry under that name so
Instant Action, the customiser and the campaign offer it, and re-adds it whenever a scene or spawner turns up without
it.

At spawn, `SpawnPatch` (in `Common`) on `UnitSpawner.SpawnUnit` renames the unit and runs `Conversion.Convert`, which
works through its `Steps` table: base visuals, visuals, paint, hatch undersides, tactical numbers, leftovers, armour,
commander's seat, interior, ammo, coax, gun, mobility, smoke launchers, hatches, turret axis, commander's station (the
Kord), modules, periscope placement, Shtora-1, LOD materials. The order of the table is the order they run in, and it
matters. The seat comes before the interior because both move the commander figure. Each step has its own try/catch (in
`ConversionSteps.Run`, in `Common`), so one failing logs an error and the rest carry on. Two frames later `LateSetup`
runs its own table: smoke crew refresh, voices, sight setup, the commander's station late setup, module wiring, thermal
heat and the ammunition self-check, once the game has built the crew.

Some steps only work at spawn (`SpawnOnly` in the table): ammo, coax, the Kord, modules, periscope placement and
Shtora-1, and in the late table module wiring and the ammunition self-check, because GHPC builds the unit's weapon and
camera lists straight afterwards. A T-90A the spawn patch missed is caught at `PlayerReady` by `ConvertSweep` (in
`Common`) and gets everything else; the log says so for the ammo and the Kord.

Parts borrowed from other vehicles (T-80B voices and sight sounds, the T-64B's machine gun station, the M60A3's thermal
screen) are copied at the main menu by `DonorParts` (in `Common`), because GHPC can unload those vehicles between
missions.

Private game members are read through `Common\Reflect.cs`, which warns once instead of throwing when a name has gone.
Patches that run every frame report through `Common\PatchGuard.cs` so one throw doesn't kill the game's loop.
`tools\check-game.ps1` checks all of it against a game build without starting the game. If you add a `Reflect` call
whose member name comes from a variable, put the names in `tools\check-game-list.txt` with a `covers` line.

## Settings

MelonPreferences in the `[T-90A]` category of `UserData\MelonPreferences.cfg`. The README has the player wording and
this table says where each one lives. `tools\check-settings.ps1` checks both tables against the code.

| Setting | Defined in | Read by |
|---|---|---|
| `Enabled` | `Mod.cs` | everything, off means the mod does nothing |
| `Add to campaign roster`, `Campaign prevalence`, `Campaign unlock after missions` | `Common\UnitRegistry.cs` (`CampaignConfig`), set up in `T90A\Mod.cs` | the campaign roster, re-read at each mission load |
| `APFSDS` | `Mod.cs` | `Firepower`, the default round where the game doesn't pick one |
| `Force APFSDS` | `Mod.cs` | `Firepower` (`ForceApfsds`): the campaign's roll gives the `APFSDS` round |
| `Gunner night sight` | `Mod.cs` | `NightSight` (`ESSA` or `Buran-M`) |
| `ESSA electronic zoom` | `Mod.cs` | `NightSight` |
| `Fire only when the gun is laid` | `Mod.cs` | `Sights`, which hands it to `GunnerFcs` as the firing permission |
| `Weathered paint` | `Common\PaintFinish.cs`, set up in `T90A\Mod.cs` and `T90A\Conversion.cs` | `Conversion` (the paint step) |
| `Shtora-1` | `Mod.cs` | `Countermeasures`, off skips the jammer and the screens |
| `Shtora-1 automatic screens`, `Shtora-1 alarm tone`, `Shtora-1 jammer on at spawn`, `Shtora-1 jam chance`, `Shtora-1 panel on screen`, `Shtora-1 panel always on screen` | `Mod.cs` | copied into the `Jammer` and `Screens` settings in `Countermeasures.cs` |
| `Kord spent cases` | `Mod.cs` | `Commander` |
| `ERA destruction effects` | `Mod.cs` | `EraBricks.Look` |
| `Armour preset` | `T90A\Armour.cs` (`Common\ArmourPresets.cs`, text in `SettingText.cs`) | `Armour` (`game-scale` or `source-only` numbers) |
| `Check for updates` | `Common\UpdateCheck.cs` | asks GitHub once per start, one console line |
| `ConfigVersion` | `Common\PortSettings.cs`, set up in `T90A\Mod.cs` | hidden, the version that last wrote the section |

Most only apply to T-90As spawned after the change. Two catches when you add or change one:

- MelonLoader writes the old default into the player's file and keeps it. To move players to a new default, add a
  `SettingsMigration` to `Migrations` in `Mod.cs`. It only changes a value still at the old default, once.
- A decimal-comma Windows can leave `"0,3"` in the file, which MelonLoader quietly ignores. `PortSettings.Finish` reads
  it anyway, so call `Finish` after all the entries exist.

## Releasing

1. Bump the version in `MelonInfo` at the top of `T90A\Mod.cs`. `Repo` next to it is what the update check asks. Commit.
2. Get the commit's 7-character short hash with `git rev-parse --short=7 HEAD` and build with it:
   `dotnet build -c Release -p:GameDir="<GHPC folder>\Bin" -p:GitCommit=<that hash>`. The hash goes into the DLL's stamp.
3. Install it, start a mission and read `MelonLoader\Latest.log`: the stamp first, then no warnings or errors from the
   mod.
4. Zip it as `T90A_<version>.zip` with this layout inside, forward slashes: `Mods/T90A.dll` and `Mods/T90A/t90a` (the bundle
   from the latest release). In a folder that holds that `Mods` folder: `tar -a -c -f T90A_<version>.zip Mods`.
5. The armour numbers, hatches, glass and smoke points are text in `Resources\`, so changing them is a rebuild of the DLL and
   nothing else. The bundle only has to change with the model, and that needs the Unity project, which isn't in this repo.
   Renaming an armour piece or a visual node is a model change too. [docs/bundle.md](docs/bundle.md) says which edits are
   safe. Every release, new bundle or not, gets its row in the hash table there (the check is in that file).
6. Tag the built commit `v<version>` (like `v0.4.4`) and push the tag. On GitHub create a release from the tag: attach the zip,
   and write the release notes, what changed for the player, a line or two each.
7. Check the result: `powershell -ExecutionPolicy Bypass -File tools\verify-release.ps1 -Tag v<version> -Release T90A_<version>.zip
   -GameDir "<GHPC folder>\Bin"` rebuilds the tag in a temporary folder with the commit the release's own stamp carries.
   Expect IDENTICAL: a release built from its tag rebuilds byte for byte (the released 0.4.4 does from tag `v0.4.4`).
   DIFFERENT means the SDK or the source differs from what built the release; compare the decompiled code then.
