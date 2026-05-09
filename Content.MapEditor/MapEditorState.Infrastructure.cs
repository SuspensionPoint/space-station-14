using System;
using System.Collections.Generic;
using Content.Client.Power.Visualizers;
using Content.Shared.Wires;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.MapEditor;

// Infrastructure mode (hide non-infra entities, show subfloor) and cable connection recomputation.
public sealed partial class MapEditorState
{
    /// <summary>
    ///     Activates infrastructure mode: hides all non-infrastructure entities and shows subfloor.
    ///     Saves the previous visibility state so it can be restored on deactivation.
    /// </summary>
    private void ActivateInfrastructureMode()
    {
        if (_infrastructureMode)
            return;

        _infrastructureMode = true;
        _toolContext.InfrastructureMode = true;

        _savedVisibility = new Dictionary<EntityUid, bool>();
        var query = _entityManager.AllEntityQueryEnumerator<SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var sprite, out var xform))
        {
            if (xform.MapID != _loadedMapId)
                continue;

            // Skip grid and map entities, only toggle placed entities.
            if (_entityManager.HasComponent<MapGridComponent>(uid) || _entityManager.HasComponent<MapComponent>(uid))
                continue;

            _savedVisibility[uid] = sprite.Visible;

            // Check if entity has any infrastructure component.
            var isInfra = false;
            foreach (var compType in InfrastructureComponents)
            {
                try
                {
                    if (_entityManager.HasComponent(uid, compType))
                    {
                        isInfra = true;
                        break;
                    }
                }
                catch
                {
                    // Component type not registered on client, skip.
                }
            }

            sprite.Visible = isInfra;
        }

        ApplySubfloorVisibility(true);
        _screen.SetInfrastructurePanelVisible(true);
        _sawmill.Debug("Infrastructure mode activated");
    }

    /// <summary>
    ///     Deactivates infrastructure mode: restores all entity visibility to the saved state
    ///     and reverts subfloor visibility to the View menu toggle state.
    /// </summary>
    private void DeactivateInfrastructureMode()
    {
        if (!_infrastructureMode)
            return;

        _infrastructureMode = false;
        _toolContext.InfrastructureMode = false;

        if (_savedVisibility != null)
        {
            foreach (var (uid, wasVisible) in _savedVisibility)
            {
                if (_entityManager.EntityExists(uid)
                    && _entityManager.TryGetComponent<SpriteComponent>(uid, out var sprite))
                {
                    sprite.Visible = wasVisible;
                }
            }

            _savedVisibility = null;
        }

        ApplySubfloorVisibility(_screen.ShowSubfloor);
        _screen.SetInfrastructurePanelVisible(false);
        _sawmill.Debug("Infrastructure mode deactivated");
    }

    /// <summary>
    ///     Computes cable connection masks client-side after map load.
    ///     Normally done by server-side NodeGroupSystem, but since we're client-only,
    ///     we check cardinal neighbors for matching cable types and set the appearance data.
    /// </summary>
    private void ComputeCableConnections()
    {
        var appearanceSys = _entityManager.System<AppearanceSystem>();

        // Build a lookup: tile position -> list of (EntityUid, StatePrefix) for cables on that tile.
        var cableTiles = new Dictionary<(EntityUid Grid, Vector2i Tile), List<(EntityUid Uid, string Prefix)>>();

        var query = _entityManager.AllEntityQueryEnumerator<CableVisualizerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var cableVis, out var xform))
        {
            if (xform.MapID != _loadedMapId)
                continue;

            var gridUid = xform.GridUid;
            if (gridUid == null || !_entityManager.TryGetComponent<MapGridComponent>(gridUid.Value, out var grid))
                continue;

            var prefix = cableVis.StatePrefix ?? "cable";
            var tile = _entityManager.System<SharedMapSystem>().CoordinatesToTile(gridUid.Value, grid, xform.Coordinates);
            var key = (gridUid.Value, tile);

            if (!cableTiles.TryGetValue(key, out var list))
            {
                list = new List<(EntityUid, string)>();
                cableTiles[key] = list;
            }
            list.Add((uid, prefix));
        }

        // For each cable, check cardinal neighbors for same-prefix cables.
        var directions = new (Vector2i Offset, WireVisDirFlags Flag)[]
        {
            (new Vector2i(0, 1), WireVisDirFlags.North),
            (new Vector2i(0, -1), WireVisDirFlags.South),
            (new Vector2i(1, 0), WireVisDirFlags.East),
            (new Vector2i(-1, 0), WireVisDirFlags.West),
        };

        foreach (var ((gridUid, tile), cables) in cableTiles)
        {
            foreach (var (uid, prefix) in cables)
            {
                var mask = WireVisDirFlags.None;

                foreach (var (offset, flag) in directions)
                {
                    var neighborKey = (gridUid, tile + offset);
                    if (cableTiles.TryGetValue(neighborKey, out var neighbors))
                    {
                        foreach (var (_, neighborPrefix) in neighbors)
                        {
                            if (neighborPrefix == prefix)
                            {
                                mask |= flag;
                                break;
                            }
                        }
                    }
                }

                if (_entityManager.TryGetComponent<AppearanceComponent>(uid, out var appearance))
                {
                    appearanceSys.SetData(uid, WireVisVisuals.ConnectedMask, mask, appearance);
                }
            }
        }

        _sawmill.Info($"Computed cable connections for {cableTiles.Count} tile positions");
    }
}
