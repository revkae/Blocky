using Blocky.Data.Serialization;
using NUnit.Framework;

namespace Blocky.Data.Tests
{
    public class ProgramSerializationTests
    {
        private static ObjectProgram BuildSampleProgram()
        {
            return new ObjectProgram
            {
                targetObjectUid = "obj_7f3a91",
                stacks = new[]
                {
                    new BlockStack
                    {
                        id = "stk_a1",
                        triggerBlockType = "event.when_key_pressed",
                        triggerParameters = new[]
                        {
                            new BlockParam { key = "key", kind = ParamKind.Choice, text = "Space" }
                        },
                        canvasPosition = new UnityEngine.Vector2(40, 40),
                        sequence = new[]
                        {
                            new BlockNode
                            {
                                id = "n_01",
                                blockType = "control.repeat",
                                parameters = new[] { new BlockParam { key = "times", kind = ParamKind.Number, number = 4 } },
                                branches = new[]
                                {
                                    new[]
                                    {
                                        new BlockNode
                                        {
                                            id = "n_02",
                                            blockType = "motion.move_forward",
                                            parameters = new[] { new BlockParam { key = "distance", kind = ParamKind.Number, number = 1 } }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        [Test]
        public void RoundTrip_PreservesStructureAndParams()
        {
            var original = BuildSampleProgram();

            var json = ProgramSerializer.Serialize(original);
            var loaded = ProgramSerializer.Deserialize(json);

            Assert.AreEqual(original.schemaVersion, loaded.schemaVersion);
            Assert.AreEqual(original.targetObjectUid, loaded.targetObjectUid);
            Assert.AreEqual(1, loaded.stacks.Length);

            var stack = loaded.stacks[0];
            Assert.AreEqual("stk_a1", stack.id);
            Assert.AreEqual("event.when_key_pressed", stack.triggerBlockType);
            Assert.AreEqual(ParamKind.Choice, stack.triggerParameters[0].kind);
            Assert.AreEqual("Space", stack.triggerParameters[0].text);

            var repeatNode = stack.sequence[0];
            Assert.AreEqual("control.repeat", repeatNode.blockType);
            Assert.AreEqual(ParamKind.Number, repeatNode.parameters[0].kind);
            Assert.AreEqual(4, repeatNode.parameters[0].number);

            var nested = repeatNode.branches[0][0];
            Assert.AreEqual("motion.move_forward", nested.blockType);
            Assert.AreEqual(1, nested.parameters[0].number);
        }

        [Test]
        public void RoundTrip_IsByteIdenticalOnSecondSerialize()
        {
            var original = BuildSampleProgram();
            var json = ProgramSerializer.Serialize(original);
            var loaded = ProgramSerializer.Deserialize(json);
            var reserialized = ProgramSerializer.Serialize(loaded);

            Assert.AreEqual(json, reserialized);
        }

        [Test]
        public void Deserialize_MissingSchemaVersion_Throws()
        {
            Assert.Throws<System.IO.InvalidDataException>(() => ProgramSerializer.Deserialize("{ \"stacks\": [] }"));
        }

        [Test]
        public void Deserialize_FutureSchemaVersion_Throws()
        {
            Assert.Throws<System.IO.InvalidDataException>(() =>
                ProgramSerializer.Deserialize("{ \"schemaVersion\": 999, \"stacks\": [] }"));
        }
    }
}
