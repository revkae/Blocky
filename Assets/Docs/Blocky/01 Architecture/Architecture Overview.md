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
Runtime (VM)                    VmScheduler · Thread pool · IBlockOp[] + IConditionOp[] op tables · TriggerBroker · ObjectProgramRunner (MonoBehaviour)
```

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
```
