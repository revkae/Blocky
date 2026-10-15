# Running the tests

Blocky ships with its EditMode tests: the program data, the compiler, the virtual machine and every block, the workspace's views and the workspace itself, the language files, and the New Block wizard. They are a quick way to check that Blocky works in your project, and they check your own language files too.

## Turn them on

Unity compiles a package's tests only when the project lists the package as *testable*. Add a `testables` list to your project's `Packages/manifest.json`, next to `dependencies`:

```json
{
  "dependencies": {
    "com.revkae.blocky": "…"
  },
  "testables": [ "com.revkae.blocky" ]
}
```

The tests need the Test Framework package (`com.unity.test-framework`), which new Unity projects have. Without `testables`, the test assemblies are left out and add nothing to your project or your builds.

## Run them

1. Leave Play mode.
2. Open **Window › General › Test Runner** and choose the **EditMode** tab.
3. Click **Run All**, or pick an assembly to run just its tests.

| Assembly | What it tests |
|---|---|
| `Blocky.Data.Tests` | Programs, their edits and undo, block limits, and saving to JSON. |
| `Blocky.Compiler.Tests` | The block registry, toolboxes, compiling programs, My Blocks, and advice. |
| `Blocky.Runtime.Tests` | The virtual machine, every block's op, triggers, playback and stepping, variables, lists, clones, sounds, speech bubbles, 2D, the save files, and the host API. |
| `Blocky.Editor.Tests` | The block views, the palette, snapping, Simple mode and the stylesheets. |
| `Blocky.Game.Tests` | The workspace itself, made from its prefab. |
| `Blocky.Localization.Tests` | The language files and the words. |
| `Blocky.Tooling.Tests` | The New Block wizard's output. |

The tests read English on every machine, whatever the computer's language.

## Your own language files

`LanguageFileTests` reads every language file in the project, yours included. It fails when a translation is missing a key English has, has a key English doesn't, or changes a sentence's placeholders — so an unfinished language shows up in the test run rather than on screen. See [Languages](languages.md).

## Your own blocks

`OpTableBuilderTests` checks that every block asset Blocky ships is bound to a class of the right kind. A test of your own can check your blocks the same way: build the tables from the registry and see that nothing is reported missing.

```csharp
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Runtime;
using NUnit.Framework;

public sealed class MyBlockTests
{
    [Test]
    public void EveryBlock_HasAClass()
    {
        var missing = new List<string>();
        OpTableBuilder.BuildAllForPlay(BlockRegistry.LoadFromResources(), missing.Add);
        CollectionAssert.IsEmpty(missing);
    }
}
```

Put it in a test assembly that references `Blocky.Runtime` and `Blocky.Compiler`.
