---
tags: [block-catalog]
---

# Block Catalog

Source: [[Welcome|TDD §9]] for the v1 MVP set. Semantics there are normative for those blocks — they define yield behaviour. Everything after the MVP section is the Scratch/Delightex parity work, designed in [[09 Decisions/Decisions#ADR-020|ADR-020]].

**87 blocks live** as `BlockDefinition` `.asset` files under `Assets/Resources/Blocks/`, verified through `BlockRegistry.LoadFromResources()`: 38 steps, 16 conditions and 23 reporters bound by `OpTableBuilder`, 10 triggers correctly unbound (`TriggerBroker` dispatches those; `define` is started by `run` blocks only). `OpTableBuilderTests.EveryBlockInTheCatalog_IsBoundToAnOp` checks the shipped assets themselves, so an asset whose op is missing or of the wrong kind fails a test instead of the first Play.

**Every white input takes an expression.** Since [[09 Decisions/Decisions#ADR-021|ADR-021]] a reporter can be dropped on any `Number`, `Text` or `Bool` input of any block in this catalog — `move forward (pick random 1 to 10)`, `wait ((timer) / (2))`, `repeat ((length of (join …)))` — with no per-block work, because every op reads its inputs through `OpContext.GetNumber` / `GetText` / `GetBool`. A `Choice` dropdown is the one input that does not: it is a fixed list, not a value.

## Events
`event.when_play_clicked` · `event.when_go_clicked` · `event.when_looked_at` (`angle_threshold`, shared raycast) · `event.when_key_pressed` (`key`, rising edge) · `event.when_collided` (`tag_filter`, from the collision relay) · `event.when_clicked` (one shared raycast on the press edge; a hit on a child collider counts as a hit on the object) · `event.when_broadcast_received` (`message`) · `event.broadcast` (`message`, a statement — the only non-hat block in this category)

Broadcast names match case- and whitespace-insensitively, so "Jump" and "jump " are one message. A broadcast starts its listeners through the scheduler, so they run on the **next** tick — a script can broadcast the message it listens for without re-entering itself.

`event.level_complete` (a statement) · `event.when_level_complete` (hat) — for puzzle levels ([[09 Decisions/Decisions#ADR-033|ADR-033]]). `level complete` tells game code (`BlockyEvents.LevelCompleted`) and starts every `when level complete` script on the next tick, like a broadcast. A teacher can make a whole level from blocks: the goal's `when collided → level complete`, the player's `when level complete → say [You did it!]`. Game code can do the same with `BlockyEvents.CompleteLevel()` — see [[12 Game Code/Blocky from CSharp|Blocky from C#]].

## Motion
`motion.move_forward`, `motion.turn_direction`, `motion.rotate_axis`, `motion.change_position` (`x`,`y`,`z`,`duration`,`space`) — all relative, so `duration = 0` is instant and otherwise the change is applied as a per-tick increment · `motion.glide_to` (`x`,`y`,`z`,`duration`) and `motion.set_position`, `motion.set_rotation` — absolute; glide converges by remaining fraction and the two setters are instant-only.

## Looks
`looks.change_color` (`color`, `duration`) · `looks.set_visible` (`visible`) · `looks.set_scale` (`x`,`y`,`z`,`duration`) · `looks.say` (`message`, `seconds`, `style`)

`say` is all four of Scratch's bubble blocks in one ([[09 Decisions/Decisions#ADR-027|ADR-027]]): `seconds = 0` leaves the bubble up until something changes it, any other value holds the script and clears it after, an empty message clears it, and the style dropdown is the difference between saying and thinking. The bubble is a `TextMesh` and a quad above the object, turned to face the camera — **not** TextMeshPro, which has no font assets in this project and would render nothing at all.

## Control
`control.wait` (`seconds`, sets Sleeping+WakeAt) · `control.wait_until` (`condition`, re-checked once per frame) · `control.if` (1 branch) · `control.if_else` (2 branches) · `control.repeat` (`times`) · `control.repeat_forever` · `control.repeat_until` (`condition` checked before each iteration) · `control.stop` (`target`: this script / all scripts / other scripts on this object)

## Sensing
`condition.mouse_down`, `condition.mouse_up` (a press on the editor is not a press in the game) · `sensing.key_pressed` (`key`) — true *while* held, unlike the `when key pressed` hat, which fires once on the press · `sensing.touching` (`tag`, empty = anything) · `sensing.timer_above` (`seconds`) · `sensing.reset_timer` (a statement)

The timer runs on the **script clock** (`VmScheduler.Now`), so pausing the scene pauses the timer, Step back restores it, and 2x speed runs it at 2x. `touching?` is answered from the contact set `BlockCollisionRelay` maintains (Enter/Exit, collisions and triggers alike), never a fresh physics query — a condition inside a loop is asked every lap.

## Operators
**Questions** (hexagons): `operator.and`, `operator.or` (two condition slots each), `operator.not` (one) · `operator.lt`, `operator.eq`, `operator.gt` (`a`, `b`) · `operator.contains` (`text`, `thing`) · `condition.true`, `condition.false`

**Values** (round): `operator.add`, `operator.subtract`, `operator.multiply`, `operator.divide`, `operator.mod` · `operator.round` · `operator.math` (`op` is a dropdown: abs, floor, ceiling, sqrt, sin, cos, tan, ln, log, e^, 10^ — **read by index, so the order in the asset is part of the contract**) · `operator.random` (`from`, `to`) · `operator.join`, `operator.letter_of`, `operator.length`

Everything nests to any depth, in both directions: a condition can hold reporters (`(x) > (3)`), a reporter can hold conditions (`join <touching?> "!"`), and an empty input reads as false / 0 / "". `and`/`or` short-circuit.

**Comparisons follow Scratch's rule**, in one shared place (`ValueComparison.Compare`): two things that both look like numbers are compared as numbers, anything else as case-insensitive words. So `"10" > "9"` is true and `"APPLE" = "apple"` is true.

**Two deliberate departures from Scratch**: dividing by zero answers 0 rather than infinity (the result usually feeds a transform, and one infinity there puts an object somewhere no learner can find it), and `mod` takes the sign of its divisor (`-1 mod 4` = 3), which is what makes it usable for wrapping a value into a range — that part *is* Scratch's behaviour.

## Variables
`variables.set` (`name`, `value`, `scope`) · `variables.change` (`name`, `by`, `scope`) · `variables.get` (`name`, `scope`, round)

The variable is **named in the block** — no "create a variable" dialog, no per-program list ([[09 Decisions/Decisions#ADR-023|ADR-023]]). It exists because something set it; one that was never set reads as empty (0 as a number, "" as text), and `change by` counts from 0. Names are trimmed and compared case-insensitively, so `Score` and `score ` are one variable.

`scope` picks which copy: **everyone** (one store for the whole scene) or **this object** (a private copy per object, so ten copies of one program each count their own). Values keep their kind — `set score to 10` stores a number, so `>` still compares it as one.

Shared variables show up live in a **watcher card** in the table's top-left corner while the workspace is open ([[09 Decisions/Decisions#ADR-024|ADR-024]]).

## Lists
**Changing a list** (statements): `lists.add` (`item`, `list`, `scope`) — "add (thing) to [list]" · `lists.delete` (`index`, `list`, `scope`) · `lists.delete_all` (`list`, `scope`) · `lists.insert` (`item`, `index`, `list`, `scope`) · `lists.replace` (`index`, `list`, `item`, `scope`) — "replace item (1) of [list] with (thing)"

**Reading one:** `lists.item` (`index`, `list`, `scope`, round) · `lists.index_of` (`item`, `list`, `scope`, round) — where the item first appears, 0 when it isn't there · `lists.length` (`list`, `scope`, round) · `lists.contains` (`list`, `item`, `scope`, hexagon)

Lists follow the variable rules ([[09 Decisions/Decisions#ADR-028|ADR-028]]): **named in the block**, trimmed and case-insensitive, and the same `everyone` / `this object` dropdown. A list and a variable may share a name — they are different things. They have their own **Lists** tab, a deeper orange than Variables, as Scratch colors them.

**Positions count from 1** and are rounded down (`item 2.7` is item 2). A position the list doesn't have does nothing when changing the list and reads as empty — never an error. `insert` also accepts one past the end, which adds to the end, so inserting at 1 into an empty list works. The inputs reject 0 or less when typed, with advice. Every block that changes a list makes it if it doesn't exist yet, so `delete all of [items]` is the usual first block of a program that fills one; reading never makes a list. Items are matched the way `=` matches (`ValueComparison`), so `10` finds `"10.0"` and `apple` finds `Apple`. Items keep their kind: a number added to a list is still a number when it comes back out.

**Capped at 10,000 items** a list (`BlockyVariables.MaxListLength`); past that `add` and `insert` quietly do nothing, like the clone cap. Shared lists show on the watcher card as `name  [a, b, c]`; a longer one shows its first five items and its length, `[a, b, c, d, e, …]  (12)`.

## Talking about another object
`sensing.distance_to` (`object`, round) · `sensing.position_of` (`object`, `axis`, round) · `sensing.touching_object` (`object`, hexagon) · `motion.point_towards` (`object`) · `motion.go_to` (`object`, `duration`)

An `ObjectRef` input holds the other object's **name** — the same thing this project already uses to identify an object ([[09 Decisions/Decisions#ADR-025|ADR-025]]). `me` / `myself` / `self` / `this` and an empty input mean the object running the script; `camera` means the main camera. A name nothing answers to resolves to null and every block then does nothing — `distance to` reads 0, so a typo cannot throw an object out of the world.

Resolution goes through a registry every programmed object joins on start, so the usual case is a dictionary hit rather than a scene search; a name that matched nothing is remembered for the rest of the frame, because these blocks are read inside loops.

`point towards` turns **yaw only** (the direction is flattened onto the ground plane first, so nothing tips over), and `go to [object] over (duration)` re-reads the target every tick, so it follows something still moving.

## Values from the world
`sensing.timer` (round) — the timer as a number, for arithmetic, where `sensing.timer_above` only answers a question · `motion.position`, `motion.rotation` (`axis`: x, y, z) — this object's own transform, so "a little further each time" is `set position ((position x) + (1))`.

## Clones
`control.create_clone` (`object`, default `me`) · `event.when_i_start_as_a_clone` (hat) · `control.delete_this_clone` (cap)

A clone is a plain `Instantiate`, so it brings its colliders, renderer and runner with it ([[09 Decisions/Decisions#ADR-026|ADR-026]]). **Capped at 100**; past the cap `create clone` quietly does nothing, and `Stop` deletes every clone. `delete this clone` does nothing on the original. The clone hat is the only `AllowConcurrent` trigger in the catalog — ten clones starting are ten independent scripts.

## Sound
`sound.play_note` (`note`, `beats`) · `sound.play` (`name`) · `sound.stop_all` · `sound.set_volume` (`percent`)

The project has no audio files, so notes are **generated**: a sine wave at the right pitch and length, faded 10 ms at each end so it does not click, cached per note. MIDI numbering as in Scratch (60 = middle C, 69 = A 440). `play note` holds the script while it sounds, so a column of them is a tune; `play sound` loads from `Resources/Sounds/` and carries straight on so sounds overlap, and is silence rather than an error when nothing matches. Each object gets one `AudioSource` on first use, 2D on purpose so a beep is never inaudible for being behind the camera. `Stop` silences everything.

## My Blocks
`custom.define` (`name`, hat) · `custom.run` (`name`, `a`, `b`, `c`) · `custom.input_a`, `custom.input_b`, `custom.input_c` (round)

Scratch's custom blocks ([[09 Decisions/Decisions#ADR-029|ADR-029]]). Put blocks under **define [jump]**, then **run [jump]** from any script on the same object: the definition runs, and the script carries on after the `run`. A `run` hands its definition three values, **a**, **b** and **c**, worked out at the moment of the call; inside the definition **input a** (b, c) reads them — the innermost call's, so recursion works. Outside a definition an input block reads as empty.

Names match the way variable and message names do — trimmed, ignoring case — and belong to the object (a clone has its owner's). A name nothing defines does nothing; of two definitions with one name, the first runs. Once an object has a `define`, the My Blocks tab offers a ready-made **run [jump]** for it under the plain `run`, so running one is a drag, not typing its name again.

A definition never starts on its own — not on Play, Go or Step ▶ — only from a `run`. `stop [this script]` inside one ends the whole script that ran it, as in Scratch. A block that runs itself **waits one frame first** (also Scratch's rule): a recursive drawing unfolds on screen, and a recursion that never stops slows down instead of freezing the game. Loops and custom blocks nest up to **256 deep** together; past that a `run` does nothing, and the stack unwinds.

**The compromise:** in Scratch a custom block grows the inputs its definition declares, with the names it gives them. Here every `run` has the same three inputs, a, b and c, because a block's inputs come from its asset — a call whose shape depended on another script would need the palette, the compiler and the save files to understand shapes that change. It can grow into the real thing later without a data change.

The advice spots the ways this goes wrong silently: a `define` without a name, two with the same name, a `run` whose name nothing defines (or that is empty), and an `input` block outside any definition.

## Not built yet
Every part of Scratch's language now has a counterpart — events, control, operators, variables, lists and custom blocks. Single blocks that are not: `ask and wait` / `answer` (needs a text box over the game), and the 2D-sprite looks (costumes, layers) and the pen, which have no obvious meaning in a 3D scene. [[11 Release/Release Readiness|Release Readiness]] lists what the product still needs.

## How a block gets added
The asset plus one `[BlockExecutor]` class, and nothing else (TDD §7). The palette, the tab rail, the compiler, serialization and the op tables all discover it: the palette groups by `BlockCategory` in enum order and skips empty categories, and `OpTableBuilder` binds by `executorKey` through one reflection scan. New assets are generated as YAML rather than hand-placed one by one — Unity imports them and writes the `.meta` files itself.

**Op implementation pattern worth remembering:** any duration-based op converges using only the single `float` in `Thread.Scratch` — no separate "start value" memory exists. Two techniques cover every case in the catalog:
- **Relative delta** (`move_forward`, `turn_direction`, `rotate_axis`, `change_position`): apply `amount * step / duration` as an *increment* each tick — no start value needed because the change is inherently relative.
- **Remaining-fraction convergence** (`change_color`, `set_scale`, `glide_to`, which all target an *absolute* value over time): each tick, `Lerp(current, target, step / remaining)` — this converges to the target as `remaining` hits 0, without ever remembering where it started. `glide_to` also snaps to the target on its last tick, because float error accumulates over a long glide. See [[04 Runtime/Runtime VM|Runtime VM]] and `ChangeColorOp`'s doc comment.

`set_position`/`set_rotation` have no duration param at all (instant-only, per the TDD catalog table) — no convergence logic needed there.
