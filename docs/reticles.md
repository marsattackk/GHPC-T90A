# The reticle files

Every reticle the mod draws is a description file in `Resources\` (`*.jsonc`), read by `ReticleSpec` and built into
GHPC's own reticle by `ReticleBuild` (`PortReticles` loads them). This page is the language. `tests\ExpressionTests.cs`
and `tests\ReticleTests.cs` pin its behaviour, and `tests\ShippedReticleTests.cs` expands every shipped file and compares
it line by line with `tests\expanded\`. The gunner's 1G46 reticle and its readout are still drawn in code
(`Sight1G46` in `Sights.1G46.cs`, `Sights.Readout.cs`), not from a file.

GHPC draws a reticle as one skinned mesh. Each element is skinned to the bone of its nearest group, and the group's
**alignment** says how the game moves that bone: `Boresight` stays on the gun's line, `VerticalRange` moves with the set
range, `Impact` follows the computed impact point, and so on. An element with no aligned group above it is refused.

## The file

JSON with `//` comments and trailing commas. Positions are **mrad** from the origin, x right, y up, unless `units` says
otherwise. Any string where a number is expected is an **expression** (below).

```jsonc
{
  "name": "TKN-4S (T-90A)",       // must be unique: GHPC caches meshes by name
  "fov": 7.8,                     // vertical field of view, deg
  "units": "mrad",                // mrad, mil (6000 a turn), mil_nato (6400), deg, moa, field (1 = field radius)
  "lights": { "from_host": true, "set": { "NightIllumination": [4, 2.2, 0.5] } },
  "style": { "w": 0.25, "visual": "painted", "light": "NightIllumination" },
  "vars": { "Step": 2.094, "Half": "x => x / 2" },
  "defs": { "Tick": { "params": ["x"], "elements": [ { "seg": ["x", 0, "x", -1] } ] } },
  "elements": [ { "group": "Fixed", "align": "Boresight", "elements": [ ... ] } ],
  "screen": { "name": "TKN-4S Canvas", "items": [ ... ] }
}
```

- `lights`: `from_host` copies the lights of the sight's own vanilla reticle; `set` gives an HDR RGB colour per light
  (NightIllumination, Powered, Loaded, LaserReady, GuidanceActive).
- `style`: defaults that a group or element can override: `w` (line width), `visual` (`painted`: etched, dark by day and
  lit at night; `additive`: projected, for monitors and thermal pictures) and `light`.
- `vars`: evaluated top to bottom, visible everywhere. Numbers, lists, `'strings'` or lambdas. The code reads some of
  them by name (`spec.Num("X")`), so a value both the drawing and the code need lives here.
- `defs`: macros. `{ "use": "Tick", "args": [3] }` evaluates the arguments where it stands, then expands the macro with
  only the globals and its parameters in scope.
- Any element may carry a `"note"`, which is ignored.

## Elements

| Element | Keys |
|---|---|
| `group` | name, `at`, `align` (None, Boresight, VerticalRange, RotaryRange, Impact, LasePoint), `ballistic` (a round key) or `stadia` (target size, m), `units`, `style`, `elements` |
| `line` | `[x, y, deg, len]`, `w`: centred on (x, y), turned `deg` counter-clockwise from +x |
| `seg` | `[x1, y1, x2, y2]`, `w` |
| `poly` | `[[x, y], ...]`, `closed`, `w`: one segment per edge |
| `dash` | `[x1, y1, x2, y2]`, `on`, `off`, `w` |
| `arc`, `circle` | `[cx, cy, r, from deg, to deg]`, `[cx, cy, r]`; `segments` (default one per 10 deg, 32 for a circle) |
| `text` | `"text {expr}"`, `at`, `size` (about 10 per mrad of height). Latin letters only (the game font), use `glyph` for Cyrillic |
| `glyph` | a letter drawn in strokes, `at`, `h`, `w`. Known: П ш Б Г Л Н (`ReticleSpec.Glyph` adds more) |
| `for` | a name or `[names]`, `in` (a range or a list; with several names each item is a list), `do` |
| `if` | an expression, `then`, `else` |
| `use` | a def's name, `args` |

In a `ballistic` group, a child's y is **metres of range** (negative is above), turned into an angle with the game's
ballistics for the round the code maps to that key. In a `stadia` group, y is the range at which a target of that size
fills the mark.

## Expressions

C-like, on doubles, lists, strings and booleans. From loosest to tightest:

| | |
|---|---|
| lambda | `x => ...`, `(a, b) => ...` |
| conditional | `c ? a : b` |
| logic | `\|\|`, then `&&` |
| equality | `==`, `!=` (on numbers, strings or lists) |
| comparison | `<`, `<=`, `>`, `>=` |
| range | `a .. b` and `a .. b step s`, both ends included (`40 .. 5 step -5`) |
| sum | `+` (numbers add, strings and lists join), `-` |
| product | `*`, `/`, `%` (C remainder, sign of the left side) |
| unary | `-`, `+`, `!` |
| call, index | `f(x)`, `list[i]` (negative counts from the end) |

Literals: numbers, `'strings'`, `true`, `false`, lists `[a, b]`. Built in: `sqrt abs sin cos tan atan atan2 deg rad
floor ceil round min max` (angles in radians; `deg` / `rad` convert), `PI`, and `Fov` (the file's `fov`). There is no
integer division: write `floor(r / 100)`. Inside a string, `{expr}` is replaced by the expression's value.

## Screen marks

Marks fixed to the screen rather than to the gun (readout rows, indicator windows, a thermal sight's electronic marks)
go in `screen.items`. They are drawn on an overlay under the sight, on an 800 x 600 reference screen whose height is the
file's `fov`, in mrad from `anchor` (`centre`, `bottom` or `top`; x from the centre line). A file can be all screen:
`"elements": []`.

| Item | Keys |
|---|---|
| `rect` | name, `at`, `size` `[w, h]`, `full_width`, `color` `[r, g, b, a]` |
| `seg` | `[x1, y1, x2, y2]`, `w`, `color` |
| `text` | `"text"`, `at`, `size` (box), `text_size` (mrad), `font` (a game TextMeshPro font), `align`, `color`; rich text allowed |
| `readout` | as `text`, plus `value`; the code sets it by name (`Overlay.Readouts[name]`) |
| `lamp` | name, `at`, `size`, `color`; drawn lit over a dim copy, switched by the code (`Overlay.Lamps[name]`) |

`SightReadout` binds named items to the fire control (the range, the ready mark, the round's letter, the laser lamp).


## Changing a reticle

1. Measure the source drawing in mrad and write the numbers and where they come from as comments.
2. Put the marks under the group whose alignment moves them right: fixed marks under `Boresight`, range scales under
   `VerticalRange` or `Impact`, ballistic scales as `ballistic` groups below those.
3. Run the tests. A changed file fails `ShippedReticleTests` until its expansion is written again: set
   `T90A_UPDATE_EXPANDED=1`, run once, read the diff of `tests\expanded\`, then commit both.
4. Check it in game: problems in a file show as warnings in the MelonLoader console, and the reticle is drawn without
   the broken part.
