#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        /* Opens an AdvancedDropdown listing all controller parameters below rect, invoking onSelected with the chosen name. */
        void ShowParameterDropdown(Rect rect, string currentParam, Action<string> onSelected)
        {
            if (_controller == null || _controller.parameters.Length == 0) return;
            new ParameterDropdown(_controller.parameters, currentParam, onSelected).ShowWithCheckmark(rect);
        }

        /* Opens an AdvancedDropdown listing layer names below rect, invoking onSelected with the chosen index. */
        void ShowLayerDropdown(Rect rect, string[] layerNames, int currentIndex, Action<int> onSelected)
        {
            string current = currentIndex >= 0 && currentIndex < layerNames.Length ? layerNames[currentIndex] : "";
            new ParameterDropdown(layerNames, current, name => onSelected(Array.IndexOf(layerNames, name)))
                .ShowWithCheckmark(rect);
        }

        class ParameterDropdown : AdvancedDropdown
        {
            static readonly FieldInfo    ItemIdField         = AccessTools.Field(typeof(AdvancedDropdownItem), "m_Id");
            static readonly FieldInfo    DataSourceField     = AccessTools.Field(typeof(AdvancedDropdown), "m_DataSource");
            static readonly PropertyInfo MaximumSizeProperty = AccessTools.Property(typeof(AdvancedDropdown), "maximumSize");
            static FieldInfo             _selectedIDsField;

            readonly AnimatorControllerParameter[] _parameters;
            readonly string[] _items;
            readonly string _currentParam;
            readonly Action<string> _onSelected;
            readonly float _maxHeight;
            ParameterItem _currentItem;

            internal ParameterDropdown(AnimatorControllerParameter[] parameters, string currentParam, Action<string> onSelected, float maxHeight = 350f)
                : base(new AdvancedDropdownState())
            {
                _parameters = parameters;
                _currentParam = currentParam;
                _onSelected = onSelected;
                _maxHeight = maxHeight;
                minimumSize = new Vector2(200, 250);
            }

            internal ParameterDropdown(string[] items, string current, Action<string> onSelected, float maxHeight = 250f)
                : base(new AdvancedDropdownState())
            {
                _items = items;
                _currentParam = current;
                _onSelected = onSelected;
                _maxHeight = maxHeight;
                minimumSize = new Vector2(200, 150);
            }

            internal void ShowWithCheckmark(Rect rect)
            {
                MaximumSizeProperty?.SetValue(this, new Vector2(10000f, _maxHeight));
                Show(rect);

                if (_currentItem == null || ItemIdField == null || DataSourceField == null) return;
                try
                {
                    var dataSource = DataSourceField.GetValue(this);
                    if (dataSource == null) return;
                    _selectedIDsField ??= AccessTools.Field(dataSource.GetType(), "m_SelectedIDs");
                    if (_selectedIDsField == null) return;
                    var selectedIDs = (List<int>)_selectedIDsField.GetValue(dataSource);
                    selectedIDs.Clear();
                    selectedIDs.Add((int)ItemIdField.GetValue(_currentItem));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AnimatorTools] ParameterDropdown checkmark: {e.Message}");
                }
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                _currentItem = null;
                if (_items != null)
                {
                    var root = new AdvancedDropdownItem("Layers");
                    foreach (var item in _items)
                    {
                        var dropdownItem = new ParameterItem(item, item);
                        if (item == _currentParam) _currentItem = dropdownItem;
                        root.AddChild(dropdownItem);
                    }
                    return root;
                }
                var parametersRoot = new AdvancedDropdownItem("Parameters");
                var groups = new Dictionary<string, AdvancedDropdownItem>();
                foreach (var param in _parameters)
                {
                    var parts = param.name.Split('/');
                    bool isCurrent = param.name == _currentParam;
                    if (parts.Length == 1)
                    {
                        var item = new ParameterItem(param.name, param.name);
                        if (isCurrent) _currentItem = item;
                        parametersRoot.AddChild(item);
                        continue;
                    }
                    var parent = parametersRoot;
                    string runningPath = null;
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        string groupPath = runningPath == null ? parts[i] : runningPath + "/" + parts[i];
                        runningPath = groupPath;
                        if (!groups.TryGetValue(groupPath, out var group))
                        {
                            group = new AdvancedDropdownItem(parts[i]);
                            parent.AddChild(group);
                            groups[groupPath] = group;
                        }
                        parent = group;
                    }
                    var leafItem = new ParameterItem(parts[parts.Length - 1], param.name);
                    if (isCurrent) _currentItem = leafItem;
                    parent.AddChild(leafItem);
                }
                return parametersRoot;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
                => _onSelected?.Invoke(item is ParameterItem parameterItem ? parameterItem.fullName : item.name);

            class ParameterItem : AdvancedDropdownItem
            {
                internal readonly string fullName;
                internal ParameterItem(string displayName, string fullName) : base(displayName)
                    => this.fullName = fullName;
            }
        }
    }
}
#endif
