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
    /// it. The workspace has an icon rail (one tab per <see cref="BlockCategory"/>), a palette of real block
    /// shapes — clicking a tab scrolls to that category — and the table: an unbounded surface you pan by
    /// dragging empty space and zoom with the wheel or the +/−/= buttons. Blocks are dragged from anywhere on
    /// them, snap when connectors come close (the matching edge glows), are selected with a click (outlined) and
    /// deleted with Delete/Backspace or by dropping them on the palette. A green "Go" button fires
    /// <c>event.when_go_clicked</c>. Every edit autosaves to <see cref="RuntimeProgramStorage"/> and hot-reloads
    /// the live <see cref="ObjectProgramRunner"/>. The right edge resizes the workspace. Never touches
    /// <c>UnityEditor</c>, so it works the same in Play mode and in a real build.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class BlockyInGamePanel : MonoBehaviour
    {
        private const float MinPanelWidth = 360f;
        private const float MinGameStrip = 160f; // always leave this much of the game view clickable for object-picking
        private const float ResizeHandleWidth = 8f;
        private const float MinZoom = 0.3f;
        private const float MaxZoom = 2.5f;
        private const float ZoomStep = 1.2f;
        private const float ContentMargin = 16f; // gap kept between the blocks and the table's edges when panning stops
        private static readonly Vector2 DefaultPan = new(24f, 24f);

        [SerializeField] private Key toggleKey = Key.Tab;
        [SerializeField] private StyleSheet[] styleSheets;
        [SerializeField] private float panelWidth = 820f;

        private UIDocument _uiDocument;
        private BlockRegistry _registry;

        private VisualElement _root;
        private Label _statusLabel;
        private VisualElement _tabRail;
        private ScrollView _paletteScroll;
        private VisualElement _canvasViewport;
        private VisualElement _resizeHandle;
        private DragLayer _dragLayer;
        private readonly Dictionary<BlockCategory, VisualElement> _paletteSections = new();

        private bool _resizing;
        private float _resizeStartPointerX;
        private float _resizeStartWidth;

        private Vector2 _pan = DefaultPan;
        private float _zoom = 1f;
        private bool _panning;
        private int _panPointerId;
        private Vector2 _panLastPointer;

        private GameObject _target;
        private ObjectProgramRunner _runner;
        private ProgramStore _store;
        private ProgramCanvasView _canvasView;
        private DragContext _dragContext;
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
            var mouse = Mouse.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                SetVisible(!_visible);

            DriveActiveDrag(mouse);

            if (!_visible) return;

            KeepContentInView();

            var deletePressed = keyboard != null && (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame);
            if (deletePressed && _dragContext?.ActiveDrag == null && !IsTyping())
                DeleteSelection();

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
            return _root.ContainsPoint(_root.WorldToLocal(ScreenToPanel(screenPointer)));
        }

        /// <summary>
        /// Input System positions are bottom-left-origin screen pixels; UI Toolkit panels are top-left-origin, and
        /// <see cref="RuntimePanelUtils.ScreenToPanel"/> takes the top-left form. Without the flip the position
        /// comes out vertically mirrored.
        /// </summary>
        private Vector2 ScreenToPanel(Vector2 screenPointer) =>
            RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screenPointer.x, Screen.height - screenPointer.y));

        /// <summary>
        /// Drives any open press/drag from the real mouse, every frame. UI events alone aren't reliable enough:
        /// a pointer-up is lost when the button is released where the UI doesn't receive input (over the game
        /// view, outside the window), and a control inside a block (checkbox, text box) captures the pointer on
        /// press and keeps the move events to itself. The physical button and position are the ground truth.
        /// </summary>
        private void DriveActiveDrag(Mouse mouse)
        {
            var drag = _dragContext?.ActiveDrag;
            if (drag == null || mouse == null || _root.panel == null) return;

            var pointer = ScreenToPanel(mouse.position.ReadValue());
            if (mouse.leftButton.isPressed) drag.Poll(pointer);
            else drag.ForceEnd(pointer);
        }

        /// <summary>Delete/Backspace inside a number or text box edits the text — it must not delete the block.</summary>
        private bool IsTyping()
        {
            for (var el = _root.panel?.focusController?.focusedElement as VisualElement; el != null; el = el.parent)
                if (el is TextField || el is FloatField || el.ClassListContains("unity-base-text-field")) return true;
            return false;
        }

        private void DeleteSelection()
        {
            if (_canvasView == null || !_canvasView.HasSelection) return;

            var stackId = _canvasView.SelectedStackId;
            var nodeId = _canvasView.SelectedNodeId;
            _canvasView.ClearSelection();
            _store.Apply(new DeleteBlock(stackId, nodeId));
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

            _canvasViewport = new VisualElement();
            _canvasViewport.AddToClassList("blocky-ingame-canvas-viewport");
            _canvasViewport.RegisterCallback<PointerDownEvent>(OnViewportPointerDown);
            _canvasViewport.RegisterCallback<PointerMoveEvent>(OnViewportPointerMove);
            _canvasViewport.RegisterCallback<PointerUpEvent>(OnViewportPointerUp);
            _canvasViewport.RegisterCallback<WheelEvent>(OnViewportWheel);

            var hint = new Label("Drag blocks anywhere · click to select, click again to unselect · Delete removes · drag empty space to move · scroll to zoom");
            hint.AddToClassList("blocky-ingame-hint");
            hint.pickingMode = PickingMode.Ignore;
            _canvasViewport.Add(hint);
            _canvasViewport.Add(BuildZoomControls());
            body.Add(_canvasViewport);

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

        private VisualElement BuildZoomControls()
        {
            var controls = new VisualElement();
            controls.AddToClassList("blocky-zoom-controls");
            controls.Add(ZoomButton("+", () => ZoomAround(ViewportCenter, _zoom * ZoomStep)));
            controls.Add(ZoomButton("−", () => ZoomAround(ViewportCenter, _zoom / ZoomStep)));
            controls.Add(ZoomButton("=", ResetView));
            return controls;
        }

        private static Button ZoomButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("blocky-zoom-button");
            return button;
        }

        private Vector2 ViewportCenter => _canvasViewport.layout.size / 2f;

        /// <summary>Empty table (not a block, not a button): pressing here pans, and clears the selection.</summary>
        private bool IsTableBackground(VisualElement hit)
        {
            for (var el = hit; el != null && el != _canvasViewport; el = el.parent)
                if (el is BlockView || el is HatView || el is UnknownBlockView || el is Button) return false;
            return true;
        }

        private void OnViewportPointerDown(PointerDownEvent evt)
        {
            if (_canvasView == null || _panning || !IsTableBackground(evt.target as VisualElement)) return;

            _canvasView.ClearSelection();
            _panning = true;
            _panPointerId = evt.pointerId;
            _panLastPointer = evt.position;
            _canvasViewport.CapturePointer(evt.pointerId);
        }

        private void OnViewportPointerMove(PointerMoveEvent evt)
        {
            if (!_panning || evt.pointerId != _panPointerId) return;

            var pointer = (Vector2)evt.position;
            _pan += pointer - _panLastPointer;
            _panLastPointer = pointer;
            ApplyCanvasTransform();
        }

        private void OnViewportPointerUp(PointerUpEvent evt)
        {
            if (!_panning || evt.pointerId != _panPointerId) return;

            _panning = false;
            _canvasViewport.ReleasePointer(evt.pointerId);
        }

        private void OnViewportWheel(WheelEvent evt)
        {
            if (_canvasView == null) return;

            var factor = evt.delta.y > 0f ? 1f / ZoomStep : ZoomStep;
            ZoomAround(_canvasViewport.WorldToLocal(evt.mousePosition), _zoom * factor);
            evt.StopPropagation();
        }

        /// <summary>Zooms while keeping the table point under <paramref name="viewportPoint"/> still, so zooming goes "into" the cursor.</summary>
        private void ZoomAround(Vector2 viewportPoint, float newZoom)
        {
            newZoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom);
            var tablePoint = (viewportPoint - _pan) / _zoom;
            _zoom = newZoom;
            _pan = viewportPoint - tablePoint * _zoom;
            ApplyCanvasTransform();
        }

        /// <summary>"=": back to 100%, with the blocks' top-left corner at the table's top-left.</summary>
        private void ResetView()
        {
            _zoom = 1f;
            _pan = ContentBounds() is { } b ? new Vector2(ContentMargin - b.xMin, ContentMargin - b.yMin) : DefaultPan;
            ApplyCanvasTransform();
        }

        private void ApplyCanvasTransform()
        {
            if (_canvasView == null) return;
            if (_dragContext?.ActiveDrag == null) _pan = ClampPan(_pan); // mid-drag, the grabbed stack isn't on the table — don't clamp to what's left
            _canvasView.style.translate = new Translate(new Length(_pan.x), new Length(_pan.y), 0f);
            _canvasView.style.scale = new Scale(new Vector3(_zoom, _zoom, 1f));
        }

        /// <summary>Re-clamps when the content or the viewport changed under us — a delete, a drop near an edge, a resize.</summary>
        private void KeepContentInView()
        {
            if (_canvasView == null || _panning || _dragContext?.ActiveDrag != null) return;
            if (ClampPan(_pan) != _pan) ApplyCanvasTransform();
        }

        /// <summary>
        /// Keeps the blocks on screen. While everything fits in the table, it can be moved around but stops at the
        /// edges; once it's bigger than the table (zoomed in), you can pan across it but never past its far side
        /// into empty space. Either way no block can end up where you can't see it — zoom out to see more.
        /// </summary>
        private Vector2 ClampPan(Vector2 pan)
        {
            var viewport = _canvasViewport.layout;
            if (ContentBounds() is not { } b || float.IsNaN(viewport.width) || float.IsNaN(viewport.height)) return pan;

            return new Vector2(
                ClampAxis(pan.x, b.xMin, b.xMax, viewport.width),
                ClampAxis(pan.y, b.yMin, b.yMax, viewport.height));
        }

        private float ClampAxis(float pan, float contentMin, float contentMax, float viewportSize)
        {
            var nearEdgeAtNearSide = ContentMargin - contentMin * _zoom;             // content's start touches the table's start
            var farEdgeAtFarSide = viewportSize - ContentMargin - contentMax * _zoom; // content's end touches the table's end
            // Fits: stay between those two. Too big: the same two limits swap, so the content always covers the view.
            return Mathf.Clamp(pan, Mathf.Min(nearEdgeAtNearSide, farEdgeAtFarSide), Mathf.Max(nearEdgeAtNearSide, farEdgeAtFarSide));
        }

        /// <summary>Union of every stack on the table, in table units; null when the table is empty or not laid out yet.</summary>
        private Rect? ContentBounds()
        {
            if (_canvasView == null) return null;

            Rect? bounds = null;
            foreach (var stackView in _canvasView.StackViews.Values)
            {
                if (stackView.parent != _canvasView) continue; // this stack is the one being dragged
                var r = stackView.layout;
                if (float.IsNaN(r.width) || float.IsNaN(r.height)) return null;
                bounds = bounds is { } b
                    ? Rect.MinMaxRect(Mathf.Min(b.xMin, r.xMin), Mathf.Min(b.yMin, r.yMin), Mathf.Max(b.xMax, r.xMax), Mathf.Max(b.yMax, r.yMax))
                    : r;
            }
            return bounds;
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
                    var item = BlockPrototype.Create(def, _registry);
                    item.AddManipulator(new PaletteDragManipulator(item, def, _dragContext));
                    section.Add(item);
                }

                _paletteScroll.Add(section);
                _paletteSections[category] = section;
            }
        }

        /// <summary>The canvas rebuilds its views on every change, so the fresh ones need their drag handles again.</summary>
        private void AttachCanvasDrag()
        {
            foreach (var stackView in _canvasView.StackViews.Values)
            {
                if (stackView.Hat != null) stackView.Hat.AddManipulator(new CanvasDragManipulator(stackView.Hat, _dragContext));
                foreach (var block in stackView.Query<BlockView>().ToList())
                    block.AddManipulator(new CanvasDragManipulator(block, _dragContext));
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

            _runner = go.GetComponent<ObjectProgramRunner>();
            if (_runner == null) _runner = go.AddComponent<ObjectProgramRunner>();

            var program = RuntimeProgramStorage.Exists(_storageKey)
                ? RuntimeProgramStorage.Load(_storageKey)
                : _runner.ProgramAsset != null ? _runner.ProgramAsset.Load() : new ObjectProgram { targetObjectUid = go.name };

            _store = new ProgramStore(program);
            _store.OnChanged += _ => OnProgramChanged();

            _statusLabel.text = $"Editing '{go.name}'";

            _canvasView?.RemoveFromHierarchy();
            _canvasView = new ProgramCanvasView(_store, _registry, tableMode: true);
            _canvasView.style.position = Position.Absolute;
            _canvasView.style.left = 0;
            _canvasView.style.top = 0;
            _canvasView.style.width = Length.Percent(100);
            _canvasView.style.height = Length.Percent(100);
            _canvasView.style.transformOrigin = new TransformOrigin(new Length(0f), new Length(0f), 0f);
            _canvasViewport.Insert(0, _canvasView); // under the zoom controls

            _pan = DefaultPan;
            _zoom = 1f;
            ApplyCanvasTransform();

            _dragContext = new DragContext(_store, _registry, _canvasView, _dragLayer,
                p => _canvasViewport.worldBound.Contains(p),
                p => _paletteScroll.worldBound.Contains(p) || _tabRail.worldBound.Contains(p),
                () => _zoom);

            _canvasView.Rebuilt += AttachCanvasDrag;
            AttachCanvasDrag(); // the first build happened inside the constructor, before we could subscribe

            BuildPalette(); // rebuilt per target: each item's drag closes over this object's store and canvas
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
