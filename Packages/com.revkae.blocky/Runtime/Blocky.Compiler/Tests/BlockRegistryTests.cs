using System;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Compiler.Tests
{
    public class BlockRegistryTests
    {
        private static BlockDefinition MakeDef(string blockType, int branchCount = 0)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.branchCount = branchCount;
            return def;
        }

        [Test]
        public void Build_AssignsOpcodesBySortedBlockType()
        {
            var registry = BlockRegistry.Build(new[]
            {
                MakeDef("motion.move_forward"),
                MakeDef("control.repeat"),
                MakeDef("event.when_play_clicked")
            });

            Assert.AreEqual(3, registry.Count);
            Assert.AreEqual("control.repeat", registry.GetByOpcode(0).blockType);
            registry.TryGetOpcode("control.repeat", out var repeatOpcode);
            registry.TryGetOpcode("event.when_play_clicked", out var eventOpcode);
            registry.TryGetOpcode("motion.move_forward", out var motionOpcode);

            Assert.Less(repeatOpcode, eventOpcode);
            Assert.Less(eventOpcode, motionOpcode);
        }

        [Test]
        public void Build_DuplicateBlockType_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => BlockRegistry.Build(new[]
            {
                MakeDef("motion.move_forward"),
                MakeDef("motion.move_forward")
            }));
        }

        [Test]
        public void Build_NonLowercaseBlockType_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => BlockRegistry.Build(new[] { MakeDef("Motion.Move") }));
        }

        [Test]
        public void Find_UnknownBlockType_ReturnsNull()
        {
            var registry = BlockRegistry.Build(new[] { MakeDef("motion.move_forward") });
            Assert.IsNull(registry.Find("does.not_exist"));
        }
    }
}
