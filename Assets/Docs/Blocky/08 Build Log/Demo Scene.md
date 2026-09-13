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
`BlockyInGamePanel` (GameObject in this scene) is a right-hand sidebar you get **while playing** — press **Tab** to open it, then **click any object** in the game view to select it. Its blocks appear live and editable right there: change a number, add a block, delete one, and the object's behavior updates immediately (the runner is hot-reloaded on every edit). Edits save to a real file under `Application.persistentDataPath/BlockyPrograms/<name>.json` — not into the project's `.asset` files, so playing and editing never touches your source assets, the same way a real shipped game's save data wouldn't.

This is a different tool from `Blocky/Program Editor` (the Editor-only window from before): that one edits project assets and only works in the Editor; this one works identically in the Editor's Play mode and in a real build, and is what "write code to any object while playing" actually means. See [[05 Editor UI/Editor UI|Editor UI]] for how both share the same underlying `BlockView`/`ProgramCanvasView` components.

## Known limitation
Drag-to-reorder blocks isn't wired into either editor yet — insert/delete via buttons is how you restructure a program for now.
