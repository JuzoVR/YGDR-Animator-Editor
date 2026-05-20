#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace YGDR.Editor.Animation
{
    internal static class AnimatorParameterOps
    {
        static VRCExpressionParameters.ValueType MapToVrcValueType(AnimatorControllerParameterType type) => type switch
        {
            AnimatorControllerParameterType.Float => VRCExpressionParameters.ValueType.Float,
            AnimatorControllerParameterType.Int   => VRCExpressionParameters.ValueType.Int,
            _                                      => VRCExpressionParameters.ValueType.Bool
        };

        internal static void InsertParameterAtIndex(AnimatorController controller,
            int index, string paramName, AnimatorControllerParameterType type)
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

        internal static void ConvertParameter(AnimatorController controller, int index,
            AnimatorControllerParameterType newType)
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
            // Inspector reads parameter type mid-frame; defer rebuild to avoid stale display after type change.
            EditorApplication.delayCall += () => ActiveEditorTracker.sharedTracker.ForceRebuild();
        }

        static void FixConditionsForConversion(AnimatorStateMachine sm, string paramName,
            AnimatorControllerParameterType sourceType, AnimatorControllerParameterType newType)
        {
            var allTransitions = new List<AnimatorStateTransition>(sm.anyStateTransitions);
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

        internal static bool TryConvertCondition(AnimatorCondition condition,
            AnimatorControllerParameterType sourceType, AnimatorControllerParameterType newType,
            out AnimatorCondition result)
        {
            result = condition;
            var mode = condition.mode;
            float threshold = condition.threshold;

            AnimatorConditionMode newMode;
            float newThreshold;

            var Int      = AnimatorControllerParameterType.Int;
            var Bool     = AnimatorControllerParameterType.Bool;
            var Float    = AnimatorControllerParameterType.Float;
            var Equals   = AnimatorConditionMode.Equals;
            var NotEqual = AnimatorConditionMode.NotEqual;
            var Greater  = AnimatorConditionMode.Greater;
            var Less     = AnimatorConditionMode.Less;
            var If       = AnimatorConditionMode.If;
            var IfNot    = AnimatorConditionMode.IfNot;

            if (sourceType == Int && newType == Bool)
            {
                if (mode == Equals)        { newMode = If;      newThreshold = 0f; }
                else if (mode == NotEqual) { newMode = IfNot;   newThreshold = 0f; }
                else return false;
            }
            else if (sourceType == Int && newType == Float)
            {
                if (mode == Equals)        { newMode = Greater; newThreshold = threshold; }
                else if (mode == NotEqual) { newMode = Less;    newThreshold = threshold; }
                else return false;
            }
            else if (sourceType == Bool && (newType == Int || newType == Float))
            {
                if (newType == Int)
                {
                    if (mode == If)        { newMode = Equals;   newThreshold = 1f; }
                    else if (mode == IfNot){ newMode = NotEqual; newThreshold = 1f; }
                    else return false;
                }
                else
                {
                    if (mode == If)        { newMode = Greater; newThreshold = 0f; }
                    else if (mode == IfNot){ newMode = Less;    newThreshold = 1f; }
                    else return false;
                }
            }
            else if (sourceType == Float && newType == Int)
            {
                if (mode == Greater)  { newMode = Equals;   newThreshold = threshold; }
                else if (mode == Less){ newMode = NotEqual; newThreshold = threshold; }
                else return false;
            }
            else if (sourceType == Float && newType == Bool)
            {
                if (mode == Greater)  { newMode = If;    newThreshold = 0f; }
                else if (mode == Less){ newMode = IfNot; newThreshold = 0f; }
                else return false;
            }
            else return false;

            result = new AnimatorCondition
            {
                mode      = newMode,
                parameter = condition.parameter,
                threshold = newThreshold
            };
            return true;
        }

        internal static void RemoveUnusedParameters(AnimatorController controller)
        {
            var usedParamNames = new HashSet<string>();
            foreach (var layer in controller.layers)
                CollectUsedParameters(layer.stateMachine, usedParamNames);

            var unusedParamNames = controller.parameters
                .Where(parameter => !usedParamNames.Contains(parameter.name))
                .Select(parameter => parameter.name)
                .ToArray();

            if (unusedParamNames.Length == 0) return;

            Undo.RegisterCompleteObjectUndo(controller, "Remove Unused Parameters");
            foreach (var unusedParamName in unusedParamNames)
            {
                int paramIndex = Array.FindIndex(controller.parameters, parameter => parameter.name == unusedParamName);
                if (paramIndex >= 0)
                    controller.RemoveParameter(paramIndex);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        static void CollectUsedParameters(AnimatorStateMachine stateMachine, HashSet<string> result)
        {
            foreach (var transition in stateMachine.anyStateTransitions)
                foreach (var condition in transition.conditions)
                    result.Add(condition.parameter);

            foreach (var childState in stateMachine.states)
            {
                foreach (var transition in childState.state.transitions)
                    foreach (var condition in transition.conditions)
                        result.Add(condition.parameter);

                CollectMotionParameters(childState.state.motion, result);

                foreach (var driver in childState.state.behaviours.OfType<VRCAvatarParameterDriver>())
                    foreach (var driverParameter in driver.parameters)
                        result.Add(driverParameter.name);
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
                CollectUsedParameters(childStateMachine.stateMachine, result);
        }

        static void CollectMotionParameters(UnityEngine.Motion motion, HashSet<string> result)
        {
            if (motion is not BlendTree blendTree) return;
            result.Add(blendTree.blendParameter);
            result.Add(blendTree.blendParameterY);
            foreach (var childMotion in blendTree.children)
                CollectMotionParameters(childMotion.motion, result);
        }

        internal static void DeleteParameterAndClean(AnimatorController controller, string paramName)
        {
            Undo.RegisterCompleteObjectUndo(controller, "Delete and Clean Parameter");

            foreach (var layer in controller.layers)
                DeleteTransitionsReferencingParam(layer.stateMachine, paramName);

            int paramIndex = Array.FindIndex(controller.parameters, parameter => parameter.name == paramName);
            if (paramIndex >= 0)
                controller.RemoveParameter(paramIndex);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        static void DeleteTransitionsReferencingParam(AnimatorStateMachine stateMachine, string paramName)
        {
            foreach (var transition in stateMachine.anyStateTransitions)
                StripConditionsForParam(transition, paramName);

            foreach (var childState in stateMachine.states)
                foreach (var transition in childState.state.transitions)
                    StripConditionsForParam(transition, paramName);

            foreach (var childStateMachine in stateMachine.stateMachines)
                DeleteTransitionsReferencingParam(childStateMachine.stateMachine, paramName);
        }

        static void StripConditionsForParam(AnimatorStateTransition transition, string paramName)
        {
            if (!transition.conditions.Any(condition => condition.parameter == paramName)) return;
            Undo.RecordObject(transition, "Delete and Clean Parameter");
            transition.conditions = transition.conditions
                .Where(condition => condition.parameter != paramName).ToArray();
        }

        internal static void RemapParameter(AnimatorController controller, string fromParamName, string toParamName)
        {
            foreach (var layer in controller.layers)
                RemapParameterInStateMachine(layer.stateMachine, fromParamName, toParamName);
            EditorUtility.SetDirty(controller);
        }

        static void RemapParameterInStateMachine(AnimatorStateMachine stateMachine,
            string fromParamName, string toParamName)
        {
            foreach (var transition in stateMachine.anyStateTransitions)
                RemapConditions(transition, fromParamName, toParamName);
            foreach (var childState in stateMachine.states)
            {
                foreach (var transition in childState.state.transitions)
                    RemapConditions(transition, fromParamName, toParamName);
                RemapDriverParameters(childState.state, fromParamName, toParamName);
            }
            foreach (var childStateMachine in stateMachine.stateMachines)
                RemapParameterInStateMachine(childStateMachine.stateMachine, fromParamName, toParamName);
        }

        static void RemapConditions(AnimatorStateTransition transition,
            string fromParamName, string toParamName)
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

        internal static void RemapDriverParameters(AnimatorState state,
            string fromParamName, string toParamName)
        {
            foreach (var driver in state.behaviours.OfType<VRCAvatarParameterDriver>())
            {
                bool modified = false;
                for (int i = 0; i < driver.parameters.Count; i++)
                {
                    if (driver.parameters[i].name != fromParamName) continue;
                    Undo.RecordObject(driver, "Remap Parameter");
                    driver.parameters[i] = new VRC_AvatarParameterDriver.Parameter
                    {
                        name     = toParamName,
                        type     = driver.parameters[i].type,
                        value    = driver.parameters[i].value,
                        valueMin = driver.parameters[i].valueMin,
                        valueMax = driver.parameters[i].valueMax,
                        chance   = driver.parameters[i].chance
                    };
                    modified = true;
                }
                if (!modified) continue;
                EditorUtility.SetDirty(driver);
            }
        }

        internal static void RemapDriverParametersInStateMachine(AnimatorStateMachine stateMachine,
            string fromParamName, string toParamName)
        {
            foreach (var childState in stateMachine.states)
                RemapDriverParameters(childState.state, fromParamName, toParamName);
            foreach (var childStateMachine in stateMachine.stateMachines)
                RemapDriverParametersInStateMachine(childStateMachine.stateMachine, fromParamName, toParamName);
        }

        internal static void AddAllToVrcParameters(VRCExpressionParameters expressionParameters,
            AnimatorController controller)
        {
            Undo.RecordObject(expressionParameters, "Add All Parameters to VRC");
            var existingNames = new HashSet<string>(
                expressionParameters.parameters.Select(expressionParameter => expressionParameter.name));
            var paramsList = expressionParameters.parameters.ToList();

            foreach (var animatorParameter in controller.parameters)
            {
                if (existingNames.Contains(animatorParameter.name)) continue;
                paramsList.Add(new VRCExpressionParameters.Parameter
                {
                    name          = animatorParameter.name,
                    valueType     = MapToVrcValueType(animatorParameter.type),
                    networkSynced = false,
                    saved         = false,
                    defaultValue  = 0f
                });
            }

            expressionParameters.parameters = paramsList.ToArray();
            EditorUtility.SetDirty(expressionParameters);
        }

        internal static void AddToVrcParameters(VRCExpressionParameters expressionParameters,
            string paramName, AnimatorControllerParameterType paramType)
        {
            Undo.RecordObject(expressionParameters, "Add VRC Parameter");
            var newParam = new VRCExpressionParameters.Parameter
            {
                name          = paramName,
                valueType     = MapToVrcValueType(paramType),
                networkSynced = true,
                saved         = false,
                defaultValue  = 0f
            };
            var paramsList = expressionParameters.parameters.ToList();
            paramsList.Add(newParam);
            expressionParameters.parameters = paramsList.ToArray();
            EditorUtility.SetDirty(expressionParameters);
        }

        internal static void SetVrcSynced(VRCExpressionParameters expressionParameters,
            string paramName, bool synced)
        {
            Undo.RecordObject(expressionParameters,
                synced ? "Set VRC Parameter Synced" : "Set VRC Parameter Not Synced");
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
    }
}
#endif
