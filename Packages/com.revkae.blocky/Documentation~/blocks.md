# Block reference

Blocky ships **88 blocks** in 10 categories. This page lists every one, in palette order, with the name a player sees in English, its inputs and their starting values, and what it does.

| Category | Blocks |
|---|---|
| [Event](#event) | 11 |
| [Motion](#motion) | 11 |
| [Looks](#looks) | 4 |
| [Control](#control) | 11 |
| [Sensing](#sensing) | 10 |
| [Operators](#operators) | 20 |
| [Sound](#sound) | 4 |
| [Variables](#variables) | 3 |
| [Lists](#lists) | 9 |
| [My Blocks](#my-blocks) | 5 |

The **Shape** column says where a block fits (see [Block shapes](workspace.md#block-shapes)): *hat*, *stack*, *C-block*, *cap*, *hexagon* (a condition) or *oval* (a value). Inputs are written as *label (starting value)*; a dropdown lists its choices.

## Rules every block follows

- **Every white input takes a value block.** A number, text or checkbox input can hold an oval or a hexagon instead of a typed value: `Wait ((timer) / (2))`. A dropdown can't.
- **An empty input** reads as 0, as empty text, or as false.
- **Text and numbers convert freely.** Text that looks like a number works as one; any other text reads as 0 where a number is needed.
- **Comparisons** (`<`, `=`, `>`, and finding an item in a list) compare two things that both look like numbers as numbers, and anything else as words, ignoring case. `"10" > "9"` is true, and `"APPLE" = "apple"` is true.
- **Object inputs** hold another object's name. `me` (or `myself`, `self`, `this`, or nothing) means the object running the script; `camera` means the main camera. A name no object has makes the block do nothing; `distance to` reads 0.
- **Durations** are in seconds. A duration of 0 is instant. Any other duration spreads the change over that time, and the script waits for it to finish.
- **3D or 2D.** In 3D, *forward* is the object's blue (Z) axis and turning is around up. On a 2D stage — an object with a `SpriteRenderer`, `Collider2D` or `Rigidbody2D`, or one whose runner's *Motion* is set to *Two D* — forward is the red (X) axis, the way a sprite faces, and turning is around Z. `Turn [left]` turns left in both.

## Event

Hats start scripts. A script starts again from the top when its event happens again while it is still running — except `When I Start As A Clone`, where each clone runs its own copy.

| Block | Shape | Inputs | What it does |
|---|---|---|---|
| **When Play Clicked** | hat | — | Runs once when the scene starts. ▶ Go runs it again when nothing on the object is running. |
| **when Go clicked** | hat | — | Runs each time ▶ Go is pressed. |
| **When Clicked** | hat | — | Runs when the player clicks or taps this object. The object needs a collider (3D or 2D) or a sprite; a click on a child's collider counts. A press on the workspace doesn't. |
| **When Key Pressed** | hat | key (Space): Space, Enter, UpArrow, DownArrow, LeftArrow, RightArrow, W, A, S, D | Runs once each time the key goes down. Holding it down doesn't repeat. |
| **When Collided** | hat | tag filter (empty) | Runs when this object bumps into another. With a tag, only objects with that tag count. Needs a collider, 3D or 2D; Unity only reports contacts when one of the two objects has a Rigidbody. |
| **When Looked At** | hat | angle threshold (15), 0–180 | Runs when the main camera turns to face this object, within that many degrees. It runs once each time the object comes into view, not every frame. |
| **When I Receive** | hat | message (message1) | Runs when a `Broadcast` sends this message. Names ignore case and spaces at either end. |
| **Broadcast** | stack | message (message1) | Sends a message to every script in the scene and carries straight on. The `When I Receive` scripts start on the next frame. |
| **When I Start As A Clone** | hat | — | Runs in each new clone made by `Create Clone Of`. |
| **level complete** | stack | — | Tells the game the level is solved and carries on. Every `when level complete` script starts on the next frame, and game code hears `BlockyEvents.LevelCompleted`. |
| **when level complete** | hat | — | Runs when a `level complete` block runs anywhere, or when game code completes the level. |

Example — a level built from blocks alone. On the goal:

```text
When Collided  tag filter [Player]
level complete
```

On the player:

```text
when level complete
Say  message [You did it!]  seconds (2)  style [say]
```

## Motion

| Block | Shape | Inputs | What it does |
|---|---|---|---|
| **Move Forward** | stack | distance (1), −1000–1000; duration (0) | Moves the object forward by that distance. A negative distance moves it back. |
| **Turn** | stack | direction (left): left, right; degrees (90), −360–360; duration (0) | Turns the object left or right. |
| **Rotate** | stack | axis (y): x, y, z; degrees (90), −360–360; duration (0) | Rotates the object around one of its own axes. |
| **Change Position** | stack | x, y, z (0), −1000–1000; duration (0); space (world): world, local | Moves the object by that much along each axis — the world's axes, or the object's own. |
| **Set Position** | stack | x, y, z (0), −1000–1000; space (world): world, local | Puts the object at that position, at once. *local* is relative to its parent. |
| **Glide To** | stack | x, y, z (0), −1000–1000; duration (1) | Glides the object to that world position over the duration. |
| **Set Rotation** | stack | x, y, z (0), −360–360; space (world): world, local | Sets the object's rotation, in degrees, at once. |
| **Go To** | stack | object (camera); duration (0) | Moves the object to another object's position. With a duration it follows the other object if it is still moving. |
| **Point Towards** | stack | object (camera) | Turns the object to face another one. In 3D it only turns around up, so nothing tips over. |
| **position** | oval | axis (x): x, y, z | This object's world x, y or z. |
| **rotation** | oval | axis (x): x, y, z | This object's rotation around x, y or z, in degrees from 0 to 360. |

Example — a little further each time:

```text
when Go clicked
Repeat (10)
    Set Position  x ((position [x]) + (1))  y (position [y])  z (position [z])
    Wait (0.2)
```

## Looks

| Block | Shape | Inputs | What it does |
|---|---|---|---|
| **Change Color** | stack | color (#FFFFFF); duration (0) | Changes the object's color: every renderer under it, every material slot. The color is a hex code such as `#FF8800`, or a color name such as `red`. A sprite is tinted. |
| **Say** | stack | message (Hello!); seconds (2); style (say): say, think | Shows a speech or thought bubble above the object. With 0 seconds it stays until something changes it; otherwise the script waits, and the bubble clears after that time. An empty message clears it. |
| **Set Scale** | stack | x, y, z (1), 0–100; duration (0) | Sets the object's scale (its local scale) along each axis. |
| **Set Visible** | stack | visible (unchecked) | Shows (checked) or hides (unchecked) every renderer under the object. Its speech bubble stays visible. |

Reset and ◀ Step put color, size and visibility back.

## Control

| Block | Shape | Inputs | What it does |
|---|---|---|---|
| **Wait** | stack | seconds (1), 0–1000 | Holds the script for that long. |
| **Wait Until** | stack | condition (hexagon) | Holds the script until the condition is true. It checks once a frame. |
| **If** | C-block | condition (hexagon) | Runs the blocks inside when the condition is true. |
| **If Else** | C-block, two mouths | condition (hexagon) | Runs the first mouth when the condition is true, the second (*else*) when it isn't. |
| **Repeat** | C-block | times (10), 0–100000 | Runs the blocks inside that many times. |
| **Repeat Forever** | cap | — | Runs the blocks inside over and over until the script is stopped. |
| **Repeat Until** | C-block | condition (hexagon) | Runs the blocks inside until the condition is true. It checks before each lap. |
| **Stop** | stack | target (this script): this script, all scripts, other scripts on this object | Ends this script, every script in the scene, or the object's other scripts. |
| **As** | C-block | object (me) | The blocks inside act on another object — move it, turn it, recolor it, make it speak. After the mouth, the script's own object is back. `As` blocks nest. |
| **Create Clone Of** | stack | object (me) | Copies the object, scripts and all, where it stands. A scene holds at most 100 clones. |
| **Delete This Clone** | cap | — | Removes this clone. On the original it does nothing. |

An empty hexagon hole counts as false. A name in `As` that no object has skips the blocks inside, with one warning in the console per run of the script.

## Sensing

| Block | Shape | Inputs | What it does |
|---|---|---|---|
| **mouse down?** | hexagon | — | True while the mouse button, a pen or a finger is pressed on the game. A press on the workspace, or on your game's own interface, doesn't count. |
| **mouse up?** | hexagon | — | True while it isn't. |
| **key pressed?** | hexagon | key (Space): Space, Enter, UpArrow, DownArrow, LeftArrow, RightArrow, W, A, S, D | True while the key is held down. |
| **touching?** | hexagon | tag (empty) | True while this object touches something with that tag. Empty means anything. |
| **touching object?** | hexagon | object (camera) | True while this object touches that one object. |
| **timer >** | hexagon | seconds (1), 0–1000 | True once the timer has passed that many seconds. |
| **timer** | oval | — | The timer, in seconds. |
| **Reset Timer** | stack | — | Starts the timer again from 0. |
| **distance to** | oval | object (camera) | How far this object is from another, in world units. |
| **position of** | oval | object (camera); axis (x): x, y, z | Another object's world x, y or z. |

The timer runs on the scripts' clock: pausing pauses it, ◀ Step puts it back, and 2x speed runs it twice as fast. `touching?` and `touching object?` need a collider, like `When Collided`.

Example — hold to move:

```text
when Go clicked
Repeat Forever
    If <key pressed? [UpArrow]>
        Move Forward  distance (0.1)  duration (0)
```

## Operators

**Conditions** (hexagons):

| Block | Shape | Inputs | What it does |
|---|---|---|---|
| **and** | hexagon | a, b (hexagon holes) | True when both are true. |
| **or** | hexagon | a, b (hexagon holes) | True when either is true. |
| **not** | hexagon | a (hexagon hole) | True when a is false. |
| **<** | hexagon | a (empty), b (50) | True when a is less than b. |
| **=** | hexagon | a (empty), b (50) | True when a equals b. |
| **>** | hexagon | a (empty), b (50) | True when a is more than b. |
| **contains?** | hexagon | text (apple), thing (a) | True when the text contains the thing, ignoring case. |
| **true** | hexagon | — | Always true. |
| **false** | hexagon | — | Always false. |

**Values** (ovals):

| Block | Shape | Inputs | What it does |
|---|---|---|---|
| **+** | oval | a (1), b (1) | a plus b. |
| **-** | oval | a (1), b (1) | a minus b. |
| **x** | oval | a (2), b (2) | a times b. |
| **/** | oval | a (10), b (2) | a divided by b. Dividing by 0 gives 0. |
| **mod** | oval | a (7), b (4) | What is left after dividing a by b. It takes the sign of b, so `-1 mod 4` is 3. |
| **round** | oval | n (1.5) | n rounded to the nearest whole number. A value exactly halfway goes to the even one: 2.5 rounds to 2. |
| **of** | oval | op (abs): abs, floor, ceiling, sqrt, sin, cos, tan, ln, log, e^, 10^; n (9) | That function of n. Angles for sin, cos and tan are in degrees. |
| **pick random** | oval | from (1), to (10) | A random number between the two, both ends included. Two whole numbers give a whole number; a decimal at either end gives a decimal. |
| **join** | oval | a (hello ), b (world) | The two joined into one piece of text. |
| **letter of** | oval | index (1); text (world) | The letter at that position, counting from 1. A position the text doesn't have gives empty text. |
| **length of** | oval | text (world) | How many letters the text has. |

Number inputs on these blocks take −1,000,000 to 1,000,000.

## Sound

| Block | Shape | Inputs | What it does |
|---|---|---|---|
| **Play Note** | stack | note (60), 0–127; beats (0.5), 0–10 | Plays a generated tone and waits while it sounds. 60 is middle C, 69 is A 440. |
| **Play Sound** | stack | name (pop) | Plays the clip with that name from a `Resources/Sounds` folder and carries straight on. A name with no clip is silent. |
| **Stop All Sounds** | stack | — | Silences every sound. |
| **Set Volume** | stack | percent (100), 0–100 | Sets the volume of this object's sounds. |

## Variables

| Block | Shape | Inputs | What it does |
|---|---|---|---|
| **set** | stack | name (score); value (0); scope (everyone): everyone, this object | Sets the variable to the value. A number stays a number. |
| **change** | stack | name (score); by (1); scope (everyone) | Adds to the variable. One that was never set counts from 0. |
| **value of** | oval | name (score); scope (everyone) | The variable's value. One that was never set reads as empty (0 as a number). |

*everyone* is one copy for the whole scene; *this object* is a copy per object. Names ignore case and spaces at either end. Shared variables show on the watcher card while the workspace is open.

## Lists

| Block | Shape | Inputs | What it does |
|---|---|---|---|
| **add to list** | stack | item (thing); list (items); scope (everyone) | Adds the item to the end of the list. |
| **delete from list** | stack | index (1); list (items); scope (everyone) | Removes the item at that position. |
| **delete all of list** | stack | list (items); scope (everyone) | Empties the list. |
| **insert into list** | stack | item (thing); index (1); list (items); scope (everyone) | Puts the item at that position. One past the end adds it to the end. |
| **replace in list** | stack | index (1); list (items); item (thing); scope (everyone) | Replaces the item at that position. |
| **item of list** | oval | index (1); list (items); scope (everyone) | The item at that position. |
| **position in list** | oval | item (thing); list (items); scope (everyone) | Where the item first appears, or 0 when it isn't there. |
| **length of list** | oval | list (items); scope (everyone) | How many items the list has. |
| **list contains?** | hexagon | list (items); item (thing); scope (everyone) | True when the list has the item. |

Positions count from 1 and are rounded down. A position the list doesn't have does nothing when changing it and reads as empty. Blocks that change a list make it if it doesn't exist; blocks that read one never do. A list holds up to 10,000 items. A list and a variable can share a name.

## My Blocks

| Block | Shape | Inputs | What it does |
|---|---|---|---|
| **define** | hat | name (my block) | Starts a block of your own. The blocks under it run when a `run` block with the same name runs. It never starts by itself — not on Play, Go or Step. |
| **run** | stack | name (my block); a, b, c (empty) | Runs this object's `define` with that name, handing it three values, then carries on. |
| **input a** | oval | — | Inside a `define` script: the value *a* the `run` block handed over. |
| **input b** | oval | — | The same for *b*. |
| **input c** | oval | — | The same for *c*. |

See [Custom blocks › My Blocks](custom-blocks.md#my-blocks-for-players) for how to use them, recursion included.
