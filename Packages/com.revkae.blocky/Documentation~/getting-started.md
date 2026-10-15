# Getting started

Five minutes from import to a running program.

## 1. Install Blocky

**From the Asset Store.** Open **Window › Package Manager**, choose **My Assets**, find Blocky and install it. Blocky is a package, so it appears under *Packages › Blocky* in the Project window, not under `Assets`.

**From git** (for your own projects, if you have access to the repository). Add this line to your project's `Packages/manifest.json`, under `dependencies`:

```json
"com.revkae.blocky": "https://github.com/revkae/Blocky.git?path=/Packages/com.revkae.blocky#v0.2.1"
```

`?path=` points at the package folder inside the repository, and `#v0.2.1` pins a release, so your project only updates when you change it. **Window › Package Manager › + › Install package from git URL…** takes the same URL.

Either way, the Package Manager also installs what Blocky needs: the Input System and Newtonsoft JSON.

## 2. Turn on the Input System

Blocky reads the mouse, keyboard and touch through the Input System. Open **Edit › Project Settings › Player**, find *Active Input Handling* under *Other Settings*, and set it to **Input System Package (New)** or **Both**. Unity asks to restart; let it.

## 3. Add the workspace

Open your scene and choose **GameObject › Blocky › Workspace**. This adds the `Blocky Workspace` object: the in-game workspace with its panel settings and stylesheets already wired. One per scene is enough — if the scene already has one, the menu selects it instead.

You don't need anything else. The scene also needs a `BlockyRuntimeTicker` to run blocks, and the workspace adds one on a new *Blocky Runtime* object when you press Play.

## 4. Give an object a program

A player can program any object from inside the game, so this step is optional. To give an object a program it starts with:

1. Choose **Blocky › Program Editor**.
2. Drag an object from the Hierarchy into *Target GameObject*. The window adds an `ObjectProgramRunner` to it and saves a new program to `Assets/Blocky/Programs`.
3. Build its script with the window's buttons. **+ Add Stack** starts a script; **+ Add** adds a block. Every change is saved to the program asset.

A good first program for a cube:

```text
when Go clicked
Repeat (4)
    Move Forward  distance (2)  duration (0.5)
    Turn  direction [right]  degrees (90)  duration (0.5)
```

## 5. Press Play

Press Play, then press **Tab** or click the **Blocks** button in the corner. The workspace opens on the left; the game stays visible on the right.

1. Click the cube in the game. Its program appears on the table.
2. Press **▶ Go**. The cube walks a square, and each block lights up while it runs.
3. Change a number, or drag a new block from the palette and drop it under another until the edge glows. The change is saved at once and the object runs the new version.

Press **Tab** again to close the workspace.

!!! note "Edits made in the game win"
    A program changed in the game is saved on your computer and runs the next time you press Play, instead of the program in the scene. To go back to the scene's programs, choose **Blocky › In-Game Saves › Delete All…**. See [Saving and loading](saving.md).

## Next steps

- [Using the workspace](workspace.md) — everything on the screen you just opened.
- [Block reference](blocks.md) — what every block does.
- [Custom blocks](custom-blocks.md) — add a block of your own in C#.
- [Scripting](scripting.md) — open the workspace from your own button, react when a level is solved, keep programs in your own save data.
