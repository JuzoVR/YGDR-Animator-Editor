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
using ReorderableList = UnityEditorInternal.ReorderableList;

namespace YGDR.Editor.Animation
{
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerToolbar
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnToolbarGUI");

        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var interceptMethod = AccessTools.Method(typeof(PatchLayerToolbar), nameof(InterceptAddLayer));

            int addLayerIdx = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(WindowPatchReflection.AddNewLayerMethod))
                {
                    addLayerIdx = i;
                    break;
                }
            }

            if (addLayerIdx < 0)
            {
                Debug.LogError("[AnimatorTools] PatchLayerToolbar: AddNewLayer call not found in OnToolbarGUI");
                return list;
            }

            // Find next leave/leave.s to exit the try block (OnToolbarGUI wraps in using EditorGUI.DisabledScope)
            CodeInstruction leaveInstruction = null;
            for (int i = addLayerIdx + 1; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Leave || list[i].opcode == OpCodes.Leave_S)
                {
                    leaveInstruction = list[i];
                    break;
                }
            }

            if (leaveInstruction == null)
                Debug.LogError("[AnimatorTools] PatchLayerToolbar: leave instruction not found — transpiler incomplete");

            var result = new List<CodeInstruction>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (i == addLayerIdx)
                {
                    // Stack before: animatorControllerTool (from preceding ldloc)
                    // Inject ldarg.0 so InterceptAddLayer receives (tool, layerControllerView)
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Call, interceptMethod));
                    if (leaveInstruction != null)
                        result.Add(new CodeInstruction(leaveInstruction.opcode, leaveInstruction.operand));
                    continue;
                }
                result.Add(list[i]);
            }
            return result;
        }

        internal static void InterceptAddLayer(object animatorControllerTool, object layerControllerView)
        {
            try
            {
                if (!AnimatorDefaultSettings.Load().layerTemplateButtonEnabled)
                {
                    WindowPatchReflection.AddNewLayerMethod.Invoke(animatorControllerTool, null);
                    return;
                }

                var menu = new GenericMenu();

                menu.AddItem(new GUIContent("New Layer"), false, () =>
                {
                    WindowPatchReflection.AddNewLayerMethod.Invoke(animatorControllerTool, null);
                    UpdateListAndBeginRename(animatorControllerTool, layerControllerView);
                });

                menu.AddSeparator("");

                var templateControllers = LoadTemplateControllers();
                var packageTemplates    = templateControllers.Where(t => !t.isUser).ToList();
                var userTemplates       = templateControllers.Where(t => t.isUser).ToList();

                if (packageTemplates.Count == 0 && userTemplates.Count == 0)
                {
                    menu.AddDisabledItem(new GUIContent("(no templates)"));
                }
                else
                {
                    foreach (var (templateName, templateController, _) in packageTemplates)
                    {
                        var capturedController = templateController;
                        var capturedLayerView  = layerControllerView;
                        menu.AddItem(new GUIContent(templateName.Replace('.', '/')), false, () =>
                            AnimatorTemplateParameterWindow.Open(capturedController, capturedLayerView));
                    }

                    if (userTemplates.Count > 0)
                    {
                        if (packageTemplates.Count > 0) menu.AddSeparator("");
                        foreach (var (templateName, templateController, _) in userTemplates)
                        {
                            var capturedController = templateController;
                            var capturedLayerView  = layerControllerView;
                            menu.AddItem(new GUIContent($"User/{templateName.Replace('.', '/')}"), false, () =>
                                AnimatorTemplateParameterWindow.Open(capturedController, capturedLayerView));
                        }
                        menu.AddSeparator("");
                        foreach (var (templateName, templateController, _) in userTemplates)
                        {
                            string capturedDir  = System.IO.Path.GetDirectoryName(
                                AssetDatabase.GetAssetPath(templateController)).Replace('\\', '/');
                            string capturedName = templateName;
                            menu.AddItem(new GUIContent($"Delete User Template/{templateName.Replace('.', '/')}"), false, () =>
                            {
                                if (EditorUtility.DisplayDialog("Delete Template",
                                    $"Delete '{capturedName}' and all its clips? This cannot be undone.",
                                    "Delete", "Cancel"))
                                    AssetDatabase.DeleteAsset(capturedDir);
                            });
                        }
                    }
                }

                menu.ShowAsContext();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] PatchLayerToolbar: {e}");
            }
        }

        static void UpdateListAndBeginRename(object animatorControllerTool, object layerControllerView)
        {
            try
            {
                var animController = WindowPatchReflection.AnimatorControllerGetter
                    .Invoke(animatorControllerTool, null) as AnimatorController;
                var layerList = WindowPatchReflection.LayerListField.GetValue(layerControllerView) as ReorderableList;
                int newIndex = (int)WindowPatchReflection.SelectedLayerIndexProperty.GetValue(animatorControllerTool);

                layerList.list = animController.layers;
                layerList.index = newIndex;
                WindowPatchReflection.LayerSelectedIndexField?.SetValue(layerControllerView, newIndex);

                var renameOverlay = WindowPatchReflection.LayerRenameOverlayProperty.GetValue(layerControllerView);
                if ((bool)WindowPatchReflection.RenameOverlayIsRenamingMethod.Invoke(renameOverlay, null))
                    WindowPatchReflection.LayerRenameEndMethod.Invoke(layerControllerView, null);
                WindowPatchReflection.RenameOverlayBeginRenameMethod.Invoke(renameOverlay,
                    new object[] { animController.layers[newIndex].name, newIndex, 0.1f });
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] PatchLayerToolbar.UpdateListAndBeginRename: {e}");
            }
        }

        internal const string UserTemplatesPath = "Assets/YGDR Animator/User Templates";

        internal static List<(string name, AnimatorController controller, bool isUser)> _templateCache;

        [InitializeOnLoadMethod]
        static void ClearTemplateCache() => _templateCache = null;

        static List<(string name, AnimatorController controller, bool isUser)> LoadTemplateControllers()
        {
            if (_templateCache != null) return _templateCache;

            var result = new List<(string, AnimatorController, bool)>();

            var packageGuids = AssetDatabase.FindAssets("t:AnimatorController",
                new[] { "Packages/com.ygdr.animator/Templates" });
            foreach (var guid in packageGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller != null)
                    result.Add((controller.name, controller, false));
            }

            if (AssetDatabase.IsValidFolder(UserTemplatesPath))
            {
                var userGuids = AssetDatabase.FindAssets("t:AnimatorController", new[] { UserTemplatesPath });
                foreach (var guid in userGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                    if (controller != null)
                        result.Add((controller.name, controller, true));
                }
            }

            _templateCache = result;
            return _templateCache;
        }

    }
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchLayerRightClick
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(WindowPatchReflection.LayerControllerViewType, "OnGUI");

        [HarmonyPrefix]
        static void Prefix(object __instance)
        {
            // Template items merged into PatchLayerCopyPaste to avoid overriding copy/paste items.
        }
    }

    internal class AnimatorTemplateParameterWindow : EditorWindow
    {
        AnimatorController _templateController;
        object _targetLayerView;
        bool _renameParameters;
        string[] _renamedParameterNames;
        string[] _cachedParamLabels;
        Vector2 _scrollPosition;
        string _targetControllerPath;

        // Create mode
        bool               _isCreateMode;
        AnimatorController _sourceController;
        int                _sourceLayerIndex;
        string             _templateName;
        AnimatorControllerParameter[] _createModeParams;

        static Color s_hoverColor;
        static bool s_hoverColorValid;

        static GUIStyle s_columnHeaderStyle;
        static GUIStyle ColumnHeaderStyle => s_columnHeaderStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize  = 11,
            padding   = new RectOffset(4, 4, 0, 0),
            normal    = { textColor = Color.white }
        };

        static GUIStyle s_headerBtnLabelStyle;
        static GUIStyle HeaderBtnLabelStyle => s_headerBtnLabelStyle ??= new GUIStyle(GUIStyle.none)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize  = 11,
            padding   = new RectOffset(8, 8, 0, 0),
            normal    = { textColor = Color.white }
        };

        static GUIStyle s_confirmLabelStyle;
        static GUIStyle ConfirmLabelStyle => s_confirmLabelStyle ??= new GUIStyle(GUIStyle.none)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize  = 12,
            normal    = { textColor = Color.white }
        };

        internal static void InvalidateStyles()
        {
            s_headerBtnLabelStyle = null;
            s_confirmLabelStyle   = null;
            s_columnHeaderStyle   = null;
            s_hoverColorValid     = false;
        }

        static Color GetHoverColor()
        {
            if (s_hoverColorValid) return s_hoverColor;
            var accent = AnimationEditorWindow.Styles.AccentColor;
            s_hoverColor = new Color(accent.r + 0.1f, accent.g + 0.1f, accent.b + 0.1f, 1f);
            s_hoverColorValid = true;
            return s_hoverColor;
        }

        internal static void OpenCreate(AnimatorController controller, int layerIndex)
        {
            var window = CreateInstance<AnimatorTemplateParameterWindow>();
            window.titleContent       = new GUIContent("Create Template");
            window.minSize            = new Vector2(400, 280);
            window._isCreateMode      = true;
            window._sourceController  = controller;
            window._sourceLayerIndex  = layerIndex;
            window._templateName      = controller.layers[layerIndex].name;

            var qualifiedNames    = CollectLayerParams(controller, layerIndex);
            var parameters        = controller.parameters.Where(p => qualifiedNames.Contains(p.name)).ToArray();
            window._createModeParams       = parameters;
            window._renamedParameterNames  = parameters.Select(p => p.name).ToArray();
            window._cachedParamLabels      = BuildParamLabels(parameters);
            window.wantsMouseMove          = true;
            window.ShowUtility();
        }

        internal static void Open(AnimatorController templateController, object targetLayerView)
        {
            var window = CreateInstance<AnimatorTemplateParameterWindow>();
            window.titleContent = new GUIContent("Import Template");
            window.minSize = new Vector2(400, 280);
            window._templateController = templateController;
            window._targetLayerView = targetLayerView;
            window._renameParameters = false;
            var parameters = templateController.parameters;
            window._renamedParameterNames = parameters.Select(parameter => parameter.name).ToArray();
            window._cachedParamLabels     = BuildParamLabels(parameters);
            window.wantsMouseMove = true;
            var targetController = Traverse.Create(targetLayerView)
                .Field("m_Host").Property("animatorController").GetValue<AnimatorController>();
            window._targetControllerPath = targetController != null
                ? AssetDatabase.GetAssetPath(targetController) : "";
            window.ShowUtility();
        }

        void OnGUI()
        {
            try
            {
                if (Event.current.type == EventType.MouseMove) Repaint();
                DrawToggleHeader();
                DrawParameterList();
            }
            catch (ExitGUIException) { throw; }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] AnimatorTemplateParameterWindow.OnGUI: {e}");
            }
        }

        void DrawToggleHeader()
        {
            var headerRect = EditorGUILayout.GetControlRect(false, 28f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                bool hovered = headerRect.Contains(Event.current.mousePosition);
                EditorGUI.DrawRect(new Rect(0, headerRect.y, EditorGUIUtility.currentViewWidth, headerRect.height),
                    hovered ? GetHoverColor() : AnimationEditorWindow.Styles.AccentColor);
                string label = _renameParameters ? "  ✓  Rename Parameters" : "     Rename Parameters";
                GUI.Label(headerRect, label, HeaderBtnLabelStyle);
            }
            if (GUI.Button(headerRect, GUIContent.none, GUIStyle.none))
                _renameParameters = !_renameParameters;
            EditorGUIUtility.AddCursorRect(headerRect, MouseCursor.Link);
        }

        void DrawParameterList()
        {
            var parameters = _isCreateMode
                ? (_createModeParams ?? System.Array.Empty<AnimatorControllerParameter>())
                : (_templateController != null ? _templateController.parameters : System.Array.Empty<AnimatorControllerParameter>());

            GUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8f);
            var outerRect = EditorGUILayout.BeginVertical(AnimationEditorWindow.Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint && outerRect.height > 0)
                EditorGUI.DrawRect(outerRect, AnimationEditorWindow.Styles.PrimaryColor);

            DrawParamScrollView(parameters);
            DrawConfirmFooter();

            EditorGUILayout.EndVertical();
            GUILayout.Space(8f);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8f);
        }

        void DrawParamScrollView(AnimatorControllerParameter[] parameters)
        {
            const float middleGap          = 8f;
            const float columnHeaderHeight = 24f;
            const float rowPad             = 2f;
            float rowHeight   = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            float totalHeight = columnHeaderHeight + rowPad + Mathf.Max(parameters.Length, 1) * rowHeight;

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            var rect        = EditorGUILayout.GetControlRect(false, totalHeight);
            float halfWidth = (rect.width - middleGap) / 2f;

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(rect.x,                         rect.y, halfWidth, rect.height), AnimationEditorWindow.Styles.SecondaryColor);
                EditorGUI.DrawRect(new Rect(rect.x + halfWidth + middleGap, rect.y, halfWidth, rect.height), AnimationEditorWindow.Styles.SecondaryColor);
                EditorGUI.DrawRect(new Rect(rect.x,                         rect.y, halfWidth, columnHeaderHeight), AnimationEditorWindow.Styles.AccentColor);
                EditorGUI.DrawRect(new Rect(rect.x + halfWidth + middleGap, rect.y, halfWidth, columnHeaderHeight), AnimationEditorWindow.Styles.AccentColor);
            }

            GUI.Label(new Rect(rect.x + 4f,                         rect.y, halfWidth - 4f, columnHeaderHeight), "Parameter",                              ColumnHeaderStyle);
            GUI.Label(new Rect(rect.x + halfWidth + middleGap + 4f, rect.y, halfWidth - 4f, columnHeaderHeight), _isCreateMode ? "Export As" : "Import As", ColumnHeaderStyle);

            float rowY = rect.y + columnHeaderHeight + rowPad;
            if (parameters.Length == 0)
            {
                GUI.Label(new Rect(rect.x, rowY, halfWidth, rowHeight), "No parameters in template.", AnimationEditorWindow.Styles.EmptyLabel);
            }
            else
            {
                for (int i = 0; i < parameters.Length; i++, rowY += rowHeight)
                    DrawParamRow(i, rowY, rect, halfWidth, middleGap, rowHeight);
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawParamRow(int rowIndex, float rowY, Rect rect, float halfWidth, float middleGap, float rowHeight)
        {
            if (Event.current.type == EventType.Repaint && rowIndex % 2 == 1)
            {
                EditorGUI.DrawRect(new Rect(rect.x,                         rowY, halfWidth, rowHeight), AnimationEditorWindow.Styles.RowAltColor);
                EditorGUI.DrawRect(new Rect(rect.x + halfWidth + middleGap, rowY, halfWidth, rowHeight), AnimationEditorWindow.Styles.RowAltColor);
            }

            GUI.Label(
                new Rect(rect.x + 4f, rowY, halfWidth - 4f, rowHeight),
                _cachedParamLabels[rowIndex],
                AnimationEditorWindow.Styles.FindUsesHeader);

            using (new EditorGUI.DisabledScope(!_renameParameters))
                _renamedParameterNames[rowIndex] = EditorGUI.TextField(
                    new Rect(rect.x + halfWidth + middleGap + 2f, rowY + 1f, halfWidth - 4f, rowHeight - 2f),
                    _renamedParameterNames[rowIndex]);
        }

        void DrawConfirmFooter()
        {
            if (_isCreateMode)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Template Name", EditorStyles.centeredGreyMiniLabel);
                _templateName = EditorGUILayout.TextField(_templateName);
            }
            else if (!string.IsNullOrEmpty(_targetControllerPath))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(
                    $"Template clips copied to {System.IO.Path.GetDirectoryName(_targetControllerPath)}",
                    EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.Space(6f);
            bool canConfirm = !_isCreateMode || !string.IsNullOrWhiteSpace(_templateName);
            var containerRect = EditorGUILayout.GetControlRect(false, 28f);
            float btnWidth = containerRect.width * 0.8f;
            var btnRect = new Rect(
                containerRect.x + (containerRect.width - btnWidth) * 0.5f,
                containerRect.y,
                btnWidth,
                containerRect.height);
            using (new EditorGUI.DisabledScope(!canConfirm))
            {
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(btnRect, btnRect.Contains(Event.current.mousePosition)
                        ? GetHoverColor() : AnimationEditorWindow.Styles.AccentColor);
                    GUI.Label(btnRect, _isCreateMode ? "Create Template" : "Confirm", ConfirmLabelStyle);
                }
                if (GUI.Button(btnRect, GUIContent.none, GUIStyle.none))
                {
                    if (_isCreateMode) ConfirmCreate();
                    else               ConfirmImport();
                    Close();
                }
            }
            EditorGUIUtility.AddCursorRect(btnRect, MouseCursor.Link);
        }

        static string[] BuildParamLabels(AnimatorControllerParameter[] parameters)
        {
            var settings = AnimatorDefaultSettings.Load();
            var labels   = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                string typeHex = ColorUtility.ToHtmlStringRGB(parameters[i].type switch
                {
                    AnimatorControllerParameterType.Float   => settings.paramColorFloat,
                    AnimatorControllerParameterType.Int     => settings.paramColorInt,
                    AnimatorControllerParameterType.Bool    => settings.paramColorBool,
                    AnimatorControllerParameterType.Trigger => settings.paramColorTrigger,
                    _                                       => new Color(0.65f, 0.65f, 0.65f)
                });
                labels[i] = $"{parameters[i].name}  <color=#{typeHex}>{parameters[i].type}</color>";
            }
            return labels;
        }

        void ConfirmCreate()
        {
            try
            {
                string safeName    = _templateName.Trim().Replace('/', '.');
                string templateDir = $"{PatchLayerToolbar.UserTemplatesPath}/{safeName}";
                EnsureAssetFolder(templateDir);

                string sourcePath = AssetDatabase.GetAssetPath(_sourceController);
                string destPath   = AssetDatabase.GenerateUniqueAssetPath($"{templateDir}/{safeName}.controller");
                if (!AssetDatabase.CopyAsset(sourcePath, destPath))
                {
                    Debug.LogError($"[AnimatorTools] CreateTemplate: CopyAsset failed to {destPath}");
                    return;
                }

                var templateController = AssetDatabase.LoadAssetAtPath<AnimatorController>(destPath);
                if (templateController == null) return;

                int layerCount = templateController.layers.Length;
                for (int i = layerCount - 1; i > _sourceLayerIndex; i--)
                    templateController.RemoveLayer(i);
                for (int i = 0; i < _sourceLayerIndex; i++)
                    templateController.RemoveLayer(0);

                var qualifiedNames = CollectLayerParams(templateController, 0);
                foreach (var parameter in templateController.parameters.ToArray())
                    if (!qualifiedNames.Contains(parameter.name))
                        templateController.RemoveParameter(parameter);

                if (_renameParameters && _createModeParams != null)
                {
                    for (int i = 0; i < _createModeParams.Length && i < _renamedParameterNames.Length; i++)
                    {
                        string oldName = _createModeParams[i].name;
                        string newName = _renamedParameterNames[i];
                        if (string.IsNullOrEmpty(newName) || oldName == newName) continue;
                        UpdateParamRefsInSM(templateController.layers[0].stateMachine, oldName, newName);
                        var paramToRemove = System.Array.Find(templateController.parameters, p => p.name == oldName);
                        if (paramToRemove != null)
                        {
                            templateController.RemoveParameter(paramToRemove);
                            if (!templateController.parameters.Any(p => p.name == newName))
                                templateController.AddParameter(newName, _createModeParams[i].type);
                        }
                    }
                }

                var clipCache = new Dictionary<string, AnimationClip>();
                CopyClipsInSM(templateController.layers[0].stateMachine, templateDir, clipCache);

                EditorUtility.SetDirty(templateController);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] AnimatorTemplateParameterWindow.ConfirmCreate: {e}");
            }
        }

        static void EnsureAssetFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static HashSet<string> CollectLayerParams(AnimatorController controller, int layerIndex)
        {
            var paramNames = new HashSet<string>();
            CollectSMParams(controller.layers[layerIndex].stateMachine, paramNames);
            paramNames.Remove("");
            return paramNames;
        }

        static void CollectSMParams(AnimatorStateMachine sm, HashSet<string> paramNames)
        {
            foreach (var transition in sm.anyStateTransitions)
                foreach (var condition in transition.conditions)
                    paramNames.Add(condition.parameter);
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                foreach (var transition in state.transitions)
                    foreach (var condition in transition.conditions)
                        paramNames.Add(condition.parameter);
                if (state.speedParameterActive)       paramNames.Add(state.speedParameter);
                if (state.timeParameterActive)        paramNames.Add(state.timeParameter);
                if (state.mirrorParameterActive)      paramNames.Add(state.mirrorParameter);
                if (state.cycleOffsetParameterActive) paramNames.Add(state.cycleOffsetParameter);
                CollectMotionParamNames(state.motion, paramNames);
            }
            foreach (var childSM in sm.stateMachines)
                CollectSMParams(childSM.stateMachine, paramNames);
        }

        static void CollectMotionParamNames(UnityEngine.Motion motion, HashSet<string> paramNames)
        {
            if (motion is AnimationClip clip)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (binding.type == typeof(Animator))
                        paramNames.Add(binding.propertyName);
                return;
            }
            if (motion is BlendTree blendTree)
            {
                if (!string.IsNullOrEmpty(blendTree.blendParameter))  paramNames.Add(blendTree.blendParameter);
                if (!string.IsNullOrEmpty(blendTree.blendParameterY)) paramNames.Add(blendTree.blendParameterY);
                foreach (var child in blendTree.children)
                {
                    if (!string.IsNullOrEmpty(child.directBlendParameter)) paramNames.Add(child.directBlendParameter);
                    CollectMotionParamNames(child.motion, paramNames);
                }
            }
        }

        static void CopyClipsInSM(AnimatorStateMachine sm, string destDir, Dictionary<string, AnimationClip> clipCache)
        {
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state.motion is AnimationClip clip && AssetDatabase.IsMainAsset(clip))
                {
                    var copied = CopyClipToDir(clip, destDir, clipCache);
                    if (copied != null) { state.motion = copied; EditorUtility.SetDirty(state); }
                }
                else if (state.motion is BlendTree blendTree)
                    CopyClipsInBlendTree(blendTree, destDir, clipCache);
            }
            foreach (var childSM in sm.stateMachines)
                CopyClipsInSM(childSM.stateMachine, destDir, clipCache);
        }

        static void CopyClipsInBlendTree(BlendTree blendTree, string destDir, Dictionary<string, AnimationClip> clipCache)
        {
            var children = blendTree.children;
            bool modified = false;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is AnimationClip clip && AssetDatabase.IsMainAsset(clip))
                {
                    var copied = CopyClipToDir(clip, destDir, clipCache);
                    if (copied != null) { children[i].motion = copied; modified = true; }
                }
                else if (children[i].motion is BlendTree childBT)
                    CopyClipsInBlendTree(childBT, destDir, clipCache);
            }
            if (modified) { blendTree.children = children; EditorUtility.SetDirty(blendTree); }
        }

        static AnimationClip CopyClipToDir(AnimationClip clip, string destDir, Dictionary<string, AnimationClip> clipCache)
        {
            string sourcePath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(sourcePath)) return null;
            if (clipCache.TryGetValue(sourcePath, out var cached)) return cached;
            string destPath = AssetDatabase.GenerateUniqueAssetPath($"{destDir}/{clip.name}.anim");
            if (!AssetDatabase.CopyAsset(sourcePath, destPath)) return null;
            var copied = AssetDatabase.LoadAssetAtPath<AnimationClip>(destPath);
            clipCache[sourcePath] = copied;
            return copied;
        }

        void ConfirmImport()
        {
            try
            {
            ConfirmImportImpl();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] AnimatorTemplateParameterWindow.ConfirmImport: {e}");
            }
        }

        void ConfirmImportImpl()
        {
            if (_templateController == null || _targetLayerView == null) return;

            var targetController = Traverse.Create(_targetLayerView)
                .Field("m_Host").Property("animatorController")
                .GetValue<AnimatorController>();
            if (targetController == null) return;

            var existingParamNames = new HashSet<string>(
                targetController.parameters.Select(parameter => parameter.name));
            int layerCountBefore = targetController.layers.Length;

            Undo.SetCurrentGroupName("Import Template Layers");
            int undoGroup = Undo.GetCurrentGroup();

            PatchLayerCopyPaste.ImportAllLayersFromTemplate(_templateController, _targetLayerView);

            var newLayers = targetController.layers.Skip(layerCountBefore).ToArray();

            CreateLocalClipsForNewLayers(targetController, newLayers);
            SyncClipAAPParams(targetController, _templateController, newLayers);

            if (_renameParameters)
            {
                var templateParameters = _templateController.parameters;
                for (int i = 0; i < templateParameters.Length && i < _renamedParameterNames.Length; i++)
                {
                    string oldName = templateParameters[i].name;
                    string newName = _renamedParameterNames[i];
                    if (string.IsNullOrEmpty(newName) || oldName == newName) continue;

                    foreach (var newLayer in newLayers)
                        UpdateParamRefsInSM(newLayer.stateMachine, oldName, newName);

                    bool wasNewlyAdded = !existingParamNames.Contains(oldName)
                        && targetController.parameters.Any(parameter => parameter.name == oldName);

                    if (wasNewlyAdded)
                    {
                        Undo.RecordObject(targetController, "Rename Template Parameter");
                        var paramToRemove = System.Array.Find(targetController.parameters,
                            parameter => parameter.name == oldName);
                        targetController.RemoveParameter(paramToRemove);

                        if (!targetController.parameters.Any(parameter => parameter.name == newName))
                            targetController.AddParameter(newName, templateParameters[i].type);
                    }
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(targetController);
        }

        static void CreateLocalClipsForNewLayers(AnimatorController targetController,
            AnimatorControllerLayer[] newLayers)
        {
            string controllerPath = AssetDatabase.GetAssetPath(targetController);
            string controllerDir = System.IO.Path.GetDirectoryName(controllerPath).Replace('\\', '/');
            string controllerName = targetController.name;
            var clipCache = new Dictionary<string, AnimationClip>();

            foreach (var layer in newLayers)
                ReplaceClipsInSM(layer.stateMachine, controllerDir, controllerName, clipCache);
        }

        static void ReplaceClipsInSM(AnimatorStateMachine sm, string controllerDir, string controllerName,
            Dictionary<string, AnimationClip> clipCache)
        {
            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state.motion is AnimationClip clip)
                {
                    var localClip = CopyClipToControllerDir(clip, controllerDir, controllerName, clipCache);
                    if (localClip != null)
                    {
                        Undo.RecordObject(state, "Create Local Clip");
                        state.motion = localClip;
                        EditorUtility.SetDirty(state);
                    }
                }
                else if (state.motion is BlendTree blendTree)
                    ReplaceClipsInBlendTree(blendTree, controllerDir, controllerName, clipCache);
            }
            foreach (var childStateMachine in sm.stateMachines)
                ReplaceClipsInSM(childStateMachine.stateMachine, controllerDir, controllerName, clipCache);
        }

        static void ReplaceClipsInBlendTree(BlendTree blendTree, string controllerDir, string controllerName,
            Dictionary<string, AnimationClip> clipCache)
        {
            var children = blendTree.children;
            bool modified = false;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].motion is AnimationClip clip)
                {
                    var localClip = CopyClipToControllerDir(clip, controllerDir, controllerName, clipCache);
                    if (localClip != null)
                    {
                        if (!modified) { Undo.RecordObject(blendTree, "Create Local Clip"); modified = true; }
                        children[i].motion = localClip;
                    }
                }
                else if (children[i].motion is BlendTree childBT)
                    ReplaceClipsInBlendTree(childBT, controllerDir, controllerName, clipCache);
            }
            if (modified)
            {
                blendTree.children = children;
                EditorUtility.SetDirty(blendTree);
            }
        }

        static AnimationClip CopyClipToControllerDir(AnimationClip sourceClip, string controllerDir,
            string controllerName, Dictionary<string, AnimationClip> clipCache)
        {
            string sourcePath = AssetDatabase.GetAssetPath(sourceClip);
            if (string.IsNullOrEmpty(sourcePath)) return null;

            if (clipCache.TryGetValue(sourcePath, out var cached)) return cached;

            string newPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{controllerDir}/{controllerName}.{sourceClip.name}.anim");

            if (!AssetDatabase.CopyAsset(sourcePath, newPath)) return null;
            var localClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(newPath);
            clipCache[sourcePath] = localClip;
            return localClip;
        }

        static void SyncClipAAPParams(AnimatorController targetController,
            AnimatorController templateController, AnimatorControllerLayer[] newLayers)
        {
            var existingParamNames = new HashSet<string>(targetController.parameters.Select(parameter => parameter.name));
            var templateParamMap = templateController.parameters.ToDictionary(parameter => parameter.name, parameter => parameter);

            foreach (var layer in newLayers)
                SyncClipAAPParamsInSM(layer.stateMachine, targetController, templateParamMap, existingParamNames);
        }

        static void SyncClipAAPParamsInSM(AnimatorStateMachine sm, AnimatorController targetController,
            Dictionary<string, AnimatorControllerParameter> templateParamMap, HashSet<string> existingParamNames)
        {
            foreach (var childState in sm.states)
            {
                if (childState.state.motion is AnimationClip clip)
                    AddMissingClipAAPParams(clip, targetController, templateParamMap, existingParamNames);
                else if (childState.state.motion is BlendTree blendTree)
                    SyncClipAAPParamsInBlendTree(blendTree, targetController, templateParamMap, existingParamNames);
            }
            foreach (var childStateMachine in sm.stateMachines)
                SyncClipAAPParamsInSM(childStateMachine.stateMachine, targetController, templateParamMap, existingParamNames);
        }

        static void SyncClipAAPParamsInBlendTree(BlendTree blendTree, AnimatorController targetController,
            Dictionary<string, AnimatorControllerParameter> templateParamMap, HashSet<string> existingParamNames)
        {
            foreach (var childMotion in blendTree.children)
            {
                if (childMotion.motion is AnimationClip clip)
                    AddMissingClipAAPParams(clip, targetController, templateParamMap, existingParamNames);
                else if (childMotion.motion is BlendTree childBT)
                    SyncClipAAPParamsInBlendTree(childBT, targetController, templateParamMap, existingParamNames);
            }
        }

        static void AddMissingClipAAPParams(AnimationClip clip, AnimatorController targetController,
            Dictionary<string, AnimatorControllerParameter> templateParamMap, HashSet<string> existingParamNames)
        {
            bool recorded = false;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Animator)) continue;
                string paramName = binding.propertyName;
                if (existingParamNames.Contains(paramName)) continue;

                var paramType = templateParamMap.TryGetValue(paramName, out var templateParam)
                    ? templateParam.type
                    : AnimatorControllerParameterType.Float;

                if (!recorded) { Undo.RecordObject(targetController, "Add Template AAP"); recorded = true; }
                targetController.AddParameter(paramName, paramType);
                existingParamNames.Add(paramName);
            }
        }

        static void UpdateParamRefsInSM(AnimatorStateMachine sm, string oldName, string newName)
        {
            foreach (var anyStateTransition in sm.anyStateTransitions)
                UpdateTransitionConditions(anyStateTransition, oldName, newName);

            foreach (var childState in sm.states)
            {
                var state = childState.state;

                foreach (var transition in state.transitions)
                    UpdateTransitionConditions(transition, oldName, newName);

                bool speedNeedsUpdate       = state.speedParameter       == oldName;
                bool timeNeedsUpdate        = state.timeParameter        == oldName;
                bool mirrorNeedsUpdate      = state.mirrorParameter      == oldName;
                bool cycleOffsetNeedsUpdate = state.cycleOffsetParameter == oldName;

                if (speedNeedsUpdate || timeNeedsUpdate || mirrorNeedsUpdate || cycleOffsetNeedsUpdate)
                {
                    Undo.RecordObject(state, "Rename Template Parameter");
                    if (speedNeedsUpdate)       state.speedParameter       = newName;
                    if (timeNeedsUpdate)        state.timeParameter        = newName;
                    if (mirrorNeedsUpdate)      state.mirrorParameter      = newName;
                    if (cycleOffsetNeedsUpdate) state.cycleOffsetParameter = newName;
                }

                if (state.motion is BlendTree blendTree)
                    UpdateBlendTreeParams(blendTree, oldName, newName);
                else if (state.motion is AnimationClip stateClip)
                    UpdateClipAAPBinding(stateClip, oldName, newName);
            }

            foreach (var childStateMachine in sm.stateMachines)
                UpdateParamRefsInSM(childStateMachine.stateMachine, oldName, newName);
        }

        static void UpdateTransitionConditions(AnimatorStateTransition transition, string oldName, string newName)
        {
            var conditions = transition.conditions;
            bool modified = false;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].parameter != oldName) continue;
                conditions[i] = new AnimatorCondition
                {
                    mode      = conditions[i].mode,
                    parameter = newName,
                    threshold = conditions[i].threshold
                };
                modified = true;
            }
            if (!modified) return;
            Undo.RecordObject(transition, "Rename Template Parameter");
            transition.conditions = conditions;
        }

        static void UpdateBlendTreeParams(BlendTree blendTree, string oldName, string newName)
        {
            if (blendTree.blendParameter == oldName || blendTree.blendParameterY == oldName)
            {
                Undo.RecordObject(blendTree, "Rename Template Parameter");
                if (blendTree.blendParameter  == oldName) blendTree.blendParameter  = newName;
                if (blendTree.blendParameterY == oldName) blendTree.blendParameterY = newName;
            }

            if (blendTree.children.Any(childMotion => childMotion.directBlendParameter == oldName))
            {
                var serializedBT = new SerializedObject(blendTree);
                serializedBT.Update();
                var childrenProperty = serializedBT.FindProperty("m_Childs");
                if (childrenProperty != null)
                {
                    bool modified = false;
                    for (int i = 0; i < childrenProperty.arraySize; i++)
                    {
                        var directParamProperty = childrenProperty.GetArrayElementAtIndex(i)
                            .FindPropertyRelative("m_DirectBlendParameter");
                        if (directParamProperty != null && directParamProperty.stringValue == oldName)
                        {
                            directParamProperty.stringValue = newName;
                            modified = true;
                        }
                    }
                    if (modified) serializedBT.ApplyModifiedProperties();
                }
            }

            foreach (var childMotion in blendTree.children)
            {
                if (childMotion.motion is BlendTree childBT)
                    UpdateBlendTreeParams(childBT, oldName, newName);
                else if (childMotion.motion is AnimationClip childClip)
                    UpdateClipAAPBinding(childClip, oldName, newName);
            }
        }

        static void UpdateClipAAPBinding(AnimationClip clip, string oldName, string newName)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings)
            {
                if (binding.type != typeof(Animator) || binding.propertyName != oldName) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                Undo.RecordObject(clip, "Rename Template AAP");
                AnimationUtility.SetEditorCurve(clip, binding, null);
                var newBinding = new EditorCurveBinding
                {
                    type = typeof(Animator),
                    path = binding.path,
                    propertyName = newName
                };
                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                EditorUtility.SetDirty(clip);
            }
        }
    }

    internal class TemplateControllerCachePostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var asset in importedAssets.Concat(deletedAssets).Concat(movedAssets))
            {
                string normalizedPath = asset.Replace('\\', '/');
                if (normalizedPath.StartsWith(PatchLayerToolbar.UserTemplatesPath) ||
                    normalizedPath.StartsWith("Packages/com.ygdr.animator/Templates"))
                {
                    PatchLayerToolbar._templateCache = null;
                    return;
                }
            }
        }
    }
}
#endif
