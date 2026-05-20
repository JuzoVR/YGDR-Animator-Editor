#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal class IconBrowserWindow : EditorWindow
    {
        const float IconSize   = 32f;
        const float CellWidth  = 100f;
        const float CellHeight = 54f;

        [MenuItem("YGDR/Icon Browser")]
        static void Open()
        {
            var window = GetWindow<IconBrowserWindow>("Icon Browser");
            window.minSize = new Vector2(400, 300);
        }

        string _search     = "";
        string _lastSearch;
        Vector2 _scroll;
        List<(string name, Texture texture)> _allIcons;
        List<(string name, Texture texture)> _filteredIcons;
        GUIStyle _cellLabelStyle;

        void OnEnable() => LoadIcons();

        /* Loads all PNG textures from Unity's internal editor asset bundle and sorts them alphabetically. */
        void LoadIcons()
        {
            _allIcons = new List<(string, Texture)>();
            try
            {
                var method = typeof(EditorGUIUtility).GetMethod("GetEditorAssetBundle",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (method?.Invoke(null, null) is not AssetBundle bundle) return;

                foreach (var assetName in bundle.GetAllAssetNames())
                {
                    if (!assetName.EndsWith(".png")) continue;
                    var texture = bundle.LoadAsset<Texture2D>(assetName);
                    if (texture == null) continue;
                    _allIcons.Add((System.IO.Path.GetFileNameWithoutExtension(assetName), texture));
                }
                _allIcons.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            }
            catch { }
            _filteredIcons = _allIcons;
        }

        void OnGUI()
        {
            if (_allIcons == null) LoadIcons();
            _cellLabelStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                wordWrap  = true,
                fontSize  = 9,
                clipping  = TextClipping.Clip
            };

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                GUILayout.Space(8);
            }
            EditorGUILayout.Space(4);

            if (_search != _lastSearch)
            {
                _lastSearch    = _search;
                _filteredIcons = string.IsNullOrEmpty(_search)
                    ? _allIcons
                    : _allIcons.Where(icon => icon.name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 16f) / CellWidth));

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Space(4);

            for (int rowStart = 0; rowStart < _filteredIcons.Count; rowStart += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(8);
                    int rowEnd = Mathf.Min(rowStart + columns, _filteredIcons.Count);
                    for (int i = rowStart; i < rowEnd; i++)
                    {
                        var (name, texture) = _filteredIcons[i];
                        var cellRect = EditorGUILayout.GetControlRect(false, CellHeight, GUILayout.Width(CellWidth));
                        EditorGUIUtility.AddCursorRect(cellRect, MouseCursor.Link);

                        if (Event.current.type == EventType.MouseDown && cellRect.Contains(Event.current.mousePosition))
                        {
                            EditorGUIUtility.systemCopyBuffer = name;
                            ShowNotification(new GUIContent($"Copied: {name}"));
                            Event.current.Use();
                        }

                        if (Event.current.type == EventType.Repaint)
                        {
                            var iconRect  = new Rect(cellRect.x + (CellWidth - IconSize) / 2f, cellRect.y + 2f, IconSize, IconSize);
                            var labelRect = new Rect(cellRect.x, cellRect.y + IconSize + 4f, CellWidth, CellHeight - IconSize - 6f);
                            GUI.DrawTexture(iconRect, texture, ScaleMode.ScaleToFit, true);
                            _cellLabelStyle.Draw(labelRect, name, false, false, false, false);
                        }
                    }
                }
            }

            GUILayout.Space(4);
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_filteredIcons?.Count ?? 0} icons — click to copy name", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(4);
        }
    }
}
#endif
