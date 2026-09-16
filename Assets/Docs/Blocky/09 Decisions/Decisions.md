---
tags: [decisions]
---

# Decisions

ADR-style log for anything that deviates from, extends, or resolves an open question in [[Welcome|the TDD]].

## ADR-001 — UI Toolkit over UGUI
**Status:** Accepted (carried over from TDD §8.1)
**Why:** Flex layout gives free vertical stacking and cavity auto-resize; UGUI needs `VerticalLayoutGroup` + `ContentSizeFitter` with manual rebuild ordering, which the TDD calls "the single biggest source of block-editor bugs." Same UITK components serve the in-Editor authoring window.
**Revisit if:** blocks must be authored on an in-world/diegetic surface (open question 1 in TDD §15) — UGUI is more mature there.

## ADR-002 — Instruction carries branch entry *and* exit pcs
**Status:** Accepted (extends TDD §6.2's illustrative `Instruction` struct)
**Why:** The TDD's `Instruction` only lists `JumpA`/`JumpB` (branch entry pcs). But `Thread.Frames[]` is documented as holding "loop counters, **branch returns**" — something has to know where a branch's last instruction is so the Runtime can pop back to the owning C-block without re-walking the tree at runtime (which would mean strings/structure lookups post-link, violating §6.2's "after linking, the runtime touches no strings and no dictionaries"). Added `JumpAExit`/`JumpBExit`: the pc immediately after each branch's last instruction, computed once at link time.
**What this doesn't decide:** whether reaching an exit pc means "fall through to next sibling" (If/IfElse) or "jump back to re-evaluate the loop" (Repeat/RepeatUntil/RepeatForever) is still an open, per-op runtime decision for Milestone 3 (`OpResult.Jump` + `Frame` push/pop) — the compiler only supplies the boundaries, not the looping behavior.

## ADR-003 — Compile errors are per-stack, not whole-program
**Status:** Accepted (TDD §10.2 explicit: "Errors block execution of that stack only, not the whole program")
**Why:** `ProgramCompiler.Link` compiles every diagnostic-free stack and marks a failing stack's `StackEntryPoints[i] = -1` instead of throwing. `Validate` now tags every diagnostic with the owning `StackId` so `Link` can group and skip cleanly. A one-bad-stack program still runs everything else — matches "the offending BlockView gets an error state," not a crash.

## ADR-004 — `BlockShape.Statement` is enum value 0, not `Trigger`
**Status:** Accepted
**Why:** Found the hard way while writing Milestone 3 tests: `OpTableBuilder` correctly skips `BlockShape.Trigger` definitions (triggers aren't dispatched by the VM). But with `Trigger = 0` as the first enum value, any `BlockDefinition` where an author forgot to set `shape` silently became "a trigger" and got skipped — the op table entry stayed null, and the VM's own try/catch around `op.Execute` turned the resulting `NullReferenceException` into a quiet `Fail`, not a loud error. Reordered so `Statement` is 0: an unset `shape` now surfaces immediately and loudly as "no IBlockOp bound to executorKey" at `OpTableBuilder.Build` time (which runs once at boot), instead of a silent no-op discovered later.
**How to apply:** Any new `BlockShape`-adjacent enum should default (0) to the "needs an executor" case, not the "skip me" case — fail loud beats fail quiet.

## ADR-005 — `Frame.IsLoop` distinguishes looping frames from one-shot skip frames
**Status:** Accepted (refines [[09 Decisions/Decisions#ADR-002|ADR-002]] and the loop-yield rule, TDD §6.4)
**Why:** `control.if_else` reuses the exact same Frame push/pop mechanism as `control.repeat` — pushing a frame is the only way to redirect a fall-through past the false branch when the true branch is taken (branches are laid out back-to-back in the flat array). But the generic wrap-detection in `VmScheduler.Step` originally force-yielded at *every* frame wrap, per the loop-yield rule. Applied unconditionally, that would silently add a hidden per-frame delay to every `if`/`if_else` with a non-yielding true branch — by far the most common block shape in any real program. Added `Frame.IsLoop`: only loop frames (`repeat`, `repeat_until`, `repeat_forever`) force a yield at wrap; a one-shot `if_else` skip-frame does not.
**How to apply:** Any future C-block whose branch is taken at most once per entry (not re-entered) should push its frame with `isLoop: false`.

## ADR-006 — RestartOnRetrigger kills and restarts, doesn't reset pc in place
**Status:** Accepted (simplifies TDD §6.4's literal wording)
**Why:** The TDD says a re-fired trigger "resets the existing thread's pc to the entry point and bumps Generation" — implying in-place reuse of the same `VmThread` object. `ObjectProgramRunner.Fire` instead marks the old thread `Done` and calls `scheduler.Start` fresh. Behaviorally equivalent for v1 (no thread pooling exists yet, so there's no allocation cost difference to avoid), and `Generation`-based cancellation only matters once the async adapter (TDD §6.6) exists — neither is built yet.
**Revisit when:** Milestone 7's thread pooling lands — at that point in-place pc reset becomes the actual cheaper path and this should change.

## ADR-007 — `ObjectProgramRunner.Initialize()`/`Shutdown()` are public, not implicit via `OnEnable`/`OnDisable` alone
**Status:** Accepted
**Why:** `GameObject.SetActive(true)` inside an EditMode test did not reliably invoke `OnEnable` synchronously within the test runner. Rather than fight edit-mode lifecycle timing, `OnEnable`/`OnDisable` now just forward to public `Initialize()`/`Shutdown()`, which tests call directly. Production behavior is unchanged; the seam only removes a testing dependency on Unity's own timing.

## ADR-008 — `ChangeColorOp` uses `renderer.material` (instances it), not a `MaterialPropertyBlock`
**Status:** Accepted for now, flagged for revisit
**Why:** `renderer.material.color = ...` is the simplest correct way to change one object's color without affecting others sharing the same material asset, but it silently creates a per-renderer material instance the first time it's touched — extra memory, and it defeats GPU instancing/batching for that object. `MaterialPropertyBlock` avoids both but is more code and was not warranted before real profiling data exists.
**Revisit when:** the Milestone 7 `ProfilerMarker`s (or the Memory Profiler) actually show this mattering for a real scene — TDD §11.2's zero-allocation goal is about the VM's steady-state hot path, not necessarily every visual op, but this is exactly the kind of thing worth checking once there's something to measure.

## ADR-009 — Stopped at Milestone 7; Milestone 8 is post-v1, not started
**Status:** Accepted (user decision, 2026-09-13)
**Why:** TDD §13's build order lists Milestone 8 as "Deferred features, in dependency order: undo UI → variables → expression blocks → custom procedures" — read in isolation, this looks like the next thing to build. But TDD §1.2 (Goals, Non-goals for v1) explicitly lists all four of those as v1 non-goals: "Variables and lists," "Custom procedures," "Expression (reporter) blocks," and "Undo/redo UI (plumbing is built in v1, stack is disabled)." The two sections describe the same items from different angles — §13 is a forward-looking roadmap note, §1.2 is the actual v1 scope boundary. Milestones 1–7 satisfy every stated v1 goal; starting Milestone 8 would mean building past what the document itself calls v1, without a real product decision to do so having been made yet.
**How to apply:** Do not start Milestone 8 work speculatively. If the user (or a future planning pass) decides the project needs it, that's a new scoping conversation — likely starting with just the undo/redo UI (cheapest, plumbing already exists), not all four items at once.

## ADR-010 — Loose stacks and proximity snapping replace the containment drop model (in-game)
**Status:** Accepted (user request, 2026-09-13)
**Why:** The user wants the canvas to behave like a table — blocks can lie anywhere, and a block's shape tells you what it connects to. That needs two things the TDD's model didn't have:
1. **Loose stacks.** The data model only had trigger-headed stacks, so a block dropped on empty canvas had nowhere to live (the previous drop was a silent no-op). A `BlockStack` with an empty `triggerBlockType` is now legal: saved, validated, never compiled or run. Chosen over a separate "loose blocks" collection because every existing command, query and view already works per stack; `ProgramQuery.IsLoose` is the only new concept.
2. **Proximity snapping.** Milestone 5's `DropCandidateResolver` picks the drop target by *pointer containment* inside thin gap rects — precise, but not what Scratch does or what the user described ("when it gets close to another block … that side should glow"). `SnapResolver` instead compares the dragged chain's connectors (top-left, bottom-left) with every connector on the table and takes the nearest within a radius, with the shape rules encoded explicitly (hat: bottom only; cap: top only, and only where nothing follows).

Every drag produces exactly one `DropChain` command (TDD §8.3's one-command-per-drag still holds). Its undo is a snapshot restore rather than a step-by-step inverse, since a single drop can edit two containers and add or remove stacks.
**Consequence:** `BlockDragManipulator`, `BlockDragController`, `DropCandidateBuilder` and `DropCandidateResolver` are no longer used by the in-game UI. They still compile and their tests pass. **Revisit:** delete them, or port the Editor window to the table model, whichever comes first — keeping two drag models long-term is a maintenance trap.

## ADR-011 — Conditions are blocks in `Reporter` params, evaluated live through a separate op table
**Context:** `if` / `if else` / `repeat until` took a checkbox (`ParamKind.Bool`) — a literal fixed at compile time, so `repeat until` could never end and nothing could react to input. The user asked for Scratch's hexagonal condition blocks (`mouse down?`, `mouse up?`, `true`, `false`) that drop into a matching hexagonal slot, with a dropdown when the empty slot is clicked.

**Decision:**
- **Data:** a condition is a `BlockNode` stored in `BlockParam.reporter` (`kind = Reporter`) — the slot the schema reserved for exactly this. No schema change. A condition lying on the table is a loose stack whose only block is that condition. `DropChain` gained a `FromConditionSlot` source and an `IntoConditionSlot` target (a filled slot swaps: the old condition pops out as a loose stack); `DeleteBlock` empties a slot; `ProgramQuery.FindNode` also searches slots.
- **Shape:** `BlockShape.Boolean` (appended, value 4) and `BlockCategory.Conditions` (appended, value 4) — appended so existing assets' serialized numbers keep their meaning. The hexagon has no notch or tab, so the silhouette itself says "doesn't stack, goes inside".
- **Compile:** a filled slot becomes `ParamValue.Reporter(opcode, paramOffset, paramCount)`; the condition's own params are appended after its owner's contiguous run, so nesting never breaks an instruction's param range. An empty slot is `EmptyReporter` (false, like Scratch). A condition in a runnable sequence, or a non-condition in a slot, is a compile error.
- **Runtime:** conditions implement `IConditionOp` (not `IBlockOp`) and get their own opcode-indexed table (`OpTableBuilder.BuildConditions`), because they are never scheduled as a step. Ops read condition params with `OpContext.GetBool(i)`, which evaluates the slot *now* — so `repeat until` re-checks every lap.
- **Input:** `BlockyInput.IsMouseDown` excludes presses over the editor (`IsPointerOverUi`, set by `BlockyInGamePanel`), so dragging blocks while a script runs doesn't count as "mouse down" in the game. `mouse up?` is the button *state* (not held), not a one-frame "released" edge.

**Consequences:** old saves still run unchanged: `ProgramUpgrades.UpgradeCheckboxConditions` turns a ticked checkbox into a `true` block and an unticked one into an empty slot, and it runs at every load point (`ObjectProgramRunner.Initialize`, the in-game editor, the Editor window) — so the compiler has no legacy branch and only ever sees the current format. Adding a condition later is one asset plus one `IConditionOp` class. The Editor window's "+ Add" pickers exclude conditions (they don't fit a sequence).

## ADR-012 — Learning aids: scene-wide playback in the scheduler, Reset from a start snapshot, friendly advice in the compiler layer
**Status:** Accepted (user request, 2026-09-14)
**Context:** Thinking as a teacher with no coding background, the user asked for five things so children can see *why* a program goes wrong and get back quickly: a Reset that puts every object back, the running block lighting up, a speed slider plus a one-step button, Undo, and friendly messages ("This script has no ▶ block on top, so it won't start").

**Decision:**
- **Playback lives in `VmScheduler`, not in each runner or op.** One pause/step/speed state that every script in the scene obeys together, like Scratch's stage. `Pause` stops the scheduler's clock too (`_now` doesn't advance), so a half-done wait or timed move resumes exactly where it was. `TimeScale` multiplies `dt`. Everything stays `dt`-driven, so runs remain deterministic.
- **What "one step" means:** each live script finishes or starts **exactly one block**, then freezes, across as many ticks as that block needs. A script caught mid-block (a timed move, a wait) just finishes it. Implemented with a per-thread budget: `VmThread.ResumePc` tells a continuing block (last result `Retry`) apart from a new one, and a thread with no budget left *parks* at the start of its next block. A C-block's lap check counts as a block, so stepping through `repeat` visibly returns to the top each lap. That's deliberate: it's how the loop really runs.
- ~~**Slow motion also lingers.**~~ *Replaced by [[#ADR-013 — Step back from snapshots; speed is 1x–4x; Pause only while running|ADR-013]]: the speed slider became 1x–4x buttons, and `BlockLinger` was removed.* (It used to rest a thread 0.15 script-seconds after each instant block below full speed, so the block stayed lit.)
- **Which block is running:** `VmThread.ActivePc` (the pc of the block started most recently), mapped back to the node through the existing `Instruction.SourceNodeId` → `DebugNodeIds`. `RunningBlocks.Collect` fills a caller-owned list, so the per-frame poll allocates nothing, and the panel only touches the table when the set changes. Drawn by the outline painter in its single Fill→Stroke pass as a thick yellow stroke (`BlockOutline.RunningClass`).
- **Reset = `WorldSnapshot`**, recorded by each `ObjectProgramRunner` on its first `Initialize` (idempotent, so hot reloads don't move the start point). It covers objects given a runner mid-game too. It records exactly what blocks can change: local transform, active, renderer enabled, shared material (undoes `change color`'s per-object copy, ADR-008), and a Rigidbody's pose and velocity. **A new block that changes anything else must extend it.**
- **Go after Reset replays the scene:** `ResetWorld` re-arms `when Play clicked` (`TriggerBroker.ResetPlaySession`). `Go` fires it (a no-op unless re-armed) plus `when Go clicked`, and un-pauses. Step with nothing running first starts what Go would, so Reset → Step walks a program from its first block.
- **Undo UI turned on** (this is the "new scoping conversation" [[#ADR-009 — Stopped at Milestone 7; Milestone 8 is post-v1, not started|ADR-009]] asked for, for undo only): `UndoCapacity = 50` per object in the in-game editor, an Undo button and Ctrl/Cmd+Z (ignored while typing, so text boxes keep their own undo). History is per selected object and starts when it's picked. No redo yet.
- **Friendly advice is `ProgramAdvice` in Blocky.Compiler**, next to the validator it complements, and unit-testable without UI. `Hint` = legal but probably not what was meant (loose blocks, empty event, empty C-block, empty ⬡ hole). `Problem` = the script won't run (number out of range, bad choice, missing value, unknown block). A catch-all turns any compiler error no rule explains into a general message, so a script never fails silently. Messages use the names printed on the blocks. Shown as rows under the table (click selects the block) and as a "!" / "?" badge on the block.

**Consequences:** `VmScheduler` now drops finished threads each tick (they used to accumulate forever), so the running-block poll and step checks stay proportional to live scripts. The Go button moved from the title bar into the run bar.

## ADR-013 — Step back from snapshots; speed is 1x–4x; Pause only while running
**Status:** Accepted (user request, 2026-09-14). Amends [[#ADR-012 — Learning aids: scene-wide playback in the scheduler, Reset from a start snapshot, friendly advice in the compiler layer|ADR-012]].
**Context:** After trying the first version, the user asked for three changes: Pause/Resume only visible once Go has been pressed and something is running; Step forward *and* Step back, to go back and forth; speed as 1x 2x 3x 4x instead of a slider.

**Decision:**
- **Step back restores snapshots; it doesn't replay.** Replaying from the start (Reset, then silently re-run N−1 steps) only works if everything is deterministic. Collisions come from Unity physics and key presses and mouse conditions come from the player, so a replay can diverge from what happened. Instead `Playback.StepForward` records a moment first:
  - every script's progress: `VmScheduler.SaveMoment` → `ThreadMoment` per thread, holding pc, frames, scratch, state and wake time, plus the script clock
  - every programmed object: `WorldSnapshot.SaveMoment`, holding transform, active, renderer, material, color and body motion
  - whether "when Play clicked" had fired yet

  `StepBack` restores the last moment and stays paused. Up to 200 moments are kept.
- **Restore writes into the same `VmThread` objects** rather than making new ones, and brings back scripts that finished since. For that to be safe, `ObjectProgramRunner` no longer keeps its own stack → thread dictionary; it asks `VmScheduler.FindLive(target, program, entryPc)`. Otherwise a restored thread would be invisible to its runner, and a retrigger could run the same stack twice. (Bonus: `Shutdown` now uses `StopWhere`, so it also stops `AllowConcurrent` threads, which the old dictionary never tracked, as its doc always claimed.)
- **Colors are restored only on a material copy.** `change color` recolors a per-object copy (ADR-008). A moment records the material and its color. Restoring recolors a material only if it isn't the object's original, because in the Editor that original is the project asset and writing to it would change the asset on disk.
- **The history covers steps taken since pausing, and is forgotten** on Resume, Go, Stop, Reset and any program edit. Once time runs on or the program changes, the saved moments no longer lead to what's on screen, and after an edit they'd hold threads of the old compiled program.
- **Step forward is ignored while a step is still finishing** (a timed block or a wait), so every saved moment is a clean stop between blocks. The button is disabled meanwhile.
- **Speed is 1x–4x** (`Playback.SetSpeed` → `VmScheduler.TimeScale`). Speeding up only affects blocks that take time. Slow motion is gone, and with it `BlockLinger`; Step is the way to watch instant blocks one at a time.
- **Pause/Resume shows only while something is running** (`Playback.IsRunning` = the scheduler has work), labelled Resume while paused. Step back is disabled when there's nothing to go back to.
- **Stop re-arms "when Play clicked"** (found in the live check). After Stop, Step ▶ started nothing: most demo scripts are "when Play clicked", which fires once per session, and only Reset re-armed it, so Go and Step ▶ silently did nothing after Stop. Now Stop re-arms it too: after Stop, Go or Step ▶ runs every script again from wherever the objects are. Reset additionally puts the objects back.

**Consequences:** the run bar's commands moved from static `BlockyRuntime` methods into the `Playback` class (`BlockyRuntime.Playback`), which can be tested with injected fixtures. Known limit: physics contacts and "looked at" state aren't part of a moment, and a loop with an instant body still runs one lap per frame at any speed.

## ADR-014 — Pause only after Go; Step/Go restart a finished scene; multi-select moves groups loose; the left button selects, the right button pans
**Status:** Accepted (user request, 2026-09-16). Amends [[#ADR-013 — Step back from snapshots; speed is 1x–4x; Pause only while running|ADR-013]] and [[#ADR-010 — Loose stacks and proximity snapping replace the containment drop model (in-game)|ADR-010]].
**Context:** The user asked for six things: Pause only after pressing Go; a working Step; Shift+click to select several blocks and move them together; a selection box dragged over empty table; a resizable palette; slimmer scrollbars.

**Decision:**
- **Pause follows Go, not "something is running".** The demo's "when Play clicked" scripts start by themselves at scene load, so ADR-013's rule (`IsRunning`) showed Pause before the user pressed anything. `Playback.IsGoing` is true after `Go` until the scripts finish, and false after Stop or Reset. A run started by Step doesn't count. It clears itself once the scheduler has no work, so a script a key starts later doesn't bring Pause back.
- **Step and Go restart a finished scene.** Reproduced live: once the demo script ran to its end, Step ▶ paused the scene and saved a step, but started nothing. "when Play clicked" fires once per session, and only Stop and Reset re-armed it. Now both Step and Go re-arm it whenever nothing is running (never started, finished, stopped, or stopped by an edit). Step with no script to start at all does nothing, rather than freezing the scene for nothing.
- **`BlockyRuntime` resets at the start of every Play session** (found during the live check). The project's Enter Play Mode Options disable domain reload, and nothing reset the runtime's statics. So a Pause or Step in one Play session carried into the next: the scheduler started paused (new scripts sat in its pending list forever), "when Play clicked" counted as already fired, and the step history pointed at objects from the previous session. Go and Step then appeared to do nothing, from the very first frame. `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` calls `BlockyRuntime.Reset()` before any scene object wakes.
- **Selection is a list of `BlockRef`s** (Blocky.Data: stack id + node id, null = hat; equal by node id, so a ref stays valid after its block moves to another stack). `ProgramCanvasView` keeps the list through rebuilds, like the single selection before. `SelectedStackId`/`SelectedNodeId` now name the most recently picked block.
- **A group moves by one command.** `MoveBlocks` runs one `DropChain` pick-up per piece under one snapshot (`DropChain.Execute`, split out of `Do`), so the table rebuilds, saves and hot-reloads once, and one Undo takes back the whole move. `MoveBlocks.Outermost` drops blocks another selected block already carries (a block under a selected hat, further down the same sequence, inside a selected C-block's mouth, a condition in a selected block's slot). Without that, the same nodes would be picked up twice. `DeleteBlocks` deletes each block as `DeleteBlock` would, skipping ones already gone with an earlier one.
- **Groups land loose; they don't snap.** A single chain has one top edge and one bottom edge to connect by; a group has none. Every piece moves by the same distance and lands where it was dropped. Dragging a single block still snaps as before.
- **Left-drag on empty table selects; right or middle drag pans.** The user asked for "click on empty space and drag to choose multiple", which was the pan gesture. Pan moved to the right or middle button. The wheel still zooms, and a plain left click on empty table still clears the selection. The box selects every block it *touches* (hats, blocks, conditions). Shift or Ctrl adds to what was selected.
- **The palette is resized by a seam** (`blocky-ingame-palette-handle`) between it and the table, clamped to at least 140px and to keep the table at least 120px. It sets the palette's `flex-basis`, so a narrow workspace can still shrink it.
- **Slim scrollbars by USS only**, scoped to `.blocky-ingame-root`: 8px track, 6px rounded thumb, no arrow buttons. The palette hides its horizontal scroller.

**Consequences:** the how-to line under the table now teaches the new gestures. The Editor window still selects one block at a time (it doesn't use the table's manipulators). A group can't be snapped onto a stack in one move: drop it, then drag the block you want to connect.

## ADR-015 — Step forward starts every script, whatever hat it has
**Status:** Accepted (user request, 2026-09-16). Amends [[#ADR-014 — Pause only after Go; Step/Go restart a finished scene; multi-select moves groups loose; the left button selects, the right button pans|ADR-014]].
**Context:** "Step still not working — when I press step it should play these blocks connected to event play one by one. All events, not just when play clicked."

**The bug, reproduced live.** ADR-014 made Step start the scripts *when nothing at all was running*: `if (!_scheduler.HasWork)`. The demo scene has SpinnerCube's `repeat forever`, which never finishes — so the scene is never idle, the branch never ran, and every Step press advanced that one spinner and nothing else. A script under any other hat (`when key pressed`, `when collided`, `when looked at`) could never be stepped at all, because between steps the scene is frozen: the learner cannot press the key, cause the bump or look at the object that the hat waits for.

**Decision:**
- **A new hat-independent trigger channel, `TriggerBroker.OnStepAll`.** Every compiled, non-loose stack subscribes to it in `ObjectProgramRunner.SubscribeTriggers`, alongside its own hat's channel. `Playback.StepForward` fires it on every press.
- **The unit is the script, not the scene.** Each press advances every running script by one block *and* starts every script that isn't running. A script that has finished starts again from its first block on the next press.
- **Stepping ignores `RetriggerPolicy`.** `ObjectProgramRunner.FireForStep` starts a stack only if `VmScheduler.FindLive` finds no thread for it. All five event hats are `RestartOnRetrigger`; restarting a script the learner is half way through walking would be the opposite of stepping.
- **`FirePlayClicked` still runs on each Step press** (it is a no-op once fired). It keeps the once-per-session flag honest, so a later Go doesn't restart the scripts Step already started, and Step back can re-arm it. `FireGoClicked` was dropped from Step — `OnStepAll` covers "when Go clicked" stacks without restarting a running one.
- **Go is unchanged.** Go stays the green flag: Play + Go hats only. Starting a "when collided" script without a collision is a debugging act, not a run.

**Consequences:** stepping can run a script whose trigger never happened — deliberate, and what "walk through my program" means for a learner. The tips list under the table says so. Step back still restores every script and object (verified live across a multi-script step).

## ADR-016 — One dark band, light surfaces, and design tokens for the workspace chrome
**Status:** Accepted (user request, 2026-09-16: "search for all these type of apps like code blocks and make this one beautiful editor, make it clean"). Supersedes the chrome half of [[#ADR-012 — Learning aids: scene-wide playback in the scheduler, Reset from a start snapshot, friendly advice in the compiler layer|ADR-012]].
**Context:** The old chrome stacked three different dark slates (title bar, run bar, tab rail) before any content, and the how-to line was a single 30-word sentence of `·`-separated clauses.

**Decision — the rule taken from Scratch 3, Blockly and MakeCode:** one dark band, then light surfaces, so the only saturated color on screen is the blocks themselves (plus green Go and red Stop).
- **Tokens, not literals.** `blocky-ingame.uss` opens with a `:root` block of `--bk-*` custom properties (surfaces, ink, lines, accent, go/stop/warn). Every rule reads from them, so the workspace can be re-themed in one place. The block colors stay where they were, in `blocky-base.uss` / `blocky-*.uss`.
- **Header:** a wordmark tile, and what's being edited as a pill with a dot that lights green — a state, not a sentence — with the toggle key drawn as a key cap at the far end.
- **Run bar:** white, one hairline under it. Go / Stop / Reset in one recessed tray, Pause / Step ◀ / Step ▶ in a second, speed as a segmented control. Paused shows a warm tint plus a "Paused" flag instead of turning the whole bar amber.
- **The table has a dot grid**, painted straight onto the viewport with `Painter2D` (`BlockyInGamePanel.PaintGrid`) and stepped by the pan and zoom, so moving around feels like moving over a surface. One path, one `Fill` — the same single-pass rule `BlockOutline` follows — and the dots are 2px squares rather than arcs, so a screenful is a few thousand triangles, not tens of thousands. It sits under the canvas because an element's own painted content draws beneath its children.
- **The how-to sentence became four chips and a "?"** that opens the full list as a card. The rail marks the category you're on; the zoom cluster is one card with a percentage readout.
- **The workspace opens at `ComfortableWidth` (900) when the screen has room.** Measured live: at the scene's serialized 420 the table sat at its 120px floor, with the footer and the zoom cluster overlapping. The seam on the right still resizes it to anything from 360 up.
- **The palette scrolls sideways** (`ScrollerVisibility.Auto`) because the widest prototype is 344px against a 250px column. `SelectCategory` resets the horizontal offset afterwards: `ScrollTo` would otherwise scroll sideways to fit the widest block and leave every block clipped on its left.

**Consequences:** USS has no `box-shadow`, so "floating" is a white surface with a hairline border; no `text-transform`, so the palette's capitals are made in C#. The Editor window (`Blocky/Program Editor`) is unaffected — it doesn't load this sheet.

## Open questions carried from TDD §15
Track resolution here as decisions get made:
1. In-world authoring surface needed? → blocks §8.1 decision above
2. Runtime authoring in-Editor too? → affects whether `ProgramStore` needs an `EditorUtility.SetDirty` bridge
3. Per-object only, or shared/global programs across multiple targets?
4. Max realistic program size? → drives virtualization threshold and `Frame[]` capacity
5. Variable support before first release?

_No decisions made yet on 2–5 — flag if implementation choices force one._
