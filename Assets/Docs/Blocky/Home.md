---
tags: [home, dashboard]
---

# Blocky — Visual Block Scripting System

This vault is the single source of truth for design, decisions, and progress. The full technical design lives in [[Welcome|Technical Design Document]] — everything below is a navigable breakdown of it, kept in sync as we build.

## Status: v1 complete (Milestones 1–7)

Every goal in the TDD's own §1.2 Goals section is met: data-driven extensibility, deterministic execution, zero-allocation-*capable* steady state (measured via §11.3's `ProfilerMarker`s, not yet optimized against), structural isolation, headless testability. All 18 v1 catalog blocks exist as real assets and run end-to-end from a real scene through `ObjectProgramRunner` and `TriggerBroker`. 85/85 tests passing throughout.

**Milestone 8 (undo UI, variables, expression blocks, custom procedures) is intentionally not started.** The TDD's own §1.2 lists all four as explicit v1 non-goals — "deferred, but the data model is shaped so none of these require a rewrite" — while §13's build order separately lists them as a future dependency chain. Read together: Milestones 1–7 *are* v1 by the document's own definition, and Milestone 8 is genuinely post-v1 scope. Decided 2026-09-13 to stop here rather than silently start building outside the document's stated v1 boundary — see [[09 Decisions/Decisions#ADR-009|ADR-009]].

## Map

- [[08 Build Log/Demo Scene|▶ Demo Scene]] — `Assets/Scenes/BlockyDemo.unity`, press Play to try it
- **✎ Program Editor** — menu `Blocky/Program Editor`: pick any GameObject, write its program directly (live fields, add/delete blocks)
- [[00 Overview/Goals and Non-Goals|Goals & Non-Goals]]
- [[01 Architecture/Architecture Overview|Architecture Overview]]
- [[02 Data Model/Data Model|Data Model]]
- [[03 Compiler/Compiler|Compiler]]
- [[04 Runtime/Runtime VM|Runtime VM]]
- [[05 Editor UI/Editor UI|Editor UI]]
- [[06 Block Catalog/Block Catalog|Block Catalog]]
- [[07 Testing/Testing Strategy|Testing Strategy]]
- [[08 Build Log/Build Log|Build Log]] — chronological progress, what shipped when
- [[09 Decisions/Decisions|Decisions]] — ADR-style records for anything that deviates from or extends the TDD
- [[10 Localization/Localization|Localization]] — English and Türkçe, and how to add a language (one file)

## Build Order (from the TDD, §13)

- [x] 1. Data model + serialization — done, see [[08 Build Log/Build Log|Build Log]]
- [x] 2. Registry + compiler — done, see [[08 Build Log/Build Log|Build Log]]
- [x] 3. VM — done, see [[08 Build Log/Build Log|Build Log]]
- [x] 4. Static UI — done, see [[08 Build Log/Build Log|Build Log]]
- [x] 5. Interaction (drag & drop) — done, see [[08 Build Log/Build Log|Build Log]]
- [x] 6. Integration (palette, canvas persistence, triggers, runner) — done, see [[08 Build Log/Build Log|Build Log]]. **A program can now run end-to-end from a real scene.**
- [x] 7. Expansion (full catalog, editor polish, virtualization, profiling) — done, see [[08 Build Log/Build Log|Build Log]]
- [ ] 8. Deferred (undo UI → variables → expression blocks → custom procedures) — **not started, by decision.** Post-v1 per TDD §1.2; pick up only when there's a real product reason to go past v1.

## Known gaps carried forward (not silently dropped)
- Live-editable param fields (`ParamFieldFactory` is still read-only by design — see [[05 Editor UI/Editor UI|Editor UI]])
- Interactive pan/zoom on `ProgramCanvasView` (rebuild-on-change and viewport-based virtualization both work; dragging/zooming the canvas itself does not exist yet — nothing drives `SetViewport` in real use)
- `BlockDragManipulator`/`DragLayer` — real pointer wiring implemented, not unit tested (needs a live panel)
- Thread pooling / zero-allocation (TDD §11.2 explicitly a "Milestone 7 profiling pass" item — the `ProfilerMarker`s exist now to *measure* this, but no pooling work has happened yet)
- `BlockViewPool` (view recycling by block type) and the collapsed-stack toggle — the two other Milestone 7 "huge programs" mitigations beyond viewport virtualization, TDD §8.4

## Working rule

Every implementation session should:
1. Update the relevant section note (not just the code) when a design detail changes.
2. Append an entry to [[08 Build Log/Build Log|Build Log]].
3. Record any deviation from the original TDD as a [[09 Decisions/Decisions|Decision]], not silently.
