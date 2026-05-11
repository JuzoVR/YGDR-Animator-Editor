#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    [Serializable]
    public class FrameRect
    {
        public string title;
        public AnimatorStateMachine layerStateMachine;
        public Rect bounds;
        public Color color = new Color(0.35f, 0.35f, 0.35f, 0.75f);
        public bool locked;
    }

    public class FrameLayoutData : ScriptableObject
    {
        public List<FrameRect> frames = new();

        public static FrameLayoutData GetOrCreate(AnimatorController controller)
        {
            var path = AssetDatabase.GetAssetPath(controller);
            var existing = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<FrameLayoutData>()
                .FirstOrDefault();
            if (existing != null) return existing;

            var data = CreateInstance<FrameLayoutData>();
            data.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(data, controller);
            AssetDatabase.SaveAssets();
            return data;
        }
    }
}
#endif
