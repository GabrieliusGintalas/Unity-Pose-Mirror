# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-07-07

### Added
- **Copy Pose** — captures every bone's local TRS (position, rotation, scale)
  into a session-persistent buffer, keyed by bone name with a hierarchy-path
  fallback for duplicate names / identical-rig instances.
- **Paste Pose** — reapplies the copied local TRS by bone name, wrapped in a
  single Undo group.
- **Paste Pose Flipped** — applies the copied pose mirrored left↔right using the
  configured naming convention. The reflection is computed in the rig's own
  space (not each bone's parent-local space), so it's correct for any bone
  orientation with no per-component sign tuning — matching Blender's Paste
  X-Flipped. Optionally scoped to just the bones selected in the Hierarchy (a
  center bone flips onto itself).
- **Bone Hierarchy browser** — the full skeleton tree in the window: expand /
  collapse, search-filter, and click to select a bone without using the
  Hierarchy. Selecting a child bone keeps the parent rig as the edit target.
- **Generic skeleton discovery** — uses `SkinnedMeshRenderer.bones` when present,
  otherwise every descendant Transform. No assumptions about names, root, rig
  type, or bone count. Humanoid not required.
- **Configurable suffix system** — preset conventions (`_L/_R`, `.L/.R`,
  `_Left/_Right`, `Left/Right`, `_l/_r`) plus Custom, case-sensitivity toggle,
  and suffix-vs-prefix option (`L_Arm / R_Arm`). Persisted in EditorPrefs per
  project.
- **Validate / Preview Pairs** — lists detected L↔R pairs and flags unmatched
  suffixed bones before pasting.
- **Symmetry-axis selector** — X/Y/Z dropdown (default X) picking the rig's
  left/right axis for the mirror plane.
- **Animation window integration** — requires `AnimationMode` (Record) to be
  active and records keyframes on the current frame via `Undo.RecordObject`; no
  reflection into internal Animation window APIs. Live record-state / target
  readout.
- **Rebindable shortcuts** for Copy / Paste / Paste Flipped
  (`Alt+Shift+C/V/F` by default) via the Shortcut Manager.
- Editor-only assembly definition so nothing leaks into player builds.

### Compatibility
- Unity 2020.3 LTS through Unity 6 (6000.x) and later.

[1.0.0]: https://github.com/gabrieliusgintalas/pose-tools/releases/tag/v1.0.0
