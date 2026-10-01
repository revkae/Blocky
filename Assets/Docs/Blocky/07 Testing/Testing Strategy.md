---
tags: [testing]
---

# Testing Strategy

Source: [[Welcome|TDD §12]].

- **Engine, headless** — hand-authored `ObjectProgram` fixtures, compiled + stepped against a mock target. Covers every control-flow shape, loop yield rule, instruction budget, retrigger policies, destroyed-target mid-execution, tick-order determinism. No scene, no UI.
- **Golden traces** — program + fixed frame sequence → recorded instruction trace; any semantic regression shows as a trace diff. Highest-value, cheapest test type here.
- **Serialization** — round-trip every fixture, byte-identical output; one fixture per schema version + its migration.
- **Compiler** — one negative test per diagnostic (unknown type, duplicate id, missing param, out-of-range, wrong branch count).
- **UI** — synthetic pointer-event tests per drop-candidate kind, plus every edge case in [[05 Editor UI/Editor UI#Edge cases|Editor UI §8.4]], especially interrupted-drag-then-canvas-reset.

Uses `com.unity.test-framework` (already in the project).

## Running them: stop Play mode first
**Asking for an EditMode run while the editor is in Play mode wedges the whole editor.** It has happened twice, both times the same way: `run_tests` times out (6 minutes), and afterwards every other command — `editor_status`, `console_status`, `recompile_status`, `editor_stop` — times out too, for as long as an hour. Nothing recovers it from outside; the editor has to be dealt with by hand.

So the order is always: **`editor_stop` → `recompile` → `run_tests`**, and only then `editor_play` for a live check. Anything else risks losing the session.

**And check that the open scene has no unsaved changes.** With a dirty scene the Test Runner opens a modal "Scene(s) Have Been Modified" dialog before the run; the run hangs and every other command times out until someone answers it by hand (2026-10-01). Ask `EditorSceneManager.GetActiveScene().isDirty` first, and keep `eval` probes from creating objects in the open scene.

## The workspace is tested outside Play mode
`Blocky.Game.Tests` instantiates the shipped `Blocky Workspace` prefab in EditMode — `UIDocument` runs in edit mode, so the prefab comes with a real panel — and drives it through `internal` seams: `Build()` for `Awake`, `Tick()` for `Update`, `PickFromPress(hit)` for a click in the game. The ticker has the same (`Begin()`, `Poll(keyboard, camera)`). Each such test sets `BlockyStorage.Current` to a fake first: with the real file storage, a ticker's `Begin()` would hand runners to objects of whatever scene is open. What these tests cannot do is move a real pointer — dragging blocks still needs checking by hand in Play mode.

Three smaller habits worth keeping, each learned by losing a run to it:
- **A renderer's own material copy logs an error outside Play mode** ("Instantiating material due to calling renderer.material during edit mode"), and the runner counts an unexpected error as a failure. `ChangeColorTests` sets `LogAssert.ignoreFailingMessages` and frees the copies itself. **Set it in the test body, not in `[SetUp]`:** the runner gives every `SetUp` and `TearDown` method a log scope of its own (`BeforeAfterTestCommandBase`), the flag belongs to the scope it is set in, and it is read at the moment a message is logged — so a flag set in `SetUp` is gone before the test starts (four tests lost to this, 2026-10-02). For the same reason it needs no resetting and cannot leak into the next test.
- **`Object.Destroy` does nothing outside play mode** — it logs an error, and the test runner treats an unexpected error as a failure. Any runtime code that destroys something needs `Application.isPlaying ? Destroy : DestroyImmediate` ([[09 Decisions/Decisions#ADR-026|clones]], [[09 Decisions/Decisions#ADR-027|bubbles]]).
- **`Awake` never runs for a component added outside play mode.** Runtime pieces that register themselves must do it on first use, not in `Awake`, or they work in the game and silently do nothing in a test.
