---
tags: [compiler, milestone-2]
---

# Compiler

Source: [[Welcome|TDD §6.2, §7]]. Status: **done** (Milestone 2) — `Assets/Scripts/Blocky.Compiler/`. See [[09 Decisions/Decisions#ADR-002 — Instruction carries branch entry and exit pcs|ADR-002]] and [[09 Decisions/Decisions#ADR-003 — Compile errors are per-stack, not whole-program|ADR-003]] for the two places this build extended the TDD's illustrative shapes.

`ProgramCompiler.Link(ObjectProgram, BlockRegistry) → CompiledProgram`, once per load:
- `blockType` string → `int` opcode (registry index)
- param `key` string → `int` slot index (`ParamSpec` order)
- `Choice` id → enum/index value
- `targetObjectUid` → cached `GameObject`
- branch structure → jump targets (`Jump`/`JumpIfFalse`, no runtime recursion)
- validation: unknown block types, missing required params, out-of-range, duplicate ids, wrong branch count

`CompiledProgram` is immutable/cacheable — identical programs across many objects share one instance. After linking, **no strings, no dictionaries** at runtime.

## Extensibility contract
`BlockDefinition` ScriptableObject (`blockType`, `displayNameKey`, `shape`, `category`, `branchCount`, `parameters`, `executorKey`, `retrigger`) + `ParamSpec`. `BlockRegistry` scans once at boot, assigns opcodes by sorted `blockType`, binds `executorKey` → `IBlockOp`.

**Never** culture-sensitive `ToLower`/`ToUpper` on `blockType`/`executorKey`/`ParamSpec.key` (Turkish-I problem) — use `ToLowerInvariant` or require-lowercase + assert.

## Validation tiers
See [[07 Testing/Testing Strategy|Testing Strategy]] and TDD §10 — authoring-time (field clamps), compile-time (`Validate()` diagnostics, per-stack not whole-program failure), runtime (`Fail` halts one thread only).
