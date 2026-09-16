---
tags: [overview]
---

# Goals & Non-Goals

Source: [[Welcome|TDD §1]]

## Goals
- Data-driven extensibility — new block = one `BlockDefinition` asset + one op class, zero editor/serialization edits
- Deterministic execution — same program + inputs + frames → same result, every platform
- Zero steady-state allocation — 0 B/frame after warm-up (WebGL is GC-sensitive and single-threaded)
- Structural isolation — editor UI bugs can't corrupt saved logic
- Headless testability — engine runs with no scene, no UI, no MonoBehaviour

## Non-goals for v1
Deferred, but the data model is shaped so none of these need a schema break:
- Variables and lists
- Custom procedures ("define" blocks)
- Expression (reporter) blocks
- ~~Undo/redo UI~~ — **undo shipped in the in-game editor** (2026-09-14, [[09 Decisions/Decisions#ADR-012|ADR-012]]); redo and the Editor window's undo are still open
- Multi-object copy/paste of stacks
- Live collaborative editing

## Dependencies
- **UniTask** — async adapter boundary only ([[04 Runtime/Runtime VM#Async adapter|§6.6]]), not the interpreter core
- **Newtonsoft.Json** — required (see [[02 Data Model/Data Model#Serialization|Serialization]])
- **No DI container** in the interpreter hot path

See [[Home]] for the full map.
