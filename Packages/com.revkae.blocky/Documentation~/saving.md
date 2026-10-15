# Saving and loading

Blocky keeps two kinds of program:

- **Scene programs** — what an object starts with. They are assets in your project, made in the Unity Editor.
- **In-game saves** — what the learner built in the workspace. They are files on the player's computer, written as the game runs.

## Scene programs

**Blocky › Program Editor** saves a new program for an object to `Assets/Blocky/Programs/<object name>.asset`, and puts it in the `Program Asset` of the object's `ObjectProgramRunner`. The asset is a `BlockProgramAsset`: the program as JSON, wrapped in an asset so you can assign it in the Inspector. **Assets › Create › Blocky › Program** makes an empty one.

Scene programs are part of your project. They go into builds like any other asset, and the workspace never writes to them.

## In-game saves

Every edit in the workspace — a drop, a delete, a changed value — is saved at once. There is no save button. A number or text box saves when you press Enter or click away.

The next time the game starts, every object runs what was saved for it, instead of its scene program. That includes objects that were first programmed in the game: the ticker gives them back their `ObjectProgramRunner` at scene start.

This is true in the Editor's Play mode too. A program you change in the Program Editor won't run while an in-game save exists for that object. **Blocky › In-Game Saves › Show Folder** opens the folder; **Blocky › In-Game Saves › Delete All…** deletes every save on this computer, and each object runs its scene program again.

### Which program runs

1. A program handed over from code: `ObjectProgramRunner.SetProgram` or `SetProgramAsset`.
2. Else the in-game save, from `BlockyStorage.Current`.
3. Else the runner's program asset from the scene.
4. Else nothing: an empty program.

### Where the files are

```text
<Application.persistentDataPath>/BlockyPrograms/<scene>/<parent>/…/<object>.json
```

One file per programmed object, keyed by its scene and its place in the hierarchy:

- **The scene folder** keeps same-named objects in different scenes apart. An unsaved scene is `Untitled`.
- **The parent folders** keep an object apart from a same-named one elsewhere in the hierarchy.
- **Same-named siblings** get a number in brackets, counting earlier siblings with the same name: `Cube.json`, `Cube[1].json`, `Cube[2].json`. Names are compared ignoring case.
- **Every name is made safe** for every file system. Characters a file system refuses, and `%`, `[` and `]`, become `%` and two hex digits (`a/b` is saved as `a%2Fb`). A trailing dot or space is escaped too, and so are Windows device names such as `CON`. A name longer than 80 characters is cut and ends with `~` and a short hash.

Renaming or moving an object in the Unity Editor gives it a new key, so it starts again from its scene program. Clones are never saved.

`RuntimeProgramStorage.Root` is the folder, and `RuntimeProgramStorage.KeyFor(go)` and `RuntimeProgramStorage.PathFor(key)` give an object's key and file.

### A file that can't be read

A save file that is damaged, or was written by a newer version of Blocky, is never overwritten. Blocky renames it to:

```text
<object>.json.<yyyyMMdd-HHmmss>.unreadable
```

The object runs its scene program, the workspace shows *The saved program for "…" couldn't be opened, so it runs its scene program. The old file was kept.*, and the console has a warning saying where the file went. The next edit writes a fresh save.

### A save that fails

A save that fails — a full disk, a read-only folder — never undoes the edit and never throws. The change runs now, the workspace shows *Couldn't save the program for "…". Your change runs now, but won't be here next time.*, and the console has a warning with the reason.

## Your own storage

A game that keeps programs in its own save data, or on a server, sets `BlockyStorage.Current` to its own `IProgramStorage`. See [Scripting › Your own storage](scripting.md#your-own-storage-iprogramstorage).

## Program files

A program is JSON, written and read with `ProgramSerializer.Serialize` and `ProgramSerializer.Deserialize` (in `Blocky.Data.Serialization`). Each file records its schema version. Blocks store their type and their inputs' stable ids, never words in a language, so a program saved in one language opens in any other. A block type the project doesn't have any more is kept and shown as an unknown block, so nothing is lost.
