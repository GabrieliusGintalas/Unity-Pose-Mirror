# Pose Tools

Blender-style **Copy Pose**, **Paste Pose**, and **Paste Pose Flipped** for Unity's
Animation window. Pose and animate any skeletal rig — biped, quadruped, or
non-character prop — and mirror poses left↔right with a fully configurable
naming convention and a tunable mirror axis.

- **Generic-first.** Works on raw Transform hierarchies. No Humanoid muscle rig
  required, no hard-coded bone names, root names, or bone counts.
- **Records into your clip.** While the Animation window is in **Record**, every
  paste is written as keyframes on the current frame via the supported
  `AnimationMode` + `Undo.RecordObject` path — no reflection into internal Unity
  APIs, so it stays stable across versions.
- **Configurable mirroring.** Pick the left/right suffix convention and the
  mirror axis; fine-tune the per-component signs when a rig needs it.
- **Rebindable hotkeys** so you can keep focus on the Animation window.

## Compatibility

Unity **2020.3 LTS** through **Unity 6** (6000.x) **and later**. Only long-stable
editor APIs are used (`AnimationMode`, `Undo`, `SkinnedMeshRenderer.bones`,
`ShortcutManager`, IMGUI `EditorWindow`, `SessionState`, `EditorPrefs`), so the
same package drops into any project across that range unchanged.

---

## Install

### A. Package Manager — Add package from Git URL

1. In Unity: **Window ▸ Package Manager**.
2. Click the **+** (top-left) ▸ **Add package from git URL…**
3. Paste the repository URL, e.g.
   `https://github.com/<you>/pose-tools.git`
4. **Add**. Unity downloads it into the immutable package cache.

To pin a version, append a tag: `…/pose-tools.git#v1.0.0`.

### B. Manual — clone into `Packages/`

Clone (or copy) the repo into your project's `Packages/` folder:

```
<YourProject>/Packages/com.gabegin.posetools/
    package.json
    Editor/…
```

Unity picks it up automatically as an embedded package.

### C. As a `.unitypackage` (alternative)

If you'd rather ship a classic asset package instead of a UPM package:

1. Import/clone this repo somewhere under a project's `Assets/` folder.
2. **Assets ▸ Export Package…**, tick the `Editor/` scripts, and export.
3. Import the resulting `.unitypackage` into any project via
   **Assets ▸ Import Package ▸ Custom Package…**

(The scripts are Editor-only via the assembly definition, so they never end up
in a player build regardless of how you import them.)

---

## Quick start

1. **Window ▸ Animation ▸ Pose Tools** to open the window.
2. **Dock it beside the Animation window:** drag the *Pose Tools* tab onto the
   edge of the Animation window so they share a dock area. Now you can see the
   record state and hit the buttons without leaving your animation workflow.
3. Select the rig (or any GameObject) you're animating. The window shows the
   detected bone count and the live record state.
4. In the **Animation window**, select the clip and press **Record** (the red
   dot). Pose Tools shows *"Animation record/preview is ACTIVE"*.
5. Pose your rig, **Copy Pose**, scrub to another frame, **Paste Pose** or
   **Paste Pose Flipped**. Keys land on the current frame of the clip.

> Paste is intentionally disabled until Record is on — pastes must land in a
> clip, so the tool refuses to run without an active recording context.

---

## The window

**Target & Record State** — the GameObject being edited, how many bones were
detected, and whether animation recording is active.

**Naming Convention** — how left/right bones are labelled:

- **Preset**: `_L / _R`, `.L / .R`, `_Left / _Right`, `Left / Right`, `_l / _r`,
  or **Custom**.
- **Left suffix / Right suffix**: auto-filled by the preset; editable when
  **Custom** is selected.
- **Case sensitive**: off means `_l` matches `_L`.
- **Match as prefix**: on supports `L_Arm / R_Arm`; off is the usual
  `Arm_L / Arm_R`.

> Bones ending in the Left suffix are swapped with the matching bone ending in
> the Right suffix when pasting flipped. Center bones (matching neither) mirror
> onto themselves.

**Validate / Preview Pairs** — lists every detected L↔R pair and flags any
suffixed bone with no partner (e.g. an `Arm_L` with no `Arm_R`), so you can
confirm the convention *before* pasting.

**Mirror** — the **Mirror axis** dropdown (X/Y/Z, default **X**) chooses the
plane the pose is reflected across and seeds sensible component signs. The
**Advanced** foldout exposes the exact per-component negations for position and
rotation, for rigs whose bone orientations need different signs.

**Actions** — **Copy Pose**, **Paste Pose**, **Paste Pose Flipped**, plus a
buffer readout and **Clear**.

The pose buffer persists across selection changes and script recompiles /
play-mode toggles; it clears on a full editor restart.

---

## Hotkeys

Copy / Paste / Paste Flipped are exposed as **rebindable** shortcuts so you can
keep focus on the Animation window. Defaults (unbound in a stock Unity install):

| Action             | Default        |
| ------------------ | -------------- |
| Copy Pose          | `Alt+Shift+C`  |
| Paste Pose         | `Alt+Shift+V`  |
| Paste Pose Flipped | `Alt+Shift+F`  |

Rebind them in **Edit ▸ Shortcuts…** — search **"Pose Tools"**.

---

## Testing the flip & adjusting when results look off

Mirroring a *local* rotation across a plane is a reflection, and the correct
per-component signs depend on how each bone's local axes are oriented — which
varies from rig to rig. The defaults (mirror across **X**: negate position X,
negate rotation Y and Z) are correct for a rig whose left/right bones are true
mirror images with matching rest orientations. If your flip looks wrong, walk
this checklist:

1. **Confirm the suffix convention first.** Click **Validate / Preview Pairs**.
   Every limb you expect to swap should appear as a `✔ L ↔ R` pair. If pairs are
   missing or land in **Unmatched**, fix the preset / suffix / case / prefix
   toggle until the pairing is right. A wrong convention looks like "the flip
   did nothing" or "only some limbs flipped".

2. **Check the mirror axis.** Most characters are symmetric across **X** (the
   left/right axis). If yours faces a different way, try **Y** or **Z**. Wrong
   axis usually looks like the pose mirroring *up/down* or *front/back* instead
   of *left/right*.

3. **Tune the rotation signs.** If limbs end up in roughly the right place but
   twisted or bent the wrong way, open **Mirror ▸ Advanced** and flip a
   *different pair* of the rotation **X/Y/Z/W** toggles. Change one pair at a
   time and re-test on an obvious asymmetric pose (e.g. one arm up, one arm
   forward) so the mirror is easy to read. Position signs rarely need changing
   beyond the axis default.

4. **Re-test with Undo.** Every paste is a single Undo step, so you can
   `Ctrl/Cmd+Z`, adjust a sign, and paste again quickly while iterating.

Once a rig's convention + axis + signs look right, they're saved per project, so
you only tune once.

---

## How recording works (and why it's version-stable)

Unity's Animation window is internal and sealed, so this tool does **not** try to
inject UI into it or reflect into its private APIs (which break between
versions). Instead it relies only on public, long-lived APIs:

- It requires `AnimationMode.InAnimationMode()` to be true (Record on).
- It edits bone `localPosition/localRotation/localScale` inside
  `Undo.RecordObject`, wrapped in a single collapsed Undo group.
- Unity's own animation recording observes those modifications and writes
  keyframes on the current frame of the clip being edited.

---

## Repo layout

```
/package.json
/README.md
/LICENSE
/CHANGELOG.md
/Editor/
    PoseTools.asmdef      Editor-only assembly definition
    PoseToolsWindow.cs    the "Pose Tools" EditorWindow + shared commands
    PoseBuffer.cs         copied-pose storage + name/path lookup
    PoseSkeleton.cs       generic bone discovery + relative paths
    PoseMirror.cs         suffix convention + mirror math + settings
    PoseShortcuts.cs      rebindable [Shortcut] hotkeys
```

## License

MIT — see [LICENSE](LICENSE).
