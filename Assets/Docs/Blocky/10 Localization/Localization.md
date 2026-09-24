---
tags: [localization]
---

# Localization

Every word Blocky shows can be read in the player's language, picked with the language buttons in the in-game title bar (next to Free / Simple). English and Turkish ship; adding a language is adding one file. Built on **Unity 6.7's built-in localization runtime** (`com.unity.modules.localizationruntime`, namespace `Unity.Localization`) — no package, no Addressables. Why this shape: [[09 Decisions/Decisions#ADR-030|ADR-030]].

## Adding a language
1. Copy `Assets/Resources/Languages/en.json` to `Assets/Resources/Languages/<code>.json` (a locale code: `de`, `fr`, `pt-BR`).
2. Set `"locale"` to the code and `"name"` to the language's own name for itself (`"Deutsch"`) — that is what its button says.
3. Translate the `"strings"` values. Keep the keys, and keep every `{0}`, `{1}`…: they are the block names and numbers the sentence is about, and a translation may move them anywhere in it.

That's all. The file is found at startup and the language gets its button. A key left out, or left `""`, shows the English text, so a half-done translation still works. `LanguageFileTests` fails until every key is there with the same placeholders, so an unfinished language is visible in the test run rather than on screen.

## Where the words come from
| What | Key | Notes |
|---|---|---|
| A block's name | `block.<blockType>` | Falls back to the asset's `displayNameKey` (its English name), so a new block shows something before it is translated |
| An input's label | `param.<key>` | One label per input name, shared by every block with that input |
| A dropdown choice | `choice.<paramKey>.<stableId>` | Only the display changes — programs keep storing the stable id ([[02 Data Model/Data Model|Data Model]]), so a program saved in Turkish runs in English |
| A category | `category.<name>` | Tab rail, palette captions |
| Advice | `advice.*` | Whole sentences with the names as placeholders — never glued together from pieces, because word order differs |
| Chrome | `run.*`, `mode.*`, `table.*`, `tips.*`, `drop.*`, … | The run bar, title bar, tips, drop captions |

In code: `BlockyText.Get("run.go")`, `BlockyText.Format("advice.empty_branch", name)`; for blocks, `BlockDefinition.DisplayName`, `ParamSpec.DisplayName` and `ParamSpec.ChoiceDisplayName(id)`. Text with a `{` in it is a Unity **Smart String**, so a translation can also use plural forms.

## How it's wired
- **`Blocky.Localization`** — a leaf assembly the Compiler, Editor and Game reference. `BlockyText` looks words up; `BlockyLanguages` sets up and switches languages; `LanguageFileProvider` is an asset provider in Unity Localization's chain that turns each language file into a `ResourceTable` of the `Blocky` collection, synchronously, so a label can be filled the moment it's built.
- **Settings:** if the project has no Localization settings asset, Blocky makes its own at runtime. If one is added later (Project Settings > Localization), Blocky adds its languages and provider to that instead and leaves the rest alone.
- **Which language shows first:** the one picked last time (PlayerPrefs `blocky.language`), else a `-language=tr` command-line argument, else the computer's language, else English.
- **The buttons** are one per language, drawn like Free / Simple, the lit one being the language on show. Past three languages they show codes (`DE`) instead of names to stay narrow.
- **Switching** sets `LocalizationSettings.SelectedLocale`, which raises `SelectedLocaleChanged`; `BlockyInGamePanel` rewords its chrome and rebuilds the palette and table in place — same object, same undo history, same view.
- **Capitals** are made per language (`BlockyText.ToUpper`): Turkish *Değişkenler* is *DEĞİŞKENLER*, not the invariant *DEĞIŞKENLER*. This is display text only; ids are never cased by culture (TDD §7's Turkish-I rule still stands).
- **Tests read English** on every machine: `EnglishForTests` pins it for the Compiler and Editor suites with `BlockyLanguages.Override`, which does not touch the player's saved choice.
- The Editor-only Program Editor window keeps English chrome (it is a developer tool); the blocks in it follow the selected language.
