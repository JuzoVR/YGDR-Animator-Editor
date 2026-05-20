#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace YGDR.Editor.Animation
{
    // Caches VRC expression parameter sync state for the last qualifying avatar + open controller.
    // Icons persist when clicking non-avatar objects. Rebuilds only when a different qualifying avatar is selected.
    internal static class VRCSyncCache
    {
        static GameObject _cachedAvatarRoot;
        static GameObject _cachedSelectedGO;
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

            var avatarDescriptor = activeGO.GetComponentInParent<VRCAvatarDescriptor>(true);
            if (avatarDescriptor == null) return;

            if (ReferenceEquals(activeGO, _cachedSelectedGO)) return;

            Rebuild(avatarDescriptor, activeGO);
        }

        static void OnUndoRedo()
        {
            if (_cachedAvatarRoot == null) return;
            var avatarDescriptor = _cachedAvatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (avatarDescriptor != null) Rebuild(avatarDescriptor, _cachedSelectedGO);
        }

        static void OnObjectChanged(ref ObjectChangeEventStream stream)
        {
            if (_cachedAvatarRoot == null) return;
            var avatarDescriptor = _cachedAvatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (avatarDescriptor == null) return;

            int avatarParamsId = avatarDescriptor.expressionParameters != null
                ? avatarDescriptor.expressionParameters.GetInstanceID() : 0;
            int vrcFuryParamsId = _isVrcFurySource && _vrcFuryParams != null
                ? _vrcFuryParams.GetInstanceID() : 0;

            if (avatarParamsId == 0 && vrcFuryParamsId == 0) return;

            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;
                stream.GetChangeAssetObjectPropertiesEvent(i, out var changeEvent);
                if (changeEvent.instanceId == avatarParamsId || changeEvent.instanceId == vrcFuryParamsId)
                {
                    Rebuild(avatarDescriptor, _cachedSelectedGO);
                    return;
                }
            }
        }

        static void Rebuild(VRCAvatarDescriptor avatarDescriptor, GameObject selectedGO)
        {
            try
            {
                _syncMap = null;
                _valueTypeMap = null;
                _cachedAvatarRoot = null;
                _cachedSelectedGO = null;
                _isVrcFurySource = false;
                _vrcFuryParams = null;

                var openController = WindowPatchReflection.GetOpenController();
                if (openController == null) return;

                _cachedAvatarRoot = avatarDescriptor.gameObject;
                _cachedSelectedGO = selectedGO;

                VRCExpressionParameters expressionParameters;

                var vrcFuryParamsCheck = selectedGO != null ? FindVrcFuryParamsOnGO(selectedGO) : null;
                if (vrcFuryParamsCheck != null)
                {
                    expressionParameters = vrcFuryParamsCheck;
                    _isVrcFurySource = true;
                    _vrcFuryParams = vrcFuryParamsCheck;
                }
                else
                {
                    bool controllerInAnimator = avatarDescriptor.GetComponent<Animator>()?.runtimeAnimatorController as AnimatorController == openController;
                    bool controllerInDescriptor = false;
                    foreach (var layer in avatarDescriptor.baseAnimationLayers)
                        if (layer.animatorController == openController) { controllerInDescriptor = true; break; }
                    if (!controllerInDescriptor)
                        foreach (var layer in avatarDescriptor.specialAnimationLayers)
                            if (layer.animatorController == openController) { controllerInDescriptor = true; break; }

                    if (controllerInAnimator || controllerInDescriptor)
                        expressionParameters = avatarDescriptor.expressionParameters;
                    else
                        return;
                }

                if (expressionParameters?.parameters == null) return;

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

        static VRCExpressionParameters FindVrcFuryParamsOnGO(GameObject selectedGO)
        {
            var vrcfuryType = AccessTools.TypeByName("VF.Model.VRCFury");
            if (vrcfuryType == null) return null;
            var components = selectedGO.GetComponents(vrcfuryType);
            if (components.Length == 0) return null;
            var getAllFeaturesMethod = AccessTools.Method(vrcfuryType, "GetAllFeatures");
            if (getAllFeaturesMethod == null) return null;

            foreach (var component in components)
            {
                var features = getAllFeaturesMethod.Invoke(component, null) as System.Collections.IEnumerable;
                if (features == null) continue;

                foreach (var feature in features)
                {
                    if (feature?.GetType().FullName != "VF.Model.Feature.FullController") continue;

                    var featureType = feature.GetType();
                    var prms = AccessTools.Field(featureType, "prms")?.GetValue(feature) as System.Collections.IEnumerable;
                    if (prms == null) continue;

                    foreach (var prmsEntry in prms)
                    {
                        if (prmsEntry == null) continue;
                        var guidParams = AccessTools.Field(prmsEntry.GetType(), "parameters")?.GetValue(prmsEntry);
                        if (guidParams == null) continue;
                        var expressionParams = AccessTools.Field(guidParams.GetType(), "objRef")?.GetValue(guidParams) as VRCExpressionParameters;
                        if (expressionParams != null) return expressionParams;
                    }
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
                as AnimatorController == openController;
            if (!controllerMatches)
                foreach (var layer in avatarDescriptor.baseAnimationLayers)
                    if (layer.animatorController == openController) { controllerMatches = true; break; }
            if (!controllerMatches)
                foreach (var layer in avatarDescriptor.specialAnimationLayers)
                    if (layer.animatorController == openController) { controllerMatches = true; break; }

            return controllerMatches ? avatarDescriptor.expressionParameters : null;
        }
    }
}
#endif
