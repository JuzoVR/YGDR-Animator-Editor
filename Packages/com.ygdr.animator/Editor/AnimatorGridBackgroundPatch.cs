#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using HarmonyLib;

namespace YGDR.Editor.Animation
{
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class AnimatorGridBackgroundPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.Graphs.GraphGUI"),
                "DrawGrid");

        static Material _coloredMat;
        static Material ColoredMat => _coloredMat ??=
            new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };

        [HarmonyPrefix]
        static bool Prefix(Rect gridRect, float zoomLevel)
        {
            var settings = AnimatorDefaultSettings.Load();
            if (!settings.graphGridOverride || Event.current.type != EventType.Repaint)
                return true;

            float t = Mathf.InverseLerp(0.1f, 1f, zoomLevel);
            Color minorColor = Color.Lerp(Color.clear, settings.graphGridColorMinor, t);
            Color majorColor = Color.Lerp(settings.graphGridColorMinor, settings.graphGridColorMajor, t);

            if (settings.graphGridUseImage && settings.graphGridBackgroundImage != null)
            {
                // All GUI — DrawTexture then DrawRect lines, both deferred, render in call order
                var previousColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, settings.graphGridBackgroundImageOpacity);
                GUI.DrawTexture(gridRect, settings.graphGridBackgroundImage, ScaleMode.ScaleAndCrop);
                GUI.color = previousColor;
                if (settings.graphGridDrawLines)
                {
                    DrawGridRectsGUI(gridRect, settings.graphGridScalingMajor * (100f / settings.graphGridDivisorMinor), minorColor);
                    DrawGridRectsGUI(gridRect, settings.graphGridScalingMajor * 100f, majorColor);
                }
            }
            else
            {
                // All GL — solid color background + lines
                ColoredMat.SetPass(0);
                GL.PushMatrix();

                GL.Begin(GL.QUADS);
                var backgroundColor = settings.graphGridBackgroundColor;
                backgroundColor.a = 1f;
                GL.Color(backgroundColor);
                GL.Vertex3(gridRect.xMin, gridRect.yMin, 0);
                GL.Vertex3(gridRect.xMax, gridRect.yMin, 0);
                GL.Vertex3(gridRect.xMax, gridRect.yMax, 0);
                GL.Vertex3(gridRect.xMin, gridRect.yMax, 0);
                GL.End();

                if (settings.graphGridDrawLines)
                {
                    GL.Begin(GL.LINES);
                    GL.Color(minorColor);
                    DrawGridLinesGL(gridRect, settings.graphGridScalingMajor * (100f / settings.graphGridDivisorMinor));
                    GL.Color(majorColor);
                    DrawGridLinesGL(gridRect, settings.graphGridScalingMajor * 100f);
                    GL.End();
                }

                GL.PopMatrix();
            }

            return false;
        }

        [HarmonyPostfix]
        static void Postfix(object __instance, Rect gridRect, float zoomLevel)
        {
            if (Event.current.type != EventType.Repaint) return;
            FrameRenderer.DrawFrames(__instance, gridRect, zoomLevel);
        }

        /* Emits GL.LINES for a uniform grid of vertical and horizontal lines at gridSize spacing within gridRect. */
        static void DrawGridLinesGL(Rect gridRect, float gridSize)
        {
            if (gridSize < 1f) gridSize = 1f;
            for (float currentX = gridRect.xMin - (gridRect.xMin % gridSize); currentX < gridRect.xMax; currentX += gridSize)
            {
                GL.Vertex3(currentX, gridRect.yMin, 0);
                GL.Vertex3(currentX, gridRect.yMax, 0);
            }
            for (float currentY = gridRect.yMin - (gridRect.yMin % gridSize); currentY < gridRect.yMax; currentY += gridSize)
            {
                GL.Vertex3(gridRect.xMin, currentY, 0);
                GL.Vertex3(gridRect.xMax, currentY, 0);
            }
        }

        /* Draws a uniform grid of 1px-wide vertical and horizontal rects at gridSize spacing within gridRect using EditorGUI.DrawRect. Used in image-background mode where GL cannot be mixed with GUI texture calls. */
        static void DrawGridRectsGUI(Rect gridRect, float gridSize, Color color)
        {
            if (gridSize < 1f) gridSize = 1f;
            for (float currentX = gridRect.xMin - (gridRect.xMin % gridSize); currentX < gridRect.xMax; currentX += gridSize)
                EditorGUI.DrawRect(new Rect(currentX, gridRect.yMin, 1, gridRect.height), color);
            for (float currentY = gridRect.yMin - (gridRect.yMin % gridSize); currentY < gridRect.yMax; currentY += gridSize)
                EditorGUI.DrawRect(new Rect(gridRect.xMin, currentY, gridRect.width, 1), color);
        }
    }

    internal static class FrameRenderer
    {
        internal static FrameRect SelectedFrame;
        internal static Rect LastGridRect;
        internal static float LastZoomLevel;
        internal static Vector2 LastScrollPosition;
        internal static FrameLayoutData LastFrameData;
        internal static AnimatorStateMachine LastRootLayerSM;
        internal static AnimatorStateMachine LastActiveSM;

        static GUIStyle _wrappedBoldLabel;

        static Type _cachedGraphGUIType;
        static PropertyInfo _scrollPositionProperty;
        static MethodInfo _getActiveStateMachineMethod;

        static AnimatorController _cachedController;
        static FrameLayoutData _cachedFrameData;
        static bool _cacheValid;

        [InitializeOnLoadMethod]
        static void RegisterCacheInvalidation()
        {
            Undo.postprocessModifications -= OnPostprocessModifications;
            Undo.postprocessModifications += OnPostprocessModifications;
            Undo.undoRedoPerformed -= InvalidateCache;
            Undo.undoRedoPerformed += InvalidateCache;
        }

        static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            foreach (var mod in modifications)
            {
                if (mod.currentValue?.target is AnimatorController || mod.currentValue?.target is AnimatorStateMachine)
                {
                    _cacheValid = false;
                    break;
                }
            }
            return modifications;
        }

        static void InvalidateCache() => _cacheValid = false;

        static void EnsureReflection(object graphGUI)
        {
            var type = graphGUI.GetType();
            if (type == _cachedGraphGUIType) return;
            _cachedGraphGUIType = type;
            _scrollPositionProperty = AccessTools.Property(type, "scrollPosition");
            _getActiveStateMachineMethod = AccessTools.Method(type, "get_activeStateMachine");
        }

        static FrameLayoutData GetFrameData(AnimatorController controller)
        {
            if (_cacheValid && controller == _cachedController && _cachedFrameData != null)
                return _cachedFrameData;
            _cachedController = controller;
            _cachedFrameData = FrameLayoutData.GetOrCreate(controller);
            _cacheValid = true;
            return _cachedFrameData;
        }

        static AnimatorStateMachine GetRootLayerSM(AnimatorController controller, AnimatorStateMachine activeSM)
        {
            foreach (var layer in controller.layers)
                if (ContainsStateMachine(layer.stateMachine, activeSM))
                    return layer.stateMachine;
            return null;
        }

        static bool ContainsStateMachine(AnimatorStateMachine root, AnimatorStateMachine target)
        {
            if (root == target) return true;
            foreach (var child in root.stateMachines)
                if (ContainsStateMachine(child.stateMachine, target)) return true;
            return false;
        }

        internal static void DrawFrames(object graphGUI, Rect gridRect, float zoomLevel)
        {
            try
            {
                EnsureReflection(graphGUI);

                var scrollPosition = (Vector2)_scrollPositionProperty.GetValue(graphGUI);
                var activeSM = _getActiveStateMachineMethod?.Invoke(graphGUI, null) as AnimatorStateMachine;
                if (activeSM == null) return;

                var controllerPath = AssetDatabase.GetAssetPath(activeSM);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller == null) return;

                var frameData = GetFrameData(controller);
                if (frameData == null) return;

                var rootLayerSM = GetRootLayerSM(controller, activeSM);

                LastGridRect = gridRect;
                LastZoomLevel = zoomLevel;
                LastScrollPosition = scrollPosition;
                LastFrameData = frameData;
                LastRootLayerSM = rootLayerSM;
                LastActiveSM = activeSM;

                if (frameData.frames.Count == 0) return;

                foreach (var frame in frameData.frames)
                {
                    if (rootLayerSM != null && frame.layerStateMachine != null && frame.layerStateMachine != rootLayerSM) continue;
                    var screenRect = GraphToScreen(frame.bounds, scrollPosition);
                    DrawFrame(frame, screenRect);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[AnimatorTools] FrameRenderer error: {exception}");
            }
        }

        // GUI.matrix already handles zoom — no zoom multiplication needed here.
        // scrollPosition shifts the graph viewport; subtract to get GUI-space position.
        internal static Rect GraphToScreen(Rect graphRect, Vector2 scrollPosition)
        {
            return new Rect(
                graphRect.x - scrollPosition.x,
                graphRect.y - scrollPosition.y,
                graphRect.width,
                graphRect.height);
        }

        internal static Rect ScreenToGraph(Rect screenRect, Vector2 scrollPosition)
        {
            return new Rect(
                screenRect.x + scrollPosition.x,
                screenRect.y + scrollPosition.y,
                screenRect.width,
                screenRect.height);
        }

        static void DrawFrame(FrameRect frame, Rect screenRect)
        {
            bool isDarkSkin = EditorGUIUtility.isProSkin;
            Color textColor = isDarkSkin ? Color.white : new Color(0.1f, 0.1f, 0.1f);

            EditorGUI.DrawRect(screenRect, frame.color);

            var previousContentColor = GUI.contentColor;

            // Title — suppressed when renaming (TextField drawn in interaction prefix)
            _wrappedBoldLabel ??= new GUIStyle(EditorStyles.boldLabel) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            var titleRect = new Rect(screenRect.x + 24, screenRect.y + 2, screenRect.width - 28, screenRect.height - 6);
            if (!(FrameInteractionPatch.IsRenaming && frame == SelectedFrame))
            {
                GUI.contentColor = textColor;
                GUI.Label(titleRect, frame.title, _wrappedBoldLabel);
            }

            GUI.contentColor = previousContentColor;

            // Lock icon (always rendered, top-left)
            var lockIconRect = new Rect(screenRect.x + 2, screenRect.y + 2, 18, 18);
            var lockIcon = frame.locked
                ? EditorGUIUtility.IconContent("LockIcon-On")
                : EditorGUIUtility.IconContent("LockIcon");
            GUI.Label(lockIconRect, lockIcon);

            // Resize handles — selected + unlocked only
            if (frame == SelectedFrame && !frame.locked)
                DrawResizeHandles(screenRect);
        }

        static void DrawResizeHandles(Rect screenRect)
        {
            foreach (var handleRect in GetHandleRects(screenRect))
                EditorGUI.DrawRect(handleRect, new Color(1f, 1f, 1f, 0.8f));
        }

        internal static Rect[] GetHandleRects(Rect screenRect)
        {
            const float handleSize = 8f;
            float half = handleSize * 0.5f;
            float centerX = screenRect.x + screenRect.width * 0.5f;
            float centerY = screenRect.y + screenRect.height * 0.5f;
            float right = screenRect.xMax;
            float bottom = screenRect.yMax;

            return new[]
            {
                new Rect(screenRect.x - half, screenRect.y - half, handleSize, handleSize), // top-left
                new Rect(right - half,         screenRect.y - half, handleSize, handleSize), // top-right
                new Rect(screenRect.x - half,  bottom - half,       handleSize, handleSize), // bottom-left
                new Rect(right - half,         bottom - half,       handleSize, handleSize), // bottom-right
                new Rect(centerX - half,       screenRect.y - half, handleSize, handleSize), // top-mid
                new Rect(centerX - half,       bottom - half,       handleSize, handleSize), // bottom-mid
                new Rect(screenRect.x - half,  centerY - half,      handleSize, handleSize), // left-mid
                new Rect(right - half,         centerY - half,      handleSize, handleSize), // right-mid
            };
        }
    }

    [HarmonyPatch]
    [HarmonyPriority(Priority.VeryLow)]
    internal static class FrameInteractionPatch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.Graphs.AnimationStateMachine.GraphGUI"),
                "OnGraphGUI");

        enum DragState { None, Moving, Resizing }

        static DragState _dragState;
        static Vector2 _dragStartMouse;
        static Rect _dragStartBounds;
        static int _dragHandleIndex;

        internal static bool IsRenaming;
        internal static string RenameBuffer;
        internal static bool IsPickingColor;
        internal static FrameRect ColorPickerTarget;

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            var frameData = FrameRenderer.LastFrameData;
            if (frameData == null) return;

            var currentEvent = Event.current;
            var gridRect = FrameRenderer.LastGridRect;
            var zoomLevel = FrameRenderer.LastZoomLevel;
            var scrollPosition = FrameRenderer.LastScrollPosition;

            // Inline rename text field
            if (IsRenaming && FrameRenderer.SelectedFrame != null)
            {
                var selectedFrame = FrameRenderer.SelectedFrame;
                var frameScreenRect = FrameRenderer.GraphToScreen(selectedFrame.bounds, scrollPosition);
                var renameRect = new Rect(frameScreenRect.x + 24, frameScreenRect.y + 2, frameScreenRect.width - 28, 18);

                // Check keys BEFORE TextField consumes them
                if (currentEvent.type == EventType.KeyDown)
                {
                    if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                    {
                        Undo.RegisterCompleteObjectUndo(frameData, "Rename Frame");
                        selectedFrame.title = RenameBuffer;
                        EditorUtility.SetDirty(frameData);
                        IsRenaming = false;
                        currentEvent.Use();
                        return;
                    }
                    if (currentEvent.keyCode == KeyCode.Escape)
                    {
                        IsRenaming = false;
                        currentEvent.Use();
                        return;
                    }
                }

                // Click outside rename rect — commit and fall through to normal interaction
                if (currentEvent.type == EventType.MouseDown && !renameRect.Contains(currentEvent.mousePosition))
                {
                    Undo.RegisterCompleteObjectUndo(frameData, "Rename Frame");
                    selectedFrame.title = RenameBuffer;
                    EditorUtility.SetDirty(frameData);
                    IsRenaming = false;
                    // no return — fall through so click deselects/selects normally
                }
                else
                {
                    GUI.SetNextControlName("FrameRename");
                    RenameBuffer = GUI.TextField(renameRect, RenameBuffer, EditorStyles.boldLabel);
                    EditorGUI.FocusTextInControl("FrameRename");
                    return;
                }
            }

            // Color picker overlay
            if (IsPickingColor && ColorPickerTarget != null)
            {
                var colorFrame = ColorPickerTarget;
                var colorFrameRect = FrameRenderer.GraphToScreen(colorFrame.bounds, scrollPosition);
                var colorFieldRect = new Rect(colorFrameRect.x, colorFrameRect.y - 24, 180, 18);

                EditorGUI.BeginChangeCheck();
                var newColor = EditorGUI.ColorField(colorFieldRect, colorFrame.color);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RegisterCompleteObjectUndo(frameData, "Change Frame Color");
                    colorFrame.color = newColor;
                    EditorUtility.SetDirty(frameData);
                }

                if (currentEvent.type == EventType.MouseDown && !colorFieldRect.Contains(currentEvent.mousePosition))
                {
                    IsPickingColor = false;
                    ColorPickerTarget = null;
                    // fall through — let click proceed normally
                }
                else
                {
                    return;
                }
            }

            // 1. Delete key
            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Delete
                && FrameRenderer.SelectedFrame != null)
            {
                Undo.RegisterCompleteObjectUndo(frameData, "Delete Frame");
                frameData.frames.Remove(FrameRenderer.SelectedFrame);
                FrameRenderer.SelectedFrame = null;
                EditorUtility.SetDirty(frameData);
                currentEvent.Use();
                return;
            }

            // Drag continuation
            if (_dragState != DragState.None)
            {
                if (currentEvent.type == EventType.MouseDrag)
                {
                    HandleDragUpdate(currentEvent.mousePosition);
                    currentEvent.Use();
                    return;
                }
                if (currentEvent.type == EventType.MouseUp)
                {
                    EditorUtility.SetDirty(frameData);
                    _dragState = DragState.None;
                    currentEvent.Use();
                    return;
                }
            }

            if (currentEvent.type != EventType.MouseDown) return;

            var mousePosition = currentEvent.mousePosition;

            // Hit-test frames (reverse = top-most frame first)
            for (int frameIndex = frameData.frames.Count - 1; frameIndex >= 0; frameIndex--)
            {
                var frame = frameData.frames[frameIndex];
                var screenRect = FrameRenderer.GraphToScreen(frame.bounds, scrollPosition);

                // 2. Lock icon
                var lockIconRect = new Rect(screenRect.x + 2, screenRect.y - 2, 18, 18);
                if (lockIconRect.Contains(mousePosition))
                {
                    Undo.RegisterCompleteObjectUndo(frameData, "Toggle Frame Lock");
                    frame.locked = !frame.locked;
                    EditorUtility.SetDirty(frameData);
                    currentEvent.Use();
                    return;
                }

                if (frame.locked) continue;

                // 3. Resize handles
                var handleRects = FrameRenderer.GetHandleRects(screenRect);
                for (int handleIndex = 0; handleIndex < handleRects.Length; handleIndex++)
                {
                    if (!handleRects[handleIndex].Contains(mousePosition)) continue;
                    FrameRenderer.SelectedFrame = frame;
                    Undo.RegisterCompleteObjectUndo(frameData, "Resize Frame");
                    _dragState = DragState.Resizing;
                    _dragStartMouse = mousePosition;
                    _dragStartBounds = frame.bounds;
                    _dragHandleIndex = handleIndex;
                    currentEvent.Use();
                    return;
                }

                // 4. Header strip
                var headerRect = new Rect(screenRect.x, screenRect.y, screenRect.width, 22);
                if (headerRect.Contains(mousePosition))
                {
                    FrameRenderer.SelectedFrame = frame;
                    if (currentEvent.button == 1)
                    {
                        ShowContextMenu(frame, frameData);
                        currentEvent.Use();
                        return;
                    }
                    Undo.RegisterCompleteObjectUndo(frameData, "Move Frame");
                    _dragState = DragState.Moving;
                    _dragStartMouse = mousePosition;
                    _dragStartBounds = frame.bounds;
                    currentEvent.Use();
                    return;
                }

                // Body — select only if no state node at this position
                if (screenRect.Contains(mousePosition))
                {
                    var graphMouse = mousePosition + scrollPosition;
                    var activeSM = FrameRenderer.LastActiveSM;
                    bool nodeAtMouse = activeSM != null && activeSM.states.Any(childState =>
                        new Rect(childState.position.x, childState.position.y, 200, 44).Contains(graphMouse));
                    if (!nodeAtMouse)
                        FrameRenderer.SelectedFrame = frame;
                    return; // don't consume — let Unity handle node/empty click
                }
            }

            // Empty space or node — deselect
            FrameRenderer.SelectedFrame = null;
            IsRenaming = false;
        }

        static void HandleDragUpdate(Vector2 mousePosition)
        {
            var frame = FrameRenderer.SelectedFrame;
            if (frame == null) return;

            var graphDelta = mousePosition - _dragStartMouse;

            if (_dragState == DragState.Moving)
            {
                frame.bounds = new Rect(
                    _dragStartBounds.x + graphDelta.x,
                    _dragStartBounds.y + graphDelta.y,
                    _dragStartBounds.width,
                    _dragStartBounds.height);
            }
            else if (_dragState == DragState.Resizing)
            {
                var newBounds = _dragStartBounds;
                switch (_dragHandleIndex)
                {
                    case 0: newBounds.xMin += graphDelta.x; newBounds.yMin += graphDelta.y; break;
                    case 1: newBounds.xMax += graphDelta.x; newBounds.yMin += graphDelta.y; break;
                    case 2: newBounds.xMin += graphDelta.x; newBounds.yMax += graphDelta.y; break;
                    case 3: newBounds.xMax += graphDelta.x; newBounds.yMax += graphDelta.y; break;
                    case 4: newBounds.yMin += graphDelta.y; break;
                    case 5: newBounds.yMax += graphDelta.y; break;
                    case 6: newBounds.xMin += graphDelta.x; break;
                    case 7: newBounds.xMax += graphDelta.x; break;
                }
                frame.bounds = newBounds;
            }
        }

        static void ShowContextMenu(FrameRect frame, FrameLayoutData frameData)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename"), false, () =>
            {
                IsRenaming = true;
                RenameBuffer = frame.title;
            });
            menu.AddItem(new GUIContent("Color"), false, () =>
            {
                IsPickingColor = true;
                ColorPickerTarget = frame;
            });
            menu.AddItem(new GUIContent(frame.locked ? "Unlock" : "Lock"), false, () =>
            {
                Undo.RegisterCompleteObjectUndo(frameData, "Toggle Frame Lock");
                frame.locked = !frame.locked;
                EditorUtility.SetDirty(frameData);
            });
            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                Undo.RegisterCompleteObjectUndo(frameData, "Delete Frame");
                frameData.frames.Remove(frame);
                if (FrameRenderer.SelectedFrame == frame) FrameRenderer.SelectedFrame = null;
                EditorUtility.SetDirty(frameData);
            });
            menu.ShowAsContext();
        }
    }
}
#endif
