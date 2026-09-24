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

**Shared runtime state is per Play session.** The project enters Play mode with domain reload *off* (Enter Play Mode Options: `DisableDomainReload, DisableSceneReload`), so statics survive from one Play session to the next. `BlockyRuntime` therefore resets itself (scheduler, triggers, world snapshot, playback) through `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`. Any new static state needs the same treatment. See [[09 Decisions/Decisions#ADR-014|ADR-014]].

Editor multi-select adds `BlockRef` and the group commands `MoveBlocks` / `DeleteBlocks` to the data layer, and `GroupDragSession` (an `IDragSession`, like `ChainDragSession`) to the editor layer.

## Layer notes
- [[02 Data Model/Data Model|Data Model]] — the only layer with serialized state
- [[03 Compiler/Compiler|Compiler]] — where every string lookup and validation happens, once, at load time
- [[04 Runtime/Runtime VM|Runtime VM]] — flat instruction array + explicit thread state, not async recursion (see [[04 Runtime/Runtime VM#Why a VM|why]])
- [[05 Editor UI/Editor UI|Editor UI]] — UI Toolkit, chosen over UGUI for flex layout + reparenting (see decision below)

## Key decision: UI Toolkit over UGUI
Recorded in [[09 Decisions/Decisions#ADR-001 UI Toolkit vs UGUI|ADR-001]].

## Folder layout (mirrors the layers)
```
Assets/Scripts/
  Blocky.Data/       ObjectProgram, BlockNode, BlockStack, BlockParam, ProgramStore, commands
  Blocky.Compiler/   ProgramCompiler, BlockRegistry, CompiledProgram
  Blocky.Runtime/    VmScheduler, Thread, IBlockOp, TriggerBroker, ObjectProgramRunner
  Blocky.Editor/     BlockView, PaletteView, ProgramCanvasView, drag/drop
  Blocky.Localization/  BlockyText, BlockyLanguages, LanguageFileProvider — a leaf the Compiler, Editor and Game read words from
```

Player-facing words live in `Assets/Resources/Languages/<code>.json`, never in code — see [[10 Localization/Localization|Localization]].
