using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.MapEditor.Tools;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.MapEditor;

// Camera pan/zoom, hover highlight, placement ghost preview, and shape tool preview overlays.
public sealed partial class MapEditorState
{
    private void UpdatePan()
    {
        var mouseDown = _input.IsKeyDown(Keyboard.Key.MouseMiddle);
        var currentPos = _input.MouseScreenPosition;

        if (mouseDown)
        {
            if (_isPanning)
            {
                var dx = currentPos.Position.X - _lastMouseScreen.Position.X;
                var dy = currentPos.Position.Y - _lastMouseScreen.Position.Y;

                if (dx != 0 || dy != 0)
                {
                    var zoom = _eye.Zoom;
                    var ppm = EyeManager.PixelsPerMeter;
                    var worldDx = -dx * zoom.X / ppm;
                    var worldDy = dy * zoom.Y / ppm;

                    var pos = _eye.Position;
                    _eye.Position = new MapCoordinates(
                        new Vector2(pos.Position.X + worldDx, pos.Position.Y + worldDy),
                        pos.MapId);
                }
            }
            else
            {
                _isPanning = true;
            }

            _lastMouseScreen = currentPos;
        }
        else
        {
            _isPanning = false;
        }
    }

    private void OnViewportScroll(float delta)
    {
        // If EntitySelectTool is active and has a selection, scroll cycles through entities
        // at the selected tile instead of zooming (matching ss14editor behavior).
        if (_activeTool is EntitySelectTool entitySelect && entitySelect.SelectedEntity != null)
        {
            var screenPos = _input.MouseScreenPosition;
            if (TryResolveGridTile(screenPos, out var tilePos))
            {
                if (entitySelect.OnScroll(_toolContext, tilePos, delta))
                    return; // Consumed, don't zoom.
            }
        }

        // Zoom toward the cursor position (not the center of the viewport).
        var mouseScreen = _input.MouseScreenPosition;
        var worldBefore = _eyeManager.PixelToMap(mouseScreen.Position);

        var factor = delta > 0 ? 0.8f : 1.25f;
        var zoom = _eye.Zoom;
        var newZoom = Math.Clamp(zoom.X * factor, MinZoom, MaxZoom);
        _eye.Zoom = new Vector2(newZoom, newZoom);

        var worldAfter = _eyeManager.PixelToMap(mouseScreen.Position);

        if (worldBefore.MapId != MapId.Nullspace && worldAfter.MapId != MapId.Nullspace)
        {
            var correction = worldBefore.Position - worldAfter.Position;
            var pos = _eye.Position;
            _eye.Position = new MapCoordinates(pos.Position + correction, pos.MapId);
        }
    }

    private void OnResetZoomPressed()
    {
        _eye.Zoom = Vector2.One;
    }

    private void UpdateHoverHighlight()
    {
        var screenPos = _input.MouseScreenPosition;

        if (_activeGridUid == EntityUid.Invalid || !IsMouseOverViewport(screenPos))
        {
            _editorOverlay.HoveredTile = null;
            return;
        }

        var mapCoords = _eyeManager.PixelToMap(screenPos.Position);
        if (mapCoords.MapId == MapId.Nullspace)
        {
            _editorOverlay.HoveredTile = null;
            return;
        }

        var gridComp = _entityManager.GetComponent<MapGridComponent>(_activeGridUid);
        var tilePos = _toolContext.MapSystem.CoordinatesToTile(_activeGridUid, gridComp, mapCoords);
        _editorOverlay.HoveredTile = tilePos;

        // Set grid world transform so the highlight renders at the correct position.
        var xformSystem = _entityManager.System<SharedTransformSystem>();
        _editorOverlay.GridWorldMatrix = xformSystem.GetWorldMatrix(_activeGridUid);

        // When Shift is held and a placement/select tool is active, show the ghost at the
        // exact cursor position (grid-local) instead of snapping to tile center.
        if (_input.IsKeyDown(Keyboard.Key.Shift)
            && (_activeToolKey == "entityplace" || _activeToolKey == "entityselect"))
        {
            var invMatrix = xformSystem.GetInvWorldMatrix(_activeGridUid);
            var gridLocal = Vector2.Transform(mapCoords.Position, invMatrix);
            _editorOverlay.FreePreviewPosition = gridLocal;
        }
        else
        {
            _editorOverlay.FreePreviewPosition = null;
        }

        // Update placement ghost preview based on active tool.
        UpdatePlacementPreview();
    }

    private void UpdatePlacementPreview()
    {
        Texture? previewTex = null;
        Angle previewRot = Angle.Zero;

        switch (_activeToolKey)
        {
            case "paint":
            case "fill":
            case "rectangle":
            case "line":
            case "circle":
            {
                // Show selected tile texture.
                var tileId = _toolContext.SelectedPaintTarget.Tile.TypeId;
                if (tileId is > 0 && _tileDefs.TryGetDefinition(tileId, out var tileDef) && tileDef.Sprite != null)
                {
                    var resourceCache = IoCManager.Resolve<Robust.Client.ResourceManagement.IResourceCache>();
                    var spritePath = tileDef.Sprite.ToString();
                    if (spritePath != null && resourceCache.TryGetResource<Robust.Client.ResourceManagement.TextureResource>(
                            spritePath, out var texRes))
                        previewTex = texRes.Texture;
                }
                break;
            }
            case "entityplace":
            {
                if (!string.IsNullOrEmpty(_toolContext.SelectedEntityPrototype)
                    && _prototypeManager.TryIndex<EntityPrototype>(_toolContext.SelectedEntityPrototype, out var proto))
                {
                    try
                    {
                        var spriteSystem = _entityManager.System<SpriteSystem>();
                        var dirTexProvider = spriteSystem.GetPrototypeTextures(proto).FirstOrDefault();
                        if (dirTexProvider != null)
                        {
                            if (_toolContext.PlacementRotation != Angle.Zero)
                            {
                                var dir = _toolContext.PlacementRotation.GetCardinalDir();
                                previewTex = dirTexProvider.TextureFor(dir);
                            }
                            else
                            {
                                previewTex = dirTexProvider.Default;
                            }
                        }
                    }
                    catch { }
                }
                break;
            }
            case "cabledraw":
            {
                if (!string.IsNullOrEmpty(_toolContext.SelectedCablePrototype)
                    && _prototypeManager.TryIndex<EntityPrototype>(_toolContext.SelectedCablePrototype, out var proto))
                {
                    try
                    {
                        var spriteSystem = _entityManager.System<SpriteSystem>();
                        previewTex = spriteSystem.GetPrototypeTextures(proto).FirstOrDefault()?.Default;
                    }
                    catch { }
                }
                break;
            }
            case "pipedraw":
            {
                if (!string.IsNullOrEmpty(_toolContext.SelectedPipePrototype)
                    && _prototypeManager.TryIndex<EntityPrototype>(_toolContext.SelectedPipePrototype, out var proto))
                {
                    try
                    {
                        var spriteSystem = _entityManager.System<SpriteSystem>();
                        var dirTexProvider = spriteSystem.GetPrototypeTextures(proto).FirstOrDefault();
                        if (dirTexProvider != null)
                        {
                            if (_toolContext.PlacementRotation != Angle.Zero)
                            {
                                var dir = _toolContext.PlacementRotation.GetCardinalDir();
                                previewTex = dirTexProvider.TextureFor(dir);
                            }
                            else
                            {
                                previewTex = dirTexProvider.Default;
                            }
                        }
                    }
                    catch { }
                }
                break;
            }
        }

        // During EntitySelectTool Shift drag, show the selected entity's sprite as a ghost
        // at the free cursor position (same visual as entity place with Shift).
        if (_activeTool is EntitySelectTool { IsDragging: true, FreeDragPosition: { } freeDragPos, SelectedEntity: { } draggedUid }
            && _entityManager.EntityExists(draggedUid))
        {
            _editorOverlay.FreePreviewPosition = freeDragPos;

            // Use cached texture for the drag to avoid per-frame Icon lookups.
            if (_dragGhostTexture == null)
            {
                try
                {
                    if (_entityManager.TryGetComponent<SpriteComponent>(draggedUid, out var sprite))
                        _dragGhostTexture = sprite.Icon?.Default;
                }
                catch { }
            }

            previewTex ??= _dragGhostTexture;
        }
        else
        {
            _dragGhostTexture = null;
        }

        _editorOverlay.PlacementPreviewTexture = previewTex;
        _editorOverlay.PlacementPreviewRotation = previewRot;
    }

    /// <summary>
    ///     Computes preview tiles for shape tools during a drag and sends them to the overlay.
    ///     Also handles entity selection outlines and selection box rendering.
    /// </summary>
    private void UpdateShapePreview()
    {
        List<Vector2i>? preview = null;
        string dimensionLabel = "";

        if (_isToolActive)
        {
            preview = _activeTool switch
            {
                RectangleTool rect when rect.DragStart != null && rect.DragEnd != null
                    => ComputeRectanglePreview(rect.DragStart.Value, rect.DragEnd.Value),
                LineTool line when line.DragStart != null && line.DragEnd != null
                    => ComputeLinePreview(line.DragStart.Value, line.DragEnd.Value),
                CircleTool circle when circle.DragStart != null && circle.DragEnd != null
                    => ComputeCirclePreview(circle.DragStart.Value, circle.DragEnd.Value),
                _ => null,
            };

            dimensionLabel = _activeTool switch
            {
                RectangleTool rect when rect.DragStart != null && rect.DragEnd != null
                    => ComputeRectDimLabel(rect.DragStart.Value, rect.DragEnd.Value),
                LineTool line when line.DragStart != null && line.DragEnd != null
                    => ComputeLineDimLabel(line.DragStart.Value, line.DragEnd.Value),
                CircleTool circle when circle.DragStart != null && circle.DragEnd != null
                    => ComputeCircleDimLabel(circle.DragStart.Value, circle.DragEnd.Value),
                SelectTool sel when sel.DragStart != null && sel.DragEnd != null
                    => ComputeRectDimLabel(sel.DragStart.Value, sel.DragEnd.Value),
                _ => "",
            };
        }

        _screen.SetStatusDimension(dimensionLabel);
        _editorOverlay.PreviewTiles = preview;

        // Update preview colors to match the active tool's highlight color.
        if (preview != null)
        {
            var fill = _editorOverlay.HighlightColor;
            _editorOverlay.PreviewFillColor = new Color(fill.R, fill.G, fill.B, 0.2f);
            _editorOverlay.PreviewBorderColor = new Color(fill.R, fill.G, fill.B, 0.5f);
        }

        // Update entity selection outline shader (PostShader on the entity's SpriteComponent).
        EntityUid? currentSelection = null;
        if (_activeTool is EntitySelectTool entitySelect)
            currentSelection = entitySelect.SelectedEntity;

        if (currentSelection != _outlinedEntity)
        {
            if (_outlinedEntity != null
                && _entityManager.EntityExists(_outlinedEntity.Value)
                && _entityManager.TryGetComponent<SpriteComponent>(_outlinedEntity.Value, out var oldSprite))
            {
                oldSprite.PostShader = null;
                oldSprite.RenderOrder = 0;
            }

            if (currentSelection != null
                && _entityManager.EntityExists(currentSelection.Value)
                && _entityManager.TryGetComponent<SpriteComponent>(currentSelection.Value, out var newSprite))
            {
                newSprite.PostShader = _selectionOutlineShader;
                newSprite.RenderOrder = unchecked((uint)Environment.TickCount);
            }

            _outlinedEntity = currentSelection;
        }

        // Update the selection box for the SelectTool both during drag and after.
        if (_activeTool is SelectTool selectTool)
        {
            if (selectTool.DragStart != null && selectTool.DragEnd != null)
            {
                var s = selectTool.DragStart.Value;
                var e = selectTool.DragEnd.Value;
                var minX = Math.Min(s.X, e.X);
                var minY = Math.Min(s.Y, e.Y);
                var maxX = Math.Max(s.X, e.X);
                var maxY = Math.Max(s.Y, e.Y);
                _editorOverlay.SelectionBox = new Box2i(minX, minY, maxX + 1, maxY + 1);
                _editorOverlay.IsDraggingSelection = true;
            }
            else if (selectTool.Selection != null)
            {
                _editorOverlay.SelectionBox = selectTool.Selection;
                _editorOverlay.IsDraggingSelection = selectTool.IsMoving;
            }
            else
            {
                _editorOverlay.SelectionBox = null;
                _editorOverlay.IsDraggingSelection = false;
            }

            // Ghost tiles during move.
            _editorOverlay.MoveGhostTiles = selectTool.MoveGhostTiles;
            _editorOverlay.MoveGhostOffset = selectTool.MoveOffset;
        }
        else
        {
            _editorOverlay.SelectionBox = null;
            _editorOverlay.MoveGhostTiles = null;

            // Suppress hover highlight while EntitySelectTool is dragging an entity.
            _editorOverlay.IsDraggingSelection = _activeTool is EntitySelectTool { IsDragging: true };
        }
    }

    private static List<Vector2i> ComputeRectanglePreview(Vector2i start, Vector2i end)
    {
        var minX = Math.Min(start.X, end.X);
        var maxX = Math.Max(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var maxY = Math.Max(start.Y, end.Y);

        var tiles = new List<Vector2i>((maxX - minX + 1) * (maxY - minY + 1));
        for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
                tiles.Add(new Vector2i(x, y));
        return tiles;
    }

    private static List<Vector2i> ComputeLinePreview(Vector2i start, Vector2i end)
    {
        var tiles = new List<Vector2i>();
        foreach (var pos in LineTool.GetLinePoints(start, end))
            tiles.Add(pos);
        return tiles;
    }

    private static List<Vector2i> ComputeCirclePreview(Vector2i center, Vector2i end)
    {
        var dx = end.X - center.X;
        var dy = end.Y - center.Y;
        var radiusSq = dx * dx + dy * dy;
        var radius = (int) Math.Ceiling(Math.Sqrt(radiusSq));

        var tiles = new List<Vector2i>();
        for (var x = center.X - radius; x <= center.X + radius; x++)
            for (var y = center.Y - radius; y <= center.Y + radius; y++)
            {
                var distSq = (x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y);
                if (distSq <= radiusSq)
                    tiles.Add(new Vector2i(x, y));
            }
        return tiles;
    }

    private static string ComputeRectDimLabel(Vector2i start, Vector2i end)
    {
        var w = Math.Abs(end.X - start.X) + 1;
        var h = Math.Abs(end.Y - start.Y) + 1;
        return $"{w}x{h}";
    }

    private static string ComputeLineDimLabel(Vector2i start, Vector2i end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = (int) Math.Ceiling(Math.Sqrt(dx * dx + dy * dy)) + 1;
        return $"L:{length}";
    }

    private static string ComputeCircleDimLabel(Vector2i center, Vector2i end)
    {
        var dx = end.X - center.X;
        var dy = end.Y - center.Y;
        var radius = (int) Math.Ceiling(Math.Sqrt(dx * dx + dy * dy));
        return $"R:{radius}";
    }
}
