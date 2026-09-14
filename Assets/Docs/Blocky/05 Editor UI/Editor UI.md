---
tags: [editor-ui, milestone-4, milestone-5]
---

# Editor UI

Source: [[Welcome|TDD §8]]. Status: **Milestones 4 and 5 done.** `PaletteView`, `ProgramCanvasView`'s pan/zoom + `OnChanged` subscription, and live-editable param fields are still open (Milestone 6, and a scope note below).

**Milestone 5 — drag interaction** (`BlockDragManipulator`, `DragLayer`, drop candidates, commands on commit, TDD §8.3-8.4). *Superseded and deleted on 2026-09-14: nothing used this system once the table drag below existed (the Editor window edits with buttons). `DragLayer` and `StackPlacementResolver` survive; the rest of the list below is history.*
- `DropCandidate`/`DropCandidateKind` — one drop target (`BodyCavity`/`SequenceGap`/`SequenceEnd`/`Canvas`), carrying `Depth` for the nesting cap.
- `DropCandidateResolver` — innermost-wins hit test (smallest containing rect).
- `DropCandidateBuilder` — walks a `StackView`/`BlockView` tree into a flat candidate list; rect lookup is injected (`Func<VisualElement, Rect>`, defaults to `worldBound`) specifically so this is unit-testable without a live panel.
- `BlockDragController` — the actual state machine (TDD §8.3), fully decoupled from UI Toolkit events so every transition, the nesting-depth rejection, and the same-container index-shift correction are unit tested directly (no panel needed).
- `StackPlacementResolver` — the (16,16)-nudge collision avoidance for dropping a whole stack (TDD §8.4 edge case 1).
- `DragLayer` + `BlockDragManipulator` — the real UI Toolkit wiring (pointer capture, reparent-into-layer-on-threshold, z-order). **Not unit tested** — needs a live panel for meaningful `worldBound` hit-testing, which doesn't exist until something hosts a `UIDocument`/`EditorWindow` (Milestone 6+). Everything it depends on (the controller, the candidate builder) is tested; the adapter itself is straightforward wiring, verified by construction rather than automated test.

**Update — live editing now exists.** The gap above was closed once there was a real reason to (the user wanted to actually author programs, not just preview them):
- `ParamFieldFactory.CreateLiveField` — same control types as the read-only version, enabled, dispatching a fresh `BlockParam` through a callback on every edit instead of the read-only factory's disabled fields.
- `BlockView`/`StackView`/`ProgramCanvasView` all take an optional `ProgramStore` (`editable` flag on the canvas) — when present, fields go live, a "✕" delete button appears per block/stack (`RemoveNode`/`DeleteStack`), and a "+ Add" button per body-slot/sequence/canvas opens a `BlockPickerPopup` (a small dependency-free inline list, not `UnityEditor.GenericMenu`, so it still works outside the Editor) that inserts a fresh prototype via `InsertNode`/`CreateStack`.
- `ProgramQuery.FindLocation` (Blocky.Data) computes a node's current `NodeLocation` on demand — needed so "delete this block" and "insert after this block" don't require every view to separately track its own position.
- **`BlockyProgramEditorWindow`** (`Assets/Scripts/Blocky.Tooling/`, a new Editor-only assembly — `Blocky/Program Editor` menu item): pick any scene GameObject, auto-adds an `ObjectProgramRunner` + fresh `BlockProgramAsset` if it doesn't have one, hosts an editable `ProgramCanvasView` in `EditorWindow.rootVisualElement` (a genuine live UI Toolkit panel — this is what makes real editing possible; nothing before this had one), and persists every `ProgramStore.OnChanged` back into the asset. Verified end-to-end via a scripted session: opened the window, targeted a real demo object, inserted and removed a node through the actual UI-backed store, confirmed the change round-tripped to the `.asset` file on disk.

**Update — the table (in-game workspace).** The in-game `BlockyInGamePanel` no longer uses buttons at all; see [[09 Decisions/Decisions#ADR-010|ADR-010]] and the [[08 Build Log/Build Log|Build Log]] entry "The table":
- **Shapes** — `BlockOutline` + `BlockShapePainter` draw Scratch silhouettes with `Painter2D` (hat / notch+tab / C-mouths / cap), filled from a per-category `--blocky-fill` USS custom property. `HatView` is the event block; `BlockView` has mouths, arms ("else") and a footer; loose stacks render without a hat.
- **Modes** — `ProgramCanvasView(store, registry, editable, tableMode)`: read-only, button mode (the Editor window, unchanged behaviour), or table mode (live fields, no buttons; host attaches drag manipulators on `Rebuilt`).
- **Drag** — `ChainDragSession` (ghost, snap evaluation, glow, one `DropChain` on release), driven by `PaletteDragManipulator` (new blocks) and `CanvasDragManipulator` (existing blocks; grab takes the tail, a hat takes the stack, drop on palette deletes). Both manipulators derive from `TableDragManipulator`, which owns the whole lifecycle once: the pointer is followed on the panel root with TrickleDown callbacks, plus the host's per-frame `Poll` (real mouse position — covers a control inside the block that captured the pointer) and `ForceEnd` (a release the UI never saw); UI events stay primary and a flag stops both sources moving the ghost in the same frame. **No pointer capture of our own:** capturing on the drag layer was tried and broke dragging — a captured pointer's events go to the capturing element, not through the root callbacks, so the ghost froze until release.
- **Drag rules from the model** — `ChainShape` (has a hat / ends with a cap / is a lone condition) is computed from the nodes and trigger via the registry, not from the views on screen; `ChainDragSession` and `SnapResolver` read it.
- **Snap targets cached per drag** — collected once into reused lists and refreshed only when the table's transform changes (pan/zoom) or a stack or condition slot on it reports `GeometryChangedEvent` (the table re-flows the frame after blocks are lifted off it). Idle frames with an unmoved pointer do nothing.
- **One `DragContext` per workspace** — retargeted with `SetTarget` when an object is picked, so the palette is built once and can be browsed before any object is selected (dragging from it waits for a target).
- **`CanvasMode`** (`ReadOnly` / `Buttons` / `Table`) replaced the `editable` + `tableMode` bool pair.
- **Block identity** — `BlockView`, `HatView` and `ConditionView` implement `IBlockElement` (`StackId`, `NodeId`; null node id = hat). Grabbing, selecting, the table-background test and attaching drag handles all check that one interface, so a new kind of block view only has to implement it.
- **Snapping** — `SnapTargetCollector` (connection points on the live tree) + `SnapResolver` (nearest within 32px, shape rules). Unit-tested; the collector is checked against a real rendered program.
- **Grab anywhere** — `CanvasDragManipulator` listens in TrickleDown so a press on a block's text, number box or checkbox is a drag handle too; the press isn't consumed, so a click still edits the field. Live field labels are `pickingMode = Ignore` (a numeric label is otherwise Unity's drag-to-change-value handle). Dropdowns are excluded. Live text/number fields are `isDelayed`.
- **Infinite table** — no `ScrollView`: a clipped viewport in `BlockyInGamePanel` moves/scales the canvas with `style.translate`/`style.scale` (pan by dragging empty space, wheel zooms toward the cursor, +/−/= buttons). `ChainDragSession` converts between world pixels and unscaled table units and scales the ghost to the zoom (`DragContext.CanvasZoom`).
- **Selection** — `ProgramCanvasView.Select`/`ClearSelection` add `BlockOutline.SelectedClass` and re-apply it after every rebuild, finding the node by id across stacks. Drawn as a lighter fill + white outline in the same single Fill→Stroke pass as the normal outline — extra stroke passes were verified not to render (see the Build Log). Delete/Backspace in the panel issues `DeleteBlock`.

**Update — condition blocks.** See [[09 Decisions/Decisions#ADR-011|ADR-011]]:
- `ConditionView` — the hexagonal condition block (`BlockShape.Boolean`, `BlockOutline.Hexagon`), in a slot (`OwnerNodeId`/`ParamKey`) or loose on the table. `BlockView.Create` returns one for a loose condition; `BlockPrototype.Create` for the palette.
- `ConditionSlot` — what a `Reporter` param renders as (via `BlockParams.Create`, shared by `BlockView` and `ConditionView`): a hexagonal hole in a darker shade of its owner's color (`BlockShapePainter` `shade`), holding a `ConditionView` when filled. Clicking an empty slot opens a `GenericDropdownMenu` of every condition block; choosing one issues `SetParam`. In `CanvasDragManipulator` the empty slot counts as a control (clicking it never unselects its block).
- Snapping — `ConditionSlotResolver.Collect` lists every slot on the table; `FindBest` picks the slot nearest the dragged hexagon's left tip (within 28px of the slot's outline, innermost on a tie). `ChainDragSession(isCondition: true)` uses it instead of the stack connectors, and the glow becomes a ring around the hole (`blocky-drop-indicator--slot`).
- Selection and Delete work on conditions (`ProgramCanvasView` finds `ConditionView`s by id too; `DeleteBlock` empties the slot).

**Still not done:** the Editor window (`Blocky/Program Editor`) still edits structure with buttons — it renders the new shapes (condition slots and their dropdown included) but has no drag.

**What Milestone 4 actually built:** `ParamFieldFactory` (real UI Toolkit controls — `FloatField`/`Toggle`/`TextField`/`DropdownField` — each `SetEnabled(false)`, so Milestone 5 wires up the *same* controls live instead of replacing them), `BlockView` (one `VisualElement` per node, holds `NodeId` not a node reference, recurses into `branchCount` body-slots), `UnknownBlockView` (a removed/renamed block type still renders and preserves its raw params, TDD §10.2), and `StackView` (trigger header + top-level sequence). Styling is a base USS sheet plus one small per-category sheet, colors as USS custom properties (`--blocky-color-*`) so re-theming touches no C#, per §8.5.

**Deliberately not built yet:** `PaletteView`, `ProgramCanvasView`'s pan/zoom and `OnChanged` subscription, `BlockDragManipulator`, `DragLayer`, `BlockViewPool` — all Milestone 5. No `EditorWindow`/`UIDocument` host exists yet either, so nothing is actually visible on screen — tests exercise the VisualElement tree directly (`Query<T>()`), which works fine without a live panel.

UI Toolkit, not UGUI — see [[09 Decisions/Decisions#ADR-001 UI Toolkit vs UGUI|ADR-001]].

## Components
- `BlockView` — one `VisualElement` subtree per node; holds a node **id**, never a reference, so a rebuilt model never leaves a stale pointer
- `ParamFieldFactory` — `ParamKind` → control mapping; adding a param kind touches only this file
- `PaletteView` — drag-out clones a fresh `BlockNode` with regenerated ids
- `ProgramCanvasView` — hosts top-level stacks, pan/zoom, subscribes to `ProgramStore.OnChanged`
- `DragLayer` — single absolutely-positioned overlay, sibling of canvas
- `TableDragManipulator` (+ `CanvasDragManipulator`, `PaletteDragManipulator`) and `ChainDragSession` — press/drag/release and the drag itself (the state machine below was the original, now-deleted design)
- `BlockViewPool` — recycles views by block type

## Drag state machine
```
Idle → (PointerDown on grab) → Picked
Picked → (move > threshold) → Dragging      | (PointerUp below threshold) → click/edit param
Dragging → (PointerMove) → Dragging          (recompute drop candidate, throttled)
Dragging → (PointerUp) → Committing → Idle
Dragging → (Esc / panel lost / canvas cleared) → Cancelling → Idle
```
Drop candidates collected once at drag start (`{rect, kind, targetId, branchIndex, index}`), rebuilt only on structural change. Kinds: `BodyCavity` (nest), `SequenceGap` (splice), `SequenceEnd` (append), `Canvas` (new stack) — ties resolve innermost-first. Commit issues exactly one `MoveNode`/`InsertNode` command; model is the only source of truth.

## Edge cases (each needs a test — TDD §8.4)
Colliding stack drop position, C-block cavity resize mid-drag, canvas reset during interrupted drag (must clear DragLayer + force Idle), DragLayer z-order, nesting depth cap (default 12), view virtualization per off-screen stack, param edit while running (marks `CompiledProgram` dirty, relinks on next trigger).
