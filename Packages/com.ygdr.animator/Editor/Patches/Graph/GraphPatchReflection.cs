#if UNITY_EDITOR
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class GraphPatchReflection
    {
        static GraphPatchReflection()
        {
            if (GraphGUIType == null)
                Debug.LogWarning("[AnimatorTools] AnimationStateMachine.GraphGUI not found — Unity version mismatch?");
            if (EdgeGUIType == null)
                Debug.LogWarning("[AnimatorTools] AnimationStateMachine.EdgeGUI not found — Unity version mismatch?");
            if (OnGraphGUIMethod == null)
                Debug.LogWarning("[AnimatorTools] GraphGUI.OnGraphGUI not found — Unity version mismatch?");
            if (DrawEdgeMethod == null)
                Debug.LogWarning("[AnimatorTools] EdgeGUI.DrawEdge not found — Unity version mismatch?");
            if (RebuildGraphMethod == null)
                Debug.LogWarning("[AnimatorTools] AnimatorControllerTool.RebuildGraph not found — Unity version mismatch?");
        }

        // ── Graph types ──────────────────────────────────────────────────────
        internal static readonly System.Type GraphGUIType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.GraphGUI");
        internal static readonly System.Type GraphGUIBaseType =
            AccessTools.TypeByName("UnityEditor.Graphs.GraphGUI");
        internal static readonly System.Type EdgeGUIType =
            AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.EdgeGUI");
        internal static readonly System.Type EdgeType =
            AccessTools.TypeByName("UnityEditor.Graphs.Edge");
        internal static readonly System.Type StylesType =
            AccessTools.TypeByName("UnityEditor.Graphs.Styles");

        // ── GraphGUI methods ─────────────────────────────────────────────────
        internal static readonly MethodInfo OnGraphGUIMethod =
            AccessTools.Method(GraphGUIType, "OnGraphGUI");
        internal static readonly MethodInfo HandleContextMenuMethod =
            AccessTools.Method(GraphGUIType, "HandleContextMenu");
        internal static readonly MethodInfo CopySelectionToPasteboardMethod =
            AccessTools.Method(GraphGUIType, "CopySelectionToPasteboard");

        // ── GraphGUI base methods ────────────────────────────────────────────
        internal static readonly MethodInfo DrawGridMethod =
            AccessTools.Method(GraphGUIBaseType, "DrawGrid");

        // ── EdgeGUI methods ──────────────────────────────────────────────────
        internal static readonly MethodInfo DrawEdgeMethod =
            AccessTools.Method(EdgeGUIType, "DrawEdge");
        internal static readonly MethodInfo DrawArrowsMethod =
            AccessTools.Method(EdgeGUIType, "DrawArrows");
        internal static readonly MethodInfo DrawArrowMethod =
            AccessTools.Method(EdgeGUIType, "DrawArrow");
        internal static readonly MethodInfo DoEdgesMethod =
            AccessTools.Method(EdgeGUIType, "DoEdges");
        internal static readonly MethodInfo GetEdgePointsMethod =
            AccessTools.Method(EdgeGUIType, "GetEdgePoints",
                new[] { EdgeType, typeof(Vector3).MakeByRefType() });
        internal static readonly MethodInfo EdgeSizeMultiplierGetter =
            AccessTools.PropertyGetter(EdgeGUIType, "edgeSizeMultiplier");

        // ── AnimatorControllerTool methods ───────────────────────────────────
        internal static readonly MethodInfo RebuildGraphMethod =
            AccessTools.Method(AnimatorEditorInit.AnimatorControllerToolType, "RebuildGraph",
                new[] { typeof(bool) });

        // ── Node fields ──────────────────────────────────────────────────────
        internal static readonly FieldInfo StateNodeStateField =
            AccessTools.Field(AnimatorEditorInit.StateNodeType, "state");
        internal static readonly FieldInfo StateMachineNodeStateMachineField =
            AccessTools.Field(AnimatorEditorInit.StateMachineNodeType, "stateMachine");
    }
}
#endif
