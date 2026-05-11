#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        enum WDState { On, Off, Mixed }


        int _controllerSubTab;

        string ControllerSectionCountLabel
        {
            get
            {
                if (_controller == null) return null;
                if (_controllerSubTab == 0) return $"{_controller.layers.Length} layers";
                if (_controllerSubTab == 2 && _subAssetsByType != null)
                    return _subAssetTypeFilter switch
                    {
                        0 => $"{_subAssetsByType[0]?.Length ?? 0} State Machines",
                        1 => $"{_subAssetsByType[1]?.Length ?? 0} States",
                        2 => $"{_subAssetsByType[2]?.Length ?? 0} Blend Trees",
                        _ => null
                    };
                return null;
            }
        }

        void DrawControllerTab()
        {
            var panelRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint && panelRect.height > 0)
                EditorGUI.DrawRect(panelRect, Styles.PrimaryColor);
            DrawControllerSubTabs();
            EditorGUILayout.Space(8);
            if (_controllerSubTab == 0)      DrawWriteDefaultsSection();
            else if (_controllerSubTab == 1) DrawNetworkSyncSection();
            else                             DrawSubAssetsSection();
            EditorGUILayout.EndVertical();
        }

        void DrawControllerSubTabs()
        {
            var rowRect      = EditorGUILayout.GetControlRect(false, 24f);
            float tabsWidth  = rowRect.width / 2f;
            float tabWidth   = tabsWidth / 3f;
            float cleanWidth = rowRect.width / 4f;

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, tabsWidth, rowRect.height), Styles.PrimaryColor);

            string[] labels = { "Write Defaults", "Network Sync", "Sub-Assets" };
            for (int i = 0; i < labels.Length; i++)
            {
                var tabRect = new Rect(rowRect.x + i * tabWidth, rowRect.y, tabWidth, 24f);
                var style   = _controllerSubTab == i ? Styles.ControllerSubTabBtnActive : Styles.ControllerSubTabBtn;
                if (GUI.Toggle(tabRect, _controllerSubTab == i, labels[i], style))
                    _controllerSubTab = i;
                EditorGUIUtility.AddCursorRect(tabRect, MouseCursor.Link);
            }

            int orphanCount  = _orphanedAssets?.Length ?? 0;
            var cleanBtnRect = new Rect(rowRect.xMax - cleanWidth, rowRect.y, cleanWidth, 24f);
            if (CursorBtn(cleanBtnRect, $"Clean ({orphanCount})", Styles.ControllerSubTabBtn) && orphanCount > 0)
                CleanOrphanedAssets();
        }

        // ── Write Defaults ────────────────────────────────────────────────────

        void DrawWriteDefaultsSection()
        {
            if (_controller == null)
            {
                EditorGUILayout.LabelField("No controller selected", Styles.EmptyLabel);
                return;
            }

            var layers      = _controller.layers;
            var onLayers    = layers.Where(layer => GetLayerWDState(layer) == WDState.On).ToArray();
            var offLayers   = layers.Where(layer => GetLayerWDState(layer) == WDState.Off).ToArray();
            var mixedLayers = layers.Where(layer => GetLayerWDState(layer) == WDState.Mixed).ToArray();

            const float middleGap = 8f;

            var btnRowRect  = EditorGUILayout.GetControlRect(false, 24f);
            float halfWidth = (btnRowRect.width - middleGap) / 2f;

            if (CursorBtn(new Rect(btnRowRect.x,                         btnRowRect.y, halfWidth, 24f), "Set All On",  Styles.IconBtn))
                SetAllLayersWD(true);
            if (CursorBtn(new Rect(btnRowRect.x + halfWidth + middleGap, btnRowRect.y, halfWidth, 24f), "Set All Off", Styles.IconBtn))
                SetAllLayersWD(false);

            float lineHeight  = EditorGUIUtility.singleLineHeight;
            float rowHeight   = lineHeight + EditorGUIUtility.standardVerticalSpacing;
            int   maxRows     = Mathf.Max(onLayers.Length, offLayers.Length);
            float totalHeight = 28f + Mathf.Max(maxRows, 1) * rowHeight;

            var rect = EditorGUILayout.GetControlRect(false, totalHeight);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(rect.x,                         rect.y, halfWidth, rect.height), Styles.SecondaryColor);
                EditorGUI.DrawRect(new Rect(rect.x + halfWidth + middleGap, rect.y, halfWidth, rect.height), Styles.SecondaryColor);
                EditorGUI.DrawRect(new Rect(rect.x,                         rect.y, halfWidth, 24f), Styles.AccentColor);
                EditorGUI.DrawRect(new Rect(rect.x + halfWidth + middleGap, rect.y, halfWidth, 24f), Styles.AccentColor);
            }

            GUI.Label(new Rect(rect.x,                         rect.y, halfWidth, 24f), "Write Defaults On",  Styles.HeaderLabel);
            GUI.Label(new Rect(rect.x + halfWidth + middleGap, rect.y, halfWidth, 24f), "Write Defaults Off", Styles.HeaderLabel);

            float rowY = rect.y + 26f;

            if (maxRows == 0)
            {
                GUI.Label(new Rect(rect.x,                         rowY, halfWidth, lineHeight), "—", Styles.EmptyLabel);
                GUI.Label(new Rect(rect.x + halfWidth + middleGap, rowY, halfWidth, lineHeight), "—", Styles.EmptyLabel);
            }
            else
            {
                for (int i = 0; i < maxRows; i++, rowY += rowHeight)
                {
                    bool hasOn  = i < onLayers.Length;
                    bool hasOff = i < offLayers.Length;

                    if (Event.current.type == EventType.Repaint && i % 2 == 1)
                    {
                        if (hasOn)  EditorGUI.DrawRect(new Rect(rect.x,                         rowY, halfWidth, rowHeight), Styles.RowAltColor);
                        if (hasOff) EditorGUI.DrawRect(new Rect(rect.x + halfWidth + middleGap, rowY, halfWidth, rowHeight), Styles.RowAltColor);
                    }

                    if (hasOn)
                        GUI.Label(new Rect(rect.x, rowY, halfWidth - 24f, lineHeight), onLayers[i].name, Styles.SmallLabelCenter);

                    if (hasOn && CursorBtn(new Rect(rect.x + halfWidth - 24f, rowY, 24f, lineHeight), "→", Styles.IconBtn))
                        SetLayerWD(onLayers[i], false);

                    if (hasOff && CursorBtn(new Rect(rect.x + halfWidth + middleGap, rowY, 24f, lineHeight), "←", Styles.IconBtn))
                        SetLayerWD(offLayers[i], true);

                    if (hasOff)
                        GUI.Label(new Rect(rect.x + halfWidth + middleGap + 24f, rowY, halfWidth - 24f, lineHeight), offLayers[i].name, Styles.SmallLabelCenter);
                }
            }

            if (mixedLayers.Length > 0)
            {
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Mixed", Styles.HeaderLabel, GUILayout.Height(24));
                    GUILayout.FlexibleSpace();
                }

                var mixedRowsRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
                if (Event.current.type == EventType.Repaint && mixedRowsRect.height > 0)
                    EditorGUI.DrawRect(mixedRowsRect, Styles.SecondaryColor);

                foreach (var layer in mixedLayers)
                {
                    var rowRect     = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                    float btnWidth  = 48f;
                    float gap       = 8f;
                    float nameWidth = Styles.SmallLabelCenter.CalcSize(new GUIContent(layer.name)).x;
                    float groupWidth = btnWidth + gap + nameWidth + gap + btnWidth;
                    float groupX    = rowRect.x + (rowRect.width - groupWidth) / 2f;

                    if (CursorBtn(new Rect(groupX, rowRect.y, btnWidth, rowRect.height), "← On", Styles.IconBtn))
                        SetLayerWD(layer, true);
                    GUI.Label(new Rect(groupX + btnWidth + gap, rowRect.y, nameWidth, rowRect.height), layer.name, Styles.SmallLabelCenter);
                    if (CursorBtn(new Rect(groupX + btnWidth + gap + nameWidth + gap, rowRect.y, btnWidth, rowRect.height), "→ Off", Styles.IconBtn))
                        SetLayerWD(layer, false);
                }

                EditorGUILayout.EndVertical();
            }
        }

        // ── Network Sync ──────────────────────────────────────────────────────

        bool   _networkUseBool;
        string _networkParamName        = "network";
        string _networkStatesPrefix     = "{N} ";
        bool   _networkRemoveParamDrivers;
        bool   _networkRemoveAudioPlay;
        bool   _networkRemoveTracking;
        bool   _networkAnyStateTransitions;
        bool   _networkPackIntoSubSM;

        void DrawNetworkSyncSection()
        {
            if (_activeStateMachine == null)
            {
                EditorGUILayout.LabelField("No animator window open", Styles.EmptyLabel);
                return;
            }


            DrawNetworkToggleRow("Sync Param Type", ref _networkUseBool,            "Int",        "Bool");
            DrawNetworkToggleRow("Transitions",     ref _networkAnyStateTransitions, "All-to-All", "Any State");

            var smAssetPath = AssetDatabase.GetAssetPath(_activeStateMachine);
            var activeController = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(smAssetPath);
            string trimmedNetworkParamName = _networkParamName.Trim();
            bool isDuplicateName = activeController != null
                && !string.IsNullOrWhiteSpace(trimmedNetworkParamName)
                && activeController.parameters.Any(parameter =>
                    parameter.name == trimmedNetworkParamName
                    || (parameter.name.StartsWith(trimmedNetworkParamName)
                        && parameter.name.Length > trimmedNetworkParamName.Length
                        && parameter.name[trimmedNetworkParamName.Length..].All(char.IsDigit)));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Sync Param Name", Styles.SmallLabel, GUILayout.Width(164));
                _networkParamName = EditorGUILayout.TextField(_networkParamName);
                if (isDuplicateName && Event.current.type == EventType.Repaint)
                {
                    var textFieldRect = GUILayoutUtility.GetLastRect();
                    float iconSize = 16f;
                    var warningRect = new Rect(textFieldRect.xMax - iconSize - 2f, textFieldRect.y + (textFieldRect.height - iconSize) * 0.5f, iconSize, iconSize);
                    GUI.Label(warningRect, new GUIContent(EditorGUIUtility.IconContent("warning@2x").image, "Duplicate Name"), GUIStyle.none);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Network States Prefix", Styles.SmallLabel, GUILayout.Width(164));
                _networkStatesPrefix = EditorGUILayout.TextField(_networkStatesPrefix);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Remove Network Behaviours", Styles.SmallLabel, GUILayout.Width(164));
                GUILayout.Label("Params", Styles.SmallLabel, GUILayout.Width(50));
                _networkRemoveParamDrivers = EditorGUILayout.Toggle(_networkRemoveParamDrivers, GUILayout.Width(16));
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                GUILayout.Space(6);
                GUILayout.Label("Audio", Styles.SmallLabel, GUILayout.Width(36));
                _networkRemoveAudioPlay = EditorGUILayout.Toggle(_networkRemoveAudioPlay, GUILayout.Width(16));
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                GUILayout.Space(6);
                GUILayout.Label("Tracking", Styles.SmallLabel, GUILayout.Width(52));
                _networkRemoveTracking = EditorGUILayout.Toggle(_networkRemoveTracking, GUILayout.Width(16));
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Pack into SubSM", Styles.SmallLabel, GUILayout.Width(164));
                _networkPackIntoSubSM = EditorGUILayout.Toggle(_networkPackIntoSubSM, GUILayout.Width(16));
            }

            EditorGUILayout.Space(6);

            bool canRun = !string.IsNullOrWhiteSpace(_networkParamName) && !string.IsNullOrWhiteSpace(_networkStatesPrefix) && !isDuplicateName;

            using (new EditorGUI.DisabledScope(!canRun))
            {
                if (CursorBtn("Run Network Sync", Styles.IconBtn, GUILayout.Height(28)))
                {
                    AnimatorNetworkSync.NetworkSync(_activeStateMachine, new NetworkSyncConfig
                    {
                        useBool             = _networkUseBool,
                        paramName           = _networkParamName.Trim(),
                        statesPrefix        = _networkStatesPrefix,
                        removeParamDrivers  = _networkRemoveParamDrivers,
                        removeAudioPlay     = _networkRemoveAudioPlay,
                        removeTracking      = _networkRemoveTracking,
                        anyStateTransitions = _networkAnyStateTransitions,
                        packIntoSubSM       = _networkPackIntoSubSM
                    });
                }
            }
        }

        /* Draws a two-button exclusive toggle row with a left-aligned label and cursor-rect on both buttons. */
        static void DrawNetworkToggleRow(string label, ref bool value, string falseLabel, string trueLabel)
        {
            var rect            = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float buttonWidth   = (rect.width - 164f) / 2f;
            float firstButtonX  = rect.x + 164f;
            float secondButtonX = firstButtonX + buttonWidth;

            GUI.Label(new Rect(rect.x, rect.y, 164f, rect.height), label, Styles.SmallLabel);

            var falseRect  = new Rect(firstButtonX,  rect.y, buttonWidth, rect.height);
            var trueRect   = new Rect(secondButtonX, rect.y, buttonWidth, rect.height);

            if (GUI.Button(falseRect, falseLabel, !value ? Styles.IconBtnActive : Styles.IconBtn)) value = false;
            EditorGUIUtility.AddCursorRect(falseRect, MouseCursor.Link);

            if (GUI.Button(trueRect, trueLabel, value ? Styles.IconBtnActive : Styles.IconBtn)) value = true;
            EditorGUIUtility.AddCursorRect(trueRect, MouseCursor.Link);
        }

        // ── Sub-Assets ────────────────────────────────────────────────────────

        static GUIContent[] _subAssetFilterContents;
        static GUIContent[] SubAssetFilterContents => _subAssetFilterContents ??= new[]
        {
            new GUIContent("State Machines", EditorGUIUtility.IconContent("d_AnimatorController Icon").image),
            new GUIContent("States",         EditorGUIUtility.IconContent("AnimatorState Icon").image),
            new GUIContent("Blend Trees",    EditorGUIUtility.IconContent("d_BlendTree Icon").image),
        };


        static Type       _animatorControllerToolType;
        static MethodInfo _setCurrentLayerMethod;
        static MethodInfo _addBreadCrumbMethod;
        static MethodInfo _frameSelectionMethod;

        static Type AnimatorControllerToolType =>
            _animatorControllerToolType ??= AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityEditor.Graphs.AnimatorControllerTool"))
                .FirstOrDefault(t => t != null);

        int                    _subAssetTypeFilter;
        string                 _subAssetSearch = "";
        AnimatorController     _subAssetCachedController;
        UnityEngine.Object[][] _subAssetsByType;
        UnityEngine.Object[]   _orphanedAssets;
        HashSet<int>           _statesWithInvalidTransitions;
        HashSet<int>           _emptySMIds;
        HashSet<int>           _blendTreesWithEmptyMotion;
        HashSet<int>           _rootSMIds;
        HashSet<int>           _allKnownSubAssetIds;
        UnityEngine.Object     _cachedAnimatorControllerTool;

        /* Invalidates and repaints only when a change event touches an object inside the active controller's asset file. Ignores unrelated scene and asset changes. Destroyed objects are matched against the cached sub-asset ID set since their path is no longer resolvable. */
        void OnAssetChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (_controller == null) return;
            string controllerPath = AssetDatabase.GetAssetPath(_controller);

            for (int i = 0; i < stream.length; i++)
            {
                bool relevant = false;
                var kind = stream.GetEventType(i);

                if (kind == ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    stream.GetChangeAssetObjectPropertiesEvent(i, out var args);
                    var changedObj = EditorUtility.InstanceIDToObject(args.instanceId);
                    relevant = changedObj != null && AssetDatabase.GetAssetPath(changedObj) == controllerPath;
                }
                else if (kind == ObjectChangeKind.CreateAssetObject)
                {
                    stream.GetCreateAssetObjectEvent(i, out var args);
                    var createdObj = EditorUtility.InstanceIDToObject(args.instanceId);
                    relevant = createdObj != null && AssetDatabase.GetAssetPath(createdObj) == controllerPath;
                }
                else if (kind == ObjectChangeKind.DestroyAssetObject)
                {
                    stream.GetDestroyAssetObjectEvent(i, out var args);
                    relevant = _allKnownSubAssetIds?.Contains(args.instanceId) ?? false;
                }

                if (!relevant) continue;
                _subAssetsByType = null;
                if (_controllerSubTab == 2) Repaint();
                return;
            }
        }

        void DrawSubAssetsSection()
        {
            if (_controller == null)
            {
                EditorGUILayout.LabelField("No controller selected", Styles.EmptyLabel);
                return;
            }

            if (_subAssetsByType == null || _subAssetCachedController != _controller)
                RebuildSubAssetCache();

            // Filter bar
            var filterBarRect  = EditorGUILayout.GetControlRect(false, 24f);
            float filterBtnWidth = filterBarRect.width / 3f;

            EditorGUIUtility.SetIconSize(new Vector2(18, 18));
            var filterContents = SubAssetFilterContents;
            for (int i = 0; i < filterContents.Length; i++)
            {
                bool isActive = _subAssetTypeFilter == i;
                var  btnRect  = new Rect(filterBarRect.x + i * filterBtnWidth, filterBarRect.y, filterBtnWidth, 24f);
                if (GUI.Toggle(btnRect, isActive, filterContents[i], isActive ? Styles.IconBtnActive : Styles.IconBtn))
                    _subAssetTypeFilter = i;
                EditorGUIUtility.AddCursorRect(btnRect, MouseCursor.Link);
            }
            EditorGUIUtility.SetIconSize(Vector2.zero);

            if (_subAssetsByType == null)
                return;

            // Search bar
            EditorGUILayout.Space(2);
            _subAssetSearch = EditorGUILayout.TextField(_subAssetSearch, EditorStyles.toolbarSearchField);
            if (string.IsNullOrEmpty(_subAssetSearch) && Event.current.type == EventType.Repaint)
            {
                var searchRect = GUILayoutUtility.GetLastRect();
                GUI.Label(new Rect(searchRect.x + 18, searchRect.y, searchRect.width - 18, searchRect.height), "Search", Styles.SubAssetSearchHint);
            }
            EditorGUILayout.Space(2);

            var assets = _subAssetsByType[_subAssetTypeFilter];
            if (assets == null || assets.Length == 0)
            {
                EditorGUILayout.LabelField("None", Styles.EmptyLabel);
                return;
            }

            bool hasSearch = !string.IsNullOrEmpty(_subAssetSearch);
            int drawn = 0;

            var listRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && listRect.height > 0)
                EditorGUI.DrawRect(listRect, Styles.SecondaryColor);

            foreach (var asset in assets)
            {
                if (asset == null) continue;
                if (hasSearch && asset.name.IndexOf(_subAssetSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;

                string label = asset.name;
                bool showEmptyWarning = false;
                bool showInvalidWarning = false;
                bool showEmptyMotionWarning = false;
                if (_subAssetTypeFilter == 0)
                {
                    if (_rootSMIds != null && !_rootSMIds.Contains(asset.GetInstanceID()))
                        label += "  <color=#888888>(Sub State Machine)</color>";
                    if (_emptySMIds != null && _emptySMIds.Contains(asset.GetInstanceID()))
                        showEmptyWarning = true;
                }
                else if (_subAssetTypeFilter == 1 &&
                    _statesWithInvalidTransitions != null &&
                    _statesWithInvalidTransitions.Contains(asset.GetInstanceID()))
                    showInvalidWarning = true;
                else if (_subAssetTypeFilter == 2 &&
                    _blendTreesWithEmptyMotion != null &&
                    _blendTreesWithEmptyMotion.Contains(asset.GetInstanceID()))
                    showEmptyMotionWarning = true;

                var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                if (Event.current.type == EventType.Repaint && drawn % 2 == 1)
                    EditorGUI.DrawRect(rowRect, Styles.RowAltColor);
                EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);
                if (GUI.Button(rowRect, label, Styles.SubAssetListLabel))
                    NavigateToAsset(asset);
                if (showEmptyWarning || showInvalidWarning || showEmptyMotionWarning)
                {
                    string warningTooltip = showEmptyWarning ? "Layer is empty"
                        : showEmptyMotionWarning ? "Contains empty motion field"
                        : "Contains invalid transition";
                    var warningIconContent = new GUIContent(EditorGUIUtility.IconContent("d_console.warnicon").image, warningTooltip);
                    var warningIconRect = new Rect(rowRect.xMax - 18, rowRect.y + 1, 16, rowRect.height - 2);
                    GUI.Label(warningIconRect, warningIconContent);
                }
                drawn++;
            }

            if (drawn == 0)
                GUI.Label(EditorGUILayout.GetControlRect(false, 20f), "No matches", Styles.EmptyLabel);

            EditorGUILayout.EndVertical();
        }

        /* Loads all sub-assets from the controller .asset file, buckets them by type into _subAssetsByType, collects unreferenced objects as orphans, and flags states with invalid transitions. */
        void RebuildSubAssetCache()
        {
            _subAssetCachedController = _controller;
            if (_controller == null) { _subAssetsByType = null; _orphanedAssets = null; return; }

            var allAssets     = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(_controller));
            var referencedIDs = CollectReferencedInstanceIDs(_controller);

            var stateMachines = new List<UnityEngine.Object>();
            var states        = new List<UnityEngine.Object>();
            var blendTrees    = new List<UnityEngine.Object>();
            var orphans       = new List<UnityEngine.Object>();

            foreach (var asset in allAssets)
            {
                if (asset == null || asset == _controller) continue;

                if (!referencedIDs.Contains(asset.GetInstanceID())) { orphans.Add(asset); continue; }

                if      (asset is AnimatorStateMachine) stateMachines.Add(asset);
                else if (asset is BlendTree)            blendTrees.Add(asset);
                else if (asset is AnimatorState)        states.Add(asset);
            }

            _subAssetsByType = new[]
            {
                stateMachines.OrderBy(a => a.name).ToArray(),
                states.OrderBy(a => a.name).ToArray(),
                blendTrees.OrderBy(a => a.name).ToArray()
            };
            _orphanedAssets = orphans.ToArray();

            var paramNames = new HashSet<string>(_controller.parameters.Select(p => p.name));
            _statesWithInvalidTransitions = new HashSet<int>();
            foreach (var asset in states)
            {
                if (asset is AnimatorState state && HasInvalidTransition(state, paramNames))
                    _statesWithInvalidTransitions.Add(asset.GetInstanceID());
            }

            _emptySMIds = new HashSet<int>(
                stateMachines
                    .OfType<AnimatorStateMachine>()
                    .Where(sm => sm.states.Length == 0 && sm.stateMachines.Length == 0)
                    .Select(sm => sm.GetInstanceID()));

            _blendTreesWithEmptyMotion = new HashSet<int>(
                blendTrees
                    .OfType<BlendTree>()
                    .Where(blendTree => blendTree.children.Any(child => child.motion == null))
                    .Select(blendTree => blendTree.GetInstanceID()));

            _rootSMIds = new HashSet<int>(
                _controller.layers.Select(layer => layer.stateMachine.GetInstanceID()));

            _allKnownSubAssetIds = new HashSet<int>(
                allAssets.Where(a => a != null && a != _controller).Select(a => a.GetInstanceID()));
        }

        /* Returns true if any transition on the state has no exit time and no conditions, or references a parameter not present in the controller. */
        static bool HasInvalidTransition(AnimatorState state, HashSet<string> paramNames)
        {
            foreach (var transition in state.transitions)
            {
                if (!transition.hasExitTime && transition.conditions.Length == 0) return true;
                foreach (var condition in transition.conditions)
                    if (!paramNames.Contains(condition.parameter)) return true;
            }
            return false;
        }

        /* Destroys all orphaned sub-assets via Undo.DestroyObjectImmediate, marks the controller dirty, and refreshes the cache. */
        void CleanOrphanedAssets()
        {
            if (_orphanedAssets == null || _orphanedAssets.Length == 0) return;
            foreach (var asset in _orphanedAssets)
            {
                if (asset != null)
                    Undo.DestroyObjectImmediate(asset);
            }
            EditorUtility.SetDirty(_controller);
            RebuildSubAssetCache();
        }

        /* Traverses all layers of the controller and returns the instance IDs of every object reachable from the graph — SMs, states, behaviours, transitions, and blend trees. Anything not in this set is an orphan. */
        static HashSet<int> CollectReferencedInstanceIDs(AnimatorController controller)
        {
            var ids = new HashSet<int>();
            ids.Add(controller.GetInstanceID());
            foreach (var layer in controller.layers)
                CollectSMReferences(layer.stateMachine, ids);
            return ids;
        }

        /* Recursively adds instance IDs of sm and all its children (states, behaviours, transitions, sub-SMs, blend trees) to ids. The ids.Add guard prevents revisiting the same SM twice. */
        static void CollectSMReferences(AnimatorStateMachine sm, HashSet<int> ids)
        {
            if (sm == null || !ids.Add(sm.GetInstanceID())) return;
            foreach (var behaviour in sm.behaviours)
                if (behaviour != null) ids.Add(behaviour.GetInstanceID());
            foreach (var transition in sm.anyStateTransitions)
                if (transition != null) ids.Add(transition.GetInstanceID());
            foreach (var transition in sm.entryTransitions)
                if (transition != null) ids.Add(transition.GetInstanceID());
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state == null) continue;
                ids.Add(state.GetInstanceID());
                foreach (var behaviour in state.behaviours)
                    if (behaviour != null) ids.Add(behaviour.GetInstanceID());
                foreach (var transition in state.transitions)
                    if (transition != null) ids.Add(transition.GetInstanceID());
                CollectBlendTreeReferences(state.motion as BlendTree, ids);
            }
            foreach (var childStateMachine in sm.stateMachines)
                CollectSMReferences(childStateMachine.stateMachine, ids);
        }

        /* Recursively adds instance IDs of blendTree and all its child blend tree nodes to ids. */
        static void CollectBlendTreeReferences(BlendTree blendTree, HashSet<int> ids)
        {
            if (blendTree == null || !ids.Add(blendTree.GetInstanceID())) return;
            foreach (var childMotion in blendTree.children)
                CollectBlendTreeReferences(childMotion.motion as BlendTree, ids);
        }

        // ── Navigation ────────────────────────────────────────────────────────

        void NavigateToAsset(UnityEngine.Object asset) => FocusAsset(asset, _controller);

        /* Navigates the Animator window to the layer containing asset, selects it, and frames it. Handles AnimatorState, AnimatorStateMachine, and BlendTree. */
        internal static void FocusAsset(UnityEngine.Object asset, AnimatorController controller)
        {
            var toolType = AnimatorControllerToolType;
            if (toolType == null || controller == null) return;

            var tools = Resources.FindObjectsOfTypeAll(toolType);
            if (tools.Length == 0) return;
            var tool = tools[0];

            int layerIndex = -1;
            var layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                var sm = layers[i].stateMachine;
                if (asset is AnimatorState layerState        && SMContainsState(sm, layerState))         { layerIndex = i; break; }
                if (asset is AnimatorStateMachine layerSubSM && SMContainsOrIs(sm, layerSubSM))          { layerIndex = i; break; }
                if (asset is BlendTree layerBlendTree        && SMContainsBlendTree(sm, layerBlendTree)) { layerIndex = i; break; }
            }
            if (layerIndex < 0) return;

            _setCurrentLayerMethod ??= toolType.GetMethod("SetCurrentLayer",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _addBreadCrumbMethod   ??= toolType.GetMethod("AddBreadCrumb",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _frameSelectionMethod  ??= toolType.GetMethod("FrameSelection",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            _setCurrentLayerMethod?.Invoke(tool, new object[] { layerIndex });

            var rootSM = controller.layers[layerIndex].stateMachine;

            if (asset is BlendTree blendTree)
            {
                var containingState = FindStateWithBlendTree(rootSM, blendTree);
                if (containingState != null)
                {
                    var parentSM = FindParentSM(rootSM, containingState);
                    PushSMBreadcrumbs(tool, rootSM, parentSM ?? rootSM);

                    var blendTreePath = FindBlendTreePath(containingState.motion as BlendTree, blendTree);
                    if (blendTreePath != null)
                    {
                        for (int i = 0; i < blendTreePath.Count; i++)
                            _addBreadCrumbMethod?.Invoke(tool, new object[] { (UnityEngine.Object)blendTreePath[i], i == blendTreePath.Count - 1 });
                    }
                }
            }
            else
            {
                AnimatorStateMachine targetSM = rootSM;
                if (asset is AnimatorState state)
                    targetSM = FindParentSM(rootSM, state) ?? rootSM;
                else if (asset is AnimatorStateMachine subSM)
                    targetSM = subSM;

                PushSMBreadcrumbs(tool, rootSM, targetSM);
            }

            Selection.activeObject = asset;

            var capturedTool   = tool;
            var capturedAsset  = asset;
            var capturedMethod = _frameSelectionMethod;
            EditorApplication.delayCall += () =>
            {
                Selection.activeObject = capturedAsset;
                EditorApplication.delayCall += () => capturedMethod?.Invoke(capturedTool, null);
            };
        }

        /* Returns the index of the first layer whose state machine hierarchy contains asset, or -1 if not found. */
        int FindLayerIndex(UnityEngine.Object asset)
        {
            var layers = _controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                var sm = layers[i].stateMachine;
                if (asset is AnimatorState state        && SMContainsState(sm, state))         return i;
                if (asset is AnimatorStateMachine subSM && SMContainsOrIs(sm, subSM))          return i;
                if (asset is BlendTree blendTree        && SMContainsBlendTree(sm, blendTree)) return i;
            }
            return -1;
        }

        /* Calls AnimatorControllerTool.AddBreadCrumb for each SM along the path from rootSM to targetSM, updating the graph only on the final entry so the window navigates in one step. */
        static void PushSMBreadcrumbs(object tool, AnimatorStateMachine rootSM, AnimatorStateMachine targetSM)
        {
            if (targetSM == rootSM) return;
            var path = FindSMPath(rootSM, targetSM);
            if (path == null) return;
            for (int i = 1; i < path.Count; i++)
                _addBreadCrumbMethod?.Invoke(tool, new object[] { (UnityEngine.Object)path[i], i == path.Count - 1 });
        }

        /* Returns the ordered list of SMs from root down to target (inclusive), or null if target is not reachable from root. */
        static List<AnimatorStateMachine> FindSMPath(AnimatorStateMachine root, AnimatorStateMachine target)
        {
            if (root == target) return new List<AnimatorStateMachine> { root };
            foreach (var childStateMachine in root.stateMachines)
            {
                var path = FindSMPath(childStateMachine.stateMachine, target);
                if (path != null) { path.Insert(0, root); return path; }
            }
            return null;
        }

        /* Returns the SM that directly contains state, searching recursively from root. Returns null if not found. */
        static AnimatorStateMachine FindParentSM(AnimatorStateMachine root, AnimatorState state)
        {
            foreach (var childState in root.states)
                if (childState.state == state) return root;
            foreach (var childStateMachine in root.stateMachines)
            {
                var found = FindParentSM(childStateMachine.stateMachine, state);
                if (found != null) return found;
            }
            return null;
        }

        /* Returns the first AnimatorState whose motion tree contains target (at any depth), or null if not found. */
        static AnimatorState FindStateWithBlendTree(AnimatorStateMachine sm, BlendTree target)
        {
            foreach (var childState in sm.states)
                if (BlendTreeContains(childState.state.motion as BlendTree, target)) return childState.state;
            foreach (var childStateMachine in sm.stateMachines)
            {
                var found = FindStateWithBlendTree(childStateMachine.stateMachine, target);
                if (found != null) return found;
            }
            return null;
        }

        /* Returns true if state is directly or recursively contained in sm. */
        static bool SMContainsState(AnimatorStateMachine sm, AnimatorState state)
        {
            foreach (var childState in sm.states)
                if (childState.state == state) return true;
            foreach (var childStateMachine in sm.stateMachines)
                if (SMContainsState(childStateMachine.stateMachine, state)) return true;
            return false;
        }

        /* Returns true if any state in sm or its sub-SMs uses target anywhere in its blend tree hierarchy. */
        static bool SMContainsBlendTree(AnimatorStateMachine sm, BlendTree target)
        {
            foreach (var childState in sm.states)
                if (BlendTreeContains(childState.state.motion as BlendTree, target)) return true;
            foreach (var childStateMachine in sm.stateMachines)
                if (SMContainsBlendTree(childStateMachine.stateMachine, target)) return true;
            return false;
        }

        /* Returns the ordered list of blend trees from root down to target (inclusive), preserving intermediate nodes for breadcrumb navigation. Returns null if target is not reachable. */
        static List<BlendTree> FindBlendTreePath(BlendTree root, BlendTree target)
        {
            if (root == null) return null;
            if (root == target) return new List<BlendTree> { root };
            foreach (var childMotion in root.children)
            {
                var path = FindBlendTreePath(childMotion.motion as BlendTree, target);
                if (path != null) { path.Insert(0, root); return path; }
            }
            return null;
        }

        /* Returns true if target is root or is reachable anywhere in root's child motion hierarchy. */
        static bool BlendTreeContains(BlendTree root, BlendTree target)
        {
            if (root == null) return false;
            if (root == target) return true;
            foreach (var childMotion in root.children)
                if (BlendTreeContains(childMotion.motion as BlendTree, target)) return true;
            return false;
        }

        // ── WD helpers ────────────────────────────────────────────────────────

        /* Returns On, Off, or Mixed depending on whether states in the layer have Write Defaults enabled, disabled, or both. Blend tree states are excluded if wdIncludeBlendTreeStates is false. */
        WDState GetLayerWDState(AnimatorControllerLayer layer)
        {
            bool hasOn = false, hasOff = false;
            bool includeBlendTrees = AnimatorDefaultSettings.Load().wdIncludeBlendTreeStates;
            CollectWDState(layer.stateMachine, ref hasOn, ref hasOff, includeBlendTrees);
            if (hasOn && hasOff) return WDState.Mixed;
            return hasOn ? WDState.On : WDState.Off;
        }

        /* Recursively sets hasOn and hasOff flags based on writeDefaultValues across all states in sm and its sub SMs. Skips states whose motion is a BlendTree when includeBlendTrees is false. */
        static void CollectWDState(AnimatorStateMachine sm, ref bool hasOn, ref bool hasOff, bool includeBlendTrees)
        {
            foreach (var childState in sm.states)
            {
                if (!includeBlendTrees && childState.state.motion is BlendTree) continue;
                if (childState.state.writeDefaultValues) hasOn = true;
                else hasOff = true;
            }
            foreach (var childStateMachine in sm.stateMachines)
                CollectWDState(childStateMachine.stateMachine, ref hasOn, ref hasOff, includeBlendTrees);
        }

        /* Sets Write Defaults on all states in a layer recursively and marks the controller dirty. Blend tree states are excluded if wdIncludeBlendTreeStates is false. */
        void SetLayerWD(AnimatorControllerLayer layer, bool value)
        {
            bool includeBlendTrees = AnimatorDefaultSettings.Load().wdIncludeBlendTreeStates;
            SetSMWD(layer.stateMachine, value, includeBlendTrees);
            EditorUtility.SetDirty(_controller);
        }

        /* Recursively sets writeDefaultValues on all states in sm and its sub SMs, registering each for undo. Skips states whose motion is a BlendTree when includeBlendTrees is false. */
        static void SetSMWD(AnimatorStateMachine sm, bool value, bool includeBlendTrees)
        {
            Undo.RegisterCompleteObjectUndo(sm, "Set Write Defaults");
            foreach (var childState in sm.states)
            {
                if (!includeBlendTrees && childState.state.motion is BlendTree) continue;
                Undo.RecordObject(childState.state, "Set Write Defaults");
                childState.state.writeDefaultValues = value;
                EditorUtility.SetDirty(childState.state);
            }
            foreach (var childStateMachine in sm.stateMachines)
                SetSMWD(childStateMachine.stateMachine, value, includeBlendTrees);
            EditorUtility.SetDirty(sm);
        }

        void SetAllLayersWD(bool value)
        {
            foreach (var layer in _controller.layers)
                SetLayerWD(layer, value);
        }

        // ── Transition focus (shared with AnimatorFindUsageWindow) ────────────

        /* Switches the Animator window to the layer and sub-SM containing transition, selects it, and frames it on the next editor tick. */
        internal static void FocusTransition(AnimatorStateTransition transition, AnimatorController controller)
        {
            var toolType = AnimatorEditorInit.AnimatorControllerToolType;
            if (toolType == null || controller == null) return;

            var tools = Resources.FindObjectsOfTypeAll(toolType);
            if (tools.Length == 0) return;
            var tool = tools[0];

            int layerIndex = -1;
            AnimatorStateMachine containingSM = null;

            for (int i = 0; i < controller.layers.Length; i++)
            {
                var found = FindSMContainingTransition(controller.layers[i].stateMachine, transition);
                if (found == null) continue;
                layerIndex = i;
                containingSM = found;
                break;
            }
            if (layerIndex < 0) return;

            _setCurrentLayerMethod ??= toolType.GetMethod("SetCurrentLayer",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _addBreadCrumbMethod   ??= toolType.GetMethod("AddBreadCrumb",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _frameSelectionMethod  ??= toolType.GetMethod("FrameSelection",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            _setCurrentLayerMethod?.Invoke(tool, new object[] { layerIndex });
            PushSMBreadcrumbs(tool, controller.layers[layerIndex].stateMachine, containingSM);

            Selection.activeObject = transition;
            var capturedTool = tool;
            var capturedFrameMethod = _frameSelectionMethod;
            EditorApplication.delayCall += () => capturedFrameMethod?.Invoke(capturedTool, null);
        }

        /* Returns the SM that directly owns transition via states or anyStateTransitions, searching recursively. Returns null if not found. */
        static AnimatorStateMachine FindSMContainingTransition(AnimatorStateMachine sm, AnimatorStateTransition transition)
        {
            foreach (var anyStateTransition in sm.anyStateTransitions)
                if (anyStateTransition == transition) return sm;
            foreach (var childState in sm.states)
                foreach (var stateTransition in childState.state.transitions)
                    if (stateTransition == transition) return sm;
            foreach (var childStateMachine in sm.stateMachines)
            {
                var found = FindSMContainingTransition(childStateMachine.stateMachine, transition);
                if (found != null) return found;
            }
            return null;
        }
    }
}
#endif
