# Binding SALSA onto a new CC avatar — what broke and why the tool exists

**Tool:** `SalsaAvatarBinder.cs` → menu **Tools ▸ Study ▸ Bind SALSA To New Avatar**
Run it in **Prefab Mode** on the avatar root of the `_real` prefabs only.

## The situation

New avatars were built by dropping a fresh CC/Reallusion FBX (`new_models/{female,male}.Fbx`)
into a prefab and **pasting** the SALSA component stack (Salsa / Emoter / Eyes / QueueProcessor)
from a working prefab. Visually the components were "there," but lip sync and head tracking
didn't work: the mouth hung open and the head never turned toward the camera rig.

## The core concept: paste copies values, not references

Unity's copy/paste on components brings across **serialized value fields** but **not object
references into the scene/prefab** (mesh renderers, bones). So the paste left us with SALSA
configs that looked complete but pointed at nothing:

- Every viseme/emote binding (`controllerVars[].smr`) came out **null** → SALSA had no mesh to
  drive → **mouth stuck open**.
- The `Eyes` module lost its **head/eye bones** → **no head tracking**.

The blend-shape *indices* survive the paste (both old and new are CC exports with the same
blend-shape order), so the data was fine — only the **bindings** were broken. That framing is
what the tool acts on: don't rebuild the config, just **re-point it at the new avatar's parts**.

## What the tool does (conceptually)

1. **Make the parts concrete.** Unpack the nested FBX instance so the meshes, bones, and any
   generated gizmos are real objects the binder can reference and save into the prefab.
2. **Find the anchors by name.** Locate the CC standard nodes: `CC_Base_Body` (the face mesh
   holding the blend shapes) and the tracking bones `CC_Base_Head` / `CC_Base_L_Eye` /
   `CC_Base_R_Eye`.
3. **Re-point lip sync.** Walk every Salsa viseme and Emoter expression and set its mesh
   reference back to the face mesh (indices kept as-is).
4. **Rebuild the eye/head rig.** The pasted `Eyes` module is too broken to patch, so destroy it
   and rebuild it through SALSA's own API (bone-rotation head template + assign the head bone +
   capture min/max), mirroring how SALSA's own one-click setup does it. Eye-dart and blink are
   added best-effort and guarded, so head tracking still lands even if the extras fail.

## The viseme gotcha (the second fix)

On the new prefabs the pasted Salsa had an **empty viseme list** — the inspector said
*"NOT READY: Visemes have not been defined."* Re-pointing the mesh does nothing when there are
no visemes to point. So the tool detects an empty list and **rebuilds the 13 visemes from the
known-good reference prefab** (`F_mini_real`), matching each shape by blend-shape **name** rather
than index (resolve the ref shape's name, then look that name up on the new mesh) so it stays
correct even if two exports differ. If visemes already exist, it leaves them alone and only
re-points the mesh, preserving any hand tuning.

## The blink gotcha (the third fix)

After binding, only the **left** eye blinked. SALSA's eyelid template creates **one blinklid
expression per eye** (`blinklids[0]` = left, `blinklids[1]` = right), each with a single
controller — not one expression with two controllers. The tool used to look for the right eye
at `blinklids[0].controllerVars[1]`, which doesn't exist, so `blinklids[1]` kept a null mesh
reference and the right eye never closed. The tool now binds each lid expression explicitly
(left → `Eye_Blink_L`, right → `Eye_Blink_R`). **Re-run the binder** on the `_real` prefabs to
apply; the summary dialog should say *"blink OK (both eyes)"*.

## The jaw gotcha (the fourth fix) — mouth wide open despite working lip sync

Not a SALSA problem at all. The new FBXs import with an **auto-generated humanoid mapping**
(`human: []` in the .meta), and Unity's auto-mapper maps `CC_Base_JawRoot` to the humanoid
**Jaw muscle bone**. The Animator then poses the jaw every frame: `my_sitting.anim` carries a
`Jaw Close = 0` muscle curve, and muscle value 0 on this rig is an **open** jaw. SALSA's
blendshape lip-sync still animates the lips on top — hence "lip sync works but the mouth hangs
open". The old avatar (`Young_cami.Fbx`) never had this because its hand-configured human bone
list mapped Head/Neck/LeftEye/RightEye but **not** the Jaw, so the jaw muscle curves were
discarded.

**Fix:** select `new_models/female.Fbx` + `male.Fbx` in the Project window, then
**Tools ▸ Study ▸ Unmap Humanoid Jaw On Selected FBX**. It copies the auto-generated mapping,
drops the Jaw entry, and reimports — the jaw becomes a plain transform again (Unity will log
the same "rotation animation discarded" info the old avatar showed, which is correct).
Run once per FBX; safe to re-run.

## The companion-mesh gotcha (the fifth fix) — eyebrows sink under the forehead

During speech, emotes raise the forehead (`Brow_Raise_*` on `CC_Base_Body`) and the skin rose
**through the eyebrows**. Cause: CC exports the brows as a separate conforming mesh
(`Female_Angled`, 159 blendshapes) that duplicates the body's blendshape *names* and is meant
to be animated in lock-step — same for `CC_Base_EyeOcclusion`, `CC_Base_TearLine`,
`CC_Base_Tear_Ducts` and `CC_Base_Tongue`. The binder only pointed controllers at
`CC_Base_Body`, so the body deformed while the attachments stayed frozen. (The old avatar's
reference prefab only ever drove body + tongue, so this never showed there.)

The tool now runs a **companion sync pass** after binding: for every face-mesh Shape
controller in the visemes, emotes and blinklids, it clones the controller onto each other
mesh that has a same-named blendshape (matched by name, amplitudes copied). It is idempotent
— existing (mesh, shape) pairs are skipped — and the rebind steps now only re-point *broken*
(null/foreign) references, so companion bindings survive re-runs. This also fixes the tear
line / eye occlusion not following blinks and the tongue not following visemes.
**Re-run the binder** on the `_real` prefabs; the dialog reports "Companion meshes: N synced
shape controllers".

## Notes

- Run on the two `_real` prefabs only. The `_toon` prefabs are **variants** of them and inherit
  the fix. The unpack can perturb toon material overrides — re-run **Tools ▸ Study ▸ Toonify** if
  the toon look reverts.
- Re-runs are safe: leftover gizmo children are cleared before the eye rig is rebuilt, so
  duplicates don't stack.
- Save the prefab (Ctrl+S) after binding.
