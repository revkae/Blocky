---
tags: [architecture]
---

# Architecture Overview

Source: [[Welcome|TDD §3]]. Four layers, dependency direction strictly downward — the data model knows nothing about views, compilation, or the VM; the VM never mutates authored data.

```
Editor UI (UI Toolkit)          BlockView · HatView · ConditionView · ProgramCanvasView · TableDragManipulator · ChainDragSession · DragLayer
        │  IProgramCommand (only write path)
        ▼
Data Model (plain C#)           ProgramStore · ObjectProgram · BlockStack · BlockNode — no MonoBehaviour, no UnityEngine.Object refs
        │  ProgramCompiler.Link()
        ▼
Compiled Program (immutable)     int[] instructions · ParamTable · jump targets
        │
        ▼
Runtime (VM)                    VmScheduler (+ pause/step/speed) · Thread pool · IBlockOp[] + IConditionOp[] op tables · TriggerBroker · ObjectProgramRunner (MonoBehaviour) · WorldSnapshot · RunningBlocks · Playback
```

`ProgramAdvice` (Compiler layer) reads the data model and the registry to produce plain-language hints and problems; the in-game editor shows them. It never feeds the VM. The editor reads the VM only through `RunningBlocks` (which block is running) and the run bar's `BlockyRuntime` commands. See [[09 Decisions/Decisions#ADR-012|ADR-012]].

**Shared runtime state is per Play session.** The project enters Play mode with domain reload *off* (Enter Play Mode Options: `DisableDomainReload, DisableSceneReload`), so statics survive from one Play session to the next. `BlockyRuntime` therefore resets itself (scheduler, triggers, world snapshot, playback) through `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`. Any new static state needs the same treatment. See [[09 Decisions/Decisions#ADR-014|ADR-014]]. Every file with static state also opens with a `#pragma` for Unity's statics-cleanup analyzer and a line saying why — [[09 Decisions/Decisions#ADR-032|ADR-032]].

**Saved programs.** The in-game editor saves one file per object (`BlockyPrograms/<scene>/<path>.json`, `RuntimeProgramStorage`), and a runner runs its object's save instead of its scene program; `BlockyRuntimeTicker` gives a runner back to a saved object that has none. See [[09 Decisions/Decisions#ADR-031|ADR-031]]. The files are the default behind an interface: a game that keeps programs in its own save data sets `BlockyStorage.Current` to its own `IProgramStorage`, keyed by `GameObject`.

**Hosting.** Blocky can be the whole experience (its defaults) or a part of a game that has a flow of its own: the game opens, closes and targets the workspace, starts scripts, decides what is looked at and clicked, and stores programs — each through one seam, all additive ([[09 Decisions/Decisions#ADR-041 — A game can host Blocky: the game owns the flow, Blocky keeps its defaults|ADR-041]], [[12 Game Code/Hosting Blocky|Hosting Blocky]]). The layering holds: the two hooks the editor layer needs from a game (`ParamFieldFactory.ObjectChoices`, `IsHidden`) are delegates set from outside, because Blocky.Editor still references neither the runtime nor the scene.

Editor multi-select adds `BlockRef` and the group commands `MoveBlocks` / `DeleteBlocks` to the data layer, and `GroupDragSession` (an `IDragSession`, like `ChainDragSession`) to the editor layer.

## Layer notes
- [[02 Data Model/Data Model|Data Model]] — the only layer with serialized state
- [[03 Compiler/Compiler|Compiler]] — where every string lookup and validation happens, once, at load time
- [[04 Runtime/Runtime VM|Runtime VM]] — flat instruction array + explicit thread state, not async recursion (see [[04 Runtime/Runtime VM#Why a VM|why]])
- [[05 Editor UI/Editor UI|Editor UI]] — UI Toolkit, chosen over UGUI for flex layout + reparenting (see decision below)

## Key decision: UI Toolkit over UGUI
Recorded in [[09 Decisions/Decisions#ADR-001 UI Toolkit vs UGUI|ADR-001]].

## Folder layout (mirrors the layers)
Blocky is a Unity package, `com.revkae.blocky`, embedded in this project ([[09 Decisions/Decisions#ADR-040 — Blocky is a Unity package, embedded in this repo and installed from git|ADR-040]]); other projects install it from the git URL in its `README.md`.
```
Packages/com.revkae.blocky/
  package.json          name, version, Unity 6000.7, dependencies
  Runtime/
    Blocky.Data/          ObjectProgram, BlockNode, BlockStack, BlockParam, ProgramStore, commands
    Blocky.Compiler/      ProgramCompiler, BlockRegistry, CompiledProgram
    Blocky.Runtime/       VmScheduler, Thread, IBlockOp, TriggerBroker, ObjectProgramRunner
    Blocky.Editor/        BlockView, PaletteView, ProgramCanvasView, drag/drop — the in-game editor, so runtime
    Blocky.Game/          BlockyInGamePanel — the workspace
    Blocky.Localization/  BlockyText, BlockyLanguages, LanguageFileProvider — a leaf the Compiler, Editor and Game read words from
    Resources/Blocks/     one BlockDefinition asset per block
    Resources/Languages/  <code>.json — every word
    UI/                   BlockyPanelSettings and its theme
    Prefabs/              Blocky Workspace — what GameObject › Blocky › Workspace adds
    link.xml              keeps the reflection-only ops in IL2CPP builds
  Editor/
    Blocky.Tooling/       Program Editor, New Block wizard, menus, BlockyPaths
```
Each assembly keeps its `Tests/` beside it — Blocky.Game too, since 0.2.0 (`Blocky.Game.Tests`). Blocky.Runtime and Blocky.Game open a few `internal` seams to their own tests with `InternalsVisibleTo`. The project's own content stays in `Assets/`: the demo scene and its programs, and `Assets/BlockyBlocks` (a game's own block). Tools read Blocky's files from the package and write a project's under `Assets` — installed from git, the package is read-only.

Player-facing words live in `Runtime/Resources/Languages/<code>.json` in the package, never in code — see [[10 Localization/Localization|Localization]].
