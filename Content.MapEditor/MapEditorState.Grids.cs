using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.MapEditor;

// Grid tab management: active grid switching, tab bar population, add/delete grids.
public sealed partial class MapEditorState
{
    private void SetActiveGrid(EntityUid gridUid)
    {
        _activeGridUid = gridUid;
        _toolContext.ActiveGridUid = gridUid;
        _sawmill.Debug($"Active grid set to {gridUid}");
    }

    /// <summary>
    ///     Enumerates all grids on the given map and populates the grid tab bar.
    ///     Sets the active grid to the first one found.
    /// </summary>
    private void PopulateGridTabs(MapId mapId)
    {
        var grids = new List<(EntityUid Uid, string Label)>();
        var query = _entityManager.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
        var index = 0;

        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            grids.Add((uid, $"Grid {index}"));
            index++;
        }

        _screen.PopulateGridTabs(grids);

        if (grids.Count > 0)
        {
            SetActiveGrid(grids[0].Uid);
            _screen.SetActiveGridTab(grids[0].Uid);
        }
        else
        {
            SetActiveGrid(EntityUid.Invalid);
        }
    }

    private void OnGridTabSelected(EntityUid gridUid)
    {
        SetActiveGrid(gridUid);
        _screen.SetActiveGridTab(gridUid);
    }

    private void OnGridTabDeleted(EntityUid gridUid)
    {
        if (!_entityManager.EntityExists(gridUid))
            return;

        // Don't delete the last grid.
        if (_screen.GridTabCount <= 1)
        {
            _screen.SetStatusInfo("Cannot delete the last grid");
            return;
        }

        // If deleting the active grid, switch to another one first.
        if (gridUid == _activeGridUid)
        {
            var query = _entityManager.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.MapID == _loadedMapId && uid != gridUid)
                {
                    SetActiveGrid(uid);
                    _screen.SetActiveGridTab(uid);
                    break;
                }
            }
        }

        _entityManager.DeleteEntity(gridUid);
        _screen.RemoveGridTab(gridUid);
        _screen.SetStatusInfo("Grid deleted");
    }

    private void OnAddGridPressed()
    {
        if (_loadedMapId == default)
            return;

        var newGrid = _mapManager.CreateGridEntity(_loadedMapId);
        var uid = newGrid.Owner;

        var tabCount = _screen.GridTabCount;
        _screen.AddGridTab(uid, $"Grid {tabCount}");

        SetActiveGrid(uid);
        _screen.SetActiveGridTab(uid);

        _sawmill.Info($"Created new grid {uid} on map {_loadedMapId}");
        _screen.SetStatusInfo($"Created new grid");
    }
}
