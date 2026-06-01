#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using ReorderableList = UnityEditorInternal.ReorderableList;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        // ── VRC Drivers section ───────────────────────────────────────────────

        void DrawVRCDriversSection()
        {
            bool anyHave = _selectedStates.Any(state => GetDriverForState(state) != null);
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetDriverForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label("Shared VRC Parameter Drivers", Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                bool hasAnyParams = _selectedStates.Any(state => { var driver = GetDriverForState(state); return driver != null && driver.parameters.Count > 0; });
                if (!hasAnyParams && CursorBtn("Add to All", EditorStyles.miniButton, GUILayout.Width(72), GUILayout.Height(24)))
                {
                    AddDriverParam();
                    anyHave = true;
                }
                if (anyHave && CursorBtn("Remove All", EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(24)))
                {
                    RemoveDriverFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.CondBody, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            // Debug String + Local Only row
            using (new EditorGUILayout.HorizontalScope())
            {
                var drivers = _selectedStates.Select(state => GetDriverForState(state)).Where(driver => driver != null).ToArray();
                if (drivers.Length > 0)
                {
                    bool multiDrivers = drivers.Length > 1;
                    var firstDriver = drivers[0];
                    EditorGUILayout.LabelField("Debug String", GUILayout.Width(80));
                    EditorGUI.showMixedValue = multiDrivers && drivers.Any(driver => driver.debugString != firstDriver.debugString);
                    EditorGUI.BeginChangeCheck();
                    string newDebugString = EditorGUILayout.TextField(firstDriver.debugString ?? "");
                    if (EditorGUI.EndChangeCheck())
                    {
                        foreach (var state in _selectedStates)
                        {
                            var driver = GetDriverForState(state);
                            if (driver == null) continue;
                            Undo.RecordObject(driver, "Edit Debug String");
                            driver.debugString = newDebugString;
                            EditorUtility.SetDirty(driver);
                        }
                    }
                    EditorGUI.showMixedValue = false;
                }
                DrawLocalOnlyButton();
            }

            var sharedParams = GetSharedDriverParams();
            float rowHeight = EditorGUIUtility.singleLineHeight;

            if (sharedParams.Count == 0)
                EditorGUILayout.LabelField("List is Empty", Styles.EmptyLabel);
            else
            {
                if (_driverParamListData == null || _driverParamListData.Count != sharedParams.Count)
                    _driverParamListData = new List<VRC_AvatarParameterDriver.Parameter>(sharedParams.Select(entry => entry.param));
                else
                    for (int i = 0; i < sharedParams.Count; i++)
                        _driverParamListData[i] = sharedParams[i].param;

                if (_driverParamReorderList == null)
                {
                    _driverParamReorderList = new ReorderableList(_driverParamListData, typeof(VRC_AvatarParameterDriver.Parameter), true, false, false, false)
                    {
                        elementHeight = rowHeight,
                        showDefaultBackground = false,
                        footerHeight = 0f,
                    };

                    _driverParamReorderList.drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        if (index >= _driverParamListData.Count) return;
                        var param = _driverParamListData[index];
                        var localStates = _selectedStates.Where(state => GetDriverForState(state) != null).ToArray();
                        bool localMulti = localStates.Length > 1;
                        bool hasMixedTypes = localMulti && !localStates.All(state => {
                            var driver = GetDriverForState(state);
                            foreach (var p in driver.parameters)
                                if (p.name == param.name) return p.type == param.type;
                            return false;
                        });
                        bool hasMixedValues = hasMixedTypes || (localMulti && !localStates.All(state => {
                            var driver = GetDriverForState(state);
                            foreach (var p in driver.parameters)
                                if (p.name == param.name) return DriverParamsMatch(p, param);
                            return false;
                        }));
                        DrawDriverParamRowRect(new Rect(rect.x, rect.y + 1f, rect.width, rect.height - 2f), new DriverParamEntry(param, index, hasMixedValues, hasMixedTypes));
                    };

                    _driverParamReorderList.onReorderCallbackWithDetails = (list, oldIndex, newIndex) =>
                    {
                        foreach (var state in _selectedStates)
                        {
                            var driver = GetDriverForState(state);
                            if (driver == null || driver.parameters.Count < 2) continue;
                            Undo.RecordObject(driver, "Reorder Driver Parameters");
                            var paramList = driver.parameters.ToList();
                            if (oldIndex < paramList.Count)
                            {
                                var item = paramList[oldIndex];
                                paramList.RemoveAt(oldIndex);
                                paramList.Insert(Mathf.Clamp(newIndex, 0, paramList.Count), item);
                                driver.parameters = paramList;
                            }
                            EditorUtility.SetDirty(driver);
                        }
                    };
                }

                _driverParamReorderList.DoLayoutList();
            }

            if (_removeDriverParamIndex >= 0)
            {
                var capturedEntries = GetSharedDriverParams();
                if (_removeDriverParamIndex < capturedEntries.Count)
                    RemoveDriverParam(capturedEntries[_removeDriverParamIndex]);
                _removeDriverParamIndex = -1;
                _driverParamReorderList = null;
            }

            EditorGUILayout.EndVertical();

            GUILayout.Space(-EditorGUIUtility.standardVerticalSpacing);
            float addBtnSize = EditorGUIUtility.singleLineHeight;
            var addRow = EditorGUILayout.GetControlRect(false, addBtnSize);
            if (CursorBtn(new Rect(addRow.xMax - 40f, addRow.y, 24f, addBtnSize), "+", Styles.CondBtn))
            {
                AddDriverParam();
                _driverParamReorderList = null;
            }
        }

        void DrawLocalOnlyButton()
        {
            bool? localOnly = GetSharedLocalOnly();
            var prevColor = GUI.color;
            GUI.color = localOnly == null ? Color.grey
                      : localOnly.Value   ? new Color(0.4f, 0.9f, 0.4f)
                      :                     new Color(0.9f, 0.4f, 0.4f);
            if (CursorBtn("Local Only", EditorStyles.miniButton, GUILayout.Width(80), GUILayout.Height(24)))
            {
                bool newLocalOnly = localOnly != true;
                foreach (var state in _selectedStates)
                {
                    var driver = GetOrCreateDriver(state);
                    Undo.RecordObject(driver, "Set Local Only");
                    driver.localOnly = newLocalOnly;
                    EditorUtility.SetDirty(driver);
                }
            }
            GUI.color = prevColor;
        }

        void DrawBoolToggleButtons(bool currentValue, bool isMixed, string trueLabel, string falseLabel, float buttonWidth, Action<bool> onChanged)
        {
            var prevContentColor = GUI.contentColor;
            GUI.contentColor = isMixed ? Color.gray : currentValue ? Color.green : Color.gray;
            if (CursorBtn(trueLabel, EditorStyles.miniButton, GUILayout.Width(buttonWidth)) && (isMixed || !currentValue))
                onChanged(true);
            GUILayout.Space(2f);
            GUI.contentColor = isMixed ? Color.gray : !currentValue ? Color.green : Color.gray;
            if (CursorBtn(falseLabel, EditorStyles.miniButton, GUILayout.Width(buttonWidth)) && (isMixed || currentValue))
                onChanged(false);
            GUI.contentColor = prevContentColor;
        }

        bool? GetSharedLocalOnly()
        {
            if (_selectedStates.Length == 0) return false;
            var drivers = _selectedStates
                .Select(state => GetDriverForState(state))
                .Where(driver => driver != null)
                .ToArray();
            if (drivers.Length == 0) return false;
            bool firstLocalOnly = drivers[0].localOnly;
            return drivers.All(driver => driver.localOnly == firstLocalOnly) ? (bool?)firstLocalOnly : null;
        }

        readonly struct DriverParamEntry
        {
            internal readonly VRC_AvatarParameterDriver.Parameter param;
            internal readonly int index;
            internal readonly bool hasMixedValues;
            internal readonly bool hasMixedTypes;
            internal DriverParamEntry(VRC_AvatarParameterDriver.Parameter param, int index, bool hasMixedValues, bool hasMixedTypes)
            { this.param = param; this.index = index; this.hasMixedValues = hasMixedValues; this.hasMixedTypes = hasMixedTypes; }
        }

        List<DriverParamEntry> GetSharedDriverParams()
        {
            var result = new List<DriverParamEntry>();
            if (_selectedStates.Length == 0) return result;

            var firstDriver = GetDriverForState(_selectedStates[0]);
            if (firstDriver == null || firstDriver.parameters.Count == 0) return result;

            for (int i = 0; i < firstDriver.parameters.Count; i++)
            {
                var param = firstDriver.parameters[i];
                bool sharedAcrossAll = _selectedStates.All(state =>
                {
                    var driver = GetDriverForState(state);
                    return driver != null && driver.parameters.Any(parameter => parameter.name == param.name);
                });
                if (!sharedAcrossAll) continue;
                bool hasMixedTypes = !_selectedStates.All(state =>
                {
                    var driver = GetDriverForState(state);
                    if (driver == null) return false;
                    foreach (var parameter in driver.parameters)
                        if (parameter.name == param.name) return parameter.type == param.type;
                    return false;
                });
                bool hasMixedValues = hasMixedTypes || !_selectedStates.All(state =>
                {
                    var driver = GetDriverForState(state);
                    if (driver == null) return false;
                    foreach (var parameter in driver.parameters)
                        if (parameter.name == param.name) return DriverParamsMatch(parameter, param);
                    return false;
                });
                result.Add(new DriverParamEntry(param, i, hasMixedValues, hasMixedTypes));
            }
            return result;
        }

        /* Draws one row of the shared parameter driver list: name dropdown, type popup, value/range/chance field (adapted to param type and ChangeType), and remove button. */
        void DrawDriverParamRowRect(Rect row, DriverParamEntry entry)
        {
            var param = entry.param;

            float nameWidth   = row.width * 0.5f;
            float typeWidth   = row.width * 0.25f;
            float removeWidth = 24f;
            float valueWidth  = row.width - nameWidth - typeWidth - removeWidth;

            float currentX = row.x;
            var nameRect   = new Rect(currentX, row.y, nameWidth,    row.height); currentX += nameWidth;
            var typeRect   = new Rect(currentX, row.y, typeWidth,    row.height); currentX += typeWidth;
            var valRect    = new Rect(currentX, row.y, valueWidth,   row.height); currentX += valueWidth;
            var removeRect = new Rect(currentX, row.y, removeWidth,  row.height);

            var capturedEntry = entry;
            if (EditorGUI.DropdownButton(nameRect, new GUIContent(string.IsNullOrEmpty(param.name) ? "—" : param.name), FocusType.Keyboard))
                ShowParameterDropdown(nameRect, param.name, newName =>
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, name: newName)));

            var paramType = GetParamType(param.name);
            bool isBool = paramType == AnimatorControllerParameterType.Bool;

            var changeTypes = isBool
                ? new[] { VRC_AvatarParameterDriver.ChangeType.Set, VRC_AvatarParameterDriver.ChangeType.Random }
                : new[] { VRC_AvatarParameterDriver.ChangeType.Set, VRC_AvatarParameterDriver.ChangeType.Add, VRC_AvatarParameterDriver.ChangeType.Random };
            var changeLabels = isBool ? new[] { "Set", "Random" } : new[] { "Set", "Add", "Random" };

            int typeIndex = Mathf.Max(0, Array.IndexOf(changeTypes, param.type));
            EditorGUI.showMixedValue = entry.hasMixedTypes;
            EditorGUI.BeginChangeCheck();
            int newTypeIndex = EditorGUI.Popup(typeRect, typeIndex, changeLabels);
            if (EditorGUI.EndChangeCheck())
                ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, type: changeTypes[newTypeIndex]));
            EditorGUI.showMixedValue = false;

            EditorGUI.showMixedValue = entry.hasMixedValues;
            if (isBool && param.type == VRC_AvatarParameterDriver.ChangeType.Set)
            {
                float toggleWidth = EditorGUIUtility.singleLineHeight;
                var toggleRect = new Rect(valRect.x + (valRect.width - toggleWidth) * 0.5f, valRect.y, toggleWidth, valRect.height);
                EditorGUI.BeginChangeCheck();
                bool newBoolValue = EditorGUI.Toggle(toggleRect, param.value >= 0.5f);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, value: newBoolValue ? 1f : 0f));
            }
            else if (isBool && param.type == VRC_AvatarParameterDriver.ChangeType.Random)
            {
                float labelWidth = 44f;
                GUI.Label(new Rect(valRect.x, valRect.y, labelWidth, valRect.height), "Chance", EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                float newChance = EditorGUI.Slider(new Rect(valRect.x + labelWidth, valRect.y, valRect.width - labelWidth, valRect.height), param.chance, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, chance: newChance));
            }
            else if (param.type == VRC_AvatarParameterDriver.ChangeType.Random)
            {
                float labelWidth = 26f;
                float fieldWidth = (valueWidth - labelWidth * 2f) * 0.5f;
                float valueX = valRect.x;
                GUI.Label(new Rect(valueX, valRect.y, labelWidth, valRect.height), "Min", EditorStyles.miniLabel);
                valueX += labelWidth;
                EditorGUI.BeginChangeCheck();
                float newMin = EditorGUI.FloatField(new Rect(valueX, valRect.y, fieldWidth, valRect.height), param.valueMin);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, valueMin: newMin));
                valueX += fieldWidth;
                GUI.Label(new Rect(valueX, valRect.y, labelWidth, valRect.height), "Max", EditorStyles.miniLabel);
                valueX += labelWidth;
                EditorGUI.BeginChangeCheck();
                float newMax = EditorGUI.FloatField(new Rect(valueX, valRect.y, fieldWidth, valRect.height), param.valueMax);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, valueMax: newMax));
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                float newValue = EditorGUI.FloatField(valRect, param.value);
                if (EditorGUI.EndChangeCheck())
                    ReplaceDriverParam(capturedEntry, CloneParam(capturedEntry.param, value: newValue));
            }
            EditorGUI.showMixedValue = false;

            if (CursorBtn(removeRect, "−", Styles.CondBtn))
                _removeDriverParamIndex = entry.index;
        }

        /* Returns a shallow copy of original with any provided fields overridden. Used to produce immutable replacements for driver parameter rows. */
        static VRC_AvatarParameterDriver.Parameter CloneParam(
            VRC_AvatarParameterDriver.Parameter original,
            string name = null,
            VRC_AvatarParameterDriver.ChangeType? type = null,
            float? value = null,
            float? valueMin = null,
            float? valueMax = null,
            float? chance = null)
        => new VRC_AvatarParameterDriver.Parameter
        {
            name     = name     ?? original.name,
            type     = type     ?? original.type,
            value    = value    ?? original.value,
            valueMin = valueMin ?? original.valueMin,
            valueMax = valueMax ?? original.valueMax,
            chance   = chance   ?? original.chance
        };

        static VRC_AvatarParameterDriver.Parameter DeepCloneParam(
            VRC_AvatarParameterDriver.Parameter original)
        => new VRC_AvatarParameterDriver.Parameter
        {
            name     = original.name,
            type     = original.type,
            value    = original.value,
            valueMin = original.valueMin,
            valueMax = original.valueMax,
            chance   = original.chance
        };

        void ReplaceDriverParam(
            DriverParamEntry entry,
            VRC_AvatarParameterDriver.Parameter replacement)
        {
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null)
                    continue;

                int parameterIndex = FindDriverParamIndex(driver, entry.param, entry.index);
                if (parameterIndex < 0)
                    continue;

                Undo.RecordObject(driver, "Edit Driver Parameter");
                driver.parameters[parameterIndex] = DeepCloneParam(replacement);
                EditorUtility.SetDirty(driver);
            }
        }

        /* Removes entry's parameter from every selected state's driver, destroying the driver component entirely if its list becomes empty. */
        void RemoveDriverParam(DriverParamEntry entry)
        {
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null) continue;
                int parameterIndex = FindDriverParamIndex(driver, entry.param, entry.index);
                if (parameterIndex < 0) continue;
                Undo.RecordObject(driver, "Remove Driver Parameter");
                driver.parameters.RemoveAt(parameterIndex);
                if (driver.parameters.Count == 0)
                {
                    Undo.RegisterCompleteObjectUndo(state, "Remove Driver Parameter");
                    state.behaviours = state.behaviours.Where(b => b != driver).ToArray();
                    Undo.DestroyObjectImmediate(driver);
                }
                EditorUtility.SetDirty(state);
            }
        }

        void AddDriverParam()
        {
            if (_selectedStates.Length > 1) EnsureUniqueDrivers();
            string defaultName = string.Empty;
            if (_controller?.parameters.Length > 0)
            {
                var defaultParam = _controller.parameters[0];
                var usedNames = new HashSet<string>(_selectedStates.SelectMany(state =>
                {
                    var driver = GetDriverForState(state);
                    return driver != null ? driver.parameters.Select(parameter => parameter.name) : Enumerable.Empty<string>();
                }));
                var unusedParam = _controller.parameters.FirstOrDefault(parameter => !usedNames.Contains(parameter.name));
                if (unusedParam != null) defaultParam = unusedParam;
                defaultName = defaultParam.name;
            }
            foreach (var state in _selectedStates)
            {
                var driver = GetOrCreateDriver(state);
                Undo.RecordObject(driver, "Add Driver Parameter");
                driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type  = VRC_AvatarParameterDriver.ChangeType.Set,
                    name  = defaultName,
                    value = 0f
                });
                EditorUtility.SetDirty(driver);
            }
        }

        /* Detects shared VRCAvatarParameterDriver instances across selected states (caused by Unity
           state duplication sharing C++ behaviours arrays). Breaks sharing by destroying all drivers,
           calling SaveAssets to write independent empty behaviours to disk (reimport separates the
           C++ arrays), then recreating unique drivers and restoring the saved parameter data. */
        void EnsureUniqueDrivers()
        {
            var seenIds = new HashSet<int>();
            bool needsRebuild = false;
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null || !seenIds.Add(driver.GetInstanceID()))
                    needsRebuild = true;
            }
            if (!needsRebuild) return;

            var savedParameters  = new Dictionary<AnimatorState, List<VRC_AvatarParameterDriver.Parameter>>();
            var savedLocalOnly   = new Dictionary<AnimatorState, bool>();
            var savedDebugString = new Dictionary<AnimatorState, string>();
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null) continue;
                savedParameters[state]  = new List<VRC_AvatarParameterDriver.Parameter>(driver.parameters);
                savedLocalOnly[state]   = driver.localOnly;
                savedDebugString[state] = driver.debugString ?? string.Empty;
            }

            var destroyedIds = new HashSet<int>();
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Add Driver Parameter");
                state.behaviours = state.behaviours.Where(b => b != driver).ToArray();
                EditorUtility.SetDirty(state);
                if (destroyedIds.Add(driver.GetInstanceID()))
                    Undo.DestroyObjectImmediate(driver);
            }

            // Flush empty behaviours to disk — reimport gives each state an independent
            // C++ behaviours array, permanently breaking the Unity-level sharing.
            AssetDatabase.SaveAssets();

            foreach (var state in _selectedStates)
            {
                var driver = GetOrCreateDriver(state);
                if (!savedParameters.ContainsKey(state)) continue;
                Undo.RecordObject(driver, "Add Driver Parameter");
                driver.parameters  = savedParameters[state];
                driver.localOnly   = savedLocalOnly[state];
                driver.debugString = savedDebugString[state];
                EditorUtility.SetDirty(driver);
            }
        }

        void RemoveDriverFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var driver = GetDriverForState(state);
                if (driver == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Driver");
                state.behaviours = state.behaviours.Where(b => b != driver).ToArray();
                Undo.DestroyObjectImmediate(driver);
                EditorUtility.SetDirty(state);
            }
        }

        /* Returns the index of the parameter in driver.parameters matching target.
           Uses indexHint first (positional match by name) — handles duplicate-name params correctly.
           Falls back to first name match if hint is out of range or name differs. */
        static int FindDriverParamIndex(VRCAvatarParameterDriver driver, VRC_AvatarParameterDriver.Parameter target, int indexHint = -1)
        {
            var parameters = driver.parameters;
            if (indexHint >= 0 && indexHint < parameters.Count && parameters[indexHint].name == target.name)
                return indexHint;
            for (int i = 0; i < parameters.Count; i++)
                if (parameters[i].name == target.name) return i;
            return -1;
        }

        /* Returns true if a and b share the same name, type, and value fields (uses min/max/chance for Random type). */
        static bool DriverParamsMatch(VRC_AvatarParameterDriver.Parameter a, VRC_AvatarParameterDriver.Parameter b)
        {
            if (a.name != b.name || a.type != b.type) return false;
            return b.type == VRC_AvatarParameterDriver.ChangeType.Random
                ? Mathf.Approximately(a.valueMin, b.valueMin) &&
                  Mathf.Approximately(a.valueMax, b.valueMax) &&
                  Mathf.Approximately(a.chance,   b.chance)
                : Mathf.Approximately(a.value, b.value);
        }

        static VRCAvatarParameterDriver GetDriverForState(AnimatorState state)
            => state.behaviours.OfType<VRCAvatarParameterDriver>().FirstOrDefault();

        /* Returns the existing VRCAvatarParameterDriver on state, or adds and registers a new one via Undo. */
        static VRCAvatarParameterDriver GetOrCreateDriver(AnimatorState state)
        {
            var driver = state.behaviours.OfType<VRCAvatarParameterDriver>().FirstOrDefault();
            if (driver != null) return driver;
            driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            Undo.RegisterCreatedObjectUndo(driver, "Create VRC Driver");
            EditorUtility.SetDirty(state);
            return driver;
        }

        // ── VRC Play Audio section ────────────────────────────────────────────

        bool _clipsExpanded = true;
        ReorderableList _clipsReorderList;
        List<AudioClip> _clipsListData;
        int _removeClipIndex = -1;

        ReorderableList _driverParamReorderList;
        List<VRC_AvatarParameterDriver.Parameter> _driverParamListData;
        int _removeDriverParamIndex = -1;

        void DrawVRCPlayAudioSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetAudioForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetAudioForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label("Shared VRC Play Audio", Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn("Add to All", EditorStyles.miniButton, GUILayout.Width(72), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreateAudio(state);
                if (anyHave && CursorBtn("Remove All", EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(24)))
                {
                    RemoveAudioFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawPlayAudioFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void SetAudioOnAll(string undoName, Action<VRCAnimatorPlayAudio> mutate)
        {
            foreach (var state in _selectedStates)
            {
                var audio = GetOrCreateAudio(state);
                Undo.RecordObject(audio, undoName);
                mutate(audio);
                EditorUtility.SetDirty(audio);
            }
        }

        void DrawPlayAudioFields()
        {
            var statesWithAudio = _selectedStates.Where(state => GetAudioForState(state) != null).ToArray();
            var first = GetAudioForState(statesWithAudio[0]);
            bool multi = statesWithAudio.Length > 1;

            DrawAudioSourceDragField();
            DrawAudioSourcePathField(first, statesWithAudio, multi);
            DrawAudioPlaybackOrderField(first, statesWithAudio, multi);
            if (first.PlaybackOrder == VRCAnimatorPlayAudio.Order.Parameter)
                DrawAudioParameterNameField(first, statesWithAudio, multi);
            DrawPlayAudioClipsList(statesWithAudio);
            DrawAudioVolumeFields(first, statesWithAudio, multi);
            DrawAudioPitchFields(first, statesWithAudio, multi);
            DrawAudioLoopField(first, statesWithAudio, multi);
            DrawAudioPlayStopColumnHeaders();
            DrawAudioOnEnterFields(first, statesWithAudio, multi);
            DrawAudioOnExitFields(first, statesWithAudio, multi);
            DrawAudioDelayField(first, statesWithAudio, multi);
        }

        void DrawAudioSourceDragField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("AudioSource", GUILayout.Width(110));
                EditorGUI.BeginChangeCheck();
                var droppedSource = (AudioSource)EditorGUILayout.ObjectField(null, typeof(AudioSource), true);
                if (EditorGUI.EndChangeCheck() && droppedSource != null)
                {
                    var descriptor = droppedSource.GetComponentInParent<VRCAvatarDescriptor>();
                    string resolvedPath = GetAudioSourcePath(droppedSource.transform, descriptor != null ? descriptor.transform : null);
                    SetAudioOnAll("Set Source Path", audio => audio.SourcePath = resolvedPath);
                }
            }
        }

        void DrawAudioSourcePathField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Source Path", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).SourcePath != first.SourcePath);
                EditorGUI.BeginChangeCheck();
                string newPath = EditorGUILayout.TextField(first.SourcePath ?? "");
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Source Path", audio => audio.SourcePath = newPath);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioPlaybackOrderField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Playback Order", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PlaybackOrder != first.PlaybackOrder);
                EditorGUI.BeginChangeCheck();
                var newOrder = (VRCAnimatorPlayAudio.Order)EditorGUILayout.EnumPopup(first.PlaybackOrder);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Playback Order", audio => audio.PlaybackOrder = newOrder);
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).ClipsApplySettings != first.ClipsApplySettings);
                EditorGUI.BeginChangeCheck();
                var newClipsApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.EnumPopup(first.ClipsApplySettings, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Clips Apply Settings", audio => audio.ClipsApplySettings = newClipsApply);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioParameterNameField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Parameter Name", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).ParameterName != first.ParameterName);
                EditorGUI.BeginChangeCheck();
                string newParam = DrawIntParamDropdown(first.ParameterName ?? "");
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Parameter Name", audio => audio.ParameterName = newParam);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioVolumeFields(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Volume", GUILayout.Width(55));
                EditorGUILayout.LabelField("Min", EditorStyles.miniLabel, GUILayout.Width(25));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Volume.x, first.Volume.x));
                EditorGUI.BeginChangeCheck();
                float newVolMin = Mathf.Clamp(EditorGUILayout.FloatField(first.Volume.x), 0f, 1f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Volume Min", audio => audio.Volume = new Vector2(newVolMin, audio.Volume.y));
                EditorGUI.showMixedValue = false;
                EditorGUILayout.LabelField("Max", EditorStyles.miniLabel, GUILayout.Width(25));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Volume.y, first.Volume.y));
                EditorGUI.BeginChangeCheck();
                float newVolMax = Mathf.Clamp(EditorGUILayout.FloatField(first.Volume.y), 0f, 1f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Volume Max", audio => audio.Volume = new Vector2(audio.Volume.x, newVolMax));
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).VolumeApplySettings != first.VolumeApplySettings);
                EditorGUI.BeginChangeCheck();
                var newVolApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.EnumPopup(first.VolumeApplySettings, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Volume Apply Settings", audio => audio.VolumeApplySettings = newVolApply);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioPitchFields(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Pitch", GUILayout.Width(55));
                EditorGUILayout.LabelField("Min", EditorStyles.miniLabel, GUILayout.Width(25));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Pitch.x, first.Pitch.x));
                EditorGUI.BeginChangeCheck();
                float newPitchMin = Mathf.Clamp(EditorGUILayout.FloatField(first.Pitch.x), -3f, 3f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Pitch Min", audio => audio.Pitch = new Vector2(newPitchMin, audio.Pitch.y));
                EditorGUI.showMixedValue = false;
                EditorGUILayout.LabelField("Max", EditorStyles.miniLabel, GUILayout.Width(25));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).Pitch.y, first.Pitch.y));
                EditorGUI.BeginChangeCheck();
                float newPitchMax = Mathf.Clamp(EditorGUILayout.FloatField(first.Pitch.y), -3f, 3f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Pitch Max", audio => audio.Pitch = new Vector2(audio.Pitch.x, newPitchMax));
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PitchApplySettings != first.PitchApplySettings);
                EditorGUI.BeginChangeCheck();
                var newPitchApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.EnumPopup(first.PitchApplySettings, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Pitch Apply Settings", audio => audio.PitchApplySettings = newPitchApply);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioLoopField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Loop", GUILayout.Width(55));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).Loop != first.Loop);
                EditorGUI.BeginChangeCheck();
                bool newLoop = EditorGUILayout.Toggle(first.Loop, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Loop", audio => audio.Loop = newLoop);
                EditorGUI.showMixedValue = false;
                GUILayout.FlexibleSpace();
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).LoopApplySettings != first.LoopApplySettings);
                EditorGUI.BeginChangeCheck();
                var newLoopApply = (VRC_AnimatorPlayAudio.ApplySettings)EditorGUILayout.EnumPopup(first.LoopApplySettings, GUILayout.Width(130));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Loop Apply Settings", audio => audio.LoopApplySettings = newLoopApply);
                EditorGUI.showMixedValue = false;
            }
        }

        static void DrawAudioPlayStopColumnHeaders()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(114);
                GUILayout.Label("Stop", EditorStyles.miniLabel, GUILayout.Width(40));
                GUILayout.Label("Play", EditorStyles.miniLabel, GUILayout.Width(40));
            }
        }

        void DrawAudioOnEnterFields(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("On Enter", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).StopOnEnter != first.StopOnEnter);
                EditorGUI.BeginChangeCheck();
                bool newStopEnter = EditorGUILayout.Toggle(first.StopOnEnter, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Stop On Enter", audio => audio.StopOnEnter = newStopEnter);
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PlayOnEnter != first.PlayOnEnter);
                EditorGUI.BeginChangeCheck();
                bool newPlayEnter = EditorGUILayout.Toggle(first.PlayOnEnter, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Play On Enter", audio => audio.PlayOnEnter = newPlayEnter);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioOnExitFields(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("On Exit", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).StopOnExit != first.StopOnExit);
                EditorGUI.BeginChangeCheck();
                bool newStopExit = EditorGUILayout.Toggle(first.StopOnExit, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Stop On Exit", audio => audio.StopOnExit = newStopExit);
                EditorGUI.showMixedValue = false;
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(state => GetAudioForState(state).PlayOnExit != first.PlayOnExit);
                EditorGUI.BeginChangeCheck();
                bool newPlayExit = EditorGUILayout.Toggle(first.PlayOnExit, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Play On Exit", audio => audio.PlayOnExit = newPlayExit);
                EditorGUI.showMixedValue = false;
            }
        }

        void DrawAudioDelayField(VRCAnimatorPlayAudio first, AnimatorState[] statesWithAudio, bool multi)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Play On Enter Delay In Seconds", GUILayout.Width(220));
                EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => !Mathf.Approximately(GetAudioForState(s).DelayInSeconds, first.DelayInSeconds));
                EditorGUI.BeginChangeCheck();
                float newDelay = Mathf.Clamp(EditorGUILayout.FloatField(first.DelayInSeconds), 0f, 60f);
                if (EditorGUI.EndChangeCheck()) SetAudioOnAll("Edit Play Delay", audio => audio.DelayInSeconds = newDelay);
                EditorGUI.showMixedValue = false;
            }
        }

        /* Draws the foldable clips list with a size int field and a ReorderableList for editing, reordering, and removing audio clips across all statesWithAudio. */
        void DrawPlayAudioClipsList(AnimatorState[] statesWithAudio)
        {
            var first = GetAudioForState(statesWithAudio[0]);
            bool multi = statesWithAudio.Length > 1;
            var clips = first.Clips ?? Array.Empty<AudioClip>();
            float rowHeight = EditorGUIUtility.singleLineHeight;

            // Outer container — single background covers foldout header + list body
            var outerRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && outerRect.height > 0)
                EditorGUI.DrawRect(outerRect, Styles.SecondaryColor);

            // Foldout header + size int field — now inside the background
            var headerRow = EditorGUILayout.GetControlRect(false, rowHeight);
            const float sizeWidth = 40f;
            var foldoutRect = new Rect(headerRow.x, headerRow.y, headerRow.width - sizeWidth - 4f, rowHeight);
            _clipsExpanded = EditorGUI.Foldout(foldoutRect, _clipsExpanded, "Clips", true, EditorStyles.foldout);
            EditorGUIUtility.AddCursorRect(foldoutRect, MouseCursor.Link);

            EditorGUI.showMixedValue = multi && statesWithAudio.Any(s => (GetAudioForState(s).Clips?.Length ?? 0) != clips.Length);
            EditorGUI.BeginChangeCheck();
            int newSize = Mathf.Max(0, EditorGUI.IntField(new Rect(headerRow.xMax - sizeWidth, headerRow.y, sizeWidth, rowHeight), clips.Length));
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var state in _selectedStates)
                {
                    var audio = GetOrCreateAudio(state);
                    Undo.RecordObject(audio, "Resize Clips");
                    var resized = new AudioClip[newSize];
                    if (audio.Clips != null) Array.Copy(audio.Clips, resized, Mathf.Min(audio.Clips.Length, newSize));
                    audio.Clips = resized;
                    EditorUtility.SetDirty(audio);
                }
                clips = first.Clips ?? Array.Empty<AudioClip>();
                _clipsListData = null;
                _clipsReorderList = null;
            }
            EditorGUI.showMixedValue = false;

            if (_clipsExpanded)
            {
                // Keep _clipsListData in sync with current clips
                if (_clipsListData == null || _clipsListData.Count != clips.Length)
                    _clipsListData = new List<AudioClip>(clips);
                else
                    for (int i = 0; i < clips.Length; i++)
                        _clipsListData[i] = clips[i];

                // Build ReorderableList once; rebuilt when nulled
                if (_clipsReorderList == null)
                {
                    _clipsReorderList = new ReorderableList(_clipsListData, typeof(AudioClip), true, false, false, false)
                    {
                        elementHeight = rowHeight,
                        showDefaultBackground = false,
                        footerHeight = 0f,
                    };

                    _clipsReorderList.drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        if (index >= _clipsListData.Count) return;
                        var localStates = _selectedStates.Where(state => GetAudioForState(state) != null).ToArray();
                        bool localMulti = localStates.Length > 1;

                        EditorGUI.showMixedValue = localMulti && localStates.Any(state => {
                            var audio = GetAudioForState(state);
                            return audio.Clips == null || index >= audio.Clips.Length || audio.Clips[index] != _clipsListData[index];
                        });
                        EditorGUI.BeginChangeCheck();
                        var newClip = (AudioClip)EditorGUI.ObjectField(
                            new Rect(rect.x, rect.y + 1f, rect.width - 26f, rect.height - 2f),
                            _clipsListData[index], typeof(AudioClip), false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            _clipsListData[index] = newClip;
                            int capturedIndex = index;
                            foreach (var state in _selectedStates)
                            {
                                var audio = GetOrCreateAudio(state);
                                if (audio.Clips == null || capturedIndex >= audio.Clips.Length)
                                {
                                    var expanded = new AudioClip[capturedIndex + 1];
                                    audio.Clips?.CopyTo(expanded, 0);
                                    audio.Clips = expanded;
                                }
                                Undo.RecordObject(audio, "Edit Audio Clip");
                                audio.Clips[capturedIndex] = newClip;
                                EditorUtility.SetDirty(audio);
                            }
                        }
                        EditorGUI.showMixedValue = false;

                        if (GUI.Button(new Rect(rect.xMax - 24f, rect.y + 1f, 24f, rect.height - 2f), "−", Styles.CondBtn))
                            _removeClipIndex = index;
                    };

                    _clipsReorderList.onReorderCallbackWithDetails = (reorderableList, oldIndex, newIndex) =>
                    {
                        var firstAudio = GetAudioForState(_selectedStates[0]);
                        if (firstAudio != null)
                        {
                            Undo.RecordObject(firstAudio, "Reorder Clips");
                            firstAudio.Clips = _clipsListData.ToArray();
                            EditorUtility.SetDirty(firstAudio);
                        }
                        for (int stateIndex = 1; stateIndex < _selectedStates.Length; stateIndex++)
                        {
                            var audio = GetOrCreateAudio(_selectedStates[stateIndex]);
                            if (audio.Clips == null || audio.Clips.Length < 2) continue;
                            Undo.RecordObject(audio, "Reorder Clips");
                            var stateClips = audio.Clips.ToList();
                            if (oldIndex < stateClips.Count)
                            {
                                var item = stateClips[oldIndex];
                                stateClips.RemoveAt(oldIndex);
                                stateClips.Insert(Mathf.Clamp(newIndex, 0, stateClips.Count), item);
                                audio.Clips = stateClips.ToArray();
                            }
                            EditorUtility.SetDirty(audio);
                        }
                    };
                }

                if (clips.Length == 0)
                    EditorGUILayout.LabelField("List is Empty", Styles.EmptyLabel);
                else
                    _clipsReorderList.DoLayoutList();

                // Deferred remove — avoids layout mismatch from inside drawElementCallback
                if (_removeClipIndex >= 0)
                {
                    int capturedIndex = _removeClipIndex;
                    _removeClipIndex = -1;
                    foreach (var state in _selectedStates)
                    {
                        var audio = GetOrCreateAudio(state);
                        if (audio.Clips == null || capturedIndex >= audio.Clips.Length) continue;
                        Undo.RecordObject(audio, "Remove Audio Clip");
                        audio.Clips = audio.Clips.Where((_, idx) => idx != capturedIndex).ToArray();
                        EditorUtility.SetDirty(audio);
                    }
                    _clipsReorderList = null;
                }
                else
                {
                    var addRow = EditorGUILayout.GetControlRect(false, rowHeight);
                    if (CursorBtn(new Rect(addRow.xMax - 24f, addRow.y, 24f, rowHeight), "+", Styles.CondBtn))
                    {
                        foreach (var state in _selectedStates)
                        {
                            var audio = GetOrCreateAudio(state);
                            Undo.RecordObject(audio, "Add Audio Clip");
                            var expanded = new AudioClip[(audio.Clips?.Length ?? 0) + 1];
                            audio.Clips?.CopyTo(expanded, 0);
                            audio.Clips = expanded;
                            EditorUtility.SetDirty(audio);
                        }
                        _clipsReorderList = null;
                    }
                }

                GUILayout.Space(4f);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4f);
        }

        static VRCAnimatorPlayAudio GetAudioForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorPlayAudio>().FirstOrDefault();

        /* Returns the existing VRCAnimatorPlayAudio on state, or adds and registers a new one via Undo. */
        static VRCAnimatorPlayAudio GetOrCreateAudio(AnimatorState state)
        {
            var audio = state.behaviours.OfType<VRCAnimatorPlayAudio>().FirstOrDefault();
            if (audio != null) return audio;
            audio = state.AddStateMachineBehaviour<VRCAnimatorPlayAudio>();
            Undo.RegisterCreatedObjectUndo(audio, "Create VRC Play Audio");
            EditorUtility.SetDirty(state);
            return audio;
        }

        // ── VRC Tracking Control section ──────────────────────────────────────

        void DrawVRCTrackingSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetTrackingForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetTrackingForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label("Shared VRC Tracking Control", Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn("Add to All", EditorStyles.miniButton, GUILayout.Width(72), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreateTracking(state);
                if (anyHave && CursorBtn("Remove All", EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(24)))
                {
                    RemoveTrackingFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawTrackingFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawTrackingFields()
        {
            var statesWithTracking = _selectedStates.Where(state => GetTrackingForState(state) != null).ToArray();
            var first = GetTrackingForState(statesWithTracking[0]);
            bool multi = statesWithTracking.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(114);
                GUILayout.Label("No Change", EditorStyles.miniLabel, GUILayout.Width(70));
                GUILayout.Label("Tracking",  EditorStyles.miniLabel, GUILayout.Width(70));
                GUILayout.Label("Animation", EditorStyles.miniLabel, GUILayout.Width(70));
            }

            // Set All row
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Set All", GUILayout.Width(110));
                DrawSetAllTrackingRadio(statesWithTracking, VRC_AnimatorTrackingControl.TrackingType.NoChange,  70f);
                DrawSetAllTrackingRadio(statesWithTracking, VRC_AnimatorTrackingControl.TrackingType.Tracking,  70f);
                DrawSetAllTrackingRadio(statesWithTracking, VRC_AnimatorTrackingControl.TrackingType.Animation, 70f);
            }
            EditorGUILayout.Space(2f);

            DrawTrackingRow("Head",           statesWithTracking, audio => audio.trackingHead,         (a, v) => a.trackingHead         = v);
            DrawTrackingRow("Left Hand",      statesWithTracking, audio => audio.trackingLeftHand,      (a, v) => a.trackingLeftHand     = v);
            DrawTrackingRow("Right Hand",     statesWithTracking, audio => audio.trackingRightHand,     (a, v) => a.trackingRightHand    = v);
            DrawTrackingRow("Hip",            statesWithTracking, audio => audio.trackingHip,           (a, v) => a.trackingHip          = v);
            DrawTrackingRow("Left Foot",      statesWithTracking, audio => audio.trackingLeftFoot,      (a, v) => a.trackingLeftFoot     = v);
            DrawTrackingRow("Right Foot",     statesWithTracking, audio => audio.trackingRightFoot,     (a, v) => a.trackingRightFoot    = v);
            DrawTrackingRow("Left Fingers",   statesWithTracking, audio => audio.trackingLeftFingers,   (a, v) => a.trackingLeftFingers  = v);
            DrawTrackingRow("Right Fingers",  statesWithTracking, audio => audio.trackingRightFingers,  (a, v) => a.trackingRightFingers = v);
            DrawTrackingRow("Eyes & Eyelids", statesWithTracking, audio => audio.trackingEyes,          (a, v) => a.trackingEyes         = v);
            DrawTrackingRow("Mouth & Jaw",    statesWithTracking, audio => audio.trackingMouth,         (a, v) => a.trackingMouth        = v);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Debug String", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithTracking.Any(state => GetTrackingForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var tracking = GetOrCreateTracking(state);
                        Undo.RecordObject(tracking, "Edit Debug String");
                        tracking.debugString = newDebugString;
                        EditorUtility.SetDirty(tracking);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        /* Draws a single tracking body-part row with label and three radio toggles (NoChange/Tracking/Animation), applying set to all selected states on change. */
        void DrawTrackingRow(
            string label,
            AnimatorState[] statesWithTracking,
            Func<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> get,
            Action<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> set)
        {
            var firstVal = get(GetTrackingForState(statesWithTracking[0]));
            bool mixed = statesWithTracking.Length > 1 && statesWithTracking.Any(state => get(GetTrackingForState(state)) != firstVal);

            Color labelColor = mixed
                ? new Color(0.4f, 0.7f, 1f)
                : firstVal == VRC_AnimatorTrackingControl.TrackingType.Tracking  ? new Color(0.4f, 0.9f, 0.4f)
                : firstVal == VRC_AnimatorTrackingControl.TrackingType.Animation ? new Color(1f, 0.85f, 0.2f)
                : Color.white;

            using (new EditorGUILayout.HorizontalScope())
            {
                var prevColor = GUI.color;
                GUI.color = labelColor;
                EditorGUILayout.LabelField(label, GUILayout.Width(110));
                GUI.color = prevColor;
                DrawTrackingRadio(statesWithTracking, get, set, VRC_AnimatorTrackingControl.TrackingType.NoChange,  firstVal, mixed, 70f);
                DrawTrackingRadio(statesWithTracking, get, set, VRC_AnimatorTrackingControl.TrackingType.Tracking,  firstVal, mixed, 70f);
                DrawTrackingRadio(statesWithTracking, get, set, VRC_AnimatorTrackingControl.TrackingType.Animation, firstVal, mixed, 70f);
            }
        }

        /* Draws one radio Toggle for targetType; sets all selected states to targetType via set when clicked while not already selected. */
        void DrawTrackingRadio(
            AnimatorState[] statesWithTracking,
            Func<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> get,
            Action<VRCAnimatorTrackingControl, VRC_AnimatorTrackingControl.TrackingType> set,
            VRC_AnimatorTrackingControl.TrackingType targetType,
            VRC_AnimatorTrackingControl.TrackingType currentVal,
            bool mixed,
            float width)
        {
            bool isSelected = !mixed && currentVal == targetType;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Toggle(isSelected, GUILayout.Width(width));
            if (EditorGUI.EndChangeCheck() && !isSelected)
            {
                foreach (var state in _selectedStates)
                {
                    var tracking = GetOrCreateTracking(state);
                    Undo.RecordObject(tracking, "Edit Tracking Control");
                    set(tracking, targetType);
                    EditorUtility.SetDirty(tracking);
                }
            }
        }

        static VRCAnimatorTrackingControl GetTrackingForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorTrackingControl>().FirstOrDefault();

        /* Returns the existing VRCAnimatorTrackingControl on state, or adds and registers a new one via Undo. */
        static VRCAnimatorTrackingControl GetOrCreateTracking(AnimatorState state)
        {
            var tracking = state.behaviours.OfType<VRCAnimatorTrackingControl>().FirstOrDefault();
            if (tracking != null) return tracking;
            tracking = state.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
            Undo.RegisterCreatedObjectUndo(tracking, "Create VRC Tracking Control");
            EditorUtility.SetDirty(state);
            return tracking;
        }

        /* Draws a "Set All" radio toggle that sets every tracking field on all selected states to targetType when clicked. */
        void DrawSetAllTrackingRadio(
            AnimatorState[] statesWithTracking,
            VRC_AnimatorTrackingControl.TrackingType targetType,
            float width)
        {
            bool allMatch = statesWithTracking.All(state => TrackingAllFieldsAre(GetTrackingForState(state), targetType));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Toggle(allMatch, GUILayout.Width(width));
            if (EditorGUI.EndChangeCheck() && !allMatch)
            {
                foreach (var state in _selectedStates)
                {
                    var tracking = GetOrCreateTracking(state);
                    Undo.RecordObject(tracking, "Set All Tracking");
                    TrackingSetAllFields(tracking, targetType);
                    EditorUtility.SetDirty(tracking);
                }
            }
        }

        /* Returns true if every tracking field on ctrl equals type, used to determine "Set All" radio state. */
        static bool TrackingAllFieldsAre(VRCAnimatorTrackingControl ctrl, VRC_AnimatorTrackingControl.TrackingType type)
            => ctrl.trackingHead == type && ctrl.trackingLeftHand == type && ctrl.trackingRightHand == type
            && ctrl.trackingHip == type && ctrl.trackingLeftFoot == type && ctrl.trackingRightFoot == type
            && ctrl.trackingLeftFingers == type && ctrl.trackingRightFingers == type
            && ctrl.trackingEyes == type && ctrl.trackingMouth == type;

        /* Sets every tracking body-part field on ctrl to type in a single statement. */
        static void TrackingSetAllFields(VRCAnimatorTrackingControl ctrl, VRC_AnimatorTrackingControl.TrackingType type)
        {
            ctrl.trackingHead = ctrl.trackingLeftHand = ctrl.trackingRightHand = ctrl.trackingHip =
            ctrl.trackingLeftFoot = ctrl.trackingRightFoot = ctrl.trackingLeftFingers =
            ctrl.trackingRightFingers = ctrl.trackingEyes = ctrl.trackingMouth = type;
        }

        void RemoveAudioFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var audio = GetAudioForState(state);
                if (audio == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Play Audio");
                state.behaviours = state.behaviours.Where(b => b != audio).ToArray();
                Undo.DestroyObjectImmediate(audio);
                EditorUtility.SetDirty(state);
            }
        }

        void RemoveTrackingFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var tracking = GetTrackingForState(state);
                if (tracking == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Tracking Control");
                state.behaviours = state.behaviours.Where(b => b != tracking).ToArray();
                Undo.DestroyObjectImmediate(tracking);
                EditorUtility.SetDirty(state);
            }
        }

        /* Builds a forward-slash path from sourceTransform up to root (exclusive). Returns "/name" prefixed with slash when root is null, indicating no avatar descriptor was found. */
        static string GetAudioSourcePath(Transform sourceTransform, Transform root)
        {
            string path = sourceTransform.name;
            for (Transform parentTransform = sourceTransform.parent; parentTransform != null && parentTransform != root; parentTransform = parentTransform.parent)
                path = parentTransform.name + "/" + path;
            return root == null ? "/" + path : path;
        }

        // ── VRC Locomotion Control section ────────────────────────────────────

        void DrawVRCLocomotionSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetLocomotionForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetLocomotionForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label("Shared VRC Locomotion Control", Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn("Add to All", EditorStyles.miniButton, GUILayout.Width(72), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreateLocomotion(state);
                if (anyHave && CursorBtn("Remove All", EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(24)))
                {
                    RemoveLocomotionFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawLocomotionFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawLocomotionFields()
        {
            var statesWithLocomotion = _selectedStates.Where(state => GetLocomotionForState(state) != null).ToArray();
            var first = GetLocomotionForState(statesWithLocomotion[0]);
            bool multi = statesWithLocomotion.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                bool mixedDisable = multi && statesWithLocomotion.Any(state => GetLocomotionForState(state).disableLocomotion != first.disableLocomotion);
                EditorGUILayout.LabelField("Locomotion", GUILayout.Width(110));
                DrawBoolToggleButtons(first.disableLocomotion, mixedDisable, "Disable", "Enable", 60f, isDisabled =>
                {
                    foreach (var state in _selectedStates)
                    {
                        var locomotion = GetOrCreateLocomotion(state);
                        Undo.RecordObject(locomotion, "Edit Locomotion Control");
                        locomotion.disableLocomotion = isDisabled;
                        EditorUtility.SetDirty(locomotion);
                    }
                });
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Debug String", GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithLocomotion.Any(state => GetLocomotionForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var locomotion = GetOrCreateLocomotion(state);
                        Undo.RecordObject(locomotion, "Edit Debug String");
                        locomotion.debugString = newDebugString;
                        EditorUtility.SetDirty(locomotion);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        static VRCAnimatorLocomotionControl GetLocomotionForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorLocomotionControl>().FirstOrDefault();

        static VRCAnimatorLocomotionControl GetOrCreateLocomotion(AnimatorState state)
        {
            var locomotion = state.behaviours.OfType<VRCAnimatorLocomotionControl>().FirstOrDefault();
            if (locomotion != null) return locomotion;
            locomotion = state.AddStateMachineBehaviour<VRCAnimatorLocomotionControl>();
            Undo.RegisterCreatedObjectUndo(locomotion, "Create VRC Locomotion Control");
            EditorUtility.SetDirty(state);
            return locomotion;
        }

        void RemoveLocomotionFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var locomotion = GetLocomotionForState(state);
                if (locomotion == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Locomotion Control");
                state.behaviours = state.behaviours.Where(b => b != locomotion).ToArray();
                Undo.DestroyObjectImmediate(locomotion);
                EditorUtility.SetDirty(state);
            }
        }

        // ── VRC Animator Layer Control section ────────────────────────────────

        void DrawVRCLayerControlSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetLayerControlForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetLayerControlForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label("Shared VRC Animator Layer Control", Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn("Add to All", EditorStyles.miniButton, GUILayout.Width(72), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreateLayerControl(state);
                if (anyHave && CursorBtn("Remove All", EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(24)))
                {
                    RemoveLayerControlFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawLayerControlFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawLayerControlFields()
        {
            var statesWithControl = _selectedStates.Where(state => GetLayerControlForState(state) != null).ToArray();
            var first = GetLayerControlForState(statesWithControl[0]);
            bool multi = statesWithControl.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Playable", "Playable layer to affect"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetLayerControlForState(state).playable != first.playable);
                EditorGUI.BeginChangeCheck();
                var newPlayable = (VRC_AnimatorLayerControl.BlendableLayer)EditorGUILayout.EnumPopup(first.playable);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Layer Control Playable");
                        control.playable = newPlayable;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Layer", "Index of sub-layer to affect"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetLayerControlForState(state).layer != first.layer);
                EditorGUI.BeginChangeCheck();
                int newLayer = Mathf.Max(0, EditorGUILayout.IntField(first.layer));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Layer Control Layer");
                        control.layer = newLayer;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Goal Weight", "Goal weight 0-1"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(GetLayerControlForState(state).goalWeight, first.goalWeight));
                EditorGUI.BeginChangeCheck();
                float newGoalWeight = EditorGUILayout.Slider(first.goalWeight, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Layer Control Goal Weight");
                        control.goalWeight = newGoalWeight;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Blend Duration", "Time to reach goal weight, should be less than animation length"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(GetLayerControlForState(state).blendDuration, first.blendDuration));
                EditorGUI.BeginChangeCheck();
                float newBlendDuration = Mathf.Max(0f, EditorGUILayout.FloatField(first.blendDuration));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Layer Control Blend Duration");
                        control.blendDuration = newBlendDuration;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Debug String", "Message for debugging"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetLayerControlForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreateLayerControl(state);
                        Undo.RecordObject(control, "Edit Debug String");
                        control.debugString = newDebugString;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        static VRCAnimatorLayerControl GetLayerControlForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorLayerControl>().FirstOrDefault();

        static VRCAnimatorLayerControl GetOrCreateLayerControl(AnimatorState state)
        {
            var control = state.behaviours.OfType<VRCAnimatorLayerControl>().FirstOrDefault();
            if (control != null) return control;
            control = state.AddStateMachineBehaviour<VRCAnimatorLayerControl>();
            Undo.RegisterCreatedObjectUndo(control, "Create VRC Animator Layer Control");
            EditorUtility.SetDirty(state);
            return control;
        }

        void RemoveLayerControlFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var control = GetLayerControlForState(state);
                if (control == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Animator Layer Control");
                state.behaviours = state.behaviours.Where(b => b != control).ToArray();
                Undo.DestroyObjectImmediate(control);
                EditorUtility.SetDirty(state);
            }
        }

        // ── VRC Playable Layer Control section ────────────────────────────────

        void DrawVRCPlayableLayerSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetPlayableLayerForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetPlayableLayerForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label("Shared VRC Playable Layer Control", Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn("Add to All", EditorStyles.miniButton, GUILayout.Width(72), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreatePlayableLayer(state);
                if (anyHave && CursorBtn("Remove All", EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(24)))
                {
                    RemovePlayableLayerFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawPlayableLayerFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawPlayableLayerFields()
        {
            var statesWithControl = _selectedStates.Where(state => GetPlayableLayerForState(state) != null).ToArray();
            var first = GetPlayableLayerForState(statesWithControl[0]);
            bool multi = statesWithControl.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Layer", "Layer to affect"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetPlayableLayerForState(state).layer != first.layer);
                EditorGUI.BeginChangeCheck();
                var newLayer = (VRC_PlayableLayerControl.BlendableLayer)EditorGUILayout.EnumPopup(first.layer);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreatePlayableLayer(state);
                        Undo.RecordObject(control, "Edit Playable Layer");
                        control.layer = newLayer;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Goal Weight", "Goal weight 0-1"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(GetPlayableLayerForState(state).goalWeight, first.goalWeight));
                EditorGUI.BeginChangeCheck();
                float newGoalWeight = EditorGUILayout.Slider(first.goalWeight, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreatePlayableLayer(state);
                        Undo.RecordObject(control, "Edit Playable Layer Goal Weight");
                        control.goalWeight = newGoalWeight;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Blend Duration", "Time to reach goal weight"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => !Mathf.Approximately(GetPlayableLayerForState(state).blendDuration, first.blendDuration));
                EditorGUI.BeginChangeCheck();
                float newBlendDuration = Mathf.Max(0f, EditorGUILayout.FloatField(first.blendDuration));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreatePlayableLayer(state);
                        Undo.RecordObject(control, "Edit Playable Layer Blend Duration");
                        control.blendDuration = newBlendDuration;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Debug String", "Message for debugging"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithControl.Any(state => GetPlayableLayerForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var control = GetOrCreatePlayableLayer(state);
                        Undo.RecordObject(control, "Edit Debug String");
                        control.debugString = newDebugString;
                        EditorUtility.SetDirty(control);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        static VRCPlayableLayerControl GetPlayableLayerForState(AnimatorState state)
            => state.behaviours.OfType<VRCPlayableLayerControl>().FirstOrDefault();

        static VRCPlayableLayerControl GetOrCreatePlayableLayer(AnimatorState state)
        {
            var control = state.behaviours.OfType<VRCPlayableLayerControl>().FirstOrDefault();
            if (control != null) return control;
            control = state.AddStateMachineBehaviour<VRCPlayableLayerControl>();
            Undo.RegisterCreatedObjectUndo(control, "Create VRC Playable Layer Control");
            EditorUtility.SetDirty(state);
            return control;
        }

        void RemovePlayableLayerFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var control = GetPlayableLayerForState(state);
                if (control == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Playable Layer Control");
                state.behaviours = state.behaviours.Where(b => b != control).ToArray();
                Undo.DestroyObjectImmediate(control);
                EditorUtility.SetDirty(state);
            }
        }

        // ── VRC Temporary Pose Space section ─────────────────────────────────

        void DrawVRCPoseSpaceSection()
        {
            bool allHave = _selectedStates.Length > 0 && _selectedStates.All(state => GetPoseSpaceForState(state) != null);
            bool anyHave = _selectedStates.Any(state => GetPoseSpaceForState(state) != null);

            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                GUILayout.Label("Shared VRC Temporary Pose Space", Styles.BehaviorSectionLabel, GUILayout.Height(24));
                GUILayout.FlexibleSpace();
                if (!allHave && CursorBtn("Add to All", EditorStyles.miniButton, GUILayout.Width(72), GUILayout.Height(24)))
                    foreach (var state in _selectedStates)
                        GetOrCreatePoseSpace(state);
                if (anyHave && CursorBtn("Remove All", EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(24)))
                {
                    RemovePoseSpaceFromAll();
                    anyHave = false;
                }
            }

            if (!anyHave) return;

            const float pad = 6f;
            var bodyRect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint && bodyRect.height > 0)
                EditorGUI.DrawRect(bodyRect, Styles.SecondaryColor);

            GUILayout.Space(pad);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.BeginVertical();

            DrawPoseSpaceFields();

            EditorGUILayout.EndVertical();
            GUILayout.Space(pad);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(pad);
            EditorGUILayout.EndVertical();
        }

        void DrawPoseSpaceFields()
        {
            var statesWithPoseSpace = _selectedStates.Where(state => GetPoseSpaceForState(state) != null).ToArray();
            var first = GetPoseSpaceForState(statesWithPoseSpace[0]);
            bool multi = statesWithPoseSpace.Length > 1;

            using (new EditorGUILayout.HorizontalScope())
            {
                bool mixedEnter = multi && statesWithPoseSpace.Any(state => GetPoseSpaceForState(state).enterPoseSpace != first.enterPoseSpace);
                EditorGUILayout.LabelField(new GUIContent("Pose Space", "Enter or exit a pose space based on the avatar's current pose."), GUILayout.Width(110));
                DrawBoolToggleButtons(first.enterPoseSpace, mixedEnter, "Enter", "Exit", 60f, isEnter =>
                {
                    foreach (var state in _selectedStates)
                    {
                        var poseSpace = GetOrCreatePoseSpace(state);
                        Undo.RecordObject(poseSpace, "Edit Pose Space");
                        poseSpace.enterPoseSpace = isEnter;
                        EditorUtility.SetDirty(poseSpace);
                    }
                });
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Fixed Delay", "Is the delay fixed or normalized."), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithPoseSpace.Any(state => GetPoseSpaceForState(state).fixedDelay != first.fixedDelay);
                EditorGUI.BeginChangeCheck();
                bool newFixedDelay = EditorGUILayout.Toggle(first.fixedDelay, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var poseSpace = GetOrCreatePoseSpace(state);
                        Undo.RecordObject(poseSpace, "Edit Fixed Delay");
                        poseSpace.fixedDelay = newFixedDelay;
                        EditorUtility.SetDirty(poseSpace);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(first.fixedDelay ? "Delay Time (s)" : "Delay Time (%)", "Delay before applying."), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithPoseSpace.Any(state => !Mathf.Approximately(GetPoseSpaceForState(state).delayTime, first.delayTime));
                EditorGUI.BeginChangeCheck();
                float newDelayTime = EditorGUILayout.FloatField(first.delayTime);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var poseSpace = GetOrCreatePoseSpace(state);
                        Undo.RecordObject(poseSpace, "Edit Delay Time");
                        poseSpace.delayTime = newDelayTime;
                        EditorUtility.SetDirty(poseSpace);
                    }
                }
                EditorGUI.showMixedValue = false;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Debug String", "Message for debugging"), GUILayout.Width(110));
                EditorGUI.showMixedValue = multi && statesWithPoseSpace.Any(state => GetPoseSpaceForState(state).debugString != first.debugString);
                EditorGUI.BeginChangeCheck();
                string newDebugString = EditorGUILayout.TextField(first.debugString ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (var state in _selectedStates)
                    {
                        var poseSpace = GetOrCreatePoseSpace(state);
                        Undo.RecordObject(poseSpace, "Edit Debug String");
                        poseSpace.debugString = newDebugString;
                        EditorUtility.SetDirty(poseSpace);
                    }
                }
                EditorGUI.showMixedValue = false;
            }
        }

        static VRCAnimatorTemporaryPoseSpace GetPoseSpaceForState(AnimatorState state)
            => state.behaviours.OfType<VRCAnimatorTemporaryPoseSpace>().FirstOrDefault();

        static VRCAnimatorTemporaryPoseSpace GetOrCreatePoseSpace(AnimatorState state)
        {
            var poseSpace = state.behaviours.OfType<VRCAnimatorTemporaryPoseSpace>().FirstOrDefault();
            if (poseSpace != null) return poseSpace;
            poseSpace = state.AddStateMachineBehaviour<VRCAnimatorTemporaryPoseSpace>();
            Undo.RegisterCreatedObjectUndo(poseSpace, "Create VRC Temporary Pose Space");
            EditorUtility.SetDirty(state);
            return poseSpace;
        }

        void RemovePoseSpaceFromAll()
        {
            foreach (var state in _selectedStates)
            {
                var poseSpace = GetPoseSpaceForState(state);
                if (poseSpace == null) continue;
                Undo.RegisterCompleteObjectUndo(state, "Remove VRC Temporary Pose Space");
                state.behaviours = state.behaviours.Where(b => b != poseSpace).ToArray();
                Undo.DestroyObjectImmediate(poseSpace);
                EditorUtility.SetDirty(state);
            }
        }
    }
}
#endif
