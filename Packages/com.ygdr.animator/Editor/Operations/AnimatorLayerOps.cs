#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal static class AnimatorLayerOps
    {
        /* Clears all anyState, entry, and state transitions in the top-level SM.
           Does not recurse into sub state machines. */
        internal static void DeleteAllTransitions(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null) return;

            var controller = GetController(stateMachine);

            // Only register the top-level SM and its direct states for undo — no recursion.
            var undoTargets = new List<Object> { stateMachine };
            foreach (var childState in stateMachine.states)
                undoTargets.Add(childState.state);
            if (controller != null) undoTargets.Add(controller);

            Undo.RegisterCompleteObjectUndo(undoTargets.ToArray(), "Delete All Transitions in Layer");

            var anyStateTransitions = stateMachine.anyStateTransitions;
            var entryTransitions = stateMachine.entryTransitions;
            var stateTransitionPairs = stateMachine.states
                .Select(childState => (state: childState.state, transitions: childState.state.transitions))
                .ToArray();

            stateMachine.anyStateTransitions = new AnimatorStateTransition[0];
            stateMachine.entryTransitions = new AnimatorTransition[0];
            foreach (var (state, _) in stateTransitionPairs)
                state.transitions = new AnimatorStateTransition[0];

            foreach (var transition in anyStateTransitions) Undo.DestroyObjectImmediate(transition);
            foreach (var transition in entryTransitions) Undo.DestroyObjectImmediate(transition);
            foreach (var (_, transitions) in stateTransitionPairs)
                foreach (var transition in transitions) Undo.DestroyObjectImmediate(transition);

            EditorUtility.SetDirty(stateMachine);
        }

        /* Creates a reversed copy of each selected transition with all conditions negated.
           Skips anyState, exit, and sub-SM-destination transitions. */
        internal static void ReverseNegateTransitions(AnimatorStateMachine activeSM, AnimatorStateTransition[] transitions)
        {
            if (transitions == null || transitions.Length == 0) return;

            // Resolve valid state-to-state pairs; skip anyState/exit/SM-destination transitions
            var transitionOwnerMap = BuildTransitionOwnerMap(activeSM);
            var validPairs = new List<(AnimatorState sourceState, AnimatorState destinationState, AnimatorStateTransition originalTransition)>();
            foreach (var transition in transitions)
            {
                if (transition == null || transition.destinationState == null) continue;
                if (!transitionOwnerMap.TryGetValue(transition, out var sourceState)) continue; // anyState transition — skip
                validPairs.Add((sourceState, transition.destinationState, transition));
            }
            if (validPairs.Count == 0) return;

            var undoTargetSet = new HashSet<Object> { activeSM };
            foreach (var (sourceState, destinationState, _) in validPairs)
            {
                undoTargetSet.Add(sourceState);
                undoTargetSet.Add(destinationState);
            }
            Undo.RegisterCompleteObjectUndo(undoTargetSet.ToArray(), "Reverse Negate Transitions");

            foreach (var (sourceState, destinationState, originalTransition) in validPairs)
            {
                var reversedTransition = destinationState.AddTransition(sourceState);
                Undo.RegisterCreatedObjectUndo(reversedTransition, "Reverse Negate Transitions");

                foreach (var condition in originalTransition.conditions)
                    reversedTransition.AddCondition(NegateConditionMode(condition.mode), condition.threshold, condition.parameter);

                reversedTransition.hasExitTime = originalTransition.hasExitTime;
                reversedTransition.exitTime = originalTransition.exitTime;
                reversedTransition.duration = originalTransition.duration;
                reversedTransition.offset = originalTransition.offset;
                reversedTransition.interruptionSource = originalTransition.interruptionSource;
                reversedTransition.orderedInterruption = originalTransition.orderedInterruption;
                reversedTransition.canTransitionToSelf = originalTransition.canTransitionToSelf;
                reversedTransition.mute = originalTransition.mute;
                reversedTransition.solo = originalTransition.solo;

                EditorUtility.SetDirty(destinationState);
            }

            EditorUtility.SetDirty(activeSM);
        }

        /* Adds an empty transition from every source state to every destination state (full cross-product). */
        internal static void MultiTransition(AnimatorStateMachine activeSM, AnimatorState[] sourceStates, AnimatorState[] destinationStates)
        {
            if (sourceStates == null || destinationStates == null || sourceStates.Length == 0 || destinationStates.Length == 0) return;

            var undoTargets = sourceStates.Cast<Object>().Concat(new Object[] { activeSM }).ToArray();
            Undo.RegisterCompleteObjectUndo(undoTargets, "Multi Transition");

            foreach (var sourceState in sourceStates)
                foreach (var destinationState in destinationStates)
                    Undo.RegisterCreatedObjectUndo(sourceState.AddTransition(destinationState), "Multi Transition");

            foreach (var sourceState in sourceStates) EditorUtility.SetDirty(sourceState);
            EditorUtility.SetDirty(activeSM);
        }

        /* For each selected transition, adds a copy pointing to each new destination state with all settings preserved.
           Original transitions are not removed. */
        internal static void RedirectTransitions(AnimatorStateMachine activeSM, AnimatorStateTransition[] transitions, AnimatorState[] destinationStates)
        {
            if (transitions == null || destinationStates == null || destinationStates.Length == 0) return;

            var transitionOwnerMap = BuildTransitionOwnerMap(activeSM);
            var validPairs = transitions
                .Where(transition => transition != null)
                .Select(transition => (
                    sourceState: transitionOwnerMap.TryGetValue(transition, out var s) ? s : null,
                    originalTransition: transition))
                .Where(pair => pair.sourceState != null)
                .ToList();
            if (validPairs.Count == 0) return;

            var undoTargets = validPairs.Select(pair => (Object)pair.sourceState).Distinct()
                .Concat(new Object[] { activeSM }).ToArray();
            Undo.RegisterCompleteObjectUndo(undoTargets, "Redirect Transitions");

            foreach (var (sourceState, originalTransition) in validPairs)
                foreach (var destinationState in destinationStates)
                {
                    var newTransition = sourceState.AddTransition(destinationState);
                    Undo.RegisterCreatedObjectUndo(newTransition, "Redirect Transitions");
                    AnimatorTransitionOps.CopySettings(newTransition, originalTransition);
                    EditorUtility.SetDirty(sourceState);
                }

            EditorUtility.SetDirty(activeSM);
        }

        /* Duplicates each selected transition onto every new source state, keeping the original destinations and all settings.
           Original transitions on the original sources are not removed. */
        internal static void ReplicateTransitions(AnimatorStateMachine activeSM, AnimatorStateTransition[] transitions, AnimatorState[] newSourceStates)
        {
            if (transitions == null || newSourceStates == null || newSourceStates.Length == 0) return;

            var validPairs = transitions
                .Where(transition => transition != null && transition.destinationState != null)
                .Select(transition => (destinationState: transition.destinationState, originalTransition: transition))
                .ToList();
            if (validPairs.Count == 0) return;

            var undoTargets = newSourceStates.Cast<Object>().Concat(new Object[] { activeSM }).ToArray();
            Undo.RegisterCompleteObjectUndo(undoTargets, "Replicate Transitions");

            foreach (var (destinationState, originalTransition) in validPairs)
                foreach (var sourceState in newSourceStates)
                {
                    var newTransition = sourceState.AddTransition(destinationState);
                    Undo.RegisterCreatedObjectUndo(newTransition, "Replicate Transitions");
                    AnimatorTransitionOps.CopySettings(newTransition, originalTransition);
                    EditorUtility.SetDirty(sourceState);
                }

            EditorUtility.SetDirty(activeSM);
        }

        /* Recursively searches the SM hierarchy to find which state owns the given transition.
           Returns null if the transition belongs to anyState or is not found in this subtree. */
        static AnimatorState FindStateContainingTransition(AnimatorStateMachine stateMachine, AnimatorStateTransition transition)
        {
            // Search direct states
            foreach (var childState in stateMachine.states)
            {
                if (childState.state.transitions.Contains(transition))
                    return childState.state;
            }

            // Search sub state machines recursively
            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                var found = FindStateContainingTransition(childStateMachine.stateMachine, transition);
                if (found != null) return found;
            }

            return null;
        }

        /* Returns the logical inverse of a condition mode (If↔IfNot, Greater↔Less, Equals↔NotEqual). */
        static AnimatorConditionMode NegateConditionMode(AnimatorConditionMode mode)
        {
            return mode switch
            {
                AnimatorConditionMode.If => AnimatorConditionMode.IfNot,
                AnimatorConditionMode.IfNot => AnimatorConditionMode.If,
                AnimatorConditionMode.Greater => AnimatorConditionMode.Less,
                AnimatorConditionMode.Less => AnimatorConditionMode.Greater,
                AnimatorConditionMode.Equals => AnimatorConditionMode.NotEqual,
                AnimatorConditionMode.NotEqual => AnimatorConditionMode.Equals,
                _ => mode
            };
        }

        static Dictionary<AnimatorStateTransition, AnimatorState> BuildTransitionOwnerMap(AnimatorStateMachine stateMachine)
        {
            var map = new Dictionary<AnimatorStateTransition, AnimatorState>();
            CollectTransitionOwners(stateMachine, map);
            return map;
        }

        static void CollectTransitionOwners(AnimatorStateMachine stateMachine, Dictionary<AnimatorStateTransition, AnimatorState> map)
        {
            foreach (var childState in stateMachine.states)
                foreach (var transition in childState.state.transitions)
                    map[transition] = childState.state;
            foreach (var childStateMachine in stateMachine.stateMachines)
                CollectTransitionOwners(childStateMachine.stateMachine, map);
        }

        internal static AnimatorController GetController(AnimatorStateMachine stateMachine)
        {
            var assetPath = AssetDatabase.GetAssetPath(stateMachine);
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
        }
    }
}
#endif
