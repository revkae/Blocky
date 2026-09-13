---
tags: [demo]
---

# Demo Scene

`Assets/Scenes/BlockyDemo.unity` — open it and press Play. Every object's program lives as a real `BlockProgramAsset` under `Assets/Demo/Programs/`, built the same way any future authoring UI would build one (hand-assembled `ObjectProgram` → `ProgramSerializer` → asset), not a special-cased demo path.

All 9 `ObjectProgramRunner`s were verified via `ProgramCompiler.Link` against the real `Assets/Resources/Blocks` registry before saving — zero compile errors.

## What to look for

| Object | Trigger | Blocks exercised |
|---|---|---|
| **SquareWalker** | when_play_clicked | `control.repeat` × `motion.move_forward` + `motion.turn_direction` — walks a square path |
| **SpinnerCube** | when_play_clicked | `control.repeat_forever` × `motion.rotate_axis` — spins forever, never finishes |
| **KeyMover** | when_key_pressed (Space) | `motion.move_forward` with duration — press Space repeatedly, each restarts the move (`RestartOnRetrigger`) |
| **Bumper → CollisionPainter** | when_play_clicked / when_collided | Bumper drives into CollisionPainter; the impact fires `looks.change_color` on the painter |
| **LookAtStatue** | when_looked_at | Camera starts aimed at it — `looks.set_scale` grows it immediately on Play (rising-edge trigger) |
| **IfDemo** | when_play_clicked | `control.if` (condition literal `true`) → `motion.move_forward` |
| **IfElseSign** | when_play_clicked | `control.if_else` (condition literal `true`) → `looks.set_visible(false)`, else-branch never taken |
| **WaiterCube** | when_play_clicked | `control.wait(2s)` → `motion.set_position` teleport |

`BlockyManager` holds the single `BlockyRuntimeTicker` that drives everything (TDD §6.5 — one scheduler tick, one keyboard poll, one look-at check per frame, shared scene-wide).

## In-game program editor
`BlockyInGamePanel` (GameObject in this scene) is a Scratch-style workspace you get **while playing** — press **Tab** to open it on the left side of the screen (the live game stays visible to its right, like Scratch's stage), then **click any object** in the game view to select it. Drag the strip on the workspace's **right edge** to resize it. Its layout:

- **Left icon rail** — one tab per `BlockCategory` actually present in the registry (Motion, Looks, Control, Event; no dead tabs for categories with zero blocks). Clicking a tab scrolls the palette to that category's section (`ScrollView.ScrollTo`).
- **Palette** — every block in the registry, grouped by category, color-tinted per category the same way canvas blocks are.
- **Canvas** — drag a block out of the palette and drop it into the program; it snaps into place using the exact same `DropCandidateBuilder`/`DropCandidateResolver` math the in-canvas reorder drag already used (a new `PaletteDragManipulator`, distinct from the existing `BlockDragManipulator` which only reorders blocks already in a program). Dropping a trigger block creates a new stack wherever you drop it (nudged clear of existing stacks); dropping a statement/C-block snaps it into the nearest slot under the pointer. Dropping a statement over open canvas — nowhere to snap to — is a no-op.
- **Green "▶ Go" button** — fires a new `event.when_go_clicked` trigger block (independent of the auto-fired `event.when_play_clicked`), so a program can be (re)started on demand instead of only once at scene start.

Every structural edit — drag-in, delete, param change — still flows through the same `ProgramStore.OnChanged`, which autosaves to `RuntimeProgramStorage` (`Application.persistentDataPath/BlockyPrograms/<name>.json`) and hot-reloads the live `ObjectProgramRunner`. There is no separate save step, drag-and-drop included.

This is a different tool from `Blocky/Program Editor` (the Editor-only window from before): that one edits project assets and only works in the Editor; this one works identically in the Editor's Play mode and in a real build. See [[05 Editor UI/Editor UI|Editor UI]] for how both share the same underlying `BlockView`/`ProgramCanvasView` components.

**Verification note:** this environment's automated Play-mode harness could not pump the player loop past frame 2 (a harness limitation, not a code issue — confirmed via `Time.frameCount` polling), so the drag interaction couldn't be captured as a live screenshot this session. What *is* verified: 88/88 EditMode tests still pass, `recompile` is clean, and the built visual tree was inspected directly in Play mode (correct child structure: topbar/body/drag-layer, correct category classes, correct stylesheet assignment). Treat the drag-and-drop path as code-reviewed and structurally verified, not yet pixel-confirmed — worth an actual manual Play-mode check next time you're at the keyboard.

## Known limitation
None beyond the note above — drag-to-reorder-within-canvas (`BlockDragManipulator`) and drag-from-palette (`PaletteDragManipulator`) now both exist; only pixel-level Play-mode confirmation of the newest palette-drag path is outstanding.
