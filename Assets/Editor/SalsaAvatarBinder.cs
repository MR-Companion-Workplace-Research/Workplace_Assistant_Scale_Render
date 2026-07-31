// SalsaAvatarBinder — rebinds a freshly-built CC/Reallusion avatar prefab to SALSA.
//
// WHY THIS EXISTS: The new avatars (new_models/{female,male}.Fbx) were made by dragging
// the FBX in and PASTING the SALSA components (Salsa/Emoter/Eyes/QueueProcessor) from the
// working prefab. Pasting copies value fields (blend indices, timings) but NOT object
// references, so every viseme/emote ended up with a null SkinnedMeshRenderer (smr) and the
// Eyes head/eye rig lost its bones + generated gizmos. Result: mouth stuck open (SALSA can't
// drive the face mesh) and no head tracking (no head bone / rig).
//
// This tool re-points those references to the NEW avatar's mesh + bones and rebuilds the
// Eyes head rig with SALSA's own API (mirrors the boxHead OneClick, adapted for CC bones).
// If the Salsa has visemes, it just re-points their SMR (keeps tuning); if the viseme list is
// EMPTY (the paste didn't carry visemes), it rebuilds the visemes from the reference prefab
// (F_mini_real), matching each shape by blendshape NAME so it works across CC meshes.
//
// USAGE: open the prefab in Prefab Mode (or drop it in a scene), select the avatar ROOT,
// then Tools > Study > Bind SALSA To New Avatar. Save the prefab afterwards (Ctrl+S).
//
// NOTE: not undoable — it edits/rebuilds components. Prefer running on a version-controlled
// prefab so you can revert if needed.

using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using CrazyMinnow.SALSA;

namespace StudyTools
{
    public static class SalsaAvatarBinder
    {
        // CC/Reallusion naming — matches both new_models and white_female_young rigs.
        const string FaceMeshName = "CC_Base_Body";
        const string HeadBoneName = "CC_Base_Head";
        const string LEyeBoneName = "CC_Base_L_Eye";
        const string REyeBoneName = "CC_Base_R_Eye";

        // Working reference prefab (F_mini_real) — source of the 13 lip-sync visemes when the
        // target Salsa has none. GUID is stable; falls back to a project search by name.
        const string ReferencePrefabGuid = "a28a5fa9dd53b47479bef39b158d5a8a";

        // Eyes tuning copied from the working F_mini_real prefab. headTargetOffset is the only
        // scale-sensitive one; if the head "look" sits off, nudge this in the Eyes inspector.
        static readonly Vector3 HeadTargetOffset = new Vector3(0f, 0.065f, 0f);
        const float HeadTargetRadius = 0.01f;
        static readonly Vector2 HeadRandDistRange = new Vector2(1f, 1f);
        const float EyeTargetRadius = 0.01f;
        static readonly Vector3 EyeRandTrackFov = new Vector3(0.1f, 0.05f, 0f); // SALSA field is Vector3
        static readonly Vector2 EyeRandDistRange = new Vector2(1f, 1f);

        // Attempt eye-darting + blink (best-effort, fully guarded). Head tracking + lip sync
        // are done regardless of whether this part succeeds.
        const bool SetupEyeMovement = true;

        [MenuItem("Tools/Study/Bind SALSA To New Avatar", false, 40)]
        public static void Bind()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorUtility.DisplayDialog("Bind SALSA To New Avatar",
                    "Select the avatar ROOT (the GameObject that holds the SALSA components), " +
                    "ideally while the prefab is open in Prefab Mode.", "OK");
                return;
            }

            // 1) Unpack any nested model/FBX instance so meshes, bones and new gizmos are concrete.
            int unpacked = UnpackNestedInstances(go);

            // 2) Locate the face mesh + bones on the (now unpacked) hierarchy.
            var faceSmr = FindFaceSmr(go);
            if (faceSmr == null)
            {
                EditorUtility.DisplayDialog("Bind SALSA To New Avatar",
                    "Could not find a face SkinnedMeshRenderer (looked for '" + FaceMeshName +
                    "' / the mesh with the most blendshapes). Make sure the avatar mesh is under this object.",
                    "OK");
                return;
            }
            var headBone = FindByName(go, HeadBoneName);
            var lEye = FindByName(go, LEyeBoneName);
            var rEye = FindByName(go, REyeBoneName);

            // 3) QueueProcessor is shared by Salsa/Emoter/Eyes.
            var qp = go.GetComponent<QueueProcessor>() ?? go.AddComponent<QueueProcessor>();

            // 4) Lip sync — build visemes (if empty) or re-point their SMR.
            string salsaResult = RebindSalsa(go, faceSmr, qp);

            // 5) Emotes — rebind the Emoter the same way.
            int emoteVars = RebindEmoter(go, faceSmr, qp);

            // 6) Head/eye tracking — rebuild the Eyes rig against the new bones.
            string eyesResult = RebuildEyes(go, faceSmr, headBone, lEye, rEye, qp);

            // 7) Companion face meshes — CC exports the brows (Female_Angled), eye
            // occlusion, tear line/ducts and tongue as SEPARATE meshes that carry the
            // SAME blendshape names as CC_Base_Body and must be driven in sync.
            // Driving only the body makes the forehead skin rise through the static
            // brow mesh during emotes (and lids blink under a static tear line).
            int companions = AddCompanionShapeControllers(go, faceSmr);

            EditorUtility.SetDirty(go);
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(stage.scene);

            string summary =
                "SALSA bound to '" + go.name + "'.\n\n" +
                "Unpacked nested instances: " + unpacked + "\n" +
                "Face mesh: " + faceSmr.name + " (" + faceSmr.sharedMesh.blendShapeCount + " blendshapes)\n" +
                "Lip sync: " + salsaResult + "\n" +
                "Emotes:   rebound " + emoteVars + " emote controllers\n" +
                "Companion meshes: " + companions + " synced shape controllers (brows/occlusion/tearline/tongue)\n" +
                "Head bone: " + (headBone ? headBone.name : "MISSING") + "\n" +
                "Eyes: " + eyesResult + "\n\n" +
                "Save the prefab now (Ctrl+S). Head look-target + external audio are wired at runtime by AvatarPlacer.";
            Debug.Log("[SalsaAvatarBinder] " + summary.Replace("\n", " | "));
            EditorUtility.DisplayDialog("Bind SALSA To New Avatar", summary, "OK");
        }

        // -------- humanoid jaw unmapping --------
        //
        // WHY: the new FBXs (new_models/{female,male}.Fbx) import with an AUTO-generated
        // humanoid mapping (meta has `human: []`), and Unity's auto-mapper maps
        // CC_Base_JawRoot to the humanoid Jaw muscle bone. The Animator then owns the jaw
        // every frame: humanoid clips recorded from the OLD avatar (e.g. my_sitting.anim)
        // carry a "Jaw Close = 0" muscle curve, and 0 on this rig poses the jaw OPEN ->
        // the mouth hangs wide open while SALSA's blendshape lip-sync plays on top.
        // The old avatar (Young_cami.Fbx) had a hand-configured human bone list WITHOUT
        // the Jaw (Head/Neck/LeftEye/RightEye mapped, jaw left as a plain transform whose
        // muscle curves are discarded) — which is why it never had this problem.
        //
        // FIX: make the human description explicit (copied from the generated Avatar) and
        // drop the Jaw entry, then reimport. Run once per new FBX; safe to re-run.

        [MenuItem("Tools/Study/Unmap Humanoid Jaw On Selected FBX", false, 41)]
        public static void UnmapJaw()
        {
            var models = Selection.objects
                .Select(AssetDatabase.GetAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => (path: p, importer: AssetImporter.GetAtPath(p) as ModelImporter))
                .Where(t => t.importer != null)
                .ToArray();
            if (models.Length == 0)
            {
                EditorUtility.DisplayDialog("Unmap Humanoid Jaw",
                    "Select the model asset(s) in the Project window first " +
                    "(new_models/female.Fbx and male.Fbx).", "OK");
                return;
            }

            int changed = 0;
            foreach (var (path, importer) in models)
            {
                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    Debug.LogWarning("[SalsaAvatarBinder] '" + path + "' is not a Humanoid rig — skipped.");
                    continue;
                }

                // Effective mapping: the explicit list if one exists, else the auto-generated
                // Avatar's (meta `human: []` means auto-mapped at import).
                var hd = importer.humanDescription;
                if (hd.human == null || hd.human.Length == 0)
                {
                    var avatar = AssetDatabase.LoadAllAssetsAtPath(path)
                        .OfType<Avatar>().FirstOrDefault();
                    if (avatar == null || !avatar.isHuman)
                    {
                        Debug.LogWarning("[SalsaAvatarBinder] No humanoid Avatar found in '" + path + "' — skipped.");
                        continue;
                    }
                    hd = avatar.humanDescription;
                }

                int before = hd.human.Length;
                hd.human = hd.human.Where(hb => hb.humanName != "Jaw").ToArray();
                if (hd.human.Length == before)
                {
                    Debug.Log("[SalsaAvatarBinder] '" + path + "' has no Jaw mapping — nothing to do.");
                    continue;
                }

                importer.humanDescription = hd;
                importer.SaveAndReimport();
                changed++;
                Debug.Log("[SalsaAvatarBinder] Unmapped humanoid Jaw on '" + path + "' (" +
                          before + " -> " + hd.human.Length + " human bones) and reimported.");
            }

            EditorUtility.DisplayDialog("Unmap Humanoid Jaw",
                changed > 0
                    ? changed + " model(s) updated: the Jaw is no longer a humanoid muscle bone, " +
                      "so the Animator stops holding the mouth open. Prefabs pick this up automatically."
                    : "No models changed (not humanoid / no Jaw mapping / no Avatar). See Console.",
                "OK");
        }

        // -------- hierarchy helpers --------

        static int UnpackNestedInstances(GameObject root)
        {
            int count = 0;
            // Repeat until stable — unpacking can expose further nested instances.
            for (int pass = 0; pass < 8; pass++)
            {
                var instanceRoot = root.GetComponentsInChildren<Transform>(true)
                    .Select(t => t.gameObject)
                    .FirstOrDefault(g => PrefabUtility.IsAnyPrefabInstanceRoot(g));
                if (instanceRoot == null) break;
                try
                {
                    PrefabUtility.UnpackPrefabInstance(instanceRoot, PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                    count++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SalsaAvatarBinder] Could not unpack '" + instanceRoot.name + "': " + e.Message);
                    break;
                }
            }
            return count;
        }

        static SkinnedMeshRenderer FindFaceSmr(GameObject root)
        {
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s.sharedMesh != null).ToArray();
            var named = smrs.FirstOrDefault(s =>
                s.name.Equals(FaceMeshName, StringComparison.OrdinalIgnoreCase) &&
                s.sharedMesh.blendShapeCount > 0);
            if (named != null) return named;
            // Fallback: the mesh carrying the most blendshapes is the CC face/body mesh.
            return smrs.OrderByDescending(s => s.sharedMesh.blendShapeCount).FirstOrDefault();
        }

        static Transform FindByName(GameObject root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // -------- Salsa / Emoter rebind --------

        static string RebindSalsa(GameObject root, SkinnedMeshRenderer faceSmr, QueueProcessor qp)
        {
            var salsa = root.GetComponentInChildren<Salsa>(true);
            if (salsa == null) salsa = root.AddComponent<Salsa>();
            salsa.queueProcessor = qp;

            // No visemes defined (fresh/paste-without-visemes) -> build them from the reference.
            if (salsa.visemes == null || salsa.visemes.Count == 0)
            {
                int built = BuildVisemesFromReference(salsa, faceSmr, qp);
                salsa.configReady = built > 0;
                return built > 0
                    ? "built " + built + " visemes from reference"
                    : "NO visemes — reference prefab not found; set up Salsa visemes manually";
            }

            // Existing visemes -> just re-point their SMR (keeps tuning). Only broken
            // references are re-pointed: an smr that is null (paste) or foreign (points
            // outside this avatar). A valid companion-mesh binding (brows/tongue/...)
            // must survive re-runs.
            int n = 0;
            foreach (var viseme in salsa.visemes)
            {
                var cvs = viseme?.expData?.controllerVars;
                if (cvs == null) continue;
                foreach (var cv in cvs)
                    if (NeedsRepoint(cv, root)) { cv.smr = faceSmr; n++; }
            }
            salsa.configReady = true;
            return "rebound SMR on " + salsa.visemes.Count + " existing visemes (" + n + " controllers)";
        }

        // Recreate the lip-sync visemes on `target` by copying the reference Salsa's visemes,
        // matching each shape by blendshape NAME (robust even if the mesh's index order differs).
        static int BuildVisemesFromReference(Salsa target, SkinnedMeshRenderer targetFace, QueueProcessor qp)
        {
            var refSalsa = LoadReferenceSalsa(out var refFace);
            if (refSalsa == null || refSalsa.visemes == null)
            {
                Debug.LogWarning("[SalsaAvatarBinder] Could not load reference Salsa (F_mini_real). " +
                                 "Lip-sync visemes were NOT built — define them manually or fix the reference GUID.");
                return 0;
            }

            target.visemes.Clear();
            int built = 0, skipped = 0;
            foreach (var refVis in refSalsa.visemes)
            {
                if (refVis?.expData?.components == null) continue;
                var newVis = new LipsyncExpression(refVis.expData.name, new InspectorControllerHelperData(), 0f);
                var expr = newVis.expData;
                // Start from a clean slate so a ctor-provided default component can't leave a
                // stray shape driving blendshape 0.
                if (expr.components != null) expr.components.Clear();
                if (expr.controllerVars != null) expr.controllerVars.Clear();
                int comps = 0;
                for (int j = 0; j < refVis.expData.components.Count; j++)
                {
                    var refComp = refVis.expData.components[j];
                    var refCv = j < refVis.expData.controllerVars.Count ? refVis.expData.controllerVars[j] : null;
                    if (refComp == null || refCv == null) continue;
                    if (refComp.controlType != ExpressionComponent.ControlType.Shape) continue;

                    var srcMesh = (refCv.smr != null ? refCv.smr : refFace)?.sharedMesh;
                    if (srcMesh == null || refCv.blendIndex < 0 || refCv.blendIndex >= srcMesh.blendShapeCount) continue;
                    string bname = srcMesh.GetBlendShapeName(refCv.blendIndex);
                    int tIdx = targetFace.sharedMesh.GetBlendShapeIndex(bname);
                    if (tIdx < 0) { skipped++; continue; }

                    var newComp = new ExpressionComponent();
                    var newCv = new InspectorControllerHelperData();
                    CopyPublicFields(refComp, newComp);
                    CopyPublicFields(refCv, newCv);
                    newCv.smr = targetFace;       // re-point to THIS avatar's face mesh
                    newCv.blendIndex = tIdx;      // re-resolve index by name
                    expr.components.Add(newComp);
                    expr.controllerVars.Add(newCv);
                    comps++;
                }
                if (comps > 0) { target.visemes.Add(newVis); built++; }
            }

            target.queueProcessor = qp;
            try { target.DistributeTriggers(LerpEasings.EasingType.SquaredIn); }
            catch (Exception e) { Debug.LogWarning("[SalsaAvatarBinder] DistributeTriggers warning: " + e.Message); }
            if (skipped > 0)
                Debug.Log("[SalsaAvatarBinder] " + skipped + " viseme shape(s) skipped (blendshape name not on target mesh).");
            return built;
        }

        static Salsa LoadReferenceSalsa(out SkinnedMeshRenderer refFace)
        {
            refFace = null;
            string path = AssetDatabase.GUIDToAssetPath(ReferencePrefabGuid);
            if (string.IsNullOrEmpty(path))
            {
                // Fallback: any prefab named F_mini_real / female_real with a Salsa that has visemes.
                path = AssetDatabase.FindAssets("t:Prefab F_mini_real")
                    .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            }
            if (string.IsNullOrEmpty(path)) return null;
            var refRoot = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (refRoot == null) return null;
            var refSalsa = refRoot.GetComponentInChildren<Salsa>(true);
            if (refSalsa == null) return null;
            // reference face = the mesh its visemes drive
            foreach (var v in refSalsa.visemes)
            {
                if (v?.expData?.controllerVars == null) continue;
                foreach (var cv in v.expData.controllerVars)
                    if (cv.smr != null) { refFace = cv.smr; break; }
                if (refFace != null) break;
            }
            if (refFace == null) refFace = FindFaceSmr(refRoot);
            return refSalsa;
        }

        static void CopyPublicFields(object src, object dst)
        {
            foreach (var f in src.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                try { f.SetValue(dst, f.GetValue(src)); } catch { /* skip incompatible/read-only */ }
            }
        }

        static int RebindEmoter(GameObject root, SkinnedMeshRenderer faceSmr, QueueProcessor qp)
        {
            var emoter = root.GetComponentInChildren<Emoter>(true);
            if (emoter == null) return 0;
            emoter.queueProcessor = qp;
            var salsa = root.GetComponentInChildren<Salsa>(true);
            if (salsa != null) salsa.emoter = emoter;

            int n = 0;
            if (emoter.emotes != null)
            {
                foreach (var emote in emoter.emotes)
                {
                    var cvs = emote?.expData?.controllerVars;
                    if (cvs == null) continue;
                    // Only broken (null/foreign) references are re-pointed — see
                    // RebindSalsa. Bone controllers ignore the smr either way.
                    foreach (var cv in cvs)
                        if (NeedsRepoint(cv, root)) { cv.smr = faceSmr; n++; }
                }
            }
            return n;
        }

        /// <summary>A controller's smr needs re-pointing when it is null (component paste
        /// dropped the reference) or references a mesh outside this avatar's hierarchy.</summary>
        static bool NeedsRepoint(InspectorControllerHelperData cv, GameObject root)
        {
            if (cv == null) return false;
            if (cv.smr == null) return true;
            return !cv.smr.transform.IsChildOf(root.transform);
        }

        // -------- companion face meshes (brows / eye occlusion / tear line / tongue) --------
        //
        // CC exports several face-conforming meshes that duplicate CC_Base_Body's
        // blendshape NAMES and are meant to be animated in lock-step: the brow mesh
        // (e.g. "Female_Angled", 159 shapes), CC_Base_EyeOcclusion, CC_Base_TearLine,
        // CC_Base_Tear_Ducts and CC_Base_Tongue. SALSA only drives the meshes its
        // controllers point at, so with body-only bindings the forehead skin rises
        // THROUGH the static brow card during speaking emotes. For every face-mesh
        // Shape controller in the visemes, emotes and blinklids, this adds a cloned
        // controller per companion mesh that has a same-named blendshape. Idempotent:
        // existing (smr, blendIndex) pairs are skipped on re-runs.

        static int AddCompanionShapeControllers(GameObject root, SkinnedMeshRenderer faceSmr)
        {
            var companions = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s != faceSmr && s.sharedMesh != null && s.sharedMesh.blendShapeCount > 0)
                .ToArray();
            if (companions.Length == 0) return 0;

            int added = 0;
            var salsa = root.GetComponentInChildren<Salsa>(true);
            if (salsa != null && salsa.visemes != null)
                foreach (var v in salsa.visemes)
                    if (v?.expData != null)
                        added += SyncExpressionShapes(v.expData.components, v.expData.controllerVars, faceSmr, companions);

            var emoter = root.GetComponentInChildren<Emoter>(true);
            if (emoter != null && emoter.emotes != null)
                foreach (var e in emoter.emotes)
                    if (e?.expData != null)
                        added += SyncExpressionShapes(e.expData.components, e.expData.controllerVars, faceSmr, companions);

            var eyes = root.GetComponentInChildren<Eyes>(true);
            if (eyes != null && eyes.blinklids != null)
                foreach (var b in eyes.blinklids)
                    if (b?.expData != null)
                        added += SyncExpressionShapes(b.expData.components, b.expData.controllerVars, faceSmr, companions);

            return added;
        }

        static int SyncExpressionShapes(System.Collections.Generic.List<ExpressionComponent> components,
                                        System.Collections.Generic.List<InspectorControllerHelperData> cvs,
                                        SkinnedMeshRenderer faceSmr, SkinnedMeshRenderer[] companions)
        {
            if (components == null || cvs == null) return 0;
            int added = 0;
            int originalCount = Mathf.Min(components.Count, cvs.Count); // don't iterate what we append
            for (int i = 0; i < originalCount; i++)
            {
                var comp = components[i];
                var cv = cvs[i];
                if (comp == null || cv == null) continue;
                if (comp.controlType != ExpressionComponent.ControlType.Shape) continue;
                if (cv.smr != faceSmr) continue;   // companions mirror the FACE controllers only
                var mesh = cv.smr.sharedMesh;
                if (mesh == null || cv.blendIndex < 0 || cv.blendIndex >= mesh.blendShapeCount) continue;
                string shapeName = mesh.GetBlendShapeName(cv.blendIndex);

                foreach (var smr in companions)
                {
                    int idx = smr.sharedMesh.GetBlendShapeIndex(shapeName);
                    if (idx < 0) continue;

                    bool exists = false;
                    for (int j = 0; j < cvs.Count; j++)
                        if (cvs[j] != null && cvs[j].smr == smr && cvs[j].blendIndex == idx) { exists = true; break; }
                    if (exists) continue;

                    var newComp = new ExpressionComponent();
                    var newCv = new InspectorControllerHelperData();
                    CopyPublicFields(comp, newComp);
                    CopyPublicFields(cv, newCv);
                    newComp.name = comp.name + "_" + smr.name;
                    newCv.smr = smr;
                    newCv.blendIndex = idx;
                    components.Add(newComp);
                    cvs.Add(newCv);
                    added++;
                }
            }
            return added;
        }

        // -------- Eyes rebuild --------

        static string RebuildEyes(GameObject root, SkinnedMeshRenderer faceSmr,
                                  Transform headBone, Transform lEye, Transform rEye, QueueProcessor qp)
        {
            if (headBone == null)
                return "FAILED — head bone '" + HeadBoneName + "' not found";

            foreach (var old in root.GetComponentsInChildren<Eyes>(true).ToArray())
                UnityEngine.Object.DestroyImmediate(old);

            // Remove any leftover SALSA gizmo objects from a previous run so they don't stack.
            foreach (var t in root.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (t == null || t == root.transform) continue;
                if (t.name.IndexOf("Gizmo", StringComparison.OrdinalIgnoreCase) >= 0)
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
            }

            var eyes = root.AddComponent<Eyes>();
            eyes.characterRoot = root.transform;
            eyes.queueProcessor = qp;

            // HEAD — bone rotation, exactly as the boxHead OneClick does it (confirmed API).
            eyes.BuildHeadTemplate(Eyes.HeadTemplates.Bone_Rotation_XY);
            eyes.heads[0].expData.controllerVars[0].bone = headBone;
            eyes.heads[0].expData.name = "head";
            eyes.heads[0].expData.components[0].name = "head";
            eyes.headTargetOffset = HeadTargetOffset;
            eyes.headTargetRadius = HeadTargetRadius;
            eyes.headRandDistRange = HeadRandDistRange;
            eyes.CaptureMin(ref eyes.heads);
            eyes.CaptureMax(ref eyes.heads);

            string eyeMsg = "head tracking OK";
            if (SetupEyeMovement)
            {
                try { eyeMsg += "; " + SetupEyeDartAndBlink(eyes, faceSmr, lEye, rEye); }
                catch (Exception e)
                {
                    eyeMsg += "; eye-dart/blink skipped (" + e.Message + ")";
                }
            }

            try { eyes.Initialize(); }
            catch (Exception e) { Debug.LogWarning("[SalsaAvatarBinder] eyes.Initialize warning: " + e.Message); }

            return eyeMsg;
        }

        static string SetupEyeDartAndBlink(Eyes eyes, SkinnedMeshRenderer faceSmr, Transform lEye, Transform rEye)
        {
            string msg = "";

            // EYES: CC uses bone-rotation eyes (not blendshapes). Discover the bone template
            // by name so this compiles against any SALSA version.
            if (lEye != null && rEye != null && TryGetEyeBoneTemplate(out var boneTpl))
            {
                eyes.BuildEyeTemplate(boneTpl);
                if (eyes.eyes != null && eyes.eyes.Count >= 1)
                    eyes.eyes[0].expData.controllerVars[0].bone = lEye;
                if (eyes.eyes != null && eyes.eyes.Count >= 2)
                    eyes.eyes[1].expData.controllerVars[0].bone = rEye;
                eyes.eyeTargetRadius = EyeTargetRadius;
                eyes.eyeRandTrackFov = EyeRandTrackFov;
                eyes.eyeRandDistRange = EyeRandDistRange;
                eyes.CaptureMin(ref eyes.eyes);
                eyes.CaptureMax(ref eyes.eyes);
                msg += "eye-dart OK";
            }
            else
            {
                msg += "eye-dart skipped (bone template or eye bones missing)";
            }

            // BLINK: CC blink blendshapes on the face mesh.
            // NOTE: SALSA's eyelid template creates ONE blinklid expression PER EYE
            // (blinklids[0] = left, blinklids[1] = right), each with a single
            // controllerVar — NOT one expression with two controllerVars. Earlier
            // versions of this tool bound only controllerVars[1] of the first lid
            // (which doesn't exist), leaving blinklids[1] with a null smr -> only the
            // left eye blinked. Bind each lid expression explicitly, with the old
            // controllerVars[1] layout kept as a fallback for other SALSA versions.
            int blinkL = FirstBlendIndex(faceSmr, "Eye_Blink_L", "Eye_Blink", "A06_Eye_Blink_Left", "Eye_Blink_Left");
            int blinkR = FirstBlendIndex(faceSmr, "Eye_Blink_R", "A07_Eye_Blink_Right", "Eye_Blink_Right");
            if (blinkL >= 0)
            {
                eyes.BuildEyelidTemplate(Eyes.EyelidTemplates.BlendShapes, Eyes.EyelidSelection.Upper);
                if (eyes.blinklids != null && eyes.blinklids.Count >= 1)
                {
                    var cv = eyes.blinklids[0].expData.controllerVars[0];
                    cv.smr = faceSmr;
                    cv.blendIndex = blinkL;

                    bool rightBound = false;
                    if (blinkR >= 0)
                    {
                        if (eyes.blinklids.Count >= 2 &&
                            eyes.blinklids[1].expData.controllerVars.Count >= 1)
                        {
                            var cv2 = eyes.blinklids[1].expData.controllerVars[0];
                            cv2.smr = faceSmr;
                            cv2.blendIndex = blinkR;
                            rightBound = true;
                        }
                        else if (eyes.blinklids[0].expData.controllerVars.Count >= 2)
                        {
                            var cv2 = eyes.blinklids[0].expData.controllerVars[1];
                            cv2.smr = faceSmr;
                            cv2.blendIndex = blinkR;
                            rightBound = true;
                        }
                    }
                    msg += rightBound ? "; blink OK (both eyes)"
                                      : "; blink LEFT ONLY (right shape or lid slot missing)";
                }
            }
            else
            {
                msg += "; blink skipped (no Eye_Blink blendshape)";
            }

            return msg;
        }

        static bool TryGetEyeBoneTemplate(out Eyes.EyeTemplates tpl)
        {
            tpl = default;
            foreach (var v in Enum.GetValues(typeof(Eyes.EyeTemplates)))
            {
                if (v.ToString().IndexOf("Bone", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tpl = (Eyes.EyeTemplates)v;
                    return true;
                }
            }
            return false;
        }

        static int FirstBlendIndex(SkinnedMeshRenderer smr, params string[] names)
        {
            if (smr == null || smr.sharedMesh == null) return -1;
            foreach (var n in names)
            {
                int i = smr.sharedMesh.GetBlendShapeIndex(n);
                if (i >= 0) return i;
            }
            return -1;
        }
    }
}
