using System;
using System.Collections.Generic;
using Content.MapEditor.Commands;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.MapEditor.Tools;

/// <summary>
///     Paints the selected tile type onto the active grid.
///     Collects all tile changes during a drag stroke into a single BatchCommand for undo.
/// </summary>
public sealed class PaintTool : IEditorTool
{
    public string Name => "Paint";

    private BatchCommand? _batch;

    private readonly HashSet<Vector2i> _paintedThisStroke = new();
    private readonly HashSet<Vector2i> _erasedThisStroke = new();

    public void OnMouseDown(ToolContext ctx, Vector2i tilePos, EditorInput input)
    {
        _batch = new BatchCommand();
        _paintedThisStroke.Clear();
        _erasedThisStroke.Clear();
        DoInput(ctx, tilePos, input);
    }

    public void OnMouseDrag(ToolContext ctx, Vector2i tilePos, EditorInput input)
    {
        DoInput(ctx, tilePos, input);
    }

    public void OnMouseUp(ToolContext ctx, EditorInput input)
    {
        if (_batch != null && _batch.Count > 0)
        {
            // Tiles were already applied during the stroke for immediate visual feedback.
            // Push without re-executing so undo has the full batch.
            ctx.CommandStack.Push(_batch);
        }

        _batch = null;
        _paintedThisStroke.Clear();
    }

    private void PaintTile(ToolContext ctx, Vector2i pos)
    {
        if (_batch == null)
            return;

        if (!_paintedThisStroke.Add(pos))
            return; // Already painted this position in the current stroke.

        var gridUid = ctx.ActiveGridUid;
        var grid = ctx.EntityManager.GetComponent<MapGridComponent>(gridUid);
        var oldTile = ctx.MapSystem.GetTileRef(gridUid, grid, pos).Tile;

        if (oldTile.TypeId == ctx.SelectedPaintTarget.Tile.TypeId)
            return; // No change needed (same type, keep existing variant).

        var cmd = new SetTileCommand(ctx.MapSystem, gridUid, grid, pos, oldTile, ctx.GetVariantTile());
        cmd.Execute(); // Apply immediately for visual feedback.
        _batch.Add(cmd);
    }

    private void EraseTile(ToolContext ctx, Vector2i pos)
    {
        if (_batch == null)
            return;

        if (!_erasedThisStroke.Add(pos))
            return;

        var gridUid = ctx.ActiveGridUid;
        var grid = ctx.EntityManager.GetComponent<MapGridComponent>(gridUid);
        var oldTile = ctx.MapSystem.GetTileRef(gridUid, grid, pos).Tile;

        if (oldTile.IsEmpty)
            return; // Already empty.

        var cmd = new SetTileCommand(ctx.MapSystem, gridUid, grid, pos, oldTile, Tile.Empty);
        cmd.Execute();
        _batch.Add(cmd);
    }

    private void PaintEntity(ToolContext ctx, Vector2i pos)
    {
        if (_batch == null)
            return;

        if (!_paintedThisStroke.Add(pos))
            return;

        var gridUid = ctx.ActiveGridUid;
        var grid = ctx.EntityManager.GetComponent<MapGridComponent>(gridUid);

        var coords = ctx.MapSystem.GridTileToLocal(
            gridUid,
            grid,
            pos);

        var uid = ctx.EntityManager.SpawnEntity(ctx.SelectedPaintTarget.EntityPrototype, coords);

        var cmd = new SpawnEntityCommand(
            ctx.EntityManager,
            uid);

        cmd.Execute();

        _batch.Add(cmd);
    }

    private void EraseEntity(ToolContext ctx, Vector2i pos)
    {
        if (_batch == null)
            return;
        if (!_erasedThisStroke.Add(pos))
            return;

        var lookup = ctx.EntityManager.System<EntityLookupSystem>();

        var coords = new EntityCoordinates(
            ctx.ActiveGridUid,
            pos.X + 0.5f,
            pos.Y + 0.5f);

        var entities = lookup.GetEntitiesIntersecting(coords);

        foreach (var entity in entities)
        {
            if (entity == ctx.ActiveGridUid)
                continue;

            if (!ctx.EntityManager.HasComponent<MetaDataComponent>(entity))
                continue;

            var cmd = new DeleteEntityCommand(ctx.EntityManager, entity);

            cmd.Execute();
            _batch.Add(cmd);
        }
    }

    private void DoInput(ToolContext ctx, Vector2i tilePos, EditorInput input)
    {
        switch (input.InputButton)
        {
            case EditorInputButton.Primary:
                PaintThing(ctx, tilePos);
                break;
            case EditorInputButton.Secondary:
                EraseThing(ctx, tilePos);
                break;
            default:
                return;
        }
    }

    private void PaintThing(ToolContext ctx, Vector2i tilePos)
    {
        switch (ctx.SelectedPaintTarget.Type)
        {
            case PaintTargetType.Tile:
                PaintTile(ctx, tilePos);
                break;
            case PaintTargetType.Entity:
                PaintEntity(ctx, tilePos);
                break;
        }
    }

    private void EraseThing(ToolContext ctx, Vector2i tilePos)
    {
        switch (ctx.SelectedPaintTarget.Type)
        {
            case PaintTargetType.Tile:
                EraseTile(ctx, tilePos);
                break;
            case PaintTargetType.Entity:
                EraseEntity(ctx, tilePos);
                break;
        }
    }
}
