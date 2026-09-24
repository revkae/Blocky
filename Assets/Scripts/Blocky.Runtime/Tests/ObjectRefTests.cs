using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>The blocks where one object talks about another, and the name resolver behind them.</summary>
    public class ObjectRefTests
    {
        private GameObject _target;
        private GameObject _ball;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("BlockyRefTarget");
            _ball = new GameObject("Ball");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            if (_ball != null) Object.DestroyImmediate(_ball);
            BlockyRuntime.Reset();
        }

        // ---- the resolver ----------------------------------------------------------------------------

        [Test]
        public void ARegisteredObject_IsFoundByName_WithoutSearchingTheScene()
        {
            var objects = new BlockyObjects();
            objects.Register(_ball);

            Assert.AreSame(_ball, objects.Find("Ball", _target));
            Assert.AreSame(_ball, objects.Find(" ball ", _target), "names are trimmed and case-insensitive");
        }

        [Test]
        public void MeAndAnEmptyName_BothMeanTheObjectRunningTheScript()
        {
            var objects = new BlockyObjects();

            Assert.AreSame(_target, objects.Find("me", _target));
            Assert.AreSame(_target, objects.Find("myself", _target));
            Assert.AreSame(_target, objects.Find("", _target));
        }

        [Test]
        public void AnUnknownName_IsNull_RatherThanAnError()
        {
            var objects = new BlockyObjects();

            Assert.IsNull(objects.Find("NoSuchThingInTheScene", _target));
        }

        [Test]
        public void RegisteringAnObject_UndoesAnEarlierMissOnThatName()
        {
            // A name that matched nothing is remembered for the rest of the frame, so one mistyped name in a loop
            // cannot search the whole scene every lap — but an object that turns up must not stay invisible.
            // The name has to be one nothing in the scene answers to, or the fallback search would find it.
            var objects = new BlockyObjects();
            Assert.IsNull(objects.Find("BallThatDoesNotExistYet", _target));

            var late = new GameObject("BallThatDoesNotExistYet");
            try
            {
                objects.Register(late);
                Assert.AreSame(late, objects.Find("BallThatDoesNotExistYet", _target));
            }
            finally
            {
                Object.DestroyImmediate(late);
            }
        }

        [Test]
        public void AnObjectDestroyedSinceItWasRegistered_IsNotHandedBack()
        {
            var objects = new BlockyObjects();
            objects.Register(_ball);
            Object.DestroyImmediate(_ball);
            _ball = null;

            Assert.IsNull(objects.Find("Ball", _target));
        }

        // ---- the blocks ------------------------------------------------------------------------------

        private static BlockDefinition Def(string blockType, BlockShape shape, int branchCount = 0, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = blockType;
            def.shape = shape;
            def.branchCount = branchCount;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static ParamSpec Num(string key) => new() { key = key, kind = ParamKind.Number, min = -1000000, max = 1000000 };
        private static ParamSpec Obj(string key) => new() { key = key, kind = ParamKind.ObjectRef };

        private static ParamSpec Axis() => new()
        {
            key = "axis",
            kind = ParamKind.Choice,
            defaultText = "x",
            choices = new[]
            {
                new ChoiceEntry { stableId = "x", displayNameKey = "x" },
                new ChoiceEntry { stableId = "y", displayNameKey = "y" },
                new ChoiceEntry { stableId = "z", displayNameKey = "z" }
            }
        };

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def("motion.move_forward", BlockShape.Statement, parameters: new[] { Num("distance"), Num("duration") }),
            Def("motion.point_towards", BlockShape.Statement, parameters: new[] { Obj("object") }),
            Def("motion.go_to", BlockShape.Statement, parameters: new[] { Obj("object"), Num("duration") }),
            Def("sensing.distance_to", BlockShape.Reporter, parameters: new[] { Obj("object") }),
            Def("sensing.position_of", BlockShape.Reporter, parameters: new[] { Obj("object"), Axis() })
        });

        private static int _nextId;
        private static string Id() => "o" + ++_nextId;

        private static BlockNode Node(string blockType, params BlockParam[] parameters) =>
            new() { id = Id(), blockType = blockType, parameters = parameters };

        private static BlockParam Number(string key, float value) => new() { key = key, kind = ParamKind.Number, number = value };
        private static BlockParam Ref(string key, string name) => new() { key = key, kind = ParamKind.ObjectRef, text = name };
        private static BlockParam Choice(string key, string id) => new() { key = key, kind = ParamKind.Choice, text = id };
        private static BlockParam Slot(string key, ParamKind kind, BlockNode block) => new() { key = key, kind = kind, reporter = block };

        private VmScheduler Run(GameObject on, int ticks, params BlockNode[] sequence)
        {
            var registry = BuildRegistry();
            var program = new ObjectProgram
            {
                stacks = new[] { new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", sequence = sequence } }
            };
            var result = ProgramCompiler.Link(program, registry);
            CollectionAssert.IsEmpty(result.Diagnostics);

            var assembly = typeof(OpTableBuilder).Assembly;
            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, assembly);
            var scheduler = new VmScheduler(steps, conditions, values);
            scheduler.Start(result.Program, on, result.Program.StackEntryPoints[0]);
            for (var i = 0; i < ticks; i++) scheduler.Tick(1f);
            return scheduler;
        }

        [Test]
        public void DistanceTo_MeasuresTheGapBetweenTheTwoObjects()
        {
            BlockyRuntime.Objects.Register(_ball);
            _ball.transform.position = new Vector3(0f, 0f, 6f);

            // move forward (distance to [Ball]) — moves exactly as far as the ball is away
            Run(_target, 1, Node("motion.move_forward",
                Slot("distance", ParamKind.Number, Node("sensing.distance_to", Ref("object", "Ball"))),
                Number("duration", 0f)));

            Assert.AreEqual(6f, _target.transform.position.z, 1e-3f);
        }

        [Test]
        public void DistanceToSomethingThatIsNotThere_IsZero_SoNothingFliesOff()
        {
            Run(_target, 1, Node("motion.move_forward",
                Slot("distance", ParamKind.Number, Node("sensing.distance_to", Ref("object", "Nowhere"))),
                Number("duration", 0f)));

            Assert.AreEqual(0f, _target.transform.position.z, 1e-4f);
        }

        [Test]
        public void PositionOf_ReadsTheOtherObjectsAxis()
        {
            BlockyRuntime.Objects.Register(_ball);
            _ball.transform.position = new Vector3(2f, 5f, 9f);

            Run(_target, 1, Node("motion.move_forward",
                Slot("distance", ParamKind.Number, Node("sensing.position_of", Ref("object", "Ball"), Choice("axis", "y"))),
                Number("duration", 0f)));

            Assert.AreEqual(5f, _target.transform.position.z, 1e-3f);
        }

        [Test]
        public void PointTowards_TurnsToFaceTheOtherObject_WithoutTippingOver()
        {
            BlockyRuntime.Objects.Register(_ball);
            _ball.transform.position = new Vector3(10f, 25f, 0f); // far above as well as to the side

            Run(_target, 1, Node("motion.point_towards", Ref("object", "Ball")));

            var euler = _target.transform.eulerAngles;
            Assert.AreEqual(90f, euler.y, 0.01f, "facing +x");
            Assert.AreEqual(0f, euler.x, 0.01f, "and not tilted up at it");
            Assert.AreEqual(0f, euler.z, 0.01f);
        }

        [Test]
        public void GoTo_WithNoDuration_ArrivesAtOnce()
        {
            BlockyRuntime.Objects.Register(_ball);
            _ball.transform.position = new Vector3(3f, 4f, 5f);

            Run(_target, 1, Node("motion.go_to", Ref("object", "Ball"), Number("duration", 0f)));

            Assert.AreEqual(new Vector3(3f, 4f, 5f), _target.transform.position);
        }

        [Test]
        public void GoTo_OverTime_FollowsTheObjectIfItMoves()
        {
            BlockyRuntime.Objects.Register(_ball);
            _ball.transform.position = new Vector3(0f, 0f, 10f);

            var registry = BuildRegistry();
            var program = new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack
                    {
                        id = "stk_1", triggerBlockType = "event.when_play_clicked",
                        sequence = new[] { Node("motion.go_to", Ref("object", "Ball"), Number("duration", 2f)) }
                    }
                }
            };
            var result = ProgramCompiler.Link(program, registry);
            var assembly = typeof(OpTableBuilder).Assembly;
            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, assembly);
            var scheduler = new VmScheduler(steps, conditions, values);
            scheduler.Start(result.Program, _target, result.Program.StackEntryPoints[0]);

            scheduler.Tick(1f);
            Assert.AreNotEqual(10f, _target.transform.position.z, "still on its way");

            _ball.transform.position = new Vector3(0f, 0f, 20f); // the target moves mid-glide
            scheduler.Tick(1f);

            Assert.AreEqual(20f, _target.transform.position.z, 1e-3f, "it lands where the ball ended up, not where it started");
        }

        [Test]
        public void ARunnersOwnObject_RegistersItself_SoOtherScriptsCanNameIt()
        {
            _target.SetActive(false);
            var asset = ScriptableObject.CreateInstance<Persistence.BlockProgramAsset>();
            asset.Save(new ObjectProgram { targetObjectUid = _target.name });

            BlockyRuntime.SetForTests(BuildRegistry(), new VmScheduler(new IBlockOp[0]), new Triggers.TriggerBroker());
            var runner = _target.AddComponent<ObjectProgramRunner>();
            runner.SetProgramAsset(asset);
            runner.Initialize();

            Assert.AreSame(_target, BlockyRuntime.Objects.Find(_target.name, null));

            runner.Shutdown();
            // Null because the registry dropped it *and* the fallback scene search cannot see it: this object was
            // deliberately left inactive, and GameObject.Find skips inactive objects.
            Assert.IsNull(BlockyRuntime.Objects.Find(_target.name, null), "and stops being registered when it shuts down");
        }
    }
}
