using System;
using System.IO;
using System.Text.RegularExpressions;
using Blocky.Data;
using Blocky.Data.Serialization;
using Blocky.Runtime.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Blocky.Runtime.Tests
{
    /// <summary>
    /// The in-game editor's save files (ADR-031): one per object, keyed by scene and hierarchy path, safe on every file
    /// system, an unreadable file set aside rather than overwritten, and a saved program run the next time the game starts.
    /// Every test saves into its own temporary folder, never the player's.
    /// </summary>
    public class RuntimeProgramStorageTests
    {
        private string _root;
        private GameObject _holder;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "BlockyStorageTests_" + Guid.NewGuid().ToString("N"));
            RuntimeProgramStorage.RootOverride = _root;
            _holder = new GameObject("StorageTestHolder");
            _holder.SetActive(false); // runners added under it must not start in edit mode
        }

        [TearDown]
        public void TearDown()
        {
            RuntimeProgramStorage.RootOverride = null;
            Object.DestroyImmediate(_holder);
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private GameObject Child(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : _holder.transform, false);
            return go;
        }

        private static ObjectProgram Program(string stackId) => new()
        {
            targetObjectUid = "obj",
            stacks = new[] { new BlockStack { id = stackId, triggerBlockType = "event.when_play_clicked" } }
        };

        private string Key(params string[] parts) =>
            RuntimeProgramStorage.SceneKey(_holder.scene) + "/" + string.Join("/", parts);

        [Test]
        public void SafeName_LeavesOrdinaryNamesAlone_TurkishIncluded()
        {
            Assert.AreEqual("Cube", RuntimeProgramStorage.SafeName("Cube"));
            Assert.AreEqual("Kırmızı Küp (2)", RuntimeProgramStorage.SafeName("Kırmızı Küp (2)"));
        }

        [Test]
        public void SafeName_EscapesWhatSomeFileSystemRefuses_AndItsOwnMarks()
        {
            Assert.AreEqual("a%2Fb", RuntimeProgramStorage.SafeName("a/b"));
            Assert.AreEqual("Why%3F", RuntimeProgramStorage.SafeName("Why?"));
            Assert.AreEqual("C%3A%5Cx%2A", RuntimeProgramStorage.SafeName("C:\\x*"));
            Assert.AreEqual("100%25", RuntimeProgramStorage.SafeName("100%"));
            Assert.AreEqual("Cube%5B1%5D", RuntimeProgramStorage.SafeName("Cube[1]"), "brackets are the key's own mark for same-named siblings");
            Assert.AreEqual("tab%09", RuntimeProgramStorage.SafeName("tab\t"));
        }

        [Test]
        public void SafeName_NeverEndsInADotOrSpace_AndIsNeverDotDot()
        {
            Assert.AreEqual("end%2E", RuntimeProgramStorage.SafeName("end."));
            Assert.AreEqual("end%20", RuntimeProgramStorage.SafeName("end "));
            Assert.AreEqual(".%2E", RuntimeProgramStorage.SafeName(".."));
            Assert.AreEqual("%2E", RuntimeProgramStorage.SafeName("."));
            Assert.AreEqual("%", RuntimeProgramStorage.SafeName(""));
        }

        [Test]
        public void SafeName_EscapesWindowsDeviceNames_WithOrWithoutAnExtension()
        {
            Assert.AreEqual("%43ON", RuntimeProgramStorage.SafeName("CON"));
            Assert.AreEqual("%6Eul", RuntimeProgramStorage.SafeName("nul"));
            Assert.AreEqual("%63om1.txt", RuntimeProgramStorage.SafeName("com1.txt"));
            Assert.AreEqual("CONSOLE", RuntimeProgramStorage.SafeName("CONSOLE"));
        }

        [Test]
        public void SafeName_CutsVeryLongNames_KeepingThemApart()
        {
            var a = RuntimeProgramStorage.SafeName(new string('a', 200));
            var b = RuntimeProgramStorage.SafeName(new string('a', 199) + "b");

            Assert.LessOrEqual(a.Length, 80);
            Assert.AreNotEqual(a, b);
            Assert.AreEqual(a, RuntimeProgramStorage.SafeName(new string('a', 200)), "the same name always gives the same file");
        }

        [Test]
        public void KeyFor_IsTheSceneThenThePathDownTheHierarchy()
        {
            var robot = Child("Robot");
            var arm = Child("Arm", robot.transform);

            Assert.AreEqual(Key("StorageTestHolder", "Robot", "Arm"), RuntimeProgramStorage.KeyFor(arm));
        }

        [Test]
        public void KeyFor_TellsSameNamedSiblingsApart_CaseInsensitively()
        {
            var first = Child("Cube");
            var other = Child("Ball");
            var second = Child("cube");
            var third = Child("Cube");

            Assert.AreEqual(Key("StorageTestHolder", "Cube"), RuntimeProgramStorage.KeyFor(first));
            Assert.AreEqual(Key("StorageTestHolder", "Ball"), RuntimeProgramStorage.KeyFor(other));
            Assert.AreEqual(Key("StorageTestHolder", "cube[1]"), RuntimeProgramStorage.KeyFor(second));
            Assert.AreEqual(Key("StorageTestHolder", "Cube[2]"), RuntimeProgramStorage.KeyFor(third));
        }

        [Test]
        public void KeyFor_GivesAnAwkwardNameAFileThatWorks()
        {
            var key = RuntimeProgramStorage.KeyFor(Child("a/b: why?"));

            Assert.IsTrue(RuntimeProgramStorage.TrySave(key, Program("stk_1"), out var problem), problem);
            Assert.AreEqual(SavedProgramStatus.Loaded, RuntimeProgramStorage.TryLoad(key, out _));
        }

        [Test]
        public void SaveThenLoad_RoundTripsTheProgram()
        {
            var key = Key("Solo");

            Assert.AreEqual(SavedProgramStatus.None, RuntimeProgramStorage.TryLoad(key, out var nothing));
            Assert.IsNull(nothing);

            Assert.IsTrue(RuntimeProgramStorage.TrySave(key, Program("stk_saved"), out _));
            Assert.AreEqual(SavedProgramStatus.Loaded, RuntimeProgramStorage.TryLoad(key, out var loaded));
            Assert.AreEqual("stk_saved", loaded.stacks[0].id);
            Assert.IsTrue(RuntimeProgramStorage.HasSavesFor(_holder.scene));
        }

        [Test]
        public void AnUnreadableFile_IsSetAside_AndTheNextSaveDoesNotOverwriteIt()
        {
            var key = Key("Broken");
            var path = RuntimeProgramStorage.PathFor(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "this is not a program");

            LogAssert.Expect(LogType.Warning, new Regex("couldn't be read"));
            Assert.AreEqual(SavedProgramStatus.Unreadable, RuntimeProgramStorage.TryLoad(key, out var program));
            Assert.IsNull(program);
            Assert.IsFalse(File.Exists(path), "moved out of the way");

            var kept = Directory.GetFiles(Path.GetDirectoryName(path), "*.unreadable");
            Assert.AreEqual(1, kept.Length);

            Assert.IsTrue(RuntimeProgramStorage.TrySave(key, Program("stk_new"), out _));
            Assert.AreEqual("this is not a program", File.ReadAllText(kept[0]), "the student's old file is still there, untouched");
        }

        [Test]
        public void AFileFromANewerBlocky_IsSetAside_NotHalfLoaded()
        {
            var key = Key("Future");
            var path = RuntimeProgramStorage.PathFor(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ \"schemaVersion\": " + (ProgramSchema.CurrentVersion + 1) + ", \"stacks\": [] }");

            LogAssert.Expect(LogType.Warning, new Regex("couldn't be read"));
            Assert.AreEqual(SavedProgramStatus.Unreadable, RuntimeProgramStorage.TryLoad(key, out _));
            Assert.AreEqual(1, Directory.GetFiles(Path.GetDirectoryName(path), "*.unreadable").Length);
        }

        [Test]
        public void ASaveThatCantBeWritten_SaysSo_InsteadOfThrowing()
        {
            Directory.CreateDirectory(_root);
            var blocker = Path.Combine(_root, "a-file-not-a-folder");
            File.WriteAllText(blocker, "");
            RuntimeProgramStorage.RootOverride = Path.Combine(blocker, "saves");

            LogAssert.Expect(LogType.Warning, new Regex("couldn't save"));
            Assert.IsFalse(RuntimeProgramStorage.TrySave(Key("Anything"), Program("stk_1"), out var problem));
            Assert.IsFalse(string.IsNullOrEmpty(problem));
        }

        [Test]
        public void PathFor_RefusesKeysThatWouldLeaveTheSaveFolder()
        {
            Assert.Throws<ArgumentException>(() => RuntimeProgramStorage.PathFor("Scene/../../escape"));
            Assert.Throws<ArgumentException>(() => RuntimeProgramStorage.PathFor("Scene//gap"));
            Assert.Throws<ArgumentException>(() => RuntimeProgramStorage.PathFor(""));
        }

        [Test]
        public void ARunner_RunsItsObjectsSavedProgram_InsteadOfItsSceneProgram()
        {
            var go = Child("Saved");
            var sceneAsset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            sceneAsset.Save(Program("stk_scene"));
            var runner = go.AddComponent<ObjectProgramRunner>();
            SetSerializedAsset(runner, sceneAsset); // as the scene would have it: not handed over by code

            RuntimeProgramStorage.TrySave(RuntimeProgramStorage.KeyFor(go), Program("stk_saved"), out _);

            Assert.AreEqual("stk_saved", runner.LoadProgram().stacks[0].id);
            Assert.AreEqual(SavedProgramStatus.Loaded, runner.SavedProgram);
            Assert.AreEqual("stk_saved", runner.ProgramAsset.Load().stacks[0].id, "a clone copies the runner's asset, so it runs the saved program too");
        }

        [Test]
        public void AProgramHandedOverByCode_WinsOverASave()
        {
            var go = Child("Handed");
            RuntimeProgramStorage.TrySave(RuntimeProgramStorage.KeyFor(go), Program("stk_saved"), out _);

            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            asset.Save(Program("stk_code"));
            var runner = go.AddComponent<ObjectProgramRunner>();
            runner.SetProgramAsset(asset);

            Assert.AreEqual("stk_code", runner.LoadProgram().stacks[0].id);
            Assert.AreEqual(SavedProgramStatus.None, runner.SavedProgram);
        }

        [Test]
        public void ARunnerWithAnUnreadableSave_RunsItsSceneProgram_AndSaysWhy()
        {
            var go = Child("Damaged");
            var sceneAsset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            sceneAsset.Save(Program("stk_scene"));
            var runner = go.AddComponent<ObjectProgramRunner>();
            SetSerializedAsset(runner, sceneAsset);

            var path = RuntimeProgramStorage.PathFor(RuntimeProgramStorage.KeyFor(go));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ broken");

            LogAssert.Expect(LogType.Warning, new Regex("couldn't be read"));
            Assert.AreEqual("stk_scene", runner.LoadProgram().stacks[0].id);
            Assert.AreEqual(SavedProgramStatus.Unreadable, runner.SavedProgram);

            var edited = ScriptableObject.CreateInstance<BlockProgramAsset>();
            edited.Save(Program("stk_edited"));
            runner.SetProgramAsset(edited); // what the in-game editor does after an edit
            Assert.AreEqual(SavedProgramStatus.None, runner.SavedProgram, "once an edit runs, the old file's problem is no longer news");
        }

        [Test]
        public void AttachToSavedObjects_GivesARunnerBackOnlyToSavedObjectsWithoutOne()
        {
            var programmedInGame = Child("ProgrammedInGame");
            var untouched = Child("Untouched");
            var nested = Child("Nested", Child("Parent").transform);
            var alreadyRunning = Child("AlreadyRunning");
            var existing = alreadyRunning.AddComponent<ObjectProgramRunner>();

            foreach (var go in new[] { programmedInGame, nested, alreadyRunning })
                RuntimeProgramStorage.TrySave(RuntimeProgramStorage.KeyFor(go), Program("stk_" + go.name), out _);

            Assert.AreEqual(2, ObjectProgramRunner.AttachToSavedObjects(_holder.scene));
            Assert.IsNotNull(programmedInGame.GetComponent<ObjectProgramRunner>());
            Assert.IsNotNull(nested.GetComponent<ObjectProgramRunner>());
            Assert.IsNull(untouched.GetComponent<ObjectProgramRunner>());
            Assert.AreSame(existing, alreadyRunning.GetComponent<ObjectProgramRunner>(), "an object's own runner is left alone");
            Assert.AreEqual(0, ObjectProgramRunner.AttachToSavedObjects(_holder.scene), "running it again adds nothing");
        }

        /// <summary>Puts an asset in the runner's serialized field the way a scene does, without <see cref="ObjectProgramRunner.SetProgramAsset"/>.</summary>
        private static void SetSerializedAsset(ObjectProgramRunner runner, BlockProgramAsset asset)
        {
            var field = typeof(ObjectProgramRunner).GetField("programAsset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, "ObjectProgramRunner.programAsset was renamed; update this test");
            field.SetValue(runner, asset);
        }
    }
}
