# Custom blocks

There are two ways to get a block Blocky doesn't have:

- **Players** make their own from other blocks, in the game, with the **My Blocks** tab.
- **Developers** add a real block in C#, with **Blocky › New Block…**.

This page also covers **toolboxes**: which blocks a level offers, and how many a program may use.

## My Blocks, for players

A block of your own is a script with a `define` hat. A `run` block with the same name runs it.

1. Drag **define** out of the *My Blocks* tab and type a name in it, say *square*.
2. Put blocks under it.
3. From any script on the same object, use **run** with the name *square*. Once the object has a `define`, the *My Blocks* tab also offers a ready-made `run [square]`, so you don't have to type the name again.

```text
define [square]
Repeat (4)
    Move Forward  distance (input a)
    Turn  direction [right]  degrees (90)

when Go clicked
run [square]  a (2)
run [square]  a (4)
```

When `run` runs, the definition runs, and then the script carries on after the `run`.

### Inputs a, b and c

Every `run` block has three inputs, **a**, **b** and **c**. Their values are worked out when the `run` runs. Inside the definition, **input a**, **input b** and **input c** read them. Outside a definition they read as empty.

### Recursion

A definition can run itself. Each call gets its own a, b and c, and `input a` reads the innermost call's.

```text
define [countdown]
If <(input a) > (0)>
    Say  message (input a)  seconds (0.5)
    run [countdown]  a ((input a) - (1))

when Go clicked
run [countdown]  a (5)
```

A block that runs itself waits one frame first, so a recursive drawing unfolds on screen, and a recursion that never ends slows down instead of freezing the game. Loops and `run` blocks nest up to 256 deep together; past that a `run` does nothing.

### Rules

- Names ignore case and spaces at either end, and belong to the object. A clone has its original's definitions.
- A `define` never starts on its own — not on Play, Go or Step ▶. Only `run` starts it.
- `Stop [this script]` inside a definition ends the whole script that ran it.
- Of two definitions with the same name, the first one runs. A `run` whose name nothing defines does nothing. The advice under the table points out both, and a `define` with no name.

## New blocks in C#, for developers

A block is two things:

1. A **`BlockDefinition` asset** in a `Resources/Blocks` folder: its type, its name, its shape, its palette tab and its inputs.
2. A **C# class** with a `[BlockExecutor]` attribute that does the work.

Blocky finds both by itself. Every `Resources/Blocks` folder in the project is read, and every `[BlockExecutor]` class in an assembly that references `Blocky.Runtime` is found. There is nothing to register, and nothing in the palette, the compiler or the save files to change.

### Blocky › New Block…

**Blocky › New Block…** makes all of it in one go. It asks for:

| Field | What to enter |
|---|---|
| **Name on the block** | The words on the block, such as *jump*. |
| **Kind** | **Command** — a stack block that does something. **Value** — an oval that answers a number or text. **Question** — a hexagon that answers yes or no. |
| **Palette tab** | The category it appears under. |
| **Block type (optional)** | The id programs save, as lowercase words joined by dots. Empty means `game.<name>`. Keep a prefix of your own, so a later version of Blocky can't take the same id. |
| **Folder** | Where the files go. It must be inside `Assets`. `Assets/BlockyBlocks` by default. |
| **Inputs** | **+ Add input** for each. Give it a *Label* and say what it *Holds*: Number, Text, Choice (a dropdown — list the choices, comma-separated), Object (another object, by name) or Condition (a hexagon hole). *Starts as* is its starting value. |

**Make the block** writes three things into the folder:

- `<Folder>/<ClassName>.cs` — the class, with every input already read. A command's class ends in `Op` (`JumpOp`), a value's in `Value`, a question's in `Condition`.
- `<Folder>/Resources/Blocks/<block_type>.asset` — the `BlockDefinition`, named after the block type with dots as underscores (`game_jump.asset`).
- `<Folder>/Resources/Languages/<code>.blocks.json` — the block's words, for every language Blocky has. Each gets the English words; translate them in place. See [Languages](languages.md#words-for-your-own-blocks).

The class opens in your code editor. Write what the block does; it is in the palette from the next Play.

Everything goes under `Assets`, never into the package: installed from the Asset Store or git, the package is read-only, and updating Blocky never touches your blocks.

The wizard makes commands, values and questions. It doesn't make hats or C-blocks: a hat needs Blocky's runner to know its event, and a C-block needs the virtual machine's loop frames.

### The example: `jump`

This is what the wizard made for a command called *jump* with one number input, *amount*, starting at 10. The block asset is `Assets/BlockyBlocks/Resources/Blocks/game_jump.asset`, and the class is `Assets/BlockyBlocks/JumpOp.cs`:

```csharp
using Blocky.Runtime;
using UnityEngine;
using UnityEngine.Scripting;

[BlockExecutor("game.jump")]
[Preserve] // Blocky makes this class by reflection and nothing else refers to it: keep it in stripped builds
public sealed class JumpOp : IBlockOp
{
    [Preserve]
    public JumpOp() { }

    public OpResult Execute(ref OpContext ctx)
    {
        var self = ctx.Target; // the object running the script
        var amount = ctx.GetNumber(0); // "amount"

        // What the block does. Return OpResult.Continue when it's done. For something that takes time, keep
        // its progress in ctx.Scratch and return OpResult.Retry until it's finished — see Blocky's MoveForwardOp.
        return OpResult.Continue;
    }
}
```

Its words, in `Assets/BlockyBlocks/Resources/Languages/en.blocks.json`:

```json
{
  "locale": "en",
  "name": "English",
  "strings": {
    "block.game.jump": "jump",
    "param.amount": "amount"
  }
}
```

### How a command works

A command implements `IBlockOp`. The virtual machine calls `Execute` when a script reaches the block, and the `OpResult` it returns says what happens next:

| `OpResult` | Meaning |
|---|---|
| `Continue` | Done. The script moves on to the next block, in the same frame if it has time. |
| `YieldFrame` | Done, but the script waits for the next frame before moving on. |
| `Retry` | Not done. The script stays on this block, and `Execute` is called again next frame. |
| `Jump` | Go to the instruction written in `ctx.NextPc`. Blocky's own control blocks use it. |
| `Fail` | Something went wrong. The script stops, and the console names the block. |

`OpContext` is what the block can see:

| Member | What it is |
|---|---|
| `ctx.Target` | The object the block acts on: the one running the script, or inside an `As` block, the object `As` names. |
| `ctx.Thread.Target` | The object whose script it is, whatever `As` says. |
| `ctx.GetNumber(i)`, `ctx.GetText(i)`, `ctx.GetBool(i)`, `ctx.GetValue(i)` | Input *i*, in the order of the asset's inputs. These run whatever value block the player dropped into the input — always read inputs through them. |
| `ctx.Params[i].ChoiceIndex` | Which choice a dropdown input has, counting from 0 in the order of the asset's choices. |
| `ctx.DeltaTime` | This frame's time step in seconds, already multiplied by the run bar's speed. |
| `ctx.Now` | The scripts' clock, in seconds. |
| `ctx.Scratch` | One `float` that belongs to this block in this script, kept between calls. |

**One instance of your class serves every script.** Keep nothing about a running block in fields. What a block needs to remember between frames goes in `ctx.Scratch`. Because Blocky saves and restores `ctx.Scratch` itself, Pause, Step ▶ and ◀ Step work for your block too.

### A block that takes time

A block that takes time keeps its progress in `ctx.Scratch` and returns `Retry` until it is finished. This is Blocky's own `Move Forward` (`Runtime/Blocky.Runtime/Ops/MoveForwardOp.cs`):

```csharp
[BlockExecutor("motion.move_forward")]
public sealed class MoveForwardOp : IBlockOp
{
    public OpResult Execute(ref OpContext ctx)
    {
        var distance = ctx.GetNumber(0);
        var duration = ctx.GetNumber(1);
        var transform = ctx.Target.transform;
        var forward = BlockyPlane.Forward(transform, BlockyPlane.IsFlat(ctx.Target));

        if (duration <= 0f)
        {
            transform.Translate(forward * distance, Space.World);
            return OpResult.Continue;
        }

        var elapsed = ctx.Scratch;
        var step = Mathf.Min(ctx.DeltaTime, duration - elapsed);
        transform.Translate(forward * (distance * step / duration), Space.World);
        elapsed += step;

        if (elapsed >= duration)
        {
            ctx.Scratch = 0f;
            return OpResult.Continue;
        }

        ctx.Scratch = elapsed;
        return OpResult.Retry;
    }
}
```

`ctx.Scratch` holds the time spent so far. Each frame the block moves a slice of the distance, and when the time is up it sets `ctx.Scratch` back to 0 for next time and returns `Continue`.

The same pattern finishes the `jump` block: a hop of `amount` units that takes half a second and lands where it started.

```csharp
public OpResult Execute(ref OpContext ctx)
{
    const float duration = 0.5f;
    var height = ctx.GetNumber(0); // "amount"
    var transform = ctx.Target.transform;

    var elapsed = ctx.Scratch;
    var step = Mathf.Min(ctx.DeltaTime, duration - elapsed);
    // Move by the change in height along an arc that is 0 at the start and the end, and 1 at the top.
    var rise = Arc((elapsed + step) / duration) - Arc(elapsed / duration);
    transform.Translate(Vector3.up * (rise * height), Space.World);
    elapsed += step;

    if (elapsed >= duration)
    {
        ctx.Scratch = 0f;
        return OpResult.Continue;
    }

    ctx.Scratch = elapsed;
    return OpResult.Retry;
}

private static float Arc(float t) => 4f * t * (1f - t);
```

### Values and questions

A **value** (an oval) implements `IValueOp` and returns a `BlockValue`:

```csharp
[BlockExecutor("game.height")]
[Preserve]
public sealed class HeightValue : IValueOp
{
    [Preserve]
    public HeightValue() { }

    public BlockValue Evaluate(ref SlotContext ctx) => BlockValue.Number(ctx.Target.transform.position.y);
}
```

`BlockValue.Text(...)` and `BlockValue.Boolean(...)` work too.

A **question** (a hexagon) implements `IConditionOp` and returns a `bool`:

```csharp
[BlockExecutor("game.on_the_ground")]
[Preserve]
public sealed class OnTheGroundCondition : IConditionOp
{
    [Preserve]
    public OnTheGroundCondition() { }

    public bool Evaluate(ref SlotContext ctx) => ctx.Target.transform.position.y <= 0.01f;
}
```

Values and questions are asked every time a block reads them — a question in a `Repeat Until` is asked every lap. Answer at once, and change nothing. `SlotContext` has `Target`, `GetNumber`, `GetText`, `GetBool` and `GetValue` like `OpContext`, plus `Timer` and `Now`.

### Rules for block classes

- The class needs a public constructor without parameters, and it must implement `IBlockOp`, `IValueOp` or `IConditionOp` to match the block's shape.
- The class must be in an assembly that references `Blocky.Runtime`. Scripts in `Assets` without an assembly definition do. An assembly definition of your own needs a reference to `Blocky.Runtime`, and to `Blocky.Compiler` if you read `ctx.Params`.
- Keep `[Preserve]` on the class and its constructor. Blocky makes the class by reflection, and nothing else refers to it, so a stripped build would otherwise remove it. See [Builds](builds.md).
- A class of yours with the key of a built-in block replaces Blocky's. Two classes of your own with one key is an error.
- A block whose class is missing, or doesn't compile yet, doesn't stop the game. The console says which block has no class when the game starts, and a script that reaches that block stops there.

### Making the asset by hand

**Assets › Create › Blocky › Block Definition** makes an empty `BlockDefinition`. Put it in a `Resources/Blocks` folder and fill in:

| Field | What it is |
|---|---|
| `blockType` | The id programs save, lowercase words joined by dots. Never change it once programs use it. |
| `displayNameKey` | The English name, shown until a language file names the block. |
| `shape` | `Statement` (a stack block), `Boolean` (a hexagon) or `Reporter` (an oval) for blocks of your own. |
| `category` | The palette tab. |
| `branchCount` | 0 for blocks of your own. |
| `parameters` | The inputs: a `key`, a `kind` (`Number`, `Text`, `Bool`, `Choice`, `ObjectRef` or `Reporter` for a hexagon hole), a starting value (`defaultText` or `defaultNumber`), `min` and `max` for numbers, and `choices` for a dropdown — each with a `stableId` that programs save and a `displayNameKey`. |
| `executorKey` | The key in your class's `[BlockExecutor("…")]`. Usually the same as `blockType`. |
| `retrigger` | How a hat reacts to firing again; leave it as it is for other blocks. |

## Toolboxes and block limits

A **toolbox** decides what one level lets the learner use: which blocks the palette offers, and how many blocks a program may have ("solve it in 5 blocks").

1. Choose **Assets › Create › Blocky › Toolbox**.
2. Set **Show** to **Only These** or **All Except These**.
3. Add whole **Categories**, and single **Blocks**. Drag block assets in from *Packages › Blocky › Runtime › Resources › Blocks* in the Project window.
4. Set **Block Limit**. 0 means no limit. Hats don't count.
5. Put the toolbox in the **Toolbox** field of the scene's `BlockyInGamePanel` (on the *Blocky Workspace* object).

A toolbox limits what can be added, not what runs: blocks already in a program keep running. A program over the limit shows red and can only lose blocks.

**Remember the hats.** With *Only These*, a learner who can't take a `when Go clicked` out of the palette can't start a script. List one, or give the object a starting script that has it.

From code, a game can hand each level its own toolbox:

```csharp
using Blocky.Data;
using Blocky.Game;

panel.Toolbox = levelToolbox;   // redraws the palette at once; null: every block, no limit
int used = panel.BlocksUsed;    // the edited object's program, counted the way the limit counts
int count = ProgramQuery.CountBlocks(program); // the same count for any ObjectProgram
```

### A puzzle level, step by step

1. Give the robot a starting program in the scene: `when Go clicked` with nothing under it. Or allow that hat in the toolbox.
2. Make a toolbox: *Only These* — the Motion category and the `Repeat` block — with a limit of 5.
3. Put it on the level's `BlockyInGamePanel`.
4. On the goal: `When Collided` → `level complete`. Or check the result in C# (see [Scripting](scripting.md#example-a-puzzle-level)).
5. React to `BlockyEvents.LevelCompleted` — the next level, stars — and to `BlockyEvents.WorldReset` to put the level back.
