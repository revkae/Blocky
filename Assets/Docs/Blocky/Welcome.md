# Visual Block Scripting System — Technical Design Document

**Status:** Draft v1 (architecture pass) **Target:** Unity 6.x, UI Toolkit (runtime + editor), IL2CPP, WebGL + Standalone **Supersedes:** BlocksEngine2 (BE2)

---

## 1. Overview

A Scratch-style visual programming system for authoring per-object behaviour with stacking and nesting blocks. Blocks snap vertically into sequences; C-shaped blocks contain nested sequences. There are no wires, no node graph, no free-floating connection ports.

Each target GameObject owns an independent program made of one or more trigger-headed stacks. All programs run concurrently and independently.

### 1.1 Goals

| Goal                         | Concrete meaning                                                                                                          |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| Data-driven extensibility    | A new block ships as one `BlockDefinition` asset + one op class. Zero edits to editor UI or serialization code.           |
| Deterministic execution      | Given the same program, inputs and frame sequence, execution order is reproducible across runs and platforms.             |
| Zero steady-state allocation | After warm-up, a running program allocates 0 bytes per frame. Critical for WebGL's single-threaded, GC-sensitive runtime. |
| Structural isolation         | An editor UI bug cannot corrupt saved logic or alter execution semantics.                                                 |
| Headless testability         | The whole engine runs and is unit-tested with no scene, no UI, no MonoBehaviour.                                          |

### 1.2 Non-goals for v1

Deferred, but the data model and mutation API below are explicitly shaped so none of these require a schema break or a rewrite:

- Variables and lists
- Custom procedures / "define" blocks
- Expression (reporter) blocks
- Undo/redo UI (plumbing is built in v1, stack is disabled)
- Multi-object copy/paste of stacks
- Live collaborative editing

### 1.3 Dependencies

- **UniTask** — used only at the async adapter boundary (§6.6), not in the interpreter core.
- **Newtonsoft.Json** — required, not optional (§5.1).
- **No DI container.** Executor lookup is an array index resolved at link time; a container in the interpreter hot path buys nothing and costs a dictionary hit per block. Wire up top-level services however the host project prefers, but the op table is plain.

---

## 2. Terminology

|Term|Meaning|
|---|---|
|**Block**|One authored instruction. A `BlockNode` in data, a `BlockView` in UI, one or more instructions after compilation.|
|**Stack**|A trigger block plus the vertical sequence beneath it. The unit of execution.|
|**Program**|All stacks belonging to one target object.|
|**Thread**|One live execution of one stack. A stack may have at most one thread (§6.4).|
|**Op**|The runtime implementation of a block's behaviour.|
|**Opcode**|An `int` index into the linked op table. Strings never appear at runtime.|

---

## 3. Architecture

Four layers, not three. The compiler is separated out because it is where all string lookups, validation and parameter resolution happen — once, at load time, instead of per block per frame.

```
┌──────────────────────────────────────────────────────────┐
│  Editor UI  (UI Toolkit)                                 │
│  BlockView · PaletteView · ProgramCanvasView             │
│  BlockDragManipulator · DragLayer · ViewPool             │
└────────────────────┬─────────────────────────────────────┘
                     │ IProgramCommand  (only write path)
                     ▼
┌──────────────────────────────────────────────────────────┐
│  Data Model  (plain serializable C#)                     │
│  ProgramStore · ObjectProgram · BlockStack · BlockNode   │
│  No MonoBehaviour. No UnityEngine.Object references.     │
└────────────────────┬─────────────────────────────────────┘
                     │ ProgramCompiler.Link()
                     ▼
┌──────────────────────────────────────────────────────────┐
│  Compiled Program  (immutable, shareable, cacheable)     │
│  int[] instructions · ParamTable · jump targets          │
└────────────────────┬─────────────────────────────────────┘
                     │
                     ▼
┌──────────────────────────────────────────────────────────┐
│  Runtime  (VM)                                           │
│  VmScheduler · Thread pool · IBlockOp[] opTable          │
│  TriggerBroker · ObjectProgramRunner (MonoBehaviour)     │
└──────────────────────────────────────────────────────────┘
```

Dependency direction is strictly downward. The data model knows nothing about views, compilation or the VM. The VM never mutates authored data.

---

## 4. Data Model

All types are plain `[Serializable]` C# classes. No `MonoBehaviour`, no `ScriptableObject`, no `UnityEngine.Object` fields — object references are stored as string UIDs and resolved at link time.

### 4.1 Parameter value — tagged union

This is the single most important shape in the document. A flat `{key, stringValue, floatValue}` struct works until the first expression block, at which point a parameter must be able to hold a nested block and every saved file breaks. The union exists from v1 even though `Reporter` is unimplemented.

```csharp
public enum ParamKind : byte { Number = 0, Text = 1, Bool = 2, Choice = 3, ObjectRef = 4, Reporter = 5 }

[Serializable]
public sealed class BlockParam
{
    public string   key;        // matches ParamSpec.key on the definition
    public ParamKind kind;

    public float    number;     // Number
    public string   text;       // Text, ObjectRef (UID), Choice (stable id, NOT display string)
    public bool     boolean;    // Bool
    public BlockNode reporter;  // Reporter — null in v1, reserved
}
```

`Choice` stores a **stable string id or enum value**, never a localized display string. Localized strings change with locale and with copy-editing passes; matching against them produces silent runtime failures that only reproduce in one language.

### 4.2 Nodes, branches, stacks

```csharp
[Serializable]
public sealed class BlockNode
{
    public string       id;            // unique within the ObjectProgram
    public string       blockType;     // e.g. "motion.move_forward"
    public BlockParam[] parameters;
    public BlockNode[][] branches;     // one entry per body cavity; empty for non-C blocks
}
```

`branches` rather than a single `body`. `IfElse` has two cavities; a single list cannot express it, and adding `elseBody` later means a migration plus a special case in every tree walker. A jagged array costs nothing and covers `If` (1), `IfElse` (2) and any future multi-branch block.

```csharp
[Serializable]
public sealed class BlockStack
{
    public string       id;
    public string       triggerBlockType;
    public BlockParam[] triggerParameters;
    public BlockNode[]  sequence;
    public Vector2      canvasPosition;   // authoring only, ignored by the VM
}

[Serializable]
public sealed class ObjectProgram
{
    public int          schemaVersion;    // see §5.2
    public string       targetObjectUid;  // resolved once at link time
    public BlockStack[] stacks;
}
```

**Target resolution.** The VM receives exactly one resolved `GameObject` per program, looked up from `targetObjectUid` at link time and cached on the runner. No block ever resolves a target itself, and no block inherits a target from a parent scope. This structurally eliminates the BE2 class of bug where a motion block acted on the wrong object.

### 4.3 ID generation

`id` is a 16-char base64 of a `Guid` prefix, generated on node creation. Duplication (palette drag-out, future copy/paste) **re-generates ids for the whole subtree**. Uniqueness is asserted in `ProgramCompiler.Validate()`; a duplicate id is a hard compile error, not a warning, because it makes diffs and future undo/redo nondeterministic.

### 4.4 Mutation API — the only write path

```csharp
public interface IProgramCommand
{
    void Do(ProgramStore store);
    void Undo(ProgramStore store);
    string Describe();          // for the undo menu and for test failure messages
}

public sealed class ProgramStore
{
    public ObjectProgram Program { get; }
    public event Action<StructureChange> OnChanged;

    public void Apply(IProgramCommand cmd);   // pushes to history, raises OnChanged
    public bool CanUndo { get; }
    public void Undo();
}
```

v1 commands: `InsertNode`, `RemoveNode`, `MoveNode`, `SetParam`, `CreateStack`, `DeleteStack`, `MoveStack`.

The undo stack is implemented but capped at 0 entries in v1 (a config flag). Turning it on later is a one-line change plus UI. This is the cheapest insurance in the document: retrofitting undo means reifying every mutation after the fact, across UI code that has already grown organic direct-mutation paths.

`OnChanged` carries a `StructureChange` diff (affected stack id, affected node id, change kind) so views can rebuild one subtree instead of the canvas.

---

## 5. Serialization

### 5.1 Format

Plain JSON via **Newtonsoft.Json** with a custom `BlockParam` converter that omits unset union fields. `JsonUtility` is not viable: it cannot serialize polymorphic references, cannot serialize `null` in a way that round-trips cleanly, cannot do jagged arrays (`BlockNode[][]`) and cannot do dictionaries. Those are not edge cases here, they are §4.1 and §4.2.

Output is stable-ordered and indented so programs diff readably in version control.

```json
{
  "schemaVersion": 1,
  "targetObjectUid": "obj_7f3a91",
  "stacks": [
    {
      "id": "stk_a1",
      "triggerBlockType": "event.when_key_pressed",
      "triggerParameters": [ { "key": "key", "kind": "Choice", "text": "Space" } ],
      "canvasPosition": { "x": 40, "y": 40 },
      "sequence": [
        { "id": "n_01", "blockType": "control.repeat",
          "parameters": [ { "key": "times", "kind": "Number", "number": 4 } ],
          "branches": [ [
            { "id": "n_02", "blockType": "motion.move_forward",
              "parameters": [ { "key": "distance", "kind": "Number", "number": 1 } ] }
          ] ] }
      ]
    }
  ]
}
```

### 5.2 Versioning and migration

`schemaVersion` is written from the first commit. Load path:

1. Read `schemaVersion`. Absent → treat as 0 and reject with a clear message.
2. If `< CurrentVersion`, run registered `ISchemaMigration` steps in order, each `n → n+1`.
3. If `> CurrentVersion`, refuse to load. Never best-effort a future file; partial loads silently destroy user work on the next save.

Each migration ships with a fixture file of the old version and a round-trip test.

---

## 6. Execution Engine

### 6.1 Why a VM instead of async recursion

The obvious design is `UniTask Execute(BlockNode, GameObject, CancellationToken)` with control-flow ops awaiting their nested bodies recursively. It is faster to write and it is the wrong core, for reasons that all show up late:

- Execution state lives in C# stack frames. It cannot be inspected, stepped, paused, snapshotted or serialized.
- Call depth tracks user nesting depth. A deeply nested authored program is a stack overflow the user caused and cannot diagnose.
- Async state machines, even pooled ones, put a per-block cost on a path that runs thousands of times per frame.
- An infinite `RepeatForever` with no awaiting body deadlocks the frame, and the fix has to be bolted onto every loop op individually.
- Cancellation is cooperative and scattered across every op instead of centralized.

A flat instruction array plus an explicit thread state makes all five disappear, and it is how Scratch itself is built.

### 6.2 Compiled form

`ProgramCompiler.Link(ObjectProgram, BlockRegistry) → CompiledProgram` performs, once:

- `blockType` string → `int` opcode, by registry index
- parameter `key` string → `int` slot index, by `ParamSpec` order on the definition
- `Choice` id → enum/index value
- `targetObjectUid` → cached `GameObject`
- branch structure → jump targets (loops become `Jump` / `JumpIfFalse`; no recursion at runtime)
- validation: unknown block types, missing required params, out-of-range values, duplicate ids, C-blocks with wrong branch count

```csharp
public readonly struct Instruction
{
    public readonly int Opcode;
    public readonly int ParamOffset;   // into CompiledProgram.paramTable
    public readonly int ParamCount;
    public readonly int JumpA;         // branch/loop target, -1 if unused
    public readonly int JumpB;
    public readonly int SourceNodeId;  // index into a debug-only id table
}

public sealed class CompiledProgram
{
    public readonly Instruction[] Code;
    public readonly ParamValue[]  ParamTable;   // flat, no per-block arrays
    public readonly int[]         StackEntryPoints;
    public readonly string[]      DebugNodeIds; // stripped in release builds
}
```

`CompiledProgram` is immutable and cacheable. Identical programs across many objects share one instance; only `Thread` state is per-object.

After linking, the runtime touches **no strings and no dictionaries**.

### 6.3 Thread model

```csharp
public enum ThreadState { Running, YieldedFrame, Sleeping, Waiting, Done }

public sealed class Thread          // pooled, never allocated in steady state
{
    public CompiledProgram Program;
    public GameObject      Target;
    public int             Pc;
    public Frame[]         Frames;       // loop counters, branch returns; fixed capacity
    public int             FrameCount;
    public float[]         Scratch;      // per-instruction op state (timers, progress)
    public ThreadState     State;
    public float           WakeAt;
    public int             Generation;   // invalidates stale handles on restart
}
```

Ops are stepped, not awaited:

```csharp
public enum OpResult
{
    Continue,    // advance pc this same tick
    YieldFrame,  // advance pc, resume next frame
    Retry,       // do not advance pc, call again next frame (Wait, glide, long motion)
    Jump,        // op wrote ctx.NextPc
    Fail         // runtime error, thread halts, logged with SourceNodeId
}

public interface IBlockOp
{
    OpResult Execute(ref OpContext ctx);
}
```

`OpContext` is a `ref struct` holding the thread, the target, a `ReadOnlySpan<ParamValue>` of resolved params, and `dt`. No boxing, no closures, no allocation.

Dispatch is `opTable[instruction.Opcode].Execute(ref ctx)` — one array index, one interface call.

### 6.4 Execution semantics

These are product decisions, not implementation details. Leaving them unwritten is how two engineers ship two different behaviours.

- **Loop yield rule.** Every loop iteration yields at least one frame if its body executed no yielding instruction. Without this, `RepeatForever { }` hangs the frame. Enforced centrally in the loop opcodes, not per op.
- **Instruction budget.** Each thread gets a per-tick budget (default 10,000 instructions). Exceeding it forces a frame yield and logs a warning naming the offending block. This is the infinite-loop guard and it is free — it's a decrement in the dispatch loop.
- **Trigger re-entrancy.** Default `RestartOnRetrigger` (matches Scratch): a re-fired trigger resets the existing thread's pc to the entry point and bumps `Generation`. Per-trigger overridable to `IgnoreWhileRunning` or `AllowConcurrent` via `BlockDefinition`. One thread per stack under the two non-concurrent policies.
- **Tick order.** Threads are stepped in `(objectRegistrationIndex, stackIndex)` order. Registration index is assigned on `ObjectProgramRunner.OnEnable` and is stable for the object's lifetime. Threads started during a tick begin on the **next** tick, never mid-tick, so ordering never depends on trigger timing within a frame.
- **Update phase.** The scheduler ticks once per frame from a single `MonoBehaviour` in `Update`. Physics-affecting ops write to a deferred queue flushed in `FixedUpdate` rather than writing rigidbodies mid-`Update`.
- **Lifecycle.** Before stepping a thread, the scheduler null-checks the cached target. A destroyed target kills its threads and returns them to the pool. Individual ops never need destroyed-object guards, which removes an entire class of forgotten check.
- **Stop semantics.** `StopAll` clears all threads. Disabling a runner halts its threads; re-enabling does **not** resume them — triggers must fire again. Resuming mid-program after a scene change is a debugging trap, not a feature.

### 6.5 Scheduler loop

```csharp
public void Tick(float dt)
{
    _now += dt;
    for (int i = 0; i < _threads.Count; i++)
    {
        var t = _threads[i];
        if (t.State == ThreadState.Done) continue;
        if (t.Target == null) { Kill(t); continue; }
        if (t.State == ThreadState.Sleeping && _now < t.WakeAt) continue;

        int budget = InstructionBudget;
        t.State = ThreadState.Running;

        while (t.State == ThreadState.Running && budget-- > 0)
            Step(t, dt);

        if (budget < 0) WarnRunaway(t);
    }
    CompactAndAdmitPending();
}
```

### 6.6 Async adapter

UniTask is still useful for ops that wrap genuinely asynchronous work (asset loads, network, long tweens authored by other teams). It lives behind an adapter, not in the core:

```csharp
public interface IAsyncBlockOp
{
    UniTask ExecuteAsync(AsyncOpContext ctx, CancellationToken ct);
}
```

`AsyncOpBridge` starts the task on first execution, stores the handle in the thread's slot, and returns `Retry` until it completes — so an async op costs one allocation at start and zero per frame. Cancellation tokens are pooled per thread and linked to `Generation`, so restarts cancel cleanly without churning `CancellationTokenSource` instances.

### 6.7 Trigger system

`TriggerBroker` owns typed channels; runners subscribe on enable and unsubscribe on disable.

|Trigger|Source|Note|
|---|---|---|
|`WhenPlayClicked`|Broker broadcast on play|Fires once per play session|
|`WhenKeyPressed`|Single input poll per frame|One poll for all listeners, filtered by key id|
|`WhenCollided`|`BlockCollisionRelay` auto-added to targets with colliders|Relay forwards to broker, does not run blocks itself|
|`WhenLookedAt`|**One** shared camera raycast per frame, result compared against listener set|Never a raycast per object — that's the cost that scales badly|

---

## 7. Extensibility Contract

Adding a block is two files and no edits to existing code.

```csharp
[CreateAssetMenu(menuName = "Blocks/Block Definition")]
public sealed class BlockDefinition : ScriptableObject
{
    public string      blockType;        // stable id, lowercase invariant, never localized
    public string      displayNameKey;   // localization key
    public BlockShape  shape;            // Trigger | Statement | CBlock | Cap
    public BlockCategory category;       // drives color via USS class
    public int         branchCount;      // 0 statement, 1 If/Repeat, 2 IfElse
    public ParamSpec[] parameters;
    public string      executorKey;      // resolved to an opcode at registry build
    public RetriggerPolicy retrigger;    // triggers only
}

[Serializable]
public sealed class ParamSpec
{
    public string    key;
    public ParamKind kind;
    public string    defaultText;
    public float     defaultNumber;
    public float     min, max;           // Number clamping, validated at compile time
    public ChoiceEntry[] choices;        // { stableId, displayNameKey }
}
```

`BlockRegistry` scans definitions once at boot, assigns opcodes by sorted `blockType` (stable across builds, so serialized debug data stays comparable), and binds each `executorKey` to its `IBlockOp` instance via a source-generated or reflection-once map. Ops are stateless singletons; all mutable state lives on the thread.

**Never** apply culture-sensitive `ToLower`/`ToUpper` to `blockType`, `executorKey` or `ParamSpec.key`. In some cultures (notably Turkish) `"I".ToLower()` is not `"i"`, which turns a key lookup into a locale-dependent failure that passes every test on the build machine. Use `ToLowerInvariant` or, better, require lowercase ids and assert.

---

## 8. Editor UI (UI Toolkit)

### 8.1 UI Toolkit vs UGUI

|Concern|UI Toolkit|UGUI|
|---|---|---|
|Vertical stacking, cavity auto-resize|Flex layout, free|`VerticalLayoutGroup` + `ContentSizeFitter`, manual rebuild ordering, the single biggest source of block-editor bugs|
|Reparenting for nesting|`parent.Add(child)`|Transform reparent + layout invalidation + anchor fixups|
|Category theming|USS class per category|Per-prefab or scripted color assignment|
|Draw calls at 300+ blocks|Batched via UIR|Canvas rebuild cost, needs manual canvas splitting|
|Runtime drag & drop|Not built in — hand-rolled pointer capture|Not built in either|
|World-space / diegetic UI|Rough in Unity 6|Mature|
|Same code in Editor tooling|Yes|No|
|Ecosystem, tutorials, third-party assets|Thinner|Deep|

**Decision: UI Toolkit.** The flex layout and reparenting story alone removes the hardest part of the feature, and the same components work for an in-Editor authoring window. Choose UGUI only if blocks must be authored on an in-world surface, or if the team has no UITK experience against a hard deadline. Layout-pass cost at high block counts is the real risk and is mitigated in §8.4.

### 8.2 Component structure

- **`BlockView`** — one `VisualElement` subtree per node. Renders the shape, binds parameter fields from `ParamSpec`, exposes one `bodySlot` container per branch. Holds a node id, never a node reference, so a rebuilt model never leaves a stale pointer.
- **`ParamFieldFactory`** — maps `ParamKind` to a control (number field, text field, dropdown, object picker). Adding a param kind touches only this file.
- **`PaletteView`** — displays prototypes by category; on drag-out clones a fresh `BlockNode` with regenerated ids (§4.3).
- **`ProgramCanvasView`** — hosts top-level stacks, owns pan/zoom, subscribes to `ProgramStore.OnChanged`.
- **`DragLayer`** — a single absolutely-positioned overlay element, sibling of the canvas, that holds the dragged subtree.
- **`BlockDragManipulator`** — the drag state machine (§8.3).
- **`BlockViewPool`** — recycles views by block type.

### 8.3 Drag state machine

```
Idle ──PointerDown on grab area──▶ Picked
Picked ──move > threshold──▶ Dragging      (below threshold ──PointerUp──▶ click, edit param)
Dragging ──PointerMove──▶ Dragging         (recompute drop candidate, throttled)
Dragging ──PointerUp──▶ Committing ──▶ Idle
Dragging ──Esc / panel lost / canvas cleared──▶ Cancelling ──▶ Idle
```

- On **Picked**, the subtree is detached from its parent and reparented into `DragLayer` at the same screen position. Origin (parent id, branch index, sibling index) is recorded for cancel.
- Drop candidates are collected **once at drag start** into a flat list of `{rect, kind, targetId, branchIndex, index}` and rebuilt only on structural change, not per pointer move. Hit-testing walks a list, not the visual tree.
- Candidate kinds: `BodyCavity` (nest), `SequenceGap` (splice between blocks), `SequenceEnd` (append), `Canvas` (new top-level stack). Ties resolve innermost-first, so dropping onto a cavity inside a stack nests rather than splices.
- The best candidate is previewed with an insertion marker and a cavity highlight. Preview is visual only — the model is untouched until commit.
- **Commit** issues exactly one `MoveNode` or `InsertNode` command (§4.4). All structural truth flows through the store; the view then reacts to `OnChanged` like any other observer, so a dropped block and a loaded block take the same code path.
- **Cancel** restores from the recorded origin.

### 8.4 Edge cases (each with a test)

|Case|Handling|
|---|---|
|Two top-level stacks dropped at the same coordinate|On commit, if the position collides with an existing stack rect, nudge by `(16, 16)` until free|
|C-block cavity size|Cavity is `flex` with a `min-height` matching an empty-body placeholder; it resizes on every structural change, including mid-drag preview, not only at drag end|
|Canvas reset during an interrupted drag|Reset unconditionally clears **all** children including hidden and detached ones, then clears `DragLayer` and forces the manipulator to `Idle`. Prevents the silent-duplication bug where an in-flight subtree survives a reload and is re-added on drop|
|Z-ordering of the dragged block|`DragLayer` is the last sibling and is never a child of the dragged block's former parent, so it renders above everything and survives a canvas clear|
|Deep nesting|Editor soft-caps nesting depth (default 12) with a rejected-drop message. `Frame[]` capacity matches this cap plus headroom|
|Huge programs|Views are pooled and virtualized **per stack**: a stack whose bounds lie outside the viewport plus margin is not instantiated at all. A collapsed-stack toggle covers the pathological case|
|Param edit while running|Editing a param marks the `CompiledProgram` dirty; it relinks on next trigger fire, never mid-thread|

### 8.5 Styling

One USS file per category plus a base sheet. Colors, corner radii and notch shapes are USS custom properties, so re-theming touches no C#. Shapes are built with borders and background images, not per-block `MeshGenerationContext` callbacks, unless profiling demands otherwise.

---

## 9. Block Catalog (MVP parity)

Semantics column is normative — it defines yield behaviour, which is what makes a program feel right.

### Events

|Block|Params|Semantics|
|---|---|---|
|`event.when_play_clicked`|—|Fires once on play|
|`event.when_looked_at`|`angle_threshold`|Shared camera raycast, rising edge only|
|`event.when_key_pressed`|`key` (Choice)|Rising edge; restart-on-retrigger|
|`event.when_collided`|`tag_filter` (Text, optional)|From collision relay; restart-on-retrigger|

### Motion

| Block                   | Params                                      | Semantics                                                                                   |
| ----------------------- | ------------------------------------------- | ------------------------------------------------------------------------------------------- |
| `motion.move_forward`   | `distance`, `duration`                      | `duration = 0` → instant, `Continue`. Otherwise `Retry` until elapsed, using thread scratch |
| `motion.turn_direction` | `direction` (Choice), `degrees`, `duration` | As above                                                                                    |
| `motion.rotate_axis`    | `axis` (Choice), `degrees`, `duration`      | As above                                                                                    |
| `motion.set_position`   | `x`, `y`, `z`, `space` (Choice)             | Instant                                                                                     |
| `motion.set_rotation`   | `x`, `y`, `z`, `space` (Choice)             | Instant                                                                                     |

### Looks

`looks.change_color` (`color`, `duration`) · `looks.set_visible` (`visible`) · `looks.set_scale` (`x`, `y`, `z`, `duration`)

### Control

|Block|Params|Semantics|
|---|---|---|
|`control.wait`|`seconds`|Sets `Sleeping` + `WakeAt`|
|`control.if`|`condition` (Bool)|1 branch|
|`control.if_else`|`condition` (Bool)|2 branches|
|`control.repeat`|`times`|Loop counter in frame; yields once per iteration if body didn't yield|
|`control.repeat_forever`|—|Same yield rule; cannot fall through|
|`control.repeat_until`|`condition` (Bool)|Condition evaluated before each iteration|

`condition` params are `Bool` literals in v1 and become `Reporter` slots when expression blocks land — no schema change required (§4.1).

---

## 10. Validation and Error Handling

Three tiers, so a user error never looks like a crash:

1. **Authoring-time** — `ParamSpec` constraints enforced in the field controls. Out-of-range numbers clamp with a tooltip; invalid object refs show a warning badge on the block.
2. **Compile-time** — `ProgramCompiler.Validate()` returns a diagnostic list `{severity, nodeId, message}`. Errors block execution of that stack only, not the whole program, and the offending `BlockView` gets an error state. Unknown `blockType` (an asset removed after a save) becomes a visible placeholder block that preserves its serialized params so the data survives a round-trip.
3. **Runtime** — an op returning `Fail` halts only its own thread, logs once with the `SourceNodeId` resolved to a human-readable path, and flags the block in the editor if one is attached. One log per failure site per session; runtime spam is how real errors get missed.

---

## 11. Performance

### 11.1 Budgets

|Scenario|Budget|
|---|---|
|200 active objects × 3 stacks, ~10 instructions/thread/frame|< 0.5 ms scheduler time|
|Steady-state allocation while running|0 B/frame|
|Link time, 500-block program|< 5 ms|
|Editor layout pass, 300 visible blocks|< 2 ms|
|Drag pointer-move handler|< 0.2 ms|

### 11.2 Rules

- No LINQ, no `foreach` over interfaces, no closures, no `params` arrays anywhere in the scheduler or in ops.
- No string work at runtime: no concatenation, no interpolation, no `ToString` outside diagnostics guarded by a debug flag.
- Pool `Thread`, `CancellationTokenSource`, `BlockView`, and all editor lists. Pre-size every collection from registry counts.
- Structs passed by `in`/`ref`; `OpContext` is a `ref struct`.
- `CompiledProgram` shared across identical programs; only threads are per-object.
- WebGL specifics: single-threaded, so no `Task.Run` and no thread-pool assumptions; IL2CPP strips aggressively, so reflection-based op binding needs `[Preserve]` or a link.xml, and a source generator is preferable; GC spikes are visible as hitches, hence the zero-allocation rule.

### 11.3 Instrumentation

`ProfilerMarker` around `Tick`, `Step`, `Link` and the editor's layout and hit-test paths. A debug overlay shows live thread count, instructions/frame, budget overruns and allocation delta. Ship it behind a flag rather than deleting it — the numbers above are worthless if nobody can see the current ones.

---

## 12. Testing

- **Engine, headless.** Hand-authored `ObjectProgram` fixtures compiled and stepped against a mock target. Covers every control-flow shape, the loop yield rule, the instruction budget, retrigger policies, destroyed-target mid-execution, and tick-order determinism. No scene, no UI.
- **Golden traces.** A program plus a fixed frame sequence produces a recorded instruction trace. Any semantic regression shows as a trace diff. This is the highest-value test type here and it is cheap.
- **Serialization.** Round-trip every fixture; assert byte-identical output. One fixture per schema version, with its migration.
- **Compiler.** One negative test per diagnostic: unknown type, duplicate id, missing param, out-of-range, wrong branch count.
- **UI.** Synthetic pointer-event tests for each drop candidate kind, plus every §8.4 edge case, particularly interrupted-drag-then-canvas-reset.

---

## 13. Build Order

Each milestone ends in something demonstrable and tested.

1. **Data model + serialization.** Types, `ProgramStore`, commands, Newtonsoft converters, version/migration harness. Tests: round-trip.
2. **Registry + compiler.** `BlockDefinition`, `ParamSpec`, opcode assignment, `Link`, `Validate`. Tests: all diagnostics.
3. **VM.** Scheduler, thread pool, `IBlockOp`, control flow, `Wait`, two motion blocks. Hand-authored program runs with no UI. Tests: golden traces, yield rule, budget.
4. **Static UI.** `BlockView` + `ParamFieldFactory` rendering a parsed stack read-only. Proves binding and flex layout, including nested cavity sizing.
5. **Interaction.** `BlockDragManipulator`, `DragLayer`, drop candidates, commands on commit. Tests: §8.4.
6. **Integration.** Palette, canvas persistence, trigger broker, runner component. First end-to-end authored-and-run behaviour.
7. **Expansion.** Remaining catalog blocks, editor polish, view virtualization, profiling pass against §11.1.
8. **Deferred features**, in dependency order: undo UI → variables → expression blocks → custom procedures.

---

## 14. Risks

|Risk|Impact|Mitigation|
|---|---|---|
|UITK runtime layout cost at high block counts|Editor hitches on WebGL|Per-stack virtualization + view pooling from milestone 7; budget in §11.1; collapsible stacks as the escape hatch|
|Expression blocks force a data-model change anyway|Save-file break|`ParamKind.Reporter` reserved now; `branches` jagged now; migration harness exists from milestone 1|
|IL2CPP strips reflection-bound ops|Works in Editor, fails in build|Source-generated op binding, or `[Preserve]` + a build-verification test that runs a program in a real WebGL build|
|Drag/drop semantics drift between engineers|Inconsistent feel, bug reports that can't be reproduced|§8.3 state machine is normative; every candidate kind has a test|
|"Feels different from Scratch" complaints|Authoring friction for users with Scratch experience|§6.4 semantics deliberately match Scratch (restart on retrigger, per-iteration yield); deviations require an explicit note here|
|World-space authoring requirement appears late|UITK choice becomes wrong|Confirm before milestone 4; the data/compiler/VM layers are UI-agnostic, so only layer 4 would be rewritten|

---

## 15. Open Questions

1. Is the block editor ever needed on an in-world surface? Answer before milestone 4 (§14).
2. Do programs need to be authored in the Unity Editor as well as at runtime? If yes, the same `BlockView` components serve both, but `ProgramStore` needs an `EditorUtility.SetDirty` bridge.
3. Are programs per-object only, or will a shared/global program ever target multiple objects? Current model is deliberately per-object; multi-target would need a scope concept in the VM and should not be retrofitted casually.
4. Max realistic program size? The virtualization threshold and `Frame[]` capacity depend on it.
5. Is variable support expected before first release? It is the largest deferred item and it changes the palette layout, not just the model.