using System.Collections.Generic;
using UnityEngine;

namespace GabeGin.PoseTools
{
    /// <summary>
    /// Generic skeleton discovery. Makes NO assumptions about bone names, the
    /// name of the root, the rig type (biped / quadruped / prop) or the number
    /// of bones. Everything is derived from the currently selected GameObject.
    /// </summary>
    public static class PoseSkeleton
    {
        /// <summary>
        /// Return the bone set for a selection.
        ///
        /// Preference order:
        ///   1. Every <see cref="SkinnedMeshRenderer.bones"/> found in the
        ///      selection's children (the authored skin skeleton). Multiple
        ///      skinned meshes are unioned; duplicates are removed.
        ///   2. If there are no skinned meshes at all, fall back to EVERY
        ///      descendant Transform under the selection (raw hierarchy rig).
        ///
        /// The selection's own Transform is included in the fallback so a rig
        /// whose root itself is animated is handled too.
        /// </summary>
        public static List<Transform> GetBones(GameObject selection)
        {
            var result = new List<Transform>();
            if (selection == null) return result;

            var seen = new HashSet<Transform>();

            // 1) Skinned skeleton, if any.
            var smrs = selection.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr == null || smr.bones == null) continue;
                foreach (var b in smr.bones)
                    if (b != null && seen.Add(b))
                        result.Add(b);
            }
            if (result.Count > 0)
                return result;

            // 2) No skinned meshes: treat the whole Transform hierarchy as the rig.
            var all = selection.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
                if (t != null && seen.Add(t))
                    result.Add(t);
            return result;
        }

        /// <summary>
        /// Path of <paramref name="t"/> relative to <paramref name="root"/>,
        /// e.g. "Hips/Spine/Chest". The root itself returns "".
        ///
        /// Used as a STABLE fallback key when two bones in the same rig share a
        /// name, so a copied pose maps to the correct bone even across separate
        /// instances of an identical rig (identical relative paths).
        /// </summary>
        public static string GetRelativePath(Transform t, Transform root)
        {
            if (t == null || t == root) return "";
            var segments = new List<string>();
            var cur = t;
            while (cur != null && cur != root)
            {
                segments.Add(cur.name);
                cur = cur.parent;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }
    }
}
