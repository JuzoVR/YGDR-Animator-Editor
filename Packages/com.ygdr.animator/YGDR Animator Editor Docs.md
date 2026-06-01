# YGDR Animator Editor Docs

![Unity](https://img.shields.io/badge/Unity-2022.3_LTS-black.png?logo=unity "=125")
![VRChat SDK](https://img.shields.io/badge/VRChat_SDK-3.x-blue.png?logo=vrchat)
![Harmony](https://img.shields.io/badge/Harmony-2.x-orange.png)

Powerful Unity Editor tool for advanced animator controller editing. Extends Animator window with multi-transition editing, state property management, VRC-specific features, and graph enhancements.

Open via **Window → YGDR → YGDR Animator Editor**, or use menu item in Animator window.

---

## Contents

- [Glossary](#glossary)
- [Custom Editor Window](#custom-editor-window)
  - [Transitions Tab](#transitions-tab)
  - [States Tab](#states-tab)
  - [Controller Tab](#controller-tab)
  - [Settings Tab](#settings-tab)
- [Layer Panel Enhancements](#layer-panel-enhancements)
- [Graph Window Enhancements](#graph-window-enhancements)
- [Blend Tree Enhancements](#blend-tree-enhancements)
- [Bottom Bar](#bottom-bar)
- [Frames](#frames)
- [Graph Node Analysis](#graph-node-analysis)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Bug Fixes & Compatibility](#bug-fixes--compatibility)
- [Undo Safety](#undo-safety)

---

## Glossary

| Term | Definition |
|---|---|
| **AAP** | Animator-Animated Parameter. A parameter whose value is driven by an animation clip rather than transitions/scripts. |
| **WD** | Write Defaults. Per-state toggle controlling whether unanimated properties reset to defaults on state entry. |
| **Sub-State Machine** | Nested state machine inside a layer. Groups related states. Has Entry, Exit, Any State, parent-machine references. |
| **Multi-Transition** | Multiple transitions sharing same source and destination states. Useful for OR-logic conditions. |
| **Frame** | Sticky-note overlay on graph. Annotates and groups nodes. Stored as hidden sub-asset on controller. |
| **Direct Blend Tree** | Blend tree type where each child weight is driven by a separate parameter directly. Requires WD on. |
| **Network Sync** | Pattern for syncing local-only parameters across VRChat clients via int/bool encoded sync parameters. |
| **Clip Remapper** | Tool fixing broken `AnimationClip` bindings when hierarchy paths change. |
| **AnyState Transition** | Transition originating from special AnyState node. Fires from any state in the layer when conditions met. |
| **Interruption Source** | Setting controlling which transitions can override an in-progress transition. |
| **VRC Parameter Driver** | `StateMachineBehaviour` setting/changing parameter values on state enter/exit. |
| **VRC Tracking Control** | `StateMachineBehaviour` toggling player tracking vs animation per body region. |
| **VRC Locomotion Control** | `StateMachineBehaviour` enabling or disabling avatar locomotion. |
| **VRC Animator Layer Control** | `StateMachineBehaviour` blending a specific animator sub-layer's weight over time. |
| **VRC Playable Layer Control** | `StateMachineBehaviour` blending an entire playable layer's weight over time. |
| **VRC Temporary Pose Space** | `StateMachineBehaviour` entering or exiting avatar pose space with optional delay. |
| **Harmony** | Runtime patching library. Used to inject features into Unity Editor internals. |

---

## Custom Editor Window

Tabbed interface for editing currently selected object(s) in Animator graph. Updates automatically as selection changes in Animator window.

### Transitions Tab

Edit multiple transitions at once. Select one or more transitions in Animator graph → tab shows all selected together → mass-edit shared properties. Toggle pill button on right to deselect and collapse displayed transition tags.

**Transition Details** — Edit timing (exit time, duration), interruption settings, atomic flags. Changes sync to Animator graph in real time.

**Condition Rows** — Each row displays parameter name, comparison mode, threshold value.

**All Conditions Mode** — Displays all conditions for all selected transitions, grouped by source. Tab between fields to enter values quickly.

**Shared Conditions Mode** — `+` adds shared condition (parameter, mode, threshold) to every selected transition. Supports all condition types:
- Bool: `If` / `IfNot`
- Int: `Equals` / `NotEqual` / `Greater` / `Less`
- Float: `Greater` / `Less`

Tool detects duplicate parameters across transitions and shows warning in either mode.

**Reverse** — `⇄` swaps all transition conditions (`Equals` → `NotEqual`, `Greater` → `Less`).

**Merge & Separate** — Tab detects multi-transitions (same source and destination). Offers options to merge or break apart.

---

### States Tab

Select one or more state nodes → tab shows properties for all selected. Collapse with right-side pill button.

**State List** — Each selected state has `In` / `Out` buttons to quickly select relevant transitions.

**Align States** — Buttons to vertically/horizontally & align/distribute all selected states. Useful for organizing complex state machines.

**State Properties** — Edit names (appends `#1`, `#2`…`#n` to subsequent selected nodes to prevent duplicates), speed, motion (animation clip), cycle offset, write defaults, mirror, foot IK toggles. Motion fields show preview of assigned clip, accept drag-drop, and display `-` on mixed values.

#### Shared Behaviors

VRC Parameter Drivers, VRC Play Audio, VRC Tracking Control, VRC Locomotion Control, VRC Animator Layer Control, VRC Playable Layer Control, VRC Temporary Pose Space.

> [!IMPORTANT]
> VRC features require VRChat SDK installed. Without SDK, these sections will not appear.

Each section has **Add to All** / **Remove All** buttons in its header. Sections only appear when at least one selected state has the component.

**VRC Parameter Driver** — Add or edit shared drivers across selected states. Rows are reorderable. Each row specifies type (`Set` / `Add` / `Random`), parameter name, and value. New rows default to the first unused controller parameter. Click `-` to remove a row. Removing all rows removes the component.

**VRC Play Audio** — Configure shared play-audio behaviour: source path (drag `AudioSource` to resolve), playback order, clips list (reorderable), volume/pitch min/max ranges, loop toggle, on-enter/on-exit play/stop flags, delay.

**VRC Tracking Control** — Override tracking on shared states for head, hands, feet, hips, fingers, eyes, eyelids, mouth, jaw. Use **Set All** row to apply one value across all body regions at once.

| Color | Meaning |
|---|---|
| Green | Tracking |
| Yellow | Animation |
| Blue | Mixed values across selection |

**VRC Locomotion Control** — Enable or disable avatar locomotion. Two-button toggle: **Disable** / **Enable** — active button shows green text.

**VRC Animator Layer Control** — Blend a specific animator sub-layer's weight over time. Fields:

| Field | Description |
|---|---|
| Playable | Playable layer to affect |
| Layer | Index of sub-layer to affect |
| Goal Weight | Target weight (0–1 slider) |
| Blend Duration | Time in seconds to reach goal weight |

**VRC Playable Layer Control** — Blend an entire playable layer's weight over time (Action / FX / Gesture / Additive). Fields: Layer enum, Goal Weight (0–1 slider), Blend Duration.

**VRC Temporary Pose Space** — Enter or exit avatar pose space. Two-button toggle: **Enter** / **Exit** — active button shows green text. Fixed Delay toggle switches delay interpretation between seconds and normalized %.  

---

### Controller Tab

Shows currently active `AnimatorController` and management tools.

**Overview** — Tabs for Per-Layer Write Defaults, Network Sync, Sub-Assets. Includes dedicated `Clean` button for controllers with orphaned sub-assets.

#### Write Defaults

Two-column layer list with WD on/off state. Buttons for setting individual layers or all layers on/off. Mixed layers listed at bottom when present.

#### Network Sync

One-click network syncing for chosen layer. Options:

- Sync parameter type (int vs bool encoded)
- Transition type (All-to-All / Any-State)
- Toggle to preserve transition properties
- Name for newly created sync parameters (duplicates blocked)
- Prefix added to front of all networked states
- Toggle to remove state behaviors for network states
- Pack into sub-state machine node for clean layers

#### Sub-Assets

Sub-tabs listing all layers, states, blend trees in controller. Each entry shows warning icons for empty layers, invalid transitions, empty motion fields. Searchable. Click item → focuses in graph.

#### Clip Remapper

Fix broken animation clip bindings.

- Drag GameObject (with `Animator` + controller) into field → enables scan button → flags broken bindings + suggests From paths.
- **Auto-Repath** — Automatically updates bindings on hierarchy GameObject rename/move. Tracks only bindings that were valid when toggled on.
- Select clip from list → focuses asset in Project window. Select multiple clips in Project → list highlights in green → direct remap available. List shows only clips belonging to avatar in slot.

Clip Remapper integration based on [hfcRed's Animation-Repathing](https://github.com/hfcRed/Animation-Repathing).

---

### Settings Tab

Tool-wide configuration. Persisted in `EditorPrefs` → available cross-project.

#### Interface

UI toggles.

- **Layer Indicators** — WD / Frames / Empty indicators on controller layers
- **Type Icons** — Float / Int / Bool / Trigger icons with custom color pickers on parameters list
- **VRC Icons** — VRC parameter icons (same color pickers)
- **AAP Icons** — Marks parameters controlled by a clip → click to find affected states/clips
- **Graph Footer** — Shows selected node/transition count + current operation mode
- **VRC Comp Icons** — Marks parameters bound to VRC contact / physbone / raycast components → click to locate component. Also shows sync status.
- **Param Budget** — Displays current parameters, synced count, total allowed

Color pickers:
- **Primary / Secondary / Accent** — Adjust full interface palette
- **Graph Analysis** — Adjust highlight indicator colors

#### Graph Background

Change graph background color, replace with image (transparency adjustable), toggle gridlines, change major/minor line colors, adjust scale/divisions.

#### Node Colors

Toggle 3D vs flat state nodes. Assign custom colors for selection, state nodes, blend tree nodes.

#### Node Icons

Overlay icons for nodes. Available: empty node, looping animation, WD on/off, contains behaviors, parameter affecting speed, parameter affecting motion, clip name in node, node coordinates in graph. Custom active/inactive colors and names.

#### Transition Overlay

- **Labels** — Show condition/threshold for single transitions, count for multiple, `invalid` for null transitions. Show VRC hand gesture names when parameter is `GestureLeft` / `GestureRight` and uses `=` or `≠`.
- **Selection Colors** — Color pickers for default, incoming, outgoing transition lines when single node selected.
- **Indicator Arrows** — Arrow cap color for default, invalid, instant (0 duration) transitions.
- **Animate** — Animated arrow caps for selected transitions, or transitions referenced by selected nodes.

#### Transition Defaults

Default settings for newly created transitions.

#### State Defaults

Default settings for newly created state nodes.

#### Miscellaneous

- **WD Blend Trees** — Controller WD section can change/detect blend tree WD status. Disable for direct blend trees (require WD on).
- **Prevent Layer Scroll** — Stops Unity scrolling layer list to top on new layer creation.
- **Prevent Param Scroll** — Same behavior for parameters list.
- **Default Weight 1** — New layers auto-set weight to `1`.
- **Clip Menu Nesting** — Nest clips in sub-menus by name. Use `parent.child.name` with `.` as separator.
- **Layer Templates** — Replaces layer `+` button with dropdown ([see below](#layer-templates)).
- **Param Add Menu** — Parameter `+` button gains quick options for VRC built-in parameters. Right-click parameter adds:
  - Add parameter below
  - Convert to Float / Int / Bool / Trigger → auto-updates all references
  - Find parameter uses → opens window showing where parameter is used + threshold condition
  - Find Affecting Objects → lists GameObjects affected by parameter → click to find in hierarchy
  - Find AAP Uses → opens window listing all states/clips controlling parameter
  - Remap to Parameter → dropdown redirects all uses to different parameter
  - Delete and Clean → removes parameter from all transitions + parameter list without leaving `Parameter does not exist in Controller` warnings
  - Remove Unused Parameters → deletes any parameter not referenced
- **Frames** — Enables custom Frames feature ([see Frames](#frames)).
- **Compatibility** — Disable individual Harmony patches if they conflict with other tools.

> [!WARNING]
> On Unity start, conflicting patches auto-disable until manually re-enabled.

> [!CAUTION]
> Editor lockup recovery: **Window → YGDR → Emergency: Unpatch All Features**. Use only as last resort — disables all features until manual re-enable.

---

## Layer Panel Enhancements

Extends built-in layer list in Animator window.

### Layer Right-Click Context Menu

Right-click any layer row to access:

- **Copy Layer** — Copies layer (states, transitions, frames) to clipboard.
- **Paste Layer** — Pastes copied layer as new layer below current. Cross-controller paste auto-adds referenced parameters to destination.
- **Paste Layer Settings** — Applies only layer properties (avatar mask, blend mode, weight, IK pass, sync settings) from clipboard. Does not replace states.
- **Delete Layer** — Removes layer.
- **Create Template** *(visible when Layer Templates enabled)* — click opens parameter-mapping window. Saves current layer as user template. Seperate new layer name with `.` or `/` to create submenu heirarchy

### Layer Templates

When **Layer Templates** enabled in Settings, `+` button becomes a dropdown:

- **New Layer** — Creates blank layer → immediately enters rename mode.
- **Package templates** — Listed directly → click opens parameter-mapping window → import template.
- **User/ templates** — User-saved templates under `User/` submenu.
- **Delete User Template/** — Removes user template + associated clips (with confirmation).

Selecting a template opens parameter window to review and remap parameters before import.

---

## Graph Window Enhancements

Patches built-in Animator window graph view. Works seamlessly with Unity native controls.

### Mouse Interactions

#### Double-Click Empty Space → Create State

Double-click empty space → instantly creates new `AnimatorState` at cursor. State centered on click position → assigned dummy clip.

#### Drag-Drop Multiple Animation Clips

Drag multiple clips from Project window → drop onto graph → each clip creates new state.

#### Drag-Drop Clips onto State nodes

Drag Animation clips from assets folder onto state nodes to apply clip motion to them.

### Context Menus

#### State Node Context Menu

Right-click a state node:

- **Set Clip Loop Time** — Toggle loop time on all clips used by selected states.
- **Pack into Sub-State Machine** — Select 2+ states → right-click → groups into new sub-state machine. Node positions preserved within bounding box. Fully undoable.
- **Select Transitions** — Submenu: all incoming / outgoing / shared transitions for selected nodes.
- **Copy / Paste Behaviors** — Copy `StateMachineBehaviour(s)` → paste onto other states. Menu shows all 7 supported types: Param Drivers, Play Audio, Tracking Control, Locomotion Control, Animator Layer Control, Playable Layer Control, Temporary Pose Space.
- **Multi-Transition** — Select source node → click menu item → select other nodes → invoke menu item again → creates transitions from source to all destinations.

##### Pack / Unpack Diagram

```
BEFORE PACK                          AFTER PACK
┌─────────────────────────┐          ┌─────────────────────────┐
│  Layer                  │          │  Layer                  │
│  ┌────┐  ┌────┐  ┌────┐ │   →      │  ┌────────────────────┐ │
│  │ A  │→ │ B  │→ │ C  │ │          │  │ NewSubSM           │ │
│  └────┘  └────┘  └────┘ │          │  │ ┌──┐ ┌──┐ ┌──┐     │ │
│                         │          │  │ │A │→│B │→│C │     │ │
└─────────────────────────┘          │  │ └──┘ └──┘ └──┘     │ │
                                     │  └────────────────────┘ │
                                     └─────────────────────────┘
```

##### Multi-Transition Diagram

```
Step 1: Right-click source        Step 2: Select dests + invoke again

                ┌─────┐                    ┌─────┐
                │  A  │ (source)           │  A  │
                └─────┘                    └──┬──┘
                                       ┌──────┼──────┐
       ┌─────┐  ┌─────┐  ┌─────┐       ▼      ▼      ▼
       │  B  │  │  C  │  │  D  │    ┌─────┐┌─────┐┌─────┐
       └─────┘  └─────┘  └─────┘    │  B  ││  C  ││  D  │
                                    └─────┘└─────┘└─────┘
```

#### Sub-State Machine Node Context Menu

Right-click sub-state machine node:

- **Unpack Sub-State Machine** — Moves all states + transitions back into parent → retains positions and transitions → removes empty sub-state machine. Fully undoable.

#### Transition Arrow Context Menu

Right-click directly on selected transition arrow:

- **Reverse Transitions** — Creates new transition from destination → source with inverse conditions (`Equal` → `NotEqual`).
- **Redirect Transitions** — Creates new transition from source → newly selected destination → retains all properties.
- **Replicate Transitions** — Creates new transition from destination → newly selected source → retains all properties.
- **Delete All Transitions in Layer** — Deletes all transitions in current layer. Excludes sub-state machines and parent layers when inside a sub-state machine.

### Modes

#### Chain Transition Mode

<kbd>Ctrl</kbd>+<kbd>Double-click</kbd> on state node:

1. Click destination node → transition created.
2. Continue clicking destinations → chain more transitions from previous node.
3. Press <kbd>Esc</kbd> to exit.

Preview line follows cursor while active. Bottom bar shows `Chain Mode`.

```
Click 1: A    Click 2: B       Click 3: C               Esc
┌───┐         ┌───┐  ┌───┐     ┌───┐  ┌───┐  ┌───┐     done
│ A │   →     │ A │→ │ B │ →   │ A │→ │ B │→ │ C │
└───┘         └───┘  └───┘     └───┘  └───┘  └───┘
```

> [!TIP]
> Combine new node double click with Chain Mode transitions to rapidly build framework state machines.

#### Fan Transition Mode

<kbd>Shift</kbd>+<kbd>Double-click</kbd> on state node:

1. Click destination node → transition created from source.
2. Continue clicking destinations → each creates another transition from the same source.
3. Press <kbd>Esc</kbd> to exit.

Preview line follows cursor while active. Bottom bar shows `Fan Mode`.

```
Click 1: B    Click 2: C         Esc
              ┌───┐  ┌───┐       done
┌───┐  ┌───┐  │ A │→ │ B │
│ A │→ │ B │  │   │  └───┘
└───┘  └───┘  │   │→ ┌───┐
              └───┘  │ C │
                     └───┘
```

> [!TIP]
> Use Fan Mode to quickly wire one hub state (e.g. idle) to many destinations in one pass.

#### Copy-Paste Transitions

Select one or more transitions → <kbd>Ctrl</kbd>+<kbd>C</kbd> to copy. click source → <kbd>Ctrl</kbd>+<kbd>V</kbd> → click destination → transitions paste with all conditions intact. Visual preview shows landing position. <kbd>Esc</kbd> cancels. Bottom bar shows `Paste N Transitions`.

### Inline Renaming

#### <kbd>F2</kbd> — States, Sub-State Machines, Blend Tree Nodes, Pamameters, Layers, Frames

Select node → <kbd>F2</kbd> → rename directly on graph. <kbd>Enter</kbd> confirms, <kbd>Esc</kbd> cancels.

#### <kbd>F3</kbd> — Animation Clips & Blend Tree Leaves, Frame Comments

Select state, blend tree leaf, or Frame → <kbd>F3</kbd> → rename clip assigned to node. Rename field appears on graph → asset updates in Project. <kbd>Enter</kbd> confirms, <kbd>Esc</kbd> cancels.

---

## Blend Tree Enhancements

### Drag-Drop Animation Clips

Drag clips from Project → drop onto blend tree node:

- **Leaf node** (existing clip) → replaces clip.
- **Blend tree node** → adds new child nodes with dropped clips.

### Drag-Reparent

Drag blend tree node from one parent to another. Motion, threshold, other values preserved. Works across blend tree nodes in same graph. Eligible parents highlighted green

### Copy-Paste Nodes

<kbd>Ctrl</kbd>+<kbd>C</kbd> / <kbd>Ctrl</kbd>+<kbd>V</kbd> — copy blend tree node (full subtree if itself a blend tree) → paste onto new parent in same or different blend tree. Deep-copies entire subtree. <kbd>Esc</kbd> cancels pending paste. Also available in right-click context menu.

### Node Type Color

Blend tree node titles use custom colors to distinguish blend tree vs clip. Colors configurable in Settings.

---

## Bottom Bar

Graph bottom bar displays:

| Position | Content |
|---|---|
| Left | Selected states/transitions count |
| Center | Active mode label (normal, chain, fan, paste, etc.) |
| Right | Controller path (clickable → pings controller in Project) |

Chain mode, fan mode, copy-paste mode, and other temporary modes update label in real time.

---

## Frames

Visual sticky notes for animator graph → organize and annotate layers. Derived from Substance Designer frames.

Frames stored as hidden sub-assets inside each controller → visible to all users with tool. Deleted frames garbage-collected at Unity domain reload/open → keeps controllers clean.

**Creating frames** — Right-click empty graph → **Create Frame**. If nodes selected at creation → frame auto-fits around them.

**Deleting all frames** — Right-click empty graph → **Delete All Frames** (excludes sub-state machines and parent layers when inside a sub-state machine).

Lock/unlock by clicking lock icon in upper-left corner. Resize by selecting and dragging square handles at sides/corners. Multiple frames can be selected, moved, and copy-pasted at once.

### Frame Context Menu

Right-click a frame:

- **Rename** — Rename frame title. Also available via <kbd>F2</kbd> on selected unlocked frame.
- **Edit Comments** — Add multi-line comments to frame body. Also via <kbd>F3</kbd> on unlocked frame.
- **Color** — Color picker for frame color + transparency.
- **Z-Layer** *(shown as `z#` in top-right corner)* — Frame stacking shortcuts:

  | Action | Shortcut |
  |---|---|
  | Move to Top | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>]</kbd> |
  | Move Up | <kbd>Ctrl</kbd>+<kbd>]</kbd> |
  | Move Down | <kbd>Ctrl</kbd>+<kbd>[</kbd> |
  | Move to Bottom | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>[</kbd> |

- **Move Nodes with Frame** — When enabled, nodes inside frame bounds move with frame.
- **Lock** — Prevents move/resize.
- **Delete** — Deletes frame.

> [!NOTE]
> Frames are copied automatically when a layer is copy-pasted, including cross-controller pastes.

---

## Graph Node Analysis

Right-click empty graph:

- **Unreachable States** — Highlights states with no incoming transitions or only invalid ones. Sub-state machines highlighted when containing unreachable states.
- **Terminal States** — Highlights states with no valid exit: only invalid exit transitions, only self-transitions, or part of group isolated from reaching any other state.

---

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| <kbd>F2</kbd> | Rename state / sub-state machine / blend tree node / frame / layer / parameter |
| <kbd>F3</kbd> | Rename clip / blend tree leaf / frame comments |
| <kbd>Ctrl</kbd>+<kbd>C</kbd> | Copy transitions / blend tree nodes / frames |
| <kbd>Ctrl</kbd>+<kbd>V</kbd> | Paste transitions / blend tree nodes / frames |
| <kbd>Ctrl</kbd>+<kbd>Double-click</kbd> | Enter Chain Transition Mode |
| <kbd>Shift</kbd>+<kbd>Double-click</kbd> | Enter Fan Transition Mode |
| <kbd>Esc</kbd> | Exit chain / fan / paste / rename mode |
| <kbd>Enter</kbd> | Confirm inline rename |
| <kbd>Ctrl</kbd>+<kbd>A</kbd> | Select all state nodes |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>A</kbd> | Select all transitions |
| <kbd>Ctrl</kbd>+<kbd>]</kbd> | Frame: move up |
| <kbd>Ctrl</kbd>+<kbd>[</kbd> | Frame: move down |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>]</kbd> | Frame: move to top |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>[</kbd> | Frame: move to bottom |

---

## Bug Fixes & Compatibility

- Reordering layers no longer switches graph from selected layer view.
- Undoing parameter rename no longer triggers `Parameter does not exist in Controller` warnings.
- <kbd>F2</kbd> renames selected layer or parameter directly in respective list.

---

## Undo Safety

All operations within controller (pack, unpack, state moves, transition creation, layer copy-paste, VRC parameter edits, etc.) fully undoable. Tool uses Unity's `Undo` system at all system boundaries → properly registers object creation and destruction.

Node Colors, Initial patching hook methods, EditorPrefs settings mechanism based on Ratz by ([rrazgriz](https://github.com/rrazgriz/RATS))