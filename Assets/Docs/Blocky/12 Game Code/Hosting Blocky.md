---
tags: [api, game-code, hosting]
---

# Hosting Blocky

For a game that has a flow of its own — its own selection, its own Play button, its own save data — and wants Blocky inside it rather than in charge. Everything here is new in **0.2.0**, additive, and off by default: a scene that sets none of it behaves as 0.1.0 did. Design: [[09 Decisions/Decisions#ADR-041 — A game can host Blocky: the game owns the flow, Blocky keeps its defaults|ADR-041]], [[09 Decisions/Decisions#ADR-042 — `as [object]` is a frame that carries the object the blocks act on|ADR-042]], [[09 Decisions/Decisions#ADR-043 — An object's look is every renderer under it, and Reset's start can be moved|ADR-043]]. The everyday events and commands are in [[12 Game Code/Blocky from CSharp|Blocky from C#]].

Written for the first host, XR-Education (the BlocksEngine2 → Blocky migration); the numbers in brackets are its requirement numbers.

## The workspace — `BlockyInGamePanel` (`Blocky.Game`)

| Member | Does |
|---|---|
| `Open(GameObject target = null)` [1] | Opens the workspace, on `target`'s program when one is given and it isn't already there. Works before the workspace's own `Awake` |
| `Close()` [1] | Closes it, as the title bar's button and the key do |
| `SetTarget(GameObject target)` [1] | Puts that object's program on the table (adds an `ObjectProgramRunner` if it has none); `null` clears the table. The object already there is read again from its runner — call it after replacing the program from your own storage |
| `IsOpen`, `Target` [1] | The state. `Target` is null before one is chosen |
| `Opened`, `Closed` [1] | Every closed→open and open→closed, whatever caused it (your call, the key, a button) |
| `TargetChanged(before, now)` [1] | Another object is on the table — your call or the learner's click. Not raised for `SetTarget(sameObject)`. When the edited object was destroyed: `(theDestroyedObject, null)`, on the next frame |
| `IsPointerOverWorkspace(Vector2 screenPosition)` [1] | True over the open workspace or one of its open dropdown menus (Input System pixels, origin bottom-left). Closed: only over the corner button, and nowhere when that is hidden |
| `PickObjectsOnClick` [2] | On (default): clicking an object in the game opens its program. Off: only your calls choose the object |
| `PickResolver` [2] | With picking on: maps what the press hit to the object to program (a model's root), or null to ignore the press |
| `ToggleKey` [3] | The key that opens and closes it. `Key.None`: no key, and the key caps go |
| `ShowOpener` [3] | Off: closed, the workspace draws nothing and takes no input |

All four switches are also fields in the Inspector (*Toggle Key*, *Pick Objects On Click*, *Show Opener*).

**Order of events.** Events are raised after the state has changed, and a handler may call `SetTarget` or `Close`. `Open(target)` on a closed workspace changes both things, then raises `Opened`, then `TargetChanged(before, target)`. Opening an open workspace on the object it shows raises nothing and keeps the table (and its undo history).

**With no object** the run bar, Undo and Delete do nothing, and the title bar shows `status.pick_object`. With picking off that sentence ("Click an object in the game to code it") is wrong for your game — override it in your own language overlay (`Resources/Languages/<code>.blocks.json`, [[10 Localization/Localization|Localization]]).

## Where programs are kept — `IProgramStorage` (`Blocky.Runtime.Persistence`) [4]

```csharp
public interface IProgramStorage
{
    bool HasSavesFor(Scene scene);                                             // cheap pre-check; false and no object is ever asked about
    bool Exists(GameObject owner);                                             // asked at scene start, to give saved objects a runner
    SavedProgramStatus TryLoad(GameObject owner, out ObjectProgram program);   // None / Loaded / Unreadable
    bool TrySave(GameObject owner, ObjectProgram program, out string problem); // never throws
}

BlockyStorage.Current = new MyStorage();   // default, and after null: FileProgramStorage.Instance
```

The workspace saves every edit through it and a runner loads through it the first time it needs its program. `Unreadable` shows the "couldn't be opened" notice under the table and the object runs its scene program. `RuntimeProgramStorage` (the files, their keys, `SafeName`) is unchanged and public.

**Set it before your programmed objects wake** — a runner that has already looked found the files. Or hand programs out yourself (`SetProgram`, below) and turn the ticker's *Attach Saved Programs* off. **It is reset at the start of every Play session**, like every static here: set it as the game starts, each time.

## Handing a runner its program — `ObjectProgramRunner.SetProgram(ObjectProgram)` [12]

Replaces the program the object runs: no asset to make, no `Shutdown` / `SetProgramAsset` / `Initialize` to order. If the runner is running, its scripts stop and the new program is compiled at once; otherwise it is what the runner runs when it starts. The program is copied. Wins over a saved program. `null` is an empty program.

## The ticker's switches — `BlockyRuntimeTicker` [5]

Put one in the scene and set these (Inspector or properties); a runner or the workspace finds it instead of adding their own.

| Switch | Off means |
|---|---|
| `FirePlayClickedOnStart` | "when Play clicked" scripts don't start at scene start. Start them yourself: `Playback.Go()`, or per object `Triggers.FirePlayClickedFor(go)` + `Triggers.FireGoClicked(go)` |
| `AttachSavedPrograms` | Objects with a saved program but no runner are not given one at scene start |
| `PollKeyboard` | "when key pressed" never fires by itself |
| `PollLookedAt` | "when looked at" is yours to raise (below) |
| `PollClicked` | "when clicked" is yours to raise (`Triggers.RaiseClicked(go)`) |

The scheduler ticks whatever they say. Saved programs are attached at the top of the ticker's `Start` (it was `Awake`), so a ticker added from code can be configured first.

## Your own gaze — `TriggerBroker.RaiseLookedAt(GameObject)` [6]

`BlockyRuntime.Triggers.RaiseLookedAt(root)` starts exactly that object's "when looked at" scripts; raised again while one runs, it restarts it. Turn `PollLookedAt` off, or Blocky's own angle check fires too. The `angle_threshold` input then does nothing — hide it (below).

## Reset after the learner moved things — `WorldSnapshot` [7]

| Call | Does |
|---|---|
| `BlockyRuntime.World.Recapture(go)` | Makes `go`'s current state its start — what Reset returns it to. Starts tracking it if it wasn't. Call at the end of a gizmo drag or a property edit |
| `BlockyRuntime.World.RecaptureAll()` | The same for every tracked object |
| `BlockyRuntime.World.Forget(go)` | Stops tracking it (deleted, or no longer programmable) |

Reset never recolors a material an object had when it was first seen — in the Editor those are project assets. `BlockyRuntime.World` is rebuilt every Play session.

## `as [object]` — one script driving another object [8]

Block type **`control.as_object`**, one input **`object`** (an `ObjectRef`; default `me`), one mouth. The blocks inside act on the named object; after it, the script's own object again. Nestable. An unknown name skips the mouth, with one warning per run of the script. In C# terms: `OpContext.Target` is the object a block acts on, `OpContext.Thread.Target` the object whose script it is. Words: English "As", Türkçe "Şu Olarak" (`block.control.as_object`).

## Object inputs as a dropdown — `ParamFieldFactory.ObjectChoices` (`Blocky.Editor`) [9]

```csharp
ParamFieldFactory.ObjectChoices = () => myScriptableObjectNames;   // IReadOnlyList<string>; null = a free text field, as before
```

Asked each time a field is drawn. "me" (`object.me`, translated) is always first and stores `me`; the program stores the **name**; a stored name the list doesn't have is still shown. Names are matched trimmed and ignoring case, as the runtime finds objects. Reset every Play session.

## Hiding an input — `ParamFieldFactory.IsHidden` [13]

```csharp
ParamFieldFactory.IsHidden = (blockType, paramKey) => blockType == "event.when_looked_at" && paramKey == "angle_threshold";
```

The input isn't drawn — on the table or in the palette — and its value stays in the program. Reset every Play session.

## Presses on your own interface — `BlockyInput` [11]

```csharp
BlockyInput.AddPointerOverUiCheck(() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
```

While any added check says true, `mouse down?` is false and "when clicked" doesn't fire. `RemovePointerOverUiCheck` takes it back. The workspace adds its own. Reset every Play session.

## `change color` on models [10]

It recolors every renderer under the object, in every material slot — so a model whose meshes sit under a renderer-less root changes color, and Reset and Step back put every one back. A speech bubble is left alone. `set visible` reaches the same renderers (0.2.1).

## Speech bubbles seen from far away — `BlockySpeechBubble.ReadableDistance` (0.2.1)

```csharp
BlockySpeechBubble.ReadableDistance = 10f; // as the game starts: reset every Play session
```

A bubble is one size in the world whatever its object's scale is. Past `ReadableDistance` from the main camera it grows with the distance, so it stays as large on screen as it is from there — for a world that is walked around in, or seen through a headset. The default, infinity, keeps every bubble its real size. A message wraps at 28 characters, and the bubble is drawn with Blocky's own shaders (`Resources/Blocky`), so there is nothing to set up for the built-in pipeline or URP ([[09 Decisions/Decisions#ADR-044 — A speech bubble is measured in the world, not in its object, and Blocky draws it with shaders of its own|ADR-044]]).

## XR-Education's wiring, as an example

```csharp
// bootstrap, every Play session (the statics are reset at its start)
BlockyStorage.Current = new SyncObjectProgramStorage();                       // [4]
BlockyInput.AddPointerOverUiCheck(IsPointerOverUGui);                         // [11]
ParamFieldFactory.ObjectChoices = ScriptableObjectNames;                      // [9]
ParamFieldFactory.IsHidden = (type, key) => key == "angle_threshold";         // [13]
// scene ticker, in the Inspector: Fire Play Clicked On Start, Attach Saved Programs and Poll Looked At off   [5]
// workspace, in the Inspector: Toggle Key None, Pick Objects On Click off, Show Opener off                    [2][3]

runner.SetProgram(program);                                                   // [12] world load, per scriptable object

panel.Open(selected);                                                         // [1] the "Code Blocks" button
panel.Closed += UploadWorldToPlayFab;
if (panel.IsPointerOverWorkspace(pointer)) return;                            // camera orbit, selection, shortcuts

foreach (var go in scriptable) { BlockyRuntime.Triggers.FirePlayClickedFor(go); BlockyRuntime.Triggers.FireGoClicked(go); }   // Play
BlockyRuntime.Playback.Stop();                                                // Stop
BlockyRuntime.Triggers.RaiseLookedAt(root);                                   // [6] reticle dwell completed
BlockyRuntime.World.Recapture(go);                                            // [7] gizmo drag ended
```

## Where this differs from the requirements as written

- `control.as_object`'s `object` input defaults to **`me`**, not empty (the same thing; it matches the other object inputs and reads better in a text field).
- `SetTarget(sameObject)` rebuilds the table but does **not** raise `TargetChanged` — nothing changed.
- A press on the object already on the table keeps its table; 0.1.0 rebuilt it and lost the undo history.
- Added beyond the list: `ToggleKey` as a property, `WorldSnapshot.CaptureDriven` (so per-object Reset and Step back cover an `as [object]` target), `IActiveDrag.Cancel`.
