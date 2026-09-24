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

Two smaller habits worth keeping, both learned by losing a run to them:
- **`Object.Destroy` does nothing outside play mode** — it logs an error, and the test runner treats an unexpected error as a failure. Any runtime code that destroys something needs `Application.isPlaying ? Destroy : DestroyImmediate` ([[09 Decisions/Decisions#ADR-026|clones]], [[09 Decisions/Decisions#ADR-027|bubbles]]).
- **`Awake` never runs for a component added outside play mode.** Runtime pieces that register themselves must do it on first use, not in `Awake`, or they work in the game and silently do nothing in a test.
