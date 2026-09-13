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

## Open questions carried from TDD §15
Track resolution here as decisions get made:
1. In-world authoring surface needed? → blocks §8.1 decision above
2. Runtime authoring in-Editor too? → affects whether `ProgramStore` needs an `EditorUtility.SetDirty` bridge
3. Per-object only, or shared/global programs across multiple targets?
4. Max realistic program size? → drives virtualization threshold and `Frame[]` capacity
5. Variable support before first release?

_No decisions made yet on 2–5 — flag if implementation choices force one._
