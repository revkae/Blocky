using System;
using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using Blocky.Editor;
using Blocky.Localization;
using Blocky.Runtime;
using Blocky.Runtime.Persistence;
using Unity.Localization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// The statics here are fixed values that never change, so there is nothing to reset between Play sessions.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Game
{
    /// <summary>
    /// In-game program authoring, Scratch-style: press <see cref="toggleKey"/> to open a left-hand workspace
    /// (the live game stays visible to its right, like Scratch's stage), click any object in the scene to select
    /// it. The workspace has an icon rail (one tab per <see cref="BlockCategory"/>), a palette of real block
    /// shapes — clicking a tab scrolls to that category; drag the seam on its right to resize it — and the table:
    /// an unbounded surface you pan by dragging empty space with the right (or middle) button and zoom with the
    /// wheel or the +/−/= buttons. Blocks are dragged from anywhere on them, snap when connectors come close (the
    /// matching edge glows), are selected with a click (outlined) and deleted with Delete/Backspace or by dropping
    /// them on the palette. Several are picked with Shift/Ctrl+click or by dragging a box over empty table with
    /// the left button, then move, or are deleted, together. Every edit autosaves to
    /// <see cref="RuntimeProgramStorage"/> and hot-reloads the live <see cref="ObjectProgramRunner"/>. The right
    /// edge resizes the workspace. Never touches <c>UnityEditor</c>, so it works the same in Play mode and in a
    /// real build.
    /// The table works one of two ways (<see cref="WorkspaceMode"/>, toggled in the title bar): Free, everything
    /// described above, or Simple — one numbered column where a block dropped anywhere joins the nearest
    /// script, nothing lies loose, and the surface only scrolls. The program is the same either way.
    /// Made for learning: the run bar drives every script in the scene at once through <see cref="Playback"/> —
    /// Go, Stop, Reset (every programmed object back to where it started), Pause/Resume (shown only after Go,
    /// while its scripts run), Step back / Step forward (one block per script — and any script that isn't
    /// running starts, whatever hat it has, so a "when key pressed" script can be walked through too) and a
    /// 1x–4x speed — and the block a script is on lights up while it runs.
    /// Undo (button or Ctrl+Z) takes back the last edit, and plain-language advice under the table, with a badge
    /// on the block, says why a script won't do anything.
    /// Every word is in the language picked in the title bar (<see cref="BlockyLanguages"/>); switching it redraws
    /// the workspace in place, keeping the object, its undo history and the view.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class BlockyInGamePanel : MonoBehaviour
    {
        private const float MinPanelWidth = 360f;
        // A Scratch-like workspace needs a palette (~250) and a table wide enough to drop a script onto, side by
        // side. Narrower than this and the table hits its 120px floor the moment the panel opens, which is what
        // the serialized 420 used to do. Treated as the width to open at when the screen has the room; the seam
        // on the right still resizes it to anything from MinPanelWidth up.
        private const float ComfortableWidth = 900f;
        private const float MinGameStrip = 160f; // always leave this much of the game view clickable for object-picking
        private const float ResizeHandleWidth = 8f;
        private const float MinZoom = 0.3f;
        private const float MaxZoom = 2.5f;
        private const float ZoomStep = 1.2f;
        private const float ContentMargin = 16f; // gap kept between the blocks and the table's edges when panning stops
        private const int UndoSteps = 50;
        private const int MaxAdviceShown = 3;
        private const float MinPaletteWidth = 140f;
        private const float MinTableWidth = 120f; // the palette never grows past the point where the table would be narrower
        private const float BoxSelectThreshold = 4f; // pointer travel before a press on empty table becomes a selection box
        private const string ActiveSpeedClass = "blocky-runbar__speed-button--active";
        private const string ActiveTabClass = "blocky-ingame-tab--active";
        private const string ActiveModeClass = "blocky-ingame-mode__button--active";
        private const float SimpleScrollStep = 60f; // table units per wheel notch when the column can only scroll
        private const float GridSpacing = 28f;    // table units between the dots on the table
        private const float MinGridSpacing = 16f; // zoomed out below this, the grid drops a level instead of smearing
        private const float GridDotSize = 2f;
        // Painter2D skips any single Fill() over 65535 vertices. Four per dot, so this leaves a wide margin.
        private const float MaxGridDots = 4000f;
        private static readonly Vector2 DefaultPan = new(24f, 24f);

        [SerializeField] private Key toggleKey = Key.Tab;
        [SerializeField] private StyleSheet[] styleSheets;
        [SerializeField] private float panelWidth = 820f;
        [SerializeField] private float paletteWidth = 280f; // enough for the widest prototype's last parameter
        [SerializeField] private WorkspaceMode workspaceMode = WorkspaceMode.Free;

        private UIDocument _uiDocument;
        private BlockRegistry _registry;

        private VisualElement _root;
        private VisualElement _statusChip;
        private Label _statusLabel;
        private VisualElement _runBar;
        private Label _pausedFlag;
        private Button _pauseButton;
        private Button _stepBackButton;
        private Button _stepButton;
        private readonly List<Button> _speedButtons = new(); // index 0 = 1x
        private Button _undoButton;
        private Button _redoButton;
        private readonly List<(WorkspaceMode mode, Button button)> _modeButtons = new();
        private readonly List<(string code, Button button)> _languageButtons = new();
        private VisualElement _adviceList;
        private VisualElement _zoomControls;
        private VisualElement _tipsRow;
        private VisualElement _tipsCard;
        private Button _tipsToggle;
        private bool _tipsOpen;
        private VisualElement _watchers;
        private readonly Dictionary<string, Label> _watcherRows = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Label> _listWatcherRows = new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Text.StringBuilder _watcherText = new();
        private Label _zoomLabel;
        private VisualElement _tabRail;
        private readonly List<(BlockCategory category, Button tab)> _tabs = new();
        private ScrollView _paletteScroll;
        private VisualElement _canvasViewport;
        private VisualElement _resizeHandle;
        private VisualElement _paletteHandle;
        private VisualElement _selectionBox;
        private DragLayer _dragLayer;
        private readonly Dictionary<BlockCategory, VisualElement> _paletteSections = new();
        private readonly List<(BlockCategory category, List<BlockDefinition> blocks)> _blocksByCategory = new();
        private readonly List<string> _customBlocksShown = new(); // the object's custom blocks, as the palette offers them

        private bool _resizing;
        private float _resizeStartPointerX;
        private float _resizeStartWidth;

        private Vector2 _pan = DefaultPan;
        private float _zoom = 1f;
        private bool _panning;
        private int _panPointerId;
        private Vector2 _panLastPointer;

        private bool _resizingPalette;
        private float _paletteResizeStartX;
        private float _paletteResizeStartWidth;

        private bool _selectingBox;
        private bool _boxShown;     // moved far enough to be a box, not a click
        private bool _boxAdditive;  // Shift/Ctrl held: the box adds to what was selected
        private int _boxPointerId;
        private Vector2 _boxStart;
        private readonly List<BlockRef> _boxBase = new(); // the selection the box adds to
        private readonly List<BlockRef> _boxHits = new(); // reused every move

        private GameObject _target;
        private ObjectProgramRunner _runner;
        private ProgramStore _store;
        private BlockProgramAsset _liveAsset; // the in-memory asset the edited object's runner hot-reloads from — one per target, reused across edits
        private ProgramCanvasView _canvasView;
        private DragContext _dragContext;
        private string _storageKey;
        private string _storageNoticeKey; // "storage.unreadable" / "storage.save_failed" while the edited object's save has a problem, else null
        private bool _visible;

        private int _pointerOverEditorFrame = -1;
        private bool _pointerOverEditor;

        // What the run bar currently shows, so it's only touched when the playback state changes.
        private bool _runStateShown;
        private bool _shownPaused;
        private bool _shownRunning;
        private bool _shownCanStepBack;
        private bool _shownStepping;

        private readonly List<Action> _languageTexts = new(); // re-run when the language changes: each puts one element's words back
        private int _zoomLabelPercent = -1;                    // what the zoom label says, so panning doesn't re-format it

        private readonly List<string> _runningNow = new();      // filled every frame, never reallocated
        private readonly HashSet<string> _runningShown = new(); // what the table currently lights up

        private void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
            BlockyRuntimeTicker.EnsureExists(); // nothing runs without one, and it gives runners back to objects programmed in an earlier session
            _registry = BlockyRuntime.Registry;
            panelWidth = Mathf.Clamp(Mathf.Max(panelWidth, ComfortableWidth), MinPanelWidth, Mathf.Max(MinPanelWidth, Screen.width - MinGameStrip));
            GroupBlocksByCategory();
            BuildChrome();
            SetVisible(false);
            BlockyInput.IsPointerOverUi = IsPointerOverEditor; // presses on this workspace aren't "mouse down?" in the game
        }

        private void OnEnable() => LocalizationSettings.SelectedLocaleChanged += OnLanguageChanged;

        private void OnDisable() => LocalizationSettings.SelectedLocaleChanged -= OnLanguageChanged;

        private void OnDestroy()
        {
            if (BlockyInput.IsPointerOverUi == (Func<bool>)IsPointerOverEditor) BlockyInput.IsPointerOverUi = null;
        }

        /// <summary>Asked by condition blocks, possibly many times a frame — hit-tests once per frame and remembers.</summary>
        private bool IsPointerOverEditor()
        {
            if (_pointerOverEditorFrame == Time.frameCount) return _pointerOverEditor;

            _pointerOverEditorFrame = Time.frameCount;
            var mouse = Mouse.current;
            _pointerOverEditor = _visible && mouse != null && IsOverWorkspace(mouse.position.ReadValue());
            return _pointerOverEditor;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                SetVisible(!_visible);

            DriveActiveDrag(mouse);
            EndStaleTableGestures(mouse);

            if (!_visible) return;

            KeepContentInView();
            RefreshRunState();
            RefreshWatchers();
            RefreshHistoryButtons();
            UpdateRunningHighlight();

            var deletePressed = keyboard != null && (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame);
            if (deletePressed && _dragContext?.ActiveDrag == null && !IsTyping())
                DeleteSelection();

            if (!IsTyping()) // inside a text box, Ctrl+Z undoes the typing instead
            {
                if (IsRedoShortcut(keyboard)) RedoLastEdit();
                else if (IsUndoShortcut(keyboard)) UndoLastEdit();
            }

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            var pointer = mouse.position.ReadValue();
            if (IsOverWorkspace(pointer)) return; // click landed somewhere in the workspace, not the game

            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(pointer);
            if (Physics.Raycast(ray, out var hit))
                SetTarget(hit.collider.gameObject);
        }

        private static bool IsUndoShortcut(Keyboard keyboard) =>
            keyboard != null && keyboard.zKey.wasPressedThisFrame && HasCommandKey(keyboard) && !keyboard.shiftKey.isPressed;

        /// <summary>Both spellings: Ctrl+Y, and the Ctrl+Shift+Z that editors on every platform also answer to.</summary>
        private static bool IsRedoShortcut(Keyboard keyboard) =>
            keyboard != null && HasCommandKey(keyboard) &&
            (keyboard.yKey.wasPressedThisFrame || (keyboard.zKey.wasPressedThisFrame && keyboard.shiftKey.isPressed));

        private static bool HasCommandKey(Keyboard keyboard) =>
            keyboard.ctrlKey.isPressed || keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed;

        /// <summary>
        /// Real panel hit-testing instead of a fixed pixel guess — correct regardless of resizing or DPI scaling,
        /// and it swallows clicks on *any* empty space inside the workspace, not just clicks on a block. An open
        /// dropdown menu (a condition slot's list, a choice field) counts too: it lives at the panel's root, not
        /// inside the workspace, and can hang past the workspace's edge over the game.
        /// </summary>
        private bool IsOverWorkspace(Vector2 screenPointer)
        {
            if (_root.panel == null) return false;

            var point = ScreenToPanel(screenPointer);
            if (_root.ContainsPoint(_root.WorldToLocal(point))) return true;

            for (var el = _root.panel.Pick(point); el != null; el = el.parent)
                if (el.ClassListContains(GenericDropdownMenu.ussClassName)) return true;
            return false;
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
                if (el is TextField || el is FloatField || el.ClassListContains(TextInputBaseField<string>.ussClassName)) return true;
            return false;
        }

        /// <summary>Deletes every selected block in one step (one Undo brings them all back).</summary>
        private void DeleteSelection()
        {
            if (_canvasView == null || !_canvasView.HasSelection) return;

            var blocks = new List<BlockRef>(_canvasView.Selection);
            _canvasView.ClearSelection();
            _store.Apply(new DeleteBlocks(blocks));
        }

        /// <summary>Takes back the last edit to the selected object's program (history is per object and starts when it's picked).</summary>
        private void UndoLastEdit()
        {
            if (_store == null || !_store.CanUndo || _dragContext?.ActiveDrag != null) return;

            _canvasView.ClearSelection();
            _store.Undo(); // raises OnChanged like any edit: the table rebuilds, the program saves and hot-reloads
        }

        /// <summary>Puts back the last edit Undo took away. Making any fresh edit clears what could be redone.</summary>
        private void RedoLastEdit()
        {
            if (_store == null || !_store.CanRedo || _dragContext?.ActiveDrag != null) return;

            _canvasView.ClearSelection();
            _store.Redo();
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

            _root.Add(BuildHeader());

            _runBar = BuildRunBar();
            _root.Add(_runBar);

            var body = new VisualElement();
            body.AddToClassList("blocky-ingame-body");

            _tabRail = new VisualElement();
            _tabRail.AddToClassList("blocky-ingame-tabrail");
            BuildTabRail();
            body.Add(_tabRail);

            // Auto, not Hidden: the widest prototypes are wider than a 250px palette, and a slim bar that appears
            // only when something is cut off beats text that is simply unreachable.
            _paletteScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { horizontalScrollerVisibility = ScrollerVisibility.Auto };
            _paletteScroll.AddToClassList("blocky-ingame-palette");
            _paletteScroll.style.flexBasis = paletteWidth;
            body.Add(_paletteScroll);

            // The seam between the palette and the table: drag it to make the palette wider or narrower.
            _paletteHandle = new VisualElement();
            _paletteHandle.AddToClassList("blocky-ingame-palette-handle");
            _paletteHandle.RegisterCallback<PointerDownEvent>(OnPaletteResizeDown);
            _paletteHandle.RegisterCallback<PointerMoveEvent>(OnPaletteResizeMove);
            _paletteHandle.RegisterCallback<PointerUpEvent>(OnPaletteResizeUp);
            _paletteHandle.Add(Grip("blocky-ingame-palette-handle__grip"));
            body.Add(_paletteHandle);

            _canvasViewport = new VisualElement();
            _canvasViewport.AddToClassList("blocky-ingame-canvas-viewport");
            _canvasViewport.RegisterCallback<PointerDownEvent>(OnViewportPointerDown);
            _canvasViewport.RegisterCallback<PointerMoveEvent>(OnViewportPointerMove);
            _canvasViewport.RegisterCallback<PointerUpEvent>(OnViewportPointerUp);
            _canvasViewport.RegisterCallback<WheelEvent>(OnViewportWheel);
            _canvasViewport.generateVisualContent += PaintGrid; // the table's dot grid, under everything on it

            _selectionBox = new VisualElement { pickingMode = PickingMode.Ignore };
            _selectionBox.AddToClassList("blocky-selection-box");
            _selectionBox.style.display = DisplayStyle.None;
            _canvasViewport.Add(_selectionBox); // the table is inserted under it once an object is picked

            _canvasViewport.Add(BuildTableFooter());
            _canvasViewport.Add(BuildTableTools());
            _canvasViewport.Add(BuildZoomControls());
            _canvasViewport.Add(BuildWatchers());
            body.Add(_canvasViewport);

            _root.Add(body);

            _resizeHandle = new VisualElement();
            _resizeHandle.AddToClassList("blocky-ingame-resize-handle");
            _resizeHandle.style.width = ResizeHandleWidth;
            _resizeHandle.RegisterCallback<PointerDownEvent>(OnResizeDown);
            _resizeHandle.RegisterCallback<PointerMoveEvent>(OnResizeMove);
            _resizeHandle.RegisterCallback<PointerUpEvent>(OnResizeUp);
            _resizeHandle.Add(Grip("blocky-ingame-resize-handle__grip"));
            _root.Add(_resizeHandle);

            _dragLayer = new DragLayer();
            _root.Add(_dragLayer); // last sibling — always renders above everything else in the workspace

            // One drag context for the workspace's whole life, retargeted when an object is picked — so the palette
            // is built once and can be browsed before anything is selected.
            _dragContext = new DragContext(_registry, _dragLayer,
                p => _canvasViewport.worldBound.Contains(p),
                p => _paletteScroll.worldBound.Contains(p) || _tabRail.worldBound.Contains(p),
                () => _zoom);
            BuildPalette();
            SetWorkspaceMode(workspaceMode); // lights the chosen mode, fills its tips and sets the view up for it
        }

        /// <summary>
        /// The title bar: the wordmark, then what's being edited as a lit pill — a state, not a sentence — and the
        /// key that hides the workspace, drawn as a key cap so it reads as one.
        /// </summary>
        private VisualElement BuildHeader()
        {
            var bar = new VisualElement();
            bar.AddToClassList("blocky-ingame-topbar");

            var mark = new Label("B");
            mark.AddToClassList("blocky-ingame-mark");
            bar.Add(mark);

            var title = new Label("Blocky");
            title.AddToClassList("blocky-ingame-title");
            bar.Add(title);

            _statusChip = new VisualElement();
            _statusChip.AddToClassList("blocky-ingame-status");
            var dot = new VisualElement();
            dot.AddToClassList("blocky-ingame-status__dot");
            _statusChip.Add(dot);
            _statusLabel = new Label();
            _statusLabel.AddToClassList("blocky-ingame-status__label");
            Localize(() =>
            {
                if (_target == null) _statusLabel.text = BlockyText.Get("status.pick_object"); // once picked, it shows the object's name
            });
            _statusChip.Add(_statusLabel);
            bar.Add(_statusChip);

            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.style.flexGrow = 1f; // the pill hugs the object's name; this pushes the key hint to the far end
            bar.Add(spacer);

            var mode = new VisualElement();
            mode.AddToClassList("blocky-ingame-mode");
            mode.Add(ModeButton("mode.free", WorkspaceMode.Free));
            mode.Add(ModeButton("mode.simple", WorkspaceMode.Simple));
            bar.Add(mode);

            bar.Add(BuildLanguageButtons());

            var keyHint = new VisualElement();
            keyHint.AddToClassList("blocky-ingame-keyhint");
            var key = new Label(toggleKey.ToString());
            key.AddToClassList("blocky-ingame-key");
            keyHint.Add(key);
            var hides = new Label();
            hides.AddToClassList("blocky-ingame-keyhint__label");
            Localize(() => hides.text = BlockyText.Get("keyhint.hides"));
            keyHint.Add(hides);
            bar.Add(keyHint);

            return bar;
        }

        /// <summary>
        /// One button per language, beside Free / Simple and drawn like them: the lit one is the language on show,
        /// and clicking another switches the whole workspace (<see cref="OnLanguageChanged"/>) and is remembered for
        /// next time. Each is named the way the language names itself ("English", "Türkçe"); past three languages
        /// they show just the code ("DE") to stay narrow, with the name in the tooltip. A language file added to
        /// <c>Resources/Languages</c> gets its button with no change here.
        /// </summary>
        private VisualElement BuildLanguageButtons()
        {
            var group = new VisualElement();
            group.AddToClassList("blocky-ingame-mode"); // the same segmented pill as Free / Simple
            group.AddToClassList("blocky-ingame-language");

            var locales = BlockyLanguages.Available;
            foreach (var locale in locales)
            {
                var code = locale.Code;
                var name = locale.LocaleName;
                var button = new Button(() => BlockyLanguages.Select(code))
                {
                    text = locales.Count <= 3 ? name : code.ToUpperInvariant() // an id, so invariant casing
                };
                button.AddToClassList("blocky-ingame-mode__button");
                Localize(() => button.tooltip = $"{BlockyText.Get("language.tooltip")}: {name}");
                _languageButtons.Add((code, button));
                group.Add(button);
            }

            ShowLanguage();
            return group;
        }

        /// <summary>Lights the button of the language on show.</summary>
        private void ShowLanguage()
        {
            var current = BlockyLanguages.Current?.Code;
            foreach (var (code, button) in _languageButtons)
                button.EnableInClassList(ActiveModeClass, string.Equals(code, current, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Puts words on an element now, and again whenever the language changes.</summary>
        private void Localize(Action apply)
        {
            _languageTexts.Add(apply);
            apply();
        }

        private void OnLanguageChanged(Locale _) => ApplyLanguage();

        /// <summary>
        /// Rewords the workspace in place: the chrome, the tips, the palette and the table (block names, fields and
        /// advice). The object being edited, its undo history, the mode and the view all stay as they were.
        /// </summary>
        private void ApplyLanguage()
        {
            ShowLanguage();
            foreach (var apply in _languageTexts) apply();
            FillTips();
            RebuildPalette();
            _runStateShown = false; // Pause/Resume is re-worded with the run state next frame
            _zoomLabelPercent = -1;
            ShowZoom();
            _canvasView?.Refresh(); // raises Rebuilt, which re-works the advice too
        }

        /// <summary><paramref name="key"/> names the button; <c>key.tooltip</c> says what the mode is.</summary>
        private Button ModeButton(string key, WorkspaceMode mode)
        {
            var button = new Button(() => SetWorkspaceMode(mode));
            button.AddToClassList("blocky-ingame-mode__button");
            Localize(() =>
            {
                button.text = BlockyText.Get(key);
                button.tooltip = BlockyText.Get(key + ".tooltip");
            });
            _modeButtons.Add((mode, button));
            return button;
        }

        /// <summary>
        /// Switches the table between the free Scratch surface and the strict single column
        /// (<see cref="WorkspaceMode"/>). Only the view and the drop rules change — the program itself is never
        /// touched, so the same object can be opened either way, and switched back and forth.
        /// </summary>
        private void SetWorkspaceMode(WorkspaceMode mode)
        {
            workspaceMode = mode;
            _dragContext.Mode = mode;
            if (_canvasView != null) _canvasView.Mode = mode;

            foreach (var (candidate, button) in _modeButtons)
                button.EnableInClassList(ActiveModeClass, candidate == mode);

            _zoomControls.style.display = mode == WorkspaceMode.Free ? DisplayStyle.Flex : DisplayStyle.None;
            FillTips();
            ResetView();
        }

        /// <summary>The little bar inside a seam that says "drag me".</summary>
        private static VisualElement Grip(string className)
        {
            var grip = new VisualElement { pickingMode = PickingMode.Ignore };
            grip.AddToClassList(className);
            return grip;
        }

        /// <summary>
        /// The table's dot grid, painted straight onto the viewport — so it sits under the canvas and its blocks —
        /// and stepped by the pan and the zoom, which is what makes moving around feel like moving over a surface
        /// instead of sliding a picture. One path and one Fill, the single-pass rule <see cref="BlockOutline"/>
        /// follows; the dots are tiny squares rather than arcs, so a screenful is a few thousand triangles instead
        /// of tens of thousands.
        /// </summary>
        private void PaintGrid(MeshGenerationContext ctx)
        {
            if (workspaceMode == WorkspaceMode.Simple) return; // the column rules its own numbered lines instead

            var size = _canvasViewport.layout.size;
            if (float.IsNaN(size.x) || float.IsNaN(size.y) || size.x <= 0f || size.y <= 0f) return;

            var spacing = GridSpacing * _zoom;
            while (spacing < MinGridSpacing) spacing *= 2f; // zoomed out: drop every other dot rather than smear them
            // A big window — or a transient layout pass reporting a viewport far larger than the screen — can ask
            // for more dots than one Fill() can hold, and Unity then skips the whole grid. Thin it out instead.
            while (size.x / spacing * (size.y / spacing) > MaxGridDots) spacing *= 2f;

            var painter = ctx.painter2D;
            painter.fillColor = new Color(0.08f, 0.12f, 0.20f, 0.14f);
            painter.BeginPath();

            const float half = GridDotSize / 2f;
            for (var x = Mathf.Repeat(_pan.x, spacing); x < size.x; x += spacing)
                for (var y = Mathf.Repeat(_pan.y, spacing); y < size.y; y += spacing)
                {
                    painter.MoveTo(new Vector2(x - half, y - half));
                    painter.LineTo(new Vector2(x + half, y - half));
                    painter.LineTo(new Vector2(x + half, y + half));
                    painter.LineTo(new Vector2(x - half, y + half));
                    painter.ClosePath();
                }

            painter.Fill();
        }

        /// <summary>
        /// Run controls for the whole scene: Go / Stop / Reset, Pause (only while something runs), Step back / Step
        /// forward, and the speed. Words rather than icons, so a child (or a teacher) doesn't have to guess.
        /// Commands go through <see cref="BlockyRuntime.Playback"/> each click, never a cached copy — it is rebuilt
        /// between play sessions.
        /// </summary>
        private VisualElement BuildRunBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("blocky-runbar");

            var transport = Tray(bar);
            transport.Add(RunButton("run.go", () => BlockyRuntime.Playback.Go(), "blocky-runbar__go"));
            transport.Add(RunButton("run.stop", () => BlockyRuntime.Playback.Stop(), "blocky-runbar__stop"));
            transport.Add(RunButton("run.reset", () => BlockyRuntime.Playback.ResetWorld(), null));

            var stepping = Tray(bar);
            _pauseButton = RunButton(null, TogglePause, null); // Pause or Resume: RefreshRunState words it
            stepping.Add(_pauseButton);
            _stepBackButton = RunButton("run.step_back", () => BlockyRuntime.Playback.StepBack(), "blocky-runbar__step");
            stepping.Add(_stepBackButton);
            _stepButton = RunButton("run.step_forward", () => BlockyRuntime.Playback.StepForward(), null);
            stepping.Add(_stepButton);

            _pausedFlag = new Label { pickingMode = PickingMode.Ignore };
            _pausedFlag.AddToClassList("blocky-runbar__flag");
            Localize(() => _pausedFlag.text = BlockyText.Get("run.paused"));
            _pausedFlag.style.display = DisplayStyle.None;
            bar.Add(_pausedFlag);

            var speed = new VisualElement();
            speed.AddToClassList("blocky-runbar__speed");
            var speedLabel = new Label();
            speedLabel.AddToClassList("blocky-runbar__speed-label");
            Localize(() => speedLabel.text = BlockyText.ToUpper(BlockyText.Get("run.speed"))); // a small-caps caption, like the palette's
            speed.Add(speedLabel);
            for (var value = 1; value <= Playback.MaxSpeed; value++)
            {
                var chosen = value;
                var button = new Button(() => SetSpeed(chosen)) { text = $"{value}x" };
                button.AddToClassList("blocky-runbar__speed-button");
                _speedButtons.Add(button);
                speed.Add(button);
            }
            bar.Add(speed);
            ShowSpeed(BlockyRuntime.Playback.Speed);

            return bar;
        }

        /// <summary>A recessed tray for controls that belong together, the way a transport bar groups play and stop.</summary>
        private static VisualElement Tray(VisualElement parent)
        {
            var tray = new VisualElement();
            tray.AddToClassList("blocky-runbar__group");
            parent.Add(tray);
            return tray;
        }

        /// <summary>A run-bar button named by the string <paramref name="key"/>; a null key leaves the wording to the caller.</summary>
        private Button RunButton(string key, Action onClick, string modifierClass)
        {
            var button = new Button(onClick);
            button.AddToClassList("blocky-runbar__button");
            if (modifierClass != null) button.AddToClassList(modifierClass);
            if (key != null) Localize(() => button.text = BlockyText.Get(key));
            return button;
        }

        private static void TogglePause()
        {
            var playback = BlockyRuntime.Playback;
            if (playback.IsPaused) playback.Resume();
            else playback.Pause();
        }

        private void SetSpeed(int speed)
        {
            var playback = BlockyRuntime.Playback;
            playback.SetSpeed(speed);
            ShowSpeed(playback.Speed);
        }

        private void ShowSpeed(int speed)
        {
            for (var i = 0; i < _speedButtons.Count; i++)
                _speedButtons[i].EnableInClassList(ActiveSpeedClass, i + 1 == speed);
        }

        /// <summary>
        /// Keeps the run bar in step with the playback state, which Go, Step and scripts finishing also change: Pause
        /// shows only after Go, while the scripts are still running (as Resume while paused) — not for scripts that
        /// started on their own at scene start — Step back only has something to go back to after a step forward,
        /// Step forward waits for a timed block to finish, and the bar turns amber while paused.
        /// </summary>
        /// <summary>
        /// Scratch's stage monitors: every shared variable and list, with its value, while a program is running. It
        /// shows itself only once something has set one, so a table with no variables on it has no card in the
        /// corner. Per-object ones are not listed — there is one row per name, and ten objects each with their own
        /// "hits" would be ten rows saying different things.
        /// </summary>
        private VisualElement BuildWatchers()
        {
            _watchers = new VisualElement { pickingMode = PickingMode.Ignore };
            _watchers.AddToClassList("blocky-watchers");
            _watchers.style.display = DisplayStyle.None;
            return _watchers;
        }

        /// <summary>Called every frame the workspace is open: rows are reused and only their text is rewritten.</summary>
        private void RefreshWatchers()
        {
            var shared = BlockyRuntime.Variables.Shared;
            var lists = BlockyRuntime.Variables.SharedLists;
            var any = shared.Count + lists.Count > 0;
            _watchers.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            if (!any)
            {
                if (_watcherRows.Count + _listWatcherRows.Count > 0) ClearWatchers();
                return;
            }

            foreach (var pair in shared)
                ShowWatcher(_watcherRows, pair.Key, $"{pair.Key}  {pair.Value.AsText()}");
            foreach (var pair in lists)
                ShowWatcher(_listWatcherRows, pair.Key, ListWatcherText(pair.Key, pair.Value));

            if (_watcherRows.Count == shared.Count && _listWatcherRows.Count == lists.Count) return;

            // A variable or a list can only disappear when the play session is reset, which drops the whole store at once.
            ClearWatchers();
        }

        private void ShowWatcher(Dictionary<string, Label> rows, string name, string text)
        {
            if (!rows.TryGetValue(name, out var row))
            {
                row = new Label();
                row.AddToClassList("blocky-watchers__row");
                _watchers.Add(row);
                rows[name] = row;
            }

            if (row.text != text) row.text = text; // a Label rebuilds its mesh on every assignment, equal or not
        }

        private void ClearWatchers()
        {
            _watchers.Clear();
            _watcherRows.Clear();
            _listWatcherRows.Clear();
        }

        /// <summary>"name  [a, b, c]"; a longer list shows its first few items and its length — "[a, b, c, d, e, …]  (12)". A watcher is a glance, not a table.</summary>
        private string ListWatcherText(string name, List<BlockValue> items)
        {
            const int shown = 5;
            _watcherText.Clear().Append(name).Append("  [");
            for (var i = 0; i < items.Count && i < shown; i++)
            {
                if (i > 0) _watcherText.Append(", ");
                _watcherText.Append(items[i].AsText());
            }

            if (items.Count <= shown) return _watcherText.Append(']').ToString();
            return _watcherText.Append(", …]  (").Append(items.Count).Append(')').ToString();
        }

        private void RefreshRunState()
        {
            var playback = BlockyRuntime.Playback;
            var paused = playback.IsPaused;
            var running = playback.IsGoing;
            var canStepBack = playback.CanStepBack;
            var stepping = playback.IsStepping;
            if (_runStateShown && paused == _shownPaused && running == _shownRunning && canStepBack == _shownCanStepBack && stepping == _shownStepping)
                return;

            _runStateShown = true;
            _shownPaused = paused;
            _shownRunning = running;
            _shownCanStepBack = canStepBack;
            _shownStepping = stepping;

            _pauseButton.text = BlockyText.Get(paused ? "run.resume" : "run.pause");
            _pauseButton.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            _pausedFlag.style.display = paused ? DisplayStyle.Flex : DisplayStyle.None;
            _stepBackButton.SetEnabled(canStepBack);
            _stepButton.SetEnabled(!stepping);
            _runBar.EnableInClassList("blocky-runbar--paused", paused);
        }

        private void RefreshHistoryButtons()
        {
            var canUndo = _store != null && _store.CanUndo;
            if (_undoButton.enabledSelf != canUndo) _undoButton.SetEnabled(canUndo);

            var canRedo = _store != null && _store.CanRedo;
            if (_redoButton.enabledSelf != canRedo) _redoButton.SetEnabled(canRedo);
        }

        /// <summary>Lights up the block each of this object's scripts is on. Touches the table only when that set changes.</summary>
        private void UpdateRunningHighlight()
        {
            if (_canvasView == null) return;

            RunningBlocks.Collect(BlockyRuntime.Scheduler, _target, _runningNow);
            if (IsShownAlready(_runningNow)) return;

            _runningShown.Clear();
            foreach (var nodeId in _runningNow) _runningShown.Add(nodeId);
            _canvasView.SetRunning(_runningNow);
        }

        private bool IsShownAlready(List<string> nodeIds)
        {
            if (nodeIds.Count != _runningShown.Count) return false;
            foreach (var nodeId in nodeIds)
                if (!_runningShown.Contains(nodeId)) return false;
            return true;
        }

        /// <summary>Advice rows (filled per edit) above the how-to line, along the bottom of the table. Empty space in it still pans the table.</summary>
        private VisualElement BuildTableFooter()
        {
            var footer = new VisualElement { pickingMode = PickingMode.Ignore };
            footer.AddToClassList("blocky-table-footer");

            _adviceList = new VisualElement { pickingMode = PickingMode.Ignore };
            _adviceList.AddToClassList("blocky-advice-list");
            _adviceList.style.display = DisplayStyle.None;
            footer.Add(_adviceList);

            _tipsCard = new VisualElement { pickingMode = PickingMode.Ignore };
            _tipsCard.AddToClassList("blocky-tips-card");
            _tipsCard.style.display = DisplayStyle.None;
            footer.Add(_tipsCard);

            _tipsRow = new VisualElement();
            _tipsRow.AddToClassList("blocky-tips");
            _tipsToggle = new Button(ToggleTips) { text = "?" };
            _tipsToggle.AddToClassList("blocky-tips__toggle");
            Localize(() => _tipsToggle.tooltip = BlockyText.Get("tips.toggle.tooltip"));
            _tipsRow.Add(_tipsToggle); // always the first child: FillTips replaces everything after it
            footer.Add(_tipsRow);
            return footer;
        }

        /// <summary>The two modes are used differently, so the tips are filled from the mode rather than built once.</summary>
        private void FillTips()
        {
            var simple = workspaceMode == WorkspaceMode.Simple;

            _tipsCard.Clear();
            foreach (var line in simple ? SimpleTipLines : FreeTipLines)
            {
                var row = new Label(BlockyText.Get(line)) { pickingMode = PickingMode.Ignore };
                row.AddToClassList("blocky-tips-card__line");
                _tipsCard.Add(row);
            }

            while (_tipsRow.childCount > 1) _tipsRow.RemoveAt(_tipsRow.childCount - 1);
            foreach (var chip in simple ? SimpleTipChips : FreeTipChips)
            {
                var label = new Label(BlockyText.Get(chip)) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("blocky-tip");
                _tipsRow.Add(label);
            }
        }

        /// <summary>Always on show, one glance each — the whole list is behind the "?". String keys, as are the lines.</summary>
        private static readonly string[] FreeTipChips = TipKeys("tips.free.chip", 4);
        private static readonly string[] SimpleTipChips = TipKeys("tips.simple.chip", 4);
        private static readonly string[] FreeTipLines = TipKeys("tips.free.line", 6);
        private static readonly string[] SimpleTipLines = TipKeys("tips.simple.line", 7);

        private static string[] TipKeys(string prefix, int count)
        {
            var keys = new string[count];
            for (var i = 0; i < count; i++) keys[i] = $"{prefix}.{i + 1}";
            return keys;
        }

        private void ToggleTips()
        {
            _tipsOpen = !_tipsOpen;
            _tipsCard.style.display = _tipsOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _tipsToggle.EnableInClassList("blocky-tips__toggle--open", _tipsOpen);
        }

        private VisualElement BuildTableTools()
        {
            var tools = new VisualElement { pickingMode = PickingMode.Ignore };
            tools.AddToClassList("blocky-table-tools");

            _undoButton = new Button(UndoLastEdit);
            _undoButton.AddToClassList("blocky-table-tools__button");
            Localize(() =>
            {
                _undoButton.text = BlockyText.Get("table.undo");
                _undoButton.tooltip = BlockyText.Get("table.undo.tooltip");
            });
            _undoButton.SetEnabled(false);
            tools.Add(_undoButton);

            _redoButton = new Button(RedoLastEdit);
            _redoButton.AddToClassList("blocky-table-tools__button");
            Localize(() =>
            {
                _redoButton.text = BlockyText.Get("table.redo");
                _redoButton.tooltip = BlockyText.Get("table.redo.tooltip");
            });
            _redoButton.SetEnabled(false);
            tools.Add(_redoButton); // under Undo: the tools column reads top to bottom
            return tools;
        }

        /// <summary>
        /// Problems (a script won't run) first, then hints; each row selects its block when clicked. The rest stay as
        /// badges on the blocks. Above them all, a line when this object's save couldn't be read or written.
        /// </summary>
        private void ShowAdvice(List<Advice> advice)
        {
            _adviceList.Clear();

            if (_storageNoticeKey != null)
            {
                var notice = new Label(BlockyText.Format(_storageNoticeKey, _target != null ? _target.name : string.Empty)) { pickingMode = PickingMode.Ignore };
                notice.AddToClassList("blocky-advice");
                notice.AddToClassList("blocky-advice--problem");
                _adviceList.Add(notice);
            }

            var shown = 0;
            foreach (var kind in new[] { AdviceKind.Problem, AdviceKind.Hint })
                foreach (var item in advice)
                {
                    if (item.Kind != kind || shown == MaxAdviceShown) continue;
                    _adviceList.Add(AdviceRow(item));
                    shown++;
                }

            if (advice.Count > shown)
            {
                var more = new Label(BlockyText.Format("advice.more", advice.Count - shown)) { pickingMode = PickingMode.Ignore };
                more.AddToClassList("blocky-advice-more");
                _adviceList.Add(more);
            }

            _adviceList.style.display = advice.Count > 0 || _storageNoticeKey != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private Button AdviceRow(Advice item)
        {
            var row = new Button(() => _canvasView?.Select(item.StackId, item.NodeId)) { text = item.Message };
            row.AddToClassList("blocky-advice");
            row.AddToClassList(item.Kind == AdviceKind.Problem ? "blocky-advice--problem" : "blocky-advice--hint");
            return row;
        }

        private VisualElement BuildZoomControls()
        {
            _zoomControls = new VisualElement();
            var controls = _zoomControls;
            controls.AddToClassList("blocky-zoom-controls");
            controls.Add(ZoomButton("+", () => ZoomAround(ViewportCenter, _zoom * ZoomStep)));
            controls.Add(ZoomButton("−", () => ZoomAround(ViewportCenter, _zoom / ZoomStep)));
            controls.Add(ZoomButton("=", ResetView));

            _zoomLabel = new Label { pickingMode = PickingMode.Ignore }; // worded by ShowZoom
            _zoomLabel.AddToClassList("blocky-zoom-level");
            controls.Add(_zoomLabel);
            return controls;
        }

        /// <summary>The zoom as a percentage, written the language's way ("%150" in Turkish) — only when it changes.</summary>
        private void ShowZoom()
        {
            var percent = Mathf.RoundToInt(_zoom * 100f);
            if (_zoomLabel == null || percent == _zoomLabelPercent) return;
            _zoomLabelPercent = percent;
            _zoomLabel.text = BlockyText.Format("table.zoom", percent);
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
                if (el is IBlockElement || el is UnknownBlockView || el is Button) return false;
            return true;
        }

        /// <summary>
        /// Empty table: the left button draws a selection box (a plain click clears the selection; with Shift/Ctrl the
        /// box adds to it), the right or middle button pans.
        /// </summary>
        private void OnViewportPointerDown(PointerDownEvent evt)
        {
            if (_canvasView == null || _panning || _selectingBox || !IsTableBackground(evt.target as VisualElement)) return;

            // Simple mode has no selection box, so empty table is there to scroll the column, with any button.
            if (workspaceMode == WorkspaceMode.Simple)
            {
                // A press on a block's numbered band has already picked that block — don't immediately unpick it.
                if (evt.button == 0 && CanvasRow.Of(evt.target as VisualElement) == null) _canvasView.ClearSelection();
                BeginPan(evt);
                return;
            }

            if (evt.button == 0) BeginBoxSelect(evt);
            else if (evt.button == 1 || evt.button == 2) BeginPan(evt);
        }

        private void BeginPan(PointerDownEvent evt)
        {
            _panning = true;
            _panPointerId = evt.pointerId;
            _panLastPointer = evt.position;
            _canvasViewport.CapturePointer(evt.pointerId);
        }

        private void OnViewportPointerMove(PointerMoveEvent evt)
        {
            if (_selectingBox && evt.pointerId == _boxPointerId)
            {
                UpdateBoxSelect(evt.position);
                return;
            }
            if (!_panning || evt.pointerId != _panPointerId) return;

            var pointer = (Vector2)evt.position;
            _pan += pointer - _panLastPointer;
            _panLastPointer = pointer;
            ApplyCanvasTransform();
        }

        private void OnViewportPointerUp(PointerUpEvent evt)
        {
            if (_selectingBox && evt.pointerId == _boxPointerId)
            {
                UpdateBoxSelect(evt.position);
                EndBoxSelect();
                return;
            }
            if (!_panning || evt.pointerId != _panPointerId) return;

            EndPan();
        }

        private void EndPan()
        {
            _panning = false;
            if (_canvasViewport.HasPointerCapture(_panPointerId)) _canvasViewport.ReleasePointer(_panPointerId);
        }

        private void BeginBoxSelect(PointerDownEvent evt)
        {
            _selectingBox = true;
            _boxShown = false;
            _boxPointerId = evt.pointerId;
            _boxStart = evt.position;
            _boxAdditive = evt.shiftKey || evt.actionKey;
            _boxBase.Clear();
            if (_boxAdditive) _boxBase.AddRange(_canvasView.Selection);
            _canvasViewport.CapturePointer(evt.pointerId);
        }

        /// <summary>Selects every block the box touches (plus, with Shift/Ctrl, what was selected before), live as it's drawn.</summary>
        private void UpdateBoxSelect(Vector2 pointer)
        {
            if (_canvasView == null) return;
            if (!_boxShown && Vector2.Distance(pointer, _boxStart) < BoxSelectThreshold) return; // still a click
            _boxShown = true;

            var world = Rect.MinMaxRect(Mathf.Min(_boxStart.x, pointer.x), Mathf.Min(_boxStart.y, pointer.y),
                Mathf.Max(_boxStart.x, pointer.x), Mathf.Max(_boxStart.y, pointer.y));
            var min = _canvasViewport.WorldToLocal(world.min);
            var max = _canvasViewport.WorldToLocal(world.max);
            _selectionBox.style.left = min.x;
            _selectionBox.style.top = min.y;
            _selectionBox.style.width = max.x - min.x;
            _selectionBox.style.height = max.y - min.y;
            _selectionBox.style.display = DisplayStyle.Flex;

            _boxHits.Clear();
            _boxHits.AddRange(_boxBase);
            _canvasView.FindBlocksIn(world, _boxHits);
            _canvasView.SetSelection(_boxHits);
        }

        private void EndBoxSelect()
        {
            if (!_boxShown && !_boxAdditive) _canvasView?.ClearSelection(); // a plain click on empty table
            _selectingBox = false;
            _boxShown = false;
            _selectionBox.style.display = DisplayStyle.None;
            if (_canvasViewport.HasPointerCapture(_boxPointerId)) _canvasViewport.ReleasePointer(_boxPointerId);
        }

        /// <summary>A release the UI never saw (over the game view, outside the window) must still end a selection box or a pan.</summary>
        private void EndStaleTableGestures(Mouse mouse)
        {
            if (mouse == null) return;
            if (_selectingBox && !mouse.leftButton.isPressed) EndBoxSelect();
            if (_panning && !IsPanButtonHeld(mouse)) EndPan();
        }

        /// <summary>Simple mode pans with the left button too — there's no selection box there to use it for.</summary>
        private bool IsPanButtonHeld(Mouse mouse) =>
            mouse.rightButton.isPressed || mouse.middleButton.isPressed ||
            (workspaceMode == WorkspaceMode.Simple && mouse.leftButton.isPressed);

        private void OnViewportWheel(WheelEvent evt)
        {
            if (_canvasView == null) return;

            if (workspaceMode == WorkspaceMode.Simple)
            {
                _pan.y -= evt.delta.y * SimpleScrollStep; // a column has nothing to zoom: the wheel scrolls it
                ApplyCanvasTransform();
            }
            else
            {
                var factor = evt.delta.y > 0f ? 1f / ZoomStep : ZoomStep;
                ZoomAround(_canvasViewport.WorldToLocal(evt.mousePosition), _zoom * factor);
            }
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
            _pan = workspaceMode == WorkspaceMode.Simple
                ? Vector2.zero // the column starts at the table's top-left corner; from there it only moves up and down
                : ContentBounds() is { } b ? new Vector2(ContentMargin - b.xMin, ContentMargin - b.yMin) : DefaultPan;
            ApplyCanvasTransform();
        }

        private void ApplyCanvasTransform()
        {
            _canvasViewport.MarkDirtyRepaint(); // the dot grid is painted from _pan and _zoom
            ShowZoom();
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

            // One column, pinned to the table's left edge: only its vertical position can change.
            if (workspaceMode == WorkspaceMode.Simple)
                return new Vector2(0f, ClampAxis(pan.y, b.yMin, b.yMax, viewport.height));

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

        private void OnPaletteResizeDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            _resizingPalette = true;
            _paletteResizeStartX = evt.position.x;
            _paletteResizeStartWidth = _paletteScroll.layout.width; // what's on screen — a narrow workspace may have shrunk it below paletteWidth
            _paletteHandle.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        /// <summary>Widens or narrows the palette, never below <see cref="MinPaletteWidth"/> nor so far the table gets narrower than <see cref="MinTableWidth"/>.</summary>
        private void OnPaletteResizeMove(PointerMoveEvent evt)
        {
            if (!_resizingPalette) return;

            var bodyWidth = _paletteScroll.parent.layout.width;
            var maxWidth = Mathf.Max(MinPaletteWidth, bodyWidth - _tabRail.layout.width - _paletteHandle.layout.width - MinTableWidth);
            paletteWidth = Mathf.Clamp(_paletteResizeStartWidth + evt.position.x - _paletteResizeStartX, MinPaletteWidth, maxWidth);
            _paletteScroll.style.flexBasis = paletteWidth;
        }

        private void OnPaletteResizeUp(PointerUpEvent evt)
        {
            _resizingPalette = false;
            _paletteHandle.ReleasePointer(evt.pointerId);
        }

        /// <summary>The registry's blocks by category, in enum order, empty categories left out — shared by the tab rail and the palette so they always agree.</summary>
        private void GroupBlocksByCategory()
        {
            var byCategory = new Dictionary<BlockCategory, List<BlockDefinition>>();
            for (var opcode = 0; opcode < _registry.Count; opcode++)
            {
                var def = _registry.GetByOpcode(opcode);
                if (!byCategory.TryGetValue(def.category, out var list))
                    byCategory[def.category] = list = new List<BlockDefinition>();
                list.Add(def);
            }

            foreach (BlockCategory category in Enum.GetValues(typeof(BlockCategory)))
                if (byCategory.TryGetValue(category, out var defs)) _blocksByCategory.Add((category, defs));
        }

        private void BuildTabRail()
        {
            foreach (var (category, _) in _blocksByCategory)
            {
                var tab = new Button(() => SelectCategory(category));
                tab.AddToClassList("blocky-ingame-tab");
                _tabs.Add((category, tab));

                var dot = new VisualElement();
                dot.AddToClassList("blocky-ingame-tab__dot");
                dot.AddToClassList(BlockClasses.Category(category));
                tab.Add(dot);

                var label = new Label(category.DisplayName());
                label.AddToClassList("blocky-ingame-tab__label");
                tab.Add(label);

                _tabRail.Add(tab);
            }

            // The palette opens scrolled to the top, so the first category is the one on show.
            if (_tabs.Count > 0) _tabs[0].tab.AddToClassList(ActiveTabClass);
        }

        /// <summary>Scrolls the palette to that category and lights its tab, so the rail says where you are.</summary>
        private void SelectCategory(BlockCategory category)
        {
            if (_paletteSections.TryGetValue(category, out var section))
            {
                _paletteScroll.ScrollTo(section);
                // ScrollTo also scrolls sideways to fit the widest block in the section, which would leave every
                // block clipped on its left. The category is what was asked for, so only the vertical move counts.
                _paletteScroll.scrollOffset = new Vector2(0f, _paletteScroll.scrollOffset.y);
            }

            foreach (var (candidate, tab) in _tabs)
                tab.EnableInClassList(ActiveTabClass, candidate == category);
        }

        /// <summary>Builds the tab rail and the palette again in the current language, staying on the same category and scroll.</summary>
        private void RebuildPalette()
        {
            var active = _tabs.Find(t => t.tab.ClassListContains(ActiveTabClass)).category;
            var scroll = _paletteScroll.scrollOffset;

            _tabRail.Clear();
            _tabs.Clear();
            BuildTabRail();
            foreach (var (category, tab) in _tabs) tab.EnableInClassList(ActiveTabClass, category == active);

            _paletteScroll.Clear();
            _paletteSections.Clear();
            BuildPalette();
            _paletteScroll.scrollOffset = scroll;
        }

        private void BuildPalette()
        {
            foreach (var (category, defs) in _blocksByCategory)
            {
                var section = new VisualElement();
                section.AddToClassList("blocky-palette__section");

                // A caption with the category's own dot in front of it - USS has no text-transform, so the
                // capitals are made here, the way the language writes them (Turkish i is İ).
                var title = new VisualElement { pickingMode = PickingMode.Ignore };
                title.AddToClassList("blocky-palette__section-title");
                var dot = new VisualElement();
                dot.AddToClassList("blocky-palette__section-dot");
                dot.AddToClassList(BlockClasses.Category(category));
                title.Add(dot);
                var caption = new Label(BlockyText.ToUpper(category.DisplayName()));
                caption.AddToClassList("blocky-palette__section-label");
                title.Add(caption);
                section.Add(title);

                foreach (var def in defs)
                {
                    var item = BlockPrototype.Create(def, _registry);
                    item.AddManipulator(new PaletteDragManipulator(item, def, _dragContext));
                    section.Add(item);
                }

                if (category == BlockCategory.MyBlocks) AddCustomBlockRuns(section);

                _paletteScroll.Add(section);
                _paletteSections[category] = section;
            }
        }

        /// <summary>
        /// A ready-made "run [name]" for each custom block this object defines, under the plain one — the way Scratch
        /// lists a sprite's own blocks under My Blocks, so running one is a drag rather than typing its name again.
        /// </summary>
        private void AddCustomBlockRuns(VisualElement section)
        {
            var run = _registry.Find(CustomBlocks.RunType);
            if (run == null) return;

            foreach (var name in _customBlocksShown)
            {
                BlockNode MakeRun()
                {
                    var node = BlockNodes.Instantiate(run);
                    var nameParam = Array.Find(node.parameters, p => p.key == CustomBlocks.NameKey);
                    if (nameParam != null) nameParam.text = name;
                    return node;
                }

                var item = BlockPrototype.Create(run, _registry, MakeRun());
                item.AddManipulator(new PaletteDragManipulator(item, run, _dragContext, MakeRun));
                section.Add(item);
            }
        }

        /// <summary>Rebuilds the palette when the object's custom blocks change: a define added, renamed or deleted, or another object picked.</summary>
        private void RefreshCustomBlocks()
        {
            var names = _store != null ? CustomBlocks.DefinedNames(_store.Program) : new List<string>();
            if (SameNames(names, _customBlocksShown)) return;

            _customBlocksShown.Clear();
            _customBlocksShown.AddRange(names);
            RebuildPalette();
        }

        private static bool SameNames(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>
        /// The canvas rebuilds its views on every change (undo included), so the fresh ones need their drag handles
        /// again, and the advice is worked out anew for the changed program.
        /// </summary>
        private void OnCanvasRebuilt()
        {
            foreach (var stackView in _canvasView.StackViews.Values)
                foreach (var element in stackView.Query<VisualElement>().Where(e => e is IBlockElement).ToList())
                    element.AddManipulator(new CanvasDragManipulator(element, _dragContext));

            var advice = ProgramAdvice.Collect(_store.Program, _registry);
            _canvasView.SetAdvice(advice);
            ShowAdvice(advice);
            RefreshCustomBlocks();
        }

        private void SetVisible(bool value)
        {
            _visible = value;
            _root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetTarget(GameObject go)
        {
            _target = go;
            _storageKey = RuntimeProgramStorage.KeyFor(go);

            _runner = go.GetComponent<ObjectProgramRunner>();
            if (_runner == null) _runner = go.AddComponent<ObjectProgramRunner>();

            // Exactly what the object runs: its save from an earlier session when it has one, else its scene program.
            var program = _runner.LoadProgram();
            if (string.IsNullOrEmpty(program.targetObjectUid)) program.targetObjectUid = go.name;
            ProgramUpgrades.UpgradeCheckboxConditions(program, _registry); // old checkbox conditions become condition blocks (saved with the next edit)
            _storageNoticeKey = _runner.SavedProgram == SavedProgramStatus.Unreadable ? "storage.unreadable" : null;

            _store = new ProgramStore(program) { UndoCapacity = UndoSteps };
            _store.OnChanged += _ => OnProgramChanged();
            _liveAsset = null; // the previous object's runner keeps its own live asset; this object gets a fresh one on its first edit

            _statusLabel.text = go.name; // the lit dot in front of it already says "this is what you're editing"
            _statusChip.AddToClassList("blocky-ingame-status--live");

            _canvasView?.RemoveFromHierarchy();
            _canvasView = new ProgramCanvasView(_store, _registry, CanvasMode.Table) { Mode = workspaceMode };
            _canvasView.style.position = Position.Absolute;
            _canvasView.style.left = 0;
            _canvasView.style.top = 0;
            _canvasView.style.width = Length.Percent(100);
            _canvasView.style.height = Length.Percent(100);
            _canvasView.style.transformOrigin = new TransformOrigin(new Length(0f), new Length(0f), 0f);
            _canvasViewport.Insert(0, _canvasView); // under the advice, the tools and the zoom controls

            _pan = DefaultPan;
            _zoom = 1f;
            ApplyCanvasTransform();

            _dragContext.SetTarget(_store, _canvasView);
            _runningShown.Clear(); // a fresh canvas lights nothing up yet

            _canvasView.Rebuilt += OnCanvasRebuilt;
            OnCanvasRebuilt(); // the first build happened inside the constructor, before we could subscribe
        }

        /// <summary>
        /// Saves to the player's save file, then hot-reloads the runner so the edit takes effect immediately —
        /// the object's own <c>OnEnable</c>/<c>Initialize</c> already ran once at scene start, so a fresh
        /// compile must be forced explicitly for a change made mid-session to actually run. The step history is
        /// dropped first: its saved scripts belong to the program as it was before this edit.
        /// </summary>
        private void OnProgramChanged()
        {
            // A failed save keeps the edit (it still runs) and says so under the table; the next good save clears it.
            _storageNoticeKey = RuntimeProgramStorage.TrySave(_storageKey, _store.Program, out _) ? null : "storage.save_failed";

            if (_liveAsset == null) _liveAsset = ScriptableObject.CreateInstance<BlockProgramAsset>();
            _liveAsset.Save(_store.Program);
            BlockyRuntime.Playback.ForgetSteps();
            _runner.Shutdown();
            _runner.SetProgramAsset(_liveAsset);
            _runner.Initialize();
        }
    }
}
