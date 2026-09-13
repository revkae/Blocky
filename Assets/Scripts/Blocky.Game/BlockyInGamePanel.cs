using System;
using System.Collections.Generic;
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
    /// In-game program authoring, Scratch-style: press <see cref="toggleKey"/> to open a left-hand workspace
    /// (the live game stays visible to its right, like Scratch's stage), click any object in the scene to select
    /// it. The workspace has an icon rail (one tab per <see cref="BlockCategory"/> present in the registry), a
    /// scrollable block palette — clicking a tab scrolls the palette to that category's section — and a canvas
    /// where blocks are dragged from the palette and snapped into place (<see cref="PaletteDragManipulator"/>).
    /// A green "Go" button fires the <c>event.when_go_clicked</c> trigger. Every structural edit flows through
    /// <see cref="ProgramStore.OnChanged"/>, which autosaves to <see cref="RuntimeProgramStorage"/> and
    /// hot-reloads the live <see cref="ObjectProgramRunner"/> — there is no separate "save" step.
    /// The right edge is a drag handle that resizes the workspace. Never touches <c>UnityEditor</c>, so it
    /// works the same in the Editor's Play mode and in a real build.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class BlockyInGamePanel : MonoBehaviour
    {
        private const float MinPanelWidth = 360f;
        private const float MinGameStrip = 160f; // always leave this much of the game view clickable for object-picking
        private const float ResizeHandleWidth = 8f;
        private const float CanvasMargin = 200f; // empty room past the furthest stack, so there's always somewhere to drop

        [SerializeField] private Key toggleKey = Key.Tab;
        [SerializeField] private StyleSheet[] styleSheets;
        [SerializeField] private float panelWidth = 820f;

        private UIDocument _uiDocument;
        private BlockRegistry _registry;

        private VisualElement _root;
        private Label _statusLabel;
        private VisualElement _tabRail;
        private ScrollView _paletteScroll;
        private ScrollView _canvasScroll;
        private VisualElement _resizeHandle;
        private DragLayer _dragLayer;
        private readonly Dictionary<BlockCategory, VisualElement> _paletteSections = new();

        private bool _resizing;
        private float _resizeStartPointerX;
        private float _resizeStartWidth;

        private GameObject _target;
        private ObjectProgramRunner _runner;
        private ProgramStore _store;
        private ProgramCanvasView _canvasView;
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

            FitCanvasToContent();

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            var pointer = mouse.position.ReadValue();
            if (IsOverWorkspace(pointer)) return; // click landed somewhere in the workspace, not the game

            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(pointer);
            if (Physics.Raycast(ray, out var hit))
                SetTarget(hit.collider.gameObject);
        }

        /// <summary>
        /// Real panel hit-testing instead of a fixed pixel guess — correct regardless of resizing or DPI scaling,
        /// and it swallows clicks on *any* empty space inside the workspace, not just clicks on a block.
        /// </summary>
        private bool IsOverWorkspace(Vector2 screenPointer)
        {
            if (_root.panel == null) return false;
            var panelPoint = RuntimePanelUtils.ScreenToPanel(_root.panel, screenPointer);
            return _root.ContainsPoint(_root.WorldToLocal(panelPoint));
        }

        /// <summary>
        /// <see cref="ProgramCanvasView"/> positions every stack absolutely, so on its own it contributes no size to
        /// layout — it collapses to the height of its one in-flow child ("+ Add Stack"), which both clips the stacks
        /// and shrinks the drop area to a thin strip. This sizes it to at least the visible viewport, and grows it
        /// past the furthest stack so the scroll view can reach everything. Only writes a style when the value
        /// actually changes, so the per-frame call never forces a relayout on its own.
        /// </summary>
        private void FitCanvasToContent()
        {
            if (_canvasView == null) return;

            var viewport = _canvasScroll.contentViewport.layout;
            if (float.IsNaN(viewport.width) || float.IsNaN(viewport.height)) return;

            var width = viewport.width;
            var height = viewport.height;
            foreach (var child in _canvasView.Children())
            {
                // Only the absolutely-positioned stacks. The in-flow "+ Add Stack" row stretches to the canvas's
                // own width, so measuring it would feed the canvas width back into itself and grow it every frame.
                if (child is not StackView) continue;
                var r = child.layout;
                if (float.IsNaN(r.xMax) || float.IsNaN(r.yMax)) continue;
                width = Mathf.Max(width, r.xMax + CanvasMargin);
                height = Mathf.Max(height, r.yMax + CanvasMargin);
            }

            if (!Mathf.Approximately(_canvasView.resolvedStyle.width, width)) _canvasView.style.width = width;
            if (!Mathf.Approximately(_canvasView.resolvedStyle.height, height)) _canvasView.style.height = height;
        }

        private void BuildChrome()
        {
            _root = _uiDocument.rootVisualElement;
            foreach (var sheet in styleSheets)
                if (sheet != null) _root.styleSheets.Add(sheet);

            // Unity's default document root already sets left/right to 0; with left, right and width all set, the
            // layout engine keeps left + width and ignores right. Say so explicitly instead of relying on that.
            _root.AddToClassList("blocky-ingame-root");
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.right = StyleKeyword.Auto;
            _root.style.top = 0;
            _root.style.bottom = 0;
            _root.style.width = panelWidth;
            _root.style.paddingRight = ResizeHandleWidth; // reserves the strip the absolutely-positioned handle sits in
            _root.style.flexDirection = FlexDirection.Column;

            var topBar = new VisualElement();
            topBar.AddToClassList("blocky-ingame-topbar");
            var titleLabel = new Label("Blocky");
            titleLabel.AddToClassList("blocky-ingame-title");
            topBar.Add(titleLabel);

            _statusLabel = new Label("Press Tab, then click an object to code it.");
            _statusLabel.AddToClassList("blocky-ingame-status");
            topBar.Add(_statusLabel);

            var goButton = new Button(() => BlockyRuntime.Triggers.FireGoClicked()) { text = "▶ Go" };
            goButton.AddToClassList("blocky-ingame-go-button");
            topBar.Add(goButton);
            _root.Add(topBar);

            var body = new VisualElement();
            body.AddToClassList("blocky-ingame-body");

            _tabRail = new VisualElement();
            _tabRail.AddToClassList("blocky-ingame-tabrail");
            BuildTabRail();
            body.Add(_tabRail);

            _paletteScroll = new ScrollView(ScrollViewMode.Vertical);
            _paletteScroll.AddToClassList("blocky-ingame-palette");
            body.Add(_paletteScroll);

            _canvasScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _canvasScroll.AddToClassList("blocky-ingame-canvas-scroll");
            body.Add(_canvasScroll);

            _root.Add(body);

            _resizeHandle = new VisualElement();
            _resizeHandle.AddToClassList("blocky-ingame-resize-handle");
            _resizeHandle.style.width = ResizeHandleWidth;
            _resizeHandle.RegisterCallback<PointerDownEvent>(OnResizeDown);
            _resizeHandle.RegisterCallback<PointerMoveEvent>(OnResizeMove);
            _resizeHandle.RegisterCallback<PointerUpEvent>(OnResizeUp);
            _root.Add(_resizeHandle);

            _dragLayer = new DragLayer();
            _root.Add(_dragLayer); // last sibling — always renders above everything else in the workspace
        }

        private void OnResizeDown(PointerDownEvent evt)
        {
            _resizing = true;
            _resizeStartPointerX = evt.position.x;
            _resizeStartWidth = panelWidth;
            _resizeHandle.CapturePointer(evt.pointerId);
        }

        private void OnResizeMove(PointerMoveEvent evt)
        {
            if (!_resizing) return;

            // The workspace is anchored to the left edge and the handle is on its right edge,
            // so dragging right (positive delta) grows it.
            var delta = evt.position.x - _resizeStartPointerX;
            var maxWidth = Mathf.Max(MinPanelWidth, Screen.width - MinGameStrip);
            panelWidth = Mathf.Clamp(_resizeStartWidth + delta, MinPanelWidth, maxWidth);
            _root.style.width = panelWidth;
        }

        private void OnResizeUp(PointerUpEvent evt)
        {
            _resizing = false;
            _resizeHandle.ReleasePointer(evt.pointerId);
        }

        private void BuildTabRail()
        {
            _tabRail.Clear();

            var present = new HashSet<BlockCategory>();
            for (var i = 0; i < _registry.Count; i++) present.Add(_registry.GetByOpcode(i).category);

            foreach (BlockCategory category in Enum.GetValues(typeof(BlockCategory)))
            {
                if (!present.Contains(category)) continue;

                var tab = new Button(() => ScrollToCategory(category));
                tab.AddToClassList("blocky-ingame-tab");

                var dot = new VisualElement();
                dot.AddToClassList("blocky-ingame-tab__dot");
                dot.AddToClassList($"blocky-block--category-{category.ToString().ToLowerInvariant()}");
                tab.Add(dot);

                var label = new Label(category.ToString());
                label.AddToClassList("blocky-ingame-tab__label");
                tab.Add(label);

                _tabRail.Add(tab);
            }
        }

        private void ScrollToCategory(BlockCategory category)
        {
            if (_paletteSections.TryGetValue(category, out var section))
                _paletteScroll.ScrollTo(section);
        }

        private void BuildPalette()
        {
            _paletteScroll.Clear();
            _paletteSections.Clear();

            var byCategory = new Dictionary<BlockCategory, List<BlockDefinition>>();
            for (var opcode = 0; opcode < _registry.Count; opcode++)
            {
                var def = _registry.GetByOpcode(opcode);
                if (!byCategory.TryGetValue(def.category, out var list))
                    byCategory[def.category] = list = new List<BlockDefinition>();
                list.Add(def);
            }

            foreach (BlockCategory category in Enum.GetValues(typeof(BlockCategory)))
            {
                if (!byCategory.TryGetValue(category, out var defs)) continue;

                var section = new VisualElement();
                section.AddToClassList("blocky-palette__section");

                var title = new Label(category.ToString());
                title.AddToClassList("blocky-palette__section-title");
                section.Add(title);

                foreach (var def in defs)
                {
                    var item = new Label(string.IsNullOrEmpty(def.displayNameKey) ? def.blockType : def.displayNameKey);
                    item.AddToClassList("blocky-block");
                    item.AddToClassList($"blocky-block--category-{category.ToString().ToLowerInvariant()}");
                    item.AddToClassList("blocky-palette__prototype");
                    item.AddManipulator(new PaletteDragManipulator(item, def, _store, _canvasView, _dragLayer));
                    section.Add(item);
                }

                _paletteScroll.Add(section);
                _paletteSections[category] = section;
            }
        }

        private void SetVisible(bool value)
        {
            _visible = value;
            _root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetTarget(GameObject go)
        {
            _target = go;
            _storageKey = go.name;
            _canvasScroll.Clear();

            _runner = go.GetComponent<ObjectProgramRunner>();
            if (_runner == null) _runner = go.AddComponent<ObjectProgramRunner>();

            var program = RuntimeProgramStorage.Exists(_storageKey)
                ? RuntimeProgramStorage.Load(_storageKey)
                : _runner.ProgramAsset != null ? _runner.ProgramAsset.Load() : new ObjectProgram { targetObjectUid = go.name };

            _store = new ProgramStore(program);
            _store.OnChanged += _ => OnProgramChanged();

            _statusLabel.text = $"Editing '{go.name}'.";

            _canvasView = new ProgramCanvasView(_store, _registry, editable: true);
            _canvasScroll.Add(_canvasView);

            BuildPalette(); // rebuilt per target: each item's drag manipulator closes over this object's store/canvas
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
