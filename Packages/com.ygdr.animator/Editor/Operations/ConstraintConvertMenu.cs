#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Animations;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace YGDR.Editor.Animation
{
    internal static class ConstraintConvertMenu
    {
        // ── Unity PositionConstraint ──────────────────────────────────────────
        [MenuItem("CONTEXT/PositionConstraint/Convert to Rotation Constraint")]
        static void UnityPositionToRotation(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((PositionConstraint)cmd.context, typeof(RotationConstraint));

        [MenuItem("CONTEXT/PositionConstraint/Convert to Parent Constraint")]
        static void UnityPositionToParent(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((PositionConstraint)cmd.context, typeof(ParentConstraint));

        // ── Unity RotationConstraint ──────────────────────────────────────────
        [MenuItem("CONTEXT/RotationConstraint/Convert to Position Constraint")]
        static void UnityRotationToPosition(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((RotationConstraint)cmd.context, typeof(PositionConstraint));

        [MenuItem("CONTEXT/RotationConstraint/Convert to Parent Constraint")]
        static void UnityRotationToParent(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((RotationConstraint)cmd.context, typeof(ParentConstraint));

        // ── Unity ParentConstraint ────────────────────────────────────────────
        [MenuItem("CONTEXT/ParentConstraint/Convert to Position Constraint")]
        static void UnityParentToPosition(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((ParentConstraint)cmd.context, typeof(PositionConstraint));

        [MenuItem("CONTEXT/ParentConstraint/Convert to Rotation Constraint")]
        static void UnityParentToRotation(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((ParentConstraint)cmd.context, typeof(RotationConstraint));

        // ── VRC PositionConstraint ────────────────────────────────────────────
        [MenuItem("CONTEXT/VRCPositionConstraint/Convert to VRC Rotation Constraint")]
        static void VRCPositionToRotation(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((VRCPositionConstraint)cmd.context, typeof(VRCRotationConstraint));

        [MenuItem("CONTEXT/VRCPositionConstraint/Convert to VRC Parent Constraint")]
        static void VRCPositionToParent(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((VRCPositionConstraint)cmd.context, typeof(VRCParentConstraint));

        // ── VRC RotationConstraint ────────────────────────────────────────────
        [MenuItem("CONTEXT/VRCRotationConstraint/Convert to VRC Position Constraint")]
        static void VRCRotationToPosition(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((VRCRotationConstraint)cmd.context, typeof(VRCPositionConstraint));

        [MenuItem("CONTEXT/VRCRotationConstraint/Convert to VRC Parent Constraint")]
        static void VRCRotationToParent(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((VRCRotationConstraint)cmd.context, typeof(VRCParentConstraint));

        // ── VRC ParentConstraint ──────────────────────────────────────────────
        [MenuItem("CONTEXT/VRCParentConstraint/Convert to VRC Position Constraint")]
        static void VRCParentToPosition(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((VRCParentConstraint)cmd.context, typeof(VRCPositionConstraint));

        [MenuItem("CONTEXT/VRCParentConstraint/Convert to VRC Rotation Constraint")]
        static void VRCParentToRotation(MenuCommand cmd) =>
            ConstraintConvertOps.Convert((VRCParentConstraint)cmd.context, typeof(VRCRotationConstraint));
    }
}
#endif
