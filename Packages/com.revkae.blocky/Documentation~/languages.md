# Languages

Every word Blocky shows can be read in the player's language. Blocky ships with **English** and **Türkçe**, and the player picks one with the language buttons in the workspace's title bar. Adding a language is adding one file.

## Add a language

1. Copy Blocky's English file, `Runtime/Resources/Languages/en.json` (under *Packages › Blocky* in the Project window), into a `Resources/Languages` folder of your project — for example `Assets/Languages/Resources/Languages/de.json`. Name the copy after the language's code: `de`, `fr`, `pt-BR`.
2. Set `"locale"` to the code, and `"name"` to the language's own name for itself. That name is what its button says.
3. Translate the values under `"strings"`. Keep the keys as they are, and keep every placeholder — `{0}`, `{1}` and so on.

```json
{
  "locale": "de",
  "name": "Deutsch",
  "strings": {
    "run.go": "▶ Los",
    "run.stop": "Stopp",
    "block.control.repeat": "Wiederhole",
    "advice.empty_branch": "In „{0}“ ist noch nichts. Lege Blöcke in die Lücke."
  }
}
```

That's all. Every `Resources/Languages` folder in the project is read at startup, and each language gets its button. The package itself is read-only, so your files always go under `Assets`.

**A key you leave out, or leave as `""`, shows the English text.** A half-done translation still works, a key at a time.

## Placeholders

`{0}`, `{1}`… stand for the block names and numbers a sentence is about. A translation may move them anywhere in the sentence, because word order differs between languages:

```json
"advice.out_of_range": "“{0}” in “{1}” must be from {2} to {3}, so this script won't run."
```

How they are filled depends on your Unity version:

- **Unity 6.3 to 6.6** — Blocky reads the language files itself and fills `{0}`-style placeholders with ordinary .NET formatting, in the language's culture.
- **Unity 6.7 and later**, in a project that has the built-in `localizationruntime` and `smartstrings` modules — Blocky uses Unity's localization runtime. Text with placeholders is a Smart String, so a translation can also use plural forms and the other Smart String formatters.

A language file that must work on both should keep to plain `{0}`, `{1}` placeholders. If a placeholder doesn't match its values, Blocky shows the text as it is and logs a warning.

## Where the words come from

| What | Key | Notes |
|---|---|---|
| A block's name | `block.<blockType>` | `block.control.repeat`. A block no file names shows the English name from its asset. |
| An input's label | `param.<key>` | One label per input name, shared by every block with that input. |
| A dropdown choice | `choice.<paramKey>.<stableId>` | `choice.direction.left`. Only the words change: programs store the stable id, so a program saved in Turkish runs in English. |
| A category | `category.<name>` | `category.motion`. The tabs and palette captions. |
| Advice | `advice.*` | Whole sentences, with names as placeholders. |
| Everything else | `run.*`, `mode.*`, `table.*`, `tips.*`, `drop.*`, `limit.*`, `storage.*`, `theme.*`, `workspace.*`… | The run bar, title bar, tips, drop captions and notices. |

Capitals are made the way each language writes them: Turkish *değişkenler* becomes *DEĞİŞKENLER*.

## Words for your own blocks

A language can have more than one file. Every file with the same `"locale"` adds to that language. The file named just by its code (`en.json`) is the base; the others — `en.blocks.json`, for example — are laid over it in name order, and a string they repeat replaces the base's.

**Blocky › New Block…** uses this: it writes each new block's words into `<code>.blocks.json` files in your own `Resources/Languages` folder, one per language, so updating Blocky never touches them. Each file gets the English words; translate them in place.

```json
{
  "locale": "tr",
  "name": "Türkçe",
  "strings": {
    "block.game.jump": "zıpla",
    "param.amount": "miktar"
  }
}
```

An overlay can also change Blocky's own words for your game. A game that turns off picking by a click can say something else where Blocky says *Click an object in the game to code it*, in a file such as `en.mygame.json`:

```json
{
  "locale": "en",
  "name": "English",
  "strings": {
    "status.pick_object": "Select an object, then press Code"
  }
}
```

## Which language shows first

1. The language the player picked last time (saved in `PlayerPrefs` under `blocky.language`).
2. Else a `-language=` command-line argument, such as `-language=tr`.
3. Else the computer's language. `tr-TR` finds `tr`.
4. Else English.

## Unity's Localization Settings

On Unity 6.7 and later with the localization modules, Blocky needs no settings asset: when the project has none, it makes its own at runtime. If the project has its own Localization Settings, Blocky adds its languages and a string table called **Blocky** to them, and leaves the rest alone.

## From C#

| Member | What it is |
|---|---|
| `BlockyLanguages.Current` | The language on show, a `BlockyLanguage` with `Code`, `Name` and `Culture`. |
| `BlockyLanguages.Available` | The languages a player can pick, a list of `BlockyLanguage`, English first. |
| `BlockyLanguages.Changed` | Event, `Action<BlockyLanguage>`: the language on show changed. |
| `BlockyLanguages.Select(code)` | Switches every Blocky string to that language and remembers the choice. False if there is no such language. |
| `BlockyLanguages.Override(code)` | Shows every Blocky string in that language until the returned `IDisposable` is disposed, without touching the player's choice. For tests. |
| `BlockyLanguages.UsesUnityLocalization` | True when Blocky's words go through Unity's localization runtime (Unity 6.7 and later, with its modules). |
| `BlockyText.Get(key)` | The text for a key in the language on show. |
| `BlockyText.Format(key, args)` | The same, with its placeholders filled. |
| `BlockyText.ToUpper(text)` | Capitals the way the language on show writes them. |
| `BlockyText.Culture` | The culture of the language on show. |

```csharp
using Blocky.Localization;
using UnityEngine;

public sealed class LanguageMenu : MonoBehaviour
{
    private void OnEnable() => BlockyLanguages.Changed += Show;

    private void OnDisable() => BlockyLanguages.Changed -= Show;

    public void PickTurkish() => BlockyLanguages.Select("tr");

    private void Show(BlockyLanguage language) => Debug.Log($"Blocky now speaks {language.Name} ({language.Code}).");
}
```

In a test, pin the language so it reads the same words on every machine:

```csharp
using (BlockyLanguages.Override("en"))
{
    Assert.AreEqual("Stop", BlockyText.Get("run.stop"));
}
```

## Check a translation

With the package's tests turned on (see [Running the tests](tests.md)), `LanguageFileTests` reads every language file in the project. It fails when a translation is missing a key English has, has a key English doesn't, or changes a sentence's placeholders.
