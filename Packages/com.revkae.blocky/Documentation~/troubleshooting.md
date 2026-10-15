# Troubleshooting

## Common questions

### Nothing reacts to the mouse or the keyboard

Blocky reads input through the Input System. Open **Edit › Project Settings › Player** and set *Active Input Handling* to **Input System Package (New)** or **Both**, then let Unity restart.

### The console says a ticker was added

> Blocky: no BlockyRuntimeTicker was in the scene, and no block runs without one, so one was added on a new 'Blocky Runtime' object. Add one to the scene yourself to choose where it lives.

This is a note, not a problem. Every scene that runs blocks needs one `BlockyRuntimeTicker`, and Blocky adds one when it is missing. To silence the note, add a `BlockyRuntimeTicker` to any object in the scene. You need your own anyway to change its switches (see [Scripting](scripting.md#the-ticker-blockyruntimeticker)).

### Clicking an object does nothing

- The object needs a **collider** (3D or 2D) or a **sprite**. A mesh with no collider can't be clicked.
- Something else may be in front of it: the click goes to the nearest collider.
- With the workspace open, a click on the workspace stays on the workspace.
- A game can turn picking off (`PickObjectsOnClick`), or `When Clicked` (the ticker's *Poll Clicked*). A press over your own interface doesn't count when your game added a check with `BlockyInput.AddPointerOverUiCheck`.
- There must be a camera tagged *MainCamera*: clicks are cast from `Camera.main`.

### The run bar, Undo and Delete do nothing

They act on the object on the table. Click an object in the game first.

### `When Key Pressed` never runs

It needs a physical keyboard, and the ticker's *Poll Keyboard* switch on. It runs once each time the key goes down; to act while a key is held, use `Repeat Forever` with `If <key pressed? […]>`.

### `When Collided` or `touching?` never fires

Both objects need colliders, and Unity only reports a contact when at least one of them has a `Rigidbody` (or a `Rigidbody2D` in 2D). With a tag filter, check the other object's tag.

### On a touchscreen, I can't scroll the palette

A finger on a palette block always drags it out. Scroll the palette with the tabs, or by dragging beside the blocks.

### A program I changed in the Editor doesn't run

A program changed in the game is saved, and wins over the scene's program the next time you press Play. Choose **Blocky › In-Game Saves › Delete All…** to go back to the scene's programs. See [Saving and loading](saving.md).

### A speech bubble is too small to read, or hidden

Bubbles are their real size in the world, and hidden behind things in front of them like any object. For a camera far from its objects, set `BlockySpeechBubble.ReadableDistance` as your game starts (see [Scripting](scripting.md#speech-bubbles-seen-from-far-away-blockyspeechbubble)).

### `Change Color` does nothing

- The color must be a hex code such as `#FF8800`, or a color name such as `red`.
- The object's materials need a `_BaseColor` or `_Color` property. URP Lit and Unlit and the Built-in Standard shader have one; some custom shaders don't.

### A block that names another object does nothing

Object inputs hold the other object's name, exactly as in the Hierarchy (case and spaces at either end are ignored). A name no object has makes the block do nothing. To avoid typing, a game can turn object inputs into a dropdown with `ParamFieldFactory.ObjectChoices`.

### My new block isn't in the palette

- It appears from the next Play after Unity has compiled its class.
- Its asset must be in a folder called `Resources/Blocks`.
- Check the console for compile errors in its class, and for a message saying no class is bound to it.

### Something I set from code is gone in the next Play session

Blocky resets its static settings at the start of every Play session — `BlockyStorage.Current`, the `ParamFieldFactory` hooks, `BlockySpeechBubble.ReadableDistance`, the `BlockyInput` checks and every `BlockyEvents` handler. Set them as your game starts, every session. See [Scripting](scripting.md#statics-are-reset-every-play-session).

## Known limits

- **Players program the objects placed in the scene.** They can make clones, but can't add new objects from the workspace.
- **My Blocks have three inputs**, always named a, b and c.
- **Hats and C-blocks** can't be added from a project: a new block is a command, a value or a question.
- **Dragging a block to the edge of the table doesn't scroll it.** Pan first, or drop the block and move it again.
- **The Program Editor window** edits with buttons; it has no drag and drop.
- **Touch** is built in but has not been tried on every kind of device. Mouse and keyboard are the most tested.
- **The High Definition Render Pipeline** has not been tested.

## Console messages

Every message Blocky writes starts with `Blocky:`.

| Message | Kind | What it means |
|---|---|---|
| no BlockyRuntimeTicker was in the scene… so one was added on a new 'Blocky Runtime' object. | Log | See [above](#the-console-says-a-ticker-was-added). |
| No IBlockOp (IConditionOp, IValueOp) bound to executorKey '…' (blockType '…'). Scripts that use that block will stop at it… | Error | A block asset has no C# class of the right kind, or its class doesn't compile. Write the class with `[BlockExecutor("…")]` and the matching interface, or fix it. The rest of the game runs. |
| block '…' failed: … | Warning | A block stopped its script: its class threw an exception (the message says which), returned `Fail`, or is missing. Logged once per block per session. |
| an "as" block on '…' found no object named '…', so the blocks inside it were skipped. | Warning | No object has the name typed into an `As` block. Logged once per run of the script. |
| the object of an "as" block on '…' was destroyed while its blocks ran, so the rest of them were skipped. | Warning | The object an `As` block was acting on was destroyed part-way through. |
| the saved program '…' couldn't be read (…). It was kept as '…', and the object runs its scene program instead. | Warning | A save file was damaged or from a newer Blocky. It was renamed to `….unreadable`. See [Saving and loading](saving.md#a-file-that-cant-be-read). |
| couldn't save the program '…': … | Warning | Writing a save file failed, for the reason given. The edit still runs. |
| the workspace isn't ready yet — its object has to be active before it can be opened or given an object. | Warning | `Open`, `Close` or `SetTarget` was called while the *Blocky Workspace* object was inactive. |
| language file '…' has no "locale" and was skipped. | Warning | A file in a `Resources/Languages` folder isn't a language file. Add `"locale"`. |
| language file '…' isn't valid JSON and was skipped: … | Warning | Fix the JSON error the message names. |
| the … text for '…' has a placeholder that doesn't match its values: … | Warning | A translation has a `{n}` that the sentence doesn't fill. The text is shown as it is. |
| this scene already has a workspace, "…". | Log (Editor) | **GameObject › Blocky › Workspace** found one and selected it. One per scene. |
| the workspace prefab is missing from Packages/com.revkae.blocky/Runtime/Prefabs/Blocky Workspace.prefab — reinstall the package. | Error (Editor) | The package is incomplete. Remove it and install it again. |
| made the block "…" — … | Log (Editor) | **Blocky › New Block…** wrote its files. |
| deleted the in-game saves in … | Log (Editor) | **Blocky › In-Game Saves › Delete All…** did its job. |

Some mistakes stop Blocky from starting at all, with an exception instead of a `Blocky:` message:

| Exception message | Fix |
|---|---|
| Duplicate blockType '…'. | Two block assets have the same `blockType`. Change one. |
| blockType '…' must be lowercase invariant | Block types are lowercase. |
| BlockDefinition '…' has no blockType. | Fill in the asset's `blockType`. |
| Duplicate executorKey '…': … and … | Two of your classes claim the same block. (A class of yours may replace one of Blocky's own.) |
| … has [BlockExecutor] but implements none of IBlockOp, IConditionOp, IValueOp. | Implement the interface that matches the block's shape. |
