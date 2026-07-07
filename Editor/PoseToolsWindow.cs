using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace GabeGin.PoseTools
{
    /// <summary>
    /// "Pose Tools" companion window — dock it beside Unity's Animation window.
    ///
    /// The built-in Animation window is internal/sealed and cannot host custom
    /// UI, so this is a separate EditorWindow. It never reflects into internal
    /// AnimationWindow APIs; all recording goes through the supported,
    /// version-stable path: require AnimationMode, edit transforms under
    /// Undo.RecordObject, and let Unity's recording auto-key on the current frame.
    /// </summary>
    public class PoseToolsWindow : EditorWindow
    {
        // ---- suffix presets (label / left / right) ----
        static readonly string[] kPresetNames = { "_L / _R", ".L / .R", "_Left / _Right", "Left / Right", "_l / _r", "Custom" };
        static readonly string[] kPresetLeft  = { "_L", ".L", "_Left", "Left", "_l", "" };
        static readonly string[] kPresetRight = { "_R", ".R", "_Right", "Right", "_r", "" };
        const int kCustomPreset = 5;

        PoseToolsSettings _settings;
        PairValidation _lastValidation;
        Vector2 _scroll;
        Vector2 _validationScroll;

        [MenuItem("Window/Animation/Pose Tools")]
        public static void Open()
        {
            var win = GetWindow<PoseToolsWindow>("Pose Tools");
            win.minSize = new Vector2(300f, 360f);
            win.Show();
        }

        void OnEnable()
        {
            _settings = PoseToolsSettings.Load();
        }

        // Keep the record-state / selection readout live even when the mouse is
        // idle over this window.
        void OnInspectorUpdate()
        {
            Repaint();
        }

        void SaveSettings()
        {
            if (_settings != null) _settings.Save();
        }

        // =================================================================
        //  GUI
        // =================================================================

        void OnGUI()
        {
            if (_settings == null) _settings = PoseToolsSettings.Load();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawStatusSection(out GameObject go, out int boneCount, out bool inAnimMode);
            EditorGUILayout.Space();
            DrawSuffixSection();
            EditorGUILayout.Space();
            DrawValidationSection(go);
            EditorGUILayout.Space();
            DrawMirrorSection();
            EditorGUILayout.Space();
            DrawActionSection(go, boneCount, inAnimMode);
            EditorGUILayout.Space();
            DrawBufferFooter();

            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------ status / readout

        void DrawStatusSection(out GameObject go, out int boneCount, out bool inAnimMode)
        {
            go = Selection.activeGameObject;
            inAnimMode = AnimationMode.InAnimationMode();
            boneCount = 0;

            EditorGUILayout.LabelField("Target & Record State", EditorStyles.boldLabel);

            if (go == null)
            {
                EditorGUILayout.HelpBox("Nothing selected. Select the rig (or any GameObject) you are animating.", MessageType.Warning);
            }
            else
            {
                var bones = PoseSkeleton.GetBones(go);
                boneCount = bones.Count;
                EditorGUILayout.LabelField("Editing", go.name);
                EditorGUILayout.LabelField("Bones detected", boneCount.ToString());
                if (boneCount == 0)
                    EditorGUILayout.HelpBox("No bones found under this selection (no SkinnedMeshRenderer bones and no child Transforms).", MessageType.Warning);
            }

            if (inAnimMode)
                EditorGUILayout.HelpBox("Animation record/preview is ACTIVE. Pastes will be keyed on the current frame of the clip being edited.", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Animation record is OFF. Enable Record in the Animation window first — paste is disabled until then.", MessageType.Warning);
        }

        // ------------------------------------------------ suffix convention

        void DrawSuffixSection()
        {
            EditorGUILayout.LabelField("Naming Convention", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            int newPreset = EditorGUILayout.Popup(new GUIContent("Preset", "Common left/right naming conventions. Pick Custom to type your own."), _settings.presetIndex, kPresetNames);
            if (newPreset != _settings.presetIndex)
            {
                _settings.presetIndex = newPreset;
                if (newPreset != kCustomPreset)
                {
                    _settings.suffix.left = kPresetLeft[newPreset];
                    _settings.suffix.right = kPresetRight[newPreset];
                }
                // switching TO Custom keeps the current strings as a starting point
            }

            using (new EditorGUI.DisabledScope(_settings.presetIndex != kCustomPreset))
            {
                _settings.suffix.left = EditorGUILayout.TextField(new GUIContent("Left suffix", "Affix that marks a left-side bone."), _settings.suffix.left);
                _settings.suffix.right = EditorGUILayout.TextField(new GUIContent("Right suffix", "Affix that marks a right-side bone."), _settings.suffix.right);
            }

            EditorGUILayout.HelpBox(
                "Bones ending in the Left suffix are swapped with the matching bone ending in the Right suffix when pasting flipped. " +
                "Center bones (matching neither) mirror onto themselves.",
                MessageType.None);

            _settings.suffix.caseSensitive = EditorGUILayout.Toggle(new GUIContent("Case sensitive", "If off, _l matches _L."), _settings.suffix.caseSensitive);
            _settings.suffix.isPrefix = EditorGUILayout.Toggle(new GUIContent("Match as prefix", "On: affix is at the START of the name (L_Arm / R_Arm). Off: at the END (Arm_L / Arm_R)."), _settings.suffix.isPrefix);

            if (EditorGUI.EndChangeCheck())
                SaveSettings();
        }

        // ------------------------------------------------ validate pairs

        void DrawValidationSection(GameObject go)
        {
            using (new EditorGUI.DisabledScope(go == null))
            {
                if (GUILayout.Button(new GUIContent("Validate / Preview Pairs", "List every detected L↔R pair and flag suffixed bones with no partner.")))
                {
                    var bones = PoseSkeleton.GetBones(go);
                    _lastValidation = PosePairs.Validate(bones, _settings.suffix);
                }
            }

            if (_lastValidation == null) return;

            EditorGUILayout.LabelField(
                string.Format("Pairs: {0}    Unmatched: {1}    Center: {2}",
                    _lastValidation.pairs.Count, _lastValidation.unmatched.Count, _lastValidation.centerCount),
                EditorStyles.miniBoldLabel);

            _validationScroll = EditorGUILayout.BeginScrollView(_validationScroll, GUILayout.MaxHeight(160f));

            foreach (var pair in _lastValidation.pairs)
            {
                string l = pair.left != null ? pair.left.name : "?";
                string r = pair.right != null ? pair.right.name : "?";
                EditorGUILayout.LabelField("✔ " + l + "  ↔  " + r);
            }

            if (_lastValidation.unmatched.Count > 0)
            {
                GUILayout.Space(2f);
                EditorGUILayout.LabelField("Unmatched (suffixed, no partner):", EditorStyles.miniBoldLabel);
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.6f, 0.4f);
                foreach (var t in _lastValidation.unmatched)
                    EditorGUILayout.LabelField("⚠ " + (t != null ? t.name : "?"));
                GUI.color = prev;
            }

            EditorGUILayout.EndScrollView();

            if (_lastValidation.unmatched.Count > 0)
                EditorGUILayout.HelpBox("Some suffixed bones have no partner. Those bones will mirror onto themselves when flipped. Check the suffix convention if that's unexpected.", MessageType.Warning);
        }

        // ------------------------------------------------ mirror settings

        void DrawMirrorSection()
        {
            EditorGUILayout.LabelField("Mirror", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var newAxis = (MirrorAxis)EditorGUILayout.EnumPopup(new GUIContent("Mirror axis", "Axis whose plane the pose is reflected across. Changing this reseeds the component signs below."), _settings.mirror.axis);
            if (EditorGUI.EndChangeCheck())
            {
                _settings.mirror.axis = newAxis;
                _settings.mirror.ApplyAxisDefaults();
                SaveSettings();
            }

            _settings.showAdvancedMirror = EditorGUILayout.Foldout(_settings.showAdvancedMirror, "Advanced (component signs)", true);
            if (_settings.showAdvancedMirror)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Which components get negated when mirroring. The correct signs depend on how your bones are oriented — tweak if a flip looks wrong, then re-test.", MessageType.None);

                EditorGUI.BeginChangeCheck();

                Vector3 ps = _settings.mirror.positionSign;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Negate position");
                bool px = NegToggle("X", ps.x < 0f);
                bool py = NegToggle("Y", ps.y < 0f);
                bool pz = NegToggle("Z", ps.z < 0f);
                EditorGUILayout.EndHorizontal();
                _settings.mirror.positionSign = new Vector3(px ? -1f : 1f, py ? -1f : 1f, pz ? -1f : 1f);

                Vector4 rs = _settings.mirror.rotationSign;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Negate rotation");
                bool rx = NegToggle("X", rs.x < 0f);
                bool ry = NegToggle("Y", rs.y < 0f);
                bool rz = NegToggle("Z", rs.z < 0f);
                bool rw = NegToggle("W", rs.w < 0f);
                EditorGUILayout.EndHorizontal();
                _settings.mirror.rotationSign = new Vector4(rx ? -1f : 1f, ry ? -1f : 1f, rz ? -1f : 1f, rw ? -1f : 1f);

                if (EditorGUI.EndChangeCheck())
                    SaveSettings();

                if (GUILayout.Button("Reset signs to axis default"))
                {
                    _settings.mirror.ApplyAxisDefaults();
                    SaveSettings();
                }
                EditorGUI.indentLevel--;
            }
        }

        static bool NegToggle(string label, bool value)
        {
            return GUILayout.Toggle(value, label, EditorStyles.miniButton, GUILayout.Width(32f));
        }

        // ------------------------------------------------ actions

        void DrawActionSection(GameObject go, int boneCount, bool inAnimMode)
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(go == null || boneCount == 0))
            {
                if (GUILayout.Button(new GUIContent("Copy Pose", "Capture every bone's local TRS into the buffer. (Alt+Shift+C)"), GUILayout.Height(26f)))
                    CopyPoseCommand();
            }

            // Paste needs record mode + a populated buffer.
            using (new EditorGUI.DisabledScope(!inAnimMode || !PoseBuffer.HasPose || go == null || boneCount == 0))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Paste Pose", "Apply the copied local TRS by bone name. (Alt+Shift+V)"), GUILayout.Height(26f)))
                    PastePoseCommand();
                if (GUILayout.Button(new GUIContent("Paste Flipped", "Apply the copied pose mirrored left↔right. (Alt+Shift+F)"), GUILayout.Height(26f)))
                    PasteFlippedCommand();
                EditorGUILayout.EndHorizontal();
            }

            if (!PoseBuffer.HasPose)
                EditorGUILayout.LabelField("Buffer is empty — Copy a pose first.", EditorStyles.miniLabel);
        }

        void DrawBufferFooter()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(PoseBuffer.HasPose ? ("Buffer: " + PoseBuffer.Count + " bones copied") : "Buffer: empty", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(!PoseBuffer.HasPose))
            {
                if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50f)))
                    PoseBuffer.Clear();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Shortcuts: Edit ▸ Shortcuts… → search \"Pose Tools\".", EditorStyles.miniLabel);
        }

        // =================================================================
        //  Commands (shared by buttons AND rebindable shortcuts)
        //  Static so the shortcuts work even if the window isn't open.
        // =================================================================

        public static void CopyPoseCommand()
        {
            var go = Selection.activeGameObject;
            if (go == null) { Debug.LogWarning("[Pose Tools] Copy failed: nothing selected."); return; }

            var bones = PoseSkeleton.GetBones(go);
            if (bones.Count == 0) { Debug.LogWarning("[Pose Tools] Copy failed: no bones under '" + go.name + "'."); return; }

            PoseBuffer.Capture(go.transform, bones);
            Debug.Log("[Pose Tools] Copied pose: " + bones.Count + " bones from '" + go.name + "'.");
            RepaintOpen();
        }

        public static void PastePoseCommand()
        {
            GameObject go; List<Transform> bones; Transform root;
            if (!PrepPaste("Paste Pose", out go, out bones, out root)) return;

            int group = BeginGroup("Paste Pose");

            var used = new HashSet<string>();
            int applied = 0;
            foreach (var b in bones)
            {
                BonePose pose;
                if (PoseBuffer.TryResolveForTarget(b, root, out pose))
                {
                    Undo.RecordObject(b, "Paste Pose");
                    b.localPosition = pose.localPosition;
                    b.localRotation = pose.localRotation;
                    b.localScale = pose.localScale;
                    applied++;
                    used.Add(pose.Key);
                }
            }

            EndGroup(group);
            int missing = PoseBuffer.Count - used.Count;
            LogPaste("Pasted pose onto", applied, missing);
            RepaintOpen();
        }

        public static void PasteFlippedCommand()
        {
            GameObject go; List<Transform> bones; Transform root;
            if (!PrepPaste("Paste Pose Flipped", out go, out bones, out root)) return;

            var settings = PoseToolsSettings.Load();
            int group = BeginGroup("Paste Pose Flipped");

            var used = new HashSet<string>();
            int applied = 0;
            foreach (var b in bones)
            {
                // The data applied to bone B is its MIRROR PARTNER's stored pose,
                // reflected across the mirror axis. Right bones read left data
                // (and vice-versa); center bones read their own data.
                string partnerName = settings.suffix.GetPartnerName(b.name);
                string pathHint = SwapLastSegment(PoseSkeleton.GetRelativePath(b, root), partnerName);

                BonePose pose;
                if (PoseBuffer.TryResolveByName(partnerName, pathHint, out pose))
                {
                    Undo.RecordObject(b, "Paste Pose Flipped");
                    b.localPosition = settings.mirror.MirrorPosition(pose.localPosition);
                    b.localRotation = settings.mirror.MirrorRotation(pose.localRotation);
                    b.localScale = pose.localScale; // scale is transferred, not mirrored
                    applied++;
                    used.Add(pose.Key);
                }
            }

            EndGroup(group);
            int missing = PoseBuffer.Count - used.Count;
            LogPaste("Pasted flipped pose onto", applied, missing);
            RepaintOpen();
        }

        // =================================================================
        //  Command helpers
        // =================================================================

        // Common front-end checks for both paste commands. Returns false (and
        // logs the reason) when the operation cannot proceed.
        static bool PrepPaste(string op, out GameObject go, out List<Transform> bones, out Transform root)
        {
            go = null; bones = null; root = null;

            if (!AnimationMode.InAnimationMode())
            {
                Debug.LogWarning("[Pose Tools] " + op + " blocked: enable Record in the Animation window first (AnimationMode is not active).");
                return false;
            }
            if (!PoseBuffer.HasPose)
            {
                Debug.LogWarning("[Pose Tools] " + op + " blocked: the pose buffer is empty. Copy a pose first.");
                return false;
            }
            go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.LogWarning("[Pose Tools] " + op + " blocked: nothing selected.");
                return false;
            }
            bones = PoseSkeleton.GetBones(go);
            if (bones.Count == 0)
            {
                Debug.LogWarning("[Pose Tools] " + op + " blocked: no bones under '" + go.name + "'.");
                return false;
            }
            root = go.transform;
            return true;
        }

        static int BeginGroup(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }

        // Collapse everything since BeginGroup into ONE undo step. While the
        // Animation window is recording, the auto-created keyframes are part of
        // the same group, so a single Undo/Redo reverts the whole paste.
        static void EndGroup(int group)
        {
            Undo.CollapseUndoOperations(group);
        }

        static void LogPaste(string verb, int applied, int missing)
        {
            string msg = "[Pose Tools] " + verb + " " + applied + " bones.";
            if (missing > 0) msg += " (" + missing + " buffered bones not found on the target rig — skipped.)";
            Debug.Log(msg);
        }

        // Replace the final "/segment" of a path with a new leaf name. Used to
        // build a best-effort path hint for the mirror partner when a bone name
        // is ambiguous in the buffer.
        static string SwapLastSegment(string path, string newLeaf)
        {
            if (string.IsNullOrEmpty(path)) return newLeaf;
            int slash = path.LastIndexOf('/');
            return slash >= 0 ? path.Substring(0, slash + 1) + newLeaf : newLeaf;
        }

        // Repaint any open Pose Tools window WITHOUT creating one (so shortcuts
        // fired with the window closed don't spawn it).
        static void RepaintOpen()
        {
            var wins = Resources.FindObjectsOfTypeAll<PoseToolsWindow>();
            for (int i = 0; i < wins.Length; i++)
                wins[i].Repaint();
        }
    }
}
