using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor.Tests
{
    public class PaletteViewTests
    {
        private static BlockDefinition Def(string blockType, BlockCategory category, int branchCount = 0, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.category = category;
            def.branchCount = branchCount;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        [Test]
        public void GroupsPrototypesByCategory()
        {
            var registry = BlockRegistry.Build(new[]
            {
                Def("motion.move_forward", BlockCategory.Motion),
                Def("motion.set_position", BlockCategory.Motion),
                Def("control.repeat", BlockCategory.Control, branchCount: 1)
            });

            var view = new PaletteView(registry);

            Assert.AreEqual(2, view.Query(className: "blocky-palette__section").ToList().Count); // Control, Motion
            Assert.AreEqual(3, view.Query(className: "blocky-palette__prototype").ToList().Count);
        }

        [Test]
        public void InstantiatePrototype_RegeneratesId_AndFillsDefaults()
        {
            var def = Def("motion.move_forward", BlockCategory.Motion, parameters: new[]
            {
                new ParamSpec { key = "distance", kind = ParamKind.Number, defaultNumber = 5f }
            });

            var first = PaletteView.InstantiatePrototype(def);
            var second = PaletteView.InstantiatePrototype(def);

            Assert.AreNotEqual(first.id, second.id);
            Assert.AreEqual("motion.move_forward", first.blockType);
            Assert.AreEqual(5f, first.parameters[0].number);
        }

        [Test]
        public void InstantiatePrototype_FillsEmptyBranchesPerBranchCount()
        {
            var def = Def("control.repeat", BlockCategory.Control, branchCount: 1);
            var node = PaletteView.InstantiatePrototype(def);

            Assert.AreEqual(1, node.branches.Length);
            Assert.AreEqual(0, node.branches[0].Length);
        }
    }
}
