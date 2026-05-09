using System.Collections.Generic;
using Content.MapEditor.Tools;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.MapEditor;

// Tool dispatch: tool switching, palette callbacks, mouse input handling, viewport helpers.
public sealed partial class MapEditorState
{
    /// <summary>
    ///     Sets the active editor tool (Paint, Erase, Eyedropper, etc.).
    /// </summary>
    public void SetActiveTool(IEditorTool tool, string toolKey)
    {
        // End any in-progress stroke before switching.
        if (_isToolActive)
        {
            _activeTool.OnMouseUp(_toolContext);
            _isToolActive = false;
        }

        _activeTool = tool;
        _activeToolKey = toolKey;
        _screen.SetActiveToolButton(toolKey);

        // Reset placement rotation when switching tools.
        _toolContext.PlacementRotation = Angle.Zero;

        // Activate/deactivate infrastructure mode based on tool type.
        var isInfraTool = toolKey is "cabledraw" or "pipedraw";
        if (isInfraTool && !_infrastructureMode)
            ActivateInfrastructureMode();
        else if (!isInfraTool && _infrastructureMode)
            DeactivateInfrastructureMode();

        UpdateHighlightColorForTool(toolKey);
    }

    private void UpdateHighlightColorForTool(string toolKey)
    {
        switch (toolKey)
        {
            case "erase":
                _editorOverlay.HighlightColor = new Color(1.0f, 0.3f, 0.3f, 0.1f);
                _editorOverlay.BorderColor = new Color(1.0f, 0.3f, 0.3f, 0.5f);
                break;
            case "eyedropper":
                _editorOverlay.HighlightColor = new Color(0.3f, 1.0f, 0.4f, 0.1f);
                _editorOverlay.BorderColor = new Color(0.3f, 1.0f, 0.4f, 0.5f);
                break;
            case "fill":
                _editorOverlay.HighlightColor = new Color(1.0f, 1.0f, 0.2f, 0.1f);
                _editorOverlay.BorderColor = new Color(1.0f, 1.0f, 0.2f, 0.5f);
                break;
            case "rectangle":
                _editorOverlay.HighlightColor = new Color(0.2f, 1.0f, 1.0f, 0.1f);
                _editorOverlay.BorderColor = new Color(0.2f, 1.0f, 1.0f, 0.5f);
                break;
            case "line":
                _editorOverlay.HighlightColor = new Color(1.0f, 0.6f, 0.2f, 0.1f);
                _editorOverlay.BorderColor = new Color(1.0f, 0.6f, 0.2f, 0.5f);
                break;
            case "circle":
                _editorOverlay.HighlightColor = new Color(1.0f, 0.3f, 1.0f, 0.1f);
                _editorOverlay.BorderColor = new Color(1.0f, 0.3f, 1.0f, 0.5f);
                break;
            case "select":
                _editorOverlay.HighlightColor = new Color(1.0f, 1.0f, 1.0f, 0.08f);
                _editorOverlay.BorderColor = new Color(1.0f, 1.0f, 1.0f, 0.6f);
                break;
            case "entityplace":
                _editorOverlay.HighlightColor = new Color(0.4f, 1.0f, 0.6f, 0.1f);
                _editorOverlay.BorderColor = new Color(0.4f, 1.0f, 0.6f, 0.5f);
                break;
            case "entityselect":
                _editorOverlay.HighlightColor = new Color(0.3f, 0.8f, 1.0f, 0.1f);
                _editorOverlay.BorderColor = new Color(0.3f, 0.8f, 1.0f, 0.5f);
                break;
            case "cabledraw":
                _editorOverlay.HighlightColor = new Color(1.0f, 0.7f, 0.1f, 0.1f);
                _editorOverlay.BorderColor = new Color(1.0f, 0.7f, 0.1f, 0.5f);
                break;
            case "pipedraw":
                _editorOverlay.HighlightColor = new Color(0.3f, 0.6f, 1.0f, 0.1f);
                _editorOverlay.BorderColor = new Color(0.3f, 0.6f, 1.0f, 0.5f);
                break;
            default: // paint
                _editorOverlay.HighlightColor = new Color(0.3f, 0.6f, 1.0f, 0.1f);
                _editorOverlay.BorderColor = new Color(0.3f, 0.6f, 1.0f, 0.5f);
                break;
        }
    }

    private void OnToolSelected(string toolKey)
    {
        IEditorTool tool = toolKey switch
        {
            "paint" => new PaintTool(),
            "erase" => new EraseTool(),
            "eyedropper" => new EyedropperTool(),
            "fill" => new FillTool(),
            "rectangle" => new RectangleTool(),
            "line" => new LineTool(),
            "circle" => new CircleTool(),
            "select" => new SelectTool(),
            "entityplace" => new EntityPlaceTool(),
            "entityselect" => new EntitySelectTool(),
            "cabledraw" => new CableDrawTool(),
            "pipedraw" => new PipeDrawTool(),
            _ => new PaintTool(),
        };

        SetActiveTool(tool, toolKey);

        // Default to HV cable when switching to cable draw without a prior selection.
        if (toolKey == "cabledraw" && string.IsNullOrEmpty(_toolContext.SelectedCablePrototype))
        {
            _toolContext.SelectedCablePrototype = "CableHV";
            _screen.SetActiveCableButton("CableHV");
        }

        // Default to GasPipeStraight when switching to pipe draw without a prior selection.
        if (toolKey == "pipedraw" && string.IsNullOrEmpty(_toolContext.SelectedPipePrototype))
        {
            _toolContext.SelectedPipePrototype = "GasPipeStraight";
            _screen.SetActivePipeButton("GasPipeStraight");
        }
    }

    private void OnTileSelected(int tileId)
    {
        _toolContext.SelectedTile = new Tile(tileId);

        // Only auto-switch to paint if the current tool isn't already tile-based.
        var isTileTool = _activeToolKey is "paint" or "erase" or "eyedropper" or "fill"
            or "rectangle" or "line" or "circle";
        if (!isTileTool)
        {
            SetActiveTool(new PaintTool(), "paint");
        }
    }

    private void OnEntityPrototypeSelected(string protoId)
    {
        _toolContext.SelectedEntityPrototype = protoId;

        if (_activeToolKey != "entityplace")
        {
            SetActiveTool(new EntityPlaceTool(), "entityplace");
        }
    }

    private void OnCableTypeSelected(string protoId)
    {
        _toolContext.SelectedCablePrototype = protoId;

        if (_activeToolKey != "cabledraw")
        {
            SetActiveTool(new CableDrawTool(), "cabledraw");
        }
    }

    private void OnPipeTypeSelected(string protoId)
    {
        _toolContext.SelectedPipePrototype = protoId;

        if (_activeToolKey != "pipedraw")
        {
            SetActiveTool(new PipeDrawTool(), "pipedraw");
        }
    }

    /// <summary>
    ///     Polls left mouse button each frame to dispatch tool start/drag/end.
    /// </summary>
    private void UpdateToolInput()
    {
        var leftDown = _input.IsKeyDown(Keyboard.Key.MouseLeft);
        var screenPos = _input.MouseScreenPosition;

        if (leftDown && !_wasLeftDown)
        {
            // Only start a tool stroke if the click is on the viewport, not on UI panels.
            if (!_isPanning && IsMouseOverViewport(screenPos) && TryResolveGridTile(screenPos, out var tilePos))
            {
                var worldCoords = _eyeManager.PixelToMap(screenPos.Position);
                _toolContext.CursorWorldPosition = worldCoords.Position;
                _toolContext.ShiftHeld = _input.IsKeyDown(Keyboard.Key.Shift);

                _isToolActive = true;
                _lastToolTilePos = tilePos;
                _activeTool.OnMouseDown(_toolContext, tilePos);

                // Mark cables dirty if we placed an entity, cable, or pipe.
                if (_activeToolKey is "entityplace" or "cabledraw" or "pipedraw")
                    _cablesDirty = true;

                if (_activeToolKey == "eyedropper")
                    _screen.SelectTileInPalette(_toolContext.SelectedTile.TypeId);
            }
        }
        else if (leftDown && _isToolActive)
        {
            // Update cursor context for free placement during drag.
            var dragWorldCoords = _eyeManager.PixelToMap(screenPos.Position);
            _toolContext.CursorWorldPosition = dragWorldCoords.Position;
            _toolContext.ShiftHeld = _input.IsKeyDown(Keyboard.Key.Shift);

            // Left mouse held drag.
            if (TryResolveGridTile(screenPos, out var tilePos))
            {
                // Always fire drag for free placement tools so position updates every frame.
                if (tilePos != _lastToolTilePos || _toolContext.ShiftHeld)
                {
                    _lastToolTilePos = tilePos;
                    _activeTool.OnMouseDrag(_toolContext, tilePos);
                }
            }
        }
        else if (!leftDown && _isToolActive)
        {
            // Left mouse released, end stroke.
            _isToolActive = false;
            _activeTool.OnMouseUp(_toolContext);
            _cablesDirty = true; // Recompute cable connections after any tool stroke.
        }

        _wasLeftDown = leftDown;
    }

    /// <summary>
    ///     Returns true if the mouse position is within the viewport control bounds.
    /// </summary>
    private bool IsMouseOverViewport(ScreenCoordinates screenPos)
    {
        var viewport = _screen.MainViewport;
        var vpRect = viewport.GlobalPixelRect;
        if (!vpRect.Contains((int) screenPos.Position.X, (int) screenPos.Position.Y))
            return false;

        // Check that no popup/menu is currently open on top of the viewport.
        var controlUnderMouse = _uiManager.MouseGetControl(screenPos);
        if (controlUnderMouse != null)
        {
            var parent = controlUnderMouse;
            while (parent != null)
            {
                if (parent is Robust.Client.UserInterface.Controls.Popup)
                    return false;
                parent = parent.Parent;
            }
        }

        return true;
    }

    /// <summary>
    ///     Converts a screen position to tile coordinates on the active grid.
    ///     Returns false if no active grid is set or the position is in nullspace.
    /// </summary>
    private bool TryResolveGridTile(ScreenCoordinates screenPos, out Vector2i tilePos)
    {
        tilePos = default;

        if (_activeGridUid == EntityUid.Invalid)
            return false;

        var mapCoords = _eyeManager.PixelToMap(screenPos.Position);
        if (mapCoords.MapId == MapId.Nullspace)
            return false;

        var gridComp = _entityManager.GetComponent<MapGridComponent>(_activeGridUid);
        var mapSystem = _toolContext.MapSystem;
        tilePos = mapSystem.CoordinatesToTile(_activeGridUid, gridComp, mapCoords);
        return true;
    }

    /// <summary>
    ///     Shows a popup listing all entities at a tile so the user can pick one.
    /// </summary>
    private void ShowEntityStackPicker(EntitySelectTool tool, List<EntityUid> entities, ScreenCoordinates mousePos)
    {
        var items = new List<(EntityUid Uid, string Label, Robust.Client.Graphics.Texture? Icon)>();

        foreach (var uid in entities)
        {
            if (!_entityManager.EntityExists(uid))
                continue;

            var meta = _entityManager.GetComponent<MetaDataComponent>(uid);
            var protoId = meta.EntityPrototype?.ID ?? "unknown";
            var label = $"{protoId} [uid={uid}]";

            Robust.Client.Graphics.Texture? icon = null;
            if (_entityManager.TryGetComponent<SpriteComponent>(uid, out var sprite))
            {
                try
                {
                    icon = sprite.Icon?.Default;
                }
                catch
                {
                    // Icon access can fail, fall back to no icon.
                }
            }

            items.Add((uid, label, icon));
        }

        if (items.Count == 0)
        {
            tool.CancelPick();
            return;
        }

        _screen.ShowEntityPicker(
            items,
            mousePos.Position,
            selectedUid =>
            {
                tool.ConfirmPick(selectedUid);
            },
            () =>
            {
                tool.CancelPick();
            });
    }

    private void OnUndoPressed()
    {
        _commandStack.Undo();
        _cablesDirty = true;
    }

    private void OnRedoPressed()
    {
        _commandStack.Redo();
        _cablesDirty = true;
    }
}
