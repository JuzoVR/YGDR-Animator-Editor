#if UNITY_EDITOR
using System;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class PatchGuard
    {
        internal static void Run(string patchName, Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] {patchName}: {e}"); }
        }
    }
}
#endif
