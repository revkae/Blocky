# Blocky

Visual block programming inside a Unity game: players open an in-game workspace, drag blocks onto a table to program the objects in the scene, and a small VM runs the programs.

## Use it in your project

Blocky is a Unity package. Add this line to your project's `Packages/manifest.json`, under `dependencies`:

```json
"com.revkae.blocky": "https://github.com/revkae/Blocky.git?path=/Packages/com.revkae.blocky#v0.2.1"
```

Then **GameObject › Blocky › Workspace**. Requirements, setup and making your own blocks: [the package's README](Packages/com.revkae.blocky/README.md).

## This repository

A Unity 6000.7 project that develops the package (the package itself supports Unity 6.3 LTS and later):

- `Packages/com.revkae.blocky/` — the package itself (embedded, so editing it here edits the package).
- `Assets/Scenes/BlockyDemo.unity` and `Assets/Demo/` — the demo scene and its programs.
- `Assets/BlockyBlocks/` — an example of a game's own block (`jump`), made with **Blocky › New Block…**.
- `Assets/Docs/Blocky/` — the design notes (an Obsidian vault).
