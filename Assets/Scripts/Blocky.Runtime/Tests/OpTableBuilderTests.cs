using System;
using Blocky.Compiler;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    public class OpTableBuilderTests
    {
        private static BlockDefinition Def(string blockType, string executorKey, BlockShape shape = BlockShape.Statement)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = executorKey;
            def.shape = shape;
            return def;
        }

        [Test]
        public void Build_BindsKnownExecutorKeys()
        {
            var registry = BlockRegistry.Build(new[]
            {
                Def("motion.move_forward", "motion.move_forward"),
                Def("control.wait", "control.wait"),
                Def("control.repeat", "control.repeat")
            });

            var table = OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly);

            registry.TryGetOpcode("motion.move_forward", out var moveOpcode);
            Assert.IsInstanceOf<Ops.MoveForwardOp>(table[moveOpcode]);
        }

        [Test]
        public void Build_UnknownExecutorKey_Throws()
        {
            var registry = BlockRegistry.Build(new[] { Def("motion.teleport", "no.such.op") });
            Assert.Throws<InvalidOperationException>(() => OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
        }

        [Test]
        public void Build_SkipsTriggers()
        {
            var registry = BlockRegistry.Build(new[] { Def("event.when_play_clicked", "unbound.executor", BlockShape.Trigger) });
            Assert.DoesNotThrow(() => OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly));
        }
    }
}
