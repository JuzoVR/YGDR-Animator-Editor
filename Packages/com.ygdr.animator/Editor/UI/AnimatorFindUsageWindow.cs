#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

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
        bool _aapMode;
        bool _effectingObjectsMode;
        List<GameObject> _effectingObjects = new();
        string _effectingComponentTypeName = "";
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

        internal static void OpenAap(AnimatorControllerParameter parameter, AnimatorController controller)
        {
            var window = GetWindow<AnimatorFindUsageWindow>("Find Uses");
            window.minSize = new Vector2(480, 280);
            window._controller = controller;
            window._parameterName = parameter.name;
            window._parameterType = parameter.type;
            window._relativePath = null;
            window._gameObjectName = null;
            window._aapMode = true;
            window._effectingObjectsMode = false;
            window._controllerPath = controller != null ? AssetDatabase.GetAssetPath(controller) : null;
            window.RebuildCache();
            window.Show();
        }

        internal static void OpenEffectingObjects(AnimatorControllerParameter parameter, AnimatorController controller)
        {
            var window = GetWindow<AnimatorFindUsageWindow>("Find Uses");
            window.minSize = new Vector2(480, 280);
            window._controller = controller;
            window._parameterName = parameter.name;
            window._parameterType = parameter.type;
            window._relativePath = null;
            window._gameObjectName = null;
            window._aapMode = false;
            window._effectingObjectsMode = true;
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
            _effectingObjects.Clear();
            _effectingComponentTypeName = "";

            if (_effectingObjectsMode)
            {
                SearchSceneForEffectingObjects();
                return;
            }

            if (_controller == null) return;

            if (_parameterName != null && !_aapMode)
            {
                foreach (var layer in _controller.layers)
                    SearchSMForParameter(layer.stateMachine);
            }
            else if (_aapMode)
            {
                var seenStateIds = new HashSet<int>();
                var seenClipIds  = new HashSet<int>();
                foreach (var layer in _controller.layers)
                    SearchSMForAapClips(layer.stateMachine, seenStateIds, seenClipIds);
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

        // ── AAP search ────────────────────────────────────────────────────────

        void SearchSMForAapClips(AnimatorStateMachine sm, HashSet<int> seenStateIds, HashSet<int> seenClipIds)
        {
            foreach (var childState in sm.states)
                CheckStateForAapClips(childState.state, seenStateIds, seenClipIds);
            foreach (var childStateMachine in sm.stateMachines)
                SearchSMForAapClips(childStateMachine.stateMachine, seenStateIds, seenClipIds);
        }

        void CheckStateForAapClips(AnimatorState state, HashSet<int> seenStateIds, HashSet<int> seenClipIds)
        {
            foreach (var clip in CollectClips(state.motion))
            {
                if (!ClipDrivesAapParam(clip)) continue;
                if (seenStateIds.Add(state.GetInstanceID()))
                    _clipStates.Add(state);
                if (seenClipIds.Add(clip.GetInstanceID()))
                    _clipAssets.Add(clip);
            }
        }

        bool ClipDrivesAapParam(AnimationClip clip)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.type == typeof(UnityEngine.Animator) && binding.propertyName == _parameterName)
                    return true;
            return false;
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
            string counts = _effectingObjectsMode
                ? $"{_effectingObjects.Count} object{(_effectingObjects.Count != 1 ? "s" : "")}"
                : (_parameterName != null && !_aapMode)
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

            int   displayRows = _effectingObjectsMode
                ? Mathf.Max(_effectingObjects.Count, 1)
                : (_parameterName != null && !_aapMode)
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

            string leftHeader  = _effectingObjectsMode ? _effectingComponentTypeName : (_parameterName != null && !_aapMode) ? "Transition" : "State Node";
            string rightHeader = _effectingObjectsMode ? "Effecting Object" : (_parameterName != null && !_aapMode) ? "Condition" : "Animation Clip";
            GUI.Label(new Rect(rect.x,                         rect.y, halfWidth, columnHeaderHeight), leftHeader,  AnimationEditorWindow.Styles.FindUsesHeader);
            GUI.Label(new Rect(rect.x + halfWidth + middleGap, rect.y, halfWidth, columnHeaderHeight), rightHeader, AnimationEditorWindow.Styles.FindUsesHeader);

            float rowY = rect.y + columnHeaderHeight + rowPad;
            bool isEmpty = _effectingObjectsMode ? _effectingObjects.Count == 0 : (_parameterName != null && !_aapMode) ? _rows.Count == 0 : _clipStates.Count == 0 && _clipAssets.Count == 0;

            if (isEmpty)
            {
                string emptyMessage = _effectingObjectsMode
                    ? "No effecting objects found in scene."
                    : (_parameterName != null && !_aapMode)
                        ? "No transitions use this parameter."
                        : _aapMode ? "No clips animate this parameter as AAP." : "No clips reference this object.";
                GUI.Label(new Rect(rect.x, rowY, halfWidth, rowHeight), emptyMessage, AnimationEditorWindow.Styles.EmptyLabel);
            }
            else if (_effectingObjectsMode)
            {
                DrawEffectingObjectRows(rect.x, halfWidth, middleGap, rowY, rowHeight);
            }
            else if (_parameterName != null && !_aapMode)
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

        // ── Effecting objects search ──────────────────────────────────────────

        internal static readonly string[] PhysBoneSuffixes =
        {
            "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish", "_Velocity", "_IsAnimated"
        };

        internal static readonly string[] RaycastSuffixes = { "_Hit", "_Ratio", "_Distance" };

        internal static bool MatchesSuffixList(string componentBase, string animatorParam, string[] suffixes)
        {
            foreach (var suffix in suffixes)
                if (animatorParam.Length == componentBase.Length + suffix.Length
                    && animatorParam.StartsWith(componentBase, System.StringComparison.Ordinal)
                    && animatorParam.EndsWith(suffix, System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        void SearchSceneForEffectingObjects()
        {
            var seenIds = new HashSet<int>();

#pragma warning disable CS0618
            foreach (var receiver in Object.FindObjectsOfType<ContactReceiver>(true))
            {
                if (receiver.parameter != _parameterName) continue;
                if (seenIds.Add(receiver.gameObject.GetInstanceID()))
                {
                    _effectingObjects.Add(receiver.gameObject);
                    _effectingComponentTypeName = "Contact";
                }
            }

            foreach (var physBone in Object.FindObjectsOfType<VRCPhysBone>(true))
            {
                if (string.IsNullOrEmpty(physBone.parameter)) continue;
                if (!MatchesSuffixList(physBone.parameter, _parameterName, PhysBoneSuffixes)) continue;
                if (seenIds.Add(physBone.gameObject.GetInstanceID()))
                {
                    _effectingObjects.Add(physBone.gameObject);
                    _effectingComponentTypeName = "PhysBone";
                }
            }

            foreach (var raycast in Object.FindObjectsOfType<VRCRaycast>(true))
            {
                var serializedRaycast = new SerializedObject(raycast);
                var parameterProperty = serializedRaycast.FindProperty("parameter");
                if (parameterProperty == null || string.IsNullOrEmpty(parameterProperty.stringValue)) continue;
                if (!MatchesSuffixList(parameterProperty.stringValue, _parameterName, RaycastSuffixes)) continue;
                if (seenIds.Add(raycast.gameObject.GetInstanceID()))
                {
                    _effectingObjects.Add(raycast.gameObject);
                    _effectingComponentTypeName = "Raycast";
                }
            }
#pragma warning restore CS0618
        }

        internal static void RemapVrcComponentParameters(string oldName, string newName)
        {
#pragma warning disable CS0618
            foreach (var receiver in Object.FindObjectsOfType<ContactReceiver>(true))
            {
                if (receiver.parameter != oldName) continue;
                Undo.RegisterCompleteObjectUndo(receiver, "Rename Parameter");
                receiver.parameter = newName;
                EditorUtility.SetDirty(receiver);
            }

            foreach (var physBone in Object.FindObjectsOfType<VRCPhysBone>(true))
            {
                if (string.IsNullOrEmpty(physBone.parameter)) continue;
                foreach (var suffix in PhysBoneSuffixes)
                {
                    if (oldName != physBone.parameter + suffix) continue;
                    if (!newName.EndsWith(suffix, System.StringComparison.Ordinal)) break;
                    string newBase = newName.Substring(0, newName.Length - suffix.Length);
                    Undo.RegisterCompleteObjectUndo(physBone, "Rename Parameter");
                    physBone.parameter = newBase;
                    EditorUtility.SetDirty(physBone);
                    break;
                }
            }

            foreach (var raycast in Object.FindObjectsOfType<VRCRaycast>(true))
            {
                var serializedRaycast = new SerializedObject(raycast);
                var parameterProperty = serializedRaycast.FindProperty("parameter");
                if (parameterProperty == null || string.IsNullOrEmpty(parameterProperty.stringValue)) continue;
                foreach (var suffix in RaycastSuffixes)
                {
                    if (oldName != parameterProperty.stringValue + suffix) continue;
                    if (!newName.EndsWith(suffix, System.StringComparison.Ordinal)) break;
                    string newBase = newName.Substring(0, newName.Length - suffix.Length);
                    parameterProperty.stringValue = newBase;
                    serializedRaycast.ApplyModifiedProperties();
                    break;
                }
            }
#pragma warning restore CS0618
        }

        internal static HashSet<string> BuildAllEffectingParamNames()
        {
            var result = new HashSet<string>();
#pragma warning disable CS0618
            foreach (var receiver in Object.FindObjectsOfType<ContactReceiver>(true))
                if (!string.IsNullOrEmpty(receiver.parameter))
                    result.Add(receiver.parameter);

            foreach (var physBone in Object.FindObjectsOfType<VRCPhysBone>(true))
            {
                if (string.IsNullOrEmpty(physBone.parameter)) continue;
                foreach (var suffix in PhysBoneSuffixes)
                    result.Add(physBone.parameter + suffix);
            }

            foreach (var raycast in Object.FindObjectsOfType<VRCRaycast>(true))
            {
                var serializedRaycast = new SerializedObject(raycast);
                var parameterProperty = serializedRaycast.FindProperty("parameter");
                if (parameterProperty == null || string.IsNullOrEmpty(parameterProperty.stringValue)) continue;
                foreach (var suffix in RaycastSuffixes)
                    result.Add(parameterProperty.stringValue + suffix);
            }
#pragma warning restore CS0618
            return result;
        }

        void DrawEffectingObjectRows(float x, float halfWidth, float middleGap, float startY, float rowHeight)
        {
            float rowY = startY;
            for (int i = 0; i < _effectingObjects.Count; i++, rowY += rowHeight)
            {
                var go = _effectingObjects[i];
                if (go == null) continue;

                if (Event.current.type == EventType.Repaint && i % 2 == 1)
                    EditorGUI.DrawRect(new Rect(x + halfWidth + middleGap, rowY, halfWidth, rowHeight), AnimationEditorWindow.Styles.RowAltColor);

                var rightRect = new Rect(x + halfWidth + middleGap, rowY, halfWidth, rowHeight);
                if (GUI.Button(rightRect, go.name, ClickableRowStyle))
                {
                    Selection.activeGameObject = go;
                    EditorGUIUtility.PingObject(go);
                }
                EditorGUIUtility.AddCursorRect(rightRect, MouseCursor.Link);
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
