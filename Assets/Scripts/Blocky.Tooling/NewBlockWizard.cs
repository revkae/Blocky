using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blocky.Compiler;
using Blocky.Localization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Tooling
{
    /// <summary>
    /// <c>Blocky › New Block…</c>: makes a block in one go — its <see cref="BlockDefinition"/> asset, the C# class that
    /// makes it work (with every input already read), and its words in every language — all in a folder of the
    /// project's own, so updating Blocky never touches them. What the block <em>does</em> is the one thing left to
    /// write. The logic is <see cref="NewBlockSpec"/>; this window only asks for it and writes the files.
    /// </summary>
    public sealed class NewBlockWizard : EditorWindow
    {
        [MenuItem("Blocky/New Block…", false, 20)]
        public static void Open()
        {
            var window = GetWindow<NewBlockWizard>(true, "New Blocky Block");
            window.minSize = new Vector2(460f, 520f);
        }

        private readonly NewBlockSpec _spec = new() { inputs = new List<NewBlockInput> { new() } };
        private BlockRegistry _registry;
        private VisualElement _inputRows;
        private Label _typePreview;
        private Label _problems;
        private Button _createButton;

        private void CreateGUI()
        {
            _registry = BlockRegistry.LoadFromResources();

            var root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 10;

            root.Add(Paragraph("Makes a block: its asset, the C# class that makes it work, and its words in every language. " +
                               "The block shows as its name followed by its inputs, like \"jump  height (10)\"."));

            var nameField = new TextField("Name on the block") { value = _spec.name };
            nameField.RegisterValueChangedCallback(e => { _spec.name = e.newValue; Refresh(); });
            root.Add(nameField);

            var kindField = new EnumField("Kind", _spec.kind) { tooltip = "Command: a stack block that does something. Value: a round block that answers. Question: a hexagon that answers yes or no." };
            kindField.RegisterValueChangedCallback(e => { _spec.kind = (NewBlockKind)e.newValue; Refresh(); });
            root.Add(kindField);

            var categoryField = new EnumField("Palette tab", _spec.category);
            categoryField.RegisterValueChangedCallback(e => { _spec.category = (BlockCategory)e.newValue; Refresh(); });
            root.Add(categoryField);

            var typeField = new TextField("Block type (optional)")
            {
                tooltip = "The id programs save, lowercase words joined by dots. Leave it empty for game.<name>; keep a prefix of your own so a later Blocky can't take the same id."
            };
            typeField.RegisterValueChangedCallback(e => { _spec.blockType = e.newValue; Refresh(); });
            root.Add(typeField);

            _typePreview = new Label();
            _typePreview.style.marginLeft = 4;
            _typePreview.style.marginBottom = 6;
            _typePreview.style.opacity = 0.7f;
            root.Add(_typePreview);

            var folderField = new TextField("Folder") { value = _spec.folder, tooltip = "Where the class, the asset and the words go. Inside Assets, outside Blocky's own folder." };
            folderField.RegisterValueChangedCallback(e => { _spec.folder = e.newValue; Refresh(); });
            root.Add(folderField);

            var inputsHeader = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 8 } };
            inputsHeader.Add(new Label("Inputs") { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } });
            inputsHeader.Add(new Button(AddInput) { text = "+ Add input" });
            root.Add(inputsHeader);

            _inputRows = new ScrollView();
            _inputRows.style.flexGrow = 1;
            root.Add(_inputRows);

            _problems = new Label { style = { color = new Color(0.85f, 0.3f, 0.25f), whiteSpace = WhiteSpace.Normal, marginTop = 6 } };
            root.Add(_problems);

            _createButton = new Button(Create) { text = "Make the block" };
            _createButton.style.height = 28;
            _createButton.style.marginTop = 6;
            _createButton.style.marginBottom = 10;
            root.Add(_createButton);

            RebuildInputRows();
            Refresh();
        }

        private static Label Paragraph(string text) => new(text) { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 8 } };

        private void AddInput()
        {
            _spec.inputs.Add(new NewBlockInput { label = "input " + (_spec.inputs.Count + 1), kind = NewBlockInputKind.Text, defaultValue = string.Empty });
            RebuildInputRows();
            Refresh();
        }

        private void RebuildInputRows()
        {
            _inputRows.Clear();
            for (var i = 0; i < _spec.inputs.Count; i++)
            {
                var input = _spec.inputs[i];
                var box = new VisualElement { style = { marginTop = 4, paddingLeft = 6, borderLeftWidth = 2, borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 0.5f) } };

                var label = new TextField("Label") { value = input.label };
                label.RegisterValueChangedCallback(e => { input.label = e.newValue; Refresh(); });
                box.Add(label);

                var kind = new EnumField("Holds", input.kind);
                kind.RegisterValueChangedCallback(e => { input.kind = (NewBlockInputKind)e.newValue; RebuildInputRows(); Refresh(); });
                box.Add(kind);

                if (input.kind == NewBlockInputKind.Choice)
                {
                    var choices = new TextField("Choices") { value = input.choices, tooltip = "Comma-separated: small, medium, large" };
                    choices.RegisterValueChangedCallback(e => { input.choices = e.newValue; Refresh(); });
                    box.Add(choices);
                }
                else if (input.kind != NewBlockInputKind.Condition)
                {
                    var starts = new TextField("Starts as") { value = input.defaultValue };
                    starts.RegisterValueChangedCallback(e => { input.defaultValue = e.newValue; Refresh(); });
                    box.Add(starts);
                }

                var index = i;
                box.Add(new Button(() => { _spec.inputs.RemoveAt(index); RebuildInputRows(); Refresh(); }) { text = "Remove", style = { alignSelf = Align.FlexEnd } });
                _inputRows.Add(box);
            }
        }

        private void Refresh()
        {
            if (_typePreview == null) return;
            _typePreview.text = $"Block type: {_spec.BlockType}   ·   class: {_spec.ClassName}";
            var problems = _spec.Problems(_registry);
            _problems.text = string.Join("\n", problems);
            _createButton.SetEnabled(problems.Count == 0);
        }

        // ---- making it -------------------------------------------------------------------------------------

        private void Create()
        {
            _registry = BlockRegistry.LoadFromResources(); // someone may have made a block since the window opened
            var problems = _spec.Problems(_registry);
            if (problems.Count > 0)
            {
                Refresh();
                return;
            }

            var folder = _spec.folder.Replace('\\', '/').TrimEnd('/');
            var scriptPath = $"{folder}/{_spec.ClassName}.cs";
            var assetPath = $"{folder}/Resources/Blocks/{_spec.AssetName}.asset";
            if (File.Exists(scriptPath) &&
                !EditorUtility.DisplayDialog("New Block", $"{scriptPath} already exists. Replace it?", "Replace", "Cancel"))
                return;

            EnsureFolder($"{folder}/Resources/Blocks");
            EnsureFolder($"{folder}/Resources/Languages");

            File.WriteAllText(scriptPath, _spec.OpSource());
            WriteWords(folder);

            var definition = _spec.CreateDefinition();
            AssetDatabase.CreateAsset(definition, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // Unity compiles the new class; the block is in the palette from the next Play
            LanguageFiles.Forget();

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (script != null) AssetDatabase.OpenAsset(script);
            EditorGUIUtility.PingObject(definition);
            Debug.Log($"Blocky: made the block \"{_spec.BlockType}\" — {assetPath}, {scriptPath}, and its words in {folder}/Resources/Languages. " +
                      $"Write what it does in {_spec.ClassName}; it's in the {_spec.category} tab from the next Play.");
            Close();
        }

        /// <summary>The block's words into <c>&lt;code&gt;.blocks.json</c> for every language Blocky has, skipping any that language already has.</summary>
        private void WriteWords(string folder)
        {
            foreach (var language in LanguageFiles.All)
            {
                var path = $"{folder}/Resources/Languages/{language.Code}.blocks.json";
                var existing = File.Exists(path) ? File.ReadAllText(path) : null;
                var additions = _spec.Strings(language.Strings);
                if (additions.Count == 0) continue;
                File.WriteAllText(path, NewBlockSpec.MergeLanguageFile(existing, language.Code, language.Name, additions));
            }
        }

        /// <summary>Creates <paramref name="path"/> one folder at a time through the AssetDatabase, so each gets its .meta.</summary>
        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
