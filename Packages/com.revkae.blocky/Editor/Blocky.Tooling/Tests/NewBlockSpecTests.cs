using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Tooling.Tests
{
    /// <summary>What <c>Blocky › New Block…</c> makes from what it's told: names, checks, the asset, the class, the words.</summary>
    public class NewBlockSpecTests
    {
        private static NewBlockSpec Jump(NewBlockKind kind = NewBlockKind.Command) => new()
        {
            name = "Jump Height",
            kind = kind,
            category = BlockCategory.Motion,
            inputs = new List<NewBlockInput>
            {
                new() { label = "how high", kind = NewBlockInputKind.Number, defaultValue = "2.5" },
                new() { label = "style", kind = NewBlockInputKind.Choice, choices = "small, Big Leap" },
                new() { label = "over", kind = NewBlockInputKind.Object, defaultValue = "" },
                new() { label = "only if", kind = NewBlockInputKind.Condition },
                new() { label = "shout", kind = NewBlockInputKind.Text, defaultValue = "hop!" }
            }
        };

        private static BlockRegistry RegistryWith(string blockType)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            return BlockRegistry.Build(new[] { def });
        }

        // ---- names -----------------------------------------------------------------------------------

        [TestCase("Jump Height!", "jump_height")]
        [TestCase("  open   the door ", "open_the_door")]
        [TestCase("3d move", "d_move")]
        [TestCase("Çiçek", "i_ek")]
        [TestCase("", "")]
        public void Snake_MakesAnId(string text, string snake) => Assert.AreEqual(snake, NewBlockSpec.Snake(text));

        [Test]
        public void TheTypeAndTheClass_ComeFromTheName_UnlessATypeIsTyped()
        {
            var spec = Jump();

            Assert.AreEqual("game.jump_height", spec.BlockType);
            Assert.AreEqual("game_jump_height", spec.AssetName);
            Assert.AreEqual("JumpHeightOp", spec.ClassName);
            Assert.AreEqual("JumpHeightValue", Jump(NewBlockKind.Value).ClassName);
            Assert.AreEqual("JumpHeightCondition", Jump(NewBlockKind.Question).ClassName);

            spec.blockType = " robot.leap ";
            Assert.AreEqual("robot.leap", spec.BlockType);
        }

        // ---- checks ----------------------------------------------------------------------------------

        [Test]
        public void AGoodSpec_HasNoProblems() => CollectionAssert.IsEmpty(Jump().Problems(RegistryWith("motion.move_forward")));

        [TestCase("Game.Jump")]
        [TestCase("jump")]
        [TestCase("game.")]
        [TestCase("game.jump height")]
        public void ABadBlockType_IsAProblem(string type)
        {
            var spec = Jump();
            spec.blockType = type;

            Assert.AreEqual(1, spec.Problems(null).Count);
        }

        [Test]
        public void ABlockTypeThatExists_IsAProblem()
        {
            StringAssert.Contains("already", Jump().Problems(RegistryWith("game.jump_height"))[0]);
        }

        [Test]
        public void InputsMustHaveDistinctLabels_ChoicesAndNumbersThatAreNumbers()
        {
            var spec = Jump();
            spec.inputs.Add(new NewBlockInput { label = "How High", kind = NewBlockInputKind.Text });
            spec.inputs.Add(new NewBlockInput { label = "size", kind = NewBlockInputKind.Choice, choices = " , " });
            spec.inputs.Add(new NewBlockInput { label = "count", kind = NewBlockInputKind.Number, defaultValue = "ten" });
            spec.folder = "Packages/elsewhere";

            Assert.AreEqual(4, spec.Problems(null).Count);
        }

        // ---- what gets made --------------------------------------------------------------------------

        [Test]
        public void TheDefinition_HasTheShapeTheInputsAndTheirStartingValues()
        {
            var definition = Jump().CreateDefinition();

            Assert.AreEqual("game.jump_height", definition.blockType);
            Assert.AreEqual("game.jump_height", definition.executorKey);
            Assert.AreEqual("Jump Height", definition.displayNameKey);
            Assert.AreEqual(BlockShape.Statement, definition.shape);
            Assert.AreEqual(BlockCategory.Motion, definition.category);

            var p = definition.parameters;
            Assert.AreEqual(5, p.Length);
            Assert.AreEqual(("how_high", ParamKind.Number, 2.5f), (p[0].key, p[0].kind, p[0].defaultNumber));
            Assert.AreEqual(ParamKind.Choice, p[1].kind);
            CollectionAssert.AreEqual(new[] { "small", "big_leap" }, new[] { p[1].choices[0].stableId, p[1].choices[1].stableId });
            Assert.AreEqual("small", p[1].defaultText, "a dropdown starts on its first choice");
            Assert.AreEqual((ParamKind.ObjectRef, "me"), (p[2].kind, p[2].defaultText));
            Assert.AreEqual(ParamKind.Reporter, p[3].kind);
            Assert.AreEqual((ParamKind.Text, "hop!"), (p[4].kind, p[4].defaultText));

            Assert.AreEqual(BlockShape.Reporter, Jump(NewBlockKind.Value).CreateDefinition().shape);
            Assert.AreEqual(BlockShape.Boolean, Jump(NewBlockKind.Question).CreateDefinition().shape);
        }

        [Test]
        public void TheClass_IsBoundToTheBlock_AndReadsEveryInputTheRightWay()
        {
            var source = Jump().OpSource();

            StringAssert.Contains("[BlockExecutor(\"game.jump_height\")]", source);
            StringAssert.Contains("public sealed class JumpHeightOp : IBlockOp", source);
            StringAssert.Contains("[Preserve]", source);
            StringAssert.Contains("var howHigh = ctx.GetNumber(0);", source);
            StringAssert.Contains("var style = ctx.Params[1].ChoiceIndex; // \"style\": 0 = small, 1 = Big Leap", source);
            StringAssert.Contains("var over = BlockyRuntime.Objects.Find(ctx.GetText(2), self);", source);
            StringAssert.Contains("var onlyIf = ctx.GetBool(3);", source);
            StringAssert.Contains("var shout = ctx.GetText(4);", source);
            StringAssert.Contains("return OpResult.Continue;", source);
        }

        [Test]
        public void AValueOrAQuestion_AnswersInsteadOfRunning()
        {
            StringAssert.Contains("public sealed class JumpHeightValue : IValueOp", Jump(NewBlockKind.Value).OpSource());
            StringAssert.Contains("return BlockValue.Number(0f);", Jump(NewBlockKind.Value).OpSource());
            StringAssert.Contains("public sealed class JumpHeightCondition : IConditionOp", Jump(NewBlockKind.Question).OpSource());
            StringAssert.Contains("return false;", Jump(NewBlockKind.Question).OpSource());
        }

        [Test]
        public void TheWords_AreTheBlockInputsAndChoices_LeavingOutWhatTheLanguageHas()
        {
            var known = new Dictionary<string, string> { ["param.style"] = "biçim" };

            var strings = Jump().Strings(known);

            Assert.AreEqual("Jump Height", strings["block.game.jump_height"]);
            Assert.AreEqual("how high", strings["param.how_high"]);
            Assert.AreEqual("Big Leap", strings["choice.style.big_leap"]);
            Assert.IsFalse(strings.ContainsKey("param.style"), "an input label the language already has is kept, not renamed on every block");
        }

        [Test]
        public void AProjectsLanguageFile_IsMadeOrAddedTo_KeepingWhatItHad()
        {
            var made = NewBlockSpec.MergeLanguageFile(null, "tr", "Türkçe", new Dictionary<string, string> { ["block.game.a"] = "a" });
            var root = JObject.Parse(made);
            Assert.AreEqual("tr", (string)root["locale"]);
            Assert.AreEqual("Türkçe", (string)root["name"]);
            Assert.AreEqual("a", (string)root["strings"]["block.game.a"]);

            var added = JObject.Parse(NewBlockSpec.MergeLanguageFile(made, "tr", "Türkçe", new Dictionary<string, string> { ["block.game.b"] = "b" }));
            Assert.AreEqual("a", (string)added["strings"]["block.game.a"]);
            Assert.AreEqual("b", (string)added["strings"]["block.game.b"]);

            var replaced = JObject.Parse(NewBlockSpec.MergeLanguageFile("{ not json", "en", "English", new Dictionary<string, string> { ["x"] = "y" }));
            Assert.AreEqual("en", (string)replaced["locale"]);
        }
    }
}
