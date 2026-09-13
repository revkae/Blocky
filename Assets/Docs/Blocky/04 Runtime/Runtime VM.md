---
tags: [runtime, vm, milestone-3]
---

# Runtime VM

Source: [[Welcome|TDD §6]]. Status: **done** — scheduler, thread, `IBlockOp`, the full v1 control catalog, all 7 motion/looks ops (Milestone 7 added the remaining 6 — see [[06 Block Catalog/Block Catalog|Block Catalog]]), `TriggerBroker`, and `ObjectProgramRunner` (Milestone 6). `ProfilerMarker`s now instrument `Tick`/`Step` (Milestone 7, TDD §11.3). A program runs end-to-end from a real scene.

**Naming note:** the TDD's "Thread" is implemented as `VmThread` to avoid colliding with `System.Threading.Thread` — same concept, different name.

**How looping actually works (this wasn't fully specified in the TDD, see [[09 Decisions/Decisions#ADR-002|ADR-002]]):** a C-block op (e.g. `control.repeat`) pushes a `Frame{OwnerPc, ExitPc, Counter}` on first entry and jumps into its branch. The VM's `Step` doesn't know or care what kind of block owns a frame — every step it just asks "has pc reached the top frame's ExitPc?" and if so hands control back to `OwnerPc` without executing anything. It's the *op itself*, re-invoked at its own pc, that decides whether to loop again (jump back into the branch) or pop the frame and fall through to `ExitPc`. This is also where the loop-yield rule lives: the VM forces one frame yield per lap that never yielded on its own, so a body with zero yielding instructions still can't hang a game frame.

## Why a VM
Async recursion (`UniTask Execute(BlockNode, ...)`) is the obvious design and the wrong core: state lives in C# stack frames (uninspectable/unpausable), call depth = user nesting depth (uncontrolled stack overflow), async state machines cost per-block even pooled, infinite loops deadlock the frame per-op instead of centrally, cancellation is scattered. A flat instruction array + explicit thread state avoids all five — this is how Scratch itself is built.

## Shapes
- `Instruction` (readonly struct): Opcode, ParamOffset, ParamCount, JumpA/B, SourceNodeId (debug)
- `Thread` (pooled): Program, Target, Pc, Frames[] (fixed capacity), Scratch[], State, WakeAt, Generation
- `OpResult`: Continue / YieldFrame / Retry / Jump / Fail
- `IBlockOp.Execute(ref OpContext ctx)` — `OpContext` is a `ref struct`, no boxing/closures/allocation
- Dispatch: `opTable[instruction.Opcode].Execute(ref ctx)` — one array index, one interface call

## Execution semantics (product decisions, not details — TDD §6.4)
- Loop yield rule: every iteration yields ≥1 frame if the body executed no yielding instruction
- Instruction budget: default 10,000/tick, forces yield + warns naming the offending block
- Retrigger: default `RestartOnRetrigger` (resets pc, bumps Generation); overridable per-trigger to `IgnoreWhileRunning`/`AllowConcurrent`
- Tick order: `(objectRegistrationIndex, stackIndex)`, stable per object lifetime; threads started mid-tick begin next tick
- Physics-affecting ops write to a deferred queue flushed in `FixedUpdate`
- Destroyed target → scheduler kills its threads before stepping; ops never need destroyed-object guards
- `StopAll` clears all threads; disabling does not auto-resume on re-enable

## Async adapter
`IAsyncBlockOp.ExecuteAsync` behind `AsyncOpBridge` — starts on first execution, returns `Retry` until done (one allocation at start, zero per frame). Cancellation tokens pooled per thread, linked to `Generation`. This is the **only** place UniTask is used (see [[00 Overview/Goals and Non-Goals|dependencies]]).

## Trigger system
`TriggerBroker` typed channels, subscribe on enable/unsubscribe on disable. `WhenLookedAt` is one shared camera raycast per frame compared against the listener set — never per-object.
