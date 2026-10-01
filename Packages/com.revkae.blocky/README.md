# Blocky

Scratch-style block programming inside a Unity game. Players open an in-game workspace, drag blocks onto a table to program the objects in the scene, and a small VM runs the programs — in Play mode and in builds.

## Requirements

- Unity **6000.7** or newer (Blocky's languages use 6.7's built-in localization module).
- The **Input System**. Set *Project Settings › Player › Active Input Handling* to *Input System Package (New)* or *Both*.

The Package Manager installs the rest (Input System, Newtonsoft JSON, and the Unity modules Blocky uses).

## Install

Add this line to your project's `Packages/manifest.json`, under `dependencies`:

```json
"com.revkae.blocky": "https://github.com/revkae/Blocky.git?path=/Packages/com.revkae.blocky#v0.2.0"
```

`?path=` points at this folder inside the repository, and `#v0.2.0` pins a release so your project only updates when you change it. Or use *Window › Package Manager › + › Install package from git URL…* with the same URL.

Working on Blocky and a game side by side? Point at your local copy instead, and edits show up in both:

```json
"com.revkae.blocky": "file:../../Blocky/Packages/com.revkae.blocky"
```

(The path is relative to your project's `Packages` folder.)

## Set up a scene

1. **GameObject › Blocky › Workspace** — adds the in-game workspace, already wired. One per scene.
2. Give an object a program: **Blocky › Program Editor**, pick the object, and build its script. The window adds an `ObjectProgramRunner` and saves the program to `Assets/Blocky/Programs`.
3. Press Play, then **Tab** (or the *Blocks* button) to open the workspace and click an object to program it in the game.

## Inside a game with a flow of its own

By default the workspace opens with Tab, programs whatever you click, saves to files and starts every script when the scene starts. A game that has its own selection, its own Play button or its own save data can take each of those over:

```csharp
panel.PickObjectsOnClick = false;                  // your selection decides what is edited
panel.ShowOpener = false;                          // your button opens it (Toggle Key: None in the Inspector)
panel.Open(selectedObject);
panel.Closed += SaveMyWorld;

BlockyStorage.Current = new MyProgramStorage();    // IProgramStorage: programs kept in your own save data
runner.SetProgram(program);                        // or hand each runner its program yourself
BlockyRuntime.World.Recapture(movedObject);        // Reset returns it to where the player last put it
```

`BlockyRuntimeTicker` has a switch for each thing it does by itself (starting scripts at scene start, reading the keyboard, deciding what is looked at and clicked), `TriggerBroker.RaiseLookedAt` raises "when looked at" from your own gaze, `BlockyInput.AddPointerOverUiCheck` keeps presses on your interface out of the game, and `ParamFieldFactory.ObjectChoices` turns object inputs into a dropdown of your objects. The statics among these are reset at the start of every Play session — set them as your game starts.

## Your own blocks

**Blocky › New Block…** makes a block in one go — its `BlockDefinition` asset, the C# class that runs it, and its words in every language — in a folder of your project (`Assets/BlockyBlocks` by default). Blocky finds the class by its `[BlockExecutor]` attribute; there is nothing to register. Everything Blocky's tools make goes under `Assets`: the package itself is read-only when installed from git.

## Tests

The package's EditMode tests run in your project's Test Runner once you list the package as testable in `Packages/manifest.json`:

```json
"testables": [ "com.revkae.blocky" ]
```

## Builds

The block ops are created by reflection, so `Runtime/link.xml` keeps them — and the program data — in IL2CPP builds at any stripping level. The package hands that file to the linker itself; there is nothing to copy.
