#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class AnimatorStyles
    {
        // ── Graph node overlay indicators ─────────────────────────────────────

        static GUIStyle _indicatorStyle;
        internal static GUIStyle IndicatorStyle => _indicatorStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        static GUIStyle _loopStyle;
        internal static GUIStyle LoopStyle => _loopStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        static GUIStyle _motionNameStyle;
        internal static GUIStyle MotionNameStyle => _motionNameStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        static GUIStyle _coordsStyle;
        internal static GUIStyle CoordsStyle => _coordsStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 9,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleLeft,
            padding   = new RectOffset(0, 0, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
        };

        static GUIStyle _nodeNameStyle;
        internal static GUIStyle NodeNameStyle => _nodeNameStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(2, 2, 0, 0),
            margin    = new RectOffset(0, 0, 0, 0),
            clipping  = TextClipping.Clip,
            normal    = { textColor = Color.white },
        };

        // ── Inline rename field (state + sub-SM) ──────────────────────────────

        static GUIStyle _renameFieldStyle;
        internal static GUIStyle RenameFieldStyle => _renameFieldStyle ??= new GUIStyle(EditorStyles.textField)
        {
            alignment = TextAnchor.MiddleCenter,
            normal    = { background = null },
            focused   = { background = null },
            hover     = { background = null },
            active    = { background = null },
        };

        // ── Transition edge label ─────────────────────────────────────────────

        static GUIStyle _transitionEdgeLabelStyle;
        internal static GUIStyle TransitionEdgeLabelStyle => _transitionEdgeLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleCenter };

        // ── Bottom bar ────────────────────────────────────────────────────────

        static GUIStyle _bottomBarLabelStyle;
        internal static GUIStyle BottomBarLabelStyle => _bottomBarLabelStyle ??= new GUIStyle(EditorStyles.miniLabel);
    }
}
#endif
