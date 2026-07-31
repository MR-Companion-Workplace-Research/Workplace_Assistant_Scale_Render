# Toon Shading — Implementation & Procedure

Documentation for the **toon render-style condition** of the Scale × Render Style study
(last updated 2026-07-18, reflecting the tuned recipe confirmed on the male + female avatars).

The design goal throughout: the toon and realistic conditions must differ **only in shading**.
Both share the same prefab rig (toon prefabs are Prefab Variants of the realistic ones), the same
Meta Depth API occlusion, and the same passthrough-opacity fix, so nothing else confounds the
study manipulation.

---

## 1. File map

| File | Role |
|---|---|
| `Assets/Toon Shaders/QuestToonLit.shader` | **V1** cel shader (`QuestToon/Lit`): 2 hard steps → 3 flat tones over the realistic albedo. Skin, teeth, tongue, clothing. Has the outline pass. |
| `Assets/Toon Shaders/QuestToonHair.shader` | **V1** hair (`QuestToon/Hair`): same ramp, alpha-test, double-sided, no outline. |
| `Assets/Toon Shaders/QuestToonLitPosterized.shader` | **V2** (`QuestToonPosterized/Lit`) — **the shader in use**. Flattened albedo + N-band lighting ramp driven by the virtual key light. |
| `Assets/Toon Shaders/QuestToonHairPosterized.shader` | **V2** hair (`QuestToonPosterized/Hair`): same banded ramp + key light, so hair and face band in the same direction. |
| `Assets/Editor/ToonifyAvatar.cs` | Editor tool. Creates/refreshes the toon material set, stamps the tuned recipe, A/B-swaps V1↔V2, and hosts the key-light editor preview. **The recipe constants in this file are the source of truth for the look.** |
| `Assets/Scripts/MR scripts/AvatarPlacer.cs` | Sets the two shader globals at spawn: `_OutlineScale` (outline thickness ∝ avatar scale) and `_ToonKeyDir` (virtual key light, from `toonKeyLocalDirection`). |
| `Assets/Editor/AvatarPlacerEditor.cs` | Decluttering custom inspector. **New serialized fields on AvatarPlacer are invisible until added to its `OneTimeSetup` whitelist.** |
| `.../new_models/male_mat/Toon/*.mat`, `.../new_models/female_mat/Toon/*.mat` | The generated toon material sets (one `<name>_Toon` clone per realistic material). |

V1 vs V2 share property names, so a material can flip between the families losslessly
(**Tools > Study > Toon Style**). The study currently runs V2; V1 is kept for A/B comparison.

---

## 2. How the V2 shader works (QuestToonPosterized/Lit)

Per fragment, in order:

1. **Depth occlusion** — `clip(CalculateEnvironmentDepthOcclusion(worldPos) - 0.5)` when the
   Meta Depth API keywords are on: real furniture/people occlude the avatar.
2. **Albedo flatten** (`_AlbedoFlatten`, currently **1** = full) — the CC skin texture bakes
   shading into the color map (AO in knuckle creases, eye sockets, nasolabial folds). Each
   texel's *brightness* is replaced by the flat `_FlatValue` skin tone while keeping its hue
   and saturation, producing flat, even skin. Without this, any banding locks onto the
   painted shadows and shatters into dark blocks (worst on the darker male skin).

   Two details matter here (both fixed **2026-07-20** — see §6):

   - **`_FlatValue` is a per-material constant, auto-stamped by the Toonify tool**, not a
     blurred sample of the texture. It used to be a second `tex2Dbias` at `_PosterizeBlur + 3`
     mip bias; on the 2048² CC head map that is a ~45× reduction — about **15 texels across
     the whole face** — and magnifying that bilinearly painted a visible **quad grid** over
     the skin. A constant has no spatial frequency, so the grid cannot exist, and it costs
     one *fewer* texture fetch.
   - **All four skin materials share one `_FlatValue`.** Their atlases differ a lot (the head
     map is full of dark lashes/lips/brows, the body map is nearly uniform skin), so their
     medians differ by ~12% in linear space — enough to show a **brightness step at the neck
     and wrist seams** if each got its own. `StampFlatValues` averages them and writes the
     same number to every skin material. Clothes/shoes each keep their own.
   - **`_FeatureFloor`** (0.25) clamps the divisor in `chroma = color / value`. On near-black
     texels (lash line, nostril crease, jaw crease) an unclamped divide amplified their
     JPEG/ASTC block noise ~10×, and the flatten then re-lit them to full skin brightness —
     that was the speckled blocking concentrated around the eyes, nose bridge and jaw. The
     floor fades in with `_AlbedoFlatten`, so at flatten 0 the texture still passes through
     bit-exact.
3. **Texture posterize** (`_PosterizeStrength`, currently **0** = off) — optional quantization
   of the flattened albedo's brightness into `_ColorSteps` flat levels, hue/saturation
   preserved exactly (value-only, in sqrt space, can never clip out of gamut). Kept off:
   the stylization comes from the lighting bands instead. This axis exists for future use.
4. **Saturation** (`_SaturationBoost`, currently **1** = neutral).
5. **Banded lighting ramp** (`LightingToonRamp`) — the heart of the look:
   - Half-Lambert `N·L * 0.5 + 0.5` against the **virtual key light** `_ToonKeyDir`
     (see §3). Falls back to the real scene light + shadow attenuation when the global
     is unset (editor without preview / realistic runs).
   - Quantized into `_LightBands` flat cells; only the top sliver of each cell is
     anti-aliased (`_RampSmoothing × bands`), so edges stay crisp and inked.
   - The cell index lerps the color between `_ShadowTint` (darkest band) and white
     (brightest). **Band contrast = how dark `_ShadowTint` is** — the bands spread evenly
     between it and white, so a deeper tint means a bigger visible step per band.
   - Multiplied (not added) with `illum = saturate(_LightColor0 + ShadeSH9 * _AmbientStrength)`
     — folding ambient in multiplicatively keeps every band visible on bright albedos
     (additive ambient used to clip light skin to white and merge the top bands).
6. **Alpha forced to 1** — passthrough never bleeds through the avatar.

A separate **inverted-hull outline pass** extrudes backfaces in world space by
`_OutlineWidth * _OutlineScale`. `_OutlineScale` is a global set by AvatarPlacer to the avatar
scale (a runtime-scaled skinned mesh bakes scale into its verts, so the shader cannot recover
it from the matrices — it must be handed over). The outline is depth-occluded and skipped on
hair (inverted hulls look broken on thin cards).

Eyes/cornea/tearline stay on their **realistic** shaders on purpose — stylized eyes read worse.

---

## 3. The virtual key light (why bands are reliable)

**Problem:** MRUK spawns the avatar on whatever anchor the room offers (TABLE/COUCH/…), facing
the participant — so the avatar's orientation relative to the scene's fixed directional light
(rotation 50°/−30°/0°) is arbitrary. Banding against the real light gave a different band
layout every room: front-lit = one flat bright band ("no banding at all"), back-lit = uniformly
dark. No threshold tuning can fix a variable that changes per spawn.

**Solution:** the ramp bands against `_ToonKeyDir`, a world-space direction global that
`AvatarPlacer.ApplyToonKeyDirection()` computes as
`spawnedAvatar.rotation * toonKeyLocalDirection.normalized` — i.e. the key light is **defined
relative to the avatar** and re-applied whenever its facing is set (spawn + `FaceTarget`).
The band layout on the face is therefore identical in every room, every spawn.

- `toonKeyLocalDirection` lives on AvatarPlacer under "Toon Key Light" (in the inspector it's
  inside the **Advanced setup** foldout — the custom editor hides non-whitelisted fields).
- Current value **(−0.5, 0.7, 0.9)** (X = avatar's local right, Y = up, Z = facing direction).
  The avatar faces the participant, so this reads to the participant as **lit from their
  top-right, shaded toward their bottom-left**, with the under-jaw in the darkest band —
  matching the previous_toon.png reference.
- Tuning: flip X's sign to mirror the shadow side; raise Y for stronger under-jaw/brow
  shadows; **lower Z (0.9 → ~0.5) to pull more of the face into the darker bands** (bolder
  banding); zero vector = fall back to the real scene light.
- The key deliberately ignores real shadow attenuation — the bands are stylization; overall
  brightness still follows the real light through the `illum` term.
- Because it's anchored to the avatar, the shading behaves like part of the character art:
  walking around the avatar keeps the shadow on the same cheek (it does not stay screen-relative).

**Editor preview:** nothing sets the global outside Play mode, so Prefab Mode shows the
fallback (scene-light) look by default. **Tools > Study > Toon Key Light > Preview In Editor**
stamps the global for the current editor session (using the scene AvatarPlacer's tuned value
when present), making Prefab Mode match the runtime layout; **Clear Preview** reverts. A stale
preview cannot leak into a trial — spawning overwrites the global.

---

## 4. Current tuned recipe (source of truth: `ToonifyAvatar.cs` constants)

Confirmed good on both the male and female avatars, 2026-07-18:

| Constant | Value | Meaning |
|---|---|---|
| `PosterLightBands` | **4.5** | Lighting bands. (Non-integer is fine — the fractional top cell just makes the brightest band narrower.) |
| `PosterRampSmoothing` | **0.06** | Crisp inked band edges (shader scales this by band count). |
| `PosterShadowTint` | **(0.4, 0.3, 0.28)** | Darkest band color = the contrast floor. Deeper than V1's (0.5, 0.4, 0.38) so each band step reads (~23% vs ~17%). Warm (R>G>B) so shadows don't go grey. |
| `PosterFlatten` | **1** | Fully flatten baked AO out of the albedo. |
| `PosterFeatureFloor` | **0.25** | How dark a painted feature (lashes, nostril, lip line) stays after flattening. Lower = features wash out toward flat skin *and* compression noise starts to show; higher = more of the realistic painted detail survives. |
| `_FlatValue` | **auto** | Not a constant — computed per avatar by `StampFlatValues` (median albedo brightness, shared across the skin materials). Expect ≈ **0.69** male / ≈ **0.66** female in this Linear project. |
| `PosterStrength` | **0** | Texture posterize off — flat skin + light bands only. |
| `PosterColorSteps` | 5 | Unused at strength 0; kept for per-material experiments. |
| `PosterBlur` | 2.5 | Mip bias of the color sample. At flatten 1 this affects **hue only** (brightness comes from `_FlatValue`), so it can be lowered for crisper lip/brow color without bringing baked AO back. |
| `PosterSaturation` | 1 | Neutral. |
| `LitOutlineWidth` | 0.0025 | Outline metres @ scale 1 (0.005 floods the mouth concavity). |
| `toonKeyLocalDirection` (AvatarPlacer) | (−0.5, 0.7, 0.9) | See §3. |

V1 constants (`ToonRampThreshold` 0.5, `ToonRampHighlight` 0.78, `ToonRampSmoothing` 0.03,
`ToonShadowTint` (0.5, 0.4, 0.38)) are stamped on all toon materials first; the V2 stamp then
overrides the shared properties on V2 materials (gated on the V2-only `_LightBands` property),
so each family keeps its own tuned look.

---

## 5. Procedures

### Toonify a new avatar (or repair an existing toon set)
1. Open the toon prefab **Variant** (e.g. `male_toon`) in Prefab Mode, select its **root**.
2. **Tools > Study > Toonify Selected Avatar.**
3. Save the prefab (Ctrl+S).

The tool derives the toon set from the **base (realistic) materials** via the prefab-variant
correspondence — never from what's currently assigned — so it is idempotent and self-healing:
re-running fixes earlier mistakes instead of cloning toon materials. It copies albedo/tint/
cutoff **by property name** across shader families (`_DiffuseMap`/`_MainTex`/`_BaseMap`/… →
`_DiffuseMap`), which is what prevents the "white clothes/eyebrows" failure. Hair/brow/scalp/
eyelash → Hair shader; eyes/cornea/tearline → kept realistic; everything else → Lit.
It preserves a material's V1/V2 family choice on re-runs.

### Change the look (recipe tuning loop)
1. Turn on the key-light preview (**Tools > Study > Toon Key Light > Preview In Editor**).
2. Experiment on **one material's sliders** — usually `Std_Skin_Head_Toon.mat` — live in
   Prefab Mode. ⚠ CC splits skin across four materials (`Std_Skin_Head`, `_Arm` = hands,
   `_Body` = under clothes, `_Leg`); edit the one on the part you're judging.
3. Copy the winning numbers into the constants in `ToonifyAvatar.cs`.
4. Select each toon avatar root (scene or Prefab Mode) and run
   **Tools > Study > Toon Style > Use Posterized (V2)** — this restamps the full V2 recipe
   onto every assigned toon material (it also refreshes materials already on V2), keeping
   both avatars and all four skin materials consistent. Hand-edited slider values are
   overwritten by design.
5. Band *direction/layout* is tuned separately on AvatarPlacer's `toonKeyLocalDirection`
   (Advanced setup foldout) — no restamp needed; it's a runtime global.
6. Changes only reach the headset with a Quest rebuild.

⚠ **A restamp (step 4) is mandatory after any change that touches `_FlatValue`** — including
the first run after pulling the 2026-07-20 shader change. `_FlatValue` defaults to 0, which
the shader reads as "never stamped" and responds to by **skipping the flatten entirely**, so
an un-restamped avatar renders with realistic (un-flattened) skin. That fallback is
deliberate: the alternative default would crush the material to black. The restamp log line
reports the value it wrote, e.g. `flat skin value 0.687 stamped on 4 skin material(s)`.

### A/B the two styles
Select the toon avatar root → **Tools > Study > Toon Style > Use Posterized (V2)** or
**Use Original Cel (V1)**. Material assets are changed directly (applies everywhere, no
prefab save needed).

---

## 6. History — tried and rejected (don't re-add without reason)

- **Color-grading posterize/saturation on V1** — per-channel posterize turned skin
  yellow-green; luminance-only turned it orange. The look is not color-grading-driven.
- **Luma-rescale posterize (`col * quantLuma/luma`)** — brightening saturated skin pushed a
  channel past 1.0; `saturate()` clipped unevenly per channel, so each band clipped to a
  *different hue* → faces/hands broke into randomly-colored patches. Replaced by the
  value-only sqrt-space `PosterizeValue` (cannot clip, hue exact).
- **Value-banding the raw albedo** — locked onto baked AO (knuckles, sockets) → hard dark
  blocks, worst on male skin. Fixed by `_AlbedoFlatten`; at full flatten the texture-banding
  axis is now simply off.
- **Feature shadow (2026-07-18, removed same day)** — re-inking the baked AO as a single
  texture-anchored shadow band (`_OcclusionShadow*`). Looked wrong in practice (too strong,
  darkened creases unpleasantly); user tuning converged on strength 0. Removed entirely.
- **Additive SH ambient in the ramp** — clipped light skin at white, merging the top bands.
  Ambient is folded in multiplicatively instead.
- **Fixed/object-space outline extrusion** — a runtime-scaled skinned mesh bakes scale into
  its verts, so object- and world-space extrusion give the same fixed world thickness, which
  floods facial concavities at miniature scale. Hence the `_OutlineScale` global.
- **Banding against the real scene light** — unreliable by construction with MRUK spawning
  (see §3); superseded by the virtual key light.
- **Blurred-mip local average as the flatten target (removed 2026-07-20)** — `_AlbedoFlatten`
  used to pull each texel toward a second `tex2Dbias` sample at `_PosterizeBlur + 3`. That is
  ~15 texels across the face on a 2048² map; bilinear magnification of it painted a **quad
  grid** over the skin, worst at the inner eye corners, nose bridge and jawline where one
  low-mip texel straddles UV islands and dark features. The tell was that lowering *either*
  `_AlbedoFlatten` or `_PosterizeBlur` removed the artifact but also removed the flatness —
  they are the same code path, so no setting could give flat-and-clean. Replaced by the
  auto-stamped `_FlatValue` constant (§2). **Don't reintroduce a texture-space blur here** —
  any low-mip sample magnified across a face will grid.

  Deliberately *not* done as part of that fix: raising the skin textures' import quality
  (mipMapMode Box→Kaiser, Bilinear→Trilinear, aniso 1→4). It would help slightly, but the
  realistic and toon conditions **share the same texture assets**, so changing import settings
  changes the realistic condition too — a confound in a study where render style is the IV.
  Revisit only if both conditions are re-baselined together.

- **Copying `_AlphaClip` verbatim onto the toon hair shaders (fixed 2026-07-20)** — caused
  *"the eyebrows are transparent on the headset"*. `Quest/HairOpaque` and the CC originals do
  not clip raw texture alpha; they remap it first —
  `a' = pow(saturate(a / _AlphaRemap), _AlphaPower); clip(a' - _AlphaClip)`. The toon hair
  shaders clip raw alpha, so copying `_AlphaClip` across raised the real threshold from
  `0.75 × 0.33 = 0.2475` to `0.33`, 33% higher. Brow cards only have ~6% of texels above the
  cutoff, so coverage is fragile as the mip chain averages alpha down: measured on
  `Female_Angled_Transparency_Diffuse`, coverage at 0.2475 is still **124%** of mip0 at mip 6,
  but at 0.33 it drops to **44%** and hits **zero by mip 7**. Net effect: toon brows vanish about
  a mip level earlier than realistic ones — invisible in the editor (you inspect the face close
  up, at mip 0) but gone at headset viewing distance, and far sooner in the miniature scale
  condition. Fixed by `ToonifyAvatar.RawAlphaCutoff()`, which inverts the remap when
  transferring the cutoff. **Affects both avatars** — re-run Toonify on `male_toon` *and*
  `female_toon`.

  **Follow-up the same day — the cutoff fix alone was not enough.** Reported symptom: brows
  render correctly close up, look too heavy at mid range, wash out to a pale smear far away, and
  vanish entirely beyond that. Cause: the cutoff fix put toon back on realistic's coverage curve,
  but *that curve is itself unstable*. Measured on the hair-card layer
  (`Female_Angled_Transparency_Diffuse`, 5.9% coverage at mip 0), coverage relative to mip 0 runs:

  | mip | 0 | 3 | 4 | 5 | 6 | 7 | 8 |
  |---|---|---|---|---|---|---|---|
  | coverage | 100% | 153% | **191%** | 141% | 124% | **26%** | **0%** |

  That is the reported artifact exactly. The `Base` layer is stable (100–122%) — only the
  hair-card layer swings. Fixed by enabling **`mipMapsPreserveCoverage: 1`** with
  `alphaTestReferenceValue: 0.2475` (the effective cutoff) on all four brow textures
  (`Female_Angled_Transparency_Diffuse` + `_Base_Transparency_Diffuse`, in `male_text/` and
  `female_text/`). Unity then rescales each mip's alpha so the fraction of texels passing the
  cutoff stays constant at every distance.

  This does change the **realistic** condition too, since the texture assets are shared — but
  both conditions were degrading *identically* (same texture, and after the cutoff fix, the same
  effective threshold), so this removes a defect from both rather than introducing a difference.
  Eyelashes and hair were checked and left alone: at the 0.2475 cutoff, lash coverage holds
  82–105% through mip 6 and hair has 20–35% base coverage, so neither collapses.

  **Second follow-up — the actual root cause of the colour shift.** Coverage was a real problem
  but it was *not* what made the brows look wrong. Measuring the headset capture directly:

  | | brow (darkest 20%) | skin | B−R |
  |---|---|---|---|
  | near | (55, 35, 20) dark brown | (190,135,100) | −35 |
  | far | (135,126,116) light **neutral grey** | (200,150,120) | **−14** |

  The far brow is not blue — it is *neutral*, and a neutral grey against saturated orange skin
  reads as cyan (simultaneous contrast). The grey has an exact source:
  `Female_Angled_Base_Transparency_Diffuse.png` stores **light grey RGB (156,139,132) in its
  fully-transparent texels** — 2.4× brighter than its opaque texels (79,54,44). With
  `alphaIsTransparency: 0`, Unity does not dilate the opaque colour outward before generating
  mips, so the mip chain averages that grey in. Through the material's cyan-ish tint
  (0.867, 0.97, 1.0) that predicts **(135, 135, 132)** — against **(135, 126, 116)** measured
  on device.

  Fixed by setting **`alphaIsTransparency: 1`** on the affected textures (both brow layers in
  `male_text/` and `female_text/`, plus `Hair_Clap_Transparency_Diffuse` and
  `Scalp_Transparency_Diffuse`, which showed the same contamination at 1.26× and 2.43×). Unity
  then bleeds the opaque colour into transparent texels before building mips, so deep mips
  converge on the brow's real dark brown instead of the background grey.

  This setting is appearance-safe by construction: it only rewrites RGB in texels that the alpha
  test discards anyway, so mip 0 (close up) is bit-identical. It only removes contamination from
  the averaged mips. Textures whose transparent texels are *darker* than their opaque ones
  (`Female_Angled_Transparency` 0.64×, `Std_Eyelash` 0.69×, `Hair_Transparency` 0.75×) were not
  causing lightening; the brow pair was set anyway for consistency.

  **`alphaIsTransparency` alone was NOT sufficient** — the artifact persisted. Unity's dilation
  only bleeds colour a limited distance around opaque texels: enough to fix edge filtering at
  shallow mips, but it cannot change what a deep mip converges to when **88.5% of the atlas is
  background**. A box-filtered mip preserves the image mean, so every deep mip tends to the
  texture's *global* mean — which was (147,129,122), i.e. (128,126,122) after tint. That is the
  grey, and no import setting moves it.

  **Final fix: the full dilation is baked into the PNGs.** A pull-push inpaint (alpha-weighted
  mip pyramid pulled coarse, then pushed back down) rewrites the RGB of *every* clipped texel to
  the nearest opaque colour, leaving alpha and all visible texels bit-identical — both are
  asserted in the script. Result for the brow base map: global mean **(147,129,122) →
  (71,46,36)**, so the tinted colour at every mip depth is now **(61,44,36)** dark brown instead
  of (128,126,122) grey. Applied to the same six textures. Originals backed up to
  `scratchpad/tex_backup/` (these assets are **not** in git — do not rely on `git checkout` to
  restore them).

  Diagnostic worth reusing: **compare a texture's opaque-texel mean RGB against its
  transparent-texel mean RGB. A ratio above ~1.25 means the background will bleed into the mips
  and wash the material out at distance.**

  Why the toon condition showed it first: `_PosterizeBlur` (2.5) makes the hair shader sample
  colour ~2.5 mips deeper than the alpha test, so toon reaches the contaminated mips at a much
  closer distance than realistic does. With `alphaIsTransparency` on, those deep mips are now
  the correct colour, so the blur costs sharpness but no longer causes a colour shift.

  Secondary, **not** changed: on hair-shader materials `_PosterizeBlur` (2.5) samples colour
  ~2.5 mips deeper than the alpha test, which lifts brow brightness by +14% at mip 5 and +23% at
  mip 6. It is a real contributor to the "too light" look but much smaller than the coverage
  collapse, and since `_PosterizeStrength` is 0 the blur's only remaining effect is softening
  albedo. If brows still read pale at distance after the coverage fix, the lever is a
  brow-specific blur constant in `ToonifyAvatar.cs` — deliberately not applied blind, because
  lowering it also sharpens the brows close up, where they currently look right.

Alternatives discussed but not implemented: quantized view-space fresnel rim band
(screen-relative form shadow); re-enabling gentle texture banding on the head material only
(`_PosterizeStrength` ≈ 0.3, `_AlbedoFlatten` ≈ 0.85) for more "color areas" now that
posterize is hue-safe.
