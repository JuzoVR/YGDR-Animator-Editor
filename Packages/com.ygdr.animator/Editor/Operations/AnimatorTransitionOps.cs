#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;

namespace YGDR.Editor.Animation
{
    internal static class AnimatorTransitionOps
    {
        internal struct TransitionData
        {
            internal bool hasExitTime;
            internal float exitTime;
            internal float duration;
            internal float offset;
            internal TransitionInterruptionSource interruptionSource;
            internal bool orderedInterruption;
            internal bool canTransitionToSelf;
            internal bool mute;
            internal bool solo;
            internal AnimatorCondition[] conditions;

            internal static TransitionData From(AnimatorStateTransition transition) => new TransitionData
            {
                hasExitTime         = transition.hasExitTime,
                exitTime            = transition.exitTime,
                duration            = transition.duration,
                offset              = transition.offset,
                interruptionSource  = transition.interruptionSource,
                orderedInterruption = transition.orderedInterruption,
                canTransitionToSelf = transition.canTransitionToSelf,
                mute                = transition.mute,
                solo                = transition.solo,
                conditions          = transition.conditions.ToArray(),
            };
        }

        internal static void PasteTransitions(AnimatorState source, AnimatorState destination, TransitionData[] clipboard)
        {
            Undo.RegisterCompleteObjectUndo(source, "Paste Transitions");
            foreach (var template in clipboard)
            {
                var newTransition = source.AddTransition(destination);
                CopySettings(newTransition, template);
            }
            EditorUtility.SetDirty(source);
        }

        internal static void CopySettings(AnimatorStateTransition destination, AnimatorStateTransition source)
        {
            destination.hasExitTime         = source.hasExitTime;
            destination.exitTime            = source.exitTime;
            destination.duration            = source.duration;
            destination.offset              = source.offset;
            destination.interruptionSource  = source.interruptionSource;
            destination.orderedInterruption = source.orderedInterruption;
            destination.canTransitionToSelf = source.canTransitionToSelf;
            destination.mute                = source.mute;
            destination.solo                = source.solo;
            destination.conditions          = source.conditions;
        }

        internal static void CopySettings(AnimatorStateTransition destination, TransitionData source)
        {
            destination.hasExitTime         = source.hasExitTime;
            destination.exitTime            = source.exitTime;
            destination.duration            = source.duration;
            destination.offset              = source.offset;
            destination.interruptionSource  = source.interruptionSource;
            destination.orderedInterruption = source.orderedInterruption;
            destination.canTransitionToSelf = source.canTransitionToSelf;
            destination.mute                = source.mute;
            destination.solo                = source.solo;
            destination.conditions          = source.conditions;
        }
    }
}
#endif
