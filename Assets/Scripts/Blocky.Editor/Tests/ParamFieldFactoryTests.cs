using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
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
    }
}
