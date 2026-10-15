# Using the workspace

The workspace is where players build programs, while the game runs. This page walks through everything on it.

## Open and close it

Press **Tab**, or click the **Blocks** button in the corner of the game. The workspace opens on the left side of the screen and the game stays visible and clickable on the right.

To close it, press **Tab** again or click the button in the title bar that shows the key. Drag the strip on the workspace's right edge to make it wider or narrower.

The key, the corner button and picking by a click can all be changed or turned off. See [Scripting › Hosting Blocky](scripting.md#hosting-blocky-in-your-own-game-flow).

## Pick an object

Click an object in the game to put its program on the table. Until you do, the title bar says *Click an object in the game to code it*, and the run bar, Undo and Delete do nothing.

You can program any object you can click: it needs a collider (3D or 2D) or a sprite. An object without a program gets an `ObjectProgramRunner` the moment you pick it. A click picks the object whose collider it hit: on a model whose colliders sit on its children, that is the child. A developer can map it to the model's root (see `PickResolver` in [Scripting](scripting.md#the-workspace-from-code)).

The palette is ready before you pick anything, so you can look through it first. Dragging blocks out starts working once an object is on the table.

## The parts of the workspace

- **The title bar** — the mode switch (*Free* / *Simple*), one button per language, the theme button, and the close button.
- **The run bar**, under the title — Go, Stop, Reset, Pause, Step and Speed.
- **The tab rail** — one colored tab per category that has blocks. Click a tab to scroll the palette to that category.
- **The palette** — every block, drawn in its real shape. Drag the seam between the palette and the table to resize it.
- **The table** — where the object's program is built.
- **The advice list**, under the table — why a script won't do anything.

## Block shapes

The shape of a block tells you where it fits.

| Shape | Examples | Fits |
|---|---|---|
| **Hat** — a rounded top | `when Go clicked`, `When Key Pressed` | The top of a script. Nothing goes above it. A script runs when its hat's event happens. |
| **Stack block** — a notch on top, a tab underneath | `Move Forward`, `Say` | Above and below other stack blocks. |
| **C-block** — a mouth | `If`, `Repeat`; `If Else` has two | Blocks go inside its mouth, and above and below it. |
| **Cap** — no tab underneath | `Repeat Forever`, `Delete This Clone` | The end of a script. Nothing can follow it. |
| **Hexagon** — a condition | `touching?`, `=`, `and` | A hexagon hole in another block, such as the one in `If`. |
| **Oval** — a value | `timer`, `+`, `pick random` | Any white input oval. A hexagon fits there too, and reads as true or false. |

## Free mode

Free mode is the free table: blocks go anywhere, several scripts sit side by side, and the table pans and zooms. It is the default.

### Build a script

- **Place a block.** Drag it out of the palette and drop it on the table. On its own it lies loose.
- **Snap it.** Bring a block close to another and the edge it will join glows. Let go to snap them together.
- **Move blocks.** Grab a block anywhere — its text and input boxes too — and it comes with every block under it. Grab a hat and the whole script comes along.
- **Change an input.** Click a number or text box and type. The value is kept when you press Enter or click somewhere else.

Blocks with no hat on top are saved but never run. Snap them under a hat to make them a script.

### Conditions and values

- **A condition into a hexagon hole.** Drag a hexagon block near the hole until the hole glows, and let go. Or click an empty hole and pick a condition from the list. An empty hole counts as false. Dropping a condition on a filled hole swaps them, and the old one lands on the table.
- **A value into an oval.** Drop an oval block on any white input. The block replaces the typed value; drag it out again and the box comes back.

Blocks nest as deep as you like: `Move Forward ((position x) + (pick random 1 to 3))` is fine.

### Select, move and delete

- **Select** a block by clicking it. It turns lighter, with a white outline.
- **Select several** with **Shift+click** (or **Ctrl+click**), or by dragging a box over empty table with the left mouse button. Hold Shift while boxing to add to the selection.
- **Move a group** by dragging any selected block. The whole group moves and lands loose, keeping its layout. A group never snaps; drag one block on its own to connect it.
- **Delete** with **Delete** or **Backspace**, or by dropping blocks back on the palette. Deleting one block from the middle of a script removes only that block, and the blocks below close up. Deleting a hat leaves its blocks on the table.

### Look around

- **Pan:** drag empty table with the right or middle mouse button.
- **Zoom:** scroll the mouse wheel to zoom toward the pointer, or use the **+** and **−** buttons in the corner. **=** puts the view back. The zoom goes from 30% to 250%, and the corner shows the current percentage.

## Simple mode

Simple mode is one numbered column, for learners who haven't built a mental map of a canvas yet. Switch with **Simple** in the title bar. It opens the same program; the positions blocks had in Free mode are waiting when you switch back.

- Every script is laid out top to bottom in one column, and every block has a numbered line.
- A block dropped anywhere joins the end of the script it landed nearest. Bring it close to another block first and the edge glows, to put it exactly where you want it.
- Dropping a block onto a line: near the top of the line it goes in **above**, near the bottom **below**, and in the middle it **replaces** that block. A bar (or an outline, for a replace) and a caption show which before you let go.
- Click anywhere on a line to pick its block, not just on the block.
- A condition only fits a hexagon hole. Dropped anywhere else, it goes back where it came from.
- Drag empty space, or scroll, to move up and down. There is no zoom, no box select and no group select.

## Undo and redo

The **Undo** and **Redo** buttons sit at the table's top-right corner.

| Action | Keys |
|---|---|
| Undo the last change | **Ctrl+Z** (**Cmd+Z** on macOS) |
| Redo | **Ctrl+Y**, or **Ctrl+Shift+Z** |

Undo remembers the last 50 changes to the object on the table. Picking another object starts its own history; clicking the object that is already on the table keeps it.

## Tips

The **?** button at the bottom of the workspace (*How the table works*) opens a short card with the tips for the mode you are in.

## Run your program

The run bar acts on the **object on the table**. The rest of the scene keeps running as it was. Only Speed applies to every object.

| Button | What it does |
|---|---|
| **▶ Go** | Starts the object's `when Go clicked` scripts. If none of its scripts is running, its `When Play Clicked` scripts start again too. |
| **Stop** | Ends the object's scripts where they are, and clears its speech bubble and sound. The object stays where it got to. |
| **Reset** | Stops the object and puts it back how it started: position, rotation, size, visibility and color. |
| **Pause / Resume** | Freezes the object's scripts. Shown after you press Go, while those scripts run. While paused, the bar turns amber and says *Paused*. |
| **Step ▶** | Runs one block of every script on the object, then freezes. A script that isn't running starts first, whatever its hat — so you can walk through a `When Key Pressed` script without pressing the key. A finished script starts again from its first block. |
| **◀ Step** | Goes back one step: the scripts and the object return to where they were before the last Step ▶. Works for up to 200 steps. Go, Resume, Stop, Reset or an edit starts the history over. |
| **Speed 1x–4x** | Speeds up moves, turns and waits that take time, for the whole scene. |

While a script runs, the block it is on has a thick yellow outline.

`When Play Clicked` scripts also start once by themselves when the scene starts. Editing a program while it runs stops the object's scripts and loads the new version at once; press Go to run it again.

## Advice

Blocky reads the program as you build it and explains, under the table, why a script won't do anything: a script with no hat, an empty hexagon hole, a number out of range, a `run` block with no matching `define`, and more.

- A red **!** badge on a block means the script won't run.
- An amber **?** badge is a hint.
- Up to three messages are listed, problems first. Click a message to select its block.

## Block limit

A level can limit how many blocks a program may use (see [Toolboxes and block limits](custom-blocks.md#toolboxes-and-block-limits)). The chip above Undo then shows *Blocks 3 / 5*. Hats don't count. At the limit the chip turns amber and the palette's blocks dim; a program over the limit shows red and can only lose blocks.

## Variables and the watcher

Variables are named in the block: `set [score] to (0)` makes a variable called *score*. There is no list of variables to manage. Names ignore case and spaces at either end, so *Score* and *score* are one variable.

The **scope** dropdown picks which copy you mean:

- **everyone** — one value shared by the whole scene.
- **this object** — a private copy for each object. Ten clones of one program each count their own.

While the workspace is open, a card in the table's top-left corner lists every shared variable and its value. It hides itself when nothing has set a variable yet.

## Lists

Lists work like variables: named in the block, with the same scope dropdown. Positions count from 1. A position the list doesn't have does nothing when you change the list, and reads as empty. Every block that changes a list makes it if it doesn't exist yet, so `delete all of list [items]` is a good first block. A list holds up to 10,000 items.

The watcher card shows shared lists too: `items  [a, b, c]`. A long list shows its first few items and its length.

## Clones

`Create Clone Of [me]` copies an object where it stands, with its scripts. The copy runs its `When I Start As A Clone` scripts. `Delete This Clone` removes a copy and does nothing on the original. A scene holds at most 100 clones; past that, `Create Clone Of` does nothing. Clones are not saved.

## Say and think bubbles

`Say` shows a bubble above the object: *say* draws a speech bubble, *think* a thought bubble. With *seconds* at 0 the bubble stays until something changes it; any other value shows it for that long and holds the script until it clears. An empty message clears the bubble. A long message wraps at its spaces.

## Sounds

`Play Note` plays a generated tone (60 is middle C, 69 is A 440) and holds the script while it sounds, so a column of notes is a tune. `Play Sound` plays a clip from a `Resources/Sounds` folder in your project and carries straight on, so sounds overlap; a name with no clip is silent. Each object gets one 2D `AudioSource` the first time it makes a sound, so a sound behind the camera is still heard.

## Themes

The theme button in the title bar steps through **Light**, **Dark** and **High contrast**. High contrast draws black on white with strong lines and lighter blocks, for low vision and bright projectors. The choice is remembered on that computer. A developer sets the starting theme on the workspace (see [Scripting](scripting.md#themes)).

## Languages

The title bar has one button per language, named the way each language names itself (*English*, *Türkçe*). With more than three languages the buttons show codes (*DE*) instead. Switching rewords the whole workspace in place; the object, its undo history and the view stay as they were. The choice is remembered. Programs don't depend on the language: a program built in Turkish runs in English. See [Languages](languages.md).

## Touch

The workspace works with a finger or a pen as well as a mouse.

- Tap an object in the game to pick it, and drag blocks as you would with a mouse.
- In Free mode, one finger on empty table pans and two fingers pinch to zoom.
- A finger on a palette block always drags it out. Scroll the palette with the tabs, or by dragging beside the blocks.
- `When Key Pressed` and `key pressed?` need a keyboard.

Touch has not been tried on every kind of device; mouse and keyboard remain the most tested way to use Blocky.
