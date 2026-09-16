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

(These two assets predate condition blocks and still store their condition as a checkbox value; it runs as before, and opening the object in the in-game editor shows it as a `true` condition block.)
| **WaiterCube** | when_play_clicked | `control.wait(2s)` → `motion.set_position` teleport |

`BlockyManager` holds the single `BlockyRuntimeTicker` that drives everything (TDD §6.5 — one scheduler tick, one keyboard poll, one look-at check per frame, shared scene-wide).

## In-game program editor
`BlockyInGamePanel` (GameObject in this scene) is a Scratch-style workspace you get **while playing** — press **Tab** to open it on the left side of the screen (the live game stays visible to its right, like Scratch's stage), then **click any object** in the game view to select it. Drag the strip on the workspace's **right edge** to resize it.

- **Left icon rail** — one tab per `BlockCategory` present in the registry, each a colored dot with its name. Clicking a tab scrolls the palette to that category and lights the tab, so the rail says where you are.
- **Palette** — visible as soon as the workspace opens, so you can browse it before picking an object (dragging from it starts working once an object is selected). Drag the **seam between the palette and the table** to make it wider or narrower. Every block drawn in its real shape: events are hats (rounded top, nothing can go above), ordinary blocks have a notch on top and a tab underneath (connect both ways), C-blocks have a mouth (`if`, `repeat`; `if else` has two), and `forever` is a cap (no tab — nothing can follow it).
- **The table** (canvas) — an unbounded surface with no buttons:
  - **Place** — drag a block from the palette and drop it anywhere; it lies there loose. Bring its connector close to another block's and the edge it will attach to **glows** — release to snap.
  - **Move** — grab a block from anywhere on it (its text, number boxes and checkboxes included) and it comes with everything under it; grab an event hat and the whole script comes along. A plain click on a number box still lets you type.
  - **Look around** — drag empty space with the **right (or middle) mouse button** to pan, scroll to zoom toward the cursor, or use the **+ / − / =** buttons in the corner (= resets).
  - **Select & delete** — click a block to select it (it turns lighter with a white outline); press **Delete** or **Backspace** to remove it. Only that block goes; the blocks below close up. Deleting an event hat keeps its blocks on the table. You can also drop anything on the palette to delete it.
  - **Select several** — **Shift+click** (or Ctrl+click) blocks to add or remove them, or **drag a box with the left button over empty table** to select every block it touches (hold Shift to add to what's selected). Drag any selected block and they all move together, keeping their places, and land loose (a group doesn't snap; drag one block on its own to connect it). Delete removes them all; dropping them on the palette deletes them all; one Undo brings them back.
  - Loose blocks with no event on top are saved but never run (same as Scratch) — put an event hat above them to make them a script.
  - **Conditions** — the **Conditions** tab holds hexagonal blocks: `mouse down?`, `mouse up?`, `true`, `false`. `if`, `if else` and `repeat until` have a matching hexagonal hole. Drag a condition near the hole (it glows) and release, or just **click the empty hole** and pick one from the list. Drag it out again, or select it and press Delete, to empty the hole. An empty hole counts as false. Dropping onto a filled hole swaps, and the old condition pops out onto the table. `mouse down?` only counts presses on the game, not on this workspace.
- **Run bar** (the row under the title), for the whole scene at once:
  - **▶ Go** fires `when Go clicked` (and un-pauses); `when Play Clicked` still runs once at scene start. Once everything has finished (or was stopped), Go runs the `when Play Clicked` scripts again too.
  - **Stop** ends every script where it is. Pressing **Go** (or **Step ▶**) afterwards runs every script again from there, `when Play Clicked` scripts included.
  - **Reset** stops everything and puts every programmed object back where it started (position, rotation, size, visibility, color). After a Reset, **Go** replays the scene from the beginning, `when Play Clicked` scripts included.
  - **Pause / Resume** appears only after you press **Go**, while those scripts are still running (not for the scripts that start by themselves when the scene loads); it freezes everything (the bar turns amber).
  - **Step ▶** runs one block of every script, then freezes — and starts every script that isn't running first, **whatever event is on top of it**. That includes `when Key Pressed`, `when Collided` and `when Looked At` scripts: between steps the scene is frozen, so you could never press the key or cause the bump they wait for. A script that has finished starts again from its first block on the next press. So Reset → Step ▶, Step ▶, Step ▶ walks every script in the scene, KeyMover included.
  - **◀ Step** goes back one step: scripts *and* objects return to where they were before the last Step ▶. Works for every step taken since pausing (up to 200); pressing Go, Resume, Stop, Reset or editing a block starts the history over.
  - **Speed 1x 2x 3x 4x** speeds up moves, turns and waits that take time.
- **Running block lights up** with a thick yellow outline while its script is on it.
- **Undo** (table's top-right, or Ctrl/Cmd+Z) takes back the last edit to the selected object.
- **Advice** under the table explains in plain words why a script won't do anything (no event on top, empty ⬡ hole, a number out of range…). The block gets a "?" (hint) or "!" (won't run) badge; click a message to select its block.

Every edit — drop, delete, value change — autosaves to `Application.persistentDataPath/BlockyPrograms/<name>.json` and hot-reloads the object's `ObjectProgramRunner`. There is no save step. Number and text boxes commit when you press Enter or click away.

This is a different tool from `Blocky/Program Editor` (the Editor-only window, which edits project assets and keeps its button-based editing). See [[05 Editor UI/Editor UI|Editor UI]] for how both share the same `BlockView`/`HatView`/`ProgramCanvasView` components.

## Known limitations
- Drag, pan and zoom gestures in the running game haven't been exercised by automation (Play-mode automation stalls the player loop). The data side (`DropChain`, `DeleteBlock`), the snap rules (`SnapResolver`) and selection are unit-tested, and the shapes and selection highlight were checked visually — but a manual pass is still the real test.
- No auto-scroll when dragging a block to the edge of the table — pan first, or drop it and move it again.
