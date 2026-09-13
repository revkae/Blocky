using Blocky.Data;
using Blocky.Runtime.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    public class BlockProgramAssetTests
    {
        [Test]
        public void SaveThenLoad_RoundTripsProgram()
        {
            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            var program = new ObjectProgram
            {
                targetObjectUid = "obj_1",
                stacks = new[] { new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked" } }
            };

            asset.Save(program);
            var loaded = asset.Load();

            Assert.AreEqual("obj_1", loaded.targetObjectUid);
            Assert.AreEqual("stk_1", loaded.stacks[0].id);
        }

        [Test]
        public void Load_WithNoSavedProgram_ReturnsEmptyProgram()
        {
            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            Assert.AreEqual(0, asset.Load().stacks.Length);
        }
    }
}
