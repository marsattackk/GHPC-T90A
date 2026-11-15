# Resources

Data files embedded in the DLL. The csproj embeds every file here under its plain name except this README, so a new
file only has to be dropped in. The reticle files (`*.jsonc`) are written in the language described in
[docs/reticles.md](../docs/reticles.md).

| File | What it holds | Read by |
|---|---|---|
| `T72M1_skin_hull_trimmed.bin` | T-72M1 hull skin mesh without the side skirts | `EmbeddedFile.T72M1Skin`, then `BaseRigT72M1.HideBaseVisuals` |
| `gun_depression.txt` | gun depression by turret angle, for the hull profile | `GunSetup`, name set in `Firepower` |
| `interior_map.txt` | where each crew member and interior part moves to | `InteriorMap`, held by `Conversion` |
| `modules_built.json` | the T-90A's damageable modules and what they hook into | `PortModules`, held by `Conversion` |
| `tkn4s_day.jsonc` | commander's TKN-4S day reticle and ammo strip | `Commander.FitTkn4s`, `Tkn4sReadout` |
| `tkn4s_night.jsonc` | commander's TKN-4S night reticle | `Commander.FitTkn4s` |
| `essa_wide.jsonc`, `essa_narrow.jsonc` | ESSA thermal reticles, wide and narrow field | `NightSight` |
| `essa_row.jsonc` | ESSA readout row under the narrow reticle | `NightSight` |
| `essa_monitor.jsonc` | ESSA monitor edges | `NightSight` |
| `round_3bm42_co.png`, `round_3bm60_co.png` | stencil textures for the 3BM42 and 3BM60 rounds | `Firepower`, applied by `PortAmmo` |
| `round_9m119m_co.png` | stencil texture for the 9M119M missile | `Firepower`, applied by `PortAmmo` |
| `round_meshes.txt` | index of the round mesh pieces below, by round | `PortAmmo.ApplyShape` |
| `round_mesh_3bm60_*.mesh` (charge, oring, rod, sabot 001 / 003 / 004) | 3BM60 shell pieces | `PortAmmo`, via `round_meshes.txt` |
| `round_mesh_9m119m_missile.mesh` | 9M119M missile body | `PortAmmo`, via `round_meshes.txt` |
| `round_mesh_9x949_charge.mesh` | booster piece for the 9M119M | `PortAmmo`, via `round_meshes.txt` |
| `shtora_panel.png` | commander's Shtora control panel picture | `ShtoraScreen` |
| `shtora_lamp_on.png`, `shtora_lamp_off.png` | panel lamps | `ShtoraScreen` |
| `shtora_layout.txt` | where the lamps sit on the panel picture | `ShtoraScreen` |
| `shtora_alarm.wav` | laser warning tone | `ShtoraScreen` |
| `dm_armour.txt`, `vanilla_edits.txt`, `material_roles.txt`, `rig_map.txt`, `era_links.txt`, `era_gone.txt`, `smoke_points.txt` | armour values, hatches, glass, ERA and smoke data, formats in [docs/bundle.md](../docs/bundle.md) | `PortText`, before the bundle's copy |
