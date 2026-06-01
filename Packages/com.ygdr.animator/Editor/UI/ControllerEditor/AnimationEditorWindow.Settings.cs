#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YGDR.Editor.Animation
{
    internal partial class AnimationEditorWindow
    {
        bool _interfaceOpen;
        bool _graphGridOpen;
        bool _nodeIconsOpen;
        bool _transitionOverlayOpen;
        bool _nodeColorsOpen;
        bool _transitionDefaultsOpen;
        bool _stateDefaultsOpen;
        bool _miscOpen;

        void DrawSettingsTab()
        {
            var settings = AnimatorDefaultSettings.Load();
            DrawInterfaceSection(settings);
            EditorGUILayout.Space(4);
            DrawGraphGridSection(settings);
            EditorGUILayout.Space(4);
            DrawNodeColorsSection(settings);
            EditorGUILayout.Space(4);
            DrawOverlaySection(settings);
            EditorGUILayout.Space(4);
            DrawTransitionOverlaySection(settings);
            EditorGUILayout.Space(4);
            DrawTransitionDefaultsSection(settings);
            EditorGUILayout.Space(4);
            DrawStateDefaultsSection(settings);
            EditorGUILayout.Space(4);
            DrawMiscellaneousSection(settings);
        }

        // ── Interface palette ─────────────────────────────────────────────────

        void DrawInterfaceSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_interfaceOpen ? "▼ " : "▶ ") + "Interface", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _interfaceOpen = !_interfaceOpen;
                GUILayout.FlexibleSpace();
                if (CursorBtn("Reset", Styles.IconBtn, GUILayout.Width(48), GUILayout.Height(24)))
                {
                    settings.ResetPalette();
                    Styles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
                    settings.Save();
                }
            }

            if (!_interfaceOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            float lineHeight = EditorGUIUtility.singleLineHeight;
            var ifRow1Rect = EditorGUILayout.GetControlRect(false, lineHeight);
            var ifRow2Rect = EditorGUILayout.GetControlRect(false, lineHeight);
            float ifColWidth = ifRow1Rect.width / 4f;

            DrawOverlayToggle(new Rect(ifRow1Rect.x + 0 * ifColWidth, ifRow1Rect.y, ifColWidth, lineHeight), "Layer Indicators", ref settings.showLayerWDIndicator, settings);
            DrawOverlayToggle(new Rect(ifRow1Rect.x + 1 * ifColWidth, ifRow1Rect.y, ifColWidth, lineHeight), "Type Icons",       ref settings.showParamTypeIcons,   settings);
            DrawOverlayToggle(new Rect(ifRow1Rect.x + 2 * ifColWidth, ifRow1Rect.y, ifColWidth, lineHeight), "VRC Icons",        ref settings.showParamVrcIcons,    settings);
            DrawOverlayToggle(new Rect(ifRow1Rect.x + 3 * ifColWidth, ifRow1Rect.y, ifColWidth, lineHeight), "AAP Icons",        ref settings.showParamAapIcons,    settings);

            DrawOverlayToggle(new Rect(ifRow2Rect.x + 0 * ifColWidth, ifRow2Rect.y, ifColWidth, lineHeight), "Graph Footer",     ref settings.showGraphFooter,            settings);
            DrawOverlayToggle(new Rect(ifRow2Rect.x + 1 * ifColWidth, ifRow2Rect.y, ifColWidth, lineHeight), "VRC Comp Icons",  ref settings.showParamVrcComponentIcons, settings);
            DrawOverlayToggle(new Rect(ifRow2Rect.x + 2 * ifColWidth, ifRow2Rect.y, ifColWidth, lineHeight), "Param Budget",    ref settings.showParamBudget,            settings);
            DrawOverlayToggle(new Rect(ifRow2Rect.x + 3 * ifColWidth, ifRow2Rect.y, ifColWidth, lineHeight), "Empty Params",    ref settings.showParamUnusedIcon,        settings);
            EditorGUILayout.Space(6);
            DrawPaletteColorRow("Primary",   ref settings.paletteColorPrimary,   AnimatorDefaultSettings.DefaultPrimary,   settings);
            DrawPaletteColorRow("Secondary", ref settings.paletteColorSecondary, AnimatorDefaultSettings.DefaultSecondary, settings);
            DrawPaletteColorRow("Accent",    ref settings.paletteColorAccent,    AnimatorDefaultSettings.DefaultAccent,    settings);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Parameter Type / VRC Icon Colors", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!settings.showParamTypeIcons))
            {
                DrawNodeColorRow("Float",   ref settings.paramColorFloat,   new Color(0.35f, 0.75f, 0.35f, 1f), settings);
                DrawNodeColorRow("Int",     ref settings.paramColorInt,     new Color(0.35f, 0.60f, 1.00f, 1f), settings);
                DrawNodeColorRow("Bool",    ref settings.paramColorBool,    new Color(1.00f, 0.55f, 0.20f, 1f), settings);
                DrawNodeColorRow("Trigger", ref settings.paramColorTrigger, new Color(0.85f, 0.30f, 0.85f, 1f), settings);
            }
            using (new EditorGUI.DisabledScope(!settings.showParamVrcIcons))
            {
                DrawNodeColorRow("VRC Label", ref settings.paramColorVrcLabel, Color.cyan, settings);
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Graph Analysis", EditorStyles.boldLabel);
            DrawNodeColorRow("Analysis Highlight", ref settings.analysisHighlightColor, Color.red, settings);
            EditorGUILayout.EndVertical();
        }

        void DrawPaletteColorRow(string label, ref Color color, Color defaultColor, AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(110));
                EditorGUI.BeginChangeCheck();
                var newColor = EditorGUILayout.ColorField(GUIContent.none, color, true, false, false);
                if (EditorGUI.EndChangeCheck())
                {
                    color = ClampPaletteColor(newColor);
                    Styles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
                    settings.Save();
                }
                if (CursorBtn("Reset", Styles.IconBtn, GUILayout.Width(48)))
                {
                    color = defaultColor;
                    Styles.ApplyPalette(settings.paletteColorPrimary, settings.paletteColorSecondary, settings.paletteColorAccent);
                    settings.Save();
                }
            }
        }

        static Color ClampPaletteColor(Color color)
        {
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            value = EditorGUIUtility.isProSkin ? Mathf.Min(value, 0.40f) : Mathf.Max(value, 0.70f);
            var clamped = Color.HSVToRGB(hue, saturation, value);
            clamped.a = color.a;
            return clamped;
        }

        // ── Graph background + grid ───────────────────────────────────────────

        void DrawGraphGridSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_graphGridOpen ? "▼ " : "▶ ") + "Graph Background", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _graphGridOpen = !_graphGridOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft("Enable", settings.graphGridOverride, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.graphGridOverride = enabled;
                    settings.Save();
                }
                if (CursorBtn("Reset", Styles.IconBtn, GUILayout.Width(48), GUILayout.Height(24)))
                {
                    settings.ResetGraphGrid();
                    settings.Save();
                }
            }

            if (!_graphGridOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            using (new EditorGUI.DisabledScope(!settings.graphGridOverride))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Background", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool useImage = EditorGUILayout.ToggleLeft("Color", !settings.graphGridUseImage, GUILayout.Width(55));
                    if (EditorGUI.EndChangeCheck() && useImage) { settings.graphGridUseImage = false; settings.Save(); }
                    EditorGUI.BeginChangeCheck();
                    bool imageSelected = EditorGUILayout.ToggleLeft("Image", settings.graphGridUseImage, GUILayout.Width(55));
                    if (EditorGUI.EndChangeCheck() && imageSelected) { settings.graphGridUseImage = true; settings.Save(); }

                    if (!settings.graphGridUseImage)
                    {
                        EditorGUI.BeginChangeCheck();
                        var newColor = EditorGUILayout.ColorField(GUIContent.none, settings.graphGridBackgroundColor, true, false, false);
                        if (EditorGUI.EndChangeCheck()) { settings.graphGridBackgroundColor = newColor; settings.Save(); }
                        if (CursorBtn("Reset", Styles.IconBtn, GUILayout.Width(48)))
                        {
                            settings.graphGridBackgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f);
                            settings.Save();
                        }
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        var texture = (UnityEngine.Texture2D)EditorGUILayout.ObjectField(settings.graphGridBackgroundImage, typeof(UnityEngine.Texture2D), false, GUILayout.ExpandWidth(true));
                        if (EditorGUI.EndChangeCheck()) { settings.graphGridBackgroundImage = texture; settings.Save(); }
                        EditorGUI.BeginChangeCheck();
                        float opacity = EditorGUILayout.Slider(settings.graphGridBackgroundImageOpacity, 0f, 1f, GUILayout.ExpandWidth(true));
                        if (EditorGUI.EndChangeCheck()) { settings.graphGridBackgroundImageOpacity = opacity; settings.Save(); }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Grid", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool drawLines = EditorGUILayout.ToggleLeft("", settings.graphGridDrawLines, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.graphGridDrawLines = drawLines; settings.Save(); }
                }

                using (new EditorGUI.DisabledScope(!settings.graphGridDrawLines))
                {
                    DrawGraphGridColorRow("Major Grid", ref settings.graphGridColorMajor, new Color(0.30f, 0.30f, 0.30f, 1f), settings);
                    DrawGraphGridColorRow("Minor Grid", ref settings.graphGridColorMinor, new Color(0.22f, 0.22f, 0.22f, 1f), settings);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Grid Scale", GUILayout.Width(110));
                        EditorGUI.BeginChangeCheck();
                        float scale = EditorGUILayout.Slider(settings.graphGridScalingMajor, 1f, 3f);
                        if (EditorGUI.EndChangeCheck()) { settings.graphGridScalingMajor = scale; settings.Save(); }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Minor Divisions", GUILayout.Width(110));
                        EditorGUI.BeginChangeCheck();
                        int div = EditorGUILayout.IntSlider(settings.graphGridDivisorMinor, 2, 10);
                        if (EditorGUI.EndChangeCheck()) { settings.graphGridDivisorMinor = div; settings.Save(); }
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        /* Draws a labeled color field row with a Reset button that restores defaultColor and auto-saves. */
        void DrawGraphGridColorRow(string label, ref Color color, Color defaultColor, AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(110));
                EditorGUI.BeginChangeCheck();
                var newColor = EditorGUILayout.ColorField(GUIContent.none, color, true, false, false);
                if (EditorGUI.EndChangeCheck())
                {
                    color = newColor;
                    settings.Save();
                }
                if (CursorBtn("Reset", Styles.IconBtn, GUILayout.Width(48)))
                {
                    color = defaultColor;
                    settings.Save();
                }
            }
        }

        // ── Node icon indicators ──────────────────────────────────────────────

        void DrawOverlaySection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_nodeIconsOpen ? "▼ " : "▶ ") + "Node Icons", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _nodeIconsOpen = !_nodeIconsOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft("Enable", settings.overlayEnabled, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck()) { settings.overlayEnabled = enabled; settings.Save(); }
            }

            if (!_nodeIconsOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            using (new EditorGUI.DisabledScope(!settings.overlayEnabled))
            {
                float lineHeight = EditorGUIUtility.singleLineHeight;
                var row1Rect = EditorGUILayout.GetControlRect(false, lineHeight);
                var row2Rect = EditorGUILayout.GetControlRect(false, lineHeight);
                float colWidth = row1Rect.width / 4f;

                DrawOverlayToggle(new Rect(row1Rect.x + 0 * colWidth, row1Rect.y, colWidth, lineHeight), "! Empty",   ref settings.overlayShowEmpty,      settings);
                DrawOverlayToggle(new Rect(row1Rect.x + 1 * colWidth, row1Rect.y, colWidth, lineHeight), "↻ Loop",    ref settings.overlayShowLoop,       settings);
                DrawOverlayToggle(new Rect(row1Rect.x + 2 * colWidth, row1Rect.y, colWidth, lineHeight), "WD",        ref settings.overlayShowWD,         settings);
                DrawOverlayToggle(new Rect(row1Rect.x + 3 * colWidth, row1Rect.y, colWidth, lineHeight), "Behaviors", ref settings.overlayShowB,          settings);

                DrawOverlayToggle(new Rect(row2Rect.x + 0 * colWidth, row2Rect.y, colWidth, lineHeight), "Speed",     ref settings.overlayShowSpeed,      settings);
                DrawOverlayToggle(new Rect(row2Rect.x + 1 * colWidth, row2Rect.y, colWidth, lineHeight), "Motion",    ref settings.overlayShowMotion,     settings);
                DrawOverlayToggle(new Rect(row2Rect.x + 2 * colWidth, row2Rect.y, colWidth, lineHeight), "Clip Name", ref settings.overlayShowMotionName, settings);
                DrawOverlayToggle(new Rect(row2Rect.x + 3 * colWidth, row2Rect.y, colWidth, lineHeight), "Coords",    ref settings.overlayShowCoords,     settings);
                EditorGUILayout.Space(4);
                DrawNodeColorRow("Active",   ref settings.overlayActiveColor,   Color.white,                         settings);
                DrawNodeColorRow("Inactive", ref settings.overlayInactiveColor, new Color(0.45f, 0.45f, 0.45f, 1f), settings);
            }
            EditorGUILayout.EndVertical();
        }

        // ── Transition overlay ────────────────────────────────────────────────

        void DrawTransitionOverlaySection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_transitionOverlayOpen ? "▼ " : "▶ ") + "Transition Overlay", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _transitionOverlayOpen = !_transitionOverlayOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft("Enable", settings.transitionOverlayEnabled, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck()) { settings.transitionOverlayEnabled = enabled; settings.Save(); }
            }

            if (!_transitionOverlayOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            using (new EditorGUI.DisabledScope(!settings.transitionOverlayEnabled))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    bool showLabel = EditorGUILayout.ToggleLeft("Labels", settings.transitionShowLabel, GUILayout.Width(60));
                    if (EditorGUI.EndChangeCheck()) { settings.transitionShowLabel = showLabel; settings.Save(); }
                    GUILayout.Space(6);
                    EditorGUI.BeginChangeCheck();
                    bool selectionColor = EditorGUILayout.ToggleLeft("Selection Colors", settings.transitionSelectionColorEnabled, GUILayout.Width(120));
                    if (EditorGUI.EndChangeCheck()) { settings.transitionSelectionColorEnabled = selectionColor; settings.Save(); }
                    GUILayout.Space(6);
                    EditorGUI.BeginChangeCheck();
                    bool arrows = EditorGUILayout.ToggleLeft("Indicator Arrows", settings.transitionIndicatorArrowsEnabled, GUILayout.Width(115));
                    if (EditorGUI.EndChangeCheck()) { settings.transitionIndicatorArrowsEnabled = arrows; settings.Save(); }
                    GUILayout.Space(6);
                    EditorGUI.BeginChangeCheck();
                    bool animate = EditorGUILayout.ToggleLeft("Animate", settings.transitionAnimateSelected, GUILayout.Width(72));
                    if (EditorGUI.EndChangeCheck()) { settings.transitionAnimateSelected = animate; settings.Save(); }
                }

                DrawNodeColorRow("Transition Line",    ref settings.transitionOverlayColor,         new Color(1.0f, 1.0f, 1.0f, 1.0f), settings);

                using (new EditorGUI.DisabledScope(!settings.transitionSelectionColorEnabled))
                {
                    DrawNodeColorRow("Selection In",   ref settings.transitionIncomingColor,        new Color(0.0f, 1.0f, 1.0f, 1.0f), settings);
                    DrawNodeColorRow("Selection Out",  ref settings.transitionOutgoingColor,        new Color(1.0f, 0.0f, 1.0f, 1.0f), settings);
                }

                using (new EditorGUI.DisabledScope(!settings.transitionIndicatorArrowsEnabled))
                {
                    DrawNodeColorRow("Default ▶",       ref settings.transitionOverlayArrowColor,    new Color(0.6f, 0.6f, 0.6f, 1.0f), settings);
                    DrawNodeColorRow("No Condition ▶",  ref settings.transitionArrowNoConditionColor, new Color(1.0f, 0.28f, 0.0f, 1.0f), settings);
                    DrawNodeColorRow("Instant ▶",       ref settings.transitionArrowInstantColor,     new Color(0.0f, 0.25f, 0.66f, 1.0f), settings);
                }
            }
            EditorGUILayout.EndVertical();
        }

        // ── Node colors ───────────────────────────────────────────────────────

        void DrawNodeColorsSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_nodeColorsOpen ? "▼ " : "▶ ") + "Node Colors", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _nodeColorsOpen = !_nodeColorsOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.ToggleLeft("Enable", settings.nodeColorEnabled, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.nodeColorEnabled = enabled;
                    settings.Save();
                }
                if (CursorBtn("Reset", Styles.IconBtn, GUILayout.Width(48), GUILayout.Height(24)))
                {
                    settings.ResetNodeColors();
                    settings.Save();
                }
            }

            if (!_nodeColorsOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            using (new EditorGUI.DisabledScope(!settings.nodeColorEnabled))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Visual Style", GUILayout.Width(115));
                    EditorGUI.BeginChangeCheck();
                    bool is3D = EditorGUILayout.ToggleLeft("Flat / 3D", settings.nodeColor3DEnabled);
                    if (EditorGUI.EndChangeCheck())
                    {
                        settings.nodeColor3DEnabled = is3D;
                        settings.Save();
                        PatchNodeStyles.Invalidate();
                    }
                }
                DrawNodeColorRow("Selection Highlight",     ref settings.nodeSelectionColor,      new(1f, 1f, 1f, 1f), settings);
                EditorGUILayout.Space(8);
                DrawNodeColorRow("State Nodes",       ref settings.stateNodeColor,       new(0.30f, 0.30f, 0.30f, 1f), settings);
                DrawNodeColorRow("Default State",     ref settings.defaultStateColor,    new(0.60f, 0.35f, 0.10f, 1f), settings);
                DrawNodeColorRow("Sub State Machine", ref settings.subStateMachineColor, new(0.35f, 0.25f, 0.50f, 1f), settings);
                DrawNodeColorRow("Entry Node",        ref settings.entryNodeColor,       new(0.20f, 0.55f, 0.20f, 1f), settings);
                DrawNodeColorRow("Exit Node",         ref settings.exitNodeColor,        new(0.55f, 0.15f, 0.15f, 1f), settings);
                DrawNodeColorRow("Any State",         ref settings.anyStateNodeColor,    new(0.15f, 0.40f, 0.50f, 1f), settings);
                EditorGUILayout.Space(8);
                DrawNodeColorRow("Blend Tree Direct",       ref settings.blendTreeDirectNodeColor, new(0.70f, 0.37f, 0.20f, 1f), settings);
                DrawNodeColorRow("Blend Tree 1D",           ref settings.blendTree1DNodeColor,    new(0.24f, 0.50f, 0.60f, 1f),  settings);
                DrawNodeColorRow("Blend Tree 2D",           ref settings.blendTree2DNodeColor,    new(00.24f, 0.60f, 0.45f, 1f),  settings);
            }
            EditorGUILayout.EndVertical();
        }

        static void DrawOverlayToggle(Rect rect, string label, ref bool value, AnimatorDefaultSettings settings)
        {
            EditorGUI.BeginChangeCheck();
            bool newValue = EditorGUI.ToggleLeft(rect, label, value);
            if (EditorGUI.EndChangeCheck()) { value = newValue; settings.Save(); }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        }

        static void DrawFeatureToggle(string featureId, string label, string tooltip)
        {
            // Read from _instances state — reflects actual patch state, not just saved prefs
            bool current = FeatureHarmony.IsEnabled(featureId);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var content = string.IsNullOrEmpty(tooltip) ? new GUIContent(label) : new GUIContent(label, tooltip);
                bool newValue = EditorGUILayout.ToggleLeft(content, current);
                if (EditorGUI.EndChangeCheck())
                    FeatureHarmony.SetEnabled(featureId, newValue);
            }
        }

        /* Draws a labeled color field row with a Reset button that restores defaultColor and auto-saves. Shared by node color and transition overlay color rows. */
        void DrawNodeColorRow(string label, ref Color color, Color defaultColor, AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(115));
                EditorGUI.BeginChangeCheck();
                var newColor = EditorGUILayout.ColorField(GUIContent.none, color, true, false, false);
                if (EditorGUI.EndChangeCheck())
                {
                    color = newColor;
                    settings.Save();
                }
                if (CursorBtn("Reset", Styles.IconBtn, GUILayout.Width(48)))
                {
                    color = defaultColor;
                    settings.Save();
                }
            }
        }

        // ── Transition defaults ───────────────────────────────────────────────

        void DrawTransitionDefaultsSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_transitionDefaultsOpen ? "▼ " : "▶ ") + "Transition Defaults", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _transitionDefaultsOpen = !_transitionDefaultsOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool applyOnCreate = EditorGUILayout.ToggleLeft("Apply on Create", settings.applyToTransitions, GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.applyToTransitions = applyOnCreate;
                    settings.Save();
                }
            }

            if (!_transitionDefaultsOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            using (new EditorGUI.DisabledScope(!settings.applyToTransitions))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Has Exit Time", GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    bool hasExit = EditorGUILayout.Toggle(settings.transHasExitTime, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transHasExitTime = hasExit; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Exit Time", GUILayout.Width(120));
                    EditorGUI.BeginChangeCheck();
                    float exitTime = EditorGUILayout.FloatField(settings.transExitTime);
                    if (EditorGUI.EndChangeCheck()) { settings.transExitTime = exitTime; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Has Fixed Duration", GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    bool hasFixed = EditorGUILayout.Toggle(settings.transHasFixedDuration, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transHasFixedDuration = hasFixed; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Transition Duration", GUILayout.Width(120));
                    EditorGUI.BeginChangeCheck();
                    float duration = EditorGUILayout.FloatField(settings.transDuration);
                    if (EditorGUI.EndChangeCheck()) { settings.transDuration = duration; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Transition Offset", GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    float offset = EditorGUILayout.FloatField(settings.transOffset);
                    if (EditorGUI.EndChangeCheck()) { settings.transOffset = offset; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Interruption Source", GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    var interruptionSource = (TransitionInterruptionSource)EditorGUILayout.EnumPopup(settings.transInterruptionSource);
                    if (EditorGUI.EndChangeCheck()) { settings.transInterruptionSource = interruptionSource; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Ordered Interruption", GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    bool ordered = EditorGUILayout.Toggle(settings.transOrderedInterruption, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transOrderedInterruption = ordered; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Mute", GUILayout.Width(80));
                    EditorGUI.BeginChangeCheck();
                    bool mute = EditorGUILayout.Toggle(settings.transMute, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transMute = mute; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Can Transition To Self", GUILayout.Width(160));
                    EditorGUI.BeginChangeCheck();
                    bool canTransitionToSelf = EditorGUILayout.Toggle(settings.transCanTransitionToSelf, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transCanTransitionToSelf = canTransitionToSelf; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Solo", GUILayout.Width(80));
                    EditorGUI.BeginChangeCheck();
                    bool solo = EditorGUILayout.Toggle(settings.transSolo, GUILayout.Width(20));
                    if (EditorGUI.EndChangeCheck()) { settings.transSolo = solo; settings.Save(); }
                }
            }
            EditorGUILayout.EndVertical();
        }

        // ── State defaults ────────────────────────────────────────────────────

        void DrawStateDefaultsSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_stateDefaultsOpen ? "▼ " : "▶ ") + "State Defaults", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _stateDefaultsOpen = !_stateDefaultsOpen;
                GUILayout.FlexibleSpace();
                EditorGUI.BeginChangeCheck();
                bool applyOnCreate = EditorGUILayout.ToggleLeft("Apply on Create", settings.applyToStates, GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck())
                {
                    settings.applyToStates = applyOnCreate;
                    settings.Save();
                }
            }

            if (!_stateDefaultsOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);
            using (new EditorGUI.DisabledScope(!settings.applyToStates))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Tag", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    string tag = EditorGUILayout.TextField(settings.stateTag);
                    if (EditorGUI.EndChangeCheck()) { settings.stateTag = tag; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Speed", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    float speed = EditorGUILayout.FloatField(settings.stateSpeed);
                    if (EditorGUI.EndChangeCheck()) { settings.stateSpeed = speed; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!settings.stateSpeedParameterActive))
                    {
                        EditorGUILayout.LabelField("Multiplier", GUILayout.Width(110));
                        EditorGUI.BeginChangeCheck();
                        string speedParam = EditorGUILayout.TextField(settings.stateSpeedParameter);
                        if (EditorGUI.EndChangeCheck()) { settings.stateSpeedParameter = speedParam; settings.Save(); }
                        GUILayout.FlexibleSpace();
                    }
                    EditorGUI.BeginChangeCheck();
                    bool speedParamActive = EditorGUILayout.ToggleLeft("Parameter", settings.stateSpeedParameterActive, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.stateSpeedParameterActive = speedParamActive; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Motion Time", GUILayout.Width(110));
                    if (settings.stateTimeParameterActive)
                    {
                        EditorGUI.BeginChangeCheck();
                        string timeParam = EditorGUILayout.TextField(settings.stateTimeParameter);
                        if (EditorGUI.EndChangeCheck()) { settings.stateTimeParameter = timeParam; settings.Save(); }
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginChangeCheck();
                    bool timeActive = EditorGUILayout.ToggleLeft("Parameter", settings.stateTimeParameterActive, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.stateTimeParameterActive = timeActive; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Mirror", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool mirror = EditorGUILayout.Toggle(settings.stateMirror, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck()) { settings.stateMirror = mirror; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginChangeCheck();
                    bool mirrorActive = EditorGUILayout.ToggleLeft("Parameter", settings.stateMirrorParameterActive, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.stateMirrorParameterActive = mirrorActive; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Cycle Offset", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    float cycleOffset = EditorGUILayout.FloatField(settings.stateCycleOffset);
                    if (EditorGUI.EndChangeCheck()) { settings.stateCycleOffset = cycleOffset; settings.Save(); }
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginChangeCheck();
                    bool cycleActive = EditorGUILayout.ToggleLeft("Parameter", settings.stateCycleOffsetParameterActive, GUILayout.Width(90));
                    if (EditorGUI.EndChangeCheck()) { settings.stateCycleOffsetParameterActive = cycleActive; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Foot IK", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool footIK = EditorGUILayout.Toggle(settings.stateIKOnFeet, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck()) { settings.stateIKOnFeet = footIK; settings.Save(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Write Defaults", GUILayout.Width(110));
                    EditorGUI.BeginChangeCheck();
                    bool writeDefaults = EditorGUILayout.Toggle(settings.stateWriteDefaultValues, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck()) { settings.stateWriteDefaultValues = writeDefaults; settings.Save(); }
                }
            }
            EditorGUILayout.EndVertical();
        }
        // ── Miscellaneous ─────────────────────────────────────────────────────

        void DrawMiscellaneousSection(AnimatorDefaultSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope(Styles.BehaviorSectionHeader))
            {
                if (CursorBtn((_miscOpen ? "▼ " : "▶ ") + "Miscellaneous", Styles.HeaderLabel, GUILayout.ExpandWidth(false), GUILayout.Height(24)))
                    _miscOpen = !_miscOpen;
                GUILayout.FlexibleSpace();
            }

            if (!_miscOpen) return;

            var bodyRect = EditorGUILayout.BeginVertical(Styles.SectionPadded);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bodyRect, Styles.PrimaryColor);

            float miscLineHeight = EditorGUIUtility.singleLineHeight;
            var miscRow1Rect = EditorGUILayout.GetControlRect(false, miscLineHeight);
            var miscRow2Rect = EditorGUILayout.GetControlRect(false, miscLineHeight);
            float miscColWidth = miscRow1Rect.width / 4f;

            DrawOverlayToggle(new Rect(miscRow1Rect.x + 0 * miscColWidth, miscRow1Rect.y, miscColWidth, miscLineHeight), "WD Blend Trees",       ref settings.wdIncludeBlendTreeStates,   settings);
            DrawOverlayToggle(new Rect(miscRow1Rect.x + 1 * miscColWidth, miscRow1Rect.y, miscColWidth, miscLineHeight), "Prevent Layer Scroll",  ref settings.preventLayerScroll,         settings);
            DrawOverlayToggle(new Rect(miscRow1Rect.x + 2 * miscColWidth, miscRow1Rect.y, miscColWidth, miscLineHeight), "Prevent Param Scroll",  ref settings.preventParameterScroll,     settings);
            DrawOverlayToggle(new Rect(miscRow1Rect.x + 3 * miscColWidth, miscRow1Rect.y, miscColWidth, miscLineHeight), "Layer Weight 1",        ref settings.newLayerWeightOne,        settings);

            DrawOverlayToggle(new Rect(miscRow2Rect.x + 0 * miscColWidth, miscRow2Rect.y, miscColWidth, miscLineHeight), "Clip Menu Nesting",     ref settings.clipMenuNestingEnabled,     settings);
            DrawOverlayToggle(new Rect(miscRow2Rect.x + 1 * miscColWidth, miscRow2Rect.y, miscColWidth, miscLineHeight), "Layer Templates",        ref settings.layerTemplateButtonEnabled, settings);
            DrawOverlayToggle(new Rect(miscRow2Rect.x + 2 * miscColWidth, miscRow2Rect.y, miscColWidth, miscLineHeight), "Param Add Menu",         ref settings.parameterAddMenuEnabled,    settings);
            DrawOverlayToggle(new Rect(miscRow2Rect.x + 3 * miscColWidth, miscRow2Rect.y, miscColWidth, miscLineHeight), "Frames",                 ref settings.framesEnabled,              settings);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Compatibility", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Disable features that conflict with other tools. Toggles take effect instantly.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(2);
            DrawFeatureToggle(FeatureHarmony.ContextMenuId,   "Context Menus",     "Disable if using RATS or another tool that patches HandleContextMenu.");
            DrawFeatureToggle(FeatureHarmony.NodeOverlayId,   "Node Overlay",      "Disable if using RATS or another tool that patches StateNode.NodeUI.");
            DrawFeatureToggle(FeatureHarmony.NodeColorId,     "Node Colors",       "Disable if using a tool that patches NodeUI or node styles.");
            DrawFeatureToggle(FeatureHarmony.TransitionId,    "Transition Overlay","Disable if using a tool that patches EdgeGUI.DrawEdge.");
            DrawFeatureToggle(FeatureHarmony.GraphInteractId, "Graph Interaction", "Disable if using RATS or a tool that patches EdgeGUI.DoEdges or GraphGUI.OnGraphGUI.");
            DrawFeatureToggle(FeatureHarmony.GridBgId,        "Grid Background",   "Disable if using a tool that patches GraphGUI.DrawGrid.");
            DrawFeatureToggle(FeatureHarmony.LayerViewId,     "Layer View",        "");
            DrawFeatureToggle(FeatureHarmony.ParamViewId,     "Parameter View",    "");
            DrawFeatureToggle(FeatureHarmony.BlendTreeId,     "Blend Tree",        "");
            DrawFeatureToggle(FeatureHarmony.BottomBarId,     "Bottom Bar",        "");

            EditorGUILayout.EndVertical();
        }
    }
}
#endif
