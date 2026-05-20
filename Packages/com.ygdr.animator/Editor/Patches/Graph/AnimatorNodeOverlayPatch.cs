#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;


namespace YGDR.Editor.Animation
{
    // ──── State nodes ────────────────────────────────────────────────────────────────────

    [HarmonyPatch]
    internal static class AnimatorStateNodeOverlayPatch
    {
        internal static readonly Dictionary<AnimatorState, Rect> NodeRects = new();
        internal static readonly Dictionary<AnimatorState, Vector2> NodeScreenCenters = new();

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "NodeUI");

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            try
            {
                var state = GetState(__instance);
                if (state == null) return;
                var stateRect    = GUILayoutUtility.GetLastRect();
                var currentEvent = Event.current;
                bool isRenaming = StateRenameState.RenameTarget == state;
                bool isRenamingMotion = MotionRenameState.RenameTargetState == state;

                if (currentEvent.type != EventType.Repaint)
                {
                    if (isRenaming) DrawRenameField(state, stateRect);
                    if (isRenamingMotion) DrawMotionRenameField(state, stateRect);
                    return;
                }

                NodeRects[state] = stateRect;
                NodeScreenCenters[state] = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
                if (AnimatorGraphAnalyzer.HighlightedStates.Contains(state))
                {
                    var highlightColor = AnimatorGraphAnalyzer.HighlightColor;
                    highlightColor.a = 0.45f;
                    EditorGUI.DrawRect(stateRect, highlightColor);
                }
                var settings = AnimatorDefaultSettings.Load();
                if (!isRenaming)
                    DrawNodeNameLabel(state, stateRect, settings);
                var graphPosition = Vector2.zero;
                if (settings.overlayEnabled && settings.overlayShowCoords)
                {
                    if (_nodeGraphInvoker == null)
                        _nodeGraphInvoker = MethodInvoker.GetHandler(AccessTools.Method(__instance.GetType(), "get_graph"));
                    var graph = _nodeGraphInvoker?.Invoke(__instance);
                    if (graph != null)
                    {
                        if (_activeStateMachineInvoker == null)
                            _activeStateMachineInvoker = MethodInvoker.GetHandler(AccessTools.Method(graph.GetType(), "get_activeStateMachine"));
                        var activeSM = _activeStateMachineInvoker?.Invoke(graph) as AnimatorStateMachine;
                        if (activeSM != null)
                        {
                            bool stale = activeSM != _positionCacheSM
                                      || EditorApplication.timeSinceStartup - _positionCacheTime > 0.02;
                            if (stale)
                            {
                                _positionCacheSM   = activeSM;
                                _positionCacheTime = EditorApplication.timeSinceStartup;
                                _positionCache.Clear();
                                foreach (var childState in activeSM.states)
                                    _positionCache[childState.state] = new Vector2(childState.position.x, childState.position.y);
                            }
                            _positionCache.TryGetValue(state, out graphPosition);
                        }
                    }
                }
                if (settings.overlayEnabled)
                    DrawIndicators(state, stateRect, settings, graphPosition);
                if (isRenaming)
                    DrawRenameField(state, stateRect);
                if (isRenamingMotion)
                    DrawMotionRenameField(state, stateRect);
            }
            catch (Exception e) { Debug.LogError($"[YGDR] State node overlay error: {e}"); }
        }

        static void DrawIndicators(AnimatorState state, Rect nodeRect, AnimatorDefaultSettings settings, Vector2 graphPosition)
        {
            var previousContentColor = GUI.contentColor;

            // Left-anchored  Rect(nodeRect.x + offsetX, nodeRect.y + offsetY, width, height)
            bool hasMotion = state.motion != null;

            if (settings.overlayShowLoop && hasMotion)
            {
                var loopRect = new Rect(nodeRect.x + 2f, nodeRect.y + -26f, 16f, 15f);
                if (state.motion is BlendTree)
                {
                    GUI.contentColor = settings.overlayActiveColor;
                    GUI.Label(loopRect, BlendTreeIcon, AnimatorStyles.LoopStyle);
                }
                else
                {
                    GUI.contentColor = IsLooping(state.motion) ? settings.overlayActiveColor : settings.overlayInactiveColor;
                    GUI.Label(loopRect, LoopIcon, AnimatorStyles.LoopStyle);
                }
            }

            if (settings.overlayShowEmpty && !hasMotion)
            {
                GUI.contentColor = settings.overlayActiveColor;
                GUI.Label(new Rect(nodeRect.x + 2f, nodeRect.y + -28f, 14f, 15f), "!", AnimatorStyles.IndicatorStyle);
            }

            // Right-anchored  Rect(nodeRect.x + nodeRect.width + offsetX, nodeRect.y + offsetY, width, height)  (offsetX is negative)
            if (settings.overlayShowB)
            {
                GUI.contentColor = state.behaviours.Length > 0 ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + nodeRect.width + -14f, nodeRect.y + -28f, 13f, 15f), "B",  AnimatorStyles.IndicatorStyle);
            }

            if (settings.overlayShowWD)
            {
                GUI.contentColor = state.writeDefaultValues ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + nodeRect.width + -36f, nodeRect.y + -28f, 22f, 15f), "WD", AnimatorStyles.IndicatorStyle);
            }

            if (settings.overlayShowSpeed)
            {
                GUI.contentColor = state.speedParameterActive ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + nodeRect.width + -14f, nodeRect.y + -5f, 13f, 15f), "S",  AnimatorStyles.IndicatorStyle);
            }

            if (settings.overlayShowMotion)
            {
                GUI.contentColor = state.timeParameterActive ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + nodeRect.width + -36f, nodeRect.y + -5f, 22f, 15f), "M",  AnimatorStyles.IndicatorStyle);
            }

            if (settings.overlayShowMotionName && MotionRenameState.RenameTargetState != state)
            {
                string label = state.motion != null ? $"[{state.motion.name}]" : "[none]";
                GUI.contentColor = state.motion != null ? settings.overlayActiveColor : settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x, nodeRect.y + -6f, nodeRect.width, 13f), label, AnimatorStyles.MotionNameStyle);
            }

            if (settings.overlayShowCoords)
            {
                GUI.contentColor = settings.overlayInactiveColor;
                GUI.Label(new Rect(nodeRect.x + 2f, nodeRect.yMax - 13f, nodeRect.width - 4f, 13f),
                    $"({(int)graphPosition.x},{(int)graphPosition.y})", AnimatorStyles.CoordsStyle);
            }

            GUI.contentColor = previousContentColor;
        }

        static bool IsLooping(Motion motion)
        {
            if (motion is AnimationClip clip) return clip.isLooping;
            if (motion is BlendTree blendTree)
            {
                var children = blendTree.children;
                return children.Length > 0 && children.All(x => x.motion != null && IsLooping(x.motion));
            }
            return false;
        }

        static FastInvokeHandler   _nodeGraphInvoker;
        static FastInvokeHandler   _activeStateMachineInvoker;
        static GUIContent          _blendTreeIcon;
        static GUIContent          _loopIcon;
        static GUIContent BlendTreeIcon => _blendTreeIcon ??= EditorGUIUtility.IconContent("d_BlendTree Icon");
        static GUIContent LoopIcon      => _loopIcon      ??= EditorGUIUtility.IconContent("d_preaudioloopoff@2x");

        static AnimatorStateMachine                        _positionCacheSM;
        static double                                      _positionCacheTime;
        static readonly Dictionary<AnimatorState, Vector2> _positionCache = new();

        static AnimatorState GetState(object node) =>
            GraphPatchReflection.StateNodeStateField?.GetValue(node) as AnimatorState;

        static void DrawNodeNameLabel(AnimatorState state, Rect nodeRect, AnimatorDefaultSettings settings)
        {
            var previousContentColor = GUI.contentColor;
            GUI.contentColor = settings.overlayActiveColor;
            GUI.Label(new Rect(nodeRect.x, nodeRect.y - 25f, nodeRect.width, 20f), state.name, AnimatorStyles.NodeNameStyle);
            GUI.contentColor = previousContentColor;
        }

        static bool _renameFieldHadFocus;

        static void DrawRenameField(AnimatorState state, Rect nodeRect)
        {
            const string controlName = "StateRenameField";
            var fieldRect    = new Rect(nodeRect.x + 2f, nodeRect.y - 24f, nodeRect.width - 4f, 17f);
            var currentEvent = Event.current;

            // Check Enter/Escape before TextField so Unity's internal handling can't consume them
            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    StateRenameState.Apply();
                    currentEvent.Use();
                    return;
                }
                if (currentEvent.keyCode == KeyCode.Escape)
                {
                    StateRenameState.Cancel();
                    currentEvent.Use();
                    return;
                }
            }

            GUI.SetNextControlName(controlName);
            StateRenameState.RenameText = EditorGUI.TextField(fieldRect, StateRenameState.RenameText, AnimatorStyles.RenameFieldStyle);

            if (StateRenameState.JustStarted)
            {
                EditorGUI.FocusTextInControl(controlName);
                StateRenameState.JustStarted = false;
                _renameFieldHadFocus = false;
                return;
            }

            bool hasFocus = GUI.GetNameOfFocusedControl() == controlName;
            if (!_renameFieldHadFocus && hasFocus)
            {
                var textEditor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
                textEditor?.SelectAll();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
            if (_renameFieldHadFocus && !hasFocus)
                StateRenameState.Apply();
            _renameFieldHadFocus = hasFocus;
        }

        static bool _motionRenameFieldHadFocus;

        static void DrawMotionRenameField(AnimatorState state, Rect nodeRect)
        {
            const string controlName = "MotionRenameField";
            var fieldRect    = new Rect(nodeRect.x + 2f, nodeRect.y - 6f, nodeRect.width - 4f, 17f);
            var currentEvent = Event.current;

            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    MotionRenameState.Apply();
                    currentEvent.Use();
                    return;
                }
                if (currentEvent.keyCode == KeyCode.Escape)
                {
                    MotionRenameState.Cancel();
                    currentEvent.Use();
                    return;
                }
            }

            GUI.SetNextControlName(controlName);
            MotionRenameState.RenameText = EditorGUI.TextField(fieldRect, MotionRenameState.RenameText, AnimatorStyles.RenameFieldStyle);

            if (MotionRenameState.JustStarted)
            {
                EditorGUI.FocusTextInControl(controlName);
                MotionRenameState.JustStarted = false;
                _motionRenameFieldHadFocus = false;
                return;
            }

            bool hasFocusMotion = GUI.GetNameOfFocusedControl() == controlName;
            if (_motionRenameFieldHadFocus && !hasFocusMotion)
                MotionRenameState.Apply();
            _motionRenameFieldHadFocus = hasFocusMotion;
        }

        // Layer 2: swallow exceptions from conflicting transpilers on this hot path to prevent GUI lockup
        [HarmonyFinalizer]
        static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
                Debug.LogError($"[AnimatorTools] Exception in NodeUI — disable conflicting feature in Compatibility settings: {__exception.Message}");
            return null;
        }
    }

    // ─── Special node rect storage (for transition overlay) ─────────────────────────────────
    internal static class SpecialNodeRects
    {
        internal static Rect AnyState;
        internal static Rect Entry;
        internal static Rect Exit;
        internal static readonly Dictionary<AnimatorStateMachine, Rect> SubSMs = new();

        internal static Vector2 AnyStateScreen;
        internal static Vector2 EntryScreen;
        internal static Vector2 ExitScreen;
        internal static readonly Dictionary<AnimatorStateMachine, Vector2> SubSMScreens = new();
    }

    // ─── Entry / Exit / Any State nodes ────────────────────────────────────────────────────────────────────

    [HarmonyPatch]
    internal static class AnimatorEntryNodeOverlayPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.EntryNodeType, "NodeUI");

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => NodeOverlayUtils.InjectColorDraw(instructions,
                AccessTools.Method(typeof(AnimatorEntryNodeOverlayPatch), nameof(Draw)));

        [HarmonyPostfix]
        static void Postfix()
        {
            SpecialNodeRects.Entry = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                SpecialNodeRects.EntryScreen = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
        }

        internal static void Draw(object node) { }
    }

    [HarmonyPatch]
    internal static class AnimatorExitNodeOverlayPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.ExitNodeType, "NodeUI");

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => NodeOverlayUtils.InjectColorDraw(instructions,
                AccessTools.Method(typeof(AnimatorExitNodeOverlayPatch), nameof(Draw)));

        [HarmonyPostfix]
        static void Postfix()
        {
            SpecialNodeRects.Exit = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                SpecialNodeRects.ExitScreen = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
        }

        internal static void Draw(object node) { }
    }

    [HarmonyPatch]
    internal static class AnimatorAnyStateNodeOverlayPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.AnyStateNodeType, "NodeUI");

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => NodeOverlayUtils.InjectColorDraw(instructions,
                AccessTools.Method(typeof(AnimatorAnyStateNodeOverlayPatch), nameof(Draw)));

        [HarmonyPostfix]
        static void Postfix()
        {
            SpecialNodeRects.AnyState = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
                SpecialNodeRects.AnyStateScreen = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
        }

        internal static void Draw(object node) { }
    }

    // ─── Sub state machine nodes ────────────────────────────────────────────────────────────────────

    [HarmonyPatch]
    internal static class AnimatorSubSMNodeOverlayPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateMachineNodeType, "NodeUI");

        static bool _renameFieldHadFocus;

        // Tune these to match the sub-SM node's visual content area
        static float _highlightWidth   = 170f;  // narrower than 200 to fit within pointed sides
        static float _highlightHeight  = 10f;   // content band height
        static float _highlightOffsetX = 15f;   // x inset from node left edge
        static float _highlightOffsetY = 30f;   // y offset from node top (pushes into lower content area)

        static AnimatorStateMachine GetStateMachine(object node) =>
            GraphPatchReflection.StateMachineNodeStateMachineField?.GetValue(node) as AnimatorStateMachine;

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            try
            {
                var sm = GetStateMachine(__instance);
                if (sm == null) return;

                var nodeLocalRect = new Rect(_highlightOffsetX, _highlightOffsetY, _highlightWidth, _highlightHeight);
                SpecialNodeRects.SubSMs[sm] = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.Repaint)
                {
                    SpecialNodeRects.SubSMScreens[sm] = GUIUtility.GUIToScreenPoint(new Vector2(100f, 20f));
                    if (AnimatorGraphAnalyzer.HighlightedSubStateMachines.Contains(sm))
                    {
                        var highlightColor = AnimatorGraphAnalyzer.HighlightColor;
                        highlightColor.a = 0.45f;
                        EditorGUI.DrawRect(nodeLocalRect, highlightColor);
                    }
                }

                if (SubSMRenameState.RenameTarget != sm) return;
                DrawRenameField();
            }
            catch (ExitGUIException) { throw; }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] AnimatorSubSMNodeOverlayPatch.Postfix: {e}"); }
        }

        static void DrawRenameField()
        {
            const string controlName = "SubSMRenameField";
            // NodeUI has no GUILayout content, draw in local window coords, title bar area at y < 0, content at y >= 0
            var fieldRect = new Rect(2f, 10f, 196f, 17f);
            var currentEvent = Event.current;

            if (currentEvent.type == EventType.KeyDown)
            {
                if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    SubSMRenameState.Apply();
                    currentEvent.Use();
                    return;
                }
                if (currentEvent.keyCode == KeyCode.Escape)
                {
                    SubSMRenameState.Cancel();
                    currentEvent.Use();
                    return;
                }
            }

            GUI.SetNextControlName(controlName);
            SubSMRenameState.RenameText = EditorGUI.TextField(fieldRect, SubSMRenameState.RenameText, AnimatorStyles.RenameFieldStyle);

            if (SubSMRenameState.JustStarted)
            {
                EditorGUI.FocusTextInControl(controlName);
                SubSMRenameState.JustStarted = false;
                _renameFieldHadFocus = false;
                return;
            }

            bool hasFocus = GUI.GetNameOfFocusedControl() == controlName;
            if (_renameFieldHadFocus && !hasFocus)
                SubSMRenameState.Apply();
            _renameFieldHadFocus = hasFocus;
        }
    }


    // ──── State rename state ────────────────────────────────────────────────────────────────────

    internal static class StateRenameState
    {
        internal static AnimatorState RenameTarget;
        internal static string RenameText;
        internal static bool JustStarted;

        /* Starts an inline rename session for state, seeding the text field with the current name. */
        internal static void Begin(AnimatorState state)
        {
            RenameTarget = state;
            RenameText   = state.name;
            JustStarted  = true;
        }

        internal static void Apply()
        {
            if (RenameTarget == null) return;
            AnimatorStateOps.RenameState(RenameTarget, RenameText);
            RenameTarget = null;
            RenameText   = null;
        }

        internal static void Cancel()
        {
            GUIUtility.keyboardControl = 0;
            RenameTarget = null;
            RenameText   = null;
        }
    }

    internal static class SubSMRenameState
    {
        internal static AnimatorStateMachine RenameTarget;
        internal static string RenameText;
        internal static bool JustStarted;

        /* Starts an inline rename session for stateMachine, seeding the text field with the current name. */
        internal static void Begin(AnimatorStateMachine stateMachine)
        {
            RenameTarget = stateMachine;
            RenameText   = stateMachine.name;
            JustStarted  = true;
        }

        internal static void Apply()
        {
            if (RenameTarget == null) return;
            AnimatorStateOps.RenameStateMachine(RenameTarget, RenameText);
            RenameTarget = null;
            RenameText   = null;
        }

        internal static void Cancel()
        {
            GUIUtility.keyboardControl = 0;
            RenameTarget = null;
            RenameText   = null;
        }
    }

    internal static class MotionRenameState
    {
        internal static Motion RenameTarget;
        internal static AnimatorState RenameTargetState;
        internal static string RenameText;
        internal static bool JustStarted;

        /* Starts an inline rename session for motion associated with state, seeding the text field with the current motion name. */
        internal static void Begin(Motion motion, AnimatorState state)
        {
            RenameTarget      = motion;
            RenameTargetState = state;
            RenameText        = motion.name;
            JustStarted       = true;
        }

        internal static void Apply()
        {
            if (RenameTarget == null) return;
            AnimatorStateOps.RenameMotion(RenameTarget, RenameText);
            RenameTarget      = null;
            RenameTargetState = null;
            RenameText        = null;
        }

        internal static void Cancel()
        {
            GUIUtility.keyboardControl = 0;
            RenameTarget      = null;
            RenameTargetState = null;
            RenameText        = null;
        }
    }

    // ─── Suppress built-in title label ────────────────────────────────────────────────────────────────────

    [HarmonyPatch]
    internal static class PatchStateNodeTitle
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "get_title");

        [HarmonyPostfix]
        static void Postfix(ref string __result) => __result = "";
    }

    // ─── Shared utilities ────────────────────────────────────────────────────────────────────

    internal static class NodeOverlayUtils
    {
        static readonly Dictionary<Type, MethodInfo> _positionGetters = new();

        /* Returns the width and height of node's position Rect via reflection, falling back to (160, 40) if unavailable. */
        internal static Vector2 GetNodeSize(object node)
        {
            var type = node.GetType();
            if (!_positionGetters.TryGetValue(type, out var getter))
                _positionGetters[type] = getter = AccessTools.Method(type, "get_position");
            if (getter?.Invoke(node, null) is Rect nodeRect) return new Vector2(nodeRect.width, nodeRect.height);
            return new Vector2(160f, 40f);
        }

        /* Inserts Ldarg_0 + Call method before every Ret instruction in the IL stream, so method receives the node instance on each exit path. */
        internal static IEnumerable<CodeInstruction> InjectColorDraw(
            IEnumerable<CodeInstruction> instructions, MethodInfo method)
        {
            var list = instructions.ToList();
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].opcode != OpCodes.Ret) continue;
                list.Insert(i, new CodeInstruction(OpCodes.Call, method));
                list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
            }
            return list;
        }
    }
}
#endif
