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
