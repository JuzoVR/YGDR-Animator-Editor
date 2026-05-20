#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;

namespace YGDR.Editor.Animation
{
    internal static class AnimatorStateOps
    {
        internal static void RenameState(AnimatorState state, string newName)
        {
            Undo.RecordObject(state, "Rename State");
            state.name = newName;
            EditorUtility.SetDirty(state);
        }

        internal static void RenameStateMachine(AnimatorStateMachine stateMachine, string newName)
        {
            Undo.RecordObject(stateMachine, "Rename Sub-State Machine");
            stateMachine.name = newName;
            EditorUtility.SetDirty(stateMachine);
        }

        internal static void AddChainTransition(AnimatorState source, AnimatorState destination)
        {
            Undo.RegisterCompleteObjectUndo(source, "Chain Transition");
            source.AddTransition(destination);
            EditorUtility.SetDirty(source);
        }

        internal static void RenameMotion(UnityEngine.Motion motion, string newName)
        {
            if (AssetDatabase.IsMainAsset(motion))
            {
                var path = AssetDatabase.GetAssetPath(motion);
                AssetDatabase.RenameAsset(path, newName);
                AssetDatabase.SaveAssets();
            }
            else
            {
                Undo.RecordObject(motion, "Rename Motion Clip");
                motion.name = newName;
                EditorUtility.SetDirty(motion);
            }
        }
    }
}
#endif
