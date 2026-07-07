using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace GabeGin.PoseTools
{
    /// <summary>
    /// One captured bone's local transform. Stored with BOTH its name and its
    /// hierarchy path so the pose can be re-mapped onto the same rig (by name)
    /// or disambiguated across identical rigs / duplicate names (by path).
    /// </summary>
    [Serializable]
    public struct BonePose
    {
        public string name;             // Transform.name at capture time
        public string path;             // path relative to the capture root, e.g. "Hips/Spine"
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;

        /// <summary>A key that is unique across the buffer (path if we have one, else name).</summary>
        public string Key
        {
            get { return string.IsNullOrEmpty(path) ? "name:" + name : "path:" + path; }
        }
    }

    [Serializable]
    class PoseData
    {
        public List<BonePose> bones = new List<BonePose>();
    }

    /// <summary>
    /// Holds the copied pose.
    ///
    /// The buffer is a static, session-persistent store: it survives selection
    /// changes AND domain reloads (script recompiles, entering/exiting play
    /// mode) by round-tripping through <see cref="SessionState"/>. It is
    /// intentionally cleared only when the editor is fully restarted, which
    /// matches how a clipboard should behave.
    /// </summary>
    public static class PoseBuffer
    {
        const string kSessionKey = "GabeGin.PoseTools.Buffer.v1";

        static PoseData s_data;

        // Lookup acceleration structures, rebuilt whenever the buffer changes.
        static Dictionary<string, int> s_nameCounts;         // name -> occurrences (ambiguity test)
        static Dictionary<string, BonePose> s_byName;        // name -> entry (use only when unique)
        static Dictionary<string, BonePose> s_byPath;        // path -> entry (always unique)

        public static bool HasPose { get { return Data.bones.Count > 0; } }
        public static int Count { get { return Data.bones.Count; } }
        public static IEnumerable<BonePose> All { get { return Data.bones; } }

        static PoseData Data
        {
            get
            {
                if (s_data == null) Load();
                return s_data;
            }
        }

        // ----------------------------------------------------------------- persistence

        static void Load()
        {
            string json = SessionState.GetString(kSessionKey, "");
            s_data = string.IsNullOrEmpty(json) ? new PoseData() : JsonUtility.FromJson<PoseData>(json);
            if (s_data == null) s_data = new PoseData();
            RebuildLookups();
        }

        static void Save()
        {
            SessionState.SetString(kSessionKey, JsonUtility.ToJson(s_data));
            RebuildLookups();
        }

        static void RebuildLookups()
        {
            s_nameCounts = new Dictionary<string, int>();
            s_byName = new Dictionary<string, BonePose>();
            s_byPath = new Dictionary<string, BonePose>();
            foreach (var b in s_data.bones)
            {
                int c;
                s_nameCounts.TryGetValue(b.name, out c);
                s_nameCounts[b.name] = c + 1;
                s_byName[b.name] = b;                              // last-wins; only trusted when count==1
                if (!string.IsNullOrEmpty(b.path)) s_byPath[b.path] = b;
            }
        }

        // ----------------------------------------------------------------- capture

        /// <summary>Capture the local TRS of every supplied bone into the buffer.</summary>
        public static void Capture(Transform root, IList<Transform> bones)
        {
            var data = new PoseData();
            for (int i = 0; i < bones.Count; i++)
            {
                var t = bones[i];
                if (t == null) continue;
                var entry = new BonePose();
                entry.name = t.name;
                entry.path = PoseSkeleton.GetRelativePath(t, root);
                entry.localPosition = t.localPosition;
                entry.localRotation = t.localRotation;
                entry.localScale = t.localScale;
                data.bones.Add(entry);
            }
            s_data = data;
            Save();
        }

        public static void Clear()
        {
            s_data = new PoseData();
            Save();
        }

        // ----------------------------------------------------------------- lookup

        /// <summary>
        /// Resolve the stored pose for a target bone (straight paste).
        ///
        /// Primary key is the bone NAME. When that name is ambiguous in the
        /// buffer (two source bones shared it) we fall back to the exact
        /// hierarchy PATH to select the correct entry rather than risk applying
        /// the wrong bone's transform.
        /// </summary>
        public static bool TryResolveForTarget(Transform bone, Transform root, out BonePose pose)
        {
            EnsureLoaded();
            string name = bone.name;
            int count;
            s_nameCounts.TryGetValue(name, out count);

            if (count == 1)
                return s_byName.TryGetValue(name, out pose);

            if (count > 1)
            {
                string path = PoseSkeleton.GetRelativePath(bone, root);
                if (s_byPath.TryGetValue(path, out pose)) return true;
            }

            pose = default(BonePose);
            return false;
        }

        /// <summary>
        /// Resolve a stored entry by NAME (flipped paste, where the mirror
        /// partner is defined purely by a name-suffix swap). When the name is
        /// ambiguous a path hint is used to disambiguate; otherwise a best-effort
        /// name match is returned.
        /// </summary>
        public static bool TryResolveByName(string name, string pathHint, out BonePose pose)
        {
            EnsureLoaded();
            int count;
            s_nameCounts.TryGetValue(name, out count);

            if (count == 1)
                return s_byName.TryGetValue(name, out pose);

            if (count > 1)
            {
                if (pathHint != null && s_byPath.TryGetValue(pathHint, out pose)) return true;
                return s_byName.TryGetValue(name, out pose); // best effort
            }

            pose = default(BonePose);
            return false;
        }

        static void EnsureLoaded()
        {
            if (s_data == null) Load();
        }
    }
}
