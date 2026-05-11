
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.PhysBone.Components;


namespace YGDR.Editor.Animation
{
    internal static class WindowPatchReflection
    {
        // Layer view
        internal static readonly Type LayerControllerViewType =
            AccessTools.TypeByName("UnityEditor.Graphs.LayerControllerView");
        internal static readonly FieldInfo LayerScrollField =
            AccessTools.Field(LayerControllerViewType, "m_LayerScroll");
        internal static readonly FieldInfo LayerListField =
            AccessTools.Field(LayerControllerViewType, "m_LayerList");
        internal static readonly FieldInfo LayerViewHostField =
            AccessTools.Field(LayerControllerViewType, "m_Host");

        // Parameter view
        internal static readonly Type ParameterControllerViewType =
            AccessTools.TypeByName("UnityEditor.Graphs.ParameterControllerView");
        internal static readonly Type ParameterControllerViewElementType =
            AccessTools.Inner(ParameterControllerViewType, "Element");

        // ReorderableList scroll helpers
        internal static readonly MethodInfo GetElementHeightMethod =
            AccessTools.Method(typeof(ReorderableList), "GetElementHeight", new Type[] { typeof(int) });
        internal static readonly MethodInfo GetElementYOffsetMethod =
            AccessTools.Method(typeof(ReorderableList), "GetElementYOffset", new Type[] { typeof(int) });

        // AnimatorControllerTool access
        internal static readonly MethodInfo AnimatorControllerGetter =
            AccessTools.PropertyGetter(
                AccessTools.TypeByName("UnityEditor.Graphs.AnimatorControllerTool"),
                "animatorController");
        internal static UnityEditor.Animations.AnimatorController GetOpenController()
        {
            var windows = Resources.FindObjectsOfTypeAll(AnimatorEditorInit.AnimatorControllerToolType);
            if (windows.Length == 0) return null;
            return AnimatorControllerGetter?.Invoke(windows[0], null)
                as UnityEditor.Animations.AnimatorController;
        }

        internal static void InsertParameterAtIndex(UnityEditor.Animations.AnimatorController controller,
            int index, string paramName, UnityEngine.AnimatorControllerParameterType type)
        {
            Undo.RegisterCompleteObjectUndo(controller, $"Add {type} Parameter");
            controller.AddParameter(paramName, type);

            var serializedObject = new SerializedObject(controller);
            serializedObject.Update();
            var parametersProperty = serializedObject.FindProperty("m_AnimatorParameters");
            parametersProperty.MoveArrayElement(parametersProperty.arraySize - 1, index);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }
    }

    // Preserve layer scroll position when reordering or editing layers
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerScrollReset
    {
        static Vector2 _scrollCache;
        static bool _refocusSelectedLayer;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "ResetUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            _scrollCache = (Vector2)WindowPatchReflection.LayerScrollField.GetValue(__instance);
        }

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            if (AnimatorDefaultSettings.Load().preventLayerScroll) return;

            var scrollpos = (Vector2)WindowPatchReflection.LayerScrollField.GetValue(__instance);
            if (scrollpos.y == 0)
                WindowPatchReflection.LayerScrollField.SetValue(__instance, _scrollCache);
            _refocusSelectedLayer = true;
        }

        internal static bool ConsumeRefocus()
        {
            if (!_refocusSelectedLayer) return false;
            _refocusSelectedLayer = false;
            return true;
        }
    }

    // Scroll to keep selected layer visible after ResetUI
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerScrollRefocus
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance, Rect rect)
        {
            if (!PatchLayerScrollReset.ConsumeRefocus()) return;
            if (AnimatorDefaultSettings.Load().preventLayerScroll) return;

            var reorderableList = (ReorderableList)WindowPatchReflection.LayerListField.GetValue(__instance);
            var currentScroll = (Vector2)WindowPatchReflection.LayerScrollField.GetValue(__instance);
            float elementHeight = (float)WindowPatchReflection.GetElementHeightMethod.Invoke(reorderableList, new object[] { reorderableList.index }) + 20;
            float elementOffset = (float)WindowPatchReflection.GetElementYOffsetMethod.Invoke(reorderableList, new object[] { reorderableList.index });
            if (elementOffset < currentScroll.y)
                WindowPatchReflection.LayerScrollField.SetValue(__instance, new Vector2(currentScroll.x, elementOffset));
            else if (elementOffset + elementHeight > currentScroll.y + rect.height)
                WindowPatchReflection.LayerScrollField.SetValue(__instance, new Vector2(currentScroll.x, elementOffset + elementHeight - rect.height));
        }
    }

    // Scroll parameter list to bottom when adding a new parameter
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchNewParameterScroll
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "AddParameterMenu");

        [HarmonyPostfix]
        static void Postfix(object __instance, object value)
        {
            var settings = AnimatorDefaultSettings.Load();
            if (!settings.scrollToNewParameter || settings.preventParameterScroll) return;
            Traverse.Create(__instance).Field("m_ScrollPosition").SetValue(new Vector2(0, 9001));
        }
    }

    // Default weight of newly added layers to 1
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerWeightDefault
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                typeof(UnityEditor.Animations.AnimatorController), "AddLayer",
                new Type[] { typeof(UnityEditor.Animations.AnimatorControllerLayer) });

        [HarmonyPrefix]
        static void Prefix(ref UnityEditor.Animations.AnimatorControllerLayer layer)
        {
            if (!AnimatorDefaultSettings.Load().defaultLayerWeight1) return;
            layer.defaultWeight = 1.0f;
        }
    }

    // Parameter row: type label overlay + VRC sync icon + right-click convert menu
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchParameterRow
    {
        static GUIStyle _typeStyle;
        static GUIStyle TypeStyle => _typeStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold,
            richText = true
        };

        static GUIContent _syncedIcon;
        static GUIContent _unsyncedIcon;
        static GUIContent SyncedIcon   => _syncedIcon   ??= EditorGUIUtility.IconContent("soloon");
        static GUIContent UnsyncedIcon => _unsyncedIcon ??= EditorGUIUtility.IconContent("solonormal");

        static GUIStyle _vrcBuiltinStyle;
        static GUIStyle VrcBuiltinStyle => _vrcBuiltinStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.cyan }
        };

        static GUIContent _clipUsesIcon;
        static GUIContent ClipUsesIcon => _clipUsesIcon ??= EditorGUIUtility.IconContent("d_unityeditor.graphs.animatorcontrollertool@2x");

        static GUIContent _vrcComponentIcon;
        static GUIContent VrcComponentIcon => _vrcComponentIcon ??= EditorGUIUtility.IconContent("templatecontainer@2x");

        static bool _vrcComponentNeedsRebuild = true;
        static HashSet<string> _vrcComponentUsedParams;

        static readonly HashSet<string> VrcBuiltinNames =
            new HashSet<string>(PatchParameterContextMenu.VrcParameters.Select(tuple => tuple.name));

        static int _clipCacheControllerId = -1;
        static HashSet<string> _clipUsedParams;

        internal static UnityEditor.Animations.AnimatorController ViewFrameController;
        internal static HashSet<string> ViewFrameClipUsedParams;

        static PatchParameterRow()
        {
            Undo.undoRedoPerformed += () => _clipCacheControllerId = -1;
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream stream) => _clipCacheControllerId = -1;
            EditorApplication.hierarchyChanged += () => _vrcComponentNeedsRebuild = true;
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream stream) =>
            {
                for (int i = 0; i < stream.length; i++)
                {
                    if (stream.GetEventType(i) != ObjectChangeKind.ChangeGameObjectOrComponentProperties) continue;
                    stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var data);
                    var obj = EditorUtility.InstanceIDToObject(data.instanceId);
                    if (obj is ContactReceiver || obj is VRCPhysBone || obj is VRCRaycast)
                    {
                        _vrcComponentNeedsRebuild = true;
                        EditorApplication.delayCall += InternalEditorUtility.RepaintAllViews;
                        return;
                    }
                }
            };
        }

        internal static HashSet<string> GetVrcComponentUsedParams()
        {
            if (!_vrcComponentNeedsRebuild && _vrcComponentUsedParams != null) return _vrcComponentUsedParams;
            _vrcComponentUsedParams = AnimatorFindUsageWindow.BuildAllEffectingParamNames();
            _vrcComponentNeedsRebuild = false;
            return _vrcComponentUsedParams;
        }

        internal static HashSet<string> GetClipUsedParams(UnityEditor.Animations.AnimatorController controller)
        {
            int controllerId = controller.GetInstanceID();
            if (_clipCacheControllerId == controllerId && _clipUsedParams != null) return _clipUsedParams;
            _clipCacheControllerId = controllerId;
            _clipUsedParams = new HashSet<string>();
            foreach (var layer in controller.layers)
                CollectClipParams(layer.stateMachine, _clipUsedParams);
            return _clipUsedParams;
        }

        static void CollectClipParams(UnityEditor.Animations.AnimatorStateMachine stateMachine, HashSet<string> result)
        {
            foreach (var childState in stateMachine.states)
                CollectMotionParams(childState.state.motion, result);
            foreach (var childStateMachine in stateMachine.stateMachines)
                CollectClipParams(childStateMachine.stateMachine, result);
        }

        static void CollectMotionParams(UnityEngine.Motion motion, HashSet<string> result)
        {
            if (motion is AnimationClip clip)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (binding.type == typeof(UnityEngine.Animator))
                        result.Add(binding.propertyName);
                return;
            }
            if (motion is UnityEditor.Animations.BlendTree blendTree)
                foreach (var childMotion in blendTree.children)
                    CollectMotionParams(childMotion.motion, result);
        }

        static bool VrcTypesMatch(UnityEngine.AnimatorControllerParameterType animType, VRCExpressionParameters.ValueType vrcType) =>
            animType switch
            {
                UnityEngine.AnimatorControllerParameterType.Float   => vrcType == VRCExpressionParameters.ValueType.Float,
                UnityEngine.AnimatorControllerParameterType.Int     => vrcType == VRCExpressionParameters.ValueType.Int,
                UnityEngine.AnimatorControllerParameterType.Bool    => vrcType == VRCExpressionParameters.ValueType.Bool,
                UnityEngine.AnimatorControllerParameterType.Trigger => vrcType == VRCExpressionParameters.ValueType.Bool,
                _ => true
            };

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewElementType, "OnGUI");

        [HarmonyPostfix]
        static void Postfix(object __instance, Rect rect, int index, bool selected, bool focused)
        {
            try
            {
                var parameter = Traverse.Create(__instance).Field("m_Parameter").GetValue<UnityEngine.AnimatorControllerParameter>();
                if (parameter == null) return;

                var settings = AnimatorDefaultSettings.Load();
                bool showType = settings.showParamTypeIcons;
                bool showVrc  = settings.showParamVrcIcons;
                bool showAap  = settings.showParamAapIcons;
                bool showVrcComponent = settings.showParamVrcComponentIcons;

                bool hasSyncData = VRCSyncCache.TryGetSync(parameter.name, out bool isSynced);
                if (!hasSyncData && !showType && !showVrc && !showAap && !showVrcComponent) return;

                VRCExpressionParameters.ValueType vrcValueType = default;
                bool hasMismatch = showType
                    && VRCSyncCache.TryGetVrcValueType(parameter.name, out vrcValueType)
                    && !VrcTypesMatch(parameter.type, vrcValueType);

                const float iconSize = 14f;
                const float iconPadding = 2f;
                const float clipIconSize = 20f;
                const float vrcLabelWidth = 23f;

                float cursorX = rect.xMax - 72f;

                if (hasSyncData)
                {
                    cursorX -= iconSize + iconPadding;
                    GUI.Label(new Rect(cursorX, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize),
                              isSynced ? SyncedIcon : UnsyncedIcon);
                    cursorX -= iconPadding;
                }

                if (showType)
                {
                    var typeColor = hasMismatch ? new Color(0.5f, 0.5f, 0.5f) : parameter.type switch
                    {
                        UnityEngine.AnimatorControllerParameterType.Float   => settings.paramColorFloat,
                        UnityEngine.AnimatorControllerParameterType.Int     => settings.paramColorInt,
                        UnityEngine.AnimatorControllerParameterType.Bool    => settings.paramColorBool,
                        UnityEngine.AnimatorControllerParameterType.Trigger => settings.paramColorTrigger,
                        _ => Color.white
                    };

                    string typeText = parameter.type.ToString();
                    float typeTextWidth = TypeStyle.CalcSize(new GUIContent(typeText)).x;

                    if (hasMismatch)
                    {
                        var vrcColor = vrcValueType switch
                        {
                            VRCExpressionParameters.ValueType.Float => settings.paramColorFloat,
                            VRCExpressionParameters.ValueType.Int   => settings.paramColorInt,
                            _                                        => settings.paramColorBool,
                        };

                        string vrcTypeText = vrcValueType.ToString();
                        float vrcTypeWidth = TypeStyle.CalcSize(new GUIContent(vrcTypeText)).x;
                        TypeStyle.normal.textColor = vrcColor;
                        cursorX -= vrcTypeWidth;
                        GUI.Label(new Rect(cursorX, rect.y, vrcTypeWidth, rect.height), vrcTypeText, TypeStyle);

                        float sepWidth = TypeStyle.CalcSize(new GUIContent("/")).x;
                        cursorX -= sepWidth;
                        TypeStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                        GUI.Label(new Rect(cursorX, rect.y, sepWidth, rect.height), "/", TypeStyle);
                    }

                    TypeStyle.normal.textColor = typeColor;
                    cursorX -= typeTextWidth;
                    GUI.Label(new Rect(cursorX, rect.y, typeTextWidth, rect.height), typeText, TypeStyle);

                    cursorX -= iconPadding;
                }

                if (showVrc && VrcBuiltinNames.Contains(parameter.name))
                {
                    cursorX -= vrcLabelWidth;
                    GUI.Label(new Rect(cursorX, rect.y, vrcLabelWidth, rect.height), "VRC", VrcBuiltinStyle);
                    cursorX -= iconPadding;
                }

                if (showVrcComponent && GetVrcComponentUsedParams().Contains(parameter.name))
                {
                    cursorX -= clipIconSize;
                    var vrcComponentIconRect = new Rect(cursorX, rect.y + (rect.height - clipIconSize) * 0.5f, clipIconSize, clipIconSize);
                    GUI.Label(vrcComponentIconRect, VrcComponentIcon);
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && vrcComponentIconRect.Contains(Event.current.mousePosition))
                    {
                        Event.current.Use();
                        AnimatorFindUsageWindow.OpenEffectingObjects(parameter, ViewFrameController);
                    }
                    cursorX -= iconPadding;
                }

                if (showAap && ViewFrameClipUsedParams != null && ViewFrameClipUsedParams.Contains(parameter.name))
                {
                    cursorX -= clipIconSize;
                    var aapIconRect = new Rect(cursorX, rect.y + (rect.height - clipIconSize) * 0.5f, clipIconSize, clipIconSize);
                    GUI.Label(aapIconRect, ClipUsesIcon);
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && aapIconRect.Contains(Event.current.mousePosition))
                    {
                        Event.current.Use();
                        AnimatorFindUsageWindow.OpenAap(parameter, ViewFrameController);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Parameter row error: {e}");
            }
        }
    }

    // Replaces the "+" dropdown to insert below selected param, plus VRC parameter presets
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchParameterAddMenu
    {
        static readonly Type _viewType = WindowPatchReflection.ParameterControllerViewType;
        static readonly Type _overlayType = AccessTools.TypeByName("UnityEditor.RenameOverlay");
        internal static readonly FieldInfo ParamListField =
            _viewType?.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f => f.FieldType == typeof(ReorderableList));
        static readonly FieldInfo _renameOverlayField  = AccessTools.Field(_viewType, "m_RenameOverlay");
        static readonly MethodInfo _rebuildListMethod  = AccessTools.Method(_viewType, "RebuildList");
        static readonly MethodInfo _renameEndMethod    = AccessTools.Method(_viewType, "RenameEnd");
        static readonly MethodInfo _isRenamingMethod   = AccessTools.Method(_overlayType, "IsRenaming");
        static readonly MethodInfo _beginRenameMethod  = AccessTools.Method(_overlayType, "BeginRename",
            new[] { typeof(string), typeof(int), typeof(float) });

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(_viewType, "OnAddParameter");

        [HarmonyPrefix]
        static bool Prefix(object __instance, Rect buttonRect)
        {
            try
            {
                var controller = WindowPatchReflection.GetOpenController();
                if (controller == null) return true;

                var reorderableList = ParamListField?.GetValue(__instance) as ReorderableList;
                int insertIndex = (reorderableList != null && reorderableList.index >= 0)
                    ? reorderableList.index + 1
                    : controller.parameters.Length;

                var menu = new GenericMenu();
                var capturedInstance = __instance;
                foreach (UnityEngine.AnimatorControllerParameterType type in
                         Enum.GetValues(typeof(UnityEngine.AnimatorControllerParameterType)))
                {
                    var capturedType = type;
                    menu.AddItem(new GUIContent(type.ToString()), false, () =>
                        InsertWithUniqueName(capturedInstance, controller, insertIndex, capturedType));
                }

                var existingParamNames = new HashSet<string>(controller.parameters.Select(parameter => parameter.name));
                menu.AddSeparator("");
                foreach (var (category, vrcParamName, vrcParamType) in PatchParameterContextMenu.VrcParameters)
                {
                    var content = new GUIContent($"VRC/{category}/{vrcParamName}");
                    if (existingParamNames.Contains(vrcParamName))
                    {
                        menu.AddDisabledItem(content, true);
                    }
                    else
                    {
                        var capturedName = vrcParamName;
                        var capturedType = vrcParamType;
                        menu.AddItem(content, false, () =>
                            WindowPatchReflection.InsertParameterAtIndex(controller, insertIndex, capturedName, capturedType));
                    }
                }

                menu.DropDown(buttonRect);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Parameter add menu error: {e}");
                return true;
            }
        }

        internal static void InsertWithUniqueName(object instance, UnityEditor.Animations.AnimatorController controller,
            int index, UnityEngine.AnimatorControllerParameterType type)
        {
            string baseName = type.ToString();
            string paramName = baseName;
            var existingNames = new HashSet<string>(controller.parameters.Select(parameter => parameter.name));
            int counter = 1;
            while (existingNames.Contains(paramName))
                paramName = $"{baseName} {counter++}";
            WindowPatchReflection.InsertParameterAtIndex(controller, index, paramName, type);

            _rebuildListMethod?.Invoke(instance, null);
            var paramList = ParamListField?.GetValue(instance) as ReorderableList;
            if (paramList != null) paramList.index = index;
            var renameOverlay = _renameOverlayField?.GetValue(instance);
            if (renameOverlay == null) return;
            if (_isRenamingMethod?.Invoke(renameOverlay, null) is true)
                _renameEndMethod?.Invoke(instance, null);
            _beginRenameMethod?.Invoke(renameOverlay, new object[] { paramName, index, 0.1f });
        }
    }


    // Right-click convert menu on ParameterControllerView.OnGUI (Element.OnGUI is Repaint-only)
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchParameterContextMenu
    {
        internal static readonly (string category, string name, UnityEngine.AnimatorControllerParameterType type)[] VrcParameters =
        {
            ("Local",    "IsLocal",              UnityEngine.AnimatorControllerParameterType.Bool),
            ("Local",    "PreviewMode",          UnityEngine.AnimatorControllerParameterType.Int),
            ("Speech",   "Viseme",               UnityEngine.AnimatorControllerParameterType.Int),
            ("Speech",   "Voice",                UnityEngine.AnimatorControllerParameterType.Float),
            ("IK",       "GestureLeft",          UnityEngine.AnimatorControllerParameterType.Int),
            ("IK",       "GestureRight",         UnityEngine.AnimatorControllerParameterType.Int),
            ("IK",       "AngularY",             UnityEngine.AnimatorControllerParameterType.Float),
            ("IK",       "VelocityX",            UnityEngine.AnimatorControllerParameterType.Float),
            ("IK",       "VelocityY",            UnityEngine.AnimatorControllerParameterType.Float),
            ("IK",       "VelocityZ",            UnityEngine.AnimatorControllerParameterType.Float),
            ("IK",       "VelocityMagnitude",    UnityEngine.AnimatorControllerParameterType.Float),
            ("IK",       "Upright",              UnityEngine.AnimatorControllerParameterType.Float),
            ("IK",       "Grounded",             UnityEngine.AnimatorControllerParameterType.Bool),
            ("IK",       "Seated",               UnityEngine.AnimatorControllerParameterType.Bool),
            ("IK",       "AFK",                  UnityEngine.AnimatorControllerParameterType.Bool),
            ("IK",       "VRMode",               UnityEngine.AnimatorControllerParameterType.Int),
            ("IK",       "InStation",            UnityEngine.AnimatorControllerParameterType.Bool),
            ("IK",       "AvatarVersion",        UnityEngine.AnimatorControllerParameterType.Int),
            ("Playable", "GestureLeftWeight",    UnityEngine.AnimatorControllerParameterType.Float),
            ("Playable", "GestureRightWeight",   UnityEngine.AnimatorControllerParameterType.Float),
            ("Playable", "TrackingType",         UnityEngine.AnimatorControllerParameterType.Int),
            ("Playable", "MuteSelf",             UnityEngine.AnimatorControllerParameterType.Bool),
            ("Playable", "Earmuffs",             UnityEngine.AnimatorControllerParameterType.Bool),
            ("Playable", "ScaleModified",        UnityEngine.AnimatorControllerParameterType.Bool),
            ("Playable", "ScaleFactor",          UnityEngine.AnimatorControllerParameterType.Float),
            ("Playable", "ScaleFactorInverse",   UnityEngine.AnimatorControllerParameterType.Float),
            ("Playable", "EyeHeightAsMeters",    UnityEngine.AnimatorControllerParameterType.Float),
            ("Playable", "EyeHeightAsPercent",   UnityEngine.AnimatorControllerParameterType.Float),
            ("Social",   "IsOnFriendsList",      UnityEngine.AnimatorControllerParameterType.Bool),
            ("System",   "IsAnimatorEnabled",    UnityEngine.AnimatorControllerParameterType.Bool),
        };

        internal static ReorderableList FindParamList(object instance) =>
            PatchParameterAddMenu.ParamListField?.GetValue(instance) as ReorderableList;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            var viewController = WindowPatchReflection.GetOpenController();
            PatchParameterRow.ViewFrameController = viewController;
            PatchParameterRow.ViewFrameClipUsedParams = viewController != null
                ? PatchParameterRow.GetClipUsedParams(viewController)
                : null;

            var currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseUp || currentEvent.button != 1) return;

            var reorderableList = FindParamList(__instance);
            if (reorderableList == null || reorderableList.index < 0) return;

            var controller = WindowPatchReflection.GetOpenController();
            if (controller == null || reorderableList.index >= controller.parameters.Length) return;

            var parameter = controller.parameters[reorderableList.index];
            var capturedIndex = reorderableList.index;
            var capturedInstance = __instance;

            var capturedScreenPos = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition);
            currentEvent.Use();
            var menu = new GenericMenu();
            foreach (UnityEngine.AnimatorControllerParameterType type in
                     Enum.GetValues(typeof(UnityEngine.AnimatorControllerParameterType)))
            {
                var capturedAddType = type;
                menu.AddItem(new GUIContent($"Add Parameter below/{type}"), false, () =>
                    PatchParameterAddMenu.InsertWithUniqueName(capturedInstance, controller, capturedIndex + 1, capturedAddType));
            }
            menu.AddSeparator("");
            foreach (UnityEngine.AnimatorControllerParameterType type in
                     Enum.GetValues(typeof(UnityEngine.AnimatorControllerParameterType)))
            {
                if (type == parameter.type) continue;
                var capturedType = type;
                menu.AddItem(new GUIContent($"Convert to {type}"), false, () =>
                    ConvertParameter(controller, capturedIndex, capturedType));
            }

            var expressionParameters = VRCSyncCache.GetExpressionParameters();
            if (expressionParameters?.parameters != null)
            {
                VRCExpressionParameters.Parameter vrcParam = null;
                foreach (var expressionParameter in expressionParameters.parameters)
                    if (expressionParameter.name == parameter.name) { vrcParam = expressionParameter; break; }

                var capturedExpressionParameters = expressionParameters;
                var capturedParamName = parameter.name;
                var capturedParamType = parameter.type;
                menu.AddSeparator("");

                if (vrcParam != null)
                {
                    bool capturedSynced = vrcParam.networkSynced;
                    menu.AddItem(new GUIContent(capturedSynced ? "Set Not Synced" : "Set Synced"), false,
                        () => SetVrcSynced(capturedExpressionParameters, capturedParamName, !capturedSynced));
                }
                else
                {
                    menu.AddItem(new GUIContent("Add to VRC Parameters"), false,
                        () => AddToVrcParameters(capturedExpressionParameters, capturedParamName, capturedParamType));
                }

                var capturedController = controller;
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Add All to VRC Parameters"), false,
                    () => AddAllToVrcParameters(capturedExpressionParameters, capturedController));
            }

            menu.AddSeparator("");
            var capturedFindParameter = parameter;
            var capturedFindController = controller;
            menu.AddItem(new GUIContent("Find Parameter Uses"), false,
                () => AnimatorFindUsageWindow.Open(capturedFindParameter, capturedFindController));

            if (PatchParameterRow.GetVrcComponentUsedParams().Contains(parameter.name))
                menu.AddItem(new GUIContent("Find Effecting Objects"), false,
                    () => AnimatorFindUsageWindow.OpenEffectingObjects(capturedFindParameter, capturedFindController));
            else
                menu.AddDisabledItem(new GUIContent("Find Effecting Objects"));

            var clipUsedParams = PatchParameterRow.ViewFrameClipUsedParams;
            if (clipUsedParams != null && clipUsedParams.Contains(parameter.name))
                menu.AddItem(new GUIContent("Find AAP Uses"), false,
                    () => AnimatorFindUsageWindow.OpenAap(capturedFindParameter, capturedFindController));
            else
                menu.AddDisabledItem(new GUIContent("Find AAP Uses"));

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Remap to Parameter"), false, static data =>
            {
                var (remapController, fromParamName, screenPos) = ((UnityEditor.Animations.AnimatorController, string, Vector2))data;
                EditorApplication.delayCall += () =>
                    new ParameterRemapDropdown(remapController, fromParamName).Show(new Rect(screenPos, Vector2.zero));
            }, (capturedFindController, capturedFindParameter.name, capturedScreenPos));
            menu.AddItem(new GUIContent("Delete and Clean"), false, static data =>
            {
                var (deleteController, deleteParamName) = ((UnityEditor.Animations.AnimatorController, string))data;
                DeleteParameterAndClean(deleteController, deleteParamName);
            }, (capturedFindController, capturedFindParameter.name));

            menu.ShowAsContext();
        }

        /* Changes the type of a parameter at the given index and fixes all affected transition conditions across all layers. */
        static void ConvertParameter(UnityEditor.Animations.AnimatorController controller, int index,
            UnityEngine.AnimatorControllerParameterType newType)
        {
            string paramName = controller.parameters[index].name;
            var sourceType = controller.parameters[index].type;
            Undo.RegisterCompleteObjectUndo(controller, "Convert Parameter");
            var serializedObject = new SerializedObject(controller);
            serializedObject.Update();
            var parametersProperty = serializedObject.FindProperty("m_AnimatorParameters");
            if (parametersProperty == null) return;
            parametersProperty.GetArrayElementAtIndex(index).FindPropertyRelative("m_Type").intValue = (int)newType;
            serializedObject.ApplyModifiedProperties();

            foreach (var layer in controller.layers)
                FixConditionsForConversion(layer.stateMachine, paramName, sourceType, newType);
        }

        /* Recursively updates conditions on all transitions in sm that reference paramName to match the new parameter type. */
        static void FixConditionsForConversion(UnityEditor.Animations.AnimatorStateMachine sm, string paramName,
            UnityEngine.AnimatorControllerParameterType sourceType, UnityEngine.AnimatorControllerParameterType newType)
        {
            var allTransitions = new List<UnityEditor.Animations.AnimatorStateTransition>(sm.anyStateTransitions);
            foreach (var childState in sm.states)
                allTransitions.AddRange(childState.state.transitions);

            foreach (var transition in allTransitions)
            {
                var conditions = transition.conditions;
                bool modified = false;
                for (int i = 0; i < conditions.Length; i++)
                {
                    if (conditions[i].parameter != paramName) continue;
                    if (!TryConvertCondition(conditions[i], sourceType, newType, out var converted)) continue;
                    conditions[i] = converted;
                    modified = true;
                }
                if (modified)
                {
                    Undo.RecordObject(transition, "Convert Parameter");
                    transition.conditions = conditions;
                }
            }

            foreach (var childStateMachine in sm.stateMachines)
                FixConditionsForConversion(childStateMachine.stateMachine, paramName, sourceType, newType);
        }

        /* Appends a VRC expression parameter for every animator parameter not already present in the expression parameters asset. */
        static void AddAllToVrcParameters(VRCExpressionParameters expressionParameters,
            UnityEditor.Animations.AnimatorController controller)
        {
            Undo.RecordObject(expressionParameters, "Add All Parameters to VRC");
            var existingNames = new HashSet<string>(expressionParameters.parameters.Select(expressionParameter => expressionParameter.name));
            var paramsList = expressionParameters.parameters.ToList();

            foreach (var animatorParameter in controller.parameters)
            {
                if (existingNames.Contains(animatorParameter.name)) continue;
                paramsList.Add(new VRCExpressionParameters.Parameter
                {
                    name = animatorParameter.name,
                    valueType = animatorParameter.type switch
                    {
                        UnityEngine.AnimatorControllerParameterType.Float => VRCExpressionParameters.ValueType.Float,
                        UnityEngine.AnimatorControllerParameterType.Int   => VRCExpressionParameters.ValueType.Int,
                        _                                                  => VRCExpressionParameters.ValueType.Bool
                    },
                    networkSynced = false,
                    saved = false,
                    defaultValue = 0f
                });
            }

            expressionParameters.parameters = paramsList.ToArray();
            EditorUtility.SetDirty(expressionParameters);
        }

        /* Appends a new VRC expression parameter matching the animator parameter type. Defaults to synced, not saved. */
        static void AddToVrcParameters(VRCExpressionParameters expressionParameters, string paramName,
            UnityEngine.AnimatorControllerParameterType paramType)
        {
            Undo.RecordObject(expressionParameters, "Add VRC Parameter");
            var newParam = new VRCExpressionParameters.Parameter
            {
                name = paramName,
                valueType = paramType switch
                {
                    UnityEngine.AnimatorControllerParameterType.Float => VRCExpressionParameters.ValueType.Float,
                    UnityEngine.AnimatorControllerParameterType.Int   => VRCExpressionParameters.ValueType.Int,
                    _                                                  => VRCExpressionParameters.ValueType.Bool
                },
                networkSynced = true,
                saved = false,
                defaultValue = 0f
            };
            var paramsList = expressionParameters.parameters.ToList();
            paramsList.Add(newParam);
            expressionParameters.parameters = paramsList.ToArray();
            EditorUtility.SetDirty(expressionParameters);
        }

        /* Sets networkSynced on the named VRC expression parameter, registers undo, and marks the asset dirty. */
        static void SetVrcSynced(VRCExpressionParameters expressionParameters, string paramName, bool synced)
        {
            Undo.RecordObject(expressionParameters, synced ? "Set VRC Parameter Synced" : "Set VRC Parameter Not Synced");
            foreach (var expressionParameter in expressionParameters.parameters)
            {
                if (expressionParameter.name == paramName)
                {
                    expressionParameter.networkSynced = synced;
                    break;
                }
            }
            EditorUtility.SetDirty(expressionParameters);
        }

        /* Maps a condition to the nearest valid mode for newType (e.g. Equals→If when Int→Bool), returning false if no mapping exists. */
        static bool TryConvertCondition(UnityEditor.Animations.AnimatorCondition condition,
            UnityEngine.AnimatorControllerParameterType sourceType, UnityEngine.AnimatorControllerParameterType newType,
            out UnityEditor.Animations.AnimatorCondition result)
        {
            result = condition;
            var mode = condition.mode;
            float threshold = condition.threshold;

            UnityEditor.Animations.AnimatorConditionMode newMode;
            float newThreshold;

            var Int     = UnityEngine.AnimatorControllerParameterType.Int;
            var Bool    = UnityEngine.AnimatorControllerParameterType.Bool;
            var Float   = UnityEngine.AnimatorControllerParameterType.Float;
            var Equals  = UnityEditor.Animations.AnimatorConditionMode.Equals;
            var NotEqual= UnityEditor.Animations.AnimatorConditionMode.NotEqual;
            var Greater = UnityEditor.Animations.AnimatorConditionMode.Greater;
            var Less    = UnityEditor.Animations.AnimatorConditionMode.Less;
            var If      = UnityEditor.Animations.AnimatorConditionMode.If;
            var IfNot   = UnityEditor.Animations.AnimatorConditionMode.IfNot;

            if (sourceType == Int && newType == Bool)
            {
                if (mode == Equals)  { newMode = If;    newThreshold = 0f; }
                else if (mode == NotEqual) { newMode = IfNot; newThreshold = 0f; }
                else return false;
            }
            else if (sourceType == Int && newType == Float)
            {
                if (mode == Equals)  { newMode = Greater; newThreshold = threshold; }
                else if (mode == NotEqual) { newMode = Less;    newThreshold = threshold; }
                else return false;
            }
            else if (sourceType == Bool && (newType == Int || newType == Float))
            {
                if (newType == Int)
                {
                    if (mode == If)    { newMode = Equals;   newThreshold = 1f; }
                    else if (mode == IfNot) { newMode = NotEqual; newThreshold = 1f; }
                    else return false;
                }
                else
                {
                    if (mode == If)    { newMode = Greater; newThreshold = 0f; }
                    else if (mode == IfNot) { newMode = Less;    newThreshold = 1f; }
                    else return false;
                }
            }
            else if (sourceType == Float && newType == Int)
            {
                if (mode == Greater) { newMode = Equals;   newThreshold = threshold; }
                else if (mode == Less)    { newMode = NotEqual; newThreshold = threshold; }
                else return false;
            }
            else if (sourceType == Float && newType == Bool)
            {
                if (mode == Greater) { newMode = If;    newThreshold = 0f; }
                else if (mode == Less)    { newMode = IfNot; newThreshold = 0f; }
                else return false;
            }
            else return false;

            result = new UnityEditor.Animations.AnimatorCondition
            {
                mode = newMode,
                parameter = condition.parameter,
                threshold = newThreshold
            };
            return true;
        }

        /* Remaps all transition conditions referencing fromParamName to toParamName across all layers. */
        static void RemapParameter(UnityEditor.Animations.AnimatorController controller, string fromParamName, string toParamName)
        {
            foreach (var layer in controller.layers)
                RemapParameterInStateMachine(layer.stateMachine, fromParamName, toParamName);
            EditorUtility.SetDirty(controller);
        }

        static void RemapParameterInStateMachine(UnityEditor.Animations.AnimatorStateMachine stateMachine, string fromParamName, string toParamName)
        {
            foreach (var transition in stateMachine.anyStateTransitions)
                RemapConditions(transition, fromParamName, toParamName);
            foreach (var childState in stateMachine.states)
                foreach (var transition in childState.state.transitions)
                    RemapConditions(transition, fromParamName, toParamName);
            foreach (var childStateMachine in stateMachine.stateMachines)
                RemapParameterInStateMachine(childStateMachine.stateMachine, fromParamName, toParamName);
        }

        static void RemapConditions(UnityEditor.Animations.AnimatorStateTransition transition, string fromParamName, string toParamName)
        {
            var conditions = transition.conditions;
            bool modified = false;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].parameter != fromParamName) continue;
                var condition = conditions[i];
                condition.parameter = toParamName;
                conditions[i] = condition;
                modified = true;
            }
            if (!modified) return;
            Undo.RecordObject(transition, "Remap Parameter");
            transition.conditions = conditions;
        }

        /* Strips conditions referencing paramName from all transitions across all layers, then removes the parameter. */
        static void DeleteParameterAndClean(UnityEditor.Animations.AnimatorController controller, string paramName)
        {
            Undo.RegisterCompleteObjectUndo(controller, "Delete and Clean Parameter");

            foreach (var layer in controller.layers)
                DeleteTransitionsReferencingParam(layer.stateMachine, paramName);

            int paramIndex = System.Array.FindIndex(controller.parameters, parameter => parameter.name == paramName);
            if (paramIndex >= 0)
                controller.RemoveParameter(paramIndex);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        static void DeleteTransitionsReferencingParam(UnityEditor.Animations.AnimatorStateMachine stateMachine, string paramName)
        {
            foreach (var transition in stateMachine.anyStateTransitions)
                StripConditionsForParam(transition, paramName);

            foreach (var childState in stateMachine.states)
                foreach (var transition in childState.state.transitions)
                    StripConditionsForParam(transition, paramName);

            foreach (var childStateMachine in stateMachine.stateMachines)
                DeleteTransitionsReferencingParam(childStateMachine.stateMachine, paramName);
        }

        static void StripConditionsForParam(UnityEditor.Animations.AnimatorStateTransition transition, string paramName)
        {
            if (!transition.conditions.Any(condition => condition.parameter == paramName)) return;
            Undo.RecordObject(transition, "Delete and Clean Parameter");
            transition.conditions = transition.conditions.Where(condition => condition.parameter != paramName).ToArray();
        }

        class ParameterRemapDropdown : AdvancedDropdown
        {
            readonly UnityEditor.Animations.AnimatorController _controller;
            readonly string _fromParam;

            internal ParameterRemapDropdown(UnityEditor.Animations.AnimatorController controller, string fromParam)
                : base(new AdvancedDropdownState())
            {
                _controller = controller;
                _fromParam = fromParam;
                minimumSize = new Vector2(200, 250);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Remap to Parameter");
                foreach (var parameter in _controller.parameters)
                {
                    if (parameter.name == _fromParam) continue;
                    root.AddChild(new AdvancedDropdownItem(parameter.name));
                }
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
                => RemapParameter(_controller, _fromParam, item.name);
        }
    }


    // Layer copy/paste via right-click context menu on each layer row
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerCopyPaste
    {
        internal static UnityEditor.Animations.AnimatorControllerLayer _layerClipboard;
        static UnityEditor.Animations.AnimatorController _controllerClipboard;

        static UnityEditor.Animations.AnimatorController GetController(object layerView) =>
            Traverse.Create(layerView).Field("m_Host").Property("animatorController")
                .GetValue<UnityEditor.Animations.AnimatorController>();

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnDrawLayer");

        [HarmonyPrefix]
        static void Prefix(object __instance, Rect rect, int index, bool selected, bool focused)
        {
            var evt = Event.current;
            if (evt.type != EventType.MouseUp || evt.button != 1 || !rect.Contains(evt.mousePosition)) return;

            evt.Use();
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Copy layer"), false,
                static data => CopyLayer(data), __instance);

            if (_layerClipboard != null)
            {
                menu.AddItem(new GUIContent("Paste layer"), false,
                    static data => PasteLayer(data), __instance);
                menu.AddItem(new GUIContent("Paste layer settings"), false,
                    static data => PasteLayerSettings(data), __instance);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste layer"));
                menu.AddDisabledItem(new GUIContent("Paste layer settings"));
            }

            menu.AddItem(new GUIContent("Delete layer"), false,
                static data => Traverse.Create(data).Method("DeleteLayer").GetValue(null), __instance);

            menu.ShowAsContext();
        }

        /* Snapshots the selected layer and its state machine to the layer clipboard and Unity's built-in pasteboard. */
        internal static void CopyLayer(object layerView)
        {
            var reorderableList = (ReorderableList)WindowPatchReflection.LayerListField.GetValue(layerView);
            var controller = GetController(layerView);
            _layerClipboard = reorderableList.list[reorderableList.index] as UnityEditor.Animations.AnimatorControllerLayer;
            _controllerClipboard = controller;
            Unsupported.CopyStateMachineDataToPasteboard(_layerClipboard.stateMachine, controller, reorderableList.index);
        }

        /* Duplicates the clipboard layer, promoting the pasted sub-SM and syncing parameters if cross-controller.
           appendToBottom: skip reorder + ClearUndo so caller can wrap multiple pastes in one undo group. */
        internal static void PasteLayer(object layerView, bool appendToBottom = false)
        {
            if (_layerClipboard == null) return;

            var reorderableList = (ReorderableList)WindowPatchReflection.LayerListField.GetValue(layerView);
            var controller = GetController(layerView);
            int targetIndex = appendToBottom ? controller.layers.Length : reorderableList.index + 1;
            string newName = controller.MakeUniqueLayerName(_layerClipboard.name);

            if (!appendToBottom) Undo.FlushUndoRecordObjects();

            controller.AddLayer(newName);
            var layers = controller.layers;
            int pastedIndex = layers.Length - 1;
            var pastedLayer = layers[pastedIndex];
            Unsupported.PasteToStateMachineFromPasteboard(pastedLayer.stateMachine, controller, pastedIndex, Vector3.zero);

            // Promote pasted SM from child wrapper to top-level
            var pastedSM = pastedLayer.stateMachine.stateMachines[0].stateMachine;
            pastedSM.name = newName;
            pastedLayer.stateMachine.stateMachines = new UnityEditor.Animations.ChildAnimatorStateMachine[0];
            UnityEngine.Object.DestroyImmediate(pastedLayer.stateMachine, true);
            pastedLayer.stateMachine = pastedSM;
            PasteLayerProperties(pastedLayer, _layerClipboard);

            if (!appendToBottom)
            {
                // Move to just below source layer
                for (int i = layers.Length - 1; i > targetIndex; i--)
                    layers[i] = layers[i - 1];
                layers[targetIndex] = pastedLayer;
            }
            controller.layers = layers;

            if (!appendToBottom)
            {
                // Prevent undo from leaving dangling sub-assets
                Undo.ClearUndo(controller);
            }

            // Cross-controller paste: sync referenced parameters
            if (controller != _controllerClipboard)
            {
                if (!appendToBottom) Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                SyncCrossControllerParams(controller, pastedSM);
                if (!appendToBottom) Undo.CollapseUndoOperations(group);
            }

            EditorUtility.SetDirty(controller);
            Traverse.Create(layerView).Property("selectedLayerIndex").SetValue(targetIndex);
        }

        /* Applies clipboard layer properties (mask, weight, blending) to the selected layer without touching its state machine. */
        internal static void PasteLayerSettings(object layerView)
        {
            if (_layerClipboard == null) return;
            var reorderableList = (ReorderableList)WindowPatchReflection.LayerListField.GetValue(layerView);
            var controller = GetController(layerView);
            var layers = controller.layers;
            PasteLayerProperties(layers[reorderableList.index], _layerClipboard);
            controller.layers = layers;
        }

        /* Copies avatar mask, blending mode, weight, IK pass, and sync settings from sourceLayer to destinationLayer. */
        static void PasteLayerProperties(UnityEditor.Animations.AnimatorControllerLayer destinationLayer, UnityEditor.Animations.AnimatorControllerLayer sourceLayer)
        {
            destinationLayer.avatarMask                = sourceLayer.avatarMask;
            destinationLayer.blendingMode              = sourceLayer.blendingMode;
            destinationLayer.defaultWeight             = sourceLayer.defaultWeight;
            destinationLayer.iKPass                    = sourceLayer.iKPass;
            destinationLayer.syncedLayerAffectsTiming  = sourceLayer.syncedLayerAffectsTiming;
            destinationLayer.syncedLayerIndex          = sourceLayer.syncedLayerIndex;
        }

        /* Recursively collects all parameters referenced by states and transitions in sm into queued, for cross-controller paste sync. */
        static void GatherSmParams(UnityEditor.Animations.AnimatorStateMachine sm,
            ref Dictionary<string, UnityEngine.AnimatorControllerParameter> src,
            ref Dictionary<string, UnityEngine.AnimatorControllerParameter> queued)
        {
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state.mirrorParameterActive      && src.ContainsKey(state.mirrorParameter))      queued[state.mirrorParameter]      = src[state.mirrorParameter];
                if (state.speedParameterActive       && src.ContainsKey(state.speedParameter))       queued[state.speedParameter]       = src[state.speedParameter];
                if (state.timeParameterActive        && src.ContainsKey(state.timeParameter))        queued[state.timeParameter]        = src[state.timeParameter];
                if (state.cycleOffsetParameterActive && src.ContainsKey(state.cycleOffsetParameter)) queued[state.cycleOffsetParameter] = src[state.cycleOffsetParameter];

                if (state.motion is UnityEditor.Animations.BlendTree blendTree)
                    GatherBtParams(blendTree, ref src, ref queued);
            }

            var transitions = new List<UnityEditor.Animations.AnimatorStateTransition>(sm.anyStateTransitions);
            foreach (var childState in sm.states)
                transitions.AddRange(childState.state.transitions);
            foreach (var transition in transitions)
                foreach (var cond in transition.conditions)
                    if (src.ContainsKey(cond.parameter))
                        queued[cond.parameter] = src[cond.parameter];

            foreach (var childStateMachine in sm.stateMachines)
                GatherSmParams(childStateMachine.stateMachine, ref src, ref queued);
        }

        /* Recursively collects all parameters referenced by a blend tree (blend params + direct child params) into queued. */
        static void GatherBtParams(UnityEditor.Animations.BlendTree blendTree,
            ref Dictionary<string, UnityEngine.AnimatorControllerParameter> src,
            ref Dictionary<string, UnityEngine.AnimatorControllerParameter> queued)
        {
            if (src.ContainsKey(blendTree.blendParameter))  queued[blendTree.blendParameter]  = src[blendTree.blendParameter];
            if (src.ContainsKey(blendTree.blendParameterY)) queued[blendTree.blendParameterY] = src[blendTree.blendParameterY];

            foreach (var childMotion in blendTree.children)
            {
                if (src.ContainsKey(childMotion.directBlendParameter))
                    queued[childMotion.directBlendParameter] = src[childMotion.directBlendParameter];
                if (childMotion.motion is UnityEditor.Animations.BlendTree childBlendTree)
                    GatherBtParams(childBlendTree, ref src, ref queued);
            }
        }

        static void SyncCrossControllerParams(UnityEditor.Animations.AnimatorController controller,
            UnityEditor.Animations.AnimatorStateMachine pastedSM)
        {
            Undo.RecordObject(controller, "Sync pasted layer parameters");

            var destParams = new Dictionary<string, UnityEngine.AnimatorControllerParameter>(controller.parameters.Length);
            foreach (var parameter in controller.parameters) destParams[parameter.name] = parameter;

            var srcParams = new Dictionary<string, UnityEngine.AnimatorControllerParameter>(_controllerClipboard.parameters.Length);
            foreach (var parameter in _controllerClipboard.parameters) srcParams[parameter.name] = parameter;

            var queued = new Dictionary<string, UnityEngine.AnimatorControllerParameter>(_controllerClipboard.parameters.Length);
            GatherSmParams(pastedSM, ref srcParams, ref queued);

            foreach (var parameter in queued.Values)
                if (!destParams.ContainsKey(parameter.name))
                    controller.AddParameter(parameter);
        }

        /* Imports all layers from templateController appended to the bottom, wrapped in a single undoable operation. */
        internal static void ImportAllLayersFromTemplate(
            UnityEditor.Animations.AnimatorController templateController, object targetLayerView)
        {
            Undo.SetCurrentGroupName("Import Template Layers");
            int undoGroup = Undo.GetCurrentGroup();
            Undo.FlushUndoRecordObjects();

            var layers = templateController.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                _layerClipboard = layers[i];
                _controllerClipboard = templateController;
                Unsupported.CopyStateMachineDataToPasteboard(layers[i].stateMachine, templateController, i);
                PasteLayer(targetLayerView, appendToBottom: true);
            }

            Undo.CollapseUndoOperations(undoGroup);
        }
    }

    // Layer list: WD indicator if all states have Write Defaults on, ! if empty
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerWDIndicator
    {
        static GUIStyle _labelStyle;
        internal static GUIStyle LabelStyle => _labelStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 9, alignment = TextAnchor.MiddleRight };

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnDrawLayer");

        [HarmonyPrefix]
        static void Prefix(object __instance, Rect rect, int index, bool selected, bool focused)
        {
            if (EditorApplication.isPlaying) return;

            var settings = AnimatorDefaultSettings.Load();
            if (!settings.showLayerWDIndicator) return;

            try
            {
                var layerViewHost = WindowPatchReflection.LayerViewHostField.GetValue(__instance);
                var controller = Traverse.Create(layerViewHost).Field("m_AnimatorController")
                    .GetValue<UnityEditor.Animations.AnimatorController>();
                if (controller == null || index >= controller.layers.Length) return;

                var stateMachine = controller.layers[index].stateMachine;
                float gearWidth = EditorStyles.iconButton.CalcSize(EditorGUIUtility.IconContent("d_SettingsIcon")).x;
                float maskOffset = controller.layers[index].avatarMask != null ? 15f : 0f;
                var labelRect = new Rect(rect.xMax - gearWidth - 4f - 4f - 32f - maskOffset, rect.yMin + 5f, 32f, 16f);

                if (stateMachine.states.Length == 0 && stateMachine.stateMachines.Length == 0)
                {
                    LabelStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                    EditorGUI.LabelField(labelRect, "empty", LabelStyle);
                    return;
                }

                int writeDefaultsOnCount = 0, writeDefaultsOffCount = 0;
                CountWD(stateMachine, ref writeDefaultsOnCount, ref writeDefaultsOffCount, settings.wdIncludeBlendTreeStates);

                if (writeDefaultsOnCount > 0 && writeDefaultsOffCount == 0)
                {
                    LabelStyle.normal.textColor = settings.layerWDColor;
                    EditorGUI.LabelField(labelRect, "WD", LabelStyle);
                }
                else if (writeDefaultsOnCount > 0 && writeDefaultsOffCount > 0)
                {
                    LabelStyle.normal.textColor = Color.cyan;
                    EditorGUI.LabelField(labelRect, "WD", LabelStyle);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Layer WD indicator error: {e}");
            }
        }

        /* Recursively tallies writeDefaultValues-on and -off state counts across sm and all nested sub state machines. Skips states whose motion is a BlendTree when includeBlendTrees is false. */
        internal static void CountWD(UnityEditor.Animations.AnimatorStateMachine sm, ref int writeDefaultsOnCount, ref int writeDefaultsOffCount, bool includeBlendTrees)
        {
            foreach (var childState in sm.states)
            {
                if (!includeBlendTrees && childState.state.motion is UnityEditor.Animations.BlendTree) continue;
                if (childState.state.writeDefaultValues) writeDefaultsOnCount++;
                else writeDefaultsOffCount++;
            }
            foreach (var childStateMachine in sm.stateMachines)
                CountWD(childStateMachine.stateMachine, ref writeDefaultsOnCount, ref writeDefaultsOffCount, includeBlendTrees);
        }
    }

    // Bottom bar: selection count, active mode label, clickable controller path
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchBottomBar
    {
        static GUIStyle _barLabelStyle;
        static GUIStyle BarLabelStyle => _barLabelStyle ??= new GUIStyle(EditorStyles.miniLabel);

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.AnimatorControllerToolType, "DoGraphBottomBar");

        [HarmonyPostfix]
        static void Postfix(object __instance, Rect nameRect)
        {
            try
            {
                var controller = WindowPatchReflection.AnimatorControllerGetter?.Invoke(__instance, null)
                    as UnityEditor.Animations.AnimatorController;
                if (controller == null) return;

                // Make existing controller path label clickable
                string controllerPath = AssetDatabase.GetAssetPath(controller);
                float controllerLabelWidth = EditorStyles.miniLabel.CalcSize(new GUIContent(controllerPath)).x + 18f;
                var controllerRect = new Rect(nameRect.xMax - controllerLabelWidth, nameRect.y, controllerLabelWidth, nameRect.height);
                EditorGUIUtility.AddCursorRect(controllerRect, MouseCursor.Link);

                var currentEvent = Event.current;
                if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && controllerRect.Contains(currentEvent.mousePosition))
                {
                    EditorGUIUtility.PingObject(controller);
                    if (currentEvent.clickCount == 2) Selection.activeObject = controller;
                    currentEvent.Use();
                }

                // Selection count label
                var bottomBarSettings = AnimatorDefaultSettings.Load();
                if (bottomBarSettings.showGraphFooter)
                {
                    int nodeCount = Selection.objects.OfType<UnityEditor.Animations.AnimatorState>().Count();
                    int transitionCount = Selection.objects.OfType<UnityEditor.Animations.AnimatorStateTransition>().Count();
                    var selectionContent = new GUIContent($"  {nodeCount} Nodes / {transitionCount} Transitions Selected");
                    float selectionWidth = BarLabelStyle.CalcSize(selectionContent).x;
                    DrawBarLabel(new Rect(nameRect.x, nameRect.y, selectionWidth, nameRect.height), selectionContent);
                }

                // Active mode label (centered)
                string modeText = GetModeText();
                if (!string.IsNullOrEmpty(modeText))
                {
                    var modeContent = new GUIContent(modeText);
                    float modeWidth = BarLabelStyle.CalcSize(modeContent).x;
                    float modeX = nameRect.x + (nameRect.width - modeWidth) * 0.5f;
                    DrawBarLabel(new Rect(modeX, nameRect.y, modeWidth, nameRect.height), modeContent);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Bottom bar error: {e}");
            }
        }

        /* Renders content as a miniLabel inside a GUILayout.BeginArea bounded by rect. */
        static void DrawBarLabel(Rect rect, GUIContent content)
        {
            GUILayout.BeginArea(rect);
            EditorGUILayout.LabelField(content, BarLabelStyle);
            GUILayout.EndArea();
        }

        static string GetModeText()
        {
            if (PatchStateChainTransition.ChainActive)              return "Chain Mode";
            if (PatchTransitionCopyPaste.PasteActive)               return $"Paste {PatchTransitionCopyPaste.ClipboardCount} Transition{(PatchTransitionCopyPaste.ClipboardCount == 1 ? "" : "s")}";
            if (PatchStateNodeMenu._multiTransitionSources != null) return "Multi Transition — click destination";
            if (PatchStateNodeMenu._redirectTransitions != null)    return "Redirect Transitions — click destination";
            if (PatchStateNodeMenu._replicateTransitions != null)   return "Replicate Transitions — click sources";
            return null;
        }
    }

    // Shared state and draw logic for compact layer mode
    internal static class PatchLayerCompact
    {
        internal static bool IsCompact;

        static readonly FieldInfo _stylesField =
            AccessTools.Field(WindowPatchReflection.LayerControllerViewType, "s_Styles");
        internal static readonly FieldInfo RenameOverlayField =
            AccessTools.Field(WindowPatchReflection.LayerControllerViewType, "renameOverlay");
        internal static readonly MethodInfo RenameEndMethod =
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "RenameEnd");
        static readonly MethodInfo _deleteLayerMethod =
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "DeleteLayer");
        static readonly MethodInfo _showLayerSettingsMethod =
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.Graphs.LayerSettingsWindow"),
                "ShowAtPosition",
                new[] { typeof(Rect), typeof(UnityEditor.Animations.AnimatorControllerLayer), typeof(int), typeof(UnityEditor.Animations.AnimatorController) });


        static bool _stylesLoaded;
        internal static GUIContent _addIcon;
        static GUIContent _settingsIcon;
        static GUIContent _sync, _syncTime, _ik, _additive, _mask;
        static GUIStyle _layerLabelStyle, _labelStyle;

        static GUIContent _compactIcon;
        internal static GUIContent CompactIcon => _compactIcon ??= EditorGUIUtility.IconContent("center@2x");

        internal static void EnsureStyles()
        {
            if (_stylesLoaded) return;
            var stylesObj = _stylesField?.GetValue(null);
            if (stylesObj == null) return;
            var stylesType   = stylesObj.GetType();
            _addIcon         = AccessTools.Field(stylesType, "addIcon")?.GetValue(stylesObj) as GUIContent;
            _settingsIcon    = AccessTools.Field(stylesType, "settingsIcon")?.GetValue(stylesObj) as GUIContent;
            _sync            = AccessTools.Field(stylesType, "sync")?.GetValue(stylesObj) as GUIContent;
            _syncTime        = AccessTools.Field(stylesType, "syncTime")?.GetValue(stylesObj) as GUIContent;
            _ik              = AccessTools.Field(stylesType, "ik")?.GetValue(stylesObj) as GUIContent;
            _additive        = AccessTools.Field(stylesType, "additive")?.GetValue(stylesObj) as GUIContent;
            _mask            = AccessTools.Field(stylesType, "mask")?.GetValue(stylesObj) as GUIContent;
            _layerLabelStyle = AccessTools.Field(stylesType, "layerLabel")?.GetValue(stylesObj) as GUIStyle;
            _labelStyle      = AccessTools.Field(stylesType, "label")?.GetValue(stylesObj) as GUIStyle;
            _stylesLoaded    = true;
        }

        internal static void DrawCompactLayer(Rect rect, int index, bool isActive, bool isFocused, object instance)
        {
            try
            {
                EnsureStyles();
                var currentEvent = Event.current;

                if (currentEvent.type == EventType.MouseUp && currentEvent.button == 1 && rect.Contains(currentEvent.mousePosition))
                {
                    currentEvent.Use();
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Copy layer"), false, () => PatchLayerCopyPaste.CopyLayer(instance));
                    if (PatchLayerCopyPaste._layerClipboard != null)
                    {
                        menu.AddItem(new GUIContent("Paste layer"), false,
                            () => PatchLayerCopyPaste.PasteLayer(instance));
                        menu.AddItem(new GUIContent("Paste layer settings"), false,
                            () => PatchLayerCopyPaste.PasteLayerSettings(instance));
                    }
                    else
                    {
                        menu.AddDisabledItem(new GUIContent("Paste layer"));
                        menu.AddDisabledItem(new GUIContent("Paste layer settings"));
                    }
                    menu.AddItem(new GUIContent("Delete layer"), false,
                        () => _deleteLayerMethod?.Invoke(instance, null));
                    menu.ShowAsContext();
                }

                var reorderableList = (ReorderableList)WindowPatchReflection.LayerListField.GetValue(instance);
                var layer = reorderableList?.list[index] as UnityEditor.Animations.AnimatorControllerLayer;
                if (layer == null) return;

                var labelStyle      = _labelStyle      ?? EditorStyles.label;
                var layerLabelStyle = _layerLabelStyle ?? EditorStyles.miniLabel;

                rect.yMin += 2f;
                rect.yMax -= 2f;

                // Settings button
                Vector2 settingsSize = EditorStyles.iconButton.CalcSize(_settingsIcon ?? GUIContent.none);
                var settingsRect = new Rect(rect.xMax - settingsSize.x - 4f, rect.yMin, settingsSize.x, rect.height);
                if (GUI.Button(settingsRect, _settingsIcon, EditorStyles.iconButton))
                {
                    var popupRect = settingsRect;
                    popupRect.x += 15f;
                    var controller = WindowPatchReflection.GetOpenController();
                    bool shown = _showLayerSettingsMethod != null &&
                        (bool)(_showLayerSettingsMethod.Invoke(null, new object[] { popupRect, layer, index, controller }) ?? false);
                    if (shown) GUIUtility.ExitGUI();
                }

                // WD indicator — overlay left of gear, no badge chain participation
                if (!EditorApplication.isPlaying && Event.current.type == EventType.Repaint)
                {
                    var wdSettings = AnimatorDefaultSettings.Load();
                    if (wdSettings.showLayerWDIndicator)
                    {
                        var stateMachine = layer.stateMachine;
                        string wdText; Color wdColor;
                        if (stateMachine.states.Length == 0 && stateMachine.stateMachines.Length == 0)
                        {
                            wdText = "empty"; wdColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                        }
                        else
                        {
                            int wdOn = 0, wdOff = 0;
                            PatchLayerWDIndicator.CountWD(stateMachine, ref wdOn, ref wdOff, wdSettings.wdIncludeBlendTreeStates);
                            wdText = wdOn > 0 ? "WD" : null;
                            wdColor = wdOn > 0 && wdOff == 0 ? wdSettings.layerWDColor : Color.cyan;
                        }
                        if (wdText != null)
                        {
                            var wdStyle = PatchLayerWDIndicator.LabelStyle;
                            float wdWidth = wdStyle.CalcSize(new GUIContent(wdText)).x;
                            var wdRect = new Rect(settingsRect.xMin - wdWidth - 4f, rect.yMin, wdWidth, rect.height);
                            wdStyle.normal.textColor = wdColor;
                            GUI.Label(wdRect, wdText, wdStyle);
                        }
                    }
                }

                // Badges — stack left of settings
                var badgeCursor = settingsRect;
                if (layer.syncedLayerIndex != -1)
                {
                    var badgeContent = layer.syncedLayerAffectsTiming ? _syncTime : _sync;
                    if (badgeContent != null)
                    {
                        Vector2 size = layerLabelStyle.CalcSize(badgeContent);
                        badgeCursor = new Rect(badgeCursor.xMin - size.x - 4f, rect.yMin, size.x, rect.height);
                        GUI.Label(badgeCursor, badgeContent, layerLabelStyle);
                    }
                }
                if (layer.iKPass && _ik != null)
                {
                    Vector2 size = layerLabelStyle.CalcSize(_ik);
                    badgeCursor = new Rect(badgeCursor.xMin - size.x - 4f, rect.yMin, size.x, rect.height);
                    GUI.Label(badgeCursor, _ik, layerLabelStyle);
                }
                if (layer.blendingMode == UnityEditor.Animations.AnimatorLayerBlendingMode.Additive && _additive != null)
                {
                    Vector2 size = layerLabelStyle.CalcSize(_additive);
                    badgeCursor = new Rect(badgeCursor.xMin - size.x - 4f, rect.yMin, size.x, rect.height);
                    GUI.Label(badgeCursor, _additive, layerLabelStyle);
                }
                if (layer.avatarMask != null && _mask != null)
                {
                    Vector2 size = layerLabelStyle.CalcSize(_mask);
                    badgeCursor = new Rect(badgeCursor.xMin - size.x - 4f, rect.yMin, size.x, rect.height);
                    GUI.Label(badgeCursor, _mask, layerLabelStyle);
                }

                // Name label
                var nameRect = Rect.MinMaxRect(rect.xMin, rect.yMin, badgeCursor.xMin - 4f, rect.yMax);

                // Rename overlay
                var renameOverlay = RenameOverlayField?.GetValue(instance);
                if (renameOverlay != null)
                {
                    var overlayTraverse  = Traverse.Create(renameOverlay);
                    bool isRenaming      = overlayTraverse.Method("IsRenaming").GetValue<bool>();
                    int  userData        = overlayTraverse.Property("userData").GetValue<int>();
                    bool waitingForDelay = overlayTraverse.Property("isWaitingForDelay").GetValue<bool>();

                    if (isRenaming && userData == index && !waitingForDelay)
                    {
                        if (nameRect.width >= 0f && nameRect.height >= 0f)
                        {
                            nameRect.x -= 2f;
                            overlayTraverse.Property("editFieldRect").SetValue(nameRect);
                        }
                        if (!overlayTraverse.Method("OnGUI").GetValue<bool>())
                            RenameEndMethod?.Invoke(instance, null);
                        return;
                    }
                }

                if (currentEvent.type == EventType.Repaint)
                    labelStyle.Draw(nameRect, layer.name, false, false, isActive, isFocused);
            }
            catch (ExitGUIException) { throw; }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Compact layer draw error: {e}");
            }
        }
    }

    // Draws compact-mode toggle button left of the + button in the layer toolbar
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerCompactButton
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnToolbarGUI");

        [HarmonyPostfix]
        static void Postfix(object __instance)
        {
            try
            {
                PatchLayerCompact.EnsureStyles();
                var toolbarRect      = GUILayoutUtility.GetLastRect();
                var icon             = PatchLayerCompact.CompactIcon;
                Vector2 iconSize     = EditorStyles.iconButton.CalcSize(icon);
                float addButtonWidth = PatchLayerCompact._addIcon != null
                    ? EditorStyles.iconButton.CalcSize(PatchLayerCompact._addIcon).x
                    : iconSize.x;
                var buttonRect = new Rect(
                    toolbarRect.xMax - addButtonWidth - 10f - 4f - iconSize.x,
                    toolbarRect.y + (int)((toolbarRect.height - iconSize.y) * 0.5f),
                    iconSize.x, iconSize.y);

                bool newCompact = GUI.Toggle(buttonRect, PatchLayerCompact.IsCompact, icon, EditorStyles.iconButton);
                if (newCompact != PatchLayerCompact.IsCompact)
                    PatchLayerCompact.IsCompact = newCompact;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Layer compact button error: {e}");
            }
        }
    }

    // Applies compact element height and draw callback after ResetUI rebuilds the list
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerCompactDraw
    {
        internal static ReorderableList.ElementCallbackDelegate OriginalDrawCallback;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            var reorderableList = WindowPatchReflection.LayerListField?.GetValue(__instance) as ReorderableList;
            if (reorderableList == null) return;

            if (PatchLayerCompact.IsCompact)
            {
                if (OriginalDrawCallback == null)
                    OriginalDrawCallback = reorderableList.drawElementCallback;
                reorderableList.elementHeight = 22f;
                var capturedInstance = __instance;
                reorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
                    PatchLayerCompact.DrawCompactLayer(rect, index, isActive, isFocused, capturedInstance);
            }
            else if (OriginalDrawCallback != null)
            {
                reorderableList.elementHeight = 40f;
                reorderableList.drawElementCallback = OriginalDrawCallback;
                OriginalDrawCallback = null;
            }
        }
    }

    // Caches VRC expression parameter sync state for the last qualifying avatar + open controller.
    // Icons persist when clicking non-avatar objects. Rebuilds only when a different qualifying avatar is selected.
    internal static class VRCSyncCache
    {
        static GameObject _cachedAvatarRoot;
        static Dictionary<string, bool> _syncMap;
        static Dictionary<string, VRCExpressionParameters.ValueType> _valueTypeMap;
        static bool _isVrcFurySource;
        static VRCExpressionParameters _vrcFuryParams;

        static VRCSyncCache()
        {
            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            ObjectChangeEvents.changesPublished += OnObjectChanged;
        }

        static void OnSelectionChanged()
        {
            var activeGO = Selection.activeGameObject;
            if (activeGO == null) return;

            var avatarDescriptor = activeGO.GetComponentInParent<VRCAvatarDescriptor>();
            if (avatarDescriptor == null) return;

            if (ReferenceEquals(avatarDescriptor.gameObject, _cachedAvatarRoot)) return;

            Rebuild(avatarDescriptor);
        }

        static void OnUndoRedo()
        {
            if (_cachedAvatarRoot == null) return;
            var avatarDescriptor = _cachedAvatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (avatarDescriptor != null) Rebuild(avatarDescriptor);
        }

        static void OnObjectChanged(ref ObjectChangeEventStream stream)
        {
            if (_cachedAvatarRoot == null) return;
            var avatarDescriptor = _cachedAvatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (avatarDescriptor?.expressionParameters == null) return;

            int expressionParametersInstanceId = avatarDescriptor.expressionParameters.GetInstanceID();
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;
                stream.GetChangeAssetObjectPropertiesEvent(i, out var changeEvent);
                if (changeEvent.instanceId == expressionParametersInstanceId)
                {
                    Rebuild(avatarDescriptor);
                    return;
                }
            }
        }

        static void Rebuild(VRCAvatarDescriptor avatarDescriptor)
        {
            try
            {
                _syncMap = null;
                _valueTypeMap = null;
                _cachedAvatarRoot = null;
                _isVrcFurySource = false;
                _vrcFuryParams = null;

                var openController = WindowPatchReflection.GetOpenController();
                if (openController == null) return;

                bool controllerInAnimator = avatarDescriptor.GetComponent<Animator>()?.runtimeAnimatorController as UnityEditor.Animations.AnimatorController == openController;

                bool controllerInDescriptor = false;
                foreach (var layer in avatarDescriptor.baseAnimationLayers)
                    if (layer.animatorController == openController) { controllerInDescriptor = true; break; }
                if (!controllerInDescriptor)
                    foreach (var layer in avatarDescriptor.specialAnimationLayers)
                        if (layer.animatorController == openController) { controllerInDescriptor = true; break; }

                VRCExpressionParameters expressionParameters;
                if (controllerInAnimator || controllerInDescriptor)
                {
                    expressionParameters = avatarDescriptor.expressionParameters;
                }
                else
                {
                    var vrcFuryParams = FindVrcFuryParams(avatarDescriptor, openController);
                    if (vrcFuryParams == null) return;
                    expressionParameters = vrcFuryParams;
                    _isVrcFurySource = true;
                    _vrcFuryParams = vrcFuryParams;
                }

                if (expressionParameters?.parameters == null) return;

                _cachedAvatarRoot = avatarDescriptor.gameObject;
                _syncMap = new Dictionary<string, bool>(expressionParameters.parameters.Length);
                _valueTypeMap = new Dictionary<string, VRCExpressionParameters.ValueType>(expressionParameters.parameters.Length);
                foreach (var expressionParameter in expressionParameters.parameters)
                {
                    if (!string.IsNullOrEmpty(expressionParameter.name))
                    {
                        _syncMap[expressionParameter.name] = expressionParameter.networkSynced;
                        _valueTypeMap[expressionParameter.name] = expressionParameter.valueType;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] VRCSyncCache rebuild error: {e}");
            }
        }

        static VRCExpressionParameters FindVrcFuryParams(VRCAvatarDescriptor avatarDescriptor,
            UnityEditor.Animations.AnimatorController openController)
        {
            var vrcfuryType = AccessTools.TypeByName("VF.Model.VRCFury");
            if (vrcfuryType == null) return null;
            var getAllFeaturesMethod = AccessTools.Method(vrcfuryType, "GetAllFeatures");
            if (getAllFeaturesMethod == null) return null;

            foreach (var component in avatarDescriptor.GetComponentsInChildren(vrcfuryType, true))
            {
                if (component == null) continue;

                var features = getAllFeaturesMethod.Invoke(component, null) as System.Collections.IEnumerable;
                if (features == null) continue;

                foreach (var feature in features)
                {
                    if (feature?.GetType().FullName != "VF.Model.Feature.FullController") continue;

                    var featureType = feature.GetType();
                    var controllers = AccessTools.Field(featureType, "controllers")?.GetValue(feature) as System.Collections.IEnumerable;
                    if (controllers == null) continue;

                    bool found = false;
                    foreach (var entry in controllers)
                    {
                        if (entry == null) continue;
                        var guidController = AccessTools.Field(entry.GetType(), "controller")?.GetValue(entry);
                        if (guidController == null) continue;
                        var controller = AccessTools.Field(guidController.GetType(), "objRef")?.GetValue(guidController)
                            as UnityEditor.Animations.AnimatorController;
                        if (controller == openController) { found = true; break; }
                    }

                    if (!found) continue;

                    var prms = AccessTools.Field(featureType, "prms")?.GetValue(feature) as System.Collections.IEnumerable;
                    if (prms == null) return null;

                    foreach (var prmsEntry in prms)
                    {
                        if (prmsEntry == null) continue;
                        var guidParams = AccessTools.Field(prmsEntry.GetType(), "parameters")?.GetValue(prmsEntry);
                        if (guidParams == null) continue;
                        var expressionParams = AccessTools.Field(guidParams.GetType(), "objRef")?.GetValue(guidParams)
                            as VRCExpressionParameters;
                        if (expressionParams != null) return expressionParams;
                    }

                    return null;
                }
            }

            return null;
        }

        internal static bool TryGetSync(string paramName, out bool synced)
        {
            synced = false;
            if (_syncMap == null) return false;
            return _syncMap.TryGetValue(paramName, out synced);
        }

        internal static bool TryGetVrcValueType(string paramName, out VRCExpressionParameters.ValueType valueType)
        {
            valueType = default;
            if (_valueTypeMap == null) return false;
            return _valueTypeMap.TryGetValue(paramName, out valueType);
        }

        internal static VRCExpressionParameters GetExpressionParameters()
        {
            if (_cachedAvatarRoot == null) return null;
            var avatarDescriptor = _cachedAvatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (avatarDescriptor == null) return null;

            var openController = WindowPatchReflection.GetOpenController();
            if (openController == null) return null;

            if (_isVrcFurySource) return _vrcFuryParams;

            bool controllerMatches = avatarDescriptor.GetComponent<Animator>()?.runtimeAnimatorController
                as UnityEditor.Animations.AnimatorController == openController;
            if (!controllerMatches)
                foreach (var layer in avatarDescriptor.baseAnimationLayers)
                    if (layer.animatorController == openController) { controllerMatches = true; break; }
            if (!controllerMatches)
                foreach (var layer in avatarDescriptor.specialAnimationLayers)
                    if (layer.animatorController == openController) { controllerMatches = true; break; }

            return controllerMatches ? avatarDescriptor.expressionParameters : null;
        }
    }

    // Caches the last selected scene GO with an Animator.
    // On state node selection, calls EditAnimationClip (which PatchEditAnimationClipGOContext upgrades to GO context).
    [InitializeOnLoad]
    internal static class PatchStateNodeClipSync
    {
        static readonly MethodInfo EditAnimationClipMethod =
            AccessTools.Method(typeof(AnimationWindow), "EditAnimationClip", new Type[] { typeof(AnimationClip) });

        internal static GameObject CachedAnimatorGameObject;

        static PatchStateNodeClipSync()
        {
            Selection.selectionChanged += OnSelectionChanged;
        }

        static void OnSelectionChanged()
        {
            var activeGameObject = Selection.activeGameObject;
            if (activeGameObject != null
                && !EditorUtility.IsPersistent(activeGameObject)
                && activeGameObject.GetComponentInParent<Animator>(true) != null)
                CachedAnimatorGameObject = activeGameObject;

            if (Selection.activeObject is not UnityEditor.Animations.AnimatorState selectedState) return;
            if (selectedState.motion is not AnimationClip clip) return;

            var animationWindow = Resources.FindObjectsOfTypeAll<AnimationWindow>().FirstOrDefault();
            if (animationWindow == null) return;

            try { EditAnimationClipMethod?.Invoke(animationWindow, new object[] { clip }); }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] Clip sync error: {e}"); }
        }
    }

    // Postfix on EditAnimationClip: upgrades clip-only context to GO context when a cached GO is available.
    // Covers both state node clicks (via PatchStateNodeClipSync) and blend tree leaf node clicks (Unity native).
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchEditAnimationClipGOContext
    {
        static readonly MethodInfo EditGameObjectMethod =
            AccessTools.Method(typeof(AnimationWindow), "EditGameObject", new Type[] { typeof(GameObject) });

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(AnimationWindow), "EditAnimationClip", new Type[] { typeof(AnimationClip) });

        [HarmonyPostfix]
        static void Postfix(AnimationWindow __instance, AnimationClip animationClip)
        {
            var animatorGameObject = GetOrFindAnimatorGameObject();
            if (animatorGameObject == null) return;
            try
            {
                EditGameObjectMethod?.Invoke(__instance, new object[] { animatorGameObject });
                Traverse.Create(__instance).Property("state").Property("activeAnimationClip").SetValue(animationClip);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] GO context upgrade error: {e}");
            }
        }

        static GameObject GetOrFindAnimatorGameObject()
        {
            if (PatchStateNodeClipSync.CachedAnimatorGameObject != null)
                return PatchStateNodeClipSync.CachedAnimatorGameObject;

            var openController = WindowPatchReflection.GetOpenController();
            if (openController == null) return null;

            foreach (var animator in UnityEngine.Object.FindObjectsOfType<Animator>(true))
            {
                if (animator.runtimeAnimatorController == openController
                    && !EditorUtility.IsPersistent(animator.gameObject))
                {
                    PatchStateNodeClipSync.CachedAnimatorGameObject = animator.gameObject;
                    return PatchStateNodeClipSync.CachedAnimatorGameObject;
                }
            }
            return null;
        }
    }

    // Format clip dropdown with '.' → '/' so clips appear as nested submenus + inject "Create New Clip..."
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchClipMenuHierarchy
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.AnimationWindowClipPopup"),
                "GetClipMenuContent");

        [HarmonyPostfix]
        static void Postfix(ref GUIContent[] __result)
        {
            if (!AnimatorDefaultSettings.Load().clipMenuHierarchyEnabled) return;
            if (__result == null) return;

            // Detect Unity's separator + "Create New Clip..." tail (added when canCreateClips).
            // Must check BEFORE replacing dots — "Create New Clip..." contains dots that would
            // become slashes and corrupt the entry into a nested submenu path.
            bool unityAddedCreate = __result.Length >= 2
                && __result[^2] == GUIContent.none
                && __result[^1]?.text == "Create New Clip...";

            int clipCount = unityAddedCreate ? __result.Length - 2 : __result.Length;
            for (int i = 0; i < clipCount; i++)
            {
                if (__result[i]?.text is { Length: > 0 } text)
                    __result[i] = new GUIContent(text.Replace('.', '/'), __result[i].tooltip);
            }

            if (unityAddedCreate) return;

            var withCreate = new GUIContent[__result.Length + 2];
            __result.CopyTo(withCreate, 0);
            withCreate[__result.Length] = GUIContent.none;
            withCreate[__result.Length + 1] = new GUIContent("Create New Clip...");
            __result = withCreate;
        }
    }

    // DoClipPopup has no clickCount guard — double-click fires DisplayClipMenu twice, causing nested menu
    [HarmonyPatch]
    internal static class PatchClipMenuDoubleClickGuard
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("UnityEditor.AnimationWindowClipPopup"),
                "DisplayClipMenu");

        [HarmonyPrefix]
        static bool Prefix() =>
            !AnimatorDefaultSettings.Load().clipMenuHierarchyEnabled ||
            Event.current.type != EventType.MouseDown || Event.current.clickCount <= 1;
    }

    internal static class HierarchyContextMenu
    {
        [MenuItem("GameObject/Find Animation Uses", false, 0)]
        static void FindAnimationUses()
        {
            var gameObject = Selection.activeGameObject;
            var animator = gameObject.GetComponentInParent<Animator>();
            var controller = (animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController)
                ?? WindowPatchReflection.GetOpenController();
            var relativePath = GetRelativePath(animator.transform, gameObject.transform);
            if (relativePath == null) return;
            AnimatorFindUsageWindow.Open(relativePath, controller, gameObject.name);
        }

        [MenuItem("GameObject/Find Animation Uses", true)]
        static bool FindAnimationUsesValidate()
        {
            var gameObject = Selection.activeGameObject;
            if (gameObject == null) return false;
            var animator = gameObject.GetComponentInParent<Animator>();
            if (animator == null) return false;
            if ((animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController) != null) return true;
            var activeController = WindowPatchReflection.GetOpenController();
            if (activeController == null) return false;
            var descriptor = gameObject.GetComponentInParent<VRCAvatarDescriptor>();
            if (descriptor == null) return false;
            return descriptor.baseAnimationLayers.Concat(descriptor.specialAnimationLayers)
                .Any(layer => layer.animatorController as UnityEditor.Animations.AnimatorController == activeController);
        }

        static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root) return "";
            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }
            return current == null ? null : string.Join("/", parts);
        }
    }
}
#endif
