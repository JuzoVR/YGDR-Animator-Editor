#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    // Bottom bar: selection count, active mode label, clickable controller path
    [HarmonyPatch]
    [HarmonyPriority(Priority.Low)]
    internal static class PatchBottomBar
    {
        static readonly GUIContent _tempContent = new GUIContent();


        [HarmonyTargetMethod]
        static MethodBase TargetMethod() =>
            AccessTools.Method(AnimatorEditorInit.AnimatorControllerToolType, "DoGraphBottomBar");

        [HarmonyPostfix]
        static void Postfix(object __instance, Rect nameRect)
        {
            try
            {
                var controller = WindowPatchReflection.AnimatorControllerGetter?.Invoke(__instance, null)
                    as AnimatorController;
                if (controller == null) return;

                // Make existing controller path label clickable
                string controllerPath = AssetDatabase.GetAssetPath(controller);
                _tempContent.text = controllerPath;
                float controllerLabelWidth = EditorStyles.miniLabel.CalcSize(_tempContent).x + 18f;
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
                    int nodeCount = Selection.objects.OfType<AnimatorState>().Count();
                    int transitionCount = Selection.objects.OfType<AnimatorStateTransition>().Count();
                    _tempContent.text = $"  {nodeCount} Nodes / {transitionCount} Transitions Selected";
                    float selectionWidth = AnimatorStyles.BottomBarLabelStyle.CalcSize(_tempContent).x;
                    DrawBarLabel(new Rect(nameRect.x, nameRect.y, selectionWidth, nameRect.height), _tempContent);
                }

                // Active mode label (centered)
                string modeText = GetModeText();
                if (!string.IsNullOrEmpty(modeText))
                {
                    _tempContent.text = modeText;
                    float modeWidth = AnimatorStyles.BottomBarLabelStyle.CalcSize(_tempContent).x;
                    float modeX = nameRect.x + (nameRect.width - modeWidth) * 0.5f;
                    DrawBarLabel(new Rect(modeX, nameRect.y, modeWidth, nameRect.height), _tempContent);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimatorTools] Bottom bar error: {e}");
            }
        }

        static void DrawBarLabel(Rect rect, GUIContent content)
        {
            GUILayout.BeginArea(rect);
            EditorGUILayout.LabelField(content, AnimatorStyles.BottomBarLabelStyle);
            GUILayout.EndArea();
        }

        static string GetModeText()
        {
            if (PatchStateChainTransition.FanActive)                return "Fan Mode";
            if (PatchStateChainTransition.ChainActive)              return "Chain Mode";
            if (PatchTransitionCopyPaste.PasteActive)               return $"Paste {PatchTransitionCopyPaste.ClipboardCount} Transition{(PatchTransitionCopyPaste.ClipboardCount == 1 ? "" : "s")}";
            if (PatchStateNodeMenu._multiTransitionSources != null) return "Multi Transition — click destination";
            if (PatchStateNodeMenu._redirectTransitions != null)    return "Redirect Transitions — click destination";
            if (PatchStateNodeMenu._replicateTransitions != null)   return "Replicate Transitions — click sources";
            return null;
        }
    }
}
#endif
