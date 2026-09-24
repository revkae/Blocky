---
tags: [home, dashboard]
---

# Blocky — Visual Block Scripting System

This vault is the single source of truth for design, decisions, and progress. The full technical design lives in [[Welcome|Technical Design Document]] — everything below is a navigable breakdown of it, kept in sync as we build.

## Status: past v1 — a Scratch-style editor inside the game, not yet released

Milestones 1–7, the TDD's own v1, are done, and every goal in its §1.2 is met: data-driven extensibility, deterministic execution, zero-allocation-*capable* steady state (measurable through §11.3's `ProfilerMarker`s, not yet optimized against), structural isolation, headless testability. Work then went past v1 on purpose — the scoping conversation [[09 Decisions/Decisions#ADR-009|ADR-009]] asked for happened when the user asked for "most stuff that exists in scratch and delightex".

What exists today: **87 blocks** (38 steps, 16 conditions, 23 reporters, 10 triggers — [[06 Block Catalog/Block Catalog|Block Catalog]]) with an expression allowed in every input, variables, lists and a watcher, custom blocks with inputs and recursion, a `level complete` block, C# events for game code, and per-level toolboxes with block limits ([[12 Game Code/Blocky from CSharp|Blocky from C#]]), Light / Dark / High contrast themes, clones, broadcasts, say/think bubbles and generated notes; an in-game editor with Free and Simple modes, multi-select, undo/redo, Go / Stop / Reset, Pause, Step ◀ / ▶, 1x–4x, the running block lit up and plain-language advice; English and Türkçe; programs saved from the game run again the next time it starts ([[09 Decisions/Decisions#ADR-031|ADR-031]]).

**Tests:** 286/286 EditMode tests at the last full run inside Unity (2026-09-18). The language pass and the 2026-09-24 fixes added tests that have so far run only outside Unity — see [[08 Build Log/Build Log|Build Log]]. Nothing has been built into a player yet.

**Before release:** [[11 Release/Release Readiness|Release Readiness]] lists what's left for the Asset Store and for classrooms.

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
- [[11 Release/Release Readiness|Release Readiness]] — what's left before the Asset Store and the classroom, as a checklist
- [[12 Game Code/Blocky from CSharp|Blocky from C#]] — events and commands for game code (script finished, level complete, messages, the run bar), and puzzle levels: toolboxes and block limits

## Build Order (from the TDD, §13)

- [x] 1. Data model + serialization — done, see [[08 Build Log/Build Log|Build Log]]
- [x] 2. Registry + compiler — done, see [[08 Build Log/Build Log|Build Log]]
- [x] 3. VM — done, see [[08 Build Log/Build Log|Build Log]]
- [x] 4. Static UI — done, see [[08 Build Log/Build Log|Build Log]]
- [x] 5. Interaction (drag & drop) — done, see [[08 Build Log/Build Log|Build Log]]
- [x] 6. Integration (palette, canvas persistence, triggers, runner) — done, see [[08 Build Log/Build Log|Build Log]]. **A program can now run end-to-end from a real scene.**
- [x] 7. Expansion (full catalog, editor polish, virtualization, profiling) — done, see [[08 Build Log/Build Log|Build Log]]
- [x] 8. Deferred (undo UI → variables → expression blocks → custom procedures) — **done**: undo/redo in the in-game editor ([[09 Decisions/Decisions#ADR-012|ADR-012]], [[09 Decisions/Decisions#ADR-018|ADR-018]]), expression blocks ([[09 Decisions/Decisions#ADR-021|ADR-021]]), variables ([[09 Decisions/Decisions#ADR-023|ADR-023]]), lists ([[09 Decisions/Decisions#ADR-028|ADR-028]]), custom blocks ([[09 Decisions/Decisions#ADR-029|ADR-029]]). Lists and custom blocks have only been run outside Unity so far.

## Known gaps carried forward (not silently dropped)
- The language pass has never been compiled or run inside Unity ([[09 Decisions/Decisions#ADR-030|ADR-030]])
- No player build yet: IL2CPP/WebGL behaviour is untested. A `link.xml` now keeps the reflection-bound ops (TDD §14), but only a real build proves it
- The in-game editor is mouse-and-keyboard only, and 3D only (picking objects, `when clicked` and collisions use 3D physics)
- The Editor window (`Blocky/Program Editor`) has no undo
- A real mouse dragging blocks in the running game can't be driven by automation (Play-mode automation stalls the player loop), so many Build Log entries end with it unverified — it needs checking by hand
- Thread pooling / zero-allocation: the `ProfilerMarker`s can measure it (TDD §11.3), but the §11.1 budgets have never been measured
- `BlockViewPool` and the collapsed-stack toggle (TDD §8.4) are not built, and viewport virtualization (`ProgramCanvasView.SetViewport`) exists but nothing calls it yet

## Working rule

Every implementation session should:
1. Update the relevant section note (not just the code) when a design detail changes.
2. Append an entry to [[08 Build Log/Build Log|Build Log]].
3. Record any deviation from the original TDD as a [[09 Decisions/Decisions|Decision]], not silently.
