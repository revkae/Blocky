---
tags: [editor-ui, milestone-4, milestone-5]
---

# Editor UI

Source: [[Welcome|TDD §8]]. Status: **Milestones 4 and 5 done.** `PaletteView`, `ProgramCanvasView`'s pan/zoom + `OnChanged` subscription, and live-editable param fields are still open (Milestone 6, and a scope note below).

**Milestone 5 — drag interaction** (`BlockDragManipulator`, `DragLayer`, drop candidates, commands on commit, TDD §8.3-8.4):
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

**Still not done:** dragging blocks to reorder them (`BlockDragManipulator` exists and *should* now work against a real panel, but hasn't been wired into the window or verified) — insertion/deletion via buttons is the only way to edit structure right now. Expression/Reporter param editing remains a v1 non-goal (unimplemented placeholder label).

**What Milestone 4 actually built:** `ParamFieldFactory` (real UI Toolkit controls — `FloatField`/`Toggle`/`TextField`/`DropdownField` — each `SetEnabled(false)`, so Milestone 5 wires up the *same* controls live instead of replacing them), `BlockView` (one `VisualElement` per node, holds `NodeId` not a node reference, recurses into `branchCount` body-slots), `UnknownBlockView` (a removed/renamed block type still renders and preserves its raw params, TDD §10.2), and `StackView` (trigger header + top-level sequence). Styling is a base USS sheet plus one small per-category sheet, colors as USS custom properties (`--blocky-color-*`) so re-theming touches no C#, per §8.5.

**Deliberately not built yet:** `PaletteView`, `ProgramCanvasView`'s pan/zoom and `OnChanged` subscription, `BlockDragManipulator`, `DragLayer`, `BlockViewPool` — all Milestone 5. No `EditorWindow`/`UIDocument` host exists yet either, so nothing is actually visible on screen — tests exercise the VisualElement tree directly (`Query<T>()`), which works fine without a live panel.

UI Toolkit, not UGUI — see [[09 Decisions/Decisions#ADR-001 UI Toolkit vs UGUI|ADR-001]].

## Components
- `BlockView` — one `VisualElement` subtree per node; holds a node **id**, never a reference, so a rebuilt model never leaves a stale pointer
- `ParamFieldFactory` — `ParamKind` → control mapping; adding a param kind touches only this file
- `PaletteView` — drag-out clones a fresh `BlockNode` with regenerated ids
- `ProgramCanvasView` — hosts top-level stacks, pan/zoom, subscribes to `ProgramStore.OnChanged`
- `DragLayer` — single absolutely-positioned overlay, sibling of canvas
- `BlockDragManipulator` — drag state machine (below)
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
