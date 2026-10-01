using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Editor.Tests
{
    public class ParamFieldFactoryTests
    {
        [Test]
        public void Number_CreatesDisabledFloatFieldWithValue()
        {
            var spec = new ParamSpec { key = "distance", kind = ParamKind.Number };
            var value = new BlockParam { key = "distance", kind = ParamKind.Number, number = 4.5f };

            var field = ParamFieldFactory.CreateReadOnlyField(spec, value);

            Assert.IsInstanceOf<FloatField>(field);
            var floatField = (FloatField)field;
            Assert.AreEqual(4.5f, floatField.value);
            Assert.IsFalse(floatField.enabledSelf);
        }

        [Test]
        public void Bool_CreatesDisabledToggleWithValue()
        {
            var spec = new ParamSpec { key = "visible", kind = ParamKind.Bool };
            var value = new BlockParam { key = "visible", kind = ParamKind.Bool, boolean = true };

            var field = ParamFieldFactory.CreateReadOnlyField(spec, value);

            Assert.IsInstanceOf<Toggle>(field);
            var toggle = (Toggle)field;
            Assert.IsTrue(toggle.value);
            Assert.IsFalse(toggle.enabledSelf);
        }

        [Test]
        public void Text_CreatesDisabledTextFieldWithValue()
        {
            var spec = new ParamSpec { key = "tag_filter", kind = ParamKind.Text };
            var value = new BlockParam { key = "tag_filter", kind = ParamKind.Text, text = "enemy" };

            var field = ParamFieldFactory.CreateReadOnlyField(spec, value);

            Assert.IsInstanceOf<TextField>(field);
            var textField = (TextField)field;
            Assert.AreEqual("enemy", textField.value);
            Assert.IsFalse(textField.enabledSelf);
        }

        [Test]
        public void Choice_CreatesDropdownWithMatchingIndexSelected()
        {
            var spec = new ParamSpec
            {
                key = "key",
                kind = ParamKind.Choice,
                choices = new[]
                {
                    new ChoiceEntry { stableId = "Space" },
                    new ChoiceEntry { stableId = "Enter" }
                }
            };
            var value = new BlockParam { key = "key", kind = ParamKind.Choice, text = "Enter" };

            var field = ParamFieldFactory.CreateReadOnlyField(spec, value);

            Assert.IsInstanceOf<DropdownField>(field);
            var dropdown = (DropdownField)field;
            Assert.AreEqual(1, dropdown.index);
        }

        [Test]
        public void Reporter_CreatesPlaceholderLabel()
        {
            var spec = new ParamSpec { key = "condition", kind = ParamKind.Reporter };

            var field = ParamFieldFactory.CreateReadOnlyField(spec, null);

            Assert.IsInstanceOf<Label>(field);
        }

        // ---- object inputs, shaped by the game (ADR-041) ---------------------------------------------------

        [TearDown]
        public void DropTheGamesHooks()
        {
            ParamFieldFactory.ObjectChoices = null;
            ParamFieldFactory.IsHidden = null;
        }

        private static readonly ParamSpec ObjectSpec = new() { key = "object", kind = ParamKind.ObjectRef, defaultText = "me" };

        private static BlockParam ObjectValue(string name) => new() { key = "object", kind = ParamKind.ObjectRef, text = name };

        [Test]
        public void ObjectRef_WithNoChoicesFromTheGame_IsAFreeTextField()
        {
            var field = ParamFieldFactory.CreateReadOnlyField(ObjectSpec, ObjectValue("Kedi"));

            Assert.IsInstanceOf<TextField>(field);
            Assert.AreEqual("Kedi", ((TextField)field).value);
        }

        [Test]
        public void ObjectRef_WithChoicesFromTheGame_IsADropdown_OfMeThenTheGamesNames()
        {
            ParamFieldFactory.ObjectChoices = () => new[] { "Kedi", "Köpek", "me", "  ", "kedi" };

            var dropdown = (DropdownField)ParamFieldFactory.CreateReadOnlyField(ObjectSpec, ObjectValue("Köpek"));

            CollectionAssert.AreEqual(new[] { "me", "Kedi", "Köpek" }, dropdown.choices, "me first; blanks, repeats and a second \"me\" dropped");
            Assert.AreEqual("Köpek", dropdown.value);
        }

        [Test]
        public void ObjectRef_ShowsMe_ForEveryWayOfSayingTheScriptsOwnObject()
        {
            ParamFieldFactory.ObjectChoices = () => new[] { "Kedi" };

            foreach (var stored in new[] { "me", "", null, " Myself ", "THIS" })
            {
                var dropdown = (DropdownField)ParamFieldFactory.CreateReadOnlyField(ObjectSpec, ObjectValue(stored));
                Assert.AreEqual(ParamFieldFactory.MeValue, dropdown.value, $"stored as '{stored}'");
            }
        }

        [Test]
        public void ObjectRef_KeepsAStoredNameTheGameNoLongerOffers()
        {
            ParamFieldFactory.ObjectChoices = () => new[] { "Kedi" };

            var dropdown = (DropdownField)ParamFieldFactory.CreateReadOnlyField(ObjectSpec, ObjectValue("Silinen Nesne"));

            CollectionAssert.AreEqual(new[] { "me", "Kedi", "Silinen Nesne" }, dropdown.choices);
            Assert.AreEqual("Silinen Nesne", dropdown.value, "an old program never loses a value");
        }

        [Test]
        public void ObjectRef_MatchesNamesTheWayTheRuntimeFindsObjects()
        {
            ParamFieldFactory.ObjectChoices = () => new[] { "Kedi" };

            var dropdown = (DropdownField)ParamFieldFactory.CreateReadOnlyField(ObjectSpec, ObjectValue(" kedi "));

            Assert.AreEqual(2, dropdown.choices.Count, "not listed a second time for its capitals or spaces");
            Assert.AreEqual("Kedi", dropdown.value);
        }

        [Test]
        public void AnInputTheGameHides_IsLeftOffTheBlock_AndStaysInTheProgram()
        {
            var definition = ScriptableObject.CreateInstance<BlockDefinition>();
            definition.blockType = "motion.go_to";
            definition.shape = BlockShape.Statement;
            definition.parameters = new[]
            {
                new ParamSpec { key = "object", kind = ParamKind.ObjectRef },
                new ParamSpec { key = "duration", kind = ParamKind.Number, min = 0, max = 100 }
            };
            var registry = BlockRegistry.Build(new[] { definition });
            var node = new BlockNode
            {
                id = "n1",
                blockType = "motion.go_to",
                parameters = new[] { ObjectValue("Kedi"), new BlockParam { key = "duration", kind = ParamKind.Number, number = 2f } }
            };

            var shown = BlockView.Create(node, registry);
            Assert.AreEqual(1, shown.Query<FloatField>().ToList().Count);

            ParamFieldFactory.IsHidden = (blockType, key) => blockType == "motion.go_to" && key == "duration";
            var hidden = BlockView.Create(node, registry);

            Assert.AreEqual(0, hidden.Query<FloatField>().ToList().Count, "the hidden input isn't drawn");
            Assert.AreEqual(1, hidden.Query<TextField>().ToList().Count, "the other one is");
            Assert.AreEqual(2f, node.parameters[1].number, "and the value is still in the program");
        }
    }
}
