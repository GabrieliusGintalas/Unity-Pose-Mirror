using UnityEngine;
using UnityEditor.ShortcutManagement;

namespace GabeGin.PoseTools
{
    /// <summary>
    /// Global editor shortcuts so Copy / Paste / Paste Flipped can be triggered
    /// while keeping keyboard focus on Unity's Animation window.
    ///
    /// These are GLOBAL (no context type) so they fire regardless of which
    /// window is focused. Every binding is fully rebindable from
    /// Edit ▸ Shortcuts… (search "Pose Tools"). The defaults use
    /// Alt+Shift+C / V / F, which are unbound in a stock Unity install; change
    /// them freely if they clash with your own shortcuts.
    /// </summary>
    static class PoseShortcuts
    {
        [Shortcut("Pose Tools/Copy Pose", KeyCode.C, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        static void CopyPose()
        {
            PoseToolsWindow.CopyPoseCommand();
        }

        [Shortcut("Pose Tools/Paste Pose", KeyCode.V, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        static void PastePose()
        {
            PoseToolsWindow.PastePoseCommand();
        }

        [Shortcut("Pose Tools/Paste Pose Flipped", KeyCode.F, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        static void PasteFlipped()
        {
            PoseToolsWindow.PasteFlippedCommand();
        }
    }
}
