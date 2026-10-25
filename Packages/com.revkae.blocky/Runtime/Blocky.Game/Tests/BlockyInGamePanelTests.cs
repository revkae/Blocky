using System;
using System.Collections.Generic;
using Blocky.Data;
using Blocky.Editor;
using Blocky.Localization;
using Blocky.Runtime;
using Blocky.Runtime.Persistence;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Blocky.Game.Tests
{
    /// <summary>
    /// The workspace as a game hosts it (ADR-041): opened, closed and pointed at an object from code, with the game
    /// told about all three; picking by a click switched off or given rules; no hotkey and no corner button; and
    /// edits saved through the game's own storage. Built from the shipped prefab, outside Play mode — so each test
    /// builds the workspace and runs its frames by hand.
    /// </summary>
    public class BlockyInGamePanelTests
    {
        private const string PrefabPath = "Packages/com.revkae.blocky/Runtime/Prefabs/Blocky Workspace.prefab";

        /// <summary>A game's storage: programs kept in memory by object, and a record of what was saved.</summary>
        private sealed class FakeStorage : IProgramStorage
        {
            public readonly Dictionary<GameObject, ObjectProgram> Programs = new();
            public readonly HashSet<GameObject> Unreadable = new();
            public readonly List<GameObject> Saves = new();

            public bool HasSavesFor(Scene scene) => true;

            public bool Exists(GameObject owner) => Programs.ContainsKey(owner);

            public SavedProgramStatus TryLoad(GameObject owner, out ObjectProgram program)
            {
                program = null;
                if (Unreadable.Contains(owner)) return SavedProgramStatus.Unreadable;
                return Programs.TryGetValue(owner, out program) ? SavedProgramStatus.Loaded : SavedProgramStatus.None;
            }

            public bool TrySave(GameObject owner, ObjectProgram program, out string problem)
            {
                Saves.Add(owner);
                Programs[owner] = program;
                problem = null;
                return true;
            }
        }

        private IDisposable _english;
        private FakeStorage _storage;
        private GameObject _workspace;
        private BlockyInGamePanel _panel;
        private GameObject _cube;
        private GameObject _other;
        private readonly List<string> _heard = new();

        [SetUp]
        public void SetUp()
        {
            _english = BlockyLanguages.Override(BlockyLanguages.SourceLanguage); // the tests read words off the workspace
            _storage = new FakeStorage();
            BlockyStorage.Current = _storage; // never the player's real save folder

            _cube = new GameObject("PanelTestCube");
            _other = new GameObject("PanelTestOther");
            _workspace = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            _panel = _workspace.GetComponent<BlockyInGamePanel>();

            _heard.Clear();
            _panel.Opened += () => _heard.Add("opened");
            _panel.Closed += () => _heard.Add("closed");
            _panel.TargetChanged += (before, now) => _heard.Add($"target {Name(before)} -> {Name(now)}");
        }

        [TearDown]
        public void TearDown()
        {
            _panel.Release();
            foreach (var go in new[] { _workspace, _cube, _other })
                if (go != null) Object.DestroyImmediate(go);

            ParamFieldFactory.ObjectChoices = null;
            BlockyStorage.Current = null;
            BlockyRuntime.Reset();
            _english.Dispose();
        }

        /// <summary>A destroyed object still has a reference to tell it by; it reads as "(destroyed)".</summary>
        private static string Name(GameObject go) => ReferenceEquals(go, null) ? "null" : go == null ? "(destroyed)" : go.name;

        private VisualElement Root => _workspace.GetComponent<UIDocument>().rootVisualElement;

        private static ObjectProgram OneScript() => new()
        {
            stacks = new[]
            {
                new BlockStack
                {
                    id = "stk_1",
                    triggerBlockType = "event.when_go_clicked",
                    sequence = new[]
                    {
                        new BlockNode
                        {
                            id = "n1",
                            blockType = "motion.point_towards",
                            parameters = new[] { new BlockParam { key = "object", kind = ParamKind.ObjectRef, text = "me" } }
                        }
                    }
                }
            }
        };

        // ---- open, close, target (#1) --------------------------------------------------------------------

        [Test]
        public void Open_ShowsTheWorkspaceOnTheObject_AndSaysSo_OpenedFirst()
        {
            _panel.Build();
            Assert.IsFalse(_panel.IsOpen);
            Assert.IsNull(_panel.Target);

            _panel.Open(_cube);

            Assert.IsTrue(_panel.IsOpen);
            Assert.AreSame(_cube, _panel.Target);
            CollectionAssert.AreEqual(new[] { "opened", "target null -> PanelTestCube" }, _heard);
            Assert.IsNotNull(_cube.GetComponent<ObjectProgramRunner>(), "an object given to the workspace can run a program");
        }

        [Test]
        public void Open_WorksBeforeTheWorkspaceHasWoken()
        {
            _panel.Open(_cube); // no Build: a game's Awake can run before the workspace's

            Assert.IsTrue(_panel.IsOpen);
            Assert.AreSame(_cube, _panel.Target);
        }

        [Test]
        public void OpeningAnOpenWorkspace_OnTheObjectItShows_SaysNothing()
        {
            _panel.Open(_cube);
            var store = _panel.Store;
            _heard.Clear();

            _panel.Open(_cube);
            _panel.Open();

            CollectionAssert.IsEmpty(_heard);
            Assert.AreSame(store, _panel.Store, "and the table wasn't rebuilt: the undo history is still there");
        }

        [Test]
        public void Open_WithAnotherObject_SwitchesToIt_WithoutSayingOpenedAgain()
        {
            _panel.Open(_cube);
            _heard.Clear();

            _panel.Open(_other);

            Assert.AreSame(_other, _panel.Target);
            CollectionAssert.AreEqual(new[] { "target PanelTestCube -> PanelTestOther" }, _heard);
        }

        [Test]
        public void Close_SaysSoOnce()
        {
            _panel.Open(_cube);
            _heard.Clear();

            _panel.Close();
            _panel.Close();

            Assert.IsFalse(_panel.IsOpen);
            CollectionAssert.AreEqual(new[] { "closed" }, _heard);
            Assert.AreSame(_cube, _panel.Target, "closing keeps the object: opening again shows its program");
        }

        [Test]
        public void AHandler_MayCloseOrRetarget_FromInsideTheEvent()
        {
            _panel.Build();
            _panel.Opened += () => _panel.SetTarget(_other);
            _panel.TargetChanged += (_, now) => { if (now == _other) _panel.Close(); };

            Assert.DoesNotThrow(() => _panel.Open(_cube));

            Assert.IsFalse(_panel.IsOpen);
            Assert.AreSame(_other, _panel.Target);
        }

        [Test]
        public void SetTarget_Null_ClearsTheTable()
        {
            _panel.Build();
            _panel.SetTarget(_cube);
            Assert.IsNotNull(Root.Q<ProgramCanvasView>());
            _heard.Clear();

            _panel.SetTarget(null);

            Assert.IsNull(_panel.Target);
            Assert.IsNull(_panel.Store);
            Assert.IsNull(Root.Q<ProgramCanvasView>(), "the table is gone");
            Assert.AreEqual(0, _panel.BlocksUsed);
            Assert.AreEqual(BlockyText.Get("status.pick_object"), Root.Q<Label>(className: "blocky-ingame-status__label").text);
            CollectionAssert.AreEqual(new[] { "target PanelTestCube -> null" }, _heard);

            _panel.SetTarget(null);
            Assert.AreEqual(1, _heard.Count, "clearing a clear table is not a change");
            Assert.DoesNotThrow(_panel.Tick, "a frame with no object does nothing");
        }

        [Test]
        public void SetTarget_TheObjectAlreadyThere_ReadsItsProgramAgain_AndIsNotAChange()
        {
            _panel.Build();
            _panel.SetTarget(_cube);
            Assert.AreEqual(0, _panel.BlocksUsed);
            _heard.Clear();

            _cube.GetComponent<ObjectProgramRunner>().SetProgram(OneScript()); // the game replaced the program from its backend
            _panel.SetTarget(_cube);

            Assert.AreEqual(1, _panel.BlocksUsed, "the table shows what the runner now runs");
            CollectionAssert.IsEmpty(_heard);
        }

        [Test]
        public void ATargetDestroyedUnderTheWorkspace_IsClearedOnTheNextFrame_AndTheGameIsToldWhich()
        {
            _panel.Open(_cube);
            _heard.Clear();

            Object.DestroyImmediate(_cube);
            _panel.Tick();

            Assert.IsTrue(ReferenceEquals(_panel.Target, null), "really cleared, not just a destroyed object that reads as null");
            Assert.IsNull(Root.Q<ProgramCanvasView>());
            CollectionAssert.AreEqual(new[] { "target (destroyed) -> null" }, _heard);
        }

        [Test]
        public void IsPointerOverWorkspace_IsFalseEverywhere_WhileClosedWithNoCornerButton()
        {
            _panel.ShowOpener = false;
            _panel.Build();

            Assert.IsFalse(_panel.IsPointerOverWorkspace(new Vector2(8f, Screen.height - 8f)), "where the corner button would be");
            Assert.IsFalse(_panel.IsPointerOverWorkspace(new Vector2(Screen.width / 2f, Screen.height / 2f)));
        }

        // ---- picking by a click (#2) ---------------------------------------------------------------------

        [Test]
        public void APressOnAnObject_PutsItOnTheTable_ByDefault()
        {
            _panel.Build();

            _panel.PickFromPress(_cube);

            Assert.AreSame(_cube, _panel.Target);
            CollectionAssert.AreEqual(new[] { "target null -> PanelTestCube" }, _heard);
        }

        [Test]
        public void WithPickingOff_APressChangesNothing_AndGivesTheObjectNoRunner()
        {
            _panel.PickObjectsOnClick = false;
            _panel.Build();

            _panel.PickFromPress(_cube);

            Assert.IsNull(_panel.Target);
            Assert.IsNull(_cube.GetComponent<ObjectProgramRunner>(), "the game's terrain and props stay as they are");
            CollectionAssert.IsEmpty(_heard);
        }

        [Test]
        public void AResolver_CanIgnoreAPress_OrSendItToAnotherObject()
        {
            _panel.Build();
            _other.transform.SetParent(_cube.transform, false); // a model: the collider is on a child

            _panel.PickResolver = _ => null;
            _panel.PickFromPress(_other);
            Assert.IsNull(_panel.Target, "null: not something to program");

            _panel.PickResolver = hit => hit.transform.root.gameObject;
            _panel.PickFromPress(_other);
            Assert.AreSame(_cube, _panel.Target, "the model's root is what gets programmed");
            Assert.IsNull(_other.GetComponent<ObjectProgramRunner>());
        }

        [Test]
        public void APressOnTheObjectAlreadyOnTheTable_KeepsItsTable()
        {
            _panel.Build();
            _panel.PickFromPress(_cube);
            var store = _panel.Store;

            _panel.PickFromPress(_cube);

            Assert.AreSame(store, _panel.Store);
        }

        // ---- no hotkey, no corner button (#3) ------------------------------------------------------------

        [Test]
        public void WithNoToggleKey_AFrameReadsNoKey_AndTheKeyCapsAreGone()
        {
            Assume.That(Keyboard.current, Is.Not.Null, "this machine has no keyboard to read");
            _panel.ToggleKey = Key.None;
            _panel.Build();

            Assert.DoesNotThrow(_panel.Tick); // Keyboard[Key.None] throws: there is no such control
            Assert.IsFalse(_panel.IsOpen);
            foreach (var cap in Root.Query<Label>(className: "blocky-ingame-key").ToList())
                Assert.AreEqual(DisplayStyle.None, cap.style.display.value);
            Assert.AreEqual(BlockyText.Get("workspace.close"), Root.Q<Label>(className: "blocky-ingame-keyhint__label").text);
        }

        [Test]
        public void WithAToggleKey_TheKeyCapsNameIt()
        {
            _panel.Build();

            var caps = Root.Query<Label>(className: "blocky-ingame-key").ToList();
            Assert.AreEqual(2, caps.Count, "one on the title bar's close button, one on the corner button");
            foreach (var cap in caps)
            {
                Assert.AreEqual("Tab", cap.text);
                Assert.AreEqual(DisplayStyle.Flex, cap.style.display.value);
            }
        }

        [Test]
        public void WithNoCornerButton_TheClosedWorkspaceIsNotDrawn_AndComesBackWhenOpened()
        {
            _panel.ShowOpener = false;
            _panel.Build();
            Assert.AreEqual(DisplayStyle.None, Root.style.display.value, "nothing of it can be seen or pressed");

            _panel.Open();
            Assert.AreEqual(DisplayStyle.Flex, Root.style.display.value);

            _panel.Close();
            Assert.AreEqual(DisplayStyle.None, Root.style.display.value);

            _panel.ShowOpener = true;
            Assert.AreEqual(DisplayStyle.Flex, Root.style.display.value, "the corner button is the closed workspace");
        }

        // ---- tooltips -------------------------------------------------------------------------------------

        [Test]
        public void EveryControlOnTheBars_AndBesideTheTable_SaysWhatItDoes()
        {
            _panel.Open(_cube);
            _panel.Tick(); // Pause/Resume is worded with the run state

            var controls = new List<VisualElement>
            {
                Root.Q(className: "blocky-ingame-status"),     // the object's name
                Root.Q(className: "blocky-runbar__speed"),     // the "Speed" caption
                Root.Q(className: "blocky-zoom-controls")      // the zoom percentage
            };
            foreach (var bar in new[] { "blocky-ingame-topbar", "blocky-runbar", "blocky-zoom-controls", "blocky-table-tools" })
                controls.AddRange(Root.Q(className: bar).Query<Button>().ToList());

            var unexplained = new List<string>();
            foreach (var control in controls)
                if (string.IsNullOrEmpty(control.tooltip) || control.tooltip.Contains(".tooltip")) // a word missing from the file reads as its key
                    unexplained.Add($".{string.Join(".", control.GetClasses())} \"{(control as Button)?.text}\": \"{control.tooltip}\"");

            Assert.Greater(controls.Count, 15, "the bars are all there");
            CollectionAssert.IsEmpty(unexplained);
        }

        [Test]
        public void TheWorkspace_DrawsItsTooltips_ForTheGamesPanelDrawsNone()
        {
            _panel.Build();

            var layer = Root.Q<TooltipLayer>();
            Assert.IsNotNull(layer);
            Assert.AreSame(Root, layer.hierarchy.parent, "it answers for the whole workspace");
            Assert.AreEqual(Root.IndexOf(layer) + 1, Root.IndexOf(Root.Q<DragLayer>()), "under the dragged block, which stays on top");
        }

        [Test]
        public void ClosingTheWorkspace_PutsAShownTooltipAway()
        {
            _panel.Open(_cube);
            var layer = Root.Q<TooltipLayer>();
            var go = Root.Q<Button>(className: "blocky-runbar__go");
            layer.Tick(go, false, 0f);
            layer.Tick(go, false, TooltipLayer.ShowDelay);
            Assume.That(layer.IsShown);

            _panel.Close();
            _panel.Tick();

            Assert.IsFalse(layer.IsShown);
        }

        // ---- the game's own storage (#4) -----------------------------------------------------------------

        [Test]
        public void AnEdit_IsSavedThroughTheGamesStorage_ForTheObjectOnTheTable()
        {
            _panel.Open(_cube);
            CollectionAssert.IsEmpty(_storage.Saves, "opening an object saves nothing");

            _panel.Store.Apply(new CreateStack(new BlockStack { id = "stk_new", triggerBlockType = "event.when_go_clicked" }));

            CollectionAssert.AreEqual(new[] { _cube }, _storage.Saves);
            Assert.AreEqual("stk_new", _storage.Programs[_cube].stacks[0].id);
            Assert.AreEqual("stk_new", _cube.GetComponent<ObjectProgramRunner>().LoadProgram().stacks[0].id, "and the runner has the edit");
        }

        [Test]
        public void TheWorkspace_ShowsTheProgramTheGamesStorageHas()
        {
            _storage.Programs[_cube] = OneScript();

            _panel.Open(_cube);

            Assert.AreEqual(1, _panel.BlocksUsed);
        }

        [Test]
        public void AnUnreadableSave_IsSaidUnderTheTable()
        {
            _storage.Unreadable.Add(_cube);

            _panel.Open(_cube);

            var notice = Root.Q<Label>(className: "blocky-advice--problem");
            Assert.IsNotNull(notice);
            Assert.AreEqual(BlockyText.Format("storage.unreadable", "PanelTestCube"), notice.text);
        }

        // ---- object inputs as a dropdown (#9) ------------------------------------------------------------

        [Test]
        public void WithChoicesFromTheGame_AnObjectInputOnTheTable_IsADropdown()
        {
            ParamFieldFactory.ObjectChoices = () => new[] { "PanelTestCube", "PanelTestOther" };
            _storage.Programs[_cube] = OneScript();

            _panel.Open(_cube);

            var table = Root.Q<ProgramCanvasView>();
            var dropdown = table.Q<DropdownField>();
            Assert.IsNotNull(dropdown, "the object input of \"point towards\"");
            CollectionAssert.AreEqual(new[] { "me", "PanelTestCube", "PanelTestOther" }, dropdown.choices);
            Assert.AreEqual(0, table.Query<TextField>().ToList().Count, "and no box to type a name into");
        }
    }
}
