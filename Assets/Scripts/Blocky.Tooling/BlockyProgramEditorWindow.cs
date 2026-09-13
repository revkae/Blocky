using Blocky.Compiler;
using Blocky.Data;
using Blocky.Editor;
using Blocky.Runtime;
using Blocky.Runtime.Persistence;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocky.Tooling
{
    /// <summary>
    /// Pick any GameObject in the scene and author its Blocky program directly — the window that turns
    /// Milestones 4-6's rendering/interaction components into an actual authoring tool. Auto-adds an
    /// <see cref="ObjectProgramRunner"/> and a fresh <see cref="BlockProgramAsset"/> the first time you pick an
    /// object that doesn't have one yet, so "write code to any object" works from a clean GameObject.
    /// </summary>
    public sealed class BlockyProgramEditorWindow : EditorWindow
    {
        private const string StylesFolder = "Assets/Scripts/Blocky.Editor/Styles";
        private const string ProgramsFolder = "Assets/Demo/Programs";

        [MenuItem("Blocky/Program Editor")]
        public static void Open() => GetWindow<BlockyProgramEditorWindow>("Blocky Program Editor");

        private BlockRegistry _registry;
        private GameObject _target;
        private BlockProgramAsset _asset;
        private ProgramStore _store;
        private VisualElement _canvasHost;
        private Label _statusLabel;

        private void CreateGUI()
        {
            _registry = BlockRegistry.LoadFromResources();

            var root = rootVisualElement;
            root.style.paddingLeft = 8;
            root.style.paddingTop = 8;
            foreach (var sheetName in new[] { "blocky-base", "blocky-motion", "blocky-looks", "blocky-control", "blocky-event" })
            {
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>($"{StylesFolder}/{sheetName}.uss");
                if (sheet != null) root.styleSheets.Add(sheet);
            }

            var objectField = new ObjectField("Target GameObject") { objectType = typeof(GameObject), allowSceneObjects = true };
            objectField.RegisterValueChangedCallback(evt => SetTarget(evt.newValue as GameObject));
            root.Add(objectField);

            _statusLabel = new Label();
            root.Add(_statusLabel);

            _canvasHost = new VisualElement();
            _canvasHost.style.flexGrow = 1;
            _canvasHost.style.marginTop = 8;
            root.Add(_canvasHost);
        }

        private void SetTarget(GameObject go)
        {
            _target = go;
            _canvasHost.Clear();
            _store = null;
            _asset = null;

            if (go == null)
            {
                _statusLabel.text = "";
                return;
            }

            var runner = go.GetComponent<ObjectProgramRunner>();
            if (runner == null) runner = Undo.AddComponent<ObjectProgramRunner>(go);

            var serializedRunner = new SerializedObject(runner);
            var assetProp = serializedRunner.FindProperty("programAsset");
            _asset = assetProp.objectReferenceValue as BlockProgramAsset;

            if (_asset == null)
            {
                _asset = CreateAssetFor(go);
                assetProp.objectReferenceValue = _asset;
                serializedRunner.ApplyModifiedProperties();
                runner.SetProgramAsset(_asset);
                _statusLabel.text = $"Created a new program for '{go.name}'.";
            }
            else
            {
                _statusLabel.text = $"Editing '{go.name}'.";
            }

            var program = _asset.Load();
            _store = new ProgramStore(program);
            _store.OnChanged += _ => Persist();

            _canvasHost.Add(new ProgramCanvasView(_store, _registry, editable: true));
        }

        private static BlockProgramAsset CreateAssetFor(GameObject go)
        {
            if (!AssetDatabase.IsValidFolder(ProgramsFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Demo")) AssetDatabase.CreateFolder("Assets", "Demo");
                AssetDatabase.CreateFolder("Assets/Demo", "Programs");
            }

            var asset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            asset.Save(new ObjectProgram { targetObjectUid = go.name });
            var path = AssetDatabase.GenerateUniqueAssetPath($"{ProgramsFolder}/{go.name}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private void Persist()
        {
            if (_asset == null || _store == null) return;
            _asset.Save(_store.Program);
            EditorUtility.SetDirty(_asset);
        }

        private void OnDisable()
        {
            if (_asset != null) AssetDatabase.SaveAssets();
        }
    }
}
