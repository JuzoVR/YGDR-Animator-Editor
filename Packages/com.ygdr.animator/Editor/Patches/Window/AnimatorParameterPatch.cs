#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.PhysBone.Components;
using VRC.SDKBase;

namespace YGDR.Editor.Animation
{
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
            try
            {
                var settings = AnimatorDefaultSettings.Load();
                if (!settings.scrollToNewParameter || settings.preventParameterScroll) return;
                Traverse.Create(__instance).Field("m_ScrollPosition").SetValue(new Vector2(0, 9001));
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchNewParameterScroll.Postfix: {e}"); }
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

        internal static AnimatorController ViewFrameController;
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
                        EditorApplication.delayCall += UnityEditorInternal.InternalEditorUtility.RepaintAllViews;
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

        internal static HashSet<string> GetClipUsedParams(AnimatorController controller)
        {
            int controllerId = controller.GetInstanceID();
            if (_clipCacheControllerId == controllerId && _clipUsedParams != null) return _clipUsedParams;
            _clipCacheControllerId = controllerId;
            _clipUsedParams = new HashSet<string>();
            foreach (var layer in controller.layers)
                CollectClipParams(layer.stateMachine, _clipUsedParams);
            return _clipUsedParams;
        }

        static void CollectClipParams(AnimatorStateMachine stateMachine, HashSet<string> result)
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
            if (motion is BlendTree blendTree)
                foreach (var childMotion in blendTree.children)
                    CollectMotionParams(childMotion.motion, result);
        }

        static bool VrcTypesMatch(AnimatorControllerParameterType animType, VRCExpressionParameters.ValueType vrcType) =>
            animType switch
            {
                AnimatorControllerParameterType.Float   => vrcType == VRCExpressionParameters.ValueType.Float,
                AnimatorControllerParameterType.Int     => vrcType == VRCExpressionParameters.ValueType.Int,
                AnimatorControllerParameterType.Bool    => vrcType == VRCExpressionParameters.ValueType.Bool,
                AnimatorControllerParameterType.Trigger => vrcType == VRCExpressionParameters.ValueType.Bool,
                _ => true
            };

        static readonly Dictionary<int, string> _elementParamNameCache = new();
        static readonly GUIContent _tempContent = new GUIContent();

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewElementType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            var parameter = Traverse.Create(__instance).Field("m_Parameter").GetValue<UnityEngine.AnimatorControllerParameter>();
            if (parameter != null)
                _elementParamNameCache[__instance.GetHashCode()] = parameter.name;
        }

        [HarmonyPostfix]
        static void Postfix(object __instance, Rect rect, int index, bool selected, bool focused)
        {
            try
            {
                var parameter = Traverse.Create(__instance).Field("m_Parameter").GetValue<UnityEngine.AnimatorControllerParameter>();
                if (parameter == null) return;

                if (_elementParamNameCache.TryGetValue(__instance.GetHashCode(), out var oldName) && oldName != parameter.name)
                {
                    var controller = ViewFrameController;
                    if (controller != null)
                        foreach (var layer in controller.layers)
                            AnimatorParameterOps.RemapDriverParametersInStateMachine(layer.stateMachine, oldName, parameter.name);
                    _elementParamNameCache[__instance.GetHashCode()] = parameter.name;
                    EditorApplication.delayCall += () => ActiveEditorTracker.sharedTracker.ForceRebuild();
                }

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

                if (hasSyncData && showVrcComponent)
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
                        AnimatorControllerParameterType.Float   => settings.paramColorFloat,
                        AnimatorControllerParameterType.Int     => settings.paramColorInt,
                        AnimatorControllerParameterType.Bool    => settings.paramColorBool,
                        AnimatorControllerParameterType.Trigger => settings.paramColorTrigger,
                        _ => Color.white
                    };

                    string typeText = parameter.type.ToString();
                    _tempContent.text = typeText;
                    float typeTextWidth = TypeStyle.CalcSize(_tempContent).x;

                    if (hasMismatch)
                    {
                        var vrcColor = vrcValueType switch
                        {
                            VRCExpressionParameters.ValueType.Float => settings.paramColorFloat,
                            VRCExpressionParameters.ValueType.Int   => settings.paramColorInt,
                            _                                        => settings.paramColorBool,
                        };

                        string vrcTypeText = vrcValueType.ToString();
                        _tempContent.text = vrcTypeText;
                        float vrcTypeWidth = TypeStyle.CalcSize(_tempContent).x;
                        TypeStyle.normal.textColor = vrcColor;
                        cursorX -= vrcTypeWidth;
                        GUI.Label(new Rect(cursorX, rect.y, vrcTypeWidth, rect.height), vrcTypeText, TypeStyle);

                        _tempContent.text = "/";
                        float sepWidth = TypeStyle.CalcSize(_tempContent).x;
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
                    VrcBuiltinStyle.normal.textColor = settings.paramColorVrcLabel;
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
        internal static readonly FieldInfo ParamListField =
            _viewType?.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f => f.FieldType == typeof(UnityEditorInternal.ReorderableList));

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(_viewType, "OnAddParameter");

        [HarmonyPrefix]
        static bool Prefix(object __instance, Rect buttonRect)
        {
            try
            {
                if (!AnimatorDefaultSettings.Load().parameterAddMenuEnabled) return true;
                var controller = WindowPatchReflection.GetOpenController();
                if (controller == null) return true;

                var reorderableList = ParamListField?.GetValue(__instance) as UnityEditorInternal.ReorderableList;
                int insertIndex = (reorderableList != null && reorderableList.index >= 0)
                    ? reorderableList.index + 1
                    : controller.parameters.Length;

                var menu = new GenericMenu();
                var capturedInstance = __instance;
                foreach (AnimatorControllerParameterType type in
                         Enum.GetValues(typeof(AnimatorControllerParameterType)))
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
                            AnimatorParameterOps.InsertParameterAtIndex(controller, insertIndex, capturedName, capturedType));
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

        internal static void InsertWithUniqueName(object instance, AnimatorController controller,
            int index, AnimatorControllerParameterType type)
        {
            string baseName = type.ToString();
            string paramName = baseName;
            var existingNames = new HashSet<string>(controller.parameters.Select(parameter => parameter.name));
            int counter = 1;
            while (existingNames.Contains(paramName))
                paramName = $"{baseName} {counter++}";
            AnimatorParameterOps.InsertParameterAtIndex(controller, index, paramName, type);

            WindowPatchReflection.ParameterRebuildListMethod?.Invoke(instance, null);
            var paramList = ParamListField?.GetValue(instance) as UnityEditorInternal.ReorderableList;
            if (paramList != null) paramList.index = index;
            var renameOverlay = WindowPatchReflection.ParameterRenameOverlayField?.GetValue(instance);
            if (renameOverlay == null) return;
            if (WindowPatchReflection.RenameOverlayIsRenamingMethod?.Invoke(renameOverlay, null) is true)
                WindowPatchReflection.ParameterRenameEndMethod?.Invoke(instance, null);
            WindowPatchReflection.RenameOverlayBeginRenameMethod?.Invoke(renameOverlay, new object[] { paramName, index, 0.1f });
        }
    }

    // Right-click convert menu on ParameterControllerView.OnGUI (Element.OnGUI is Repaint-only)
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchParameterContextMenu
    {
        internal static readonly (string category, string name, AnimatorControllerParameterType type)[] VrcParameters =
        {
            ("Local",    "IsLocal",              AnimatorControllerParameterType.Bool),
            ("Local",    "PreviewMode",          AnimatorControllerParameterType.Int),
            ("Speech",   "Viseme",               AnimatorControllerParameterType.Int),
            ("Speech",   "Voice",                AnimatorControllerParameterType.Float),
            ("IK",       "GestureLeft",          AnimatorControllerParameterType.Int),
            ("IK",       "GestureRight",         AnimatorControllerParameterType.Int),
            ("IK",       "AngularY",             AnimatorControllerParameterType.Float),
            ("IK",       "VelocityX",            AnimatorControllerParameterType.Float),
            ("IK",       "VelocityY",            AnimatorControllerParameterType.Float),
            ("IK",       "VelocityZ",            AnimatorControllerParameterType.Float),
            ("IK",       "VelocityMagnitude",    AnimatorControllerParameterType.Float),
            ("IK",       "Upright",              AnimatorControllerParameterType.Float),
            ("IK",       "Grounded",             AnimatorControllerParameterType.Bool),
            ("IK",       "Seated",               AnimatorControllerParameterType.Bool),
            ("IK",       "AFK",                  AnimatorControllerParameterType.Bool),
            ("IK",       "VRMode",               AnimatorControllerParameterType.Int),
            ("IK",       "InStation",            AnimatorControllerParameterType.Bool),
            ("IK",       "AvatarVersion",        AnimatorControllerParameterType.Int),
            ("Playable", "GestureLeftWeight",    AnimatorControllerParameterType.Float),
            ("Playable", "GestureRightWeight",   AnimatorControllerParameterType.Float),
            ("Playable", "TrackingType",         AnimatorControllerParameterType.Int),
            ("Playable", "MuteSelf",             AnimatorControllerParameterType.Bool),
            ("Playable", "Earmuffs",             AnimatorControllerParameterType.Bool),
            ("Playable", "ScaleModified",        AnimatorControllerParameterType.Bool),
            ("Playable", "ScaleFactor",          AnimatorControllerParameterType.Float),
            ("Playable", "ScaleFactorInverse",   AnimatorControllerParameterType.Float),
            ("Playable", "EyeHeightAsMeters",    AnimatorControllerParameterType.Float),
            ("Playable", "EyeHeightAsPercent",   AnimatorControllerParameterType.Float),
            ("Social",   "IsOnFriendsList",      AnimatorControllerParameterType.Bool),
            ("System",   "IsAnimatorEnabled",    AnimatorControllerParameterType.Bool),
        };

        static readonly Dictionary<int, string[]> _paramNameCache = new();
        static bool _isProcessingSiblingRenames;

        static PatchParameterContextMenu()
        {
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream stream) =>
            {
                for (int i = 0; i < stream.length; i++)
                {
                    if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;
                    stream.GetChangeAssetObjectPropertiesEvent(i, out var eventData);
                    if (EditorUtility.InstanceIDToObject(eventData.instanceId) is not AnimatorController controller) continue;
                    if (!_paramNameCache.TryGetValue(controller.GetInstanceID(), out var oldNames)) continue;
                    var newNames = controller.parameters.Select(parameter => parameter.name).ToArray();
                    if (oldNames.Length != newNames.Length) { _paramNameCache[controller.GetInstanceID()] = newNames; continue; }
                    // Pure reorder: same set of names, different positions — don't treat as rename
                    if (oldNames.OrderBy(n => n).SequenceEqual(newNames.OrderBy(n => n))) { _paramNameCache[controller.GetInstanceID()] = newNames; continue; }
                    for (int j = 0; j < newNames.Length; j++)
                    {
                        if (newNames[j] == oldNames[j]) continue;
                        foreach (var layer in controller.layers)
                            AnimatorParameterOps.RemapDriverParametersInStateMachine(layer.stateMachine, oldNames[j], newNames[j]);
                        if (PatchParameterRow.GetVrcComponentUsedParams().Contains(oldNames[j]))
                            AnimatorFindUsageWindow.RemapVrcComponentParameters(oldNames[j], newNames[j]);
                        if (!_isProcessingSiblingRenames)
                            TryRenameSiblingVariants(controller, newNames, oldNames[j], newNames[j]);
                        EditorUtility.SetDirty(controller);
                    }
                    _paramNameCache[controller.GetInstanceID()] = newNames;
                }
            };
        }

        internal static UnityEditorInternal.ReorderableList FindParamList(object instance) =>
            PatchParameterAddMenu.ParamListField?.GetValue(instance) as UnityEditorInternal.ReorderableList;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            try
            {
            var viewController = WindowPatchReflection.GetOpenController();
            PatchParameterRow.ViewFrameController = viewController;
            PatchParameterRow.ViewFrameClipUsedParams = viewController != null
                ? PatchParameterRow.GetClipUsedParams(viewController)
                : null;

            if (viewController != null && !_paramNameCache.ContainsKey(viewController.GetInstanceID()))
                _paramNameCache[viewController.GetInstanceID()] = viewController.parameters.Select(parameter => parameter.name).ToArray();

            if (!AnimatorDefaultSettings.Load().parameterAddMenuEnabled) return;
            var currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseUp || currentEvent.button != 1) return;

            var reorderableList = FindParamList(__instance);
            if (reorderableList == null || reorderableList.index < 0) return;

            var controller = WindowPatchReflection.GetOpenController();
            if (controller == null) return;

            // reorderableList.index is the visual (filtered) index — derive actual parameter from the list item
            var listItem = reorderableList.index < reorderableList.list.Count
                ? reorderableList.list[reorderableList.index]
                : null;
            var parameter = listItem != null
                ? Traverse.Create(listItem).Field("m_Parameter").GetValue<AnimatorControllerParameter>()
                : null;
            if (parameter == null) return;

            // actual index in the unfiltered controller.parameters array
            var capturedIndex = Array.FindIndex(controller.parameters, p => p.name == parameter.name);
            if (capturedIndex < 0) return;
            var capturedInstance = __instance;

            var capturedScreenPos = GUIUtility.GUIToScreenPoint(currentEvent.mousePosition);
            currentEvent.Use();
            var menu = new GenericMenu();
            foreach (AnimatorControllerParameterType type in
                     Enum.GetValues(typeof(AnimatorControllerParameterType)))
            {
                var capturedAddType = type;
                menu.AddItem(new GUIContent($"Add Parameter below/{type}"), false, () =>
                    PatchParameterAddMenu.InsertWithUniqueName(capturedInstance, controller, capturedIndex + 1, capturedAddType));
            }
            menu.AddSeparator("");
            foreach (AnimatorControllerParameterType type in
                     Enum.GetValues(typeof(AnimatorControllerParameterType)))
            {
                if (type == parameter.type) continue;
                var capturedType = type;
                menu.AddItem(new GUIContent($"Convert to {type}"), false, () =>
                    AnimatorParameterOps.ConvertParameter(controller, capturedIndex, capturedType));
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
                        () => AnimatorParameterOps.SetVrcSynced(capturedExpressionParameters, capturedParamName, !capturedSynced));
                }
                else
                {
                    menu.AddItem(new GUIContent("Add to VRC Parameters"), false,
                        () => AnimatorParameterOps.AddToVrcParameters(capturedExpressionParameters, capturedParamName, capturedParamType));
                }

                var capturedController = controller;
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Add All to VRC Parameters"), false,
                    () => AnimatorParameterOps.AddAllToVrcParameters(capturedExpressionParameters, capturedController));
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
                var (remapController, fromParamName, screenPos) = ((AnimatorController, string, Vector2))data;
                EditorApplication.delayCall += () =>
                    new ParameterRemapDropdown(remapController, fromParamName).Show(new Rect(screenPos, Vector2.zero));
            }, (capturedFindController, capturedFindParameter.name, capturedScreenPos));
            menu.AddItem(new GUIContent("Delete and Clean"), false, static data =>
            {
                var (deleteController, deleteParamName) = ((AnimatorController, string))data;
                AnimatorParameterOps.DeleteParameterAndClean(deleteController, deleteParamName);
                EditorApplication.delayCall += UnityEditorInternal.InternalEditorUtility.RepaintAllViews;
            }, (capturedFindController, capturedFindParameter.name));
            menu.AddItem(new GUIContent("Remove Unused Parameters"), false, static data =>
            {
                AnimatorParameterOps.RemoveUnusedParameters((AnimatorController)data);
                EditorApplication.delayCall += UnityEditorInternal.InternalEditorUtility.RepaintAllViews;
            }, capturedFindController);

            menu.ShowAsContext();
            }
            catch (ExitGUIException) { throw; }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchParameterContextMenu.Prefix: {e}"); }
        }

        static void TryRenameSiblingVariants(AnimatorController controller, string[] newNames, string oldName, string newName)
        {
            string[] suffixes = null;
            string componentTypeName = null;
            string matchedSuffix = null;

            foreach (var suffix in AnimatorFindUsageWindow.PhysBoneSuffixes)
            {
                if (oldName.EndsWith(suffix, StringComparison.Ordinal) && newName.EndsWith(suffix, StringComparison.Ordinal))
                {
                    suffixes = AnimatorFindUsageWindow.PhysBoneSuffixes;
                    componentTypeName = "PhysBone";
                    matchedSuffix = suffix;
                    break;
                }
            }
            if (suffixes == null)
            {
                foreach (var suffix in AnimatorFindUsageWindow.RaycastSuffixes)
                {
                    if (oldName.EndsWith(suffix, StringComparison.Ordinal) && newName.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        suffixes = AnimatorFindUsageWindow.RaycastSuffixes;
                        componentTypeName = "Raycast";
                        matchedSuffix = suffix;
                        break;
                    }
                }
            }
            if (suffixes == null) return;

            string oldBase = oldName.Substring(0, oldName.Length - matchedSuffix.Length);
            string newBase = newName.Substring(0, newName.Length - matchedSuffix.Length);

            var siblings = new List<(int paramIndex, string oldSiblingName, string newSiblingName)>();
            for (int k = 0; k < newNames.Length; k++)
            {
                if (newNames[k] == oldName) continue;
                foreach (var suffix in suffixes)
                {
                    if (newNames[k] == oldBase + suffix)
                    {
                        siblings.Add((k, oldBase + suffix, newBase + suffix));
                        break;
                    }
                }
            }
            if (siblings.Count == 0) return;

            string siblingList = string.Join("\n", siblings.Select(sibling => $"{sibling.oldSiblingName}  →  {sibling.newSiblingName}"));
            bool confirmed = EditorUtility.DisplayDialog(
                "Rename Sibling Parameters",
                $"Renaming '{oldBase}' → '{newBase}' affects \n{siblings.Count} other {componentTypeName} parameter{(siblings.Count == 1 ? "" : "s")}:\n\n{siblingList}\n\nRename these too?",
                "Rename All",
                "Skip");
            if (!confirmed) return;

            foreach (var (paramIndex, _, newSiblingName) in siblings)
                newNames[paramIndex] = newSiblingName;
            _paramNameCache[controller.GetInstanceID()] = newNames;

            _isProcessingSiblingRenames = true;
            try
            {
                var serializedController = new SerializedObject(controller);
                var parametersProperty = serializedController.FindProperty("m_AnimatorParameters");
                foreach (var (_, oldSiblingName, newSiblingName) in siblings)
                {
                    for (int k = 0; k < parametersProperty.arraySize; k++)
                    {
                        var nameProperty = parametersProperty.GetArrayElementAtIndex(k).FindPropertyRelative("m_Name");
                        if (nameProperty.stringValue == oldSiblingName)
                        {
                            nameProperty.stringValue = newSiblingName;
                            break;
                        }
                    }
                }
                serializedController.ApplyModifiedProperties();

                foreach (var (_, oldSiblingName, newSiblingName) in siblings)
                {
                    AnimatorParameterOps.RemapParameter(controller, oldSiblingName, newSiblingName);
                    if (PatchParameterRow.GetVrcComponentUsedParams().Contains(oldSiblingName))
                        AnimatorFindUsageWindow.RemapVrcComponentParameters(oldSiblingName, newSiblingName);
                }

                EditorUtility.SetDirty(controller);
            }
            finally
            {
                _isProcessingSiblingRenames = false;
            }
        }

        internal static readonly HashSet<string> _vrcBuiltinNamesForBudget =
            new HashSet<string>(VrcParameters.Select(tuple => tuple.name));

        class ParameterRemapDropdown : AdvancedDropdown
        {
            readonly AnimatorController _controller;
            readonly string _fromParam;

            internal ParameterRemapDropdown(AnimatorController controller, string fromParam)
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
                => AnimatorParameterOps.RemapParameter(_controller, _fromParam, item.name);
        }
    }
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchParameterBudget
    {
        static GUIStyle _style;
        static GUIStyle Style => _style ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            richText   = true
        };

        static AnimatorController _controller;

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "OnToolbarGUI");

        [HarmonyPrefix]
        static void Prefix() => _controller = WindowPatchReflection.GetOpenController();

        [HarmonyPostfix]
        static void Postfix()
        {
            try
            {
            if (Event.current.type != EventType.Repaint) return;
            if (!AnimatorDefaultSettings.Load().showParamBudget) return;
            if (_controller == null) return;

            int controllerBits = 0;
            int syncedBits     = 0;
            var builtins       = PatchParameterContextMenu._vrcBuiltinNamesForBudget;

            foreach (var parameter in _controller.parameters)
            {
                if (builtins.Contains(parameter.name)) continue;
                int cost = parameter.type switch
                {
                    AnimatorControllerParameterType.Float   => 8,
                    AnimatorControllerParameterType.Int     => 8,
                    AnimatorControllerParameterType.Bool    => 1,
                    AnimatorControllerParameterType.Trigger => 1,
                    _ => 0
                };
                controllerBits += cost;
                if (VRCSyncCache.TryGetSync(parameter.name, out bool isSynced) && isSynced)
                {
                    int syncedCost = VRCSyncCache.TryGetVrcValueType(parameter.name, out VRCExpressionParameters.ValueType vrcType)
                        ? vrcType switch
                        {
                            VRCExpressionParameters.ValueType.Float => 8,
                            VRCExpressionParameters.ValueType.Int   => 8,
                            VRCExpressionParameters.ValueType.Bool  => 1,
                            _ => 0
                        }
                        : cost;
                    syncedBits += syncedCost;
                }
            }

            string text;
            float textWidth;
            bool hasSyncData = VRCSyncCache.GetExpressionParameters() != null;

            if (hasSyncData)
            {
                string syncedPart = syncedBits > 256
                    ? $"<color=#ff4444>{syncedBits}/256</color>"
                    : $"{syncedBits}/256";
                text      = $"{controllerBits} | {syncedPart}";
                textWidth = 110f;
            }
            else
            {
                text      = controllerBits > 256
                    ? $"<color=#ff4444>{controllerBits}/256</color>"
                    : $"{controllerBits}/256";
                textWidth = 64f;
            }

            var plusRect = GUILayoutUtility.GetLastRect();
            GUI.Label(new Rect(plusRect.x - textWidth - 18f, plusRect.y, textWidth, plusRect.height), text, Style);
            }
            catch (Exception e) { Debug.LogError($"[AnimatorTools] PatchParameterBudget.Postfix: {e}"); }
        }
    }
}
#endif
