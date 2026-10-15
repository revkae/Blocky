# Blocky

Blocky puts visual block programming inside a Unity game. Players open an in-game workspace, drag blocks onto a table to program the objects in the scene, and a small virtual machine runs the programs — in Play mode and in builds.

You add the workspace to a scene with one menu item, give objects a starting program if you want one, and press Play. Everything else happens in the game.

## Features

- **An in-game workspace.** Built with UI Toolkit and free of `UnityEditor` code, so it works the same in the Editor's Play mode and in a build. Tab (or the *Blocks* button) opens it; click an object in the game to program it.
- **88 blocks in 10 categories:** Event, Motion, Looks, Control, Sensing, Operators, Sound, Variables, Lists and My Blocks. Every white input takes a value block, so `move forward (pick random 1 to 10)` just works.
- **Two ways to build.** *Free* is the free table: blocks go anywhere, scripts sit side by side, the table pans and zooms. *Simple* is one numbered column where every block joins a script. Both open the same program.
- **A run bar made for learning:** Go, Stop, Reset, Pause, Step back and Step forward, and 1x–4x speed. The block a script is on lights up while it runs.
- **Advice in plain words.** A script that won't do anything says why, under the table, with a badge on the block.
- **Undo and redo**, box select, group drag, delete by dropping on the palette.
- **Variables, lists, clones, say and think bubbles, and generated notes** — no audio files needed.
- **My Blocks:** define your own block, run it with three inputs, recursion included.
- **Your own blocks in C#.** **Blocky › New Block…** makes the block asset, the C# class that runs it, and its words in every language. There is nothing to register.
- **Programs save themselves.** Every edit is saved as it happens and runs the next time the game starts. A game can keep programs in its own save data instead.
- **Hosting.** A game with its own selection, Play button and save data can drive the workspace from C#.
- **Puzzle levels.** A toolbox limits which blocks a level offers and how many a program may use. `level complete` and `BlockyEvents` tell your code when the learner solved it.
- **Three themes** (Light, Dark, High contrast) and **two languages** (English, Türkçe). A new language is one JSON file.
- **3D and 2D.** Motion, clicks and contacts work with 3D colliders, 2D colliders and sprites.
- **Mouse, keyboard and touch** through the Input System.
- **EditMode tests** ship with the package.

## Requirements

- Unity **6.3 LTS** or newer.
- The **Input System** package, with *Active Input Handling* set to *Input System Package (New)* or *Both*. See [Getting started](getting-started.md).
- **Newtonsoft JSON** (`com.unity.nuget.newtonsoft-json`). The Package Manager installs it, and the Input System, with Blocky.
- The Universal Render Pipeline or the Built-in Render Pipeline. See [Builds and platforms](builds.md).

## This manual

| Page | What it covers |
|---|---|
| [Getting started](getting-started.md) | Five minutes from import to a running program. |
| [Using the workspace](workspace.md) | The palette, the table, the run bar and everything else a player touches. |
| [Block reference](blocks.md) | Every block, what it does and what its inputs mean. |
| [Custom blocks](custom-blocks.md) | My Blocks for players; new C# blocks, toolboxes and block limits for developers. |
| [Scripting](scripting.md) | Blocky from C#: events, the run bar, runners, and hosting Blocky inside your own game flow. |
| [Saving and loading](saving.md) | Where programs are saved, how files are named, and how to use your own storage. |
| [Languages](languages.md) | Adding a language, words for your own blocks, and the language API. |
| [Builds and platforms](builds.md) | IL2CPP, WebGL, render pipelines and 2D scenes. |
| [Running the tests](tests.md) | The package's EditMode tests in your project. |
| [Troubleshooting](troubleshooting.md) | Known limits, common questions and Blocky's console messages. |
| [Changelog](changelog.md) | What changed in each version. |
