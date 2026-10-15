# Scripting

How your game code talks to Blocky: hearing what the learner's scripts do, sending them messages, driving the run bar, handing objects their programs, and hosting the workspace inside a game that has a flow of its own.

## Where things are

| Namespace (and assembly) | What's in it |
|---|---|
| `Blocky.Runtime` | `BlockyEvents`, `BlockyRuntime`, `ObjectProgramRunner`, `BlockyRuntimeTicker`, `BlockyInput`, `BlockySpeechBubble`, `BlockValue`, and the interfaces for your own blocks |
| `Blocky.Runtime.Persistence` (in `Blocky.Runtime`) | `IProgramStorage`, `BlockyStorage`, `FileProgramStorage`, `RuntimeProgramStorage`, `BlockProgramAsset` |
| `Blocky.Runtime.Triggers` (in `Blocky.Runtime`) | `TriggerBroker` |
| `Blocky.Game` | `BlockyInGamePanel` (the workspace) and `BlockyTheme` |
| `Blocky.Editor` | `ParamFieldFactory`. Despite its name this is a runtime assembly: the workspace's block views, which also run in builds. |
| `Blocky.Data` | `ObjectProgram`, `ProgramQuery`, and `ProgramSerializer` (in `Blocky.Data.Serialization`) |
| `Blocky.Compiler` | `BlockDefinition`, `BlockyToolbox` |
| `Blocky.Localization` | `BlockyLanguages`, `BlockyText` — see [Languages](languages.md) |

Scripts in `Assets` without an assembly definition see all of these. An assembly definition of your own needs references to the ones it uses.

## Statics are reset every Play session

Blocky clears its static state at the start of every Play session, so it behaves the same whether or not your project reloads the domain when entering Play mode. That includes:

- every handler on `BlockyEvents`,
- `BlockyStorage.Current`,
- the checks added with `BlockyInput.AddPointerOverUiCheck`, and `BlockyInput.IsPointerOverUi`,
- `ParamFieldFactory.ObjectChoices` and `ParamFieldFactory.IsHidden`,
- `BlockySpeechBubble.ReadableDistance`,
- everything behind `BlockyRuntime`: the scheduler, `Playback`, `Variables`, `World` and the rest.

So set these as your game starts, every session. Subscribe to events in `OnEnable` or `Start` and unsubscribe in `OnDisable`. For things that must be in place before any object wakes, a static method marked `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` runs after Blocky's reset and before the first scene loads.

Properties on components, such as `BlockyInGamePanel.PickResolver`, belong to the component and last as long as it does.

## Events — `BlockyEvents`

| Event | When | Typical use |
|---|---|---|
| `ScriptStarted(BlockyScript)` | A script began running (the frame after its event happened). | "Running…" indicators, counting runs. |
| `ScriptFinished(BlockyScript, ScriptEndReason)` | A script ended by itself: `Completed` (ran out of blocks), `StoppedByBlock` (a `Stop` block), `Failed` (a block failed; the console names it). | Check a result when the learner's script is done. |
| `AllScriptsFinished()` | A script ended by itself and nothing is left running or waiting to start. | "Did the robot reach the goal?" |
| `LevelCompleted(GameObject)` | A `level complete` block ran (its object), or `CompleteLevel()` was called. | Next level, stars. |
| `MessageSent(string)` | Any broadcast, from a `Broadcast` block or from `BlockyEvents.Broadcast`. | Let blocks drive game code: `broadcast [open door]`. |
| `Stopped()` | Every script was stopped with `Playback.Stop()` or `Playback.ResetWorld()` for the whole scene. | Reset game state that isn't a programmed object. |
| `WorldReset()` | `Playback.ResetWorld()` put every programmed object back, after `Stopped`. | Put collectibles, doors and scores back. |
| `ProgramEdited(GameObject)` | The learner changed an object's program in the workspace. | Clear a "solved" badge, count edits. |

`BlockyScript` carries the `Object`, the script's `Index` among the object's scripts, and its `Trigger` — the block type of its hat, such as `event.when_go_clicked`.

A script ended from outside is not reported as finished: by Stop or Reset, by an edit to its program, by its object being disabled or destroyed, or by its own event restarting it.

The workspace's run bar acts on the object on its table, not the whole scene, so its Stop and Reset buttons don't raise `Stopped` or `WorldReset`. Call `Playback.Stop()` or `Playback.ResetWorld()` from your own button for a scene-wide stop.

**Rules for handlers:**

- Everything is raised on the main thread, while Blocky runs its scripts or from the call that caused it.
- A handler that throws is logged, and never stops the scripts or the other handlers.
- `LevelCompleted` fires every time: a goal touched twice completes twice. Ignore repeats if they matter.

## Commands

| Call | Does |
|---|---|
| `BlockyEvents.Broadcast("open")` | What a `Broadcast` block does: every `When I Receive [open]` script starts. Names ignore case and surrounding spaces. |
| `BlockyEvents.CompleteLevel()` | What the `level complete` block does: `LevelCompleted`, and every `when level complete` script. |
| `BlockyRuntime.Playback.Go()`, `Stop()`, `ResetWorld()` | The run bar's Go, Stop and Reset, for the whole scene. |
| `BlockyRuntime.Playback.Pause()`, `Resume()`, `StepForward()`, `StepBack()` | Pause, Resume and stepping, for the whole scene. |
| `BlockyRuntime.Playback.SetSpeed(2)` | Speed, from 1 to 4. |
| `BlockyRuntime.Variables.Get("score")`, `Set(...)`, `Change(...)` | Read or change a variable the scripts see. Pass `owner:` for an object's own copy. |
| `BlockyRuntime.Variables.Item("items", 1)`, `Length("items")`, `Add(...)`, `DeleteAll(...)` | The same for lists. |

The `Playback` commands take an optional object: `Playback.Go(robot)` acts on that object alone, as the workspace's run bar does for the object on its table. `Playback.IsRunning`, `IsPaused`, `Speed` and `CanStepBack` tell you the current state.

Variables hold `BlockValue`s:

```csharp
float score = BlockyRuntime.Variables.Get("score").AsNumber();
BlockyRuntime.Variables.Set("score", BlockValue.Number(0));
BlockyRuntime.Variables.Set("name", BlockValue.Text("Robo"), owner: robot); // the robot's own copy
```

## Example: a puzzle level

```csharp
using Blocky.Runtime;
using UnityEngine;

public sealed class PuzzleLevel : MonoBehaviour
{
    [SerializeField] private Transform robot;
    [SerializeField] private Transform goal;
    [SerializeField] private GameObject winBanner;

    private void OnEnable()
    {
        BlockyEvents.AllScriptsFinished += CheckSolution;
        BlockyEvents.LevelCompleted += Win;
        BlockyEvents.WorldReset += HideBanner;
    }

    private void OnDisable()
    {
        BlockyEvents.AllScriptsFinished -= CheckSolution;
        BlockyEvents.LevelCompleted -= Win;
        BlockyEvents.WorldReset -= HideBanner;
    }

    // The learner's program ran to its end: did it get the robot there?
    private void CheckSolution()
    {
        if (Vector3.Distance(robot.position, goal.position) < 0.5f) BlockyEvents.CompleteLevel();
    }

    private void Win(GameObject by) => winBanner.SetActive(true);

    private void HideBanner() => winBanner.SetActive(false);
}
```

The same level needs no C# at all: the goal gets `When Collided` → `level complete`, the player `when level complete` → `Say [You did it!]`.

## Programs on objects — `ObjectProgramRunner`

An `ObjectProgramRunner` makes one object run its program. **Blocky › Program Editor** adds one with a program asset; the workspace adds one when the learner picks an object that has none.

| Member | Does |
|---|---|
| `SetProgram(ObjectProgram program)` | Replaces the program the object runs. If the runner is running, its scripts stop and the new program is compiled at once; otherwise it runs when the runner starts. The program is copied. `null` is an empty program. |
| `SetProgramAsset(BlockProgramAsset asset)` | Gives the runner a program asset. Call it before the object is enabled. |
| `LoadProgram()` | The program the object runs: its saved program when it has one, else the one it was given in the scene. |
| `SavedProgram` | What the runner found in storage: `None`, `Loaded` or `Unreadable`. |
| `Motion` | `Auto`, `ThreeD` or `TwoD`: which way motion blocks move it. Auto picks 2D for an object with a `SpriteRenderer`, `Collider2D` or `Rigidbody2D`. |

A program handed over with `SetProgram` or `SetProgramAsset` wins over a saved one. Order of precedence: a program from code, then the save, then the program asset in the scene. See [Saving and loading](saving.md).

## The ticker — `BlockyRuntimeTicker`

One `BlockyRuntimeTicker` drives everything each frame: it ticks the scripts, reads the keyboard, checks what the camera looks at, and raycasts clicks. A scene without one gets one on a new *Blocky Runtime* object, with a note in the console. Put one in the scene yourself to choose where it lives and to set its switches:

| Switch | On (the default) | Off |
|---|---|---|
| `FirePlayClickedOnStart` | `When Play Clicked` scripts start when the scene starts. | You start them: `Playback.Go()`, or per object `Triggers.FirePlayClickedFor(go)` and `Triggers.FireGoClicked(go)`. |
| `AttachSavedPrograms` | Objects with a saved program but no runner get one at scene start. | You hand out programs yourself. |
| `PollKeyboard` | The keyboard is read for `When Key Pressed`. | `When Key Pressed` never fires. |
| `PollLookedAt` | `When Looked At` is decided by the angle to the main camera. | You raise it: `Triggers.RaiseLookedAt(go)`. |
| `PollClicked` | A press raycasts for `When Clicked`. | You raise it: `Triggers.RaiseClicked(go)`. |

They are also fields in the Inspector. `FirePlayClickedOnStart` and `AttachSavedPrograms` are read once, in the ticker's `Start`, so a ticker added from code can be set up first.

## Hosting Blocky in your own game flow

By default the workspace opens with Tab, programs whatever you click, saves to files, and starts every script when the scene starts. A game that has its own selection, its own Play button or its own save data can take over each of those. Every default is what an unhosted scene does, so you only change what you need.

```csharp
panel.PickObjectsOnClick = false;                  // your selection decides what is edited
panel.ShowOpener = false;                          // your button opens it (Toggle Key: None in the Inspector)
panel.Open(selectedObject);
panel.Closed += SaveMyWorld;

BlockyStorage.Current = new MyProgramStorage();    // IProgramStorage: programs kept in your own save data
runner.SetProgram(program);                        // or hand each runner its program yourself
BlockyRuntime.World.Recapture(movedObject);        // Reset returns it to where the player last put it
```

### The workspace from code

`BlockyInGamePanel` is the component on the *Blocky Workspace* object.

| Member | Does |
|---|---|
| `Open(GameObject target = null)` | Opens the workspace, on `target`'s program when one is given. Works before the workspace's own `Awake`. |
| `Close()` | Closes it, as the title bar's button and the key do. |
| `SetTarget(GameObject target)` | Puts that object's program on the table, adding an `ObjectProgramRunner` if it has none. `null` clears the table. Called with the object already there, it reads the program again from its runner — do that after replacing the program from your own storage. |
| `IsOpen`, `Target` | The state. `Target` is null before an object is chosen. |
| `Opened`, `Closed` | Raised on every change between closed and open, whatever caused it. |
| `TargetChanged(before, now)` | Another object is on the table — your call or the learner's click. Not raised when `SetTarget` is given the object already there. When the edited object is destroyed: `(theDestroyedObject, null)`, on the next frame. |
| `IsPointerOverWorkspace(Vector2 screenPosition)` | True over the open workspace or one of its open dropdown menus (Input System pixels, origin bottom-left). Closed: only over the corner button. Use it to keep your camera and shortcuts out of the workspace. |
| `PickObjectsOnClick` | On (default): clicking an object in the game opens its program. Off: only your calls choose the object. |
| `PickResolver` | With picking on: maps what a press hit to the object to program — say, the root of a model whose colliders are on its children — or returns null to ignore the press. |
| `ToggleKey` | The key that opens and closes it (Tab). `Key.None`: no key, and the key caps go from the title bar and the corner button. |
| `ShowOpener` | Off: while closed, the workspace draws nothing and takes no input. |
| `Toolbox`, `BlocksUsed` | The level's toolbox, and the block count. See [Toolboxes](custom-blocks.md#toolboxes-and-block-limits). |
| `Theme` | `BlockyTheme.Light`, `Dark` or `HighContrast`. |

**Order of events.** Events are raised after the state has changed, and a handler may call `SetTarget` or `Close`. `Open(target)` on a closed workspace changes both things, then raises `Opened`, then `TargetChanged`. Opening an open workspace on the object it already shows raises nothing and keeps the table and its undo history.

**With no object on the table**, the title bar says *Click an object in the game to code it*. If your game turns picking off, that sentence is wrong for it: change the `status.pick_object` string in an overlay file of your own (see [Languages](languages.md#words-for-your-own-blocks)).

The workspace's Inspector also has *Toggle Key*, *Pick Objects On Click*, *Show Opener*, *Panel Width*, *Palette Width*, *Workspace Mode* (Free or Simple at start), *Toolbox* and *Theme*.

### Your own storage — `IProgramStorage`

The workspace saves every edit through `BlockyStorage.Current`, and a runner loads through it the first time it needs its program. The default is `FileProgramStorage.Instance` (see [Saving and loading](saving.md)). A game that keeps programs in its own save data, or on a server, implements `IProgramStorage`:

```csharp
public interface IProgramStorage
{
    bool HasSavesFor(Scene scene);                                             // cheap pre-check: false, and no object is asked about
    bool Exists(GameObject owner);                                             // asked at scene start, to give saved objects a runner
    SavedProgramStatus TryLoad(GameObject owner, out ObjectProgram program);   // None, Loaded or Unreadable
    bool TrySave(GameObject owner, ObjectProgram program, out string problem); // never throws
}
```

An example that keeps each program as JSON in a dictionary your save system writes out:

```csharp
using System;
using System.Collections.Generic;
using Blocky.Data;
using Blocky.Data.Serialization;
using Blocky.Runtime.Persistence;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SaveGameProgramStorage : IProgramStorage
{
    private readonly Dictionary<string, string> _programs;               // object id -> program JSON, in your save data
    private readonly Dictionary<string, string> _unreadable = new();     // kept aside, never overwritten

    public SaveGameProgramStorage(Dictionary<string, string> programs) => _programs = programs;

    public bool HasSavesFor(Scene scene) => _programs.Count > 0;

    public bool Exists(GameObject owner) => _programs.ContainsKey(IdOf(owner));

    public SavedProgramStatus TryLoad(GameObject owner, out ObjectProgram program)
    {
        program = null;
        var id = IdOf(owner);
        if (!_programs.TryGetValue(id, out var json)) return SavedProgramStatus.None;

        try
        {
            program = ProgramSerializer.Deserialize(json);
            return SavedProgramStatus.Loaded;
        }
        catch (Exception)
        {
            _unreadable[id] = json;
            _programs.Remove(id);
            return SavedProgramStatus.Unreadable;
        }
    }

    public bool TrySave(GameObject owner, ObjectProgram program, out string problem)
    {
        _programs[IdOf(owner)] = ProgramSerializer.Serialize(program);
        problem = null;
        return true;
    }

    private static string IdOf(GameObject go) => go.name; // use your game's own stable id for the object
}
```

- **Set it before your programmed objects wake.** A runner that has already looked has found the files. Or hand programs out yourself with `SetProgram`, and turn the ticker's *Attach Saved Programs* off.
- `Unreadable` shows the *couldn't be opened* notice under the table, and the object runs its scene program.
- `TrySave` must never throw: a failed save must not undo the edit that caused it. Return false and say why in `problem`; the workspace shows *Couldn't save the program*.
- Setting `BlockyStorage.Current = null` goes back to the files.

### Your own Play button and input

- **Start scripts yourself.** Turn the ticker's *Fire Play Clicked On Start* off, then start every object with `BlockyRuntime.Playback.Go()`, or chosen objects with `BlockyRuntime.Triggers.FirePlayClickedFor(go)` and `BlockyRuntime.Triggers.FireGoClicked(go)`. `BlockyRuntime.Playback.Stop()` stops everything.
- **Your own gaze.** `BlockyRuntime.Triggers.RaiseLookedAt(go)` starts exactly that object's `When Looked At` scripts; raised again while one runs, it restarts it. Turn the ticker's *Poll Looked At* off, or Blocky's own angle check fires too. The block's *angle threshold* then does nothing, so hide it (below).
- **Your own clicks.** Turn *Poll Clicked* off and call `BlockyRuntime.Triggers.RaiseClicked(go)`.
- **Presses on your own interface.** While any check added with `BlockyInput.AddPointerOverUiCheck` says true, `mouse down?` is false and `When Clicked` doesn't fire. The workspace adds its own check. `BlockyInput.RemovePointerOverUiCheck` takes one back.

```csharp
BlockyInput.AddPointerOverUiCheck(() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
```

### Reset after the learner moved things — `WorldSnapshot`

Reset puts each programmed object back to its start. When your game lets the player move or recolor objects between runs, tell Blocky where the new start is:

| Call | Does |
|---|---|
| `BlockyRuntime.World.Recapture(go)` | Makes `go`'s current state its start. Starts tracking it if it wasn't. Call it at the end of a gizmo drag or a property edit. |
| `BlockyRuntime.World.RecaptureAll()` | The same for every tracked object. |
| `BlockyRuntime.World.Forget(go)` | Stops tracking it: deleted, or no longer programmable. |

Reset never recolors a material an object had when it was first seen.

### Object inputs as a dropdown — `ParamFieldFactory.ObjectChoices`

Object inputs (`Go To`, `distance to`, `As`…) are text boxes where the learner types a name. Give them your objects' names and they become a dropdown:

```csharp
ParamFieldFactory.ObjectChoices = () => myObjectNames; // IReadOnlyList<string>; null: a text box again
```

It is asked each time a field is drawn. *me* is always first. The program still stores the name, and a stored name the list no longer has is still shown.

### Hiding an input — `ParamFieldFactory.IsHidden`

```csharp
ParamFieldFactory.IsHidden = (blockType, paramKey) => blockType == "event.when_looked_at" && paramKey == "angle_threshold";
```

The input isn't drawn — on the table or in the palette — and its value stays in the program.

### Speech bubbles seen from far away — `BlockySpeechBubble`

```csharp
BlockySpeechBubble.ReadableDistance = 10f;
```

A bubble is one size in the world, whatever its object's scale. Past `ReadableDistance` from the main camera it grows with the distance, so it stays as large on screen as it is from there — for a world that is walked around in, or seen through a headset. The default, infinity, keeps every bubble its real size. A message wraps at its spaces, `BlockySpeechBubble.MaxLineLength` (28) characters to a line; `BlockySpeechBubble.Wrap(text)` does the same wrapping for your own text.

### A full host, as an example

```csharp
// As the game starts, every Play session (Blocky resets its statics at the start of each)
BlockyStorage.Current = new SaveGameProgramStorage(save.Programs);
BlockyInput.AddPointerOverUiCheck(IsPointerOverMyUi);
ParamFieldFactory.ObjectChoices = MyObjectNames;
ParamFieldFactory.IsHidden = (type, key) => key == "angle_threshold";
// Scene ticker, in the Inspector: Fire Play Clicked On Start, Attach Saved Programs and Poll Looked At off.
// Workspace, in the Inspector: Toggle Key None, Pick Objects On Click off, Show Opener off.

runner.SetProgram(program);                                // world load, for each programmable object

panel.Open(selected);                                      // your "Code" button
panel.Closed += SaveWorld;
if (panel.IsPointerOverWorkspace(pointer)) return;         // camera orbit, selection, shortcuts

foreach (var go in programmable)                           // your Play button
{
    BlockyRuntime.Triggers.FirePlayClickedFor(go);
    BlockyRuntime.Triggers.FireGoClicked(go);
}
BlockyRuntime.Playback.Stop();                             // your Stop button
BlockyRuntime.Triggers.RaiseLookedAt(root);                // your gaze picked something
BlockyRuntime.World.Recapture(go);                         // a gizmo drag ended
```

## Themes

```csharp
panel.Theme = BlockyTheme.Dark; // Light, Dark or HighContrast
```

Setting it from code changes the theme for this session. The learner can switch with the theme button; their pick is remembered on that computer and wins the next time the workspace starts. The Inspector's *Theme* field is the look before anyone has picked.
