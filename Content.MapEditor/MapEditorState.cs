using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.MapEditor.Commands;
using Content.MapEditor.Systems;
using Content.MapEditor.Tools;
using Content.MapEditor.UI;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Content.Client.Power.Visualizers;
using Content.Server.MapEditor;
using Content.Shared.SubFloor;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.MapEditor;

/// <summary>
///     Main editor state. Split across partial class files by responsibility:
///     - MapEditorState.cs (this file): fields, lifecycle, status bar, view toggles
///     - MapEditorState.Grids.cs: grid tab management
///     - MapEditorState.Camera.cs: pan, zoom, hover highlight, placement preview, shape preview
///     - MapEditorState.Input.cs: keyboard shortcut polling
///     - MapEditorState.Tools.cs: tool dispatch, selection callbacks, mouse input
///     - MapEditorState.FileIO.cs: map open/save
///     - MapEditorState.Infrastructure.cs: infrastructure mode and cable connection recompute
/// </summary>
public sealed partial class MapEditorState : State
{
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IFileDialogManager _fileDialog = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefs = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    private ISawmill _sawmill = default!;
    private MapEditorScreen _screen = default!;
    private Eye _eye = default!;

    // Camera pan state
    private bool _isPanning;
    private ScreenCoordinates _lastMouseScreen;

    // Zoom limits
    private const float MinZoom = 0.05f;
    private const float MaxZoom = 10f;

    // Loaded map tracking
    private MapId _loadedMapId;

    // Active grid: all tool operations target this grid.
    private EntityUid _activeGridUid;

    // Tool system
    private readonly CommandStack _commandStack = new();
    private ToolContext _toolContext = default!;
    private IEditorTool _activeTool = new PaintTool();
    private string _activeToolKey = "paint";
    private bool _isToolActive; // true while left mouse is held and tool is in a stroke
    private bool _wasLeftDown;
    private Vector2i _lastToolTilePos;

    // Hover highlight overlay
    private EditorOverlay _editorOverlay = default!;

    // Keyboard shortcut edge detection (tracks previous frame state to detect press edges)
    private bool _wasBDown;
    private bool _wasEDown;
    private bool _wasIDown;
    private bool _wasFDown;
    private bool _wasRDown;
    private bool _wasLDown;
    private bool _wasCDown;
    private bool _wasSDown;
    private bool _wasXDown;
    private bool _wasVDown;
    private bool _wasZDown;
    private bool _wasYDown;
    private bool _wasDeleteDown;
    private bool _wasGDown;
    private bool _wasQDown;
    private bool _wasJDown;
    private bool _wasKDown;

    // Entity outline shader for selection highlight.
    private ShaderInstance? _selectionOutlineShader;
    private EntityUid? _outlinedEntity;
    private Texture? _dragGhostTexture;

    // Cable connection recompute flag set when entities are added/removed/moved.
    private bool _cablesDirty;
    private float _cableRecomputeTimer;

    // Infrastructure mode hides non-infrastructure entities and shows subfloor.
    private bool _infrastructureMode;
    private Dictionary<EntityUid, bool>? _savedVisibility;

    // Editor Category List
    public Dictionary<string, EditorCategory> EditorCategories { get; } = new();


    /// <summary>
    ///     Component types that remain visible during infrastructure mode.
    ///     Entities with any of these components are considered "infrastructure".
    /// </summary>
    private static readonly Type[] InfrastructureComponents =
    {
        typeof(SubFloorHideComponent),            // cables, pipes, subfloor entities
        typeof(CableVisualizerComponent),         // cable visualization
        typeof(Content.Shared.Power.Components.BatteryComponent), // SMES, batteries
        typeof(Content.Shared.Atmos.Components.AtmosDeviceComponent), // vents, scrubbers
        typeof(Content.Client.Power.APC.ApcVisualsComponent), // APCs
    };

    public MapEditorState()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Startup()
    {
        _sawmill = _logManager.GetSawmill("map_editor");
        _sawmill.Info("MapEditorState started");

        _uiManager.LoadScreen<MapEditorScreen>();
        _screen = (MapEditorScreen) _uiManager.ActiveScreen!;

        // Create a dedicated eye for the editor and point the viewport at it.
        _eye = new Eye
        {
            Zoom = Vector2.One,
            DrawFov = false,
            DrawLight = false,
        };
        _eyeManager.CurrentEye = _eye;
        _eyeManager.MainViewport = _screen.MainViewport.Viewport;

        // Initialize tool context.
        _toolContext = new ToolContext
        {
            EntityManager = _entityManager,
            MapSystem = _entityManager.System<SharedMapSystem>(),
            CommandStack = _commandStack,
            TileDefinitionManager = _tileDefs,
        };

        // Wire menu button events.
        _screen.FileNewButton.OnPressed += OnFileNewPressed;
        _screen.FileOpenButton.OnPressed += OnFileOpenPressed;
        _screen.FileSaveButton.OnPressed += OnFileSavePressed;
        _screen.FileExitButton.OnPressed += OnFileExitPressed;
        _screen.EditUndoButton.OnPressed += OnUndoPressed;
        _screen.EditRedoButton.OnPressed += OnRedoPressed;
        _screen.ViewResetZoomButton.OnPressed += OnResetZoomPressed;

        // Wire scroll-wheel zoom from the viewport overlay.
        _screen.OnViewportScroll += OnViewportScroll;

        // Wire toolbar and palette events.
        _screen.OnToolSelected += OnToolSelected;
        _screen.OnTileSelected += OnTileSelected;

        // Wire grid tab events.
        _screen.OnGridTabSelected += OnGridTabSelected;
        _screen.OnGridTabDeleted += OnGridTabDeleted;
        _screen.OnAddGridPressed += OnAddGridPressed;

        // Wire entity palette events.
        _screen.OnEntityPrototypeSelected += OnEntityPrototypeSelected;

        // Wire infrastructure panel events.
        _screen.OnCableTypeSelected += OnCableTypeSelected;
        _screen.OnPipeTypeSelected += OnPipeTypeSelected;

        // Wire view toggle events.
        _screen.ViewShowEntitiesButton.OnPressed += OnToggleShowEntities;
        _screen.ViewShowSubfloorButton.OnPressed += OnToggleShowSubfloor;

        // Wire entity info panel button events.
        _screen.OnEntityRotateCW += OnEntityInfoRotateCW;
        _screen.OnEntityRotateCCW += OnEntityInfoRotateCCW;
        _screen.OnEntityDelete += OnEntityInfoDelete;
        _screen.OnEntityDeselect += OnEntityInfoDeselect;

        // Populate the tile palette.
        _screen.PopulateTilePalette(_tileDefs);

        // Populate the entity palette.
        _screen.PopulateEntityPalette(_prototypeManager);

        // Register hover highlight overlay.
        _editorOverlay = new EditorOverlay();
        IoCManager.Resolve<IOverlayManager>().AddOverlay(_editorOverlay);

        // Prepare the selection outline shader (uses the game's existing outline shader).
        // Set fullbright since the editor has DrawLight=false, otherwise the outline is invisible.
        var outlineProtoId = new ProtoId<ShaderPrototype>("SelectionOutlineInrange");
        _selectionOutlineShader = _prototypeManager.Index(outlineProtoId).InstanceUnique();
        _selectionOutlineShader.SetParameter("outline_fullbright", true);
        _selectionOutlineShader.SetParameter("outline_width", 4.0f);
        _selectionOutlineShader.SetParameter("outline_color", new Color(0.1f, 1.0f, 0.3f, 0.8f));

        // Build the Editor Palette Category Prototype tree.
        BuildProtoIndex();

        // Set initial toolbar state.
        _screen.SetActiveToolButton(_activeToolKey);

        // Create a default empty map so the editor isn't a black void on startup.
        CreateNewMap();
    }

    /// <summary>
    ///     Creates a new empty map with a single grid and positions the camera at the origin.
    /// </summary>
    private void CreateNewMap()
    {
        var mapSystem = _entityManager.System<SharedMapSystem>();
        mapSystem.CreateMap(out var mapId);
        _loadedMapId = mapId;

        // Create a grid on the new map.
        var newGrid = _mapManager.CreateGridEntity(mapId);
        var gridUid = newGrid.Owner;

        // Position the eye on the new map.
        _eye.Position = new MapCoordinates(new Vector2(0, 0), mapId);
        _eye.Zoom = Vector2.One;

        // Populate grid tabs.
        PopulateGridTabs(mapId);

        _screen.SetStatusInfo("New map");
        _sawmill.Info($"Created new map {mapId} with grid {gridUid}");
    }

    protected override void Shutdown()
    {
        // Restore visibility if infrastructure mode was active.
        if (_infrastructureMode)
            DeactivateInfrastructureMode();

        _screen.FileNewButton.OnPressed -= OnFileNewPressed;
        _screen.FileOpenButton.OnPressed -= OnFileOpenPressed;
        _screen.FileSaveButton.OnPressed -= OnFileSavePressed;
        _screen.FileExitButton.OnPressed -= OnFileExitPressed;
        _screen.EditUndoButton.OnPressed -= OnUndoPressed;
        _screen.EditRedoButton.OnPressed -= OnRedoPressed;
        _screen.ViewResetZoomButton.OnPressed -= OnResetZoomPressed;
        _screen.OnViewportScroll -= OnViewportScroll;
        _screen.OnToolSelected -= OnToolSelected;
        _screen.OnTileSelected -= OnTileSelected;
        _screen.OnGridTabSelected -= OnGridTabSelected;
        _screen.OnGridTabDeleted -= OnGridTabDeleted;
        _screen.OnAddGridPressed -= OnAddGridPressed;
        _screen.OnEntityPrototypeSelected -= OnEntityPrototypeSelected;
        _screen.OnCableTypeSelected -= OnCableTypeSelected;
        _screen.OnPipeTypeSelected -= OnPipeTypeSelected;
        _screen.OnEntityRotateCW -= OnEntityInfoRotateCW;
        _screen.OnEntityRotateCCW -= OnEntityInfoRotateCCW;
        _screen.OnEntityDelete -= OnEntityInfoDelete;
        _screen.OnEntityDeselect -= OnEntityInfoDeselect;
        _screen.ViewShowEntitiesButton.OnPressed -= OnToggleShowEntities;
        _screen.ViewShowSubfloorButton.OnPressed -= OnToggleShowSubfloor;

        // Remove outline from any selected entity.
        if (_outlinedEntity != null
            && _entityManager.EntityExists(_outlinedEntity.Value)
            && _entityManager.TryGetComponent<SpriteComponent>(_outlinedEntity.Value, out var outlinedSprite))
        {
            outlinedSprite.PostShader = null;
            outlinedSprite.RenderOrder = 0;
        }
        _outlinedEntity = null;

        IoCManager.Resolve<IOverlayManager>().RemoveOverlay(_editorOverlay);

        _uiManager.UnloadScreen();

        _sawmill.Info("MapEditorState shutdown");
    }

    public override void FrameUpdate(FrameEventArgs e)
    {
        UpdatePan();
        UpdateKeyboardShortcuts();
        UpdateToolInput();
        UpdateHoverHighlight();
        UpdateShapePreview();
        UpdateStatusBar();

        // Recompute cable connections after a short delay when dirty.
        // Batches rapid changes (e.g. placing multiple cables quickly).
        if (_cablesDirty)
        {
            _cableRecomputeTimer += e.DeltaSeconds;
            if (_cableRecomputeTimer > 0.2f) // 200ms debounce
            {
                ComputeCableConnections();
                _cablesDirty = false;
                _cableRecomputeTimer = 0f;
            }
        }
    }

    #region Status Bar

    private void UpdateStatusBar()
    {
        var pos = _eye.Position.Position;
        _screen.SetStatusCoords($"({pos.X:F1}, {pos.Y:F1})");

        var zoom = _eye.Zoom.X;
        _screen.SetStatusZoom($"Zoom: {zoom:F2}x");

        _screen.SetStatusTool(_activeTool.Name);

        // Show entity info when EntitySelectTool has a selection.
        if (_activeTool is EntitySelectTool { SelectedEntity: { } selectedUid } entitySel &&
            _entityManager.EntityExists(selectedUid))
        {
            var meta = _entityManager.GetComponent<MetaDataComponent>(selectedUid);
            var xform = _entityManager.GetComponent<TransformComponent>(selectedUid);
            var entPos = xform.Coordinates.Position;
            var protoId = meta.EntityPrototype?.ID ?? "unknown";
            var cycleInfo = entitySel.CycleCount > 1
                ? $" [{entitySel.CyclePosition}/{entitySel.CycleCount} scroll to cycle]"
                : "";
            _screen.SetStatusInfo($"Entity: {protoId} @ ({entPos.X:F1}, {entPos.Y:F1}){cycleInfo}");

            // Update the entity info panel.
            string? displayName = null;
            try { displayName = meta.EntityName; } catch { /* localization may fail */ }
            var rotDeg = (float)(xform.LocalRotation.Degrees);
            _screen.UpdateEntityInfoPanel(protoId, displayName, entPos, rotDeg);
        }
        else
        {
            _screen.HideEntityInfoPanel();
        }
    }

    private void OnEntityInfoRotateCW()
    {
        if (_activeTool is EntitySelectTool entitySelect)
            entitySelect.RotateSelectedCW(_toolContext);
    }

    private void OnEntityInfoRotateCCW()
    {
        if (_activeTool is EntitySelectTool entitySelect)
            entitySelect.RotateSelectedCCW(_toolContext);
    }

    private void OnEntityInfoDelete()
    {
        if (_activeTool is EntitySelectTool entitySelect)
        {
            entitySelect.DeleteSelected(_toolContext);
            _cablesDirty = true;
        }
    }

    private void OnEntityInfoDeselect()
    {
        if (_activeTool is EntitySelectTool entitySelect)
            entitySelect.Deselect();
    }

    #endregion

    #region View Toggles

    private void OnToggleShowEntities()
    {
        var show = _screen.ShowEntities;
        var query = _entityManager.AllEntityQueryEnumerator<SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var sprite, out var xform))
        {
            if (xform.MapID != _loadedMapId)
                continue;
            // Skip grid and map entities, only toggle placed entities.
            if (_entityManager.HasComponent<MapGridComponent>(uid) || _entityManager.HasComponent<MapComponent>(uid))
                continue;

            sprite.Visible = show;
        }
    }

    private void OnToggleShowSubfloor()
    {
        ApplySubfloorVisibility(_screen.ShowSubfloor);
    }

    /// <summary>
    ///     Controls subfloor entity visibility using the engine's SubFloorHideSystem.ShowAll.
    ///     Wrapped in try-catch because the setter tries to send network events and update
    ///     sandbox UI, which may fail in the editor's standalone environment.
    /// </summary>
    private void ApplySubfloorVisibility(bool showAll)
    {
        try
        {
            var subFloorSystem = _entityManager.System<Content.Client.SubFloor.SubFloorHideSystem>();
            subFloorSystem.ShowAll = showAll;
        }
        catch (Exception ex)
        {
            _sawmill.Warning($"SubFloorHideSystem.ShowAll setter error (expected): {ex.Message}");
        }

        // Manually queue appearance updates for all subfloor entities
        // since the normal network round-trip doesn't exist in the editor.
        var appearanceSystem = _entityManager.System<AppearanceSystem>();
        var query = _entityManager.AllEntityQueryEnumerator<SubFloorHideComponent, AppearanceComponent>();
        while (query.MoveNext(out var uid, out _, out var appearance))
        {
            appearanceSystem.QueueUpdate(uid, appearance);
        }
    }

    #endregion

    // Add Categories according to Metadata component.
    private void BuildProtoIndex()
    {
        foreach (var proto in _prototypeManager.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Components.TryGetComponent<EditorMetadataComponent>(_componentFactory, out var metadata))
            {
                if (string.IsNullOrEmpty(metadata.Category))
                {
                    if (!EditorCategories.ContainsKey(metadata.Category))
                    {
                        EditorCategories.Add(metadata.Category, new EditorCategory(metadata.Category));
                    }

                    var category = EditorCategories[metadata.Category];
                    category.EntityPrototypes.Add(proto);
                }
            }
        }
    }
}
