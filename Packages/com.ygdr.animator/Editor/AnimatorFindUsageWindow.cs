#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal class AnimatorFindUsageWindow : EditorWindow
    {
        struct UsageRow
        {
            internal string sourceName;
            internal string destinationName;
            internal string conditionLabel;
            internal AnimatorStateTransition transition;
        }

        AnimatorController _controller;
        string _parameterName;
        AnimatorControllerParameterType _parameterType;
        string _relativePath;
        string _gameObjectName;
        string _controllerPath;
        List<UsageRow> _rows = new();
        HashSet<int> _knownTransitionIds = new();
        List<AnimatorState> _clipStates = new();
        List<AnimationClip> _clipAssets = new();
        Vector2 _scrollPosition;


        static GUIStyle s_rowLabelStyle;
        static GUIStyle RowLabelStyle => s_rowLabelStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize  = 11,
            padding   = new RectOffset(4, 4, 0, 0)
        };

        static GUIStyle s_clickableRowStyle;
        static GUIStyle ClickableRowStyle
        {
            get
            {
                if (s_clickableRowStyle != null) return s_clickableRowStyle;
                var hoverTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                hoverTex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.07f));
                hoverTex.Apply();
                s_clickableRowStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize  = 11,
                    padding   = new RectOffset(4, 4, 0, 0),
                    hover     = { background = hoverTex, textColor = Color.white }
                };
                return s_clickableRowStyle;
            }
        }

        internal static void Open(AnimatorControllerParameter parameter, AnimatorController controller)
        {
            var window = GetWindow<AnimatorFindUsageWindow>("Find Uses");
            window.minSize = new Vector2(480, 280);
            window._controller = controller;
            window._parameterName = parameter.name;
            window._parameterType = parameter.type;
            window._relativePath = null;
            window._controllerPath = controller != null ? AssetDatabase.GetAssetPath(controller) : null;
            window.RebuildCache();
            window.Show();
        }

        internal static void Open(string relativePath, AnimatorController controller, string gameObjectName)
        {
            var window = GetWindow<AnimatorFindUsageWindow>("Find Uses");
            window.minSize = new Vector2(480, 280);
            window._controller = controller;
            window._relativePath = relativePath;
            window._gameObjectName = gameObjectName;
            window._parameterName = null;
            window._controllerPath = controller != null ? AssetDatabase.GetAssetPath(controller) : null;
            window.RebuildCache();
            window.Show();
        }

        void OnEnable()
        {
            wantsMouseMove = true;
            ObjectChangeEvents.changesPublished += OnAssetChangesPublished;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnAssetChangesPublished;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        void OnUndoRedo()
        {
            RebuildCache();
            Repaint();
        }

        void OnAssetChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (_controller == null || _controllerPath == null) return;

            for (int i = 0; i < stream.length; i++)
            {
                bool relevant = false;
                var kind = stream.GetEventType(i);

                if (kind == ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    stream.GetChangeAssetObjectPropertiesEvent(i, out var args);
                    var changedObj = EditorUtility.InstanceIDToObject(args.instanceId);
                    relevant = changedObj != null && AssetDatabase.GetAssetPath(changedObj) == _controllerPath;
                }
                else if (kind == ObjectChangeKind.CreateAssetObject)
                {
                    stream.GetCreateAssetObjectEvent(i, out var args);
                    var createdObj = EditorUtility.InstanceIDToObject(args.instanceId);
                    relevant = createdObj != null && AssetDatabase.GetAssetPath(createdObj) == _controllerPath;
                }
                else if (kind == ObjectChangeKind.DestroyAssetObject)
                {
                    stream.GetDestroyAssetObjectEvent(i, out var args);
                    relevant = _knownTransitionIds.Contains(args.instanceId);
                }

                if (!relevant) continue;
                RebuildCache();
                Repaint();
                return;
            }
        }

        void RebuildCache()
        {
            _rows.Clear();
            _knownTransitionIds.Clear();
            _clipStates.Clear();
            _clipAssets.Clear();
            if (_controller == null) return;

            if (_parameterName != null)
            {
                foreach (var layer in _controller.layers)
                    SearchSMForParameter(layer.stateMachine);
            }
            else
            {
                if (_relativePath == null) return;
                var seenStateIds = new HashSet<int>();
                var seenClipIds  = new HashSet<int>();
                foreach (var layer in _controller.layers)
                    SearchSMForClips(layer.stateMachine, seenStateIds, seenClipIds);
            }
        }

        // ── Parameter search ──────────────────────────────────────────────────

        void SearchSMForParameter(AnimatorStateMachine sm)
        {
            foreach (var anyStateTransition in sm.anyStateTransitions)
            {
                _knownTransitionIds.Add(anyStateTransition.GetInstanceID());
                CheckTransition(anyStateTransition, "Any State", ResolveDestinationName(anyStateTransition));
            }

            foreach (var childState in sm.states)
                foreach (var stateTransition in childState.state.transitions)
                {
                    _knownTransitionIds.Add(stateTransition.GetInstanceID());
                    CheckTransition(stateTransition, childState.state.name, ResolveDestinationName(stateTransition));
                }

            foreach (var childStateMachine in sm.stateMachines)
                SearchSMForParameter(childStateMachine.stateMachine);
        }

        void CheckTransition(AnimatorStateTransition transition, string sourceName, string destinationName)
        {
            foreach (var condition in transition.conditions)
            {
                if (condition.parameter != _parameterName) continue;
                _rows.Add(new UsageRow
                {
                    sourceName      = sourceName,
                    destinationName = destinationName,
                    conditionLabel  = FormatCondition(condition),
                    transition      = transition
                });
            }
        }

        string ResolveDestinationName(AnimatorStateTransition transition)
        {
            if (transition.isExit) return "Exit";
            if (transition.destinationState != null) return transition.destinationState.name;
            if (transition.destinationStateMachine != null) return transition.destinationStateMachine.name;
            return "?";
        }

        string FormatCondition(AnimatorCondition condition)
        {
            return _parameterType switch
            {
                AnimatorControllerParameterType.Bool    => condition.mode == AnimatorConditionMode.If ? "True" : "False",
                AnimatorControllerParameterType.Trigger => "",
                AnimatorControllerParameterType.Float   => $"{condition.mode} {condition.threshold:0.###}",
                AnimatorControllerParameterType.Int     => $"{condition.mode} {(int)condition.threshold}",
                _                                       => condition.mode.ToString()
            };
        }

        // ── Clip search ───────────────────────────────────────────────────────

        void SearchSMForClips(AnimatorStateMachine sm, HashSet<int> seenStateIds, HashSet<int> seenClipIds)
        {
            foreach (var childState in sm.states)
                CheckStateForClips(childState.state, seenStateIds, seenClipIds);
            foreach (var childStateMachine in sm.stateMachines)
                SearchSMForClips(childStateMachine.stateMachine, seenStateIds, seenClipIds);
        }

        void CheckStateForClips(AnimatorState state, HashSet<int> seenStateIds, HashSet<int> seenClipIds)
        {
            foreach (var clip in CollectClips(state.motion))
            {
                if (!ClipContainsPath(clip)) continue;
                if (seenStateIds.Add(state.GetInstanceID()))
                    _clipStates.Add(state);
                if (seenClipIds.Add(clip.GetInstanceID()))
                    _clipAssets.Add(clip);
            }
        }

        static IEnumerable<AnimationClip> CollectClips(Motion motion)
        {
            if (motion is AnimationClip clip)
            {
                yield return clip;
            }
            else if (motion is BlendTree blendTree)
            {
                foreach (var child in blendTree.children)
                    foreach (var childClip in CollectClips(child.motion))
                        yield return childClip;
            }
        }

        bool ClipContainsPath(AnimationClip clip)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.path == _relativePath) return true;
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                if (binding.path == _relativePath) return true;
            return false;
        }

        // ── GUI ───────────────────────────────────────────────────────────────

        void OnGUI()
        {
            DrawHeader();
            DrawColumns();
        }

        void DrawHeader()
        {
            var headerRect = EditorGUILayout.GetControlRect(false, 28f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                var fullWidthRect = headerRect;
                fullWidthRect.x = 0;
                fullWidthRect.width = EditorGUIUtility.currentViewWidth;
                EditorGUI.DrawRect(fullWidthRect, AnimationEditorWindow.Styles.SectionHeaderBg);
            }

            var settings = AnimatorDefaultSettings.Load();
            string typeHex = ColorUtility.ToHtmlStringRGB(_parameterType switch
            {
                AnimatorControllerParameterType.Float   => settings.paramColorFloat,
                AnimatorControllerParameterType.Int     => settings.paramColorInt,
                AnimatorControllerParameterType.Bool    => settings.paramColorBool,
                AnimatorControllerParameterType.Trigger => settings.paramColorTrigger,
                _                                       => new Color(0.65f, 0.65f, 0.65f)
            });
            string name = _parameterName != null
                ? $"{_parameterName}  <color=#{typeHex}>{_parameterType}</color>"
                : _gameObjectName ?? "";
            string counts = _parameterName != null
                ? $"{_rows.Count} transition{(_rows.Count != 1 ? "s" : "")}"
                : $"{_clipStates.Count} node{(_clipStates.Count != 1 ? "s" : "")}  ·  {_clipAssets.Count} clip{(_clipAssets.Count != 1 ? "s" : "")}";
            GUI.Label(headerRect, $"{name}  —  {counts}", AnimationEditorWindow.Styles.FindUsesHeader);
        }

        void DrawColumns()
        {
            const float middleGap        = 8f;
            const float columnHeaderHeight = 24f;
            const float rowPad           = 2f;
            float rowHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            int   displayRows = _parameterName != null
                ? Mathf.Max(_rows.Count, 1)
                : Mathf.Max(_clipStates.Count, _clipAssets.Count, 1);
            float totalHeight = columnHeaderHeight + rowPad + displayRows * rowHeight;

            GUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8f);
            var outerRect = EditorGUILayout.BeginVertical(AnimationEditorWindow.Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint && outerRect.height > 0)
                EditorGUI.DrawRect(outerRect, AnimationEditorWindow.Styles.PrimaryColor);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            var rect = EditorGUILayout.GetControlRect(false, totalHeight);
            float halfWidth = (rect.width - middleGap) / 2f;

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(rect.x,                         rect.y, halfWidth, rect.height), AnimationEditorWindow.Styles.SecondaryColor);
                EditorGUI.DrawRect(new Rect(rect.x + halfWidth + middleGap, rect.y, halfWidth, rect.height), AnimationEditorWindow.Styles.SecondaryColor);
                EditorGUI.DrawRect(new Rect(rect.x,                         rect.y, halfWidth, columnHeaderHeight), AnimationEditorWindow.Styles.AccentColor);
                EditorGUI.DrawRect(new Rect(rect.x + halfWidth + middleGap, rect.y, halfWidth, columnHeaderHeight), AnimationEditorWindow.Styles.AccentColor);
            }

            string leftHeader  = _parameterName != null ? "Transition" : "State Node";
            string rightHeader = _parameterName != null ? "Condition"  : "Animation Clip";
            GUI.Label(new Rect(rect.x,                         rect.y, halfWidth, columnHeaderHeight), leftHeader,  AnimationEditorWindow.Styles.FindUsesHeader);
            GUI.Label(new Rect(rect.x + halfWidth + middleGap, rect.y, halfWidth, columnHeaderHeight), rightHeader, AnimationEditorWindow.Styles.FindUsesHeader);

            float rowY = rect.y + columnHeaderHeight + rowPad;
            bool isEmpty = _parameterName != null ? _rows.Count == 0 : _clipStates.Count == 0 && _clipAssets.Count == 0;

            if (isEmpty)
            {
                string emptyMessage = _parameterName != null
                    ? "No transitions use this parameter."
                    : "No clips reference this object.";
                GUI.Label(new Rect(rect.x, rowY, halfWidth, rowHeight), emptyMessage, AnimationEditorWindow.Styles.EmptyLabel);
            }
            else if (_parameterName != null)
            {
                DrawParameterRows(rect.x, halfWidth, middleGap, rowY, rowHeight);
            }
            else
            {
                DrawClipRows(rect.x, halfWidth, middleGap, rowY, rowHeight);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            GUILayout.Space(8f);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10);
        }

        void DrawParameterRows(float x, float halfWidth, float middleGap, float startY, float rowHeight)
        {
            float rowY = startY;
            for (int i = 0; i < _rows.Count; i++, rowY += rowHeight)
            {
                var row = _rows[i];
                if (Event.current.type == EventType.Repaint && i % 2 == 1)
                {
                    EditorGUI.DrawRect(new Rect(x,                         rowY, halfWidth, rowHeight), AnimationEditorWindow.Styles.RowAltColor);
                    EditorGUI.DrawRect(new Rect(x + halfWidth + middleGap, rowY, halfWidth, rowHeight), AnimationEditorWindow.Styles.RowAltColor);
                }

                var leftRect  = new Rect(x,                         rowY, halfWidth, rowHeight);
                var rightRect = new Rect(x + halfWidth + middleGap, rowY, halfWidth, rowHeight);

                if (GUI.Button(leftRect, $"{row.sourceName}  →  {row.destinationName}", ClickableRowStyle))
                    AnimationEditorWindow.FocusTransition(row.transition, _controller);
                EditorGUIUtility.AddCursorRect(leftRect, MouseCursor.Link);

                GUI.Label(rightRect, row.conditionLabel ?? "", RowLabelStyle);
            }
        }

        void DrawClipRows(float x, float halfWidth, float middleGap, float startY, float rowHeight)
        {
            int maxRows = Mathf.Max(_clipStates.Count, _clipAssets.Count);
            float rowY = startY;
            for (int i = 0; i < maxRows; i++, rowY += rowHeight)
            {
                bool hasState = i < _clipStates.Count;
                bool hasClip  = i < _clipAssets.Count;

                if (Event.current.type == EventType.Repaint && i % 2 == 1)
                {
                    if (hasState) EditorGUI.DrawRect(new Rect(x,                         rowY, halfWidth, rowHeight), AnimationEditorWindow.Styles.RowAltColor);
                    if (hasClip)  EditorGUI.DrawRect(new Rect(x + halfWidth + middleGap, rowY, halfWidth, rowHeight), AnimationEditorWindow.Styles.RowAltColor);
                }

                var leftRect  = new Rect(x,                         rowY, halfWidth, rowHeight);
                var rightRect = new Rect(x + halfWidth + middleGap, rowY, halfWidth, rowHeight);

                if (hasState)
                {
                    if (GUI.Button(leftRect, _clipStates[i].name, ClickableRowStyle))
                        AnimationEditorWindow.FocusAsset(_clipStates[i], _controller);
                    EditorGUIUtility.AddCursorRect(leftRect, MouseCursor.Link);
                }

                if (hasClip)
                {
                    if (GUI.Button(rightRect, _clipAssets[i].name, ClickableRowStyle))
                    {
                        Selection.activeObject = _clipAssets[i];
                        EditorGUIUtility.PingObject(_clipAssets[i]);
                    }
                    EditorGUIUtility.AddCursorRect(rightRect, MouseCursor.Link);
                }
            }
        }
    }
}
#endif
