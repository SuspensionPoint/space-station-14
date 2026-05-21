using System.Collections.Generic;
using Content.MapEditor.Commands;
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

        if (oldTile.TypeId == ctx.SelectedTile.TypeId)
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

    private void DoInput(ToolContext ctx, Vector2i tilePos, EditorInput input)
    {
        switch (input.InputButton)
        {
            case EditorInputButton.Primary:
                PaintTile(ctx, tilePos);
                break;
            case EditorInputButton.Secondary:
                EraseTile(ctx, tilePos);
                break;
            default:
                return;
        }
    }
}
