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

        // The rig currently being edited. Locked independently of Selection so
        // that clicking a child bone (here or in the Hierarchy) does NOT re-target
        // the tool onto that single bone's sub-tree — Copy/Paste keep operating on
        // the whole parent rig. This is what makes Paste Flipped able to see both
        // the _L and _R sides at once.
        Transform _rigRoot;

        // Bone-hierarchy browser state.
        Vector2 _boneTreeScroll;
        string _boneSearch = "";
        readonly HashSet<int> _collapsed = new HashSet<int>();
        GUIStyle _selectedBoneStyle;

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

            SyncRigRoot();

            GameObject target = ResolveTarget();
            bool inAnimMode = AnimationMode.InAnimationMode();
            List<Transform> rigBones = target != null ? PoseSkeleton.GetBones(target) : null;
            int boneCount = rigBones != null ? rigBones.Count : 0;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawStatusSection(target, boneCount, inAnimMode);
            EditorGUILayout.Space();
            DrawBoneTreeSection(target);
            EditorGUILayout.Space();
            DrawSuffixSection();
            EditorGUILayout.Space();
            DrawValidationSection(target);
            EditorGUILayout.Space();
            DrawMirrorSection();
            EditorGUILayout.Space();
            DrawActionSection(target, rigBones, boneCount, inAnimMode);
            EditorGUILayout.Space();
            DrawBufferFooter();

            EditorGUILayout.EndScrollView();
        }

        // The GameObject the tool acts on: the locked rig root, or (before a rig
        // is locked) whatever is selected, so the "no bones" hints still show.
        GameObject ResolveTarget()
        {
            if (_rigRoot != null) return _rigRoot.gameObject;
            return Selection.activeGameObject;
        }

        // Decide whether the current Selection should become the edit target.
        //   * Selection is the locked rig, or a descendant of it  -> keep the rig
        //     (this is the "clicking a child bone keeps the parent" behaviour).
        //   * Selection is something else that actually has bones  -> adopt it.
        //   * Selection has no bones (a light, an empty, ...)      -> keep the
        //     previously locked rig.
        void SyncRigRoot()
        {
            var sel = Selection.activeTransform;
            if (sel == null) return;
            if (_rigRoot != null && (sel == _rigRoot || sel.IsChildOf(_rigRoot)))
                return;
            if (PoseSkeleton.GetBones(sel.gameObject).Count > 0)
                _rigRoot = sel;
        }

        // ------------------------------------------------ status / readout

        void DrawStatusSection(GameObject target, int boneCount, bool inAnimMode)
        {
            EditorGUILayout.LabelField("Target & Record State", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var newRoot = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("Rig", "The rig being edited. Auto-locks when you select a rig; selecting one of its child bones keeps this parent as the target, so Copy/Paste still act on the whole rig. Drag a different root here to change it."),
                _rigRoot, typeof(Transform), true);
            if (EditorGUI.EndChangeCheck())
                _rigRoot = newRoot;

            if (target == null)
            {
                EditorGUILayout.HelpBox("No rig selected. Select the rig (or any bone under it) you are animating.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("Editing", target.name);
                EditorGUILayout.LabelField("Bones detected", boneCount.ToString());
                if (boneCount == 0)
                    EditorGUILayout.HelpBox("No bones found under this rig (no SkinnedMeshRenderer bones and no child Transforms).", MessageType.Warning);
            }

            if (inAnimMode)
                EditorGUILayout.HelpBox("Animation record/preview is ACTIVE. Pastes will be keyed on the current frame of the clip being edited.", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Animation record is OFF. Enable Record in the Animation window first — paste is disabled until then.", MessageType.Warning);
        }

        // ------------------------------------------------ bone hierarchy browser

        void DrawBoneTreeSection(GameObject target)
        {
            EditorGUILayout.LabelField("Bone Hierarchy", EditorStyles.boldLabel);

            if (target == null)
            {
                EditorGUILayout.HelpBox("Select a rig to browse and pick its bones here.", MessageType.None);
                return;
            }

            var root = target.transform;
            EnsureBoneStyles();

            // Toolbar: search + expand / collapse all.
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search", GUILayout.Width(46f));
            _boneSearch = EditorGUILayout.TextField(_boneSearch);
            if (GUILayout.Button(new GUIContent("+", "Expand all"), EditorStyles.miniButtonLeft, GUILayout.Width(24f)))
                _collapsed.Clear();
            if (GUILayout.Button(new GUIContent("−", "Collapse all"), EditorStyles.miniButtonRight, GUILayout.Width(24f)))
                CollapseAll(root);
            EditorGUILayout.EndHorizontal();

            _boneTreeScroll = EditorGUILayout.BeginScrollView(_boneTreeScroll, GUILayout.MinHeight(120f), GUILayout.MaxHeight(260f));

            if (string.IsNullOrEmpty(_boneSearch))
                DrawBoneNode(root, 0);
            else
                DrawFilteredBones(root, _boneSearch);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField("Click a bone to select it — the rig above stays the edit target.", EditorStyles.miniLabel);
        }

        void DrawBoneNode(Transform t, int depth)
        {
            if (t == null) return;

            int childCount = t.childCount;
            bool hasChildren = childCount > 0;
            int id = t.GetInstanceID();
            bool expanded = !_collapsed.Contains(id);
            bool showChildren = false;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 14f);

            if (hasChildren)
            {
                Rect fr = GUILayoutUtility.GetRect(12f, EditorGUIUtility.singleLineHeight, GUILayout.Width(12f));
                bool now = EditorGUI.Foldout(fr, expanded, GUIContent.none);
                if (now != expanded)
                {
                    if (now) _collapsed.Remove(id);
                    else _collapsed.Add(id);
                }
                showChildren = now;
            }
            else
            {
                GUILayout.Space(12f);
            }

            DrawBoneLabel(t);
            EditorGUILayout.EndHorizontal();

            if (showChildren)
                for (int i = 0; i < childCount; i++)
                    DrawBoneNode(t.GetChild(i), depth + 1);
        }

        void DrawFilteredBones(Transform root, string filter)
        {
            int shown = 0;
            string needle = filter.ToLowerInvariant();
            DrawFilteredRecursive(root, needle, ref shown);
            if (shown == 0)
                EditorGUILayout.LabelField("No bones match \"" + filter + "\".", EditorStyles.miniLabel);
        }

        void DrawFilteredRecursive(Transform t, string needle, ref int shown)
        {
            if (t == null) return;
            if (t.name.ToLowerInvariant().Contains(needle))
            {
                EditorGUILayout.BeginHorizontal();
                DrawBoneLabel(t);
                EditorGUILayout.EndHorizontal();
                shown++;
            }
            for (int i = 0; i < t.childCount; i++)
                DrawFilteredRecursive(t.GetChild(i), needle, ref shown);
        }

        // A clickable bone row. Selecting it pings the bone in the Scene/Hierarchy
        // but leaves the locked rig root untouched (see SyncRigRoot).
        void DrawBoneLabel(Transform t)
        {
            bool isSelected = Selection.activeTransform == t;
            var style = isSelected ? _selectedBoneStyle : EditorStyles.label;
            if (GUILayout.Button(new GUIContent(t.name), style, GUILayout.ExpandWidth(true)))
            {
                Selection.activeTransform = t;
                EditorGUIUtility.PingObject(t);
            }
        }

        void CollapseAll(Transform root)
        {
            _collapsed.Clear();
            CollapseRecursive(root, true);
        }

        void CollapseRecursive(Transform t, bool isRoot)
        {
            if (t == null) return;
            if (!isRoot && t.childCount > 0) _collapsed.Add(t.GetInstanceID());
            for (int i = 0; i < t.childCount; i++)
                CollapseRecursive(t.GetChild(i), false);
        }

        void EnsureBoneStyles()
        {
            if (_selectedBoneStyle != null) return;
            _selectedBoneStyle = new GUIStyle(EditorStyles.label);
            _selectedBoneStyle.fontStyle = FontStyle.Bold;
            var hi = new Color(0.42f, 0.72f, 1f);
            _selectedBoneStyle.normal.textColor = hi;
            _selectedBoneStyle.hover.textColor = hi;
        }

        // ------------------------------------------------ suffix convention

        void DrawSuffixSection()
        {
            EditorGUILayout.LabelField("Naming Convention", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            // Presets as inline segmented buttons (no dropdown arrow, one click).
            EditorGUILayout.LabelField(new GUIContent("Preset", "Common left/right naming conventions. Pick Custom to type your own."));
            int newPreset = GUILayout.SelectionGrid(_settings.presetIndex, kPresetNames, 3, EditorStyles.miniButton);
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
            var newAxis = (MirrorAxis)EditorGUILayout.EnumPopup(
                new GUIContent("Symmetry axis", "The rig's left/right axis, in the rig's own local space. Paste Flipped reflects the pose across the plane perpendicular to it — exactly like Blender's Paste X-Flipped. X is correct for virtually all humanoid / character rigs."),
                _settings.mirror.axis);
            if (EditorGUI.EndChangeCheck())
            {
                _settings.mirror.axis = newAxis;
                SaveSettings();
            }

            EditorGUILayout.HelpBox(
                "Flips across the rig's local " + _settings.mirror.axis + " plane, like Blender — no per-bone tuning. " +
                "Each bone's orientation is handled automatically because the mirror is computed in the rig's space, not each bone's local space.",
                MessageType.None);
        }

        // ------------------------------------------------ actions

        void DrawActionSection(GameObject go, List<Transform> rigBones, int boneCount, bool inAnimMode)
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _settings.flipSelectedOnly = EditorGUILayout.ToggleLeft(
                new GUIContent("Paste Flipped: selected bones only",
                    "On: Paste Flipped only writes to the bones you've selected in the Hierarchy. Each receives its mirror partner's copied pose; a center bone flips onto itself. Non-selected bones (including a selected bone's partner) are left alone. Select just the rig — or nothing — to flip the whole rig."),
                _settings.flipSelectedOnly);
            if (EditorGUI.EndChangeCheck())
                SaveSettings();

            if (_settings.flipSelectedOnly)
            {
                int sel = CountSelectedInRig(rigBones, go != null ? go.transform : null);
                EditorGUILayout.LabelField(
                    sel > 0
                        ? "Paste Flipped → " + sel + " selected bone(s)."
                        : "Paste Flipped → whole rig (no specific bones selected).",
                    EditorStyles.miniLabel);
            }

            using (new EditorGUI.DisabledScope(go == null || boneCount == 0))
            {
                if (GUILayout.Button(new GUIContent("Copy Pose", "Capture every bone's local TRS into the buffer. (Alt+Shift+C)"), GUILayout.Height(26f)))
                    CopyPose(go);
            }

            // Paste needs record mode + a populated buffer.
            using (new EditorGUI.DisabledScope(!inAnimMode || !PoseBuffer.HasPose || go == null || boneCount == 0))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Paste Pose", "Apply the copied local TRS by bone name. (Alt+Shift+V)"), GUILayout.Height(26f)))
                    PastePose(go);
                if (GUILayout.Button(new GUIContent("Paste Flipped", "Apply the copied pose mirrored left↔right. (Alt+Shift+F)"), GUILayout.Height(26f)))
                    PasteFlipped(go);
                EditorGUILayout.EndHorizontal();
            }

            if (!PoseBuffer.HasPose)
                EditorGUILayout.LabelField("Buffer is empty — Copy a pose first.", EditorStyles.miniLabel);
        }

        // Count Selection.transforms that are real bones of this rig (excluding
        // the rig root itself — selecting only the root means "the whole rig").
        static int CountSelectedInRig(List<Transform> rigBones, Transform root)
        {
            if (rigBones == null) return 0;
            var set = new HashSet<Transform>(rigBones);
            int n = 0;
            var sel = Selection.transforms;
            for (int i = 0; i < sel.Length; i++)
            {
                var s = sel[i];
                if (s != null && s != root && set.Contains(s)) n++;
            }
            return n;
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
        //  Commands
        //
        //  The *Command() entry points read Selection and are what the global
        //  rebindable shortcuts call (no window context available). The window's
        //  own buttons call the GameObject overloads directly with the locked rig
        //  root, so a paste always covers the whole rig even when a child bone is
        //  selected. Static so the shortcuts work even if the window isn't open.
        // =================================================================

        public static void CopyPoseCommand()    { CopyPose(ResolveActiveTarget()); }
        public static void PastePoseCommand()   { PastePose(ResolveActiveTarget()); }
        public static void PasteFlippedCommand() { PasteFlipped(ResolveActiveTarget()); }

        // Shortcuts have no window context; prefer an open Pose Tools window's
        // locked rig root so a keyboard paste covers the same rig the window
        // shows (and Paste Flipped still reads the Hierarchy selection for its
        // scope). Fall back to the raw Selection when no window is open.
        static GameObject ResolveActiveTarget()
        {
            var wins = Resources.FindObjectsOfTypeAll<PoseToolsWindow>();
            for (int i = 0; i < wins.Length; i++)
                if (wins[i]._rigRoot != null) return wins[i]._rigRoot.gameObject;
            return Selection.activeGameObject;
        }

        public static void CopyPose(GameObject go)
        {
            if (go == null) { Debug.LogWarning("[Pose Tools] Copy failed: nothing selected."); return; }

            var bones = PoseSkeleton.GetBones(go);
            if (bones.Count == 0) { Debug.LogWarning("[Pose Tools] Copy failed: no bones under '" + go.name + "'."); return; }

            PoseBuffer.Capture(go.transform, bones);
            Debug.Log("[Pose Tools] Copied pose: " + bones.Count + " bones from '" + go.name + "'.");
            RepaintOpen();
        }

        public static void PastePose(GameObject target)
        {
            GameObject go; List<Transform> bones; Transform root;
            if (!PrepPaste("Paste Pose", target, out go, out bones, out root)) return;

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

        public static void PasteFlipped(GameObject target)
        {
            GameObject go; List<Transform> bones; Transform root;
            if (!PrepPaste("Paste Pose Flipped", target, out go, out bones, out root)) return;

            var settings = PoseToolsSettings.Load();

            // Decide which bones get written. Default is the whole rig; when
            // "selected only" is on AND specific bones are selected, restrict to
            // that selection expanded with each bone's mirror partner.
            List<Transform> writeSet = bones;
            bool scoped = false;
            if (settings.flipSelectedOnly)
            {
                var sub = CollectSelectedRigBones(bones, root);
                if (sub.Count > 0) { writeSet = sub; scoped = true; }
                // else: nothing specific selected — fall through to the whole rig
            }

            // Parents before children: a child's mirrored local transform is
            // expressed relative to its (by then already-mirrored) live parent.
            SortParentsFirst(writeSet, root);

            Quaternion rootRot = root.rotation;

            int group = BeginGroup("Paste Pose Flipped");

            var used = new HashSet<string>();
            int applied = 0, noPartner = 0;
            foreach (var b in writeSet)
            {
                // The data applied to bone B is its MIRROR PARTNER's stored pose,
                // reflected across the symmetry plane. Right bones read left data
                // (and vice-versa); a CENTER bone is its own partner, so it reads
                // its own data and mirrors onto itself.
                string partnerName = settings.suffix.GetPartnerName(b.name);
                string pathHint = SwapLastSegment(PoseSkeleton.GetRelativePath(b, root), partnerName);

                BonePose pose;
                if (!PoseBuffer.TryResolveByName(partnerName, pathHint, out pose)) { noPartner++; continue; }

                // Mirror in ROOT space, then re-express in this bone's local space
                // relative to its live parent. Root-space (not parent-local) is
                // what makes the flip correct for any bone orientation — Blender's
                // behaviour — instead of needing per-bone sign tweaks.
                Vector3 worldPos = root.TransformPoint(settings.mirror.MirrorRootPosition(pose.rootPosition));
                Quaternion worldRot = rootRot * settings.mirror.MirrorRootRotation(pose.rootRotation);

                Undo.RecordObject(b, "Paste Pose Flipped");
                Transform parent = b.parent;
                if (parent != null)
                {
                    b.localPosition = parent.InverseTransformPoint(worldPos);
                    b.localRotation = Quaternion.Inverse(parent.rotation) * worldRot;
                }
                else
                {
                    b.position = worldPos;
                    b.rotation = worldRot;
                }
                b.localScale = pose.localScale; // scale is transferred, not mirrored

                applied++;
                used.Add(pose.Key);
            }

            EndGroup(group);
            LogFlipped(applied, noPartner, scoped);
            RepaintOpen();
        }

        // The bones a scoped flip writes to: exactly the rig bones selected in the
        // Hierarchy. The rig root is ignored, so selecting only the root (or
        // nothing) reads as "flip the whole rig" back in PasteFlipped. Each of
        // these bones pulls its mirror partner's pose from the buffer; a CENTER
        // bone is its own partner, so it flips onto itself. Non-selected bones —
        // including a selected bone's partner — are never written.
        static List<Transform> CollectSelectedRigBones(List<Transform> rigBones, Transform root)
        {
            var rigSet = new HashSet<Transform>(rigBones);
            var result = new List<Transform>();
            var added = new HashSet<Transform>();

            var sel = Selection.transforms;
            for (int i = 0; i < sel.Length; i++)
            {
                var s = sel[i];
                if (s == null || s == root || !rigSet.Contains(s)) continue;
                if (added.Add(s)) result.Add(s);
            }
            return result;
        }

        // Order bones so every bone comes after all of its ancestors, by depth
        // below the rig root. Paste Flipped writes each bone relative to its live
        // parent, so parents must be mirrored first.
        static void SortParentsFirst(List<Transform> set, Transform root)
        {
            set.Sort((a, b) => DepthBelow(a, root).CompareTo(DepthBelow(b, root)));
        }

        static int DepthBelow(Transform t, Transform root)
        {
            int d = 0;
            for (var c = t; c != null && c != root; c = c.parent) d++;
            return d;
        }

        // =================================================================
        //  Command helpers
        // =================================================================

        // Common front-end checks for both paste commands. Returns false (and
        // logs the reason) when the operation cannot proceed.
        static bool PrepPaste(string op, GameObject target, out GameObject go, out List<Transform> bones, out Transform root)
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
            go = target;
            if (go == null)
            {
                Debug.LogWarning("[Pose Tools] " + op + " blocked: no rig selected.");
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

        static void LogFlipped(int applied, int noPartner, bool scoped)
        {
            string scope = scoped ? "selected bones + partners" : "whole rig";
            string msg = "[Pose Tools] Pasted flipped pose onto " + applied + " bones (" + scope + ").";
            if (noPartner > 0)
                msg += " " + noPartner + " bone(s) had no mirror partner in the buffer — check the Naming Convention (use Validate / Preview Pairs) and Copy the whole rig first.";
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
