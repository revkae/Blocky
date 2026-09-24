---
tags: [release, checklist]
---

# Release Readiness — Asset Store and Classroom

What has to happen before Blocky goes on the Unity Asset Store, and before a teacher can hand it to a class. Written 2026-09-24 from a read-through of the code and this vault, plus the Asset Store's published rules on that date. **Nothing was run for this review** — there was no Unity editor — so section 1 is finding out what actually works.

**Two finish lines, for two different people:**
- **Asset Store buyers are Unity developers** building a learning app with Blocky. They need a clean package, a manual, a one-step setup, and builds that work.
- **Teachers and students never open Unity.** They need a finished app (Windows, Mac, a browser), saving they can trust, controls that suit a classroom, and something to teach with.

**Where it stands:**
- **Built and tested in Unity:** 71 blocks, the compiler and VM, 286 EditMode tests, Free and Simple modes, undo/redo, stepping forward and back, the running-block glow, advice.
- **Built, never checked in Unity:** English and Türkçe (the language pass).
- **Never tried:** a build outside the editor, a tablet, a real class.
- **Missing for a classroom:** a finished app, projects and sharing, touch, lesson material.

Work top to bottom — sections 1 and 2 come first, because both finish lines depend on them. Tick items off here. A change of design still gets an ADR in [[09 Decisions/Decisions|Decisions]], and each session an entry in [[08 Build Log/Build Log|Build Log]], per the working rule on [[Home]].

## 1. Find out what works now
- [ ] **Open the project in Unity and get a clean compile.** The language pass (`94ddf78`, [[09 Decisions/Decisions#ADR-030|ADR-030]]) was compiled only against hand-written stubs, and `Blocky.Editor` and `Blocky.Game` were never compiled with it at all. Compiler, Editor and Game all reference `Blocky.Localization`, so one error there stops everything. *Done when:* no errors in the console, and every warning written down (see §2).
- [ ] **Run every EditMode test**, Play mode stopped first ([[07 Testing/Testing Strategy|Testing Strategy]]). The last full run was 286/286, before the language pass added its own tests.
- [ ] **Check the language pass in Play mode** — what ADR-030 left open: Smart String formatting, the language buttons in the title bar, and the in-place redraw when switching (same object, same undo history).
- [ ] **One full pass with a real mouse.** Much of the table was only ever driven through code (the Build Log's "Not verified: a real mouse…" lines). Palette drag and snap · a condition into a ⬡ hole · a reporter into an oval · Simple mode's above / replace / below · Shift+click, box select, group drag · pan and zoom · Delete · Undo / Redo · Go, Stop, Reset, Pause, Step ◀ / ▶, speeds · variables and the watcher · clones · say bubbles · sounds you can hear (editor focused, Game view not muted). *Done when:* each one works or has a bug written down.

## 2. Fix what is known to be wrong
All fixed in code on 2026-09-24 — see the [[08 Build Log/Build Log|Build Log]]. Nothing was run inside Unity, so each *Confirm in Unity* below belongs in section 1's pass.

- [x] **A saved program didn't run after a restart.** Only the in-game editor read a save back, when an object was clicked; `ObjectProgramRunner` ran its scene program, and an object first programmed in the game had no runner at all. **Fixed:** the runner runs its object's save, and the ticker gives runners back to saved objects that have none ([[09 Decisions/Decisions#ADR-031|ADR-031]]). *Confirm in Unity:* edit → quit → start again → the object runs the edited program without the editor being opened, and a clone of it runs the same one.
- [x] **Block ops could be stripped out of builds.** `OpTableBuilder` makes every op with `Activator.CreateInstance`, and nothing else names the op classes, so the linker can remove them above *Minimal* stripping ([[Welcome|TDD §14]]). **Fixed:** `Assets/Scripts/link.xml` keeps `Blocky.Runtime`, and `Blocky.Data`, which Newtonsoft fills by reflection. *Confirm in Unity:* a WebGL build at High stripping runs the demo.
- [x] **Save files were named after the object** — same-named objects shared a file, some names couldn't be saved, and a damaged or newer file threw in the editor. **Fixed:** saves are keyed by scene and hierarchy path with `[n]` for same-named siblings, every name is made file-safe, an unreadable file is renamed to `….unreadable` rather than overwritten and the editor says so, and a failed save never throws ([[09 Decisions/Decisions#ADR-031|ADR-031]]; 17 tests in `RuntimeProgramStorageTests`). *Confirm in Unity:* the tests pass there too.
- [x] **Build Settings listed the URP template's `SampleScene`.** **Fixed:** `BlockyDemo` is the one scene in the build.
- [x] **Without a `BlockyRuntimeTicker` nothing ran, and nothing said so.** **Fixed:** the first runner to start in Play mode, or the in-game editor, adds one on a new *Blocky Runtime* object and says so in the console. *Confirm in Unity:* delete `BlockyManager`'s ticker, press Play, and the demo still runs.
- [x] **Compiler warnings `UAL0010` / `UAL0013`.** **Fixed:** each non-test file with static state answers the analyzer with a pragma and a one-line reason, which stays quiet on every Unity version ([[09 Decisions/Decisions#ADR-032|ADR-032]]); `BlockyInput`'s statics are now reset every Play session like the rest. *Confirm in Unity:* a clean console after a recompile.
- [x] **Player Settings said `DefaultCompany`.** **Fixed:** company `revkae` and app id `com.revkae.blocky` (the name in `LICENSE`). If you publish under another name, change both before the first build anyone keeps.
- [x] **[[Home]] was out of date.** **Fixed:** its status, build order and known gaps now match what exists.

## 3. Choose the Unity version — before packaging
The project is on `6000.7.0a6`, an early alpha. 6.7 is in beta now, with its LTS expected at the end of 2026. The Asset Store takes new packages from 2022.3 up, and a package uploaded from 6.5 or later must support URP or HDRP. Most buyers — and most school IT — are on 6.0 LTS or 6.3 LTS.

What ties Blocky to 6.7 is the language pass: `Blocky.Localization` runs on 6.7's built-in localization module ([[09 Decisions/Decisions#ADR-030|ADR-030]]). The language files themselves don't need it — `LanguageFiles` already parses them with Newtonsoft. Whether anything else needs 6.7 is unknown until the project is compiled on an older version.

| Option | Work | For | Against |
|---|---|---|---|
| **A. Wait for 6.7 LTS** | Upgrade when it ships, test again | No porting | Only buyers on 6.7+; launch waits |
| **B. Support 6.3 LTS and up** (recommended) | On older versions, look words up in `LanguageFiles` directly; keep the module path behind a version define for 6.7+ if its plural forms matter | Far more buyers and schools; launch sooner | Porting, and testing on two versions |

- [ ] Choose A or B and record it as an ADR.
- [ ] Move the project off alpha 6 — to the current 6.7 beta (A) or to 6.3 LTS (B) — and run section 1 again there.

## 4. Ready for teachers and students
They use a finished app, never the Unity Editor, so most of this is the app around the workspace.

### Must have
- [ ] **A finished app to hand out**: Windows and Mac builds, and a WebGL build for Chromebooks and locked-down school PCs (nothing to install). In WebGL, saves live in the browser's IndexedDB — check they survive a reload and a browser restart.
- [ ] **A start screen**: choose a lesson (scene) and a language, then *Continue* or *Start fresh*. Today the app opens straight into the scene with the workspace hidden.
- [x] **An on-screen button to open the workspace.** Done 2026-09-24: a "Blocks [Tab]" button in the corner while it's closed, and "Tab hides this" in the title bar is now a button that closes it ([[09 Decisions/Decisions#ADR-037 — Touch goes through the last-used pointer; one finger pans the table, two pinch|ADR-037]]). *Still open:* until an object is chosen, say how — "click something in the scene".
- [ ] **Projects instead of hidden files.** New / Open / Save as, with names; *Reset this object* and *Reset the lesson* back to the original programs. Today each object saves itself to a hidden folder, and starting over means deleting files by hand.
- [ ] **Shared lab computers.** The next student opens the last student's work. Ask for a name at the start (or open a named project) so each student's work stays apart.
- [ ] **Hand in and hand out.** Export a project as one file and import it back, so a teacher can collect work, mark it, return it, and share a starter project.
- [ ] **Inputs a young child can fill in.** Color is typed as a hex code (`#FFFFFF` in `looks.change_color`) — give it a color picker or a short list of named colors. Object inputs want an exact name typed in — give them a dropdown of the scene's programmed objects (`BlockyObjects` already knows them).
- [ ] **Say what it needs.** Touch is built but untried on a device (below). Until it has been, the store page and the teacher guide say *mouse and keyboard recommended*; `when key pressed` and `key pressed?` need a keyboard either way.
- [ ] **Readable on classroom screens.** A UI size setting for projectors and small laptops; check the workspace at 1366×768. (Light, Dark and High contrast themes exist since 2026-09-24 — look at all three on a projector.)
- [ ] **Lesson material.** 5–10 short challenges on the demo scene (walk a triangle, jump on Space, count with a variable, a fountain of clones…), a one-page teacher guide, and a one-page block sheet for students — in English and Türkçe.

### Should have
- [ ] **Touch and tablets** (iPad, Android, touchscreen Chromebooks). **Built 2026-09-24, not yet tried on a device** ([[09 Decisions/Decisions#ADR-037 — Touch goes through the last-used pointer; one finger pans the table, two pinch|ADR-037]]): everything reads the last-used pointer (mouse, pen or finger) instead of `Mouse.current`; one finger on empty table pans, two pinch-zoom; the workspace opens and closes with on-screen buttons. *Try on:* an iPad or Android tablet (WebGL or a native build) and a touchscreen laptop — pick an object, drag a block out and snap it, pan, pinch, type a number, Go / Step, `when clicked` and `mouse down?`. *Known limit:* a finger on a palette block always drags it out — scroll the palette with the tabs or beside the blocks.
- [x] **Custom blocks** ("define" / procedures) — built 2026-09-24: `define`, `run` with inputs a/b/c, recursion ([[09 Decisions/Decisions#ADR-029|ADR-029]]). *Confirm in Unity:* the My Blocks tab, a ready-made `run [name]` appearing after you name a `define`, and a recursive program in Play mode.
- [ ] **2D scenes.** `when clicked` and choosing an object use `Physics.Raycast`, and collisions come from the 3D relay, so a flat 2D stage mostly doesn't work. Add the `Physics2D` paths.
- [ ] **Speed on school hardware.** Try the WebGL build on a cheap Chromebook with a long program open. The TDD's budgets ([[Welcome|TDD §11.1]]) have markers but were never measured.
- [ ] **A privacy note for schools**: what the app stores (project files, on the device) and what it sends. Unity Analytics is off in `UnityConnectSettings` today — keep it off, and check what the Unity player collects on its own. Schools ask before children use an app.

### Later
- [ ] Students add their own objects — today they can only program what was placed in the scene in Unity.
- [ ] Challenges the app can check ("reach the flag"). The pieces exist since 2026-09-24 — a `level complete` block and `BlockyEvents` ([[12 Game Code/Blocky from CSharp|Blocky from C#]]); what's missing is a challenge list in the app itself.
- [ ] More languages — one file each ([[10 Localization/Localization|Localization]]); pick by where you sell.

## 5. Ready for the Asset Store

### Package
- [ ] **One root folder.** Everything the buyer gets goes under `Assets/Blocky/`: scripts, `Resources/Blocks`, `Resources/Languages`, the demo scene and its programs, `BlockyPanelSettings` and the `UnityDefaultRuntimeTheme.tss` it points at, and the manual. Move inside Unity so `.meta` files and references follow. (`Resources` folders work at any depth.)
- [ ] **Leave out** the URP template's leftovers — `TutorialInfo/`, `Readme.asset`, `SampleScene.unity`, `Settings/`, `InputSystem_Actions.inputactions` — and this vault. Better still, move the vault out of `Assets/` (e.g. `Docs/` at the repo root) so it can never be packaged and Unity stops importing notes; reopen it in Obsidian from there.
- [ ] **One-step setup.** A *Blocky Workspace* prefab (`UIDocument` + `BlockyPanelSettings` + the six stylesheets + `BlockyInGamePanel` + `BlockyRuntimeTicker`) and menu items: *GameObject › Blocky › Workspace*, and *Add Blocky Program* for a selected object. Today a buyer copies the wiring from the [[08 Build Log/Demo Scene|Demo Scene]] by hand.
- [ ] **Declare the dependencies**: Input System, Newtonsoft JSON, URP (for the demo), and the localization module if you stay on 6.7. Blocky reads the Input System only — a project on the old Input Manager must set *Active Input Handling* to *Input System* or *Both*. Say so in the manual.
- [ ] **Render pipelines.** List only what's tested. `looks.change_color` sets `material.color`, which URP Lit and Built-in both answer to; the speech bubble looks for `Universal Render Pipeline/Unlit`, then `Sprites/Default`. Uploading from 6.5 or later requires URP or HDRP support.
- [ ] **Namespaces** — already right: everything is in `Blocky.*`.
- [ ] **Ship the tests** — they're a selling point. They sit behind `UNITY_INCLUDE_TESTS`; check they stay quiet in a project without the Test Framework.
- [ ] **Clean-install test.** A new, empty project on the lowest Unity version you support (URP, and Built-in if you list it) → import the `.unitypackage` → no errors or warnings → open the demo → Play works → Windows and WebGL builds work.
- [ ] **Asset Store Tools validator** — run it, fix everything it reports, and upload with Asset Store Tools from your chosen Unity version.

### Legal and account
- [ ] **Make the GitHub repo private before selling.** It is public under MIT, so anyone may download Blocky free — and resell it. Copies taken before you close it stay MIT; the repo is new and has no forks, so the exposure is small.
- [ ] **Leave the MIT `LICENSE` out of the package.** Buyers get the Asset Store EULA; other license terms need Unity's written agreement.
- [ ] **Third-Party Notices.** Nothing third-party is bundled today — sounds are generated, the bubble uses Unity's built-in font, Newtonsoft comes as a Unity package. Add `Third-Party Notices.txt` the day that changes.
- [ ] **Disclose AI assistance.** Blocky was built with AI coding assistance, so the listing must say so in its *AI description* field — plainly, with what was added or changed by hand.
- [ ] **Trademarks.** Keep *Scratch*, *Delightex*, *CoSpaces*, *CoBlocks* and *Blockly* out of the title, keywords, images and on-screen text. The Free-mode tooltip says "The Scratch table" today (`mode.free.tooltip` in `en.json`, "Scratch masası" in `tr.json`). Code comments and this vault are fine.
- [ ] **A title people can find.** "Blocky" alone is crowded on the store (*Blocky Vehicle Pack*, *Blocky Level Generator*…) and one letter from Google's *Blockly*. Something like *Blocky – Visual Block Coding for Education*.
- [ ] **Publisher account** in the Publisher Portal: profile, payout profile (PayPal monthly or bank transfer quarterly), tax form (W-8BEN from outside the US). Unity keeps 30% of each sale.

### Manual and store page
- [ ] **A manual inside the package** (PDF or Markdown in `Assets/Blocky/Documentation/`) — required for code packages; this vault is design notes, not a manual:
  - Quick start in five minutes: import → drop in the prefab → give an object a program → Play → Tab.
  - Using the workspace — most of it is already in [[08 Build Log/Demo Scene#In-game program editor|Demo Scene]].
  - Block reference — from [[06 Block Catalog/Block Catalog|Block Catalog]].
  - Adding a block: one `BlockDefinition` asset + one `[BlockExecutor]` class — what developers are buying. `Blocky › New Block…` makes both, and the words ([[09 Decisions/Decisions#ADR-036 — New blocks live in the project's own folder, and a missing op stops one script, not the game|ADR-036]]).
  - Adding a language — from [[10 Localization/Localization|Localization]].
  - Saving and loading programs, and the scripting entry points (`ObjectProgramRunner`, `BlockyRuntime.Playback`, `RuntimeProgramStorage`, `BlockyEvents` — from [[12 Game Code/Blocky from CSharp|Blocky from C#]]).
  - Known limits: Unity version, mouse and keyboard, 3D.
- [ ] **Version and changelog.** `bundleVersion` is `0.1.0`; ship `1.0.0` with a changelog.
- [ ] **A support address** (email, Discord, or a public issues-only repo) and a reply time you can keep — reviews follow support.
- [ ] **Store text**: summary, features, requirements (Unity version, Input System, pipelines, platforms tested), what's *not* included, the AI disclosure. Category: the competitor sits in *Templates › Systems*; *Tools › Visual Scripting* fits too.
- [ ] **Key images** (24-bit PNG, no alpha, exact sizes): icon 160×160, card 420×280, cover 1950×1300, social 1200×630.
- [ ] **5–10 screenshots** (2400×1600 suggested, at least 1200 wide): palette and table, Simple mode's numbered column, stepping with the running block lit, advice badges, the Turkish workspace, a 3D scene running.
- [ ] **A 1–2 minute video**: build a script, press Go, step through it, fix a mistake with the advice.
- [ ] **A playable WebGL demo** (itch.io or your own site) — the main competitor has one.
- [ ] **Price.** The one direct competitor, *Blocks Engine 2* (MeadowGames), is about $35 (seen at $17.50 on sale), has years of reviews, and was updated in August 2026. Launch at **$29.99**; move to **$39.99–$49.99** once touch, 2D and custom blocks are in and reviews arrive. A free *Lite* edition with a smaller block set is a good way for a new publisher to be found. Paid assets start at $4.99.

## 6. Submit, then keep it alive
- [ ] Upload with Asset Store Tools and submit. A new package takes about ten business days to review; updates about two.
- [ ] If it's rejected, fix every reason given and resubmit.
- [ ] After launch: answer questions and reviews quickly; plan 1.1 (touch, 2D, custom blocks); test each new Unity release — 6.7 LTS at the end of 2026, then Unity 7, expected in 2027 on the new CoreCLR runtime (worth an early run of the reflection-built op table).

## Done when
**Asset Store:** a clean import into an empty project on the lowest supported Unity version shows no errors or warnings, runs the demo, and builds for Windows and WebGL; the validator passes; manual, images, video and store text are in; the repo is private.

**Classroom:** a teacher installs the app (or opens it in a browser); a student picks a lesson, builds and runs a program with the mouse, saves it under their name, finds it still running the next day, and hands it in as a file — in English or Türkçe.

## Sources (checked 2026-09-24)
- [Asset Store Submission Guidelines](https://assetstore.unity.com/publishing/submission-guidelines)
- [Asset Store publisher updates](https://assetstore.unity.com/publishing/release-updates) — minimum Unity version, the URP/HDRP rule
- [How long will it take for my Asset to be approved?](https://support.unity.com/hc/en-us/articles/210569723-How-long-will-it-take-for-my-Asset-to-be-approved)
- [Can I publish and sell content generated with AI on the Asset Store?](https://support.unity.com/hc/en-us/articles/16456407029524-Can-I-publish-and-sell-content-generated-with-AI-on-the-Asset-Store)
- [Asset Store key images and screenshots](https://hotpot.ai/blog/unity-screenshots-and-key-images)
- [Blocks Engine 2 on the Asset Store](https://assetstore.unity.com/packages/templates/systems/blocks-engine-2-201602)
- [Unity 6.7 Beta announcement](https://discussions.unity.com/t/unity-6-7-beta-is-now-available/1736830)
