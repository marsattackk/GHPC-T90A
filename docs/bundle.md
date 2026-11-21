# The asset bundle

The model, textures and armour meshes live in one Unity asset bundle, `Mods\T90A\t90a`. It isn't in this repo and
neither is the Unity project it came from, so this page is what the code expects to find in it. If you rebuild it,
this is what to build. Where it says "silently", nothing goes in the log and the tank just looks wrong.

Built with Unity **2022.3.62f2**, the same as GHPC. A bundle has to match the game's Unity version (see
[game-updates.md](game-updates.md)).

## What gets loaded

`T90A\Mod.cs` opens the bundle through `BundleAssets` and loads the model's prefab by name, `Armour.cs` the armour's:

| Asset | Needed | If missing |
|---|---|---|
| `T90A_visual` | yes | error, no model |
| `T90A_armor` | no | warning, the T-72M1's armour stays |

A 0.4.4 or older bundle won't do for the current code: it has the old armour pieces.

The text data is in `Resources\` as `<name>.txt` and goes into the DLL. `PortText` reads the DLL's copy first and only
falls back to a `TextAsset` of the same name in the bundle, which older bundles carry and the current one doesn't
need. Edit these and rebuild the DLL, no Unity involved:

| File | Needed | If missing |
|---|---|---|
| `dm_armour` | with `T90A_armor` | warning, the T-72M1's armour stays |
| `vanilla_edits` | with `T90A_armor` | warning, the T-72M1's armour stays switched on under the T-90A's |
| `material_roles` | no | warning, glass reads as opaque plastic |
| `rig_map` | no | warning, hatches and running gear don't move, the turret turns on the T-72M1's pivots and the Kord sits level |
| `era_links` | no | silently, bricks matched by position only |
| `era_gone` | no | silently, no stowage goes with a brick |
| `smoke_points` | no | warning, grenades leave from the T-72's tubes |

The catch is that most of them name things in the bundle. Numbers, armour types, x-ray colours, roles, donors, hinge
angles and smoke points are safe to change on their own. Renaming an armour piece, or pointing a line at a different
node, brick or material, only works if the bundle has that name too. Otherwise it's a new bundle, built with the
renamed pieces.

Empty lines and lines starting `#` are skipped. A bad line in `dm_armour`, `vanilla_edits` or `rig_map` is skipped
with a warning naming the line, and the rest loads. Numbers use a full stop for the decimal point. `<TAB>` below means
a real tab, no spaces around it.

## `T90A_visual`

`Conversion` (its visuals step) instantiates it, moves these children of the root onto the game's bones and throws the rest
away. Each moved part gets `T90A_` in front of its name and its local transform reset, so model each one in its bone's
own space.

| Node | Goes onto | If missing |
|---|---|---|
| `HULL`, `TURRET`, `GUN` | `---MESH---/HULL`, `.../TURRET`, `.../GUN` | silently, no hull / turret / gun |
| `STUB_HATCH` | turret, at a fixed hinge point | silently |
| `MANTLET_COVER` | gun, skinned to the turret too | warning, the cover stays on the turret |
| `CUPOLA` | the game's `CUPOLA` bone, modelled around `Commander.CupolaAxis` | silently, no cupola and no belt |
| `KORD` | the Kord's cradle, modelled around `Commander.Trunnion` | silently |

- `MANTLET_COVER` needs a plain, readable mesh on the node itself. It's re-skinned from turret at the back to gun at
  the front by z.
- The Kord belt is cut out of the cupola model at run time, from a renderer under `CUPOLA` whose material starts
  `t90a_mg_6p49_kord_ammo`. Readable mesh, or no belt and no cases.
- Hinge lids, road wheels, whips, flaps and tracks are found later by the names in `rig_map`, as `T90A_<name>` or
  `<name>` anywhere under the unit.
- LODs: each `LODGroup` with one renderer in LOD0 and one in LOD1 gets LOD0's materials on LOD1, so LOD1 needs as many
  submeshes as LOD0 has materials.

## Materials

Matched with `StartsWith` unless it says exact.

- `t90a_t_90a_*` on the seven parts above is replaced with a copy of the base vehicle's `GHPC/VehicleShader` material,
  taking your `_MainTex`, `_BumpMap`, `_MetallicGlossMap`, `_DetailMask` (paint mask) and `_OcclusionMap`.
  `_DetailAlbedoMap` and `_DetailNormalMap` hold the clean albedo and metal-gloss for `Weathered paint` off.
- `t90a_t_90a_turret` must stay a 4096 x 2048 atlas: the hatch undersides are painted as a flat patch at x 320, y 1350,
  230 x 110 px.
- `t90a_hatch_inner*` is the inside of the hatch lids. Without it the log says `no hatch underside material found`.
- Anything on those parts using plain `Standard` is switched to GHPC's `Standard (FLIR)` so it shows in thermal.
- Names in the first column of `material_roles` are matched exactly.
- A material whose name ends `headlight_glass_a_under` marks the Shtora lenses on the full-detail turret mesh, which
  has to be readable. Without it the emitters neither glow nor warm up.

## `T90A_armor`

Shapes only, cut from War Thunder's damage model of the T-90A. The numbers come from `dm_armour`. `DmArmourApply`
applies `vanilla_edits` first, then puts the pieces under the root's `HULL`, `TURRET` and `GUN` onto the matching bone
and collider group.

Each piece is a direct child of its group, with a readable mesh, named `dm_<anything>|<piece>`. `<piece>` is the key in
`dm_armour` and becomes the object's name, which `era_links` and `era_gone` refer to. Children not starting `dm_` are
skipped, which keeps the `reshape|<name>` meshes out of the armour. `geometry` pieces are measured through the mesh, so
it has to be closed.

## Text files

### `dm_armour`

One piece per line, tab separated, at least 11 fields:

```
<piece> <mode> <ke> <ce> <ke after> <ce after> <angle> <material> <x-ray> <name> <source> [<slat>]
```

- `mode`: `geometry` (`ke` and `ce` are multipliers per mm of path), `era` (`ke after` and `ce after` once it has gone
  off), `composite` (uniform, convex collider), `drawn` (x-ray and AAR only, no armour), anything else uniform (the
  bundle uses `nominal`).
- `ke`, `ce`: mm of rolled steel against kinetic and chemical rounds.
- `angle`: `1` to count the hit angle on a uniform piece.
- `material`: `armour_steel`, `mild_steel`, `plain`, `composite`, `filling`, `filling_steel` or `liner`, all the mod's
  own armour types. Empty for `drawn`.
- `x-ray`: empty (not drawn), `template` (GHPC's see-through material) or `r,g,b[,a]` (a tinted copy).
- `name`: the label in the armour view and damage report. `source` isn't read. `slat`: `1` for slat armour.

The numbers are the `game-scale` preset. `source-only` scales down every `geometry` piece with `_pkg` in its name and
the three glacis fillings in `Armour.GlacisFilling`, so renaming those leaves them at game scale in both.

```
body_side_dm<TAB>nominal<TAB>80<TAB>80<TAB>0<TAB>0<TAB>1<TAB>armour_steel<TAB><TAB>hull side<TAB><TAB>0
```

### `vanilla_edits`

What to do to the T-72M1's own armour, by collider object name, `|` separated. The pieces War Thunder's model replaces
are switched off by the code, so the file doesn't need to list them.

| Line | Does |
|---|---|
| `off\|name` | switches the piece and its children off |
| `strip\|name` | removes its colliders, armour and renderer, keeps the children (done last) |
| `reshape\|name` | gives it the mesh of `reshape\|name` in `T90A_armor` |
| `xform\|name\|m0,...,m15` | moves it by a `Matrix4x4` (column-major), in the unit's root space at rest |
| `bone\|path\|group\|dx,dy,dz` | moves a bone and the colliders following it, in metres (`group` is a label, not read) |

### `material_roles`

```
<bundle material><TAB><role><TAB><vanilla material or -><TAB><donor units, comma separated>
```

The role is only for the log. `-` makes the line a note. Donors are unit keys like `T80B`, tried in order.

### `rig_map`

`|` separated, positions in the unit's root space, extra trailing fields ignored. `mount` lines are read but the T-90A
has none and nothing here uses them.

| Line | Fields |
|---|---|
| `axes\|turret\|gun\|muzzle\|pitch` | where the turret and the gun turn and the muzzle sits (`x,y,z` each), and the nose-down level in degrees that the turret, seat and Kord sit at |
| `gear\|mode` | `base` (leave it, the default), `follow` (wheels and tracks follow the suspension) or `port` (park parts on bones) |
| `follow\|restY\|uvX,uvY\|radius\|rimGap\|riseLimit` | the wheel numbers `GearFollow` needs |
| `fpart\|id\|node\|kind\|drive\|pivot\|target\|mate` | one moving part, `kind` is `wheel`, `arm`, `rod`, `spin` or `track` (a track's id ends `_l` or `_r`) |
| `hatch\|?\|role\|node\|baseBone\|hinge\|axis\|angle\|lift` | one hatch, driven by the base bone's `Openness`: lifts for the first 30 %, then turns |
| `part\|?\|node\|bone\|qx,qy,qz,qw` | `port` only, parks the node on the bone |
| `whip\|?\|node\|base\|tip\|rigid` | the radio whip |
| `flap\|?\|node\|a\|b\|?\|limit\|taper\|ramp\|lag` | a hanging flap on a jiggle bone, `lag` optional |

`?` fields aren't read. What `rigid`, `taper`, `ramp` and `lag` do is in `HatchRig.Whip` and `HatchRig.Flap`.

### `era_links` and `era_gone`

```
<armour piece><TAB><visual brick>
<visual part><TAB><brick>,<brick>
```

`era_links` ties each `era` piece to its brick in the visual. Without a line the brick is picked by geometry, which
gets the cheek boxes wrong. Pieces on the same brick set each other off. A brick needs a node `<brick>` and, beside it,
`<brick>_open` (the burst look). `<brick>_lid` and `<brick>_soot` are optional, and `<brick>_c1`, `_c2` go off with
`<brick>_c0`.

`era_gone` hides a visual part (the cheek stowage boxes) when any of its bricks goes off, along with an armour piece
`box_<part>` if there is one.

### `smoke_points`

Floats separated by any spaces, commas, tabs or newlines, three per tube mouth, in the turret bone's space. Left and
right go by the sign of x, then top down, matched to the T-72M1's smoke slots. One bad number drops the whole list,
since it would shift every tube after it.

## Released bundles

SHA-256 of `Mods/T90A/t90a` in each release zip. Every release gets a row, new bundle or not, in the commit that raises
the version (Releasing in [MAINTAINING.md](../MAINTAINING.md#releasing)).

| Version | Bundle |
|---|---|
| 0.2.0 | `f8e1b08104b422bb8d567ff8b91a340607e968e5dc944a8fb4ff5a23194c378e` |
| 0.3.0 | `4e509eb4021900b54d084436047522cd005c5d6fd3925cd2e442e653622e492c` |
| 0.3.1 | `fcc19c9aa3b19e9714d030a30634041463a939ffb065f3c0af3c4510ca3ef264` |
| 0.3.2 | `15f37de0b06ab5705330f893994625c055aeff441acbb95249b99a714a86d94c` |
| 0.4.0 | `b56dd420cf26c2dea5292f2b726d2889746b5c480ead5f6588ea16b1551d5a9e` |
| 0.4.1 | `71c45a36bd5bce26c4466aed04c8d645251439d47b30137a93bbe217b8d58d7b` |
| 0.4.3 | `46e7f122d1630e7afcf82df6e7adcfeee631cb8fd7edfdd31489be4ac855e8e6` |
| 0.4.4 | `18d41763d99896ec7c815f0e64bc04512cd2edc766a6f985655d436259af86d1` |
| 0.5.0 | `e17c1c3c6aa23478b6f3474847a62aa0b6541e91b7c6893a255825ca3376e963` |
| 0.5.1 | `e8ee068d0e7e88414a02288fac136a54405ddae264bd9dfb04935bcb2dc58d1d` |
| 0.5.2 | `ef1e8e75a8753e60017d42240dafa0c9eb0611508ce4e43a8e029f1d582ff5aa` |
| 0.5.3 | `2d908371ed319505eaf2bccf7ac1475037b197001ceb9595233033c692fab1d1` |

0.1.0 and 0.4.2 are missing because my saved zips for those don't match what GitHub serves.

```
Expand-Archive T90A_<version>.zip -DestinationPath check
(Get-FileHash check\Mods\T90A\t90a -Algorithm SHA256).Hash.ToLower()
```
