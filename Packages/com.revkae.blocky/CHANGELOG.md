# Changelog

All notable changes to this package are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [0.2.1] — 2026-10-02

Found by building a whole world out of library models in the first game that hosts Blocky. No block, save file or API of 0.2.0 changes meaning.

### Added
- **`BlockySpeechBubble.ReadableDistance`:** past this distance from the main camera a bubble grows with the distance, so it stays as large on screen as it is from there — for a world that is walked around in or seen through a headset. Infinity (the default) keeps every bubble its real size. Reset every Play session, like the other switches.
- **A long message wraps** at its spaces, 28 characters to a line (`BlockySpeechBubble.Wrap`, `MaxLineLength`); a sentence used to be one line across the scene.

### Changed
- **`set visible` shows or hides every renderer under the object**, as `change color` recolors them all — on a model whose meshes sit under a root with no renderer it did nothing. Reset and Step back put each renderer back. A speech bubble is left alone.
- **A speech bubble is the same size whatever the object's scale is**, sits above everything the object shows (not half the root renderer's height above its pivot), and lies flat on the screen instead of facing where the camera stands.
- **The bubble is drawn with Blocky's own two shaders** (`Resources/Blocky`), the same in every render pipeline. Its text is depth tested, and its card writes depth.

### Fixed
- A speech bubble's card was magenta in a project that has the URP package installed but renders with the built-in pipeline.
- A speech bubble was huge over a scaled-up model, invisible over a scaled-down one, and inside a model whose pivot is at its feet.
- A speech bubble's text showed through walls, and through a nearer bubble.

## [0.2.0] — 2026-10-01

Blocky can be hosted: a game with a flow of its own — its own selection, Play button and save data — can run Blocky inside it. Everything here is additive, and every default is what 0.1.0 did.

### Added
- **The workspace can be driven from code** (`BlockyInGamePanel`): `Open(target)`, `Close()`, `SetTarget(target)` (null clears the table), `IsOpen`, `Target`, the events `Opened`, `Closed` and `TargetChanged(before, now)`, and `IsPointerOverWorkspace(screenPosition)`.
- **Picking by a click is a choice:** `PickObjectsOnClick` turns it off, and `PickResolver` maps what a press hit to the object to program, or ignores the press.
- **No hotkey, no corner button:** `ToggleKey = Key.None` (the key caps go, and no key is read) and `ShowOpener = false` (closed, the workspace draws nothing and takes no input).
- **Program storage is pluggable:** `IProgramStorage`, `BlockyStorage.Current`, and `FileProgramStorage` — the default, the same save files as before.
- **`ObjectProgramRunner.SetProgram(ObjectProgram)`:** hands a runner its program in one call; a running runner compiles it at once.
- **Switches on `BlockyRuntimeTicker`:** `FirePlayClickedOnStart`, `AttachSavedPrograms`, `PollKeyboard`, `PollLookedAt`, `PollClicked`.
- **`TriggerBroker.RaiseLookedAt(target)`**, for a game that decides what is looked at itself.
- **Reset follows the learner:** `WorldSnapshot.Recapture(go)`, `RecaptureAll()` and `Forget(go)`.
- **A new block, `as [object]`** (`control.as_object`, input `object`): the blocks inside it act on another object.
- **Object inputs as a dropdown:** `ParamFieldFactory.ObjectChoices`. **Hiding an input:** `ParamFieldFactory.IsHidden`.
- **`BlockyInput.AddPointerOverUiCheck` / `RemovePointerOverUiCheck`:** a press on the game's own interface is not a press on the game.
- Tests for the workspace itself (`Blocky.Game.Tests`).

### Changed
- `change color` recolors every renderer under the object, in every material slot — a model whose meshes sit under a root with no renderer now changes color — and Reset and Step back put each one back. A speech bubble is left alone.
- Clicking the object already on the table keeps its table and undo history; it used to rebuild them.
- The ticker gives saved objects their runners at the top of `Start` instead of in `Awake`, so a ticker added from code can be configured first.
- The workspace registers its "pointer is over me" check with `AddPointerOverUiCheck`, and no longer sets `BlockyInput.IsPointerOverUi` (still honoured for code that does).
- `IActiveDrag` has a new member, `Cancel()`.

### Fixed
- A toggle key of `Key.None` threw every frame.

## [0.1.0] — 2026-09-29

First release as a Unity package.

### Added
- The package: runtime (`Blocky.Data`, `Blocky.Compiler`, `Blocky.Localization`, `Blocky.Runtime`, `Blocky.Editor`, `Blocky.Game`), editor tools (`Blocky.Tooling`), and their EditMode tests.
- **GameObject › Blocky › Workspace** and the `Blocky Workspace` prefab it adds: the in-game workspace with its panel settings and stylesheets already wired.
- The package's `link.xml` is handed to the linker for every build, so IL2CPP keeps the reflection-only block ops however the package was installed.

### Changed
- The Program Editor window saves a new program to `Assets/Blocky/Programs` (it was `Assets/Demo/Programs`).
