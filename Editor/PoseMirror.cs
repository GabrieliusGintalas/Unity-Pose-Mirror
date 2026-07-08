using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace GabeGin.PoseTools
{
    public enum MirrorAxis { X = 0, Y = 1, Z = 2 }

    // =====================================================================
    //  Suffix / naming convention
    // =====================================================================

    /// <summary>
    /// Describes how left/right bones are labelled and knows how to turn a bone
    /// name into its mirror-partner name.
    ///
    /// Supports suffix conventions (Arm_L / Arm_R) and prefix conventions
    /// (L_Arm / R_Arm), with optional case sensitivity.
    /// </summary>
    [Serializable]
    public class SuffixConvention
    {
        public string left = "_L";
        public string right = "_R";
        public bool caseSensitive = true;
        public bool isPrefix = false;   // false => suffix (Arm_L); true => prefix (L_Arm)

        public enum Side { Center, Left, Right }

        StringComparison Cmp
        {
            get { return caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase; }
        }

        /// <summary>Which side a bone belongs to based on its affix.</summary>
        public Side GetSide(string boneName)
        {
            // Left is tested first; if a rig somehow uses the same string for
            // both, left wins deterministically.
            if (HasAffix(boneName, left)) return Side.Left;
            if (HasAffix(boneName, right)) return Side.Right;
            return Side.Center;
        }

        bool HasAffix(string boneName, string affix)
        {
            if (string.IsNullOrEmpty(affix) || string.IsNullOrEmpty(boneName)) return false;
            if (boneName.Length < affix.Length) return false;
            return isPrefix ? boneName.StartsWith(affix, Cmp)
                            : boneName.EndsWith(affix, Cmp);
        }

        /// <summary>
        /// Build the mirror-partner name for a bone:
        ///   * A LEFT bone maps to the same name with the left affix replaced by
        ///     the right affix.
        ///   * A RIGHT bone maps the other way.
        ///   * A CENTER bone (matches neither affix — spine, root, tail, ...)
        ///     returns its own name, i.e. it mirrors onto itself.
        /// </summary>
        public string GetPartnerName(string boneName)
        {
            switch (GetSide(boneName))
            {
                case Side.Left:  return ReplaceAffix(boneName, left, right);
                case Side.Right: return ReplaceAffix(boneName, right, left);
                default:         return boneName;
            }
        }

        string ReplaceAffix(string boneName, string from, string to)
        {
            // `from` is guaranteed present (length checked in HasAffix). Because
            // we matched by length, this stays correct even when case-insensitive.
            if (isPrefix)
                return to + boneName.Substring(from.Length);
            return boneName.Substring(0, boneName.Length - from.Length) + to;
        }
    }

    // =====================================================================
    //  Mirror transform math
    // =====================================================================

    /// <summary>
    /// Reflects a local position + rotation across a chosen axis plane.
    ///
    /// WHY THE SIGNS ARE EXPOSED
    /// -------------------------
    /// Mirroring a pose across a plane is a reflection (an improper transform).
    /// For a LOCAL rotation the correct component signs depend on how each
    /// bone's local axes are oriented relative to that plane, which varies from
    /// rig to rig. Rather than hard-code one guess, both the position sign
    /// (Vector3) and the quaternion sign (Vector4 over x,y,z,w) are data you can
    /// tune. The axis dropdown just seeds sensible defaults.
    ///
    /// DEFAULTS (derived)
    /// ------------------
    /// Reflecting an ORIENTATION across the plane whose normal is the mirror
    /// axis is the "pseudovector" mirror: keep w and the component on the mirror
    /// axis, negate the other two vector components. Equivalently for X:
    ///
    ///     q  = (x,  y,  z,  w)   [Unity's component order]
    ///     q' = (x, -y, -z,  w)
    ///
    /// Derivation: a rotation of angle t about axis a mirrors to a rotation of
    /// angle -t about the plane-reflected axis. Reflecting the axis across the
    /// YZ-plane negates a.x; reversing the angle negates the whole vector part;
    /// the two combine to "negate y and z, keep x", with w = cos(t/2) unchanged.
    /// Position simply negates the component on the mirror axis.
    ///
    /// Per axis the defaults are therefore:
    ///     X : pos(-1, 1, 1)   quat( 1,-1,-1, 1)
    ///     Y : pos( 1,-1, 1)   quat(-1, 1,-1, 1)
    ///     Z : pos( 1, 1,-1)   quat(-1,-1, 1, 1)
    ///
    /// If a flip looks wrong for your rig (e.g. a limb twists the wrong way),
    /// flip a different pair of quaternion signs in the Advanced section — see
    /// the README's "adjusting the mirror" note.
    /// </summary>
    [Serializable]
    public class MirrorSettings
    {
        // Which of the rig's OWN local axes is its left/right (symmetry) axis.
        // The pose is reflected across the plane perpendicular to it. X is the
        // Blender convention and correct for virtually all character rigs.
        public MirrorAxis axis = MirrorAxis.X;

        /// <summary>
        /// Reflect a ROOT-SPACE position across the symmetry plane: negate the
        /// component on the symmetry axis, keep the others.
        /// </summary>
        public Vector3 MirrorRootPosition(Vector3 p)
        {
            switch (axis)
            {
                case MirrorAxis.X: return new Vector3(-p.x, p.y, p.z);
                case MirrorAxis.Y: return new Vector3(p.x, -p.y, p.z);
                default:           return new Vector3(p.x, p.y, -p.z);
            }
        }

        /// <summary>
        /// Reflect a ROOT-SPACE rotation across the symmetry plane.
        ///
        /// Reflecting an orientation is the reflection matrix M conjugating the
        /// rotation (M·R·M), which is a *proper* rotation (det +1) even though M
        /// itself is improper. In quaternion terms that keeps w and the component
        /// on the symmetry axis and negates the other two vector components — the
        /// same reflection Blender applies to a bone's pose. Doing this in root
        /// space (rather than each bone's parent-local space) is what makes the
        /// flip correct regardless of how the bone's local axes are oriented.
        /// </summary>
        public Quaternion MirrorRootRotation(Quaternion q)
        {
            Quaternion r;
            switch (axis)
            {
                case MirrorAxis.X: r = new Quaternion( q.x, -q.y, -q.z, q.w); break;
                case MirrorAxis.Y: r = new Quaternion(-q.x,  q.y, -q.z, q.w); break;
                default:           r = new Quaternion(-q.x, -q.y,  q.z, q.w); break;
            }
            // Component flips preserve unit length; normalize defensively against
            // accumulated float error before handing back a rotation.
            return Normalize(r);
        }

        static Quaternion Normalize(Quaternion q)
        {
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag < 1e-8f) return Quaternion.identity;
            float inv = 1f / mag;
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }
    }

    // =====================================================================
    //  Pair validation
    // =====================================================================

    public struct BonePair
    {
        public Transform left;
        public Transform right;
    }

    public class PairValidation
    {
        public List<BonePair> pairs = new List<BonePair>();
        public List<Transform> unmatched = new List<Transform>(); // suffixed, but partner absent
        public int centerCount;
    }

    /// <summary>
    /// Cross-checks a convention against an actual bone list: which L↔R pairs
    /// exist, which suffixed bones have no partner, and how many center bones
    /// there are. Lets the user confirm the convention before pasting flipped.
    /// </summary>
    public static class PosePairs
    {
        public static PairValidation Validate(IList<Transform> bones, SuffixConvention c)
        {
            var result = new PairValidation();
            if (bones == null) return result;

            var comparer = c.caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            var byName = new Dictionary<string, Transform>(comparer);
            foreach (var b in bones)
                if (b != null && !byName.ContainsKey(b.name))
                    byName[b.name] = b;

            var consumed = new HashSet<Transform>();
            foreach (var b in bones)
            {
                if (b == null || consumed.Contains(b)) continue;

                var side = c.GetSide(b.name);
                if (side == SuffixConvention.Side.Center)
                {
                    result.centerCount++;
                    continue;
                }

                string partnerName = c.GetPartnerName(b.name);
                Transform partner;
                if (byName.TryGetValue(partnerName, out partner) && partner != b)
                {
                    var pair = new BonePair();
                    if (side == SuffixConvention.Side.Left) { pair.left = b; pair.right = partner; }
                    else { pair.left = partner; pair.right = b; }
                    result.pairs.Add(pair);
                    consumed.Add(b);
                    consumed.Add(partner);
                }
                else
                {
                    result.unmatched.Add(b);
                    consumed.Add(b);
                }
            }
            return result;
        }
    }

    // =====================================================================
    //  Persistent settings (suffix + mirror), stored per project
    // =====================================================================

    [Serializable]
    public class PoseToolsSettings
    {
        public SuffixConvention suffix = new SuffixConvention();
        public MirrorSettings mirror = new MirrorSettings();
        public int presetIndex = 0;          // index into PoseToolsWindow presets

        // When true, Paste Flipped acts only on the bones currently selected in
        // the Hierarchy plus each one's mirror partner. Selecting just the rig
        // (or nothing) falls back to flipping the whole rig.
        public bool flipSelectedOnly = true;

        // Draw the rig's skeleton as a clickable overlay in the Scene view.
        public bool showSkeletonOverlay = true;

        // EditorPrefs is per-user/per-machine; scope the key to this project so
        // conventions don't bleed between projects on the same machine.
        static string Key { get { return "GabeGin.PoseTools.Settings." + ProjectId(); } }

        static string ProjectId()
        {
            // Stable (non-runtime-randomized) hash of the project location.
            return StableHash(Application.dataPath).ToString("X8");
        }

        static uint StableHash(string s)
        {
            // FNV-1a — deterministic across runs/platforms, unlike string.GetHashCode.
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < s.Length; i++)
                {
                    hash ^= s[i];
                    hash *= 16777619u;
                }
                return hash;
            }
        }

        public static PoseToolsSettings Load()
        {
            string json = EditorPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(json)) return new PoseToolsSettings();
            try
            {
                var s = JsonUtility.FromJson<PoseToolsSettings>(json);
                if (s == null) return new PoseToolsSettings();
                if (s.suffix == null) s.suffix = new SuffixConvention();
                if (s.mirror == null) s.mirror = new MirrorSettings();
                return s;
            }
            catch
            {
                return new PoseToolsSettings();
            }
        }

        public void Save()
        {
            EditorPrefs.SetString(Key, JsonUtility.ToJson(this));
        }
    }
}
