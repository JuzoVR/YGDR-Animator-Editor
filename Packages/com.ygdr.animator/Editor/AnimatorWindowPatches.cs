#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;


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
            if (!AnimatorDefaultSettings.Load().fixLayerScrollReset) return;

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
            if (!AnimatorDefaultSettings.Load().scrollToNewParameter) return;
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
                bool hasSyncData = VRCSyncCache.TryGetSync(parameter.name, out bool isSynced);

                const float labelWidth = 66f;
                const float iconSize = 14f;
                const float iconPadding = 2f;

                if (settings.showParamTypeLabels)
                {
                    var resolvedColor = parameter.type switch
                    {
                        UnityEngine.AnimatorControllerParameterType.Float   => settings.paramColorFloat,
                        UnityEngine.AnimatorControllerParameterType.Int     => settings.paramColorInt,
                        UnityEngine.AnimatorControllerParameterType.Bool    => settings.paramColorBool,
                        UnityEngine.AnimatorControllerParameterType.Trigger => settings.paramColorTrigger,
                        _ => Color.white
                    };
                    bool hasMismatch = VRCSyncCache.TryGetVrcValueType(parameter.name, out var vrcValueType) && !VrcTypesMatch(parameter.type, vrcValueType);
                    if (hasMismatch)
                        resolvedColor = new Color(0.5f, 0.5f, 0.5f);
                    TypeStyle.normal.textColor = resolvedColor;

                    float adjustedLabelWidth = hasSyncData ? labelWidth - iconSize - iconPadding - 6f : labelWidth - 6f;
                    var labelRect = new Rect(rect.xMax - labelWidth * 2f, rect.y, adjustedLabelWidth, rect.height);
                    if (hasMismatch)
                    {
                        var vrcColor = vrcValueType switch
                        {
                            VRCExpressionParameters.ValueType.Float => settings.paramColorFloat,
                            VRCExpressionParameters.ValueType.Int   => settings.paramColorInt,
                            _                                        => settings.paramColorBool,
                        };
                        const float mismatchExtraWidth = 12f;
                        var mismatchLabelRect = new Rect(labelRect.x - mismatchExtraWidth, labelRect.y, adjustedLabelWidth + mismatchExtraWidth, labelRect.height);
                        GUI.Label(mismatchLabelRect, $"<color=#808080>{parameter.type}</color>/<color=#{ColorUtility.ToHtmlStringRGB(vrcColor)}>{vrcValueType}</color>", TypeStyle);
                    }
                    else
                    {
                        GUI.Label(labelRect, parameter.type.ToString(), TypeStyle);
                    }

                    if (hasSyncData)
                    {
                        var iconRect = new Rect(labelRect.xMax + iconPadding, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize);
                        GUI.Label(iconRect, isSynced ? SyncedIcon : UnsyncedIcon);
                    }
                }
                else if (hasSyncData)
                {
                    var iconRect = new Rect(rect.xMax - labelWidth * 2f, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize);
                    GUI.Label(iconRect, isSynced ? SyncedIcon : UnsyncedIcon);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Parameter row error: {e}");
            }
        }
    }

    // Right-click convert menu on ParameterControllerView.OnGUI (Element.OnGUI is Repaint-only)
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchParameterContextMenu
    {
        static readonly (string category, string name, UnityEngine.AnimatorControllerParameterType type)[] VrcParameters =
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

        /* Finds the ReorderableList field on a ParameterControllerView instance by type, since the field name is internal. */
        static ReorderableList FindParamList(object instance)
        {
            foreach (var field in instance.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (field.FieldType == typeof(ReorderableList))
                {
                    if (field.GetValue(instance) is ReorderableList reorderableList)
                        return reorderableList;
                }
            }
            return null;
        }

        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.ParameterControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            var currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseUp || currentEvent.button != 1) return;

            var reorderableList = FindParamList(__instance);
            if (reorderableList == null || reorderableList.index < 0) return;

            var controller = WindowPatchReflection.GetOpenController();
            if (controller == null || reorderableList.index >= controller.parameters.Length) return;

            var parameter = controller.parameters[reorderableList.index];
            var capturedIndex = reorderableList.index;

            currentEvent.Use();
            var menu = new GenericMenu();
            foreach (UnityEngine.AnimatorControllerParameterType type in
                     Enum.GetValues(typeof(UnityEngine.AnimatorControllerParameterType)))
            {
                var capturedAddType = type;
                menu.AddItem(new GUIContent($"Add Parameter above/{type}"), false, () =>
                    AddParameterAbove(controller, capturedIndex, capturedAddType));
            }
            var existingParamNames = new HashSet<string>(controller.parameters.Select(p => p.name));
            foreach (var (category, vrcParamName, vrcParamType) in VrcParameters)
            {
                bool alreadyExists = existingParamNames.Contains(vrcParamName);
                var content = new GUIContent($"VRC/{category}/{vrcParamName}");
                if (alreadyExists)
                {
                    menu.AddDisabledItem(content, true);
                }
                else
                {
                    var capturedName = vrcParamName;
                    var capturedType = vrcParamType;
                    menu.AddItem(content, false, () =>
                        WindowPatchReflection.InsertParameterAtIndex(controller, capturedIndex, capturedName, capturedType));
                }
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

            menu.ShowAsContext();
        }

        static void AddParameterAbove(UnityEditor.Animations.AnimatorController controller, int index,
            UnityEngine.AnimatorControllerParameterType type)
        {
            string baseName = type.ToString();
            string paramName = baseName;
            var existingNames = new HashSet<string>(controller.parameters.Select(parameter => parameter.name));
            int counter = 1;
            while (existingNames.Contains(paramName))
                paramName = $"{baseName} {counter++}";
            WindowPatchReflection.InsertParameterAtIndex(controller, index, paramName, type);
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
    }


    // Layer copy/paste via right-click context menu on each layer row
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerCopyPaste
    {
        static UnityEditor.Animations.AnimatorControllerLayer _layerClipboard;
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
        static void CopyLayer(object layerView)
        {
            var reorderableList = (ReorderableList)WindowPatchReflection.LayerListField.GetValue(layerView);
            var controller = GetController(layerView);
            _layerClipboard = reorderableList.list[reorderableList.index] as UnityEditor.Animations.AnimatorControllerLayer;
            _controllerClipboard = controller;
            Unsupported.CopyStateMachineDataToPasteboard(_layerClipboard.stateMachine, controller, reorderableList.index);
        }

        /* Duplicates the clipboard layer below the selected one, promoting the pasted sub-SM and syncing parameters if cross-controller. */
        static void PasteLayer(object layerView)
        {
            if (_layerClipboard == null) return;

            var reorderableList = (ReorderableList)WindowPatchReflection.LayerListField.GetValue(layerView);
            var controller = GetController(layerView);
            int targetIndex = reorderableList.index + 1;
            string newName = controller.MakeUniqueLayerName(_layerClipboard.name);
            Undo.FlushUndoRecordObjects();

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

            // Move to just below source layer
            for (int i = layers.Length - 1; i > targetIndex; i--)
                layers[i] = layers[i - 1];
            layers[targetIndex] = pastedLayer;
            controller.layers = layers;

            // Prevent undo from leaving dangling sub-assets
            Undo.ClearUndo(controller);

            // Cross-controller paste: sync referenced parameters
            if (controller != _controllerClipboard)
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
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

                Undo.CollapseUndoOperations(group);
            }

            EditorUtility.SetDirty(controller);
            Traverse.Create(layerView).Property("selectedLayerIndex").SetValue(targetIndex);
        }

        /* Applies clipboard layer properties (mask, weight, blending) to the selected layer without touching its state machine. */
        static void PasteLayerSettings(object layerView)
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
    }

    // Layer list: WD indicator if all states have Write Defaults on, ! if empty
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerWDIndicator
    {
        static GUIStyle _labelStyle;
        static GUIStyle LabelStyle => _labelStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 9 };

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
                var labelRect = new Rect(rect.x - 19f, rect.y + 15f, 18f, 18f);

                if (stateMachine.states.Length == 0 && stateMachine.stateMachines.Length == 0)
                {
                    LabelStyle.normal.textColor = settings.layerEmptyColor;
                    EditorGUI.LabelField(labelRect, "   !", LabelStyle);
                    return;
                }

                int writeDefaultsOnCount = 0, writeDefaultsOffCount = 0;
                CountWD(stateMachine, ref writeDefaultsOnCount, ref writeDefaultsOffCount);

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

        /* Recursively tallies writeDefaultValues-on and -off state counts across sm and all nested sub state machines. */
        static void CountWD(UnityEditor.Animations.AnimatorStateMachine sm, ref int writeDefaultsOnCount, ref int writeDefaultsOffCount)
        {
            foreach (var childState in sm.states)
            {
                if (childState.state.writeDefaultValues) writeDefaultsOnCount++;
                else writeDefaultsOffCount++;
            }
            foreach (var childStateMachine in sm.stateMachines)
                CountWD(childStateMachine.stateMachine, ref writeDefaultsOnCount, ref writeDefaultsOffCount);
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
                int nodeCount = Selection.objects.OfType<UnityEditor.Animations.AnimatorState>().Count();
                int transitionCount = Selection.objects.OfType<UnityEditor.Animations.AnimatorStateTransition>().Count();
                var selectionContent = new GUIContent($"  {nodeCount} Nodes / {transitionCount} Transitions Selected");
                float selectionWidth = BarLabelStyle.CalcSize(selectionContent).x;
                DrawBarLabel(new Rect(nameRect.x, nameRect.y, selectionWidth, nameRect.height), selectionContent);

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

    // Caches VRC expression parameter sync state for the last qualifying avatar + open controller.
    // Icons persist when clicking non-avatar objects. Rebuilds only when a different qualifying avatar is selected.
    internal static class VRCSyncCache
    {
        static GameObject _cachedAvatarRoot;
        static Dictionary<string, bool> _syncMap;
        static Dictionary<string, VRCExpressionParameters.ValueType> _valueTypeMap;

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

                var openController = WindowPatchReflection.GetOpenController();
                if (openController == null) return;

                bool controllerInAnimator = avatarDescriptor.GetComponent<Animator>()?.runtimeAnimatorController as UnityEditor.Animations.AnimatorController == openController;

                bool controllerInDescriptor = false;
                foreach (var layer in avatarDescriptor.baseAnimationLayers)
                    if (layer.animatorController == openController) { controllerInDescriptor = true; break; }
                if (!controllerInDescriptor)
                    foreach (var layer in avatarDescriptor.specialAnimationLayers)
                        if (layer.animatorController == openController) { controllerInDescriptor = true; break; }

                if (!controllerInAnimator && !controllerInDescriptor) return;

                var expressionParameters = avatarDescriptor.expressionParameters;
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
            return _cachedAvatarRoot.GetComponent<VRCAvatarDescriptor>()?.expressionParameters;
        }
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
