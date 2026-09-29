using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using Blocky.Runtime.Persistence;
using Blocky.Runtime.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>2D scenes: motion on the flat XY stage, sprites tinted and put back, and 2D contacts reaching the scripts.</summary>
    public class TwoDTests
    {
        private readonly List<Object> _made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var made in _made) Object.DestroyImmediate(made);
            _made.Clear();
            BlockyRuntime.Reset();
        }

        private GameObject Make(string name, bool sprite)
        {
            var go = new GameObject(name);
            if (sprite) go.AddComponent<SpriteRenderer>();
            _made.Add(go);
            return go;
        }

        private static BlockDefinition Def(string blockType, BlockShape shape, params ParamSpec[] parameters)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = blockType;
            def.shape = shape;
            def.parameters = parameters;
            return def;
        }

        private static ParamSpec Num(string key) => new() { key = key, kind = ParamKind.Number, min = -1000, max = 1000 };

        private static ParamSpec Choices(string key, params string[] ids)
        {
            var entries = new ChoiceEntry[ids.Length];
            for (var i = 0; i < ids.Length; i++) entries[i] = new ChoiceEntry { stableId = ids[i], displayNameKey = ids[i] };
            return new ParamSpec { key = key, kind = ParamKind.Choice, defaultText = ids[0], choices = entries };
        }

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def("event.when_collided", BlockShape.Trigger, new ParamSpec { key = "tag_filter", kind = ParamKind.Text }),
            Def("motion.move_forward", BlockShape.Statement, Num("distance"), Num("duration")),
            Def("motion.turn_direction", BlockShape.Statement, Choices("direction", "left", "right"), Num("degrees"), Num("duration")),
            Def("motion.point_towards", BlockShape.Statement, new ParamSpec { key = "object", kind = ParamKind.ObjectRef }),
            Def("looks.change_color", BlockShape.Statement, new ParamSpec { key = "color", kind = ParamKind.Text }, Num("duration"))
        });

        private static int _nextId;

        private static BlockNode Node(string type, params BlockParam[] parameters) => new() { id = "d" + ++_nextId, blockType = type, parameters = parameters };
        private static BlockParam Number(string key, float value) => new() { key = key, kind = ParamKind.Number, number = value };
        private static BlockParam Text(string key, string value) => new() { key = key, kind = ParamKind.Text, text = value };
        private static BlockParam Choice(string key, string id) => new() { key = key, kind = ParamKind.Choice, text = id };

        private static BlockNode Move(float distance) => Node("motion.move_forward", Number("distance", distance), Number("duration", 0f));
        private static BlockNode Turn(string direction, float degrees) => Node("motion.turn_direction", Choice("direction", direction), Number("degrees", degrees), Number("duration", 0f));

        private static void Run(GameObject on, params BlockNode[] sequence)
        {
            var registry = BuildRegistry();
            var result = ProgramCompiler.Link(new ObjectProgram
            {
                stacks = new[] { new BlockStack { id = "stk", triggerBlockType = "event.when_play_clicked", sequence = sequence } }
            }, registry);
            CollectionAssert.IsEmpty(result.Diagnostics);

            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, typeof(OpTableBuilder).Assembly);
            var scheduler = new VmScheduler(steps, conditions, values);
            scheduler.Start(result.Program, on, result.Program.StackEntryPoints[0]);
            scheduler.Tick(1f);
        }

        // ---- which plane -----------------------------------------------------------------------------

        [Test]
        public void ASprite_IsFlat_APlainObjectIsNot_AndTheRunnerCanSayOtherwise()
        {
            var sprite = Make("Sprite", sprite: true);
            var cube = Make("Cube", sprite: false);

            Assert.IsTrue(BlockyPlane.IsFlat(sprite));
            Assert.IsFalse(BlockyPlane.IsFlat(cube));

            sprite.AddComponent<ObjectProgramRunner>().Motion = MotionPlane.ThreeD;
            cube.AddComponent<ObjectProgramRunner>().Motion = MotionPlane.TwoD;
            Assert.IsFalse(BlockyPlane.IsFlat(sprite), "a 2.5D game's sprite can walk in 3D");
            Assert.IsTrue(BlockyPlane.IsFlat(cube));
        }

        // ---- motion ----------------------------------------------------------------------------------

        [Test]
        public void OnA2DStage_ASpriteMovesAlongX_TheWayItFaces()
        {
            var cat = Make("Cat", sprite: true);

            Run(cat, Move(3f));

            Assert.AreEqual(3f, cat.transform.position.x, 1e-4f);
            Assert.AreEqual(0f, cat.transform.position.z, 1e-4f, "nothing moves towards the camera");
        }

        [Test]
        public void OnA2DStage_TurningLeft_IsAnticlockwise_AndForwardTurnsWithIt()
        {
            var cat = Make("Cat", sprite: true);

            Run(cat, Turn("left", 90f), Move(2f));

            Assert.AreEqual(90f, cat.transform.eulerAngles.z, 1e-3f);
            Assert.AreEqual(2f, cat.transform.position.y, 1e-3f, "facing up after a left turn");
            Assert.AreEqual(0f, cat.transform.position.x, 1e-3f);
        }

        [Test]
        public void In3D_TurningRight_FacesPlusX()
        {
            var robot = Make("Robot", sprite: false);

            Run(robot, Turn("right", 90f), Move(2f));

            Assert.AreEqual(90f, robot.transform.eulerAngles.y, 1e-3f);
            Assert.AreEqual(2f, robot.transform.position.x, 1e-3f, "facing +Z, the right is +X");
        }

        [Test]
        public void OnA2DStage_PointTowards_AimsTheSpritesXAxis()
        {
            var cat = Make("Cat", sprite: true);
            var mouse = Make("Mouse", sprite: true);
            mouse.transform.position = new Vector3(0f, 5f, 0f);
            BlockyRuntime.Objects.Register(mouse);

            Run(cat, Node("motion.point_towards", Text("object", "Mouse")));

            Assert.AreEqual(90f, cat.transform.eulerAngles.z, 1e-3f);
            Assert.AreEqual(0f, cat.transform.eulerAngles.y, 1e-3f, "no turning out of the stage");
        }

        // ---- looks -----------------------------------------------------------------------------------

        [Test]
        public void ChangeColor_TintsASprite_AndResetPutsTheTintBack()
        {
            var cat = Make("Cat", sprite: true);
            var sprite = cat.GetComponent<SpriteRenderer>();
            sprite.color = Color.white;
            BlockyRuntime.World.Capture(cat);

            Run(cat, Node("looks.change_color", Text("color", "#FF0000"), Number("duration", 0f)));
            Assert.AreEqual(Color.red, sprite.color);

            BlockyRuntime.World.RestoreAll();
            Assert.AreEqual(Color.white, sprite.color);
        }

        // ---- contacts --------------------------------------------------------------------------------

        [Test]
        public void ABump_StartsWhenCollided_FilteredByTheOtherObjectsTag()
        {
            var registry = BuildRegistry();
            var broker = new TriggerBroker();
            var (steps, conditions, values) = OpTableBuilder.BuildAll(registry, typeof(OpTableBuilder).Assembly);
            var scheduler = new VmScheduler(steps, conditions, values);
            BlockyRuntime.SetForTests(registry, scheduler, broker);

            var cat = Make("Cat", sprite: true);
            cat.SetActive(false);
            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            _made.Add(asset);
            asset.Save(new ObjectProgram
            {
                stacks = new[]
                {
                    new BlockStack
                    {
                        id = "stk", triggerBlockType = "event.when_collided",
                        triggerParameters = new[] { Text("tag_filter", "Finish") },
                        sequence = new[] { Move(1f) }
                    }
                }
            });
            var runner = cat.AddComponent<ObjectProgramRunner>();
            runner.SetProgramAsset(asset);
            runner.Initialize();

            var wall = Make("Wall", sprite: true);
            var flag = Make("Flag", sprite: true);
            flag.tag = "Finish";

            broker.RaiseCollided(cat, wall);
            scheduler.Tick(0.1f);
            Assert.AreEqual(0f, cat.transform.position.x, 1e-4f, "the wall isn't the finish");

            broker.RaiseCollided(cat, flag);
            scheduler.Tick(0.1f);
            Assert.AreEqual(1f, cat.transform.position.x, 1e-4f, "moved along X: it's a sprite");
        }
    }
}
