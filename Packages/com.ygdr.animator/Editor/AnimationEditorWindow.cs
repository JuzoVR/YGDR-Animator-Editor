#if UNITY_EDITOR
using System;
using System.Linq;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow : EditorWindow
    {
        static readonly string[] _tabs = { "Transitions", "States", "Controller", "Settings" };
        bool[] _tabOpen = { true, false, false, false };
        Vector2 _scrollPosition;

        AnimatorStateTransition[] _selectedTransitions = Array.Empty<AnimatorStateTransition>();
        AnimatorState[] _selectedStates = Array.Empty<AnimatorState>();
        AnimatorController _controller;
        AnimatorStateMachine _activeStateMachine;
        string _controllerName = "—";
        string _layerName = "—";
        string[] _subContextPath;
        UnityEngine.Object _cachedGraph;
        UnityEngine.Object _cachedBlendTreeGraphGUI;
        bool _showSharedConditions = true;
        bool _paletteApplied;

        [MenuItem("YGDR/YGDR Animator Editor")]
        static void Open()
        {
            var window = GetWindow<AnimationEditorWindow>("YGDR Animator Editor");
            window.minSize = new Vector2(540, 320);
            window.Show();
        }

        void OnEnable()
        {
            _cachedVersion = null;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += PollAnimatorWindow;
            ObjectChangeEvents.changesPublished += OnAssetChangesPublished;
            wantsMouseMove = true;
            OnSelectionChanged();
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.update -= PollAnimatorWindow;
            ObjectChangeEvents.changesPublished -= OnAssetChangesPublished;
        }

        void OnSelectionChanged()
        {
            _selectedTransitions = Selection.objects.OfType<AnimatorStateTransition>().ToArray();
            _selectedStates = Selection.objects.OfType<AnimatorState>().ToArray();
            _conditionCacheDirty = true;
            Repaint();
        }

        void PollAnimatorWindow()
        {
            if (AnimatorEditorInit.GraphType == null || AnimatorEditorInit.GetActiveStateMachineMethod == null) return;

            AnimatorStateMachine activeStateMachine = null;
            if (_cachedGraph != null)
                activeStateMachine = AnimatorEditorInit.GetActiveStateMachineMethod.Invoke(_cachedGraph, null) as AnimatorStateMachine;

            if (activeStateMachine == null)
            {
                _cachedGraph = null;
                var graphs = Resources.FindObjectsOfTypeAll(AnimatorEditorInit.GraphType);
                foreach (var graph in graphs)
                {
                    activeStateMachine = AnimatorEditorInit.GetActiveStateMachineMethod.Invoke(graph, null) as AnimatorStateMachine;
                    if (activeStateMachine != null) { _cachedGraph = graph; break; }
                }
            }

            if (activeStateMachine == null)
            {
                // Fallback: blend tree view — SM graph returns null activeStateMachine.
                // Derive controller from the blend tree graph's rootBlendTree asset path.
                var blendTreeController = TryGetControllerFromBlendTreeGraph();
                if (blendTreeController != null)
                {
                    var rootBlendTree         = TryGetRootBlendTree();
                    string blendTreeLayerName = rootBlendTree != null ? FindLayerForRootBlendTree(blendTreeController, rootBlendTree) : "—";
                    string blendTreeName      = rootBlendTree?.name;
                    bool subContextUnchanged  = blendTreeName == null ? _subContextPath == null : _subContextPath != null && _subContextPath.Length == 1 && _subContextPath[0] == blendTreeName;
                    if (_controller == blendTreeController && _layerName == blendTreeLayerName && subContextUnchanged) return;
                    _controller     = blendTreeController;
                    _controllerName = blendTreeController.name;
                    _layerName      = blendTreeLayerName;
                    _subContextPath = blendTreeName != null ? new[] { blendTreeName } : null;
                    Repaint();
                    return;
                }

                if (_controller != null) { _controller = null; _activeStateMachine = null; _controllerName = "—"; _layerName = "—"; _subContextPath = null; Repaint(); }
                return;
            }

            var path = AssetDatabase.GetAssetPath(activeStateMachine);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) return;

            string layerName = "—";
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == activeStateMachine || SMContainsOrIs(layer.stateMachine, activeStateMachine)) { layerName = layer.name; break; }
            }

            string controllerName = controller.name;
            if (_controller == controller && _controllerName == controllerName && _layerName == layerName && _activeStateMachine == activeStateMachine) return;

            _controller = controller;
            _activeStateMachine = activeStateMachine;
            _controllerName = controllerName;
            _layerName = layerName;
            _subContextPath = BuildSubSMPath(controller, layerName, activeStateMachine);
            Repaint();
        }

        AnimatorController TryGetControllerFromBlendTreeGraph()
        {
            if (AnimatorEditorInit.BlendTreeGraphGUIType == null) return null;

            if (_cachedBlendTreeGraphGUI != null)
            {
                var controller = ControllerFromBlendTreeGraphGUI(_cachedBlendTreeGraphGUI);
                if (controller != null) return controller;
                _cachedBlendTreeGraphGUI = null;
            }

            foreach (var graphGUI in Resources.FindObjectsOfTypeAll(AnimatorEditorInit.BlendTreeGraphGUIType))
            {
                var controller = ControllerFromBlendTreeGraphGUI(graphGUI);
                if (controller != null) { _cachedBlendTreeGraphGUI = graphGUI; return controller; }
            }

            return null;
        }

        static AnimatorController ControllerFromBlendTreeGraphGUI(UnityEngine.Object graphGUI)
        {
            var graph = Traverse.Create(graphGUI).Property("graph").GetValue();
            if (graph == null) return null;
            var rootBlendTree = Traverse.Create(graph).Property("rootBlendTree").GetValue() as BlendTree;
            if (rootBlendTree == null) return null;
            var assetPath = AssetDatabase.GetAssetPath(rootBlendTree);
            if (string.IsNullOrEmpty(assetPath)) return null;
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
        }

        /* Returns true if sm is target or recursively contains target as a nested sub state machine. */
        static bool SMContainsOrIs(AnimatorStateMachine sm, AnimatorStateMachine target)
        {
            if (sm == target) return true;
            foreach (var childStateMachine in sm.stateMachines)
                if (SMContainsOrIs(childStateMachine.stateMachine, target)) return true;
            return false;
        }

        BlendTree TryGetRootBlendTree()
        {
            if (_cachedBlendTreeGraphGUI == null) return null;
            var graph = Traverse.Create(_cachedBlendTreeGraphGUI).Property("graph").GetValue();
            if (graph == null) return null;
            return Traverse.Create(graph).Property("rootBlendTree").GetValue() as BlendTree;
        }

        static string FindLayerForRootBlendTree(AnimatorController controller, BlendTree rootBlendTree)
        {
            foreach (var layer in controller.layers)
                if (SMContainsMotion(layer.stateMachine, rootBlendTree)) return layer.name;
            return "—";
        }

        static bool SMContainsMotion(AnimatorStateMachine sm, Motion target)
        {
            foreach (var childState in sm.states)
                if (childState.state.motion == target) return true;
            foreach (var childStateMachine in sm.stateMachines)
                if (SMContainsMotion(childStateMachine.stateMachine, target)) return true;
            return false;
        }

        static string[] BuildSubSMPath(AnimatorController controller, string layerName, AnimatorStateMachine target)
        {
            if (controller == null || target == null || layerName == "—") return null;
            var layer = System.Array.Find(controller.layers, l => l.name == layerName);
            if (layer == null || layer.stateMachine == target) return null;
            var pathSegments = new System.Collections.Generic.List<string>();
            if (FindSMPath(layer.stateMachine, target, pathSegments)) return pathSegments.ToArray();
            return null;
        }

        static bool FindSMPath(AnimatorStateMachine current, AnimatorStateMachine target, System.Collections.Generic.List<string> pathSegments)
        {
            foreach (var childStateMachine in current.stateMachines)
            {
                if (childStateMachine.stateMachine == target)
                {
                    pathSegments.Add(target.name);
                    return true;
                }
                if (FindSMPath(childStateMachine.stateMachine, target, pathSegments))
                {
                    pathSegments.Insert(0, childStateMachine.stateMachine.name);
                    return true;
                }
            }
            return false;
        }

        void OnGUI()
        {
            if (!_paletteApplied)
            {
                _paletteApplied = true;
                var settings = AnimatorDefaultSettings.Load();
                Styles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
            }
            if (Event.current.type == EventType.MouseMove)
                Repaint();
            DrawTabs();
            DrawLayerBar();
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUIStyle.none, GUI.skin.verticalScrollbar);
            _scrollPosition.x = 0;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();
            if (_tabOpen[0]) { DrawSectionHeader("Transitions", _selectedTransitions.Length > 0 ? $"{_selectedTransitions.Length} selected" : null); DrawTransitionsTab(); EditorGUILayout.Space(10); }
            if (_tabOpen[1]) { DrawSectionHeader("States", _selectedStates.Length > 0 ? $"{_selectedStates.Length} selected" : null); DrawStatesTab(); EditorGUILayout.Space(10); }
            if (_tabOpen[2]) { DrawSectionHeader("Controller", ControllerSectionCountLabel);  DrawControllerTab();  EditorGUILayout.Space(10); }
            if (_tabOpen[3]) { DrawSectionHeader("Settings");    DrawSettingsTab();    EditorGUILayout.Space(10); }
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
            DrawFooter();
            EditorGUILayout.EndScrollView();
        }

        void DrawTabs()
        {
            using var _ = new EditorGUILayout.HorizontalScope(GUIStyle.none, GUILayout.Height(24), GUILayout.ExpandWidth(true));
            for (int i = 0; i < _tabs.Length; i++)
            {
                var style = _tabOpen[i] ? Styles.TabActive : Styles.TabInactive;
                _tabOpen[i] = GUILayout.Toggle(_tabOpen[i], _tabs[i], style, GUILayout.ExpandWidth(true), GUILayout.Height(24));
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }
        }

        void DrawLayerBar()
        {
            var barRect = EditorGUILayout.GetControlRect(false, 28f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(new Rect(0, barRect.y, EditorGUIUtility.currentViewWidth, barRect.height), Styles.SectionHeaderBg);

            bool hasLayer      = _layerName != "—";
            bool hasSubContext = _subContextPath != null && _subContextPath.Length > 0;

            float x = barRect.x + 8f;
            DrawBreadcrumbSegment(ref x, barRect, _controllerName, isLeaf: !hasLayer && !hasSubContext);

            if (hasLayer)
            {
                DrawBreadcrumbSeparator(ref x, barRect);
                DrawBreadcrumbSegment(ref x, barRect, _layerName, isLeaf: !hasSubContext);
            }

            if (hasSubContext)
            {
                for (int i = 0; i < _subContextPath.Length; i++)
                {
                    DrawBreadcrumbSeparator(ref x, barRect);
                    DrawBreadcrumbSegment(ref x, barRect, _subContextPath[i], isLeaf: i == _subContextPath.Length - 1);
                }
            }
        }

        static readonly GUIContent s_breadcrumbSeparatorContent = new(" > ");
        static readonly GUIContent s_breadcrumbSegmentContent  = new();

        static void DrawBreadcrumbSegment(ref float x, Rect barRect, string text, bool isLeaf)
        {
            var style = isLeaf ? Styles.BreadcrumbLeaf : Styles.BreadcrumbParent;
            s_breadcrumbSegmentContent.text = text;
            float width = style.CalcSize(s_breadcrumbSegmentContent).x;
            GUI.Label(new Rect(x, barRect.y, width, barRect.height), text, style);
            x += width;
        }

        static void DrawBreadcrumbSeparator(ref float x, Rect barRect)
        {
            float width = Styles.BreadcrumbParent.CalcSize(s_breadcrumbSeparatorContent).x;
            GUI.Label(new Rect(x, barRect.y, width, barRect.height), s_breadcrumbSeparatorContent, Styles.BreadcrumbParent);
            x += width;
        }

        /* GUILayout.Button that shows the finger-pointer cursor on hover. */
        static bool CursorBtn(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            bool clicked = GUILayout.Button(text, style, options);
            EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            return clicked;
        }

        /* GUI.Button at rect with a finger-pointer cursor (string label overload). */
        static bool CursorBtn(Rect rect, string text, GUIStyle style)
        {
            bool clicked = GUI.Button(rect, text, style);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return clicked;
        }

        /* GUI.Button at rect with a finger-pointer cursor (GUIContent overload for tooltip support). */
        static bool CursorBtn(Rect rect, GUIContent content, GUIStyle style)
        {
            bool clicked = GUI.Button(rect, content, style);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return clicked;
        }

        /* Draws a full-width dark header bar containing label, spanning edge-to-edge regardless of scroll indent. */
        static void DrawSectionHeader(string label, string rightLabel = null)
        {
            var rect = EditorGUILayout.GetControlRect(false, 28f, GUILayout.ExpandWidth(true));
            var backgroundRect = new Rect(0, rect.y - EditorGUIUtility.standardVerticalSpacing, EditorGUIUtility.currentViewWidth, rect.height + EditorGUIUtility.standardVerticalSpacing);
            EditorGUI.DrawRect(backgroundRect, Styles.SectionHeaderBg);
            GUI.Label(rect, label, Styles.TabSectionLabel);
            if (rightLabel != null)
                GUI.Label(rect, rightLabel, Styles.SectionHeaderCount);
        }

        static string _cachedVersion;
        static string GetVersion()
        {
            if (_cachedVersion != null) return _cachedVersion;
            _cachedVersion = "V" + (UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AnimationEditorWindow).Assembly)?.version ?? "?");
            return _cachedVersion;
        }

        static void DrawFooter()
        {
            var rect = EditorGUILayout.GetControlRect(false, 18f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, Styles.SectionHeaderBg);
            GUI.Label(rect, "Created by YerGodDamnRight", Styles.FooterLabel);
            GUI.Label(rect, GetVersion(), Styles.FooterVersion);
        }

        static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.4f));
        }
    }
}
#endif
