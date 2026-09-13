using Blocky.Compiler;
using Blocky.Data;
using Blocky.Editor;
using Blocky.Runtime;
using Blocky.Runtime.Persistence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Blocky.Game
{
    /// <summary>
    /// In-game program authoring: press <see cref="toggleKey"/> to show a sidebar, click any object in the
    /// scene to select it, edit its blocks live while playing. Unlike <c>BlockyProgramEditorWindow</c> (Editor
    /// tooling, persists to a project asset), this never touches <c>UnityEditor</c> — it works the same in the
    /// Editor's Play mode and in a real build, and edits persist to a save file under
    /// <see cref="Application.persistentDataPath"/> via <see cref="RuntimeProgramStorage"/>, not to project assets.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class BlockyInGamePanel : MonoBehaviour
    {
        [SerializeField] private Key toggleKey = Key.Tab;
        [SerializeField] private StyleSheet[] styleSheets;
        [SerializeField] private float panelWidth = 420f;

        private UIDocument _uiDocument;
        private BlockRegistry _registry;
        private VisualElement _panelRoot;
        private Label _statusLabel;
        private VisualElement _canvasHost;

        private GameObject _target;
        private ObjectProgramRunner _runner;
        private ProgramStore _store;
        private string _storageKey;
        private bool _visible;

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
            _registry = BlockyRuntime.Registry;
            BuildChrome();
            SetVisible(false);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                SetVisible(!_visible);

            if (!_visible) return;

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            var pointer = mouse.position.ReadValue();
            if (pointer.x > Screen.width - panelWidth) return; // click landed on the sidebar itself, not the game

            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(pointer);
            if (Physics.Raycast(ray, out var hit))
                SetTarget(hit.collider.gameObject);
        }

        private void BuildChrome()
        {
            _panelRoot = _uiDocument.rootVisualElement;
            foreach (var sheet in styleSheets)
                if (sheet != null) _panelRoot.styleSheets.Add(sheet);

            _panelRoot.style.position = Position.Absolute;
            _panelRoot.style.right = 0;
            _panelRoot.style.top = 0;
            _panelRoot.style.bottom = 0;
            _panelRoot.style.width = panelWidth;
            _panelRoot.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.95f);
            _panelRoot.style.paddingLeft = 8;
            _panelRoot.style.paddingTop = 8;
            _panelRoot.style.paddingRight = 8;

            _panelRoot.Add(new Label("Blocky — click an object to edit it")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 }
            });

            _statusLabel = new Label("No object selected.");
            _panelRoot.Add(_statusLabel);

            var scrollView = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            _canvasHost = scrollView.contentContainer;
            _panelRoot.Add(scrollView);
        }

        private void SetVisible(bool value)
        {
            _visible = value;
            _panelRoot.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetTarget(GameObject go)
        {
            _target = go;
            _storageKey = go.name;
            _canvasHost.Clear();

            _runner = go.GetComponent<ObjectProgramRunner>();
            if (_runner == null) _runner = go.AddComponent<ObjectProgramRunner>();

            var program = RuntimeProgramStorage.Exists(_storageKey)
                ? RuntimeProgramStorage.Load(_storageKey)
                : _runner.ProgramAsset != null ? _runner.ProgramAsset.Load() : new ObjectProgram { targetObjectUid = go.name };

            _store = new ProgramStore(program);
            _store.OnChanged += _ => OnProgramChanged();

            _statusLabel.text = $"Editing '{go.name}'.";
            _canvasHost.Add(new ProgramCanvasView(_store, _registry, editable: true));
        }

        /// <summary>
        /// Saves to the player's save file, then hot-reloads the runner so the edit takes effect immediately —
        /// the object's own <c>OnEnable</c>/<c>Initialize</c> already ran once at scene start, so a fresh
        /// compile must be forced explicitly for a change made mid-session to actually run.
        /// </summary>
        private void OnProgramChanged()
        {
            RuntimeProgramStorage.Save(_storageKey, _store.Program);

            var liveAsset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            liveAsset.Save(_store.Program);
            _runner.Shutdown();
            _runner.SetProgramAsset(liveAsset);
            _runner.Initialize();
        }
    }
}
