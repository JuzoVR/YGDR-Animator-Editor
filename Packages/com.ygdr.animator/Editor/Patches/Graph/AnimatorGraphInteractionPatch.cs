#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    /* Double-click empty graph space → create state at cursor; assigns _buffer.anim as motion if found in package.
       Also tracks hovered node for chain-mode snap. */
    [HarmonyPatch]
    internal static class PatchGraphDoubleClickCreate
    {
        static FieldInfo _mGraphField;
        static EditorWindow _animWindow;

        static Vector2 _lastMousePosition;
        static HashSet<AnimatorState> _prepasteStateSet;
        static HashSet<AnimatorStateMachine> _prepasteSubSMSet;
        static AnimatorStateMachine _pasteSM;
        static AnimationClip _bufferClip;

        /* Lazily resolves and caches the m_Graph FieldInfo from the GraphGUI instance type. */
        static FieldInfo MGraphField(object instance) =>
            _mGraphField ??= AccessTools.Field(instance.GetType(), "m_Graph");

        internal static EditorWindow AnimWindow
        {
            get
            {
                if (_animWindow == null)
                {
                    var arr = Resources.FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType);
                    _animWindow = arr.Length > 0 ? arr[0] as EditorWindow : null;
                }
                return _animWindow;
            }
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.OnGraphGUIMethod;

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
                var currentEvent = Event.current;

                if (currentEvent.isMouse || currentEvent.type == EventType.MouseMove)
                {
                    _lastMousePosition = currentEvent.mousePosition;
                    if (currentEvent.type == EventType.MouseDown)
                    {
                        PatchLayerF2Rename._panelClicked = false;
                        PatchParameterF2Rename._panelClicked = false;
                    }
                }

                if (currentEvent.type == EventType.ExecuteCommand && currentEvent.commandName == "Paste")
                {
                    var getActiveSM = AccessTools.Method(__instance.GetType(), "get_activeStateMachine");
                    var activeSM = getActiveSM?.Invoke(__instance, null) as AnimatorStateMachine;
                    if (activeSM != null)
                    {
                        _pasteSM = activeSM;
                        _prepasteStateSet = new HashSet<AnimatorState>(activeSM.states.Select(childState => childState.state));
                        _prepasteSubSMSet = new HashSet<AnimatorStateMachine>(activeSM.stateMachines.Select(childStateMachine => childStateMachine.stateMachine));
                    }
                }

                if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.F2)
                {
                    var selectedState = Selection.activeObject as AnimatorState;
                    if (selectedState != null)
                    {
                        MotionRenameState.Cancel();
                        SubSMRenameState.Cancel();
                        StateRenameState.Begin(selectedState);
                        currentEvent.Use();
                        return;
                    }
                    var selectedSubSM = Selection.activeObject as AnimatorStateMachine;
                    if (selectedSubSM != null)
                    {
                        MotionRenameState.Cancel();
                        StateRenameState.Cancel();
                        SubSMRenameState.Begin(selectedSubSM);
                        currentEvent.Use();
                        return;
                    }
                }

                if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.F3)
                {
                    var selectedState = Selection.activeObject as AnimatorState;
                    if (selectedState != null && selectedState.motion != null)
                    {
                        StateRenameState.Cancel();
                        SubSMRenameState.Cancel();
                        MotionRenameState.Begin(selectedState.motion, selectedState);
                        currentEvent.Use();
                        return;
                    }
                }

                if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
                {
                    if (PatchStateChainTransition.ChainActive) { PatchStateChainTransition.Clear(); currentEvent.Use(); return; }
                    if (PatchTransitionCopyPaste.PasteActive) { PatchTransitionCopyPaste.ClearPaste(); currentEvent.Use(); return; }
                    if (PatchStateNodeMenu._multiTransitionSources != null || PatchStateNodeMenu._redirectTransitions != null || PatchStateNodeMenu._replicateTransitions != null)
                    {
                        PatchStateNodeMenu.CancelPending();
                        currentEvent.Use();
                        return;
                    }
                }

                if (currentEvent.type == EventType.KeyDown && (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter))
                {
                    if (PatchStateNodeMenu._multiTransitionSources != null)
                    {
                        var destinationStates = Selection.objects.OfType<AnimatorState>().ToArray();
                        var multiSources = PatchStateNodeMenu._multiTransitionSources;
                        var multiSM = PatchStateNodeMenu._multiTransitionSM;
                        PatchStateNodeMenu._multiTransitionSources = null;
                        PatchStateNodeMenu._multiTransitionSM = null;
                        if (destinationStates.Length > 0) AnimatorLayerOps.MultiTransition(multiSM, multiSources, destinationStates);
                        currentEvent.Use();
                        return;
                    }
                    if (PatchStateNodeMenu._redirectTransitions != null)
                    {
                        var destinationStates = Selection.objects.OfType<AnimatorState>().ToArray();
                        var redirectTransitions = PatchStateNodeMenu._redirectTransitions;
                        var redirectSM = PatchStateNodeMenu._redirectSM;
                        PatchStateNodeMenu._redirectTransitions = null;
                        PatchStateNodeMenu._redirectSM = null;
                        if (destinationStates.Length > 0) AnimatorLayerOps.RedirectTransitions(redirectSM, redirectTransitions, destinationStates);
                        currentEvent.Use();
                        return;
                    }
                    if (PatchStateNodeMenu._replicateTransitions != null)
                    {
                        var newSourceStates = Selection.objects.OfType<AnimatorState>().ToArray();
                        var replicateTransitions = PatchStateNodeMenu._replicateTransitions;
                        var replicateSM = PatchStateNodeMenu._replicateSM;
                        PatchStateNodeMenu._replicateTransitions = null;
                        PatchStateNodeMenu._replicateSM = null;
                        if (newSourceStates.Length > 0) AnimatorLayerOps.ReplicateTransitions(replicateSM, replicateTransitions, newSourceStates);
                        currentEvent.Use();
                        return;
                    }
                }

                if (currentEvent.type == EventType.KeyDown && currentEvent.control && currentEvent.keyCode == KeyCode.C)
                {
                    var selectedTransitions = Selection.objects.OfType<AnimatorStateTransition>().ToArray();
                    var selectedStates = Selection.objects.OfType<AnimatorState>().ToArray();
                    if (selectedTransitions.Length > 0 && selectedStates.Length == 0) { PatchTransitionCopyPaste.SetClipboard(selectedTransitions); PatchCopySelectionToPasteboard.ClearCopy(); currentEvent.Use(); return; }
                }

                if (currentEvent.type == EventType.KeyDown && currentEvent.control && currentEvent.keyCode == KeyCode.V
                    && PatchTransitionCopyPaste.HasClipboard)
                {
                    var pasteSource = Selection.activeObject as AnimatorState;
                    if (pasteSource != null)
                    {
                        var pasteGraph = MGraphField(__instance)?.GetValue(__instance);
                        foreach (var node in GetNodes(pasteGraph) ?? System.Array.Empty<object>())
                        {
                            if (node.GetType() != AnimatorEditorInit.StateNodeType) continue;
                            var nodeState = GraphPatchReflection.StateNodeStateField?.GetValue(node) as AnimatorState;
                            if (nodeState != pasteSource) continue;
                            var sourceRect = Traverse.Create(node).Field("position").GetValue<Rect>();
                            PatchTransitionCopyPaste.BeginPaste(pasteSource, sourceRect);
                            if (AnimWindow != null) AnimWindow.wantsMouseMove = true;
                            currentEvent.Use();
                            break;
                        }
                    }
                    return;
                }

                if ((PatchStateChainTransition.ChainActive || PatchTransitionCopyPaste.PasteActive) && currentEvent.type == EventType.MouseMove)
                    UpdateSnapTarget(__instance, currentEvent.mousePosition);

                if (currentEvent.type != EventType.MouseDown || currentEvent.clickCount != 2 || currentEvent.button != 0 || currentEvent.control)
                    return;

                var mousePos = currentEvent.mousePosition;
                var graph = MGraphField(__instance)?.GetValue(__instance);
                if (graph == null) return;

                var nodes = GetNodes(graph);
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        var pos = Traverse.Create(node).Field("position").GetValue();
                        if (pos is Rect rect && rect.Contains(mousePos)) return;
                    }
                }

                var getActiveStateMachine = AccessTools.Method(__instance.GetType(), "get_activeStateMachine");
                var activeStateMachine = getActiveStateMachine?.Invoke(__instance, null) as AnimatorStateMachine;
                if (activeStateMachine == null) return;

                Undo.RegisterCompleteObjectUndo(activeStateMachine, "Create State");
                var newState = activeStateMachine.AddState("New State");

                var states = activeStateMachine.states;
                for (int i = 0; i < states.Length; i++)
                {
                    if (states[i].state != newState) continue;
                    var childAnimatorState = states[i];
                    childAnimatorState.position = new Vector3(mousePos.x - 100, mousePos.y - 22, 0);
                    states[i] = childAnimatorState;
                    break;
                }
                activeStateMachine.states = states;
                EditorUtility.SetDirty(activeStateMachine);

                var bufferClip = FindBufferClip();
                if (bufferClip != null)
                {
                    Undo.RegisterCompleteObjectUndo(newState, "Create State");
                    newState.motion = bufferClip;
                    EditorUtility.SetDirty(newState);
                }

                currentEvent.Use();
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Double-click create state error: {e}");
            }
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            if (_pasteSM == null) return;
            try
            {
                var allChildStates = _pasteSM.states;
                var allChildSMs = _pasteSM.stateMachines;

                var newStateIndices = new List<int>();
                for (int i = 0; i < allChildStates.Length; i++)
                {
                    if (!_prepasteStateSet.Contains(allChildStates[i].state))
                        newStateIndices.Add(i);
                }

                var newSubSMIndices = new List<int>();
                if (_prepasteSubSMSet != null)
                {
                    for (int i = 0; i < allChildSMs.Length; i++)
                    {
                        if (!_prepasteSubSMSet.Contains(allChildSMs[i].stateMachine))
                            newSubSMIndices.Add(i);
                    }
                }

                if (newStateIndices.Count == 0 && newSubSMIndices.Count == 0) return;

                Vector2 centroid = Vector2.zero;
                int totalNodes = newStateIndices.Count + newSubSMIndices.Count;
                foreach (int index in newStateIndices)
                    centroid += new Vector2(allChildStates[index].position.x, allChildStates[index].position.y);
                foreach (int index in newSubSMIndices)
                    centroid += new Vector2(allChildSMs[index].position.x, allChildSMs[index].position.y);
                centroid /= totalNodes;

                Vector2 offset = _lastMousePosition - centroid;

                for (int j = 0; j < newStateIndices.Count; j++)
                {
                    int index = newStateIndices[j];
                    var childState = allChildStates[index];
                    childState.position = new Vector3(
                        childState.position.x + offset.x,
                        childState.position.y + offset.y,
                        childState.position.z);
                    allChildStates[index] = childState;
                }

                for (int j = 0; j < newSubSMIndices.Count; j++)
                {
                    int index = newSubSMIndices[j];
                    var childSM = allChildSMs[index];
                    childSM.position = new Vector3(
                        childSM.position.x + offset.x,
                        childSM.position.y + offset.y,
                        childSM.position.z);
                    allChildSMs[index] = childSM;
                }

                _pasteSM.states = allChildStates;
                _pasteSM.stateMachines = allChildSMs;
                EditorUtility.SetDirty(_pasteSM);
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Paste reposition error: {e}");
            }
            finally
            {
                _pasteSM = null;
                _prepasteStateSet = null;
                _prepasteSubSMSet = null;
            }
        }

        /* Updates PatchStateChainTransition.SnapTarget to the center of whichever state node the mouse is over, or null. */
        static void UpdateSnapTarget(object graphGUI, Vector2 mousePos)
        {
            var graph = MGraphField(graphGUI)?.GetValue(graphGUI);
            if (graph == null) { PatchStateChainTransition.SnapTarget = null; return; }

            foreach (var node in GetNodes(graph) ?? Array.Empty<object>())
            {
                if (node.GetType() != AnimatorEditorInit.StateNodeType) continue;
                var pos = Traverse.Create(node).Field("position").GetValue();
                if (pos is Rect rect && rect.Contains(mousePos))
                {
                    PatchStateChainTransition.SnapTarget = rect.center;
                    return;
                }
            }
            PatchStateChainTransition.SnapTarget = null;
        }

        static AnimationClip FindBufferClip()
        {
            if (_bufferClip != null) return _bufferClip;
            var guids = AssetDatabase.FindAssets("_buffer t:AnimationClip", new[] { "Packages/com.ygdr.animator/Templates" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == "_buffer")
                {
                    _bufferClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    return _bufferClip;
                }
            }
            return null;
        }

        /* Returns the nodes collection from a graph object, trying the nodes property then the nodes field. */
        internal static IEnumerable GetNodes(object graph)
        {
            var traverse = Traverse.Create(graph);
            return traverse.Property("nodes").GetValue() as IEnumerable
                ?? traverse.Field("nodes").GetValue() as IEnumerable;
        }
    }

    // Draws chain-mode transition preview line on the same layer as real edges (under nodes)
    [HarmonyPatch]
    internal static class PatchEdgeGUIDoEdges
    {
        static FastInvokeHandler _drawArrowInvoker;
        static FastInvokeHandler _edgeSizeMultiplierInvoker;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.DoEdgesMethod;

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            bool isActive = PatchStateChainTransition.ChainActive || PatchTransitionCopyPaste.PasteActive;
            if (!isActive) return;
            try
            {
                PatchGraphDoubleClickCreate.AnimWindow?.Repaint();

                if (Event.current.type != EventType.Repaint) return;

                var sourceRect = PatchStateChainTransition.ChainActive
                    ? PatchStateChainTransition.ChainSourceRect
                    : PatchTransitionCopyPaste.PasteSourceRect;
                if (sourceRect == Rect.zero) return;

                var source = new Vector3(sourceRect.center.x, sourceRect.center.y, 0);
                Vector3 destination;
                if (PatchStateChainTransition.SnapTarget.HasValue)
                {
                    var snap = PatchStateChainTransition.SnapTarget.Value;
                    destination = new Vector3(snap.x, snap.y, 0);
                }
                else
                {
                    destination = new Vector3(Event.current.mousePosition.x, Event.current.mousePosition.y, 0);
                }

                var direction     = (destination - source).normalized;
                var perpendicular = new Vector3(-direction.y, direction.x, 0);
                var midpoint      = (source + destination) * 0.5f;
                _edgeSizeMultiplierInvoker ??= MethodInvoker.GetHandler(GraphPatchReflection.EdgeSizeMultiplierGetter);
                float mult = _edgeSizeMultiplierInvoker != null ? (float)_edgeSizeMultiplierInvoker(__instance) : 1f;
                var previewColor  = new Color(1f, 1f, 1f, 0.8f);

                Handles.BeginGUI();
                Handles.color = previewColor;
                Handles.DrawAAPolyLine(4f * mult, source, destination);
                Handles.EndGUI();

                _drawArrowInvoker ??= MethodInvoker.GetHandler(GraphPatchReflection.DrawArrowMethod);
                _drawArrowInvoker?.Invoke(null, previewColor, perpendicular, direction, midpoint,
                    5f * mult, 2f * mult);

                if (AnimatorDefaultSettings.Load().transitionAnimateSelected)
                {
                    var animatedPosition = PatchDrawEdge.GetAnimatedArrowPosition(source, midpoint, destination);
                    _drawArrowInvoker?.Invoke(null, previewColor, perpendicular, direction, animatedPosition,
                        5f * mult, 2f * mult);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Chain line draw error: {e}");
            }
        }

        // Layer 2: swallow exceptions from conflicting transpilers on this hot path to prevent GUI lockup
        [HarmonyFinalizer]
        static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
                Debug.LogError($"[AnimatorTools] Exception in DoEdges — disable conflicting feature in Compatibility settings: {__exception.Message}");
            return null;
        }
    }

    // Ctrl+double-click state → begin transition chain; click next state to continue; Escape to stop
    [HarmonyPatch]
    internal static class PatchStateChainTransition
    {
        internal static bool ChainActive { get; private set; }
        internal static Rect ChainSourceRect { get; private set; }
        internal static Vector2? SnapTarget { get; set; }
        private static AnimatorState _chainSource;

        internal static void Clear()
        {
            ChainActive = false;
            _chainSource = null;
            ChainSourceRect = Rect.zero;
            SnapTarget = null;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "NodeUI",
                new[] { GraphPatchReflection.GraphGUIType });

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
                var currentEvent = Event.current;
                if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0) return;

                var nodeState = GraphPatchReflection.StateNodeStateField?.GetValue(__instance) as AnimatorState;
                if (nodeState == null) return;

                if (currentEvent.control && currentEvent.clickCount == 2)
                {
                    ChainActive = true;
                    _chainSource = nodeState;
                    ChainSourceRect = Traverse.Create(__instance).Field("position").GetValue<Rect>();
                    SnapTarget = null;
                    // Enable MouseMove delivery once when chain starts
                    if (PatchGraphDoubleClickCreate.AnimWindow != null)
                        PatchGraphDoubleClickCreate.AnimWindow.wantsMouseMove = true;
                    currentEvent.Use();
                    return;
                }

                if (ChainActive && currentEvent.clickCount == 1 && !currentEvent.control)
                {
                    AnimatorStateOps.AddChainTransition(_chainSource, nodeState);
                    _chainSource = nodeState;
                    ChainSourceRect = Traverse.Create(__instance).Field("position").GetValue<Rect>();
                    SnapTarget = null;
                    currentEvent.Use();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Chain transition error: {e}");
            }
        }
    }
    // Ctrl+C to copy selected transitions, Ctrl+V on source state, click destination to paste
    [HarmonyPatch]
    internal static class PatchTransitionCopyPaste
    {
        static AnimatorTransitionOps.TransitionData[] _clipboard;
        static AnimatorState _pasteSource;

        internal static bool PasteActive { get; private set; }
        internal static Rect PasteSourceRect { get; private set; }
        internal static bool HasClipboard => _clipboard != null && _clipboard.Length > 0;
        internal static int ClipboardCount => _clipboard?.Length ?? 0;

        /* Snapshots transition data at copy time so clipboard survives deletion of the originals. */
        internal static void SetClipboard(AnimatorStateTransition[] transitions) =>
            _clipboard = transitions?.Select(AnimatorTransitionOps.TransitionData.From).ToArray();

        /* Activates paste mode, recording the source state and its node rect for preview line drawing. */
        internal static void BeginPaste(AnimatorState source, Rect sourceRect)
        {
            PasteActive = true;
            _pasteSource = source;
            PasteSourceRect = sourceRect;
            PatchStateChainTransition.SnapTarget = null;
        }

        internal static void ClearPaste()
        {
            PasteActive = false;
            _pasteSource = null;
            PasteSourceRect = Rect.zero;
            PatchStateChainTransition.SnapTarget = null;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.StateNodeType, "NodeUI",
                new[] { GraphPatchReflection.GraphGUIType });

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            if (!PasteActive) return;
            try
            {
                var currentEvent = Event.current;
                if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0 || currentEvent.clickCount != 1) return;

                var destinationState = GraphPatchReflection.StateNodeStateField?.GetValue(__instance) as AnimatorState;
                if (destinationState == null) return;

                AnimatorTransitionOps.PasteTransitions(_pasteSource, destinationState, _clipboard);
                ClearPaste();
                currentEvent.Use();
            }
            catch (Exception e)
            {
                Debug.LogError($"[YGDR] Paste transitions error: {e}");
            }
        }

    }

    // Captures source sub-SM on copy; uses ObjectChangeEvents to detect paste at any time
    [HarmonyPatch]
    internal static class PatchCopySelectionToPasteboard
    {
        static AnimatorStateMachine _sourceSM;
        static AnimatorController _sourceController;
        static AnimatorStateMachine _monitorActiveSM;
        static ChildAnimatorStateMachine[] _monitorSnapshot;

        internal static void ClearCopy()
        {
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            _sourceSM = null;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() => GraphPatchReflection.CopySelectionToPasteboardMethod;

        [HarmonyPostfix]
        static void Postfix(object __instance, bool __result)
        {
            if (!__result) return;

            try
            {
                ObjectChangeEvents.changesPublished -= OnChangesPublished;
                _sourceSM = null;

                _sourceSM = Selection.objects
                    .OfType<AnimatorStateMachine>()
                    .FirstOrDefault();
                if (_sourceSM == null) return;

                PatchTransitionCopyPaste.SetClipboard(null);

                _sourceController = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    AssetDatabase.GetAssetPath(_sourceSM));
                if (_sourceController == null) { _sourceSM = null; return; }

                var getActiveSM = AccessTools.Method(__instance.GetType(), "get_activeStateMachine");
                _monitorActiveSM = getActiveSM?.Invoke(__instance, null) as AnimatorStateMachine;
                _monitorSnapshot = _monitorActiveSM?.stateMachines.ToArray()
                    ?? new ChildAnimatorStateMachine[0];

                ObjectChangeEvents.changesPublished += OnChangesPublished;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Sub-SM copy frame capture failed: {e}");
            }
        }

        static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (_sourceSM == null || _monitorActiveSM == null) return;

            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.CreateAssetObject) continue;
                stream.GetCreateAssetObjectEvent(i, out var eventData);
                if (EditorUtility.InstanceIDToObject(eventData.instanceId) is not AnimatorStateMachine) continue;

                var currentChildSMs = _monitorActiveSM.stateMachines;
                if (currentChildSMs.Length == _monitorSnapshot.Length) continue;

                var newChildSMs = currentChildSMs
                    .Where(childSM => !_monitorSnapshot.Any(snapshot => snapshot.stateMachine == childSM.stateMachine))
                    .ToArray();
                if (newChildSMs.Length == 0) continue;

                ObjectChangeEvents.changesPublished -= OnChangesPublished;
                ApplyFrames(newChildSMs[0].stateMachine);
                return;
            }
        }

        static void ApplyFrames(AnimatorStateMachine destinationSM)
        {
            try
            {
                var destinationController = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    AssetDatabase.GetAssetPath(_monitorActiveSM));
                if (destinationController == null) return;

                var destinationLayerSM = FrameRenderer.GetRootLayerSM(destinationController, _monitorActiveSM);
                if (destinationLayerSM == null) return;

                var sourceData = FrameLayoutData.GetOrCreate(_sourceController);
                var destinationData = FrameLayoutData.GetOrCreate(destinationController);

                var smMap = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
                PatchLayerCopyPaste.BuildSMMap(_sourceSM, destinationSM, smMap);

                bool dirty = false;
                foreach (var frame in sourceData.frames.ToArray())
                {
                    if (!smMap.TryGetValue(frame.activeSM, out var mappedActiveSM)) continue;
                    destinationData.frames.Add(new FrameRect
                    {
                        title             = frame.title,
                        layerStateMachine = destinationLayerSM,
                        activeSM          = mappedActiveSM,
                        bounds            = frame.bounds,
                        color             = frame.color,
                        locked            = frame.locked,
                    });
                    dirty = true;
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(destinationData);
                    AssetDatabase.SaveAssets();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Sub-SM paste frame copy failed: {e}");
            }
            finally
            {
                _sourceSM = null;
            }
        }

    }
    // ─── Drag-and-drop clip onto existing node ────────────────────────────────────────────────────────────────────

    // Intercepts AnimatorStateMachine.AddState(name, position) during drag-and-drop.
    // Single clip on existing node: assigns clip without creating a new state.
    // Multiple clips: creates one state per clip, cascaded diagonally from drop position.
    [HarmonyPatch(typeof(AnimatorStateMachine), "AddState", new[] { typeof(string), typeof(Vector3) })]
    internal static class PatchAddStateDrop
    {
        static int[]   _activeDropClipIds   = Array.Empty<int>();
        static int     _activeDropCallIndex  = 0;
        static bool    _handlingDrop         = false;
        static Vector3 _dropBasePosition;
        internal static bool DropIntercepted = false;

        [HarmonyPrefix]
        static bool Prefix(AnimatorStateMachine __instance, Vector3 position, ref AnimatorState __result)
        {
            if (_handlingDrop) return true;
            if (PatchBlendTreeOnGraphGUI.InBlendTreeGUI) return true;
            try
            {
                var clips = DragAndDrop.objectReferences.OfType<AnimationClip>().ToArray();
                if (clips.Length == 0) return true;

                // Single clip on existing node: assign without creating new state; otherwise create with sanitized name
                if (clips.Length == 1)
                {
                    const float nodeW = 200f, nodeH = 40f;
                    foreach (var childState in __instance.states)
                    {
                        var nodeRect = new Rect(childState.position.x, childState.position.y, nodeW, nodeH);
                        if (!nodeRect.Contains(new Vector2(position.x, position.y))) continue;

                        Undo.RegisterCompleteObjectUndo(childState.state, "Assign Motion Clip");
                        childState.state.motion = clips[0];
                        EditorUtility.SetDirty(childState.state);
                        __result = childState.state;
                        DropIntercepted = true;
                        return false;
                    }

                    var sanitizedSingleName = clips[0].name.Replace('.', '_');
                    _handlingDrop = true;
                    try
                    {
                        var newState = __instance.AddState(sanitizedSingleName, position);
                        if (newState != null)
                        {
                            Undo.RegisterCompleteObjectUndo(newState, "Drag Drop Clip");
                            newState.motion = clips[0];
                            EditorUtility.SetDirty(newState);
                        }
                        __result = newState;
                    }
                    finally { _handlingDrop = false; }
                    return false;
                }

                // Multiple clips: track call index per drop operation
                var clipIds = clips.Select(c => c.GetInstanceID()).ToArray();
                bool isSameDrop = clipIds.SequenceEqual(_activeDropClipIds) && _activeDropCallIndex < clips.Length;
                if (!isSameDrop)
                {
                    _activeDropClipIds  = clipIds;
                    _activeDropCallIndex = 0;
                }

                int callIndex = _activeDropCallIndex++;
                if (callIndex >= clips.Length) return true;
                if (callIndex == 0) _dropBasePosition = position;

                const float cascadeStepX = 40f;
                const float cascadeStepY = 65f;
                var cascadePosition = _dropBasePosition + new Vector3(callIndex * cascadeStepX, callIndex * cascadeStepY, 0f);

                _handlingDrop = true;
                try
                {
                    var newState = __instance.AddState(clips[callIndex].name.Replace('.', '_'), cascadePosition);
                    if (newState != null)
                    {
                        Undo.RegisterCompleteObjectUndo(newState, "Drag Drop Clips");
                        newState.motion = clips[callIndex];
                        EditorUtility.SetDirty(newState);
                    }
                    __result = newState;
                }
                finally { _handlingDrop = false; }
                return false;
            }
            catch (Exception e) { Debug.LogError($"[YGDR] AddState drop error: {e}"); }
            return true;
        }
    }
}
#endif
