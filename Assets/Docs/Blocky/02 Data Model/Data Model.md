---
tags: [data-model, milestone-1]
---

# Data Model

Source: [[Welcome|TDD §4-5]]. Status: **done** (Milestone 1) — see [[08 Build Log/Build Log|Build Log]] for what shipped and the Vector2 serialization gotcha.

Plain `[Serializable]` C# only. No `MonoBehaviour`, no `ScriptableObject`, no `UnityEngine.Object` fields — object refs are string UIDs resolved at link time. Code lives in `Assets/Scripts/Blocky.Data/`.

## Shapes
- `BlockParam` — tagged union (`ParamKind`: Number/Text/Bool/Choice/ObjectRef/Reporter). `Choice` stores a stable id, never a localized display string. **`reporter` is not part of the union**: any param may carry a block in it (a condition in a hexagonal hole, a reporter dropped on a value oval), and the literal stays beside it so pulling the block out restores what was typed — see [[09 Decisions/Decisions#ADR-021|ADR-021]] and [[09 Decisions/Decisions#ADR-022|ADR-022]]. `ParamKind.Reporter` now means only "this input is a hexagonal hole with no literal of its own".
- `BlockNode` — one instruction: `id`, `blockType`, `parameters[]`, `branches[][]` (jagged, one entry per cavity — covers If/IfElse/future multi-branch without a schema change).
- `BlockStack` — a trigger + its sequence: `id`, `triggerBlockType`, `triggerParameters[]`, `sequence[]`, `canvasPosition` (authoring-only).
- `ObjectProgram` — `schemaVersion`, `targetObjectUid`, `stacks[]`.
- IDs: 16-char base64 GUID prefix, regenerated for the whole subtree on duplication; uniqueness enforced as a hard compile error.

## Mutation API — the only write path
`IProgramCommand { Do, Undo, Describe }` applied through `ProgramStore.Apply()`, which raises `OnChanged` with a `StructureChange` diff. v1 commands: `InsertNode`, `RemoveNode`, `MoveNode`, `SetParam`, `CreateStack`, `DeleteStack`, `MoveStack`. Undo stack exists but is capped at 0 entries (config flag) — see [[00 Overview/Goals and Non-Goals|non-goals]].

## Serialization
Newtonsoft.Json, custom `BlockParam` converter omitting unset union fields. `JsonUtility` is not viable (no polymorphism, no clean null round-trip, no jagged arrays, no dictionaries).

**Versioning:** `schemaVersion` written from the first commit. Load path: read version → migrate forward via `ISchemaMigration` steps if older → refuse to load if newer (never best-effort a future file). Each migration ships a fixture + round-trip test.

## Tests to write (TDD §12)
- Round-trip every fixture, byte-identical output
- One fixture per schema version + its migration
