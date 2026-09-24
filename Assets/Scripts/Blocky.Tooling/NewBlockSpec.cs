using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Blocky.Compiler;
using Blocky.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The statics here are fixed values that never change, so there is nothing to reset between Play sessions.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Tooling
{
    /// <summary>What kind of block the wizard makes — the three a game adds most. Hats and C-blocks need the runner or the VM's frames, and are made by hand.</summary>
    public enum NewBlockKind
    {
        /// <summary>A stack block that does something: "jump", "open the door".</summary>
        Command,

        /// <summary>A round block that answers a value: "score of", "height".</summary>
        Value,

        /// <summary>A hexagon that answers yes or no: "on the ground?".</summary>
        Question
    }

    /// <summary>What one input of a new block holds.</summary>
    public enum NewBlockInputKind
    {
        Number,
        Text,

        /// <summary>A dropdown; its choices are listed, comma-separated, in <see cref="NewBlockInput.Choices"/>.</summary>
        Choice,

        /// <summary>Another object, by name ("me" or empty means the object running the script).</summary>
        Object,

        /// <summary>A ⬡ hole for a condition block.</summary>
        Condition
    }

    [Serializable]
    public sealed class NewBlockInput
    {
        public string label = "amount";
        public NewBlockInputKind kind = NewBlockInputKind.Number;
        public string defaultValue = "10";
        public string choices = string.Empty;

        /// <summary>The input's key, from its label: <c>jump height</c> → <c>jump_height</c>.</summary>
        public string Key => NewBlockSpec.Snake(label);

        /// <summary>The dropdown's choices, trimmed, empty ones left out.</summary>
        public string[] ChoiceList => (choices ?? string.Empty).Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
    }

    /// <summary>
    /// Everything <c>Blocky › New Block…</c> asks for, and everything it makes from it — kept apart from the window so
    /// it can be tested: the block asset, the op class that makes it work, and its words in every language file.
    /// </summary>
    [Serializable]
    public sealed class NewBlockSpec
    {
        /// <summary>The block type a new block gets unless someone types another — its own prefix, so a later version of Blocky can't take the same name.</summary>
        public const string DefaultPrefix = "game";

        public string name = "jump";
        public NewBlockKind kind = NewBlockKind.Command;
        public BlockCategory category = BlockCategory.Motion;
        public string blockType = string.Empty; // empty: DefaultPrefix + "." + the name
        public string folder = "Assets/BlockyBlocks";
        public List<NewBlockInput> inputs = new();

        private static readonly Regex BlockTypePattern = new(@"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$", RegexOptions.Compiled);

        /// <summary>The block type this makes: what was typed, or <c>game.&lt;name&gt;</c>.</summary>
        public string BlockType => string.IsNullOrWhiteSpace(blockType) ? $"{DefaultPrefix}.{Snake(name)}" : blockType.Trim();

        /// <summary>The asset's file name: the block type with dots as underscores, as Blocky's own are named.</summary>
        public string AssetName => BlockType.Replace('.', '_');

        /// <summary>The op class: <c>jump height</c> → <c>JumpHeightOp</c> (a value block ends in Value, a question in Condition).</summary>
        public string ClassName => Pascal(Snake(name)) + kind switch
        {
            NewBlockKind.Value => "Value",
            NewBlockKind.Question => "Condition",
            _ => "Op"
        };

        public BlockShape Shape => kind switch
        {
            NewBlockKind.Value => BlockShape.Reporter,
            NewBlockKind.Question => BlockShape.Boolean,
            _ => BlockShape.Statement
        };

        /// <summary>What's wrong with this spec, in words; empty when the block can be made.</summary>
        public List<string> Problems(BlockRegistry existing)
        {
            var problems = new List<string>();
            if (Snake(name).Length == 0) problems.Add("Give the block a name.");

            var type = BlockType;
            if (!BlockTypePattern.IsMatch(type))
                problems.Add($"The block type \"{type}\" must be lowercase words joined by dots, like \"game.jump\".");
            else if (existing?.Find(type) != null)
                problems.Add($"There already is a block \"{type}\". Pick another name or type.");

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in inputs)
            {
                var key = input.Key;
                if (key.Length == 0) problems.Add("Every input needs a label.");
                else if (!keys.Add(key)) problems.Add($"Two inputs are called \"{key}\".");
                if (input.kind == NewBlockInputKind.Choice && input.ChoiceList.Length == 0) problems.Add($"The dropdown \"{input.label}\" needs its choices, separated by commas.");
                if (input.kind == NewBlockInputKind.Number && !string.IsNullOrWhiteSpace(input.defaultValue) &&
                    !float.TryParse(input.defaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    problems.Add($"The number input \"{input.label}\" starts at \"{input.defaultValue}\", which isn't a number.");
            }

            if (string.IsNullOrWhiteSpace(folder) || !folder.Replace('\\', '/').StartsWith("Assets", StringComparison.Ordinal))
                problems.Add("The folder must be inside Assets.");
            return problems;
        }

        // ---- what gets made ----------------------------------------------------------------------------

        /// <summary>The block's asset, ready for <c>AssetDatabase.CreateAsset</c>.</summary>
        public BlockDefinition CreateDefinition()
        {
            var definition = ScriptableObject.CreateInstance<BlockDefinition>();
            definition.name = AssetName;
            definition.blockType = BlockType;
            definition.executorKey = BlockType;
            definition.displayNameKey = name.Trim();
            definition.shape = Shape;
            definition.category = category;
            definition.branchCount = 0;
            definition.retrigger = RetriggerPolicy.RestartOnRetrigger;
            definition.parameters = inputs.Select(ToParamSpec).ToArray();
            return definition;
        }

        private static ParamSpec ToParamSpec(NewBlockInput input)
        {
            var spec = new ParamSpec { key = input.Key, choices = Array.Empty<ChoiceEntry>() };
            switch (input.kind)
            {
                case NewBlockInputKind.Number:
                    spec.kind = ParamKind.Number;
                    spec.min = -1000000f;
                    spec.max = 1000000f;
                    float.TryParse(input.defaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out spec.defaultNumber);
                    break;
                case NewBlockInputKind.Text:
                    spec.kind = ParamKind.Text;
                    spec.defaultText = input.defaultValue ?? string.Empty;
                    break;
                case NewBlockInputKind.Choice:
                    spec.kind = ParamKind.Choice;
                    spec.choices = input.ChoiceList.Select(c => new ChoiceEntry { stableId = Snake(c), displayNameKey = c }).ToArray();
                    spec.defaultText = spec.choices.Length > 0 ? spec.choices[0].stableId : string.Empty;
                    break;
                case NewBlockInputKind.Object:
                    spec.kind = ParamKind.ObjectRef;
                    spec.defaultText = string.IsNullOrWhiteSpace(input.defaultValue) ? "me" : input.defaultValue.Trim();
                    break;
                case NewBlockInputKind.Condition:
                    spec.kind = ParamKind.Reporter;
                    break;
            }
            return spec;
        }

        /// <summary>The op class that makes the block work, with each input already read — the part left is what the block does.</summary>
        public string OpSource()
        {
            var type = BlockType;
            var source = new StringBuilder();
            source.AppendLine("using Blocky.Runtime;");
            source.AppendLine("using UnityEngine;");
            source.AppendLine("using UnityEngine.Scripting;");
            source.AppendLine();
            source.AppendLine("/// <summary>");
            source.AppendLine($"/// \"{Xml(name.Trim())}\" — made with Blocky › New Block. The block itself is Resources/Blocks/{AssetName}.asset,");
            source.AppendLine("/// and its words are in Resources/Languages/*.blocks.json, next to it.");
            source.AppendLine("/// </summary>");
            source.AppendLine($"[BlockExecutor(\"{type}\")]");
            source.AppendLine("[Preserve] // Blocky makes this class by reflection and nothing else refers to it: keep it in stripped builds");
            var (interfaceName, signature) = kind switch
            {
                NewBlockKind.Value => ("IValueOp", "public BlockValue Evaluate(ref SlotContext ctx)"),
                NewBlockKind.Question => ("IConditionOp", "public bool Evaluate(ref SlotContext ctx)"),
                _ => ("IBlockOp", "public OpResult Execute(ref OpContext ctx)")
            };
            source.AppendLine($"public sealed class {ClassName} : {interfaceName}");
            source.AppendLine("{");
            source.AppendLine("    [Preserve]");
            source.AppendLine($"    public {ClassName}() {{ }}");
            source.AppendLine();
            source.AppendLine($"    {signature}");
            source.AppendLine("    {");
            source.AppendLine("        var self = ctx.Target; // the object running the script");

            for (var i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                var variable = Camel(input.Key);
                switch (input.kind)
                {
                    case NewBlockInputKind.Number:
                        source.AppendLine($"        var {variable} = ctx.GetNumber({i}); // \"{Comment(input.label)}\"");
                        break;
                    case NewBlockInputKind.Text:
                        source.AppendLine($"        var {variable} = ctx.GetText({i}); // \"{Comment(input.label)}\"");
                        break;
                    case NewBlockInputKind.Choice:
                        var choices = string.Join(", ", input.ChoiceList.Select((c, n) => $"{n} = {Comment(c)}"));
                        source.AppendLine($"        var {variable} = ctx.Params[{i}].ChoiceIndex; // \"{Comment(input.label)}\": {choices}");
                        break;
                    case NewBlockInputKind.Object:
                        source.AppendLine($"        var {variable} = BlockyRuntime.Objects.Find(ctx.GetText({i}), self); // \"{Comment(input.label)}\"; null when no object has that name");
                        break;
                    case NewBlockInputKind.Condition:
                        source.AppendLine($"        var {variable} = ctx.GetBool({i}); // \"{Comment(input.label)}\"; an empty hole reads as false");
                        break;
                }
            }

            source.AppendLine();
            switch (kind)
            {
                case NewBlockKind.Value:
                    source.AppendLine("        // What the block answers. BlockValue.Text(...) and BlockValue.Boolean(...) work too.");
                    source.AppendLine("        return BlockValue.Number(0f);");
                    break;
                case NewBlockKind.Question:
                    source.AppendLine("        // The block's answer: true or false.");
                    source.AppendLine("        return false;");
                    break;
                default:
                    source.AppendLine("        // What the block does. Return OpResult.Continue when it's done. For something that takes time, keep");
                    source.AppendLine("        // its progress in ctx.Scratch and return OpResult.Retry until it's finished — see Blocky's MoveForwardOp.");
                    source.AppendLine("        return OpResult.Continue;");
                    break;
            }

            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        /// <summary>
        /// The strings the block needs in one language, leaving out any <paramref name="known"/> already has — an input
        /// called "distance" keeps the label Blocky gave it, rather than renaming it on every block that has one. Every
        /// language gets the English words, to translate in its own <c>*.blocks.json</c>.
        /// </summary>
        public Dictionary<string, string> Strings(IReadOnlyDictionary<string, string> known)
        {
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            void Add(string key, string text)
            {
                if (known == null || !known.ContainsKey(key)) strings[key] = text;
            }

            Add("block." + BlockType, name.Trim());
            foreach (var input in inputs)
            {
                Add("param." + input.Key, input.label.Trim());
                if (input.kind != NewBlockInputKind.Choice) continue;
                foreach (var choice in input.ChoiceList) Add($"choice.{input.Key}.{Snake(choice)}", choice);
            }
            return strings;
        }

        /// <summary>
        /// A language file for a project's own blocks (<c>en.blocks.json</c>…), with <paramref name="additions"/> added to
        /// what <paramref name="existingJson"/> already had. Blocky reads it on top of its own file for that language.
        /// </summary>
        public static string MergeLanguageFile(string existingJson, string code, string languageName, IReadOnlyDictionary<string, string> additions)
        {
            JObject root;
            try
            {
                root = string.IsNullOrWhiteSpace(existingJson) ? new JObject() : JObject.Parse(existingJson);
            }
            catch (JsonException)
            {
                root = new JObject(); // an unreadable file is replaced rather than left to break the language
            }

            root["locale"] ??= code;
            root["name"] ??= languageName;
            root["about"] ??= "Words for this project's own blocks, read on top of Blocky's file for this language. Made by Blocky › New Block — translate any that are still in English.";
            if (root["strings"] is not JObject strings) root["strings"] = strings = new JObject();
            foreach (var pair in additions) strings[pair.Key] = pair.Value;
            return root.ToString(Formatting.Indented) + "\n";
        }

        // ---- names -------------------------------------------------------------------------------------

        /// <summary><c>Jump Height!</c> → <c>jump_height</c>: lowercase letters, digits and underscores, starting with a letter.</summary>
        public static string Snake(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var snake = new StringBuilder();
            foreach (var c in text.Trim().ToLowerInvariant())
            {
                if (c is >= 'a' and <= 'z' || c is >= '0' and <= '9') snake.Append(c);
                else if (snake.Length > 0 && snake[snake.Length - 1] != '_') snake.Append('_');
            }

            var result = snake.ToString().Trim('_');
            while (result.Length > 0 && char.IsDigit(result[0])) result = result.Substring(1);
            return result.TrimStart('_');
        }

        /// <summary><c>jump_height</c> → <c>JumpHeight</c>.</summary>
        public static string Pascal(string snake) =>
            string.Concat(snake.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));

        private static string Camel(string snake)
        {
            var pascal = Pascal(snake);
            var camel = pascal.Length == 0 ? "value" : char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
            return camel is "self" or "ctx" or "base" or "this" or "object" or "string" or "event" or "params" ? camel + "Input" : camel;
        }

        /// <summary>Text for a <c>//</c> comment: one line.</summary>
        private static string Comment(string text) => (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');

        /// <summary>Text for a <c>///</c> doc comment: one line, and XML's three special characters written out.</summary>
        private static string Xml(string text) => Comment(text).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
