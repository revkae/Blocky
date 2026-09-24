---
tags: [api, game-code]
---

# Blocky from C#

How game code talks to Blocky: hearing what the learner's scripts do, sending them messages, and driving the run bar. Everything here is in the `Blocky.Runtime` namespace and assembly. Design notes: [[09 Decisions/Decisions#ADR-033|ADR-033]].

## Events — `BlockyEvents`

| Event | When | Typical use |
|---|---|---|
| `ScriptStarted(BlockyScript)` | A script began running (the frame after its event happened) | "Running…" indicators, counting runs |
| `ScriptFinished(BlockyScript, ScriptEndReason)` | A script ended **by itself**: `Completed` (ran out of blocks), `StoppedByBlock` (a `stop` block), `Failed` (a block threw — the console names it) | Check a result once the learner's script is done |
| `AllScriptsFinished()` | A script ended by itself and nothing is left running or waiting to start | "Did the robot reach the goal?" |
| `LevelCompleted(GameObject)` | A `level complete` block ran (its object), or `CompleteLevel()` was called (null) | Next level, stars, confetti |
| `MessageSent(string)` | Any broadcast — from a `broadcast` block or from `Broadcast()` | Let blocks drive game code: `broadcast [open door]` |
| `Stopped()` | The run bar's Stop or Reset ended every script | Reset game state that isn't a programmed object |
| `WorldReset()` | Reset put every programmed object back, after `Stopped` | Put collectibles, doors, scores back |
| `ProgramEdited(GameObject)` | The learner changed an object's program in the in-game editor | Clear a "solved" badge, count edits |

`BlockyScript` carries the `Object`, the script's `Index` among the object's scripts, and its `Trigger` — the hat's block type, such as `event.when_go_clicked`.

**Not reported as finished:** scripts ended from outside — the Stop or Reset button (`Stopped` says so instead), an edit to the program, the object being disabled or destroyed, or the script's own event restarting it.

## Commands

| Call | Does |
|---|---|
| `BlockyEvents.Broadcast("open")` | What a `broadcast` block does: every `when I receive [open]` script starts (names match ignoring case and surrounding spaces) |
| `BlockyEvents.CompleteLevel()` | What the `level complete` block does: `LevelCompleted`, and every `when level complete` script |
| `BlockyRuntime.Playback.Go()` / `Stop()` / `ResetWorld()` | The run bar's Go, Stop and Reset |
| `BlockyRuntime.Playback.Pause()` / `Resume()` / `StepForward()` / `StepBack()` / `SetSpeed(1–4)` | The rest of the run bar |
| `BlockyRuntime.Variables.Get("score")` / `Set(...)` / `Item("items", 1)` / `Length("items")` | Read or change what the scripts see; `owner` picks an object's own copy |
| `runner.SetProgramAsset(asset)` then `Shutdown()` + `Initialize()` | Give an object a program from code (it wins over a save — [[09 Decisions/Decisions#ADR-031|ADR-031]]) |

## Rules for handlers

- Everything is raised on the **main thread**, while Blocky runs its scripts or from the call that caused it.
- A handler that throws is logged and never stops the scripts or the other handlers.
- **Handlers are dropped at the start of every Play session.** Domain reload is off in this project, so a handler left from the last session would belong to an object that no longer exists. Subscribe in `OnEnable` or `Start`, unsubscribe in `OnDisable`.
- `LevelCompleted` fires every time — a goal touched twice completes twice. Ignore repeats if they matter.

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

A teacher can build the same level with no C# at all: the goal gets `when collided → level complete`, the player `when level complete → say [You did it!]`.
